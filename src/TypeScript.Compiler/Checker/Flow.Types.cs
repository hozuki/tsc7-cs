// ported from: internal/checker/flow.go:2122-2433（dotted name / 显式类型解析、this 类型、初始类型
//   （变量声明/绑定元素/初始化器）、赋值类型（二元/数组字面量/展开/属性/简写）、去结构化类型、
//   getTypeWithDefault、getAssignmentReducedType(Worker)、typeMaybeAssignableTo）
//
// 与 Go 的偏离（逐条；其余与 Go 逐行对应）：
// 1) 字段 `_camelCase`；Go 的 `panic` → `Debug.Fail` + 尾随 `return _errorType;`（`Debug.Fail` 抛
//    `DebugFailureException` 但不被编译器视为"不返回"，需补一个不可达 return）。
// 2) Go 的 `defer c.resolvingExplicitTypeOfSymbol.Delete(symbol)` → C# `try/finally`。
// 3) Go 的 map 读缺失键 → `TryGetValue`；`switch { case cond: }` → `if`/`else if` 链。
// 4) 命中 artifacts/checker-method-collisions.md 的未导出方法用 `Worker` 后缀
//    （`GetTypeOfSymbolWorker`/`GetDeclaredTypeOfSymbolWorker`/`SymbolToStringWorker`/`GetTypeOfPropertyOfTypeWorker` 等）。
namespace TypeScript.Checker;

using TypeScript.Ast;
using TypeScript.Core;
using TypeScript.Diagnostics;
using static TypeScript.Ast.Ast;
using static TypeScript.Core.Core;

public sealed partial class Checker
{
    // We require the dotted function name in an assertion expression to be comprised of identifiers
    // that reference function, method, class or value module symbols; or variable, property or
    // parameter symbols with declarations that have explicit type annotations. Such references are
    // resolvable with no possibility of triggering circularities in control flow analysis.
    internal Type? GetTypeOfDottedName(Node node, Diagnostic? diagnostic)
    {
        if ((node.Flags & NodeFlags.InWithStatement) == 0)
        {
            switch (node.Kind)
            {
                case Kind.Identifier:
                    var symbol = GetExportSymbolOfValueSymbolIfExported(GetResolvedSymbolWorker(node))!;
                    return GetExplicitTypeOfSymbol(symbol, diagnostic);
                case Kind.ThisKeyword:
                    return GetExplicitThisType(node);
                case Kind.SuperKeyword:
                    return CheckSuperExpression(node);
                case Kind.PropertyAccessExpression:
                    var t = GetTypeOfDottedName(node.Expression()!, diagnostic);
                    if (t != null)
                    {
                        var name = node.Name()!;
                        Symbol? prop;
                        if (IsPrivateIdentifier(name))
                        {
                            prop = t._symbol != null ? GetPropertyOfTypeWorker(t, TypeScript.Binder.Binder.GetSymbolNameForPrivateIdentifier(t._symbol, name.Text())) : null;
                        }
                        else
                        {
                            prop = GetPropertyOfTypeWorker(t, name.Text());
                        }
                        if (prop != null)
                        {
                            return GetExplicitTypeOfSymbol(prop, diagnostic);
                        }
                    }
                    break;
                case Kind.ParenthesizedExpression:
                    return GetTypeOfDottedName(node.Expression()!, diagnostic);
            }
        }
        return null;
    }

    internal Type? GetExplicitTypeOfSymbol(Symbol symbol, Diagnostic? diagnostic)
    {
        symbol = ResolveSymbol(symbol)!;
        if (!_resolvingExplicitTypeOfSymbol.AddIfAbsent(symbol))
        {
            return null;
        }
        try
        {
            if ((symbol.Flags & (SymbolFlags.Function | SymbolFlags.Method | SymbolFlags.Class | SymbolFlags.ValueModule)) != 0)
            {
                return GetTypeOfSymbolWorker(symbol);
            }
            if ((symbol.Flags & (SymbolFlags.Variable | SymbolFlags.Property)) != 0)
            {
                if ((symbol.CheckFlags & CheckFlags.Mapped) != 0)
                {
                    var origin = _mappedSymbolLinks.Get(symbol)._syntheticOrigin;
                    if (origin != null && GetExplicitTypeOfSymbol(origin, diagnostic) != null)
                    {
                        return GetTypeOfSymbolWorker(symbol);
                    }
                }
                var declaration = symbol.ValueDeclaration;
                if (declaration != null)
                {
                    if (IsDeclarationWithExplicitTypeAnnotation(declaration))
                    {
                        return GetTypeOfSymbolWorker(symbol);
                    }
                    if (IsVariableDeclaration(declaration) && IsForOfStatement(declaration.Parent!.Parent!))
                    {
                        var statement = declaration.Parent!.Parent!;
                        var expressionType = GetTypeOfDottedName(statement.Expression()!, null /*diagnostic*/);
                        if (expressionType != null)
                        {
                            var use = statement.AsForInOrOfStatement()!.AwaitModifier != null ? IterationUse.ForAwaitOf : IterationUse.ForOf;
                            return CheckIteratedTypeOrElementType(use, expressionType, _undefinedType, null /*errorNode*/);
                        }
                    }
                    if (diagnostic != null)
                    {
                        diagnostic.AddRelatedInfo(CreateDiagnosticForNode(declaration, Diagnostics.X_0_needs_an_explicit_type_annotation, SymbolToStringWorker(symbol)));
                    }
                }
            }
            return null;
        }
        finally
        {
            _resolvingExplicitTypeOfSymbol.Delete(symbol);
        }
    }

    internal bool IsDeclarationWithExplicitTypeAnnotation(Node node)
    {
        return (IsVariableDeclaration(node) || IsPropertyDeclaration(node) || IsPropertySignatureDeclaration(node) || IsParameterDeclaration(node)) && node.Type() != null ||
            IsExpandoPropertyFunctionWithReturnTypeAnnotation(node);
    }

    internal bool IsExpandoPropertyFunctionWithReturnTypeAnnotation(Node node)
    {
        if (IsBinaryExpression(node))
        {
            var expr = node.AsBinaryExpression()!.Right;
            if (IsFunctionLike(expr) && expr!.Type() != null)
            {
                return true;
            }
        }
        return false;
    }

    internal bool HasTypePredicateOrNeverReturnType(Signature sig)
    {
        return GetTypePredicateOfSignatureWorker(sig) != null || sig._declaration != null && (OrElse(GetReturnTypeFromAnnotation(sig._declaration), _unknownType)!._flags & TypeFlags.Never) != 0;
    }

    internal Type? GetExplicitThisType(Node node)
    {
        var container = GetThisContainer(node, false /*includeArrowFunctions*/, false /*includeClassComputedPropertyName*/);
        if (IsFunctionLike(container))
        {
            var signature = GetSignatureFromDeclarationWorker(container!);
            if (signature._thisParameter != null)
            {
                return GetExplicitTypeOfSymbol(signature._thisParameter, null);
            }
        }
        if (container!.Parent != null && IsClassLike(container.Parent))
        {
            var symbol = GetSymbolOfDeclaration(container.Parent!)!;
            if (IsStatic(container))
            {
                return GetTypeOfSymbolWorker(symbol);
            }
            return GetDeclaredTypeOfSymbolWorker(symbol).AsInterfaceType()!._thisType;
        }
        return null;
    }

    internal Type GetInitialType(Node node)
    {
        switch (node.Kind)
        {
            case Kind.VariableDeclaration:
                return GetInitialTypeOfVariableDeclaration(node);
            case Kind.BindingElement:
                return GetInitialTypeOfBindingElement(node);
        }
        Debug.Fail("Unhandled case in getInitialType");
        return _errorType;
    }

    internal Type GetInitialTypeOfVariableDeclaration(Node node)
    {
        if (node.Initializer() != null)
        {
            return GetTypeOfInitializer(node.Initializer()!);
        }
        if (IsForInStatement(node.Parent!.Parent!))
        {
            return _stringType;
        }
        if (IsForOfStatement(node.Parent!.Parent!))
        {
            var t = CheckRightHandSideOfForOf(node.Parent!.Parent!);
            if (t != null)
            {
                return t;
            }
        }
        return _errorType;
    }

    internal Type GetTypeOfInitializer(Node node)
    {
        // Return the cached type if one is available. If the type of the variable was inferred
        // from its initializer, we'll already have cached the type. Otherwise we compute it now
        // without caching such that transient types are reflected.
        if (_typeNodeLinks.Has(node))
        {
            var t = _typeNodeLinks.Get(node)._resolvedType;
            if (t != null)
            {
                return t;
            }
        }
        return GetTypeOfExpression(node);
    }

    internal Type GetInitialTypeOfBindingElement(Node node)
    {
        var pattern = node.Parent!;
        var parentType = GetInitialType(pattern.Parent!);
        Type t;
        if (IsObjectBindingPattern(pattern))
        {
            t = GetTypeOfDestructuredProperty(parentType, GetBindingElementPropertyName(node)!);
        }
        else if (!HasDotDotDotToken(node))
        {
            t = GetTypeOfDestructuredArrayElement(parentType, Array.IndexOf(pattern.Elements() ?? [], node));
        }
        else
        {
            t = GetTypeOfDestructuredSpreadExpression(parentType);
        }
        return GetTypeWithDefault(t, node.Initializer());
    }

    internal Type GetAssignedType(Node node)
    {
        var parent = node.Parent!;
        switch (parent.Kind)
        {
            case Kind.ForInStatement:
                return _stringType;
            case Kind.ForOfStatement:
                var t = CheckRightHandSideOfForOf(parent);
                if (t != null)
                {
                    return t;
                }
                break;
            case Kind.BinaryExpression:
                return GetAssignedTypeOfBinaryExpression(parent);
            case Kind.DeleteExpression:
                return _undefinedType;
            case Kind.ArrayLiteralExpression:
                return GetAssignedTypeOfArrayLiteralElement(parent, node);
            case Kind.SpreadElement:
                return GetAssignedTypeOfSpreadExpression(parent);
            case Kind.PropertyAssignment:
                return GetAssignedTypeOfPropertyAssignment(parent);
            case Kind.ShorthandPropertyAssignment:
                return GetAssignedTypeOfShorthandPropertyAssignment(parent);
        }
        return _errorType;
    }

    internal Type GetAssignedTypeOfBinaryExpression(Node node)
    {
        var isDestructuringDefaultAssignment = IsArrayLiteralExpression(node.Parent!) && IsDestructuringAssignmentTarget(node.Parent!) ||
            IsPropertyAssignment(node.Parent!) && IsDestructuringAssignmentTarget(node.Parent!.Parent!);
        if (isDestructuringDefaultAssignment)
        {
            return GetTypeWithDefault(GetAssignedType(node), node.AsBinaryExpression()!.Right!);
        }
        return GetTypeOfExpression(node.AsBinaryExpression()!.Right!);
    }

    internal Type GetAssignedTypeOfArrayLiteralElement(Node node, Node element)
    {
        return GetTypeOfDestructuredArrayElement(GetAssignedType(node), Array.IndexOf(node.Elements() ?? [], element));
    }

    internal Type GetTypeOfDestructuredArrayElement(Type t, int index)
    {
        if (EveryType(t, IsTupleLikeType))
        {
            var elementType = GetTupleElementType(t, index);
            if (elementType != null)
            {
                return elementType;
            }
        }
        var iteratedElementType = CheckIteratedTypeOrElementType(IterationUse.Destructuring, t, _undefinedType, null /*errorNode*/);
        if (iteratedElementType != null)
        {
            return IncludeUndefinedInIndexSignature(iteratedElementType)!;
        }
        return _errorType;
    }

    internal Type? IncludeUndefinedInIndexSignature(Type? t)
    {
        if (t == null)
        {
            return null;
        }
        if (_compilerOptions.NoUncheckedIndexedAccess == Tristate.TSTrue)
        {
            return GetUnionTypeWorker(new[] { t, _missingType });
        }
        return t;
    }

    internal Type GetAssignedTypeOfSpreadExpression(Node node)
    {
        return GetTypeOfDestructuredSpreadExpression(GetAssignedType(node.Parent!));
    }

    internal Type GetTypeOfDestructuredSpreadExpression(Type t)
    {
        var elementType = CheckIteratedTypeOrElementType(IterationUse.Destructuring, t, _undefinedType, null /*errorNode*/);
        if (elementType == null)
        {
            elementType = _errorType;
        }
        return CreateArrayType(elementType);
    }

    internal Type GetAssignedTypeOfPropertyAssignment(Node node)
    {
        return GetTypeOfDestructuredProperty(GetAssignedType(node.Parent!), node.Name()!);
    }

    internal Type GetTypeOfDestructuredProperty(Type t, Node name)
    {
        var nameType = GetLiteralTypeFromPropertyName(name);
        if (!IsTypeUsableAsPropertyName(nameType))
        {
            return _errorType;
        }
        var text = GetPropertyNameFromType(nameType);
        var propType = GetTypeOfPropertyOfTypeWorker(t, text);
        if (propType != null)
        {
            return propType;
        }
        var indexInfo = GetApplicableIndexInfoForName(t, text);
        if (indexInfo != null)
        {
            return IncludeUndefinedInIndexSignature(indexInfo._valueType)!;
        }
        return _errorType;
    }

    internal Type GetAssignedTypeOfShorthandPropertyAssignment(Node node)
    {
        return GetTypeWithDefault(GetAssignedTypeOfPropertyAssignment(node), node.AsShorthandPropertyAssignment()!.ObjectAssignmentInitializer);
    }

    internal bool IsDestructuringAssignmentTarget(Node parent)
    {
        return IsBinaryExpression(parent.Parent!) && parent.Parent!.AsBinaryExpression()!.Left == parent ||
            IsForOfStatement(parent.Parent!) && parent.Parent!.Initializer() == parent;
    }

    internal Type GetTypeWithDefault(Type t, Node? defaultExpression)
    {
        if (defaultExpression != null)
        {
            return GetUnionTypeWorker(new[] { GetNonUndefinedType(t), GetTypeOfExpression(defaultExpression) });
        }
        return t;
    }

    // Remove those constituent types of declaredType to which no constituent type of assignedType is assignable.
    // For example, when a variable of type number | string | boolean is assigned a value of type number | boolean,
    // we remove type string.
    internal Type GetAssignmentReducedType(Type declaredType, Type assignedType)
    {
        if (declaredType == assignedType)
        {
            return declaredType;
        }
        if ((assignedType._flags & TypeFlags.Never) != 0)
        {
            return assignedType;
        }
        var key = new AssignmentReducedKey(declaredType._id, assignedType._id);
        if (!_assignmentReducedTypes.TryGetValue(key, out var result))
        {
            result = GetAssignmentReducedTypeWorker(declaredType, assignedType);
            _assignmentReducedTypes[key] = result;
        }
        return result;
    }

    internal Type GetAssignmentReducedTypeWorker(Type declaredType, Type assignedType)
    {
        var filteredType = FilterType(declaredType, t => TypeMaybeAssignableTo(assignedType, t));
        // Ensure that we narrow to fresh types if the assignment is a fresh boolean literal type.
        var reducedType = filteredType;
        if ((assignedType._flags & TypeFlags.BooleanLiteral) != 0 && IsFreshLiteralType(assignedType))
        {
            reducedType = MapType(filteredType, GetFreshTypeOfLiteralType);
        }
        // Our crude heuristic produces an invalid result in some cases: see GH#26130.
        // For now, when that happens, we give up and don't narrow at all.  (This also
        // means we'll never narrow for erroneous assignments where the assigned type
        // is not assignable to the declared type.)
        if (IsTypeAssignableToWorker(assignedType, reducedType))
        {
            return reducedType;
        }
        return declaredType;
    }

    internal bool TypeMaybeAssignableTo(Type source, Type target)
    {
        if ((source._flags & TypeFlags.Union) == 0)
        {
            return IsTypeAssignableToWorker(source, target);
        }
        // Quick exit when source union contains the target type
        if (ContainsType(source.Types(), target))
        {
            return true;
        }
        // Otherwise, check if any constituent type of the source union is assignable to the target type
        foreach (var t in source.Types())
        {
            if (IsTypeAssignableToWorker(t, target))
            {
                return true;
            }
        }
        return false;
    }
}
