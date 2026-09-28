// ported from: internal/checker/flow.go:1597-1932（引用匹配与 flow 缓存键：isMatchingReference、
//   getFlowReferenceKey/writeFlowCacheKey、getAccessedPropertyName 族、isConstantReference、
//   contains/optionalChainContains/getReferenceCandidate/getReferenceRoot/hasMatchingArgument/
//   isOrContainsMatchingReference、replacePrimitivesWithLiterals、isCoercibleUnderDoubleEquals）
//
// 与 Go 的偏离（逐条；其余与 Go 逐行对应）：
// 1) 字段 `_camelCase`；Go 的 `(T, bool)` → `bool TryX(..., out T)`（docs/checker-plan.md §5.4）：
//    `getAccessedPropertyName`/`tryGetElementAccessExpressionName`/`tryGetNameFromEntityNameExpression`/
//    `getDestructuringPropertyName`/`getLiteralPropertyNameText`。包级 `tryGetNameFromType` 与 checker.go:19060 的
//    实例方法同名 → 按 artifacts/checker-method-collisions.md 加 `Worker`（`TryGetNameFromTypeWorker`）。
// 2) Go 的 `slices.Index(slice, x)` → `Array.IndexOf(array, x)`（找不到返回 -1，与 Go 一致）；
//    `strconv.Itoa` → `ToString(CultureInfo.InvariantCulture)`。
// 3) Go 的 `switch` + `fallthrough`（writeFlowCacheKey 的 Identifier → ThisKeyword）→ C# `goto case`。
// 4) Go 的 `moveType` 无；`var nonDottedNameCacheKey` 包级变量 → `internal static readonly` 字段（`_camelCase`）。
// 5) 实例方法作为函数值传给 `MapType`/`FilterType` 等泛型方法时直接传方法组。
namespace TypeScript.Checker;

using System.Globalization;
using TypeScript.Ast;
using TypeScript.Core;
using static TypeScript.Ast.Ast;
using static TypeScript.Core.Core;
using static TypeScript.Evaluator.EvaluatorFunctions;
using static TypeScript.Xxh3.Xxh3;

public sealed partial class Checker
{
    internal bool IsMatchingReference(Node source, Node target)
    {
        switch (target.Kind)
        {
            case Kind.ParenthesizedExpression:
            case Kind.NonNullExpression:
                return IsMatchingReference(source, target.Expression()!);
            case Kind.BinaryExpression:
                return IsAssignmentExpression(target, false) && IsMatchingReference(source, target.AsBinaryExpression()!.Left!) ||
                    IsBinaryExpression(target) && target.AsBinaryExpression()!.OperatorToken!.Kind == Kind.CommaToken &&
                        IsMatchingReference(source, target.AsBinaryExpression()!.Right!);
        }
        switch (source.Kind)
        {
            case Kind.MetaProperty:
                return IsMetaProperty(target) && source.AsMetaProperty()!.KeywordToken == target.AsMetaProperty()!.KeywordToken && source.Name()!.Text() == target.Name()!.Text();
            case Kind.Identifier:
            case Kind.PrivateIdentifier:
                if (IsThisInTypeQuery(source))
                {
                    return target.Kind == Kind.ThisKeyword;
                }
                return IsIdentifier(target) && GetResolvedSymbolWorker(source) == GetResolvedSymbolWorker(target) ||
                    (IsVariableDeclaration(target) || IsBindingElement(target)) && GetExportSymbolOfValueSymbolIfExported(GetResolvedSymbolWorker(source)) == GetSymbolOfDeclaration(target);
            case Kind.ThisKeyword:
                return target.Kind == Kind.ThisKeyword;
            case Kind.SuperKeyword:
                return target.Kind == Kind.SuperKeyword;
            case Kind.NonNullExpression:
            case Kind.ParenthesizedExpression:
            case Kind.SatisfiesExpression:
                return IsMatchingReference(source.Expression()!, target);
            case Kind.PropertyAccessExpression:
            case Kind.ElementAccessExpression:
                if (TryGetAccessedPropertyName(source, out var sourcePropertyName))
                {
                    if (IsAccessExpression(target))
                    {
                        if (TryGetAccessedPropertyName(target, out var targetPropertyName))
                        {
                            return targetPropertyName == sourcePropertyName && IsMatchingReference(source.Expression()!, target.Expression()!);
                        }
                    }
                }
                if (IsElementAccessExpression(source) && IsElementAccessExpression(target))
                {
                    var sourceArg = source.AsElementAccessExpression()!.ArgumentExpression!;
                    var targetArg = target.AsElementAccessExpression()!.ArgumentExpression!;
                    if (IsIdentifier(sourceArg) && IsIdentifier(targetArg))
                    {
                        var symbol = GetResolvedSymbolWorker(sourceArg);
                        if (symbol == GetResolvedSymbolWorker(targetArg) && (IsConstantVariable(symbol) || IsParameterOrMutableLocalVariable(symbol) && !IsSymbolAssigned(symbol)))
                        {
                            return IsMatchingReference(source.Expression()!, target.Expression()!);
                        }
                    }
                }
                break;
            case Kind.QualifiedName:
                if (IsAccessExpression(target))
                {
                    if (TryGetAccessedPropertyName(target, out var targetPropertyName))
                    {
                        return source.AsQualifiedName()!.Right!.Text() == targetPropertyName && IsMatchingReference(source.AsQualifiedName()!.Left!, target.Expression()!);
                    }
                }
                break;
            case Kind.BinaryExpression:
                return IsBinaryExpression(source) && source.AsBinaryExpression()!.OperatorToken!.Kind == Kind.CommaToken && IsMatchingReference(source.AsBinaryExpression()!.Right!, target);
        }
        return false;
    }

    internal static readonly CacheHashKey _nonDottedNameCacheKey = new(HashString128("?"));

    // Return the flow cache key for a "dotted name" (i.e. a sequence of identifiers
    // separated by dots). The key consists of the id of the symbol referenced by the
    // leftmost identifier followed by zero or more property names separated by dots.
    // The result is nonDottedNameCacheKey if the reference isn't a dotted name.
    internal CacheHashKey GetFlowReferenceKey(FlowState f)
    {
        var b = new KeyBuilder();
        if (WriteFlowCacheKey(ref b, f._reference!, f._declaredType!, f._initialType!, f._flowContainer))
        {
            return b.Hash();
        }
        return _nonDottedNameCacheKey; // Reference isn't a dotted name
    }

    internal bool WriteFlowCacheKey(ref KeyBuilder b, Node node, Type declaredType, Type initialType, Node? flowContainer)
    {
        switch (node.Kind)
        {
            case Kind.Identifier:
                if (!IsThisInTypeQuery(node))
                {
                    var symbol = GetResolvedSymbolWorker(node);
                    if (symbol == _unknownSymbol)
                    {
                        return false;
                    }
                    b.WriteSymbol(symbol);
                }
                goto case Kind.ThisKeyword;
            case Kind.ThisKeyword:
                b.WriteByte((byte)':');
                b.WriteType(declaredType);
                if (initialType != declaredType)
                {
                    b.WriteByte((byte)'=');
                    b.WriteType(initialType);
                }
                if (flowContainer != null)
                {
                    b.WriteByte((byte)'@');
                    b.WriteNode(flowContainer);
                }
                return true;
            case Kind.NonNullExpression:
            case Kind.ParenthesizedExpression:
                return WriteFlowCacheKey(ref b, node.Expression()!, declaredType, initialType, flowContainer);
            case Kind.QualifiedName:
                if (!WriteFlowCacheKey(ref b, node.AsQualifiedName()!.Left!, declaredType, initialType, flowContainer))
                {
                    return false;
                }
                b.WriteByte((byte)'.');
                b.WriteString(node.AsQualifiedName()!.Right!.Text());
                return true;
            case Kind.PropertyAccessExpression:
            case Kind.ElementAccessExpression:
                if (TryGetAccessedPropertyName(node, out var propName))
                {
                    if (!WriteFlowCacheKey(ref b, node.Expression()!, declaredType, initialType, flowContainer))
                    {
                        return false;
                    }
                    b.WriteByte((byte)'.');
                    b.WriteString(propName);
                    return true;
                }
                if (IsElementAccessExpression(node) && IsIdentifier(node.AsElementAccessExpression()!.ArgumentExpression!))
                {
                    var symbol = GetResolvedSymbolWorker(node.AsElementAccessExpression()!.ArgumentExpression!);
                    if (IsConstantVariable(symbol) || IsParameterOrMutableLocalVariable(symbol) && !IsSymbolAssigned(symbol))
                    {
                        if (!WriteFlowCacheKey(ref b, node.Expression()!, declaredType, initialType, flowContainer))
                        {
                            return false;
                        }
                        b.WriteString(".@");
                        b.WriteSymbol(symbol);
                        return true;
                    }
                }
                break;
            case Kind.ObjectBindingPattern:
            case Kind.ArrayBindingPattern:
            case Kind.FunctionDeclaration:
            case Kind.FunctionExpression:
            case Kind.ArrowFunction:
            case Kind.MethodDeclaration:
                b.WriteNode(node);
                b.WriteByte((byte)'#');
                b.WriteType(declaredType);
                return true;
        }
        return false;
    }

    internal bool TryGetAccessedPropertyName(Node access, out string name)
    {
        if (IsPropertyAccessExpression(access))
        {
            name = access.Name()!.Text();
            return true;
        }
        if (IsElementAccessExpression(access))
        {
            return TryGetElementAccessExpressionName(access.AsElementAccessExpression()!, out name);
        }
        if (IsBindingElement(access))
        {
            return TryGetDestructuringPropertyName(access, out name);
        }
        if (IsParameterDeclaration(access))
        {
            name = Array.IndexOf(access.Parent!.Parameters()!, access).ToString(CultureInfo.InvariantCulture);
            return true;
        }
        name = "";
        return false;
    }

    internal bool TryGetElementAccessExpressionName(ElementAccessExpression node, out string name)
    {
        if (IsStringOrNumericLiteralLike(node.ArgumentExpression))
        {
            name = node.ArgumentExpression!.Text();
            return true;
        }
        if (IsEntityNameExpression(node.ArgumentExpression))
        {
            return TryGetNameFromEntityNameExpression(node.ArgumentExpression!, out name);
        }
        name = "";
        return false;
    }

    internal bool TryGetNameFromEntityNameExpression(Node node, out string name)
    {
        var symbol = ResolveEntityName(node, SymbolFlags.Value, true /*ignoreErrors*/, false, null);
        if (symbol == null || !(IsConstantVariable(symbol) || (symbol.Flags & SymbolFlags.EnumMember) != 0))
        {
            name = "";
            return false;
        }
        var declaration = symbol.ValueDeclaration;
        if (declaration == null)
        {
            name = "";
            return false;
        }
        var t = TryGetTypeFromTypeNode(declaration);
        if (t != null)
        {
            if (TryGetNameFromTypeWorker(t, out name))
            {
                return true;
            }
        }
        // We exclude binding elements because their initializers don't solely determine their types and resolving
        // full types can cause circularities (see https://github.com/microsoft/TypeScript/issues/63192).
        if (HasOnlyExpressionInitializer(declaration) && !IsBindingElement(declaration) && IsBlockScopedNameDeclaredBeforeUse(declaration, node))
        {
            var initializer = declaration.Initializer();
            if (initializer != null)
            {
                var initializerType = GetTypeOfExpression(initializer);
                if (initializerType != null)
                {
                    return TryGetNameFromTypeWorker(initializerType, out name);
                }
            }
            else if (IsEnumMember(declaration))
            {
                return TryGetTextOfPropertyName(declaration.Name()!, out name);
            }
        }
        name = "";
        return false;
    }

    internal static bool TryGetNameFromTypeWorker(Type t, out string name)
    {
        if ((t._flags & TypeFlags.UniqueESSymbol) != 0)
        {
            name = t.AsUniqueESSymbolType()!._name;
            return true;
        }
        if ((t._flags & TypeFlags.StringOrNumberLiteral) != 0)
        {
            name = AnyToString(t.AsLiteralType()!._value);
            return true;
        }
        name = "";
        return false;
    }

    internal bool TryGetDestructuringPropertyName(Node node, out string name)
    {
        var parent = node.Parent!;
        if (IsBindingElement(node) && IsObjectBindingPattern(parent))
        {
            return TryGetLiteralPropertyNameText(GetBindingElementPropertyName(node)!, out name);
        }
        if (IsPropertyAssignment(node) || IsShorthandPropertyAssignment(node))
        {
            return TryGetLiteralPropertyNameText(node.Name()!, out name);
        }
        if (IsArrayLiteralExpression(parent) || IsArrayBindingPattern(parent))
        {
            name = Array.IndexOf(parent.Elements() ?? [], node).ToString(CultureInfo.InvariantCulture);
            return true;
        }
        name = "";
        return false;
    }

    internal bool TryGetLiteralPropertyNameText(Node name, out string text)
    {
        var t = GetLiteralTypeFromPropertyName(name);
        if ((t._flags & (TypeFlags.StringLiteral | TypeFlags.NumberLiteral)) != 0)
        {
            text = AnyToString(t.AsLiteralType()!._value);
            return true;
        }
        text = "";
        return false;
    }

    internal bool IsConstantReference(Node node)
    {
        switch (node.Kind)
        {
            case Kind.ThisKeyword:
                return true;
            case Kind.Identifier:
                if (!IsThisInTypeQuery(node))
                {
                    var symbol = GetResolvedSymbolWorker(node);
                    return IsConstantVariable(symbol) || IsParameterOrMutableLocalVariable(symbol) && !IsSymbolAssigned(symbol) || symbol.ValueDeclaration != null && IsFunctionExpression(symbol.ValueDeclaration!);
                }
                break;
            case Kind.PropertyAccessExpression:
            case Kind.ElementAccessExpression:
                // The resolvedSymbol property is initialized by checkPropertyAccess or checkElementAccess before we get here.
                if (IsConstantReference(node.Expression()!))
                {
                    var symbol = GetResolvedSymbolOrNil(node);
                    if (symbol != null)
                    {
                        return IsReadonlySymbolWorker(symbol);
                    }
                }
                break;
            case Kind.ObjectBindingPattern:
            case Kind.ArrayBindingPattern:
                var rootDeclaration = GetRootDeclaration(node.Parent!)!;
                if (IsParameterDeclaration(rootDeclaration) || IsVariableDeclaration(rootDeclaration) && IsCatchClause(rootDeclaration.Parent!))
                {
                    return !IsSomeSymbolAssigned(rootDeclaration);
                }
                return IsVariableDeclaration(rootDeclaration) && IsVarConstLike(rootDeclaration);
        }
        return false;
    }

    internal bool ContainsMatchingReference(Node source, Node target)
    {
        while (IsAccessExpression(source))
        {
            source = source.Expression()!;
            if (IsMatchingReference(source, target))
            {
                return true;
            }
        }
        return false;
    }

    internal bool OptionalChainContainsReference(Node source, Node target)
    {
        while (IsOptionalChain(source))
        {
            source = source.Expression()!;
            if (IsMatchingReference(source, target))
            {
                return true;
            }
        }
        return false;
    }

    internal Node GetReferenceCandidate(Node node)
    {
        switch (node.Kind)
        {
            case Kind.ParenthesizedExpression:
                return GetReferenceCandidate(node.Expression()!);
            case Kind.BinaryExpression:
                switch (node.AsBinaryExpression()!.OperatorToken!.Kind)
                {
                    case Kind.EqualsToken:
                    case Kind.BarBarEqualsToken:
                    case Kind.AmpersandAmpersandEqualsToken:
                    case Kind.QuestionQuestionEqualsToken:
                        return GetReferenceCandidate(node.AsBinaryExpression()!.Left!);
                    case Kind.CommaToken:
                        return GetReferenceCandidate(node.AsBinaryExpression()!.Right!);
                }
                break;
        }
        return node;
    }

    internal Node GetReferenceRoot(Node node)
    {
        var parent = node.Parent!;
        if (IsParenthesizedExpression(parent) ||
            IsBinaryExpression(parent) && parent.AsBinaryExpression()!.OperatorToken!.Kind == Kind.EqualsToken && parent.AsBinaryExpression()!.Left == node ||
            IsBinaryExpression(parent) && parent.AsBinaryExpression()!.OperatorToken!.Kind == Kind.CommaToken && parent.AsBinaryExpression()!.Right == node)
        {
            return GetReferenceRoot(parent);
        }
        return node;
    }

    internal bool HasMatchingArgument(Node expression, Node reference)
    {
        foreach (var argument in expression.Arguments() ?? Array.Empty<Node>())
        {
            if (IsOrContainsMatchingReference(reference, argument) || OptionalChainContainsReference(argument, reference))
            {
                return true;
            }
        }
        if (IsPropertyAccessExpression(expression.Expression()!) && IsOrContainsMatchingReference(reference, expression.Expression()!.Expression()!))
        {
            return true;
        }
        return false;
    }

    internal bool IsOrContainsMatchingReference(Node source, Node target)
    {
        return IsMatchingReference(source, target) || ContainsMatchingReference(source, target);
    }

    // Return a new type in which occurrences of the string, number and bigint primitives and placeholder template
    // literal types in typeWithPrimitives have been replaced with occurrences of compatible and more specific types
    // from typeWithLiterals. This is essentially a limited form of intersection between the two types. We avoid a
    // true intersection because it is more costly and, when applied to union types, generates a large number of
    // types we don't actually care about.
    internal Type ReplacePrimitivesWithLiterals(Type typeWithPrimitives, Type typeWithLiterals)
    {
        if (MaybeTypeOfKind(typeWithPrimitives, TypeFlags.String | TypeFlags.TemplateLiteral | TypeFlags.Number | TypeFlags.BigInt) &&
            MaybeTypeOfKind(typeWithLiterals, TypeFlags.StringLiteral | TypeFlags.TemplateLiteral | TypeFlags.StringMapping | TypeFlags.NumberLiteral | TypeFlags.BigIntLiteral))
        {
            return MapType(typeWithPrimitives, t =>
            {
                if ((t._flags & TypeFlags.String) != 0)
                {
                    return ExtractTypesOfKind(typeWithLiterals, TypeFlags.String | TypeFlags.StringLiteral | TypeFlags.TemplateLiteral | TypeFlags.StringMapping);
                }
                if (IsPatternLiteralType(t) && !MaybeTypeOfKind(typeWithLiterals, TypeFlags.String | TypeFlags.TemplateLiteral | TypeFlags.StringMapping))
                {
                    return ExtractTypesOfKind(typeWithLiterals, TypeFlags.StringLiteral);
                }
                if ((t._flags & TypeFlags.Number) != 0)
                {
                    return ExtractTypesOfKind(typeWithLiterals, TypeFlags.Number | TypeFlags.NumberLiteral);
                }
                if ((t._flags & TypeFlags.BigInt) != 0)
                {
                    return ExtractTypesOfKind(typeWithLiterals, TypeFlags.BigInt | TypeFlags.BigIntLiteral);
                }
                return t;
            });
        }
        return typeWithPrimitives;
    }

    internal static bool IsCoercibleUnderDoubleEquals(Type source, Type target)
    {
        return (source._flags & (TypeFlags.Number | TypeFlags.String | TypeFlags.BooleanLiteral)) != 0 &&
            (target._flags & (TypeFlags.Number | TypeFlags.String | TypeFlags.Boolean)) != 0;
    }
}
