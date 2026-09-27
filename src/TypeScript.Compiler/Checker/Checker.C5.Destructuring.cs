// ported from: internal/checker/checker.go:18001-18692
//   （isNullOrUndefined、checkRightHandSideOfForOf、getTypeForBindingElement、getTypeForBindingElementParent、
//    getBindingElementTypeFromParentType、getRestType、getFlowTypeOfDestructuring、getSyntheticElementAccess、
//    getParentElementAccess、getTypeFromBindingPattern、getTypeFromObjectBindingPattern、
//    getTypeFromArrayBindingPattern、getTypeFromBindingElement、declarationBelongsToPrivateAmbientMember、
//    getTypeOfPrototypeProperty、type thisAssignmentDeclarationKind（→ ThisAssignmentDeclarationKind enum）、
//    getWidenedTypeForAssignmentDeclaration、getAssignmentDeclarationInitializerType、hasParentWithTypeAnnotation、
//    containsSameNamedThisProperty、getTypeFromPropertyDescriptor、isConstructorDeclaredThisProperty、
//    isGlobalSymbolConstructor、widenTypeForVariableLikeDeclaration、reportImplicitAny）
//
// 与 Go 的偏离（逐条；其余与 Go 逐行对应）：
// 1) `func (c *Checker) x()` → 本 partial 类的实例方法（未导出 → `internal` + PascalCase）；Go 的包级函数
//    （无接收者）→ 同一个 `public sealed partial class Checker` 的 static 成员（本窗口没有包级函数）。
// 2) 字段命名：Go 未导出字段 → `_camelCase`（docs/checker-plan.md §5.5）：`c.strictNullChecks` → `_strictNullChecks`、
//    `c.noImplicitAny` → `_noImplicitAny`、`c.errorType` → `_errorType`、`c.factory` → `_factory`、
//    `c.valueSymbolLinks.Get(s).resolvedType` → `_valueSymbolLinks.Get(s)._resolvedType`、`t.flags` → `t._flags`、
//    `t.objectFlags` → `t._objectFlags`、`t.symbol` → `t._symbol`。
// 3) `artifacts/checker-method-collisions.md` 的 `Worker` 改名（Go 未导出者加后缀；本文件调用点）：
//    `getResolvedSymbol`→`GetResolvedSymbolWorker`、`getPropertiesOfType`→`GetPropertiesOfTypeWorker`、
//    `getUnionType`→`GetUnionTypeWorker`、`getUnionTypeEx`→`GetUnionTypeExWorker`、
//    `isTypeAssignableTo`→`IsTypeAssignableToWorker`、`getIndexInfosOfType`→`GetIndexInfosOfTypeWorker`、
//    `isArrayLikeType`→`IsArrayLikeTypeWorker`、`getReducedType`→`GetReducedTypeWorker`、
//    `getWidenedType`→`GetWidenedTypeWorker`、`getTypeFromTypeNode`→`GetTypeFromTypeNodeWorker`、
//    `getDeclaredTypeOfSymbol`→`GetDeclaredTypeOfSymbolWorker`、
//    `getTypeOfPropertyOfType`→`GetTypeOfPropertyOfTypeWorker`、
//    `getReturnTypeOfSignature`→`GetReturnTypeOfSignatureWorker`；包级同名对（Go 有导出孪生）：
//    `isTupleType`→`IsTupleTypeWorker`、`getDeclarationModifierFlagsFromSymbol`→
//    `GetDeclarationModifierFlagsFromSymbolWorker`、`isTypeUsableAsPropertyName`→`IsTypeUsableAsPropertyNameWorker`、
//    `getPropertyNameFromType`→`GetPropertyNameFromTypeWorker`。
// 4) `panic("…")` → `Debug.Fail("…")` + `throw new System.Diagnostics.UnreachableException()`（`Debug.Fail` 没有
//    `[DoesNotReturn]`，其后必须显式 throw；docs/architecture.md §2 禁止 `using System.Diagnostics;`，故写全名）。
// 5) Go 的无表达式 `switch { case <cond>: … }`（reportImplicitAny 的参数分支）→ `if/else if/else`，判定顺序不变。
// 6) `fallthrough`（getAssignmentDeclarationInitializerType 的 ThisProperty case 落到 default）→ `goto default;`。
// 7) `slices.Index(a, x)` → `Array.IndexOf(a, x)`；`slices.Contains(a, x)` → `Array.IndexOf(a, x) >= 0`
//    （两者找不到都返回 -1，与 Go 一致）。
// 8) Go 对切片做 `append` / `[:len-1]`（`c.contextualBindingPatterns`）→ C# 的 `List<Node>`：
//    `_contextualBindingPatterns.Add(pattern)` / `RemoveAt(Count - 1)`（docs/architecture.md §3.2；ChkC1 已确认字段形状）。
// 9) Go map 的缺失键返回零值 → C# 用 `GetValueOrDefault`/`TryGetValue`（绝不用会抛的索引器读）；
//    `_patternForType[result] = pattern`、`_thisExpandoKinds[symbol] = kind` 等**写**仍用索引器。
// 10) `getDestructuringPropertyName(node) (string, bool)` → `TryGetDestructuringPropertyName(node, out propName)`（§5.4）。
//     `isConstructorDeclaredThisProperty` 的 `(thisAssignmentDeclarationKind, *ast.Node)` → C# 元组
//     `(ThisAssignmentDeclarationKind Kind, Node? Location)`（C6 先例：非 "查找成功" 语义的多返回值用元组）。
// 11) `jsnum.Number(index)` → `(Number)index`（JsNum.Number 定义了 int→Number 的隐式转换）。
// 12) `^TypeFlagsNullable` → `~TypeFlags.Nullable`；`&^` → `& ~`。
// 13) Go 把实例方法当函数值传（`core.Map(properties, c.getLiteralTypeFromPropertyName)`）→ C# lambda（方法组
//     在这里配合泛型推断不稳，用 lambda 更保险；行为一致）。
// 14) `type thisAssignmentDeclarationKind int32`（checker.go:18388，iota 0..3）→ 命名空间级
//     `internal enum ThisAssignmentDeclarationKind`（数值与 Go 一致，成员名 = Go 常量名去 `thisAssignmentDeclaration` 前缀；
//     ChkC1 的 `_thisExpandoKinds` 字段已按此类型名引用）。Go 该类型上没有方法，故无对应成员。
// 15) `fmt.Sprintf("arg%v", i)` → `$"arg{i}"`（Go 的 `%v` 对 int 与 C# 默认格式一致）；`scanner.X`/`ast.X`/`core.X`/
//     `diagnostics.X` 一律 `using static` + 裸名（与 Go 的包前缀一一对应）。
// 16) Go 的 **func 类型字段**（不是方法；查 checker.go 的 Checker 字段表）→ C# 委托字段，调用时带 `_`：
//     `c.resolveName(...)` → `_resolveName(...)`（字段 `Func<Node?, string, SymbolFlags, Message?, bool, bool, Symbol?>`，
//     checker.go:661）、`c.getGlobalOmitSymbol()` → `_getGlobalOmitSymbol()`（`Func<Symbol?>`，checker.go:855）、
//     `c.getGlobalESSymbolConstructorTypeSymbolOrNil()` → `_getGlobalESSymbolConstructorTypeSymbolOrNil()`
//     （`Func<Symbol?>`，checker.go:847）。
namespace TypeScript.Checker;

using System;
using System.Collections.Generic;
using TypeScript.Ast;
using TypeScript.Core;
using TypeScript.Diagnostics;
using TypeScript.JsNum;
using static TypeScript.Ast.Ast;
using static TypeScript.Core.Core;
using static TypeScript.Diagnostics.Diagnostics;
using static TypeScript.Scanner.Scanner;

/// <summary>Go: <c>type thisAssignmentDeclarationKind int32</c>（checker.go:18388）。</summary>
internal enum ThisAssignmentDeclarationKind
{
    /// <summary>not (all) this.property assignments</summary>
    None = 0,
    /// <summary>typed; use the type annotation</summary>
    Typed = 1,
    /// <summary>at least one in the constructor; use control flow</summary>
    Constructor = 2,
    /// <summary>methods only; look in base first, and if not found, union all declaration types plus undefined</summary>
    Method = 3,
}

public sealed partial class Checker
{
    /// <summary>Go: <c>func (c *Checker) isNullOrUndefined(node *ast.Node) bool</c>（checker.go:18001）。</summary>
    internal bool IsNullOrUndefined(Node node)
    {
        var expr = SkipParentheses(node)!;
        switch (expr.Kind)
        {
            case Kind.NullKeyword:
                return true;
            case Kind.Identifier:
                return GetResolvedSymbolWorker(expr) == _undefinedSymbol;
        }
        return false;
    }

    /// <summary>Go: <c>func (c *Checker) checkRightHandSideOfForOf(statement *ast.Node) *Type</c>（checker.go:18012）。</summary>
    internal Type CheckRightHandSideOfForOf(Node statement)
    {
        var use = IfElse(statement.AsForInOrOfStatement()!.AwaitModifier != null, IterationUse.ForAwaitOf, IterationUse.ForOf);
        return CheckIteratedTypeOrElementType(use, CheckNonNullExpression(statement.Expression()!), _undefinedType, statement.Expression()!);
    }

    // Return the inferred type for a binding element

    /// <summary>Go: <c>func (c *Checker) getTypeForBindingElement(declaration *ast.Node) *Type</c>（checker.go:18018）。</summary>
    internal Type? GetTypeForBindingElement(Node declaration)
    {
        var checkMode = IfElse(HasDotDotDotToken(declaration), CheckMode.RestBindingElement, CheckMode.Normal);
        var parentType = GetTypeForBindingElementParent(declaration.Parent!.Parent!, checkMode);
        if (parentType != null)
        {
            return GetBindingElementTypeFromParentType(declaration, parentType, false /*noTupleBoundsCheck*/);
        }
        return null;
    }

    // Return the type of a binding element parent. We check SymbolLinks first to see if a type has been
    // assigned by contextual typing.

    /// <summary>Go: <c>func (c *Checker) getTypeForBindingElementParent(node *ast.Node, checkMode CheckMode) *Type</c>（checker.go:18029）。</summary>
    internal Type? GetTypeForBindingElementParent(Node node, CheckMode checkMode)
    {
        if (checkMode == CheckMode.Normal)
        {
            // We can use a cached resolved type if no optionality was included in that type.
            var symbol = GetSymbolOfDeclaration(node);
            if (symbol != null)
            {
                var resolvedType = _valueSymbolLinks.Get(symbol)._resolvedType;
                if (resolvedType != null && !(_strictNullChecks && IsOptionalDeclaration(node)))
                {
                    return resolvedType;
                }
            }
        }
        return GetTypeForVariableLikeDeclaration(node, false /*includeOptionality*/, checkMode);
    }

    /// <summary>Go: <c>func (c *Checker) getBindingElementTypeFromParentType(declaration *ast.Node, parentType *Type, noTupleBoundsCheck bool) *Type</c>（checker.go:18041）。</summary>
    internal Type GetBindingElementTypeFromParentType(Node declaration, Type parentType, bool noTupleBoundsCheck)
    {
        // If an any type was inferred for parent, infer that for the binding element
        if (IsTypeAny(parentType))
        {
            return parentType;
        }
        var pattern = declaration.Parent!;
        // Relax null check on ambient destructuring parameters, since the parameters have no implementation and are just documentation
        if (_strictNullChecks && (declaration.Flags & NodeFlags.Ambient) != 0 && IsPartOfParameterDeclaration(declaration))
        {
            parentType = GetNonNullableType(parentType);
        }
        else if (_strictNullChecks && pattern.Parent!.Initializer() != null && !HasTypeFacts(GetTypeOfInitializer(pattern.Parent!.Initializer()!), TypeFacts.EQUndefined))
        {
            parentType = GetTypeWithFacts(parentType, TypeFacts.NEUndefined);
        }
        var accessFlags = AccessFlags.ExpressionPosition | IfElse(noTupleBoundsCheck || HasDefaultValue(declaration), AccessFlags.AllowMissing, (AccessFlags)0);
        Type t;
        switch (pattern.Kind)
        {
            case Kind.ObjectBindingPattern:
                if (HasDotDotDotToken(declaration))
                {
                    parentType = GetReducedTypeWorker(parentType);
                    if ((parentType._flags & TypeFlags.Unknown) != 0 || !IsValidSpreadType(parentType))
                    {
                        Error(declaration, Rest_types_may_only_be_created_from_object_types);
                        return _errorType;
                    }
                    var elements = pattern.Elements() ?? [];
                    var literalMembers = new List<Node>(elements.Length);
                    foreach (var element in elements)
                    {
                        if (!HasDotDotDotToken(element))
                        {
                            var name = element.PropertyNameOrName()!;
                            literalMembers.Add(name);
                        }
                    }
                    t = GetRestType(parentType, literalMembers.ToArray(), declaration.Symbol!);
                }
                else
                {
                    // Use explicitly specified property name ({ p: xxx } form), or otherwise the implied name ({ p } form)
                    var name = declaration.PropertyNameOrName()!;
                    var indexType = GetLiteralTypeFromPropertyName(name);
                    var declaredType = GetIndexedAccessTypeEx(parentType, indexType, accessFlags, name, null);
                    t = GetFlowTypeOfDestructuring(declaration, declaredType);
                }
                break;
            case Kind.ArrayBindingPattern:
                // This elementType will be used if the specific property corresponding to this index is not
                // present (aka the tuple element property). This call also checks that the parentType is in
                // fact an iterable or array (depending on target language).
                var elementType = CheckIteratedTypeOrElementType(IterationUse.Destructuring | IfElse(HasDotDotDotToken(declaration), (IterationUse)0, IterationUse.PossiblyOutOfBounds), parentType, _undefinedType, pattern);
                var index = Array.IndexOf(pattern.Elements() ?? [], declaration);
                if (HasDotDotDotToken(declaration))
                {
                    // If the parent is a tuple type, the rest element has a tuple type of the
                    // remaining tuple element types. Otherwise, the rest element has an array type with same
                    // element type as the parent type.
                    var baseConstraint = MapType(parentType, inner =>
                    {
                        if ((inner._flags & TypeFlags.InstantiableNonPrimitive) != 0)
                        {
                            return GetBaseConstraintOrType(inner);
                        }
                        return inner;
                    });
                    if (EveryType(baseConstraint, inner => IsTupleTypeWorker(inner)))
                    {
                        t = MapType(baseConstraint, inner => SliceTupleType(inner, index, 0));
                    }
                    else
                    {
                        t = CreateArrayType(elementType);
                    }
                }
                else if (IsArrayLikeTypeWorker(parentType))
                {
                    var indexType = GetNumberLiteralType((Number)index);
                    var declaredType = OrElse(GetIndexedAccessTypeOrUndefined(parentType, indexType, accessFlags, declaration.Name(), null), _errorType);
                    t = GetFlowTypeOfDestructuring(declaration, declaredType!);
                }
                else
                {
                    t = elementType;
                }
                break;
            default:
                Debug.Fail("Unhandled case in getBindingElementTypeFromParentType");
                throw new System.Diagnostics.UnreachableException();
        }
        if (declaration.Initializer() == null)
        {
            return t;
        }
        if (WalkUpBindingElementsAndPatterns(declaration)!.Type() != null)
        {
            // In strict null checking mode, if a default value of a non-undefined type is specified, remove
            // undefined from the final type.
            if (_strictNullChecks && !HasTypeFacts(CheckDeclarationInitializer(declaration, CheckMode.Normal, null), TypeFacts.IsUndefined))
            {
                return GetNonUndefinedType(t);
            }
            return t;
        }
        return WidenTypeInferredFromInitializer(declaration, GetUnionTypeExWorker(new[] { GetNonUndefinedType(t), CheckDeclarationInitializer(declaration, CheckMode.Normal, null) }, UnionReduction.Subtype, null, null));
    }

    /// <summary>Go: <c>func (c *Checker) getRestType(source *Type, properties []*ast.Node, symbol *ast.Symbol) *Type</c>（checker.go:18126）。</summary>
    internal Type GetRestType(Type source, Node[] properties, Symbol symbol)
    {
        source = FilterType(source, inner => (inner._flags & TypeFlags.Nullable) == 0);
        if ((source._flags & TypeFlags.Never) != 0)
        {
            return _emptyObjectType;
        }
        if ((source._flags & TypeFlags.Union) != 0)
        {
            return MapType(source, inner => GetRestType(inner, properties, symbol));
        }
        var omitKeyType = GetUnionTypeWorker(Map(properties, name => GetLiteralTypeFromPropertyName(name)));
        var spreadableProperties = new List<Symbol>();
        var unspreadableToRestKeys = new List<Type>();
        foreach (var prop in GetPropertiesOfTypeWorker(source) ?? [])
        {
            var literalTypeFromProperty = GetLiteralTypeFromProperty(prop, TypeFlags.StringOrNumberLiteralOrUnique, false);
            if (!IsTypeAssignableToWorker(literalTypeFromProperty, omitKeyType) && (GetDeclarationModifierFlagsFromSymbolWorker(prop) & (ModifierFlags.Private | ModifierFlags.Protected)) == 0 && IsSpreadableProperty(prop))
            {
                spreadableProperties.Add(prop);
            }
            else
            {
                unspreadableToRestKeys.Add(literalTypeFromProperty);
            }
        }
        if (IsGenericObjectType(source) || IsGenericIndexType(omitKeyType))
        {
            if (unspreadableToRestKeys.Count != 0)
            {
                // If the type we're spreading from has properties that cannot
                // be spread into the rest type (e.g. getters, methods), ensure
                // they are explicitly omitted, as they would in the non-generic case.
                omitKeyType = GetUnionTypeWorker(Concatenate(new[] { omitKeyType }, unspreadableToRestKeys.ToArray()));
            }
            if ((omitKeyType._flags & TypeFlags.Never) != 0)
            {
                return source;
            }
            var omitTypeAlias = _getGlobalOmitSymbol();
            if (omitTypeAlias == null)
            {
                return _errorType;
            }
            return GetTypeAliasInstantiation(omitTypeAlias, new[] { source, omitKeyType }, null);
        }
        var members = new SymbolTable();
        foreach (var prop in spreadableProperties)
        {
            members[prop.Name] = GetSpreadSymbol(prop, false /*readonly*/);
        }
        var result = NewAnonymousType(symbol, members, null, null, GetIndexInfosOfTypeWorker(source));
        result._objectFlags |= ObjectFlags.ObjectRestType;
        return result;
    }

    // Determine the control flow type associated with a destructuring declaration or assignment. The following
    // forms of destructuring are possible:
    //
    //	let { x } = obj;  // BindingElement
    //	let [ x ] = obj;  // BindingElement
    //	{ x } = obj;      // ShorthandPropertyAssignment
    //	{ x: v } = obj;   // PropertyAssignment
    //	[ x ] = obj;      // Expression
    //
    // We construct a synthetic element access expression corresponding to 'obj.x' such that the control
    // flow analyzer doesn't have to handle all the different syntactic forms.

    /// <summary>Go: <c>func (c *Checker) getFlowTypeOfDestructuring(node *ast.Node, declaredType *Type) *Type</c>（checker.go:18183）。</summary>
    internal Type GetFlowTypeOfDestructuring(Node node, Type declaredType)
    {
        var reference = GetSyntheticElementAccess(node);
        if (reference != null)
        {
            return GetFlowTypeOfReference(reference, declaredType);
        }
        return declaredType;
    }

    /// <summary>Go: <c>func (c *Checker) getSyntheticElementAccess(node *ast.Node) *ast.Node</c>（checker.go:18191）。</summary>
    internal Node? GetSyntheticElementAccess(Node node)
    {
        var parentAccess = GetParentElementAccess(node);
        if (parentAccess != null && GetFlowNodeOfNode(parentAccess) != null)
        {
            if (TryGetDestructuringPropertyName(node, out var propName))
            {
                var literal = _factory.NewStringLiteral(propName, TokenFlags.None);
                literal.Loc = node.Loc;
                var lhsExpr = parentAccess;
                if (!IsLeftHandSideExpression(parentAccess))
                {
                    lhsExpr = _factory.NewParenthesizedExpression(parentAccess);
                    lhsExpr.Loc = node.Loc;
                }
                var result = _factory.NewElementAccessExpression(lhsExpr, null, literal, NodeFlags.None);
                result.Loc = node.Loc;
                literal.Parent = result;
                result.Parent = node;
                if (lhsExpr != parentAccess)
                {
                    lhsExpr.Parent = result;
                }
                result.FlowNodeData()!.FlowNode = GetFlowNodeOfNode(parentAccess);
                return result;
            }
        }
        return null;
    }

    /// <summary>Go: <c>func (c *Checker) getParentElementAccess(node *ast.Node) *ast.Node</c>（checker.go:18216）。</summary>
    internal Node? GetParentElementAccess(Node node)
    {
        var ancestor = node.Parent!.Parent!;
        switch (ancestor.Kind)
        {
            case Kind.BindingElement:
            case Kind.PropertyAssignment:
                return GetSyntheticElementAccess(ancestor);
            case Kind.ArrayLiteralExpression:
                return GetSyntheticElementAccess(node.Parent!);
            case Kind.VariableDeclaration:
                return ancestor.Initializer();
            case Kind.BinaryExpression:
                return ancestor.AsBinaryExpression()!.Right;
        }
        return null;
    }

    // Return the type implied by a binding pattern. This is the type implied purely by the binding pattern itself
    // and without regard to its context (i.e. without regard any type annotation or initializer associated with the
    // declaration in which the binding pattern is contained). For example, the implied type of [x, y] is [any, any]
    // and the implied type of { x, y: z = 1 } is { x: any; y: number; }. The type implied by a binding pattern is
    // used as the contextual type of an initializer associated with the binding pattern. Also, for a destructuring
    // parameter with no type annotation or initializer, the type implied by the binding pattern becomes the type of
    // the parameter.

    /// <summary>Go: <c>func (c *Checker) getTypeFromBindingPattern(pattern *ast.Node, includePatternInType bool, reportErrors bool) *Type</c>（checker.go:18238）。</summary>
    internal Type GetTypeFromBindingPattern(Node pattern, bool includePatternInType, bool reportErrors)
    {
        if (includePatternInType)
        {
            _contextualBindingPatterns.Add(pattern);
        }
        Type result;
        if (IsObjectBindingPattern(pattern))
        {
            result = GetTypeFromObjectBindingPattern(pattern, includePatternInType, reportErrors);
        }
        else
        {
            result = GetTypeFromArrayBindingPattern(pattern, includePatternInType, reportErrors);
        }
        if (includePatternInType)
        {
            _contextualBindingPatterns.RemoveAt(_contextualBindingPatterns.Count - 1);
        }
        return result;
    }

    // Return the type implied by an object binding pattern

    /// <summary>Go: <c>func (c *Checker) getTypeFromObjectBindingPattern(pattern *ast.Node, includePatternInType bool, reportErrors bool) *Type</c>（checker.go:18255）。</summary>
    internal Type GetTypeFromObjectBindingPattern(Node pattern, bool includePatternInType, bool reportErrors)
    {
        var members = new SymbolTable();
        IndexInfo? stringIndexInfo = null;
        var objectFlags = ObjectFlags.ObjectLiteral | ObjectFlags.ContainsObjectOrArrayLiteral;
        foreach (var e in pattern.Elements() ?? [])
        {
            var name = e.PropertyNameOrName()!;
            if (HasDotDotDotToken(e))
            {
                stringIndexInfo = NewIndexInfo(_stringType, _anyType, false /*isReadonly*/, null, null);
                continue;
            }
            var exprType = GetLiteralTypeFromPropertyName(name);
            if (!IsTypeUsableAsPropertyNameWorker(exprType))
            {
                // do not include computed properties in the implied type
                objectFlags |= ObjectFlags.ObjectLiteralPatternWithComputedProperties;
                continue;
            }
            var text = GetPropertyNameFromTypeWorker(exprType);
            var flags = SymbolFlags.Property | IfElse(e.Initializer() != null, SymbolFlags.Optional, (SymbolFlags)0);
            var symbol = NewSymbol(flags, text);
            _valueSymbolLinks.Get(symbol)._resolvedType = GetTypeFromBindingElement(e, includePatternInType, reportErrors);
            members[symbol.Name] = symbol;
        }
        IndexInfo[]? indexInfos = null;
        if (stringIndexInfo != null)
        {
            indexInfos = new[] { stringIndexInfo };
        }
        // Go 传 nil symbol；C# 侧 `NewAnonymousType` 的首参声明为非空 `Symbol`，故用 `null!` 压掉可空告警（运行时值就是 null）。
        var result = NewAnonymousType(null!, members, null, null, indexInfos);
        result._objectFlags |= objectFlags;
        if (includePatternInType)
        {
            _patternForType[result] = pattern;
            result._objectFlags |= ObjectFlags.ContainsObjectOrArrayLiteral;
        }
        return result;
    }

    // Return the type implied by an array binding pattern

    /// <summary>Go: <c>func (c *Checker) getTypeFromArrayBindingPattern(pattern *ast.Node, includePatternInType bool, reportErrors bool) *Type</c>（checker.go:18291）。</summary>
    internal Type GetTypeFromArrayBindingPattern(Node pattern, bool includePatternInType, bool reportErrors)
    {
        var elements = pattern.Elements() ?? [];
        var lastElement = LastOrNil(elements);
        Node? restElement = null;
        if (lastElement != null && IsBindingElement(lastElement) && HasDotDotDotToken(lastElement))
        {
            restElement = lastElement;
        }
        if (elements.Length == 0 || (elements.Length == 1 && restElement != null))
        {
            // TODO: remove ScriptTargetES2015
            if (_languageVersion >= ScriptTarget.ES2015)
            {
                return CreateIterableType(_anyType);
            }
            return _anyArrayType;
        }
        var minLength = FindLastIndex(elements, x => !(x == restElement || x.Name() == null || HasDefaultValue(x))) + 1;
        var elementTypes = new Type[elements.Length];
        var elementInfos = new TupleElementInfo[elements.Length];
        for (var i = 0; i < elements.Length; i++)
        {
            var e = elements[i];
            Type t;
            if (e.Name() == null)
            {
                t = _anyType;
            }
            else
            {
                t = GetTypeFromBindingElement(e, includePatternInType, reportErrors);
            }
            ElementFlags flags;
            if (e == restElement)
            {
                flags = ElementFlags.Rest;
            }
            else if (i >= minLength)
            {
                flags = ElementFlags.Optional;
            }
            else
            {
                flags = ElementFlags.Required;
            }
            elementTypes[i] = t;
            elementInfos[i] = new TupleElementInfo { _flags = flags };
        }
        var result = CreateTupleTypeEx(elementTypes, elementInfos, false);
        if (includePatternInType)
        {
            result = CloneTypeReference(result);
            _patternForType[result] = pattern;
            result._objectFlags |= ObjectFlags.ContainsObjectOrArrayLiteral;
        }
        return result;
    }

    // Return the type implied by a binding pattern element. This is the type of the initializer of the element if
    // one is present. Otherwise, if the element is itself a binding pattern, it is the type implied by the binding
    // pattern. Otherwise, it is the type any.

    /// <summary>Go: <c>func (c *Checker) getTypeFromBindingElement(element *ast.Node, includePatternInType bool, reportErrors bool) *Type</c>（checker.go:18340）。</summary>
    internal Type GetTypeFromBindingElement(Node element, bool includePatternInType, bool reportErrors)
    {
        if (element.Initializer() != null)
        {
            // The type implied by a binding pattern is independent of context, so we check the initializer with no
            // contextual type or, if the element itself is a binding pattern, with the type implied by that binding
            // pattern.
            var contextualType = _unknownType;
            if (IsBindingPattern(element.Name()))
            {
                contextualType = GetTypeFromBindingPattern(element.Name()!, true /*includePatternInType*/, false /*reportErrors*/);
            }
            return AddOptionality(GetWidenedLiteralTypeForInitializer(element, CheckDeclarationInitializer(element, CheckMode.Normal, contextualType)));
        }
        if (IsBindingPattern(element.Name()))
        {
            return GetTypeFromBindingPattern(element.Name()!, includePatternInType, reportErrors);
        }
        if (reportErrors && !DeclarationBelongsToPrivateAmbientMember(element))
        {
            ReportImplicitAny(element, _anyType, WideningKind.Normal);
        }
        // When we're including the pattern in the type (an indication we're obtaining a contextual type), we
        // use a non-inferrable any type. Inference will never directly infer this type, but it is possible
        // to infer a type that contains it, e.g. for a binding pattern like [foo] or { foo }. In such cases,
        // widening of the binding pattern type substitutes a regular any for the non-inferrable any.
        if (includePatternInType)
        {
            return _nonInferrableAnyType;
        }
        return _anyType;
    }

    /// <summary>Go: <c>func (c *Checker) declarationBelongsToPrivateAmbientMember(declaration *ast.Node) bool</c>（checker.go:18367）。</summary>
    internal bool DeclarationBelongsToPrivateAmbientMember(Node declaration)
    {
        var memberDeclaration = GetRootDeclaration(declaration)!;
        if (IsParameterDeclaration(memberDeclaration))
        {
            memberDeclaration = memberDeclaration.Parent!;
        }
        return IsPrivateWithinAmbient(memberDeclaration);
    }

    /// <summary>Go: <c>func (c *Checker) getTypeOfPrototypeProperty(prototype *ast.Symbol) *Type</c>（checker.go:18375）。</summary>
    internal Type GetTypeOfPrototypeProperty(Symbol prototype)
    {
        // TypeScript 1.0 spec (April 2014): 8.4
        // Every class automatically contains a static property member named 'prototype',
        // the type of which is an instantiation of the class type with type Any supplied as a type argument for each type parameter.
        // It is an error to explicitly declare a static property member with the name 'prototype'.
        var classType = GetDeclaredTypeOfSymbolWorker(GetParentOfSymbol(prototype)!);
        var typeParameters = classType.AsInterfaceType()!.TypeParameters();
        if (typeParameters.Length != 0)
        {
            return CreateTypeReference(classType, Map(typeParameters, _ => _anyType));
        }
        return classType;
    }

    /// <summary>Go: <c>func (c *Checker) getWidenedTypeForAssignmentDeclaration(symbol *ast.Symbol) *Type</c>（checker.go:18397）。</summary>
    internal Type GetWidenedTypeForAssignmentDeclaration(Symbol symbol)
    {
        Type? t = null;
        var (kind, location) = IsConstructorDeclaredThisProperty(symbol);
        switch (kind)
        {
            case ThisAssignmentDeclarationKind.Typed:
                if (location == null)
                {
                    Debug.Fail("location should not be nil when this assignment has a type.");
                    throw new System.Diagnostics.UnreachableException();
                }
                t = GetTypeFromTypeNodeWorker(location);
                break;
            case ThisAssignmentDeclarationKind.Constructor:
                if (location == null)
                {
                    Debug.Fail("constructor should not be nil when this assignment is in a constructor.");
                    throw new System.Diagnostics.UnreachableException();
                }
                t = GetFlowTypeInConstructor(symbol, location);
                break;
            case ThisAssignmentDeclarationKind.Method:
                t = GetTypeOfPropertyInBaseClass(symbol);
                break;
        }
        if (t == null)
        {
            Type[]? types = null;
            var declarations = symbol.Declarations ?? [];
            for (var i = 0; i < declarations.Length; i++)
            {
                var declaration = declarations[i];
                if (IsBinaryExpression(declaration) && declaration.Type() != null)
                {
                    t = GetTypeFromTypeNodeWorker(declaration.Type()!);
                    break;
                }
                var assignedType = GetAssignmentDeclarationInitializerType(declaration);
                if (assignedType != null)
                {
                    // We ignore initial assignments of undefined to CommonJS exports when there are multiple assignment declarations
                    if (GetAssignmentDeclarationKind(declaration) != JSDeclarationKind.ExportsProperty || i != 0 || declarations.Length == 1 || (assignedType._flags & TypeFlags.Undefined) == 0)
                    {
                        types = AppendIfUnique(types, assignedType);
                    }
                }
            }
            if (kind == ThisAssignmentDeclarationKind.Method && (types?.Length ?? 0) > 0)
            {
                if (_strictNullChecks)
                {
                    types = AppendIfUnique(types, _undefinedOrMissingType);
                }
            }
            if (t == null)
            {
                t = _anyType;
                if ((types?.Length ?? 0) != 0)
                {
                    t = GetUnionTypeWorker(types!);
                }
            }
        }
        t = GetWidenedTypeWorker(t!);
        // report an all-nullable or empty union as an implicit any in JS files
        if (symbol.ValueDeclaration != null && IsInJSFile(symbol.ValueDeclaration) &&
            FilterType(t!, x => (x._flags & ~TypeFlags.Nullable) != 0) == _neverType)
        {
            ReportImplicitAny(symbol.ValueDeclaration, _anyType, WideningKind.Normal);
            return _anyType;
        }
        return t!;
    }

    /// <summary>Go: <c>func (c *Checker) getAssignmentDeclarationInitializerType(node *ast.Node) *Type</c>（checker.go:18450）。</summary>
    internal Type? GetAssignmentDeclarationInitializerType(Node node)
    {
        if (IsBinaryExpression(node))
        {
            Type? t;
            switch (GetAssignmentDeclarationKind(node))
            {
                case JSDeclarationKind.ModuleExports:
                case JSDeclarationKind.ExportsProperty:
                    t = GetRegularTypeOfLiteralType(CheckExpressionCached(GetRightMostAssignedExpression(node)));
                    break;
                case JSDeclarationKind.ThisProperty:
                    if (ContainsSameNamedThisProperty(node.AsBinaryExpression()!.Left!, node.AsBinaryExpression()!.Right!))
                    {
                        return null;
                    }
                    goto default;
                default:
                    t = CheckExpressionForMutableLocation(node.AsBinaryExpression()!.Right!, CheckMode.Normal);
                    break;
            }
            if (IsEmptyArrayLiteralType(t!) && !HasParentWithTypeAnnotation(node.Symbol!))
            {
                ReportImplicitAny(node, _anyArrayType, WideningKind.Normal);
                return _anyArrayType;
            }
            return t;
        }
        if (IsCallExpression(node))
        {
            return GetTypeFromPropertyDescriptor(node.Arguments()![2]);
        }
        return null;
    }

    // Return true if the parent symbol of the given assignment declaration symbol has declaration with a type
    // annotation. For example, returns true for the symbol associated with `f.a` below:
    //
    //	const f: { (): void, a: string[] } = () => {};
    //	f.a = [];

    /// <summary>Go: <c>func (c *Checker) hasParentWithTypeAnnotation(symbol *ast.Symbol) bool</c>（checker.go:18481）。</summary>
    internal bool HasParentWithTypeAnnotation(Symbol symbol)
    {
        if (symbol.Parent != null && symbol.Parent.ValueDeclaration != null && IsFunctionExpressionOrArrowFunction(symbol.Parent.ValueDeclaration))
        {
            var possiblyAnnotatedSymbol = GetSymbolOfNode(symbol.Parent.ValueDeclaration.Parent!);
            if (possiblyAnnotatedSymbol != null && possiblyAnnotatedSymbol.ValueDeclaration != null)
            {
                return possiblyAnnotatedSymbol.ValueDeclaration.Type() != null;
            }
        }
        return false;
    }

    /// <summary>Go: <c>func (c *Checker) containsSameNamedThisProperty(thisProperty *ast.Node, expression *ast.Node) bool</c>（checker.go:18490）。</summary>
    internal bool ContainsSameNamedThisProperty(Node thisProperty, Node expression)
    {
        // Go 的局部递归函数 `var visit func(node *ast.Node) bool` → C# 局部函数。
        bool Visit(Node node)
        {
            if (IsMatchingReference(thisProperty, node))
            {
                return true;
            }
            if (IsFunctionLike(node))
            {
                return false;
            }
            return node.ForEachChild(Visit);
        }
        return Visit(expression);
    }

    /// <summary>Go: <c>func (c *Checker) getTypeFromPropertyDescriptor(node *ast.Node) *Type</c>（checker.go:18504）。</summary>
    internal Type GetTypeFromPropertyDescriptor(Node node)
    {
        var objectLiteralType = CheckExpressionCached(node);
        var valueType = GetTypeOfPropertyOfTypeWorker(objectLiteralType, "value");
        if (valueType != null)
        {
            return valueType;
        }
        var getFunc = GetTypeOfPropertyOfTypeWorker(objectLiteralType, "get");
        if (getFunc != null)
        {
            var getSig = GetSingleCallSignature(getFunc);
            if (getSig != null)
            {
                return GetReturnTypeOfSignatureWorker(getSig);
            }
        }
        var setFunc = GetTypeOfPropertyOfTypeWorker(objectLiteralType, "set");
        if (setFunc != null)
        {
            var setSig = GetSingleCallSignature(setFunc);
            if (setSig != null)
            {
                return GetTypeOfFirstParameterOfSignature(setSig);
            }
        }
        return _anyType;
    }

    // A property is considered a constructor declared property when all declaration sites are this.xxx assignments,
    // when no declaration sites have JSDoc type annotations, and when at least one declaration site is in the body of
    // a class constructor.

    /// <summary>Go: <c>func (c *Checker) isConstructorDeclaredThisProperty(symbol *ast.Symbol) (thisAssignmentDeclarationKind, *ast.Node)</c>（checker.go:18525）。</summary>
    internal (ThisAssignmentDeclarationKind Kind, Node? Location) IsConstructorDeclaredThisProperty(Symbol symbol)
    {
        if (symbol.ValueDeclaration == null || !IsBinaryExpression(symbol.ValueDeclaration))
        {
            return (ThisAssignmentDeclarationKind.None, null);
        }
        if (_thisExpandoKinds.TryGetValue(symbol, out var cachedKind))
        {
            if (!_thisExpandoLocations.TryGetValue(symbol, out var cachedLocation))
            {
                Debug.Fail("location should be cached whenever this expando symbol is cached");
                throw new System.Diagnostics.UnreachableException();
            }
            return (cachedKind, cachedLocation);
        }
        var allThis = true;
        Node? typeAnnotation = null;
        foreach (var declaration in symbol.Declarations ?? [])
        {
            if (!IsBinaryExpression(declaration))
            {
                allThis = false;
                break;
            }
            var bin = declaration.AsBinaryExpression()!;
            if (GetAssignmentDeclarationKind(declaration) == JSDeclarationKind.ThisProperty &&
                (bin.Left!.Kind != Kind.ElementAccessExpression || IsStringOrNumericLiteralLike(bin.Left!.AsElementAccessExpression()!.ArgumentExpression)))
            {
                if (bin.Type != null)
                {
                    typeAnnotation = bin.Type;
                }
            }
            else
            {
                allThis = false;
                break;
            }
        }
        Node? location = null;
        var kind = ThisAssignmentDeclarationKind.None;
        if (allThis)
        {
            if (typeAnnotation != null)
            {
                location = typeAnnotation;
                kind = ThisAssignmentDeclarationKind.Typed;
            }
            else
            {
                location = GetDeclaringConstructor(symbol);
                kind = IfElse(location == null, ThisAssignmentDeclarationKind.Method, ThisAssignmentDeclarationKind.Constructor);
            }
        }
        _thisExpandoKinds[symbol] = kind;
        // Go 在 allThis == false 时把 nil 存进 thisExpandoLocations；字段值类型声明为非空 `Node`，故 `!` 压告警（运行时仍是 null）。
        _thisExpandoLocations[symbol] = location!;
        return (kind, location);
    }

    /// <summary>Go: <c>func (c *Checker) isGlobalSymbolConstructor(node *ast.Node) bool</c>（checker.go:18570）。</summary>
    internal bool IsGlobalSymbolConstructor(Node node)
    {
        var symbol = GetSymbolOfNode(node);
        var globalSymbol = _getGlobalESSymbolConstructorTypeSymbolOrNil();
        return globalSymbol != null && symbol == globalSymbol;
    }

    /// <summary>Go: <c>func (c *Checker) widenTypeForVariableLikeDeclaration(t *Type, declaration *ast.Node, reportErrors bool) *Type</c>（checker.go:18576）。</summary>
    internal Type WidenTypeForVariableLikeDeclaration(Type? t, Node declaration, bool reportErrors)
    {
        if (t != null)
        {
            // This special case is required for backwards compatibility with libraries that merge a `symbol` property into `SymbolConstructor`.
            // See https://github.com/microsoft/TypeScript/tsc/issues/1212
            if ((t._flags & TypeFlags.ESSymbol) != 0 && IsGlobalSymbolConstructor(declaration.Parent!))
            {
                t = GetESSymbolLikeTypeForNode(declaration);
            }

            if (reportErrors)
            {
                ReportErrorsFromWidening(declaration, t, WideningKind.Normal);
            }

            // always widen a 'unique symbol' type if the type was created for a different declaration.
            if ((t._flags & TypeFlags.UniqueESSymbol) != 0 && (IsBindingElement(declaration) || declaration.Type() == null) && t._symbol != GetSymbolOfDeclaration(declaration))
            {
                t = _esSymbolType;
            }
            return GetWidenedTypeWorker(t);
        }
        // Rest parameters default to type any[], other parameters default to type any
        if (IsParameterDeclaration(declaration) && declaration.AsParameterDeclaration()!.DotDotDotToken != null)
        {
            t = _anyArrayType;
        }
        else
        {
            t = _anyType;
        }
        // Report implicit any errors unless this is a private property within an ambient declaration
        if (reportErrors)
        {
            if (!DeclarationBelongsToPrivateAmbientMember(declaration))
            {
                ReportImplicitAny(declaration, t!, WideningKind.Normal);
            }
        }
        return t!;
    }

    /// <summary>Go: <c>func (c *Checker) reportImplicitAny(declaration *ast.Node, t *Type, wideningKind WideningKind)</c>（checker.go:18609）。</summary>
    internal void ReportImplicitAny(Node declaration, Type t, WideningKind wideningKind)
    {
        if (IsInJSFile(declaration) && !IsCheckJSEnabledForFile(GetSourceFileOfNode(declaration)!, _compilerOptions))
        {
            // Only report implicit any errors/suggestions in TS and ts-check JS files
            return;
        }
        var typeAsString = TypeToString(GetWidenedTypeWorker(t));
        Message? diagnostic = null;
        switch (declaration.Kind)
        {
            case Kind.BinaryExpression:
            case Kind.PropertyDeclaration:
            case Kind.PropertySignature:
                diagnostic = IfElse(_noImplicitAny,
                    Member_0_implicitly_has_an_1_type,
                    Member_0_implicitly_has_an_1_type_but_a_better_type_may_be_inferred_from_usage);
                break;
            case Kind.Parameter:
                var param = declaration.AsParameterDeclaration()!;
                if (IsIdentifier(param.Name()!))
                {
                    var name = param.Name()!.AsIdentifier()!;
                    var originalKeywordKind = IdentifierToKeywordKind(name);
                    if ((IsCallSignatureDeclaration(declaration.Parent!) || IsMethodSignatureDeclaration(declaration.Parent!) || IsFunctionTypeNode(declaration.Parent!)) &&
                        Array.IndexOf(declaration.Parent!.Parameters() ?? [], declaration) >= 0 &&
                        (IsTypeNodeKind(originalKeywordKind) || _resolveName(declaration, name.Text, SymbolFlags.Type, null /*nameNotFoundMessage*/, true /*isUse*/, false /*excludeGlobals*/) != null))
                    {
                        var newName = $"arg{Array.IndexOf(declaration.Parent!.Parameters() ?? [], declaration)}";
                        var typeName = DeclarationNameToString(param.Name()) + IfElse(param.DotDotDotToken != null, "[]", "");
                        ErrorOrSuggestion(_noImplicitAny, declaration, Parameter_has_a_name_but_no_type_Did_you_mean_0_Colon_1, newName, typeName);
                        return;
                    }
                }
                // Go 的无表达式 switch { case …: } → if/else if/else（判定顺序不变）。
                if (param.DotDotDotToken != null)
                {
                    if (_noImplicitAny)
                    {
                        diagnostic = Rest_parameter_0_implicitly_has_an_any_type;
                    }
                    else
                    {
                        diagnostic = Rest_parameter_0_implicitly_has_an_any_type_but_a_better_type_may_be_inferred_from_usage;
                    }
                }
                else if (_noImplicitAny)
                {
                    diagnostic = Parameter_0_implicitly_has_an_1_type;
                }
                else
                {
                    diagnostic = Parameter_0_implicitly_has_an_1_type_but_a_better_type_may_be_inferred_from_usage;
                }
                break;
            case Kind.BindingElement:
                diagnostic = Binding_element_0_implicitly_has_an_1_type;
                if (!_noImplicitAny)
                {
                    // Don't issue a suggestion for binding elements since the codefix doesn't yet support them.
                    return;
                }
                break;
            case Kind.FunctionDeclaration:
            case Kind.MethodDeclaration:
            case Kind.MethodSignature:
            case Kind.GetAccessor:
            case Kind.SetAccessor:
            case Kind.FunctionExpression:
            case Kind.ArrowFunction:
                if (_noImplicitAny && declaration.Name() == null)
                {
                    if (wideningKind == WideningKind.GeneratorYield)
                    {
                        Error(declaration, Generator_implicitly_has_yield_type_0_Consider_supplying_a_return_type_annotation, typeAsString);
                    }
                    else
                    {
                        Error(declaration, Function_expression_which_lacks_return_type_annotation_implicitly_has_an_0_return_type, typeAsString);
                    }
                    return;
                }
                if (!_noImplicitAny)
                {
                    diagnostic = X_0_implicitly_has_an_1_return_type_but_a_better_type_may_be_inferred_from_usage;
                }
                else if ((declaration.Flags & NodeFlags.Reparsed) != 0)
                {
                    var name = DeclarationNameToString(GetNameOfDeclaration(declaration));
                    if (name != "")
                    {
                        Error(declaration, X_0_which_lacks_return_type_annotation_implicitly_has_an_1_return_type, name, typeAsString);
                    }
                    else
                    {
                        Error(declaration, This_overload_implicitly_returns_the_type_0_because_it_lacks_a_return_type_annotation, typeAsString);
                    }
                    return;
                }
                else if (wideningKind == WideningKind.GeneratorYield)
                {
                    diagnostic = X_0_which_lacks_return_type_annotation_implicitly_has_an_1_yield_type;
                }
                else
                {
                    diagnostic = X_0_which_lacks_return_type_annotation_implicitly_has_an_1_return_type;
                }
                break;
            case Kind.MappedType:
                if (_noImplicitAny)
                {
                    Error(declaration, Mapped_object_type_implicitly_has_an_any_template_type);
                }
                return;
            default:
                if (_noImplicitAny)
                {
                    diagnostic = Variable_0_implicitly_has_an_1_type;
                }
                else
                {
                    diagnostic = Variable_0_implicitly_has_an_1_type_but_a_better_type_may_be_inferred_from_usage;
                }
                break;
        }
        ErrorOrSuggestion(_noImplicitAny, declaration, diagnostic!, DeclarationNameToString(GetNameOfDeclaration(declaration)), typeAsString);
    }
}
