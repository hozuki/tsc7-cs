// ported from: internal/checker/checker.go:16718-17694
//   （getTypeOfSymbolWithDeferredType … isThislessInterface：符号的读/写类型、延迟与实例化符号、
//     变量/参数/属性的类型推断（含声明初始化器、绑定元素、元组/对象字面量补齐）、函数/类/枚举/模块类型、
//     类与接口的声明类型与基构造类型、类型参数约束推导（type parameter / indexed access / conditional））
//
// 与 Go 的偏离（逐条；其余与 Go 逐行对应）：
// 1) Go 的 `func (c *Checker) x()` → 实例方法（PascalCase）；Go 的**包级函数**（无接收者）→ 同一个
//    `public sealed partial class Checker` 的 `internal static` 成员（docs/checker-plan.md §5.2；本窗口只有
//    `signatureHasRestParameter`）。
// 2) 命名：Go 未导出**字段** → `_camelCase`（`links.resolvedType` → `links._resolvedType`、`t.flags` → `t._flags`、
//    `tp.constraint` → `tp._constraint`、`d.objectType` → `d._objectType`、`t.symbol` → `t._symbol`）；
//    Go 导出字段保持原名；枚举成员名 = Go 常量名去类型前缀（`TypeFlagsUnion` → `TypeFlags.Union`、
//    `ObjectFlagsAnonymous` → `ObjectFlags.Anonymous`）。
// 3) `artifacts/checker-method-collisions.md` 的 Worker 改名（定义点与调用点都改）：本窗口 4 个未导出方法与
//    Go 导出同名 → `getTypeOfSymbol` → `GetTypeOfSymbolWorker`、`getNonMissingTypeOfSymbol` →
//    `GetNonMissingTypeOfSymbolWorker`、`getBaseConstructorTypeOfClass` → `GetBaseConstructorTypeOfClassWorker`、
//    `getConstraintOfTypeParameter` → `GetConstraintOfTypeParameterWorker`；调用别人的成员同样用改后名
//    （`GetTypeFromTypeNodeWorker`、`GetReturnTypeOfSignatureWorker`、`GetSignatureFromDeclarationWorker`、
//    `GetSignaturesOfTypeWorker`、`ResolveExternalModuleSymbolWorker`、`GetWidenedTypeWorker`、
//    `GetWidenedLiteralTypeWorker`、`GetUnionTypeWorker`、`GetIndexInfosOfTypeWorker`、`GetPropertyOfTypeWorker`、
//    `GetBaseConstraintOfTypeWorker`、`GetElementTypeOfArrayTypeWorker`、`SymbolToStringWorker`；
//    本窗口调用的 `TypeToString` 是 Go 的**导出**方法（printer.go:43），保持原名）。
// 4) Go 的 `panic("…")` → `Debug.Fail("…")` + `throw new System.Diagnostics.UnreachableException()`
//    （`Debug.Fail` 没有 `[DoesNotReturn]`，其后必须补 throw 才能满足控制流与返回值要求；全项目禁止
//    `using System.Diagnostics;`，故 UnreachableException 写全名）。
// 5) Go 的 `slices.Clone(x)` + 循环 `append` → `List<T>`（`padTupleType`、`getInferredTypeParameterConstraint`），
//    结尾 `ToArray()`；Go 的 nil slice ↔ 空集合、`len(nil) == 0` ↔ `Count == 0`。
//    `append(typeParameters, d.thisType)` → `Core.Concatenate(typeParameters, new Type[] { d.thisType })`。
// 6) `slices.Index(parent.TypeArguments(), child)` → `FindIndex(parent.TypeArguments(), n => n == child)`：
//    Go 的 slices.Index 对 nil 返回 -1，而 `Array.IndexOf(null, …)` 会抛异常；`Core.FindIndex` 对 null 返回 -1，
//    语义一致（`getInferredTypeParameterConstraint`）。
// 7) Go 的 `switch { case cond: }`（无表达式的 switch）→ `if` / `else if`（C# 无该形式，逐条件、逐短路顺序对应）。
// 8) Go map 读缺失键返回 nil → `Dictionary.GetValueOrDefault`（`c.cachedTypes[CachedTypeKey{…}]` →
//    `_cachedTypes.GetValueOrDefault(new CachedTypeKey(…))`）。
// 9) C# 的 `Node.Symbol` / `Node.ModifierFlags` 是**属性**（Go 的 `Symbol()` / `ModifierFlags()` 方法）→ 调用点去括号。
// 10) `TupleElementInfo{flags: ElementFlagsOptional}` → `new TupleElementInfo { _flags = ElementFlags.Optional }`
//    （C# 里是 struct，字段 `_flags`）。
// 11) `sig.typeParameters` / `sig.parameters`（可空字段）改用 ChkTypes 的访问器 `TypeParameters()` / `Parameters()`
//    （返回非空数组，与 Go 的 `len(nil) == 0` 一致）。
// 12) Go 里"运行期已证明非空、但 C# 可空分析看不出来"的位置补 `!`（编译期抑制，不影响运行期语义）。
// 布局（依 Main/T6Declarations 的探针结论）：usings 必须位于 file-scoped `namespace` **之后**。
// 若放在 namespace 之前，裸写 `Diagnostics.X` 会先命中外层命名空间链里的命名空间 `TypeScript.Diagnostics`
// → CS0234（T6 实测：那种布局下连 `using Diagnostics = TypeScript.Diagnostics.Diagnostics;` 别名也无效）。
namespace TypeScript.Checker;

using System;
using System.Collections.Generic;
using TypeScript.Ast;
using TypeScript.Core;
using TypeScript.Diagnostics;
using static TypeScript.Ast.Ast;
using static TypeScript.Core.Core;

public sealed partial class Checker
{
    internal Type GetTypeOfSymbolWithDeferredType(Symbol symbol)
    {
        var links = _valueSymbolLinks.Get(symbol);
        if (links._resolvedType == null)
        {
            var deferred = _deferredSymbolLinks.Get(symbol);
            if ((deferred._parent!._flags & TypeFlags.Union) != 0)
            {
                links._resolvedType = GetUnionTypeWorker(deferred._constituents!);
            }
            else
            {
                links._resolvedType = GetIntersectionType(deferred._constituents!);
            }
        }

        return links._resolvedType!;
    }

    internal Type GetWriteTypeOfSymbolWithDeferredType(Symbol symbol)
    {
        var links = _valueSymbolLinks.Get(symbol);
        if (links._writeType == null)
        {
            var deferred = _deferredSymbolLinks.Get(symbol);
            if ((deferred._writeConstituents?.Length ?? 0) != 0)
            {
                if ((deferred._parent!._flags & TypeFlags.Union) != 0)
                {
                    links._writeType = GetUnionTypeWorker(deferred._writeConstituents!);
                }
                else
                {
                    links._writeType = GetIntersectionType(deferred._writeConstituents!);
                }
            }
            else
            {
                links._writeType = GetTypeOfSymbolWithDeferredType(symbol);
            }
        }

        return links._writeType!;
    }

    // Distinct write types come only from set accessors, but synthetic union and intersection
    // properties deriving from set accessors will either pre-compute or defer the union or
    // intersection of the writeTypes of their constituents.
    internal Type GetWriteTypeOfSymbol(Symbol symbol)
    {
        if ((symbol.CheckFlags & CheckFlags.SyntheticProperty) != 0)
        {
            if ((symbol.CheckFlags & CheckFlags.DeferredType) != 0)
            {
                return GetWriteTypeOfSymbolWithDeferredType(symbol);
            }
            var links = _valueSymbolLinks.Get(symbol);
            return OrElse(links._writeType, links._resolvedType)!;
        }
        if ((symbol.Flags & SymbolFlags.Property) != 0)
        {
            return RemoveMissingType(GetTypeOfSymbolWorker(symbol), (symbol.Flags & SymbolFlags.Optional) != 0);
        }
        if ((symbol.Flags & SymbolFlags.Accessor) != 0)
        {
            if ((symbol.CheckFlags & CheckFlags.Instantiated) != 0)
            {
                return GetWriteTypeOfInstantiatedSymbol(symbol);
            }
            return GetWriteTypeOfAccessors(symbol);
        }
        return GetTypeOfSymbolWorker(symbol);
    }

    public Type GetTypeOfSymbolAtLocation(Symbol symbol, Node? location)
    {
        symbol = GetExportSymbolOfValueSymbolIfExported(symbol)!;
        if (location != null)
        {
            // If we have an identifier or a property access at the given location, if the location is
            // an dotted name expression, and if the location is not an assignment target, obtain the type
            // of the expression (which will reflect control flow analysis). If the expression indeed
            // resolved to the given symbol, return the narrowed type.
            if ((IsIdentifier(location) || IsPrivateIdentifier(location)) &&
                !(IsJsxTagName(location) || IsJsxAttribute(location.Parent!) || IsJsxNamespacedName(location.Parent!)))
            {
                if (IsRightSideOfQualifiedNameOrPropertyAccess(location))
                {
                    location = location.Parent!;
                }
                if (IsExpressionNode(location) && (!IsAssignmentTarget(location) || IsWriteAccess(location)))
                {
                    Type t;
                    if (IsWriteAccess(location) && location.Kind == Kind.PropertyAccessExpression)
                    {
                        t = CheckPropertyAccessExpression(location, CheckMode.Normal, true /*writeOnly*/);
                    }
                    else
                    {
                        t = GetTypeOfExpression(location);
                    }
                    if (GetExportSymbolOfValueSymbolIfExported(_symbolNodeLinks.Get(location)._resolvedSymbol) == symbol)
                    {
                        return RemoveOptionalTypeMarker(t);
                    }
                }
            }
            if (IsDeclarationName(location) && IsSetAccessorDeclaration(location.Parent!) && GetAnnotatedAccessorTypeNode(location.Parent!) != null)
            {
                return GetWriteTypeOfAccessors(location.Parent!.Symbol!);
            }
            // The location isn't a reference to the given symbol, meaning we're being asked
            // a hypothetical question of what type the symbol would have if there was a reference
            // to it at the given location. Since we have no control flow information for the
            // hypothetical reference (control flow information is created and attached by the
            // binder), we simply return the declared type of the symbol.
            if (IsRightSideOfAccessExpression(location) && IsWriteAccess(location.Parent!))
            {
                return GetWriteTypeOfSymbol(symbol);
            }
        }
        return GetNonMissingTypeOfSymbolWorker(symbol);
    }

    internal Type GetTypeOfSymbolWorker(Symbol symbol)
    {
        if ((symbol.CheckFlags & CheckFlags.DeferredType) != 0)
        {
            return GetTypeOfSymbolWithDeferredType(symbol);
        }
        if ((symbol.CheckFlags & CheckFlags.Instantiated) != 0)
        {
            return GetTypeOfInstantiatedSymbol(symbol);
        }
        if ((symbol.CheckFlags & CheckFlags.Mapped) != 0)
        {
            return GetTypeOfMappedSymbol(symbol);
        }
        if ((symbol.CheckFlags & CheckFlags.ReverseMapped) != 0)
        {
            return GetTypeOfReverseMappedSymbol(symbol);
        }
        if ((symbol.Flags & SymbolFlags.Accessor) != 0)
        {
            return GetTypeOfAccessors(symbol);
        }
        if ((symbol.Flags & (SymbolFlags.Variable | SymbolFlags.Property)) != 0)
        {
            return GetTypeOfVariableOrParameterOrProperty(symbol);
        }
        if ((symbol.Flags & (SymbolFlags.Function | SymbolFlags.Method | SymbolFlags.Class | SymbolFlags.Enum | SymbolFlags.ValueModule)) != 0)
        {
            return GetTypeOfFuncClassEnumModule(symbol);
        }
        if ((symbol.Flags & SymbolFlags.EnumMember) != 0)
        {
            return GetTypeOfEnumMember(symbol);
        }
        if ((symbol.Flags & SymbolFlags.Alias) != 0)
        {
            return GetTypeOfAlias(symbol);
        }
        return _errorType!;
    }

    internal Type GetNonMissingTypeOfSymbolWorker(Symbol symbol)
    {
        return RemoveMissingType(GetTypeOfSymbolWorker(symbol), (symbol.Flags & SymbolFlags.Optional) != 0);
    }

    internal Type GetTypeOfInstantiatedSymbol(Symbol symbol)
    {
        var links = _valueSymbolLinks.Get(symbol);
        if (links._resolvedType == null)
        {
            links._resolvedType = InstantiateType(GetTypeOfSymbolWorker(links._target!), links._mapper);
        }
        return links._resolvedType!;
    }

    internal Type GetWriteTypeOfInstantiatedSymbol(Symbol symbol)
    {
        var links = _valueSymbolLinks.Get(symbol);
        if (links._writeType == null)
        {
            links._writeType = InstantiateType(GetWriteTypeOfSymbol(links._target!), links._mapper);
        }
        return links._writeType!;
    }

    internal Type GetTypeOfVariableOrParameterOrProperty(Symbol symbol)
    {
        var links = _valueSymbolLinks.Get(symbol);
        if (links._resolvedType == null)
        {
            var t = GetTypeOfVariableOrParameterOrPropertyWorker(symbol);
            if (t == null)
            {
                Debug.Fail("Unexpected nil type");
                throw new System.Diagnostics.UnreachableException();
            }
            // For a contextually typed parameter it is possible that a type has already
            // been assigned (in assignTypeToParameterAndFixTypeParameters), and we want
            // to preserve this type. In fact, we need to _prefer_ that type, but it won't
            // be assigned until contextual typing is complete, so we need to defer in
            // cases where contextual typing may take place.
            if (links._resolvedType == null && !IsParameterOfContextSensitiveSignature(symbol))
            {
                links._resolvedType = t;
            }
            return t;
        }
        return links._resolvedType!;
    }

    internal bool IsParameterOfContextSensitiveSignature(Symbol symbol)
    {
        var decl = symbol.ValueDeclaration;
        if (decl == null)
        {
            return false;
        }
        if (IsBindingElement(decl))
        {
            decl = WalkUpBindingElementsAndPatterns(decl);
        }
        if (IsParameterDeclaration(decl!))
        {
            return IsContextSensitiveFunctionOrObjectLiteralMethod(decl!.Parent!);
        }
        return false;
    }

    internal Type GetTypeOfVariableOrParameterOrPropertyWorker(Symbol symbol)
    {
        // Handle prototype property
        if ((symbol.Flags & SymbolFlags.Prototype) != 0)
        {
            return GetTypeOfPrototypeProperty(symbol);
        }
        // CommonsJS require and module both have type any.
        if (symbol == _requireSymbol)
        {
            return _anyType!;
        }
        Debug.Assert(symbol.ValueDeclaration != null);
        var declaration = symbol.ValueDeclaration!;
        if (IsSourceFile(declaration) && IsJsonSourceFile(declaration.AsSourceFile()!))
        {
            var statements = declaration.Statements() ?? [];
            if (statements.Length == 0)
            {
                return _emptyObjectType!;
            }
            return GetWidenedTypeWorker(GetWidenedLiteralTypeWorker(CheckExpression(statements[0].Expression()!)));
        }
        // Handle variable, parameter or property
        if (!PushTypeResolution(symbol, TypeSystemPropertyName.Type))
        {
            return ReportCircularityError(symbol);
        }
        if ((symbol.Flags & SymbolFlags.ModuleExports) != 0)
        {
            if (symbol.Name == "exports")
            {
                // Go: resolveExternalModuleSymbol 对非 nil 输入恒返回非 nil（checker.go:15873）→ `!` 只是让 C# 可空分析通过
                return GetTypeOfSymbolWorker(ResolveExternalModuleSymbolWorker(symbol.ValueDeclaration!.Symbol!, false /*dontResolveAlias*/)!);
            }
            return NewAnonymousType(symbol, symbol.Members, null, null, null);
        }
        Type result;
        switch (declaration.Kind)
        {
            case Kind.Parameter:
            case Kind.PropertyDeclaration:
            case Kind.PropertySignature:
            case Kind.VariableDeclaration:
            case Kind.BindingElement:
                result = GetWidenedTypeForVariableLikeDeclaration(declaration, !IsParameterOfContextSensitiveSignature(symbol)); // only report diagnostics for context-insensitive parameters - context-sensitive ones may have their type fixed to something else
                break;
            case Kind.PropertyAssignment:
                result = CheckPropertyAssignment(declaration, CheckMode.Normal);
                break;
            case Kind.ShorthandPropertyAssignment:
                result = CheckShorthandPropertyAssignment(declaration, true /*inDestructuringPattern*/, CheckMode.Normal);
                break;
            case Kind.MethodDeclaration:
                result = CheckObjectLiteralMethod(declaration, CheckMode.Normal);
                break;
            case Kind.ExportAssignment:
                if (declaration.Type() != null)
                {
                    result = GetTypeFromTypeNodeWorker(declaration.Type()!);
                }
                else
                {
                    result = WidenTypeForVariableLikeDeclaration(CheckExpressionCached(declaration.Expression()!), declaration, false /*reportErrors*/);
                }
                break;
            case Kind.BinaryExpression:
            case Kind.CallExpression:
                result = GetWidenedTypeForAssignmentDeclaration(symbol);
                break;
            case Kind.JsxAttribute:
                result = CheckJsxAttribute(declaration, CheckMode.Normal);
                break;
            case Kind.EnumMember:
                result = GetTypeOfEnumMember(symbol);
                break;
            default:
                Debug.Fail("Unhandled case in getTypeOfVariableOrParameterOrPropertyWorker: " + declaration.Kind.String());
                throw new System.Diagnostics.UnreachableException();
        }
        if (!PopTypeResolution())
        {
            return ReportCircularityError(symbol);
        }
        return result;
    }

    // Return the type associated with a variable, parameter, or property declaration. In the simple case this is the type
    // specified in a type annotation or inferred from an initializer. However, in the case of a destructuring declaration it
    // is a bit more involved. For example:
    //
    //	var [x, s = ""] = [1, "one"];
    //
    // Here, the array literal [1, "one"] is contextually typed by the type [any, string], which is the implied type of the
    // binding pattern [x, s = ""]. Because the contextual type is a tuple type, the resulting type of [1, "one"] is the
    // tuple type [number, string]. Thus, the type inferred for 'x' is number and the type inferred for 's' is string.
    internal Type GetWidenedTypeForVariableLikeDeclaration(Node declaration, bool reportErrors)
    {
        return WidenTypeForVariableLikeDeclaration(GetTypeForVariableLikeDeclaration(declaration /*includeOptionality*/, true, CheckMode.Normal), declaration, reportErrors);
    }

    // Return the inferred type for a variable, parameter, or property declaration
    internal Type? GetTypeForVariableLikeDeclaration(Node declaration, bool includeOptionality, CheckMode checkMode)
    {
        // A variable declared in a for..in statement is of type string, or of type keyof T when the
        // right hand expression is of a type parameter type.
        if (IsVariableDeclaration(declaration))
        {
            var grandParent = declaration.Parent!.Parent!;
            switch (grandParent.Kind)
            {
                case Kind.ForInStatement:
                    var indexType = GetIndexType(GetNonNullableTypeIfNeeded(CheckExpressionEx(grandParent.Expression()!, checkMode /*checkMode*/)));
                    if ((indexType._flags & (TypeFlags.TypeParameter | TypeFlags.Index)) != 0)
                    {
                        return GetExtractStringType(indexType);
                    }
                    return _stringType;
                case Kind.ForOfStatement:
                    // checkRightHandSideOfForOf will return undefined if the for-of expression type was
                    // missing properties/signatures required to get its iteratedType (like
                    // [Symbol.iterator] or next). This may be because we accessed properties from anyType,
                    // or it may have led to an error inside getElementTypeOfIterable.
                    return CheckRightHandSideOfForOf(grandParent);
            }
        }
        else if (IsBindingElement(declaration))
        {
            return GetTypeForBindingElement(declaration);
        }
        var isProperty = (IsPropertyDeclaration(declaration) && !HasAccessorModifier(declaration)) || IsPropertySignatureDeclaration(declaration);
        var isOptional = includeOptionality && IsOptionalDeclaration(declaration);
        // Use type from type annotation if one is present
        var declaredType = TryGetTypeFromTypeNode(declaration);
        if (IsCatchClauseVariableDeclarationOrBindingElement(declaration))
        {
            if (declaredType != null)
            {
                // If the catch clause is explicitly annotated with any or unknown, accept it, otherwise error.
                if ((declaredType._flags & TypeFlags.AnyOrUnknown) != 0)
                {
                    return declaredType;
                }
                return _errorType!;
            }
            // If the catch clause is not explicitly annotated, treat it as though it were explicitly
            // annotated with unknown or any, depending on useUnknownInCatchVariables.
            if (_useUnknownInCatchVariables)
            {
                return _unknownType;
            }
            else
            {
                return _anyType;
            }
        }
        if (declaredType != null)
        {
            return AddOptionalityEx(declaredType, isProperty, isOptional);
        }
        if (_noImplicitAny && IsVariableDeclaration(declaration) && !IsBindingPattern(declaration.Name()!) &&
            (GetCombinedModifierFlagsCached(declaration) & ModifierFlags.Export) == 0 && (declaration.Flags & NodeFlags.Ambient) == 0)
        {
            // If --noImplicitAny is on or the declaration is in a Javascript file,
            // use control flow tracked 'any' type for non-ambient, non-exported var or let variables with no
            // initializer or a 'null' or 'undefined' initializer.
            var initializer = declaration.Initializer();
            if ((GetCombinedNodeFlagsCached(declaration) & NodeFlags.Constant) == 0 && (initializer == null || IsNullOrUndefined(initializer!)))
            {
                return _autoType;
            }
            // Use control flow tracked 'any[]' type for non-ambient, non-exported variables with an empty array
            // literal initializer.
            if (initializer != null && IsEmptyArrayLiteral(initializer))
            {
                return _autoArrayType;
            }
        }
        if (IsParameterDeclaration(declaration))
        {
            var parameterSymbol = declaration.Symbol;
            if (parameterSymbol == null)
            {
                // parameters of function types defined in JSDoc in TS files don't have symbols
                return null;
            }
            var fn = declaration.Parent!;
            // For a parameter of a set accessor, use the type of the get accessor if one is present
            if (IsSetAccessorDeclaration(fn) && HasBindableName(fn))
            {
                var getter = GetDeclarationOfKind(GetSymbolOfDeclaration(declaration.Parent!), Kind.GetAccessor);
                if (getter != null)
                {
                    var getterSignature = GetSignatureFromDeclarationWorker(getter);
                    var thisParameter = GetAccessorThisParameter(fn);
                    if (thisParameter != null && declaration == thisParameter)
                    {
                        // Use the type from the *getter*
                        Debug.Assert(thisParameter.Type() == null);
                        return GetTypeOfSymbolWorker(getterSignature._thisParameter!);
                    }
                    return GetReturnTypeOfSignatureWorker(getterSignature);
                }
            }
            var fullSignatureParameterType = GetParameterTypeOfFullSignature(fn, declaration);
            if (fullSignatureParameterType != null)
            {
                return fullSignatureParameterType;
            }
            // Use contextual parameter type if one is available
            Type? t;
            if (parameterSymbol.Name == InternalSymbolNameThis)
            {
                t = GetContextualThisParameterType(fn);
            }
            else
            {
                t = GetContextuallyTypedParameterType(declaration);
            }
            if (t != null)
            {
                return AddOptionalityEx(t, false /*isProperty*/, isOptional);
            }
        }
        // Use the type of the initializer expression if one is present and the declaration is
        // not a parameter of a contextually typed function
        if (declaration.Initializer() != null)
        {
            var t = WidenTypeInferredFromInitializer(declaration, CheckDeclarationInitializer(declaration, checkMode, null /*contextualType*/));
            return AddOptionalityEx(t, isProperty, isOptional);
        }
        if (_noImplicitAny && IsPropertyDeclaration(declaration))
        {
            // We have a property declaration with no type annotation or initializer, in noImplicitAny mode or a .js file.
            // Use control flow analysis of this.xxx assignments in the constructor or static block to determine the type of the property.
            if (!HasStaticModifier(declaration))
            {
                var constructor = FindConstructorDeclaration(declaration.Parent!);
                Type? t;
                if (constructor != null)
                {
                    t = GetFlowTypeInConstructor(declaration.Symbol!, constructor);
                }
                else if ((declaration.ModifierFlags & ModifierFlags.Ambient) != 0)
                {
                    t = GetTypeOfPropertyInBaseClass(declaration.Symbol!);
                }
                else
                {
                    t = null;
                }
                if (t == null)
                {
                    return null;
                }
                return AddOptionalityEx(t, true /*isProperty*/, isOptional);
            }
            else
            {
                var staticBlocks = Filter(declaration.Parent!.Members(), IsClassStaticBlockDeclaration);
                Type? t;
                if (staticBlocks.Length != 0)
                {
                    t = GetFlowTypeInStaticBlocks(declaration.Symbol!, staticBlocks);
                }
                else if ((declaration.ModifierFlags & ModifierFlags.Ambient) != 0)
                {
                    t = GetTypeOfPropertyInBaseClass(declaration.Symbol!);
                }
                else
                {
                    t = null;
                }
                if (t == null)
                {
                    return null;
                }
                return AddOptionalityEx(t, true /*isProperty*/, isOptional);
            }
        }
        if (IsJsxAttribute(declaration))
        {
            // if JSX attribute doesn't have initializer, by default the attribute will have boolean value of true.
            // I.e <Elem attr /> is sugar for <Elem attr={true} />
            return _trueType;
        }
        // If the declaration specifies a binding pattern and is not a parameter of a contextually
        // typed function, use the type implied by the binding pattern
        if (IsBindingPattern(declaration.Name()!))
        {
            return GetTypeFromBindingPattern(declaration.Name()! /*includePatternInType*/, false /*reportErrors*/, true);
        }
        // No type specified and nothing can be inferred
        return null;
    }

    internal Type CheckDeclarationInitializer(Node declaration, CheckMode checkMode, Type? contextualType)
    {
        var initializer = declaration.Initializer()!;
        var t = GetQuickTypeOfExpression(initializer);
        if (t == null)
        {
            if (contextualType != null)
            {
                t = CheckExpressionWithContextualType(initializer, contextualType, null /*inferenceContext*/, checkMode);
            }
            else
            {
                t = CheckExpressionCachedEx(initializer, checkMode);
            }
        }
        if (IsParameterDeclaration(GetRootDeclaration(declaration)!))
        {
            var name = declaration.Name()!;
            switch (name.Kind)
            {
                case Kind.ObjectBindingPattern:
                    if (IsObjectLiteralType(t))
                    {
                        return PadObjectLiteralType(t, name);
                    }
                    break;
                case Kind.ArrayBindingPattern:
                    if (IsTupleType(t))
                    {
                        return PadTupleType(t, name);
                    }
                    break;
            }
        }
        return t;
    }

    internal Type PadObjectLiteralType(Type t, Node pattern)
    {
        var missingElements = new List<Node>();
        foreach (var e in pattern.Elements() ?? [])
        {
            if (HasDotDotDotToken(e))
            {
                continue;
            }
            var name = GetPropertyNameFromBindingElement(e);
            if (name != InternalSymbolNameMissing && GetPropertyOfTypeWorker(t, name) == null)
            {
                missingElements.Add(e);
            }
        }
        if (missingElements.Count == 0)
        {
            return t;
        }
        var members = new SymbolTable();
        foreach (var prop in GetPropertiesOfObjectType(t) ?? [])
        {
            members[prop.Name] = prop;
        }
        foreach (var e in missingElements)
        {
            var symbol = NewSymbol(SymbolFlags.Property | SymbolFlags.Optional, GetPropertyNameFromBindingElement(e));
            _valueSymbolLinks.Get(symbol)._resolvedType = GetTypeFromBindingElement(e, false /*includePatternInType*/, true /*reportErrors*/);
            members[symbol.Name] = symbol;
        }
        var result = NewAnonymousType(t._symbol!, members, null, null, GetIndexInfosOfTypeWorker(t));
        result._objectFlags = t._objectFlags;
        return result;
    }

    internal string GetPropertyNameFromBindingElement(Node e)
    {
        var exprType = GetLiteralTypeFromPropertyName(e.PropertyNameOrName()!);
        if (IsTypeUsableAsPropertyName(exprType))
        {
            return GetPropertyNameFromType(exprType);
        }
        return InternalSymbolNameMissing;
    }

    internal Type PadTupleType(Type t, Node pattern)
    {
        var patternElements = pattern.Elements() ?? [];
        if ((t.TargetTupleType()!._combinedFlags & ElementFlags.Variable) != 0 || GetTypeReferenceArity(t) >= patternElements.Length)
        {
            return t;
        }
        var elementTypes = new List<Type>(GetElementTypes(t) ?? []);
        var elementInfos = new List<TupleElementInfo>(t.TargetTupleType()!._elementInfos ?? []);
        for (var i = GetTypeReferenceArity(t); i < patternElements.Length; i++)
        {
            var e = patternElements[i];
            if (i < patternElements.Length - 1 || !(IsBindingElement(e) && HasDotDotDotToken(e)))
            {
                var elementType = _anyType!;
                if (!IsOmittedExpression(e) && HasDefaultValue(e))
                {
                    elementType = GetTypeFromBindingElement(e, false /*includePatternInType*/, false /*reportErrors*/);
                }
                elementTypes.Add(elementType);
                elementInfos.Add(new TupleElementInfo { _flags = ElementFlags.Optional });
                if (!IsOmittedExpression(e) && !HasDefaultValue(e))
                {
                    ReportImplicitAny(e, _anyType!, WideningKind.Normal);
                }
            }
        }
        return CreateTupleTypeEx(elementTypes.ToArray(), elementInfos.ToArray(), t.TargetTupleType()!._readonly);
    }

    internal Type WidenTypeInferredFromInitializer(Node declaration, Type t)
    {
        var widened = GetWidenedLiteralTypeForInitializer(declaration, t);
        if (IsInJSFile(declaration))
        {
            if (IsEmptyLiteralType(widened))
            {
                ReportImplicitAny(declaration, _anyType!, WideningKind.Normal);
                return _anyType!;
            }
            if (IsEmptyArrayLiteralType(widened))
            {
                ReportImplicitAny(declaration, _anyArrayType!, WideningKind.Normal);
                return _anyArrayType!;
            }
        }
        return widened;
    }

    internal Type GetWidenedLiteralTypeForInitializer(Node declaration, Type t)
    {
        if ((GetCombinedNodeFlagsCached(declaration) & NodeFlags.Constant) != 0 || IsDeclarationReadonly(declaration))
        {
            return t;
        }
        return GetWidenedLiteralTypeWorker(t);
    }

    internal Type GetTypeOfFuncClassEnumModule(Symbol symbol)
    {
        var links = _valueSymbolLinks.Get(symbol);
        if (links._resolvedType == null)
        {
            links._resolvedType = GetTypeOfFuncClassEnumModuleWorker(symbol);
        }
        return links._resolvedType!;
    }

    internal Type GetTypeOfFuncClassEnumModuleWorker(Symbol symbol)
    {
        if ((symbol.Flags & SymbolFlags.Module) != 0 && IsShorthandAmbientModuleSymbol(symbol))
        {
            return _anyType!;
        }
        else if ((symbol.Flags & SymbolFlags.ValueModule) != 0 && symbol.ValueDeclaration != null &&
            IsSourceFile(symbol.ValueDeclaration) && symbol.ValueDeclaration.AsSourceFile()!.CommonJSModuleIndicator != null)
        {
            // Go: resolveExternalModuleSymbol 对非 nil 输入恒返回非 nil（checker.go:15873）→ `!` 只是让 C# 可空分析通过
            var resolvedModule = ResolveExternalModuleSymbolWorker(symbol, false /*dontResolveAlias*/)!;
            if (resolvedModule != symbol)
            {
                return GetTypeOfSymbolWorker(resolvedModule);
            }
        }
        var t = NewObjectType(ObjectFlags.Anonymous, symbol);
        if ((symbol.Flags & SymbolFlags.Class) != 0)
        {
            var baseTypeVariable = GetBaseTypeVariableOfClass(symbol);
            if (baseTypeVariable != null)
            {
                return GetIntersectionType(new Type[] { t, baseTypeVariable });
            }
            return t;
        }
        if (_strictNullChecks && (symbol.Flags & SymbolFlags.Optional) != 0)
        {
            return GetOptionalType(t /*isProperty*/, true);
        }
        return t;
    }

    internal Type? GetBaseTypeVariableOfClass(Symbol symbol)
    {
        var baseConstructorType = GetBaseConstructorTypeOfClassWorker(GetDeclaredTypeOfClassOrInterface(symbol));
        if ((baseConstructorType._flags & TypeFlags.TypeVariable) != 0)
        {
            return baseConstructorType;
        }
        if ((baseConstructorType._flags & TypeFlags.Intersection) != 0)
        {
            return Find(baseConstructorType.Types(), t => (t._flags & TypeFlags.TypeVariable) != 0);
        }
        return null;
    }

    /// <summary>
    /// The base constructor of a class can resolve to
    /// * undefinedType if the class has no extends clause,
    /// * errorType if an error occurred during resolution of the extends expression,
    /// * nullType if the extends expression is the null value,
    /// * anyType if the extends expression has type any, or
    /// * an object type with at least one construct signature.
    /// </summary>
    internal Type GetBaseConstructorTypeOfClassWorker(Type t)
    {
        var data = t.AsInterfaceType()!;
        if (data._resolvedBaseConstructorType != null)
        {
            return data._resolvedBaseConstructorType;
        }
        var baseTypeNode = GetBaseTypeNodeOfClass(t);
        if (baseTypeNode == null)
        {
            data._resolvedBaseConstructorType = _undefinedType!;
            return data._resolvedBaseConstructorType;
        }
        if (!PushTypeResolution(t, TypeSystemPropertyName.ResolvedBaseConstructorType))
        {
            return _errorType!;
        }
        var baseConstructorType = CheckExpression(baseTypeNode.Expression()!);
        if ((baseConstructorType._flags & (TypeFlags.Object | TypeFlags.Intersection)) != 0)
        {
            // Resolving the members of a class requires us to resolve the base class of that class.
            // We force resolution here such that we catch circularities now.
            ResolveStructuredTypeMembers(baseConstructorType);
        }
        if (!PopTypeResolution())
        {
            Error(t._symbol!.ValueDeclaration, Diagnostics.X_0_is_referenced_directly_or_indirectly_in_its_own_base_expression, SymbolToStringWorker(t._symbol!));
            if (data._resolvedBaseConstructorType == null)
            {
                data._resolvedBaseConstructorType = _errorType!;
            }
            return data._resolvedBaseConstructorType;
        }
        if ((baseConstructorType._flags & TypeFlags.Any) == 0 && baseConstructorType != _nullWideningType && !IsConstructorType(baseConstructorType))
        {
            var err = Error(baseTypeNode.Expression()!, Diagnostics.Type_0_is_not_a_constructor_function_type, TypeToString(baseConstructorType));
            if ((baseConstructorType._flags & TypeFlags.TypeParameter) != 0)
            {
                var constraint = GetConstraintFromTypeParameter(baseConstructorType);
                var ctorReturn = _unknownType!;
                if (constraint != null)
                {
                    var ctorSigs = GetSignaturesOfTypeWorker(constraint, SignatureKind.Construct) ?? [];
                    if (ctorSigs.Length != 0)
                    {
                        ctorReturn = GetReturnTypeOfSignatureWorker(ctorSigs[0]);
                    }
                }
                if (baseConstructorType._symbol!.Declarations != null)
                {
                    err.AddRelatedInfo(CreateDiagnosticForNode(baseConstructorType._symbol!.Declarations![0], Diagnostics.Did_you_mean_for_0_to_be_constrained_to_type_new_args_Colon_any_1, SymbolToStringWorker(baseConstructorType._symbol!), TypeToString(ctorReturn)));
                }
            }
            if (data._resolvedBaseConstructorType == null)
            {
                data._resolvedBaseConstructorType = _errorType!;
            }
            return data._resolvedBaseConstructorType;
        }
        if (data._resolvedBaseConstructorType == null)
        {
            data._resolvedBaseConstructorType = baseConstructorType;
        }
        return data._resolvedBaseConstructorType;
    }

    internal bool IsFunctionType(Type t)
    {
        return (t._flags & TypeFlags.Object) != 0 && (GetSignaturesOfTypeWorker(t, SignatureKind.Call)?.Length ?? 0) > 0;
    }

    internal bool IsConstructorType(Type t)
    {
        if ((GetSignaturesOfTypeWorker(t, SignatureKind.Construct)?.Length ?? 0) > 0)
        {
            return true;
        }
        if ((t._flags & TypeFlags.TypeVariable) != 0)
        {
            var constraint = GetBaseConstraintOfTypeWorker(t);
            return constraint != null && IsMixinConstructorType(constraint);
        }
        return false;
    }

    // A type is a mixin constructor if it has a single construct signature taking no type parameters and a single
    // rest parameter of type any[].
    internal bool IsMixinConstructorType(Type t)
    {
        var signatures = GetSignaturesOfTypeWorker(t, SignatureKind.Construct) ?? [];
        if (signatures.Length == 1)
        {
            var s = signatures[0];
            if (s.TypeParameters().Length == 0 && s.Parameters().Length == 1 && SignatureHasRestParameter(s))
            {
                var paramType = GetTypeOfParameter(s.Parameters()[0]);
                return IsTypeAny(paramType) || GetElementTypeOfArrayTypeWorker(paramType) == _anyType;
            }
        }
        return false;
    }

    /// <summary>Go: 包级 <c>func signatureHasRestParameter(sig *Signature) bool</c>（checker.go:17360）。</summary>
    internal static bool SignatureHasRestParameter(Signature sig)
    {
        return (sig._flags & SignatureFlags.HasRestParameter) != 0;
    }

    internal Type GetTypeOfParameter(Symbol symbol)
    {
        var declaration = symbol.ValueDeclaration;
        return AddOptionalityEx(GetTypeOfSymbolWorker(symbol), false /*isProperty*/, declaration != null && (declaration.Initializer() != null || IsOptionalDeclaration(declaration)));
    }

    internal Type? GetConstraintOfType(Type t)
    {
        if ((t._flags & TypeFlags.TypeParameter) != 0)
        {
            return GetConstraintOfTypeParameterWorker(t);
        }
        if ((t._flags & TypeFlags.IndexedAccess) != 0)
        {
            return GetConstraintOfIndexedAccess(t);
        }
        if ((t._flags & TypeFlags.Conditional) != 0)
        {
            return GetConstraintOfConditionalType(t);
        }
        return GetBaseConstraintOfTypeWorker(t);
    }

    internal Type? GetConstraintOfTypeParameterWorker(Type typeParameter)
    {
        if (HasNonCircularBaseConstraint(typeParameter))
        {
            return GetConstraintFromTypeParameter(typeParameter);
        }
        return null;
    }

    internal bool HasNonCircularBaseConstraint(Type t)
    {
        return GetResolvedBaseConstraint(t, null) != _circularConstraintType;
    }

    // This is a worker function. Use getConstraintOfTypeParameter which guards against circular constraints
    internal Type? GetConstraintFromTypeParameter(Type t)
    {
        if ((t._flags & TypeFlags.TypeParameter) == 0)
        {
            return null;
        }

        var tp = t.AsTypeParameter()!;
        if (tp._constraint == null)
        {
            Type? constraint;
            if (tp._target != null)
            {
                constraint = InstantiateType(GetConstraintOfTypeParameterWorker(tp._target)!, tp._mapper);
            }
            else
            {
                var constraintDeclaration = GetConstraintDeclaration(t);
                if (constraintDeclaration != null)
                {
                    constraint = GetTypeFromTypeNodeWorker(constraintDeclaration);
                    if ((constraint._flags & TypeFlags.Any) != 0 && !IsErrorType(constraint))
                    {
                        // use stringNumberSymbolType as the base constraint for mapped type key constraints (unknown isn;t assignable to that, but `any` was),
                        // use unknown otherwise
                        if (IsMappedTypeNode(constraintDeclaration.Parent!.Parent!))
                        {
                            constraint = _stringNumberSymbolType!;
                        }
                        else
                        {
                            constraint = _unknownType!;
                        }
                    }
                }
                else
                {
                    constraint = GetInferredTypeParameterConstraint(t, false /*omitTypeReferences*/);
                }
            }
            if (constraint == null)
            {
                constraint = _noConstraintType!;
            }
            tp._constraint = constraint;
        }
        if (tp._constraint != _noConstraintType)
        {
            return tp._constraint;
        }
        return null;
    }

    internal Type GetConstraintOrUnknownFromTypeParameter(Type t)
    {
        var result = GetConstraintFromTypeParameter(t);
        return result != null ? result : _unknownType!;
    }

    internal Type? GetInferredTypeParameterConstraint(Type t, bool omitTypeReferences)
    {
        var inferences = new List<Type>();
        if (t._symbol != null && (t._symbol.Declarations?.Length ?? 0) != 0)
        {
            foreach (var declaration in t._symbol.Declarations!)
            {
                if (IsInferTypeNode(declaration.Parent!))
                {
                    // When an 'infer T' declaration is immediately contained in a type reference node
                    // (such as 'Foo<infer T>'), T's constraint is inferred from the constraint of the
                    // corresponding type parameter in 'Foo'. When multiple 'infer T' declarations are
                    // present, we form an intersection of the inferred constraint types.
                    var child = declaration.Parent!;
                    var parent = child.Parent;
                    while (parent != null && IsParenthesizedTypeNode(parent))
                    {
                        child = parent;
                        parent = child.Parent;
                    }
                    if (IsTypeReferenceNode(parent!) && !omitTypeReferences)
                    {
                        var typeParameters = GetTypeParametersForTypeReferenceOrImport(parent!);
                        if (typeParameters != null)
                        {
                            var index = FindIndex(parent!.TypeArguments(), n => n == child);
                            if (index >= 0 && index < typeParameters.Length)
                            {
                                var declaredConstraint = GetConstraintOfTypeParameterWorker(typeParameters[index]);
                                if (declaredConstraint != null)
                                {
                                    // Type parameter constraints can reference other type parameters so
                                    // constraints need to be instantiated. If instantiation produces the
                                    // type parameter itself, we discard that inference. For example, in
                                    //   type Foo<T extends string, U extends T> = [T, U];
                                    //   type Bar<T> = T extends Foo<infer X, infer X> ? Foo<X, X> : T;
                                    // the instantiated constraint for U is X, so we discard that inference.
                                    var mapper = NewDeferredTypeMapper(typeParameters, MapIndex<Type, Func<Type>>(typeParameters, (_, mappedIndex) => () => GetEffectiveTypeArgumentAtIndex(parent!, typeParameters, mappedIndex)));
                                    var constraint = InstantiateType(declaredConstraint, mapper);
                                    if (constraint != t)
                                    {
                                        inferences.Add(constraint);
                                    }
                                }
                            }
                        }
                    }
                    else if (IsParameterDeclaration(parent!) && parent!.AsParameterDeclaration()!.DotDotDotToken != null ||
                        IsRestTypeNode(parent!) ||
                        IsNamedTupleMember(parent!) && parent!.AsNamedTupleMember()!.DotDotDotToken != null)
                    {
                        inferences.Add(CreateArrayType(_unknownType!));
                    }
                    else if (IsTemplateLiteralTypeSpan(parent!))
                    {
                        inferences.Add(_stringType!);
                    }
                    else if (IsTypeParameterDeclaration(parent!) && IsMappedTypeNode(parent!.Parent!))
                    {
                        inferences.Add(_stringNumberSymbolType!);
                    }
                    else if (IsMappedTypeNode(parent!) && parent!.Type() != null &&
                        SkipParentheses(parent!.Type()) == declaration.Parent &&
                        IsConditionalTypeNode(parent!.Parent!) &&
                        parent!.Parent!.AsConditionalTypeNode()!.ExtendsType == parent &&
                        IsMappedTypeNode(parent!.Parent!.AsConditionalTypeNode()!.CheckType!) &&
                        parent!.Parent!.AsConditionalTypeNode()!.CheckType!.Type() != null)
                    {
                        var checkMappedType = parent!.Parent!.AsConditionalTypeNode()!.CheckType!;
                        var nodeType = GetTypeFromTypeNodeWorker(checkMappedType.Type()!);
                        var checkMappedTypeParameter = checkMappedType.AsMappedTypeNode()!.TypeParameter!;
                        var mapper = NewSimpleTypeMapper(GetDeclaredTypeOfTypeParameter(GetSymbolOfDeclaration(checkMappedTypeParameter)!),
                            IfElse(checkMappedTypeParameter.AsTypeParameterDeclaration()!.Constraint != null,
                                GetTypeFromTypeNodeWorker(checkMappedTypeParameter.AsTypeParameterDeclaration()!.Constraint!),
                                _stringNumberSymbolType!));
                        inferences.Add(InstantiateType(nodeType, mapper));
                    }
                }
            }
        }
        if (inferences.Count != 0)
        {
            return GetIntersectionType(inferences.ToArray());
        }
        return null;
    }

    internal Type[]? GetTypeParametersForTypeReferenceOrImport(Node node)
    {
        var t = GetTypeFromTypeNodeWorker(node);
        if (!IsErrorType(t))
        {
            var symbol = GetResolvedSymbolOrNil(node);
            if (symbol != null)
            {
                return GetTypeParametersForTypeAndSymbol(t, symbol);
            }
        }
        return null;
    }

    internal Type[]? GetTypeParametersForTypeAndSymbol(Type t, Symbol symbol)
    {
        if (!IsErrorType(t))
        {
            if ((symbol.Flags & SymbolFlags.TypeAlias) != 0)
            {
                var typeParameters = _typeAliasLinks.Get(symbol)._typeParameters;
                if ((typeParameters?.Length ?? 0) != 0)
                {
                    return typeParameters;
                }
            }
            if ((t._objectFlags & ObjectFlags.Reference) != 0)
            {
                return t.Target()!.AsInterfaceType()!.LocalTypeParameters();
            }
        }
        return null;
    }

    internal Type GetEffectiveTypeArgumentAtIndex(Node node, Type[] typeParameters, int index)
    {
        var typeArguments = node.TypeArguments();
        if (typeArguments != null && index < typeArguments.Length)
        {
            return GetTypeFromTypeNodeWorker(typeArguments[index]);
        }
        return GetEffectiveTypeArguments(node, typeParameters)[index];
    }

    internal Type? GetConstraintOfIndexedAccess(Type t)
    {
        if (HasNonCircularBaseConstraint(t))
        {
            return GetConstraintFromIndexedAccess(t);
        }
        return null;
    }

    internal Type? GetConstraintFromIndexedAccess(Type t)
    {
        var d = t.AsIndexedAccessType()!;
        if (IsMappedTypeGenericIndexedAccess(t))
        {
            // For indexed access types of the form { [P in K]: E }[X], where K is non-generic and X is generic,
            // we substitute an instantiation of E where P is replaced with X.
            return SubstituteIndexedMappedType(d._objectType!, d._indexType!);
        }
        var indexConstraint = GetSimplifiedTypeOrConstraint(d._indexType!);
        if (indexConstraint != null && indexConstraint != d._indexType)
        {
            var indexedAccess = GetIndexedAccessTypeOrUndefined(d._objectType!, indexConstraint, d._accessFlags, null, null);
            if (indexedAccess != null)
            {
                return indexedAccess;
            }
        }
        var objectConstraint = GetSimplifiedTypeOrConstraint(d._objectType!);
        if (objectConstraint != null && objectConstraint != d._objectType)
        {
            return GetIndexedAccessTypeOrUndefined(objectConstraint, d._indexType!, d._accessFlags, null, null);
        }
        return null;
    }

    internal Type? GetConstraintOfConditionalType(Type t)
    {
        if (HasNonCircularBaseConstraint(t))
        {
            return GetConstraintFromConditionalType(t);
        }
        return null;
    }

    internal Type? GetConstraintFromConditionalType(Type t)
    {
        var constraint = GetConstraintOfDistributiveConditionalType(t);
        if (constraint != null)
        {
            return constraint;
        }
        return GetDefaultConstraintOfConditionalType(t);
    }

    internal Type GetDefaultConstraintOfConditionalType(Type t)
    {
        var d = t.AsConditionalType()!;
        if (d._resolvedDefaultConstraint == null)
        {
            // An `any` branch of a conditional type would normally be viral - specifically, without special handling here,
            // a conditional type with a single branch of type `any` would be assignable to anything, since it's constraint would simplify to
            // just `any`. This result is _usually_ unwanted - so instead here we elide an `any` branch from the constraint type,
            // in effect treating `any` like `never` rather than `unknown` in this location.
            var trueConstraint = GetInferredTrueTypeFromConditionalType(t);
            var falseConstraint = GetFalseTypeFromConditionalType(t);
            if (IsTypeAny(trueConstraint))
            {
                d._resolvedDefaultConstraint = falseConstraint;
            }
            else if (IsTypeAny(falseConstraint))
            {
                d._resolvedDefaultConstraint = trueConstraint;
            }
            else
            {
                d._resolvedDefaultConstraint = GetUnionTypeWorker(new Type[] { trueConstraint, falseConstraint });
            }
        }
        return d._resolvedDefaultConstraint!;
    }

    internal Type? GetConstraintOfDistributiveConditionalType(Type t)
    {
        var d = t.AsConditionalType()!;
        if (d._resolvedConstraintOfDistributive == null)
        {
            // Check if we have a conditional type of the form 'T extends U ? X : Y', where T is a constrained
            // type parameter. If so, create an instantiation of the conditional type where T is replaced
            // with its constraint. We do this because if the constraint is a union type it will be distributed
            // over the conditional type and possibly reduced. For example, 'T extends undefined ? never : T'
            // removes 'undefined' from T.
            // We skip returning a distributive constraint for a restrictive instantiation of a conditional type
            // as the constraint for all type params (check type included) have been replace with `unknown`, which
            // is going to produce even more false positive/negative results than the distribute constraint already does.
            // Please note: the distributive constraint is a kludge for emulating what a negated type could to do filter
            // a union - once negated types exist and are applied to the conditional false branch, this "constraint"
            // likely doesn't need to exist.
            if (d._root!._isDistributive && _cachedTypes.GetValueOrDefault(new CachedTypeKey(CachedTypeKind.RestrictiveInstantiation, t._id)) != t)
            {
                var constraint = GetSimplifiedType(d._checkType!, false /*writing*/);
                if (constraint == d._checkType)
                {
                    constraint = GetConstraintOfType(constraint!);
                }
                if (constraint != null && constraint != d._checkType)
                {
                    var instantiated = GetConditionalTypeInstantiation(t, PrependTypeMapping(d._root!._checkType!, constraint, d._mapper), true /*forConstraint*/, null);
                    if ((instantiated._flags & TypeFlags.Never) == 0)
                    {
                        d._resolvedConstraintOfDistributive = instantiated;
                        return instantiated;
                    }
                }
            }
            d._resolvedConstraintOfDistributive = _noConstraintType!;
        }
        if (d._resolvedConstraintOfDistributive != _noConstraintType)
        {
            return d._resolvedConstraintOfDistributive;
        }
        return null;
    }

    internal Type GetDeclaredTypeOfClassOrInterface(Symbol symbol)
    {
        var links = _declaredTypeLinks.Get(symbol);
        if (links._declaredType == null)
        {
            var kind = IfElse((symbol.Flags & SymbolFlags.Class) != 0, ObjectFlags.Class, ObjectFlags.Interface);
            var t = NewObjectType(kind, symbol);
            links._declaredType = t;
            var outerTypeParameters = GetOuterTypeParametersOfClassOrInterface(symbol);
            var typeParameters = AppendLocalTypeParametersOfClassOrInterfaceOrTypeAlias(outerTypeParameters, symbol);
            // A class or interface is generic if it has type parameters or a "this" type. We always give classes a "this" type
            // because it is not feasible to analyze all members to determine if the "this" type escapes the class (in particular,
            // property types inferred from initializers and method return types inferred from return statements are very hard
            // to exhaustively analyze). We give interfaces a "this" type if we can't definitely determine that they are free of
            // "this" references.
            if (typeParameters != null || kind == ObjectFlags.Class || !IsThislessInterface(symbol))
            {
                t._objectFlags |= ObjectFlags.Reference;
                var d = t.AsInterfaceType()!;
                d._thisType = NewTypeParameter(symbol);
                d._thisType.AsTypeParameter()!._isThisType = true;
                d._thisType.AsTypeParameter()!._constraint = t;
                d._allTypeParameters = Concatenate(typeParameters, new Type[] { d._thisType! });
                d._outerTypeParameterCount = outerTypeParameters?.Length ?? 0;
                d._resolvedTypeArguments = d.TypeParameters();
                d._instantiations = new Dictionary<CacheHashKey, Type>();
                d._instantiations[GetTypeListKey(d._resolvedTypeArguments!)] = t;
                d._target = t;
            }
        }
        return links._declaredType!;
    }

    /// <summary>
    /// Returns true if the interface given by the symbol is free of "this" references.
    ///
    /// Specifically, the result is true if the interface itself contains no references
    /// to "this" in its body, if all base types are interfaces,
    /// and if none of the base interfaces have a "this" type.
    /// </summary>
    internal bool IsThislessInterface(Symbol symbol)
    {
        foreach (var declaration in symbol.Declarations ?? [])
        {
            if (IsInterfaceDeclaration(declaration))
            {
                if ((declaration.Flags & NodeFlags.ContainsThis) != 0)
                {
                    return false;
                }
                var baseTypeNodes = GetExtendsHeritageClauseElements(declaration);
                foreach (var node in baseTypeNodes)
                {
                    var name = GetHeritageClauseElementName(node);
                    if (IsEntityName(name) || IsEntityNameExpression(name))
                    {
                        var baseSymbol = ResolveEntityName(name!, SymbolFlags.Type, true /*ignoreErrors*/, false, null);
                        if (baseSymbol == null || (baseSymbol.Flags & SymbolFlags.Interface) == 0 || GetDeclaredTypeOfClassOrInterface(baseSymbol).AsInterfaceType()!._thisType != null)
                        {
                            return false;
                        }
                    }
                }
            }
        }
        return true;
    }
}
