// ported from: internal/checker/checker.go:28491-29322（getSingleBaseForNonAugmentingSubtype / getModifiersTypeFromMappedType /
//   extractTypesOfKind / getRegularTypeOfObjectLiteral / transformTypeOfMembers / markLinkedReferences 及别名标记族 /
//   getPromisedTypeOfPromise 前的装饰器元数据族 / markTypeNodeAsReferenced）
//
// 与 Go 的差异（逐条，一行一条理由；其余逐行对应，含注释与分支顺序）：
// - 类形状：`public sealed partial class Checker`；Go 的包级函数（无接收者）→ 本类的 `internal static` 成员，名字 PascalCase
//   （`isExportOrExportExpression` → `IsExportOrExportExpression`、`getEntityNameFromTypeNode` → `GetEntityNameFromTypeNode` 等）。
// - 字段命名：Go 未导出字段 → `_camelCase`（Main 终局口径，docs/architecture.md §2 + checker-plan §5.5）：
//   `c.cachedTypes` → `_cachedTypes`、`c.sourceFileLinks` → `_sourceFileLinks`、`c.unknownType` → `_unknownType`、
//   `links.requestedExternalEmitHelpers` → `links._requestedExternalEmitHelpers`（links 是 types.go 的未导出字段结构体）。
// - 跨片 `Worker` 后缀（Main 的 artifacts/checker-method-collisions.md，导出名保持不变、未导出者加后缀）：
//   `getBaseTypes` → `GetBaseTypesWorker`、`getTypeArguments` → `GetTypeArgumentsWorker`、`getTypeOfSymbol` → `GetTypeOfSymbolWorker`、
//   `getTypeFromTypeNode` → `GetTypeFromTypeNodeWorker`、`getConstraintOfTypeParameter` → `GetConstraintOfTypeParameterWorker`、
//   `getResolvedSymbol` → `GetResolvedSymbolWorker`、`getApparentType` → `GetApparentTypeWorker`、`getWidenedType` → `GetWidenedTypeWorker`、
//   `getPropertyOfType` → `GetPropertyOfTypeWorker`、`getJsxNamespace` → `GetJsxNamespaceWorker`、`getTypeOnlyAliasDeclaration` →
//   `GetTypeOnlyAliasDeclarationWorker`、`resolveAlias` → `ResolveAliasWorker`、`getSymbolFlags` → `GetSymbolFlagsWorker`、
//   `getExportsOfModule` → `GetExportsOfModuleWorker`。
// - Go 的别名类型参数（`*ast.IdentifierNode`/`*ast.TypeNode`/`*ast.ParameterDeclarationNode`/`*ast.ExportSpecifierNode` 在
//   ast_generated.go 里都是 `= Node`）在 C# 里一律写成 `Node`（避免无意义的强制转换）。
// - 闭包参数不能与外围局部/参数重名（C# CS0136）：Go 里 `func(t *Type) bool { return t.flags&kind != 0 }` 内的 `t`
//   会遮蔽方法参数，改成 `u =>`（`extractTypesOfKind`/`filterType` 调用点）。
// - Go 把实例方法当函数值传递（`c.getRegularTypeOfObjectLiteral`）→ C# 用 lambda `x => GetRegularTypeOfObjectLiteral(x)`
//   （方法组对委托的可空性不匹配会 CS8622；docs/checker-plan.md §5.4）。
// - `foreach` 的迭代变量不可赋值：`transformTypeOfMembers` 里 Go 重绑 `property`，C# 用局部 `member` 代替。
// - Go 的 nil 切片 range（不迭代）→ `X ?? []`（`ctor.Parameters() ?? []`、`rootSymbol.Declarations ?? []`）；
//   `len(nil) == 0` → `(...?.Length ?? 0) == 0`（`Declarations.Nodes`）。
// - map 读（缺键得 nil）→ `GetValueOrDefault(key)`（`Dictionary` 索引器会抛 KeyNotFoundException）。
// - `panic(...)`/`debug.Fail(...)` → `Debug.Fail(...)`；其后控制流需要时补 `throw new System.Diagnostics.UnreachableException()`。
// - Go 的 `&^=`/`&^` → `&= ~(...)`/`& ~(...)`；位测试一律加括号（`&` 优先级低于 `==`）。
// - `core.X` → `using static TypeScript.Core.Core` 后的裸名（`IfElse`/`FirstOrNil`/`Some`/`Map`/`Every`/`FindIndex`/
//   `ReplaceElement`/`LastOrNil`）；`ExternalEmitHelpersFirstEmitHelper` 等 iota 成员按「去类型前缀」写成
//   `ExternalEmitHelpers.FirstEmitHelper`（types.go 定义）。
// - 枚举移位：C# 枚举不支持 `<<=` → `helper = (ExternalEmitHelpers)((uint)helper << 1)`；`(uncheckedHelpers & helper) == 0`
//   依赖 C# 允许枚举与字面量 0 比较。
// - `CachedTypeKey` 是 C1 定义的 class（Checker.Keys.cs），用 2 参构造器 `new CachedTypeKey(kind, typeId)`。
namespace TypeScript.Checker;

using System;
using TypeScript.Ast;
using TypeScript.Core;
using TypeScript.Diagnostics;
using static TypeScript.Ast.Ast;
using static TypeScript.Core.Core;
using static TypeScript.Diagnostics.Diagnostics;

public sealed partial class Checker
{
    internal Type? GetSingleBaseForNonAugmentingSubtype(Type t)
    {
        if ((t._objectFlags & ObjectFlags.Reference) == 0 || (t.Target()!._objectFlags & ObjectFlags.ClassOrInterface) == 0)
        {
            return null;
        }
        var key = new CachedTypeKey(CachedTypeKind.EquivalentBaseType, t._id);
        if ((t._objectFlags & ObjectFlags.IdenticalBaseTypeCalculated) != 0)
        {
            return _cachedTypes.GetValueOrDefault(key);
        }
        t._objectFlags |= ObjectFlags.IdenticalBaseTypeCalculated;
        var target = t.Target()!;
        if ((target._objectFlags & ObjectFlags.Class) != 0)
        {
            var baseTypeNode = GetBaseTypeNodeOfClass(target);
            // A base type expression may circularly reference the class itself (e.g. as an argument to function call), so we only
            // check for base types specified as simple qualified names.
            if (baseTypeNode != null && !IsIdentifier(baseTypeNode.Expression()!) && !IsPropertyAccessExpression(baseTypeNode.Expression()!))
            {
                return null;
            }
        }
        var bases = GetBaseTypesWorker(target) ?? [];
        if (bases.Length != 1)
        {
            return null;
        }
        if (GetMembersOfSymbol(t._symbol!).Count != 0)
        {
            // If the interface has any members, they may subtype members in the base, so we should do a full structural comparison
            return null;
        }
        Type instantiatedBase;
        var typeParameters = target.AsInterfaceType()!.TypeParameters();
        if (typeParameters.Length == 0)
        {
            instantiatedBase = bases[0];
        }
        else
        {
            instantiatedBase = InstantiateType(bases[0], NewTypeMapper(typeParameters, GetTypeArgumentsWorker(t).AsSpan(0, typeParameters.Length).ToArray()));
        }
        if (GetTypeArgumentsWorker(t).Length > typeParameters.Length)
        {
            instantiatedBase = GetTypeWithThisArgument(instantiatedBase, LastOrNil(GetTypeArgumentsWorker(t)), false);
        }
        _cachedTypes[key] = instantiatedBase;
        return instantiatedBase;
    }

    internal Type GetModifiersTypeFromMappedType(Type t)
    {
        var m = t.AsMappedType()!;
        if (m._modifiersType == null)
        {
            if (IsMappedTypeWithKeyofConstraintDeclaration(t))
            {
                // If the constraint declaration is a 'keyof T' node, the modifiers type is T. We check
                // AST nodes here because, when T is a non-generic type, the logic below eagerly resolves
                // 'keyof T' to a literal union type and we can't recover T from that type.
                m._modifiersType = InstantiateType(GetTypeFromTypeNodeWorker(GetConstraintDeclarationForMappedType(t)!.Type()!), m._mapper);
            }
            else
            {
                // Otherwise, get the declared constraint type, and if the constraint type is a type parameter,
                // get the constraint of that type parameter. If the resulting type is an indexed type 'keyof T',
                // the modifiers type is T. Otherwise, the modifiers type is unknown.
                var declaredType = GetTypeFromMappedTypeNode(m._declaration!);
                var constraint = GetConstraintTypeFromMappedType(declaredType);
                var extendedConstraint = constraint;
                if (constraint != null && (constraint._flags & TypeFlags.TypeParameter) != 0)
                {
                    extendedConstraint = GetConstraintOfTypeParameterWorker(GetNonDistributedTypeParameter(constraint));
                }
                if (extendedConstraint != null && (extendedConstraint._flags & TypeFlags.Index) != 0)
                {
                    m._modifiersType = InstantiateType(extendedConstraint.AsIndexType()!._target!, m._mapper);
                }
                else
                {
                    m._modifiersType = _unknownType;
                }
            }
        }
        return m._modifiersType;
    }

    internal Type ExtractTypesOfKind(Type t, TypeFlags kind)
    {
        return FilterType(t, u => (u._flags & kind) != 0);
    }

    internal Type GetRegularTypeOfObjectLiteral(Type t)
    {
        if (!(IsObjectLiteralType(t) && (t._objectFlags & ObjectFlags.FreshLiteral) != 0))
        {
            return t;
        }
        var key = new CachedTypeKey(CachedTypeKind.RegularObjectLiteral, t._id);
        var cached = _cachedTypes.GetValueOrDefault(key);
        if (cached != null)
        {
            return cached;
        }
        var resolved = ResolveStructuredTypeMembers(t);
        var members = TransformTypeOfMembers(t, x => GetRegularTypeOfObjectLiteral(x));
        var regular = NewAnonymousType(t._symbol!, members, resolved.CallSignatures(), resolved.ConstructSignatures(), resolved._indexInfos);
        regular._flags = resolved._flags;
        regular._objectFlags |= resolved._objectFlags & ~ObjectFlags.FreshLiteral;
        _cachedTypes[key] = regular;
        return regular;
    }

    internal SymbolTable TransformTypeOfMembers(Type t, Func<Type, Type> f)
    {
        var members = new SymbolTable();
        foreach (var property in GetPropertiesOfObjectType(t) ?? [])
        {
            var original = GetTypeOfSymbolWorker(property);
            var updated = f(original);
            var member = property;
            if (updated != original)
            {
                member = CreateSymbolWithType(property, updated);
            }
            members[member.Name] = member;
        }
        return members;
    }

    internal void MarkLinkedReferences(Node location, ReferenceHint hint, Symbol? propSymbol, Type? parentType)
    {
        if (!_canCollectSymbolAliasAccessibilityData)
        {
            return;
        }
        if ((location.Flags & NodeFlags.Ambient) != 0 && !IsPropertySignatureDeclaration(location) && !IsPropertyDeclaration(location))
        {
            // References within types and declaration files are never going to contribute to retaining a JS import,
            // except for properties (which can be decorated).
            return;
        }
        switch (hint)
        {
            case ReferenceHint.Identifier:
                MarkIdentifierAliasReferenced(location);
                break;
            case ReferenceHint.Property:
                MarkPropertyAliasReferenced(location, propSymbol, parentType);
                break;
            case ReferenceHint.ExportAssignment:
                MarkExportAssignmentAliasReferenced(location);
                break;
            case ReferenceHint.Jsx:
                MarkJsxAliasReferenced(location);
                break;
            case ReferenceHint.ExportImportEquals:
                MarkImportEqualsAliasReferenced(location);
                break;
            case ReferenceHint.ExportSpecifier:
                MarkExportSpecifierAliasReferenced(location);
                break;
            case ReferenceHint.Decorator:
                MarkDecoratorAliasReferenced(location);
                break;
            case ReferenceHint.Unspecified:
                if ((location.Flags & NodeFlags.InWithStatement) != 0)
                {
                    // We cannot answer semantic questions within a with block, do not proceed any further
                    return;
                }
                if (IsJsxTagName(location) && IsJsxIntrinsicTagName(location))
                {
                    return; // builtin JSX tag names aren't real type refs by most metrics, but are expressions, so must be filtered
                }
                if (IsIdentifier(location))
                {
                    // A shorthand property with an object-assignment-initializer (e.g. `{ s = 5 }`) is only valid inside a
                    // destructuring assignment target. When it appears in an ordinary object literal expression, the checker
                    // checks the initializer and never resolves the property name, so resolving it here would report a spurious
                    // "No value exists in scope for the shorthand property" diagnostic. Skip such names to match checking.
                    var parent = location.Parent;
                    if (IsShorthandPropertyAssignment(parent!) &&
                        parent!.Name() == location &&
                        parent.AsShorthandPropertyAssignment()!.ObjectAssignmentInitializer != null &&
                        !IsAssignmentTarget(parent.Parent))
                    {
                        return;
                    }
                    var res = FindManyAncestors(
                        location,
                        n => IsMetaProperty(n),
                        n => IsDecorator(n),
                        n => IsForInOrOfStatement(n),
                        n => IsComputedPropertyName(n),
                        n => IsHeritageClause(n));
                    var metaProperty = res[0];
                    var decorator = res[1];
                    var forNode = res[2];
                    var computedName = res[3];
                    var heritageClause = res[4];
                    if (metaProperty != null)
                    {
                        return; // identifiers in meta properties shouldn't be resolved, but are expressions, so must be filtered
                    }
                    if (decorator != null)
                    {
                        // Decorators on nodes that cannot be decorated (e.g. class expressions, static blocks,
                        // `this` parameters) are never resolved during normal checking, so resolving them here would
                        // report spurious diagnostics. Only bail out for such invalid-position decorators; valid
                        // decorator expressions must still be resolved and marked for emit.
                        var decorated = decorator.Parent;
                        if (decorated != null && !NodeCanBeDecorated(_legacyDecorators, decorated, decorated.Parent, decorated.Parent!.Parent))
                        {
                            return;
                        }
                    }
                    // The right-hand side of a 'for-in'/'for-of' statement whose initializer is an empty variable
                    // declaration list (a grammar error, e.g. `for (var of X)`) is never checked, because the RHS
                    // is only checked while inferring the type of a variable declaration and there is none here.
                    // Resolving identifiers in the RHS here would report spurious diagnostics.
                    if (forNode != null)
                    {
                        var data = forNode.AsForInOrOfStatement()!;
                        if (IsVariableDeclarationList(data.Initializer!) &&
                            (data.Initializer!.AsVariableDeclarationList()!.Declarations!.Nodes?.Length ?? 0) == 0 &&
                            data.Expression != null &&
                            (location == data.Expression || IsNodeDescendantOf(location, data.Expression)))
                        {
                            return;
                        }
                    }
                    // Computed property names on enum members are a grammar error and are never checked
                    // (checkEnumMember only checks the member initializer, not the name), so resolving
                    // identifiers in them here would report a spurious "Cannot find name" diagnostic.
                    if (computedName != null)
                    {
                        if (IsEnumMember(computedName.Parent!))
                        {
                            return;
                        }
                        if (IsInvalidComputedPropertyName(computedName))
                        {
                            return;
                        }
                    }
                    if (heritageClause != null)
                    {
                        // extends heritage clauses on interfaces are not expressions and are unchecked if they are
                        if (IsInterfaceDeclaration(heritageClause.Parent!))
                        {
                            return;
                        }
                        // On a class, only the first `extends` type is resolved as a value (the base class); any
                        // additional `extends` types are grammar errors (e.g. `class C extends A extends B` or
                        // `class C extends A, B`) and are never resolved during checking.
                        if (IsClassLike(heritageClause.Parent) &&
                            heritageClause.AsHeritageClause()!.Token == Kind.ExtendsKeyword)
                        {
                            var firstExtends = GetClassExtendsHeritageElement(heritageClause.Parent);
                            if (firstExtends != null &&
                                location != firstExtends && !IsNodeDescendantOf(location, firstExtends))
                            {
                                return;
                            }
                        }
                    }
                    // Identifiers in expression contexts are emitted, so we need to follow their referenced aliases and mark them as used
                    // Some non-expression identifiers are also treated as expression identifiers for this purpose, eg, `a` in `b = {a}` or `q` in `import r = q`
                    // This is the exception, rather than the rule - most non-expression identifiers are declaration names.
                    if ((IsExpressionNode(location) ||
                        IsShorthandPropertyAssignment(location.Parent!)) &&
                        ShouldMarkIdentifierAliasReferenced(location))
                    {
                        if (IsPropertyAccessOrQualifiedName(location.Parent))
                        {
                            Node? left;
                            if (IsPropertyAccessExpression(location.Parent!))
                            {
                                left = location.Parent!.Expression();
                            }
                            else
                            {
                                left = location.Parent!.AsQualifiedName()!.Left;
                            }
                            if (left != location)
                            {
                                return; // Only mark the LHS (the RHS is a property lookup)
                            }
                        }
                        MarkIdentifierAliasReferenced(location);
                        return;
                    }
                }
                if (IsPropertyAccessOrQualifiedName(location))
                {
                    Node? topProp = location;
                    while (IsPropertyAccessOrQualifiedName(topProp))
                    {
                        if (IsPartOfTypeNode(topProp))
                        {
                            return;
                        }
                        topProp = topProp!.Parent;
                    }
                    MarkPropertyAliasReferenced(location, null /*propSymbol*/, null /*parentType*/);
                    return;
                }
                if (IsExportAssignment(location))
                {
                    MarkExportAssignmentAliasReferenced(location);
                    return;
                }
                if (IsJsxOpeningLikeElement(location) || IsJsxOpeningFragment(location))
                {
                    MarkJsxAliasReferenced(location);
                    return;
                }
                if (IsImportEqualsDeclaration(location))
                {
                    if (IsInternalModuleImportEqualsDeclaration(location) || CheckExternalImportOrExportDeclaration(location))
                    {
                        MarkImportEqualsAliasReferenced(location);
                        return;
                    }
                    return;
                }
                if (IsExportSpecifier(location))
                {
                    MarkExportSpecifierAliasReferenced(location);
                    return;
                }
                if (!_compilerOptions.EmitDecoratorMetadata.IsTrue())
                {
                    return;
                }
                if (!CanHaveDecorators(location) || !HasDecorators(location) || location.Modifiers() == null || !NodeCanBeDecorated(_legacyDecorators, location, location.Parent, location.Parent!.Parent))
                {
                    return;
                }

                MarkDecoratorAliasReferenced(location);
                break;
            default:
                Debug.Fail("Unhandled reference hint");
                break;
        }
    }

    internal static bool IsExportOrExportExpression(Node location)
    {
        return FindAncestor(location, n =>
        {
            var parent = n.Parent;
            if (parent != null)
            {
                if (IsAnyExportAssignment(parent))
                {
                    return parent.Expression() == n && IsEntityNameExpression(n);
                }
                if (IsExportSpecifier(parent))
                {
                    return parent.AsExportSpecifier()!.Name() == n || parent.PropertyName() == n;
                }
            }
            return false;
        }) != null;
    }

    internal static bool ShouldMarkIdentifierAliasReferenced(Node node)
    {
        var parent = node.Parent;
        if (parent != null)
        {
            // A property access expression LHS? checkPropertyAccessExpression will handle that.
            if (IsPropertyAccessExpression(parent) && parent.Expression() == node)
            {
                return false;
            }
            // Next two check for an identifier inside a type only export.
            if (IsExportSpecifier(parent) && parent.IsTypeOnly())
            {
                return false;
            }
            if (parent.Parent != null)
            {
                var greatGrandparent = parent.Parent.Parent;
                if (greatGrandparent != null && IsExportDeclaration(greatGrandparent) && greatGrandparent.IsTypeOnly())
                {
                    return false;
                }
            }
        }
        return true;
    }

    internal static bool IsInternalModuleImportEqualsDeclaration(Node node)
    {
        return node.Kind == Kind.ImportEqualsDeclaration &&
            node.AsImportEqualsDeclaration()!.ModuleReference!.Kind != Kind.ExternalModuleReference;
    }

    internal void MarkIdentifierAliasReferenced(Node location)
    {
        if (IsThisInTypeQuery(location))
        {
            return;
        }
        var symbol = GetResolvedSymbolWorker(location);
        if (symbol != null && symbol != _argumentsSymbol && symbol != _unknownSymbol)
        {
            MarkAliasReferenced(symbol, location);
        }
    }

    internal void MarkPropertyAliasReferenced(Node location /*PropertyAccessExpression | QualifiedName*/, Symbol? propSymbol, Type? parentType)
    {
        if (IsPartOfImportEqualsModuleReference(location))
        {
            return;
        }
        Node? left;
        if (IsPropertyAccessExpression(location))
        {
            left = location.Expression();
        }
        else
        {
            left = location.AsQualifiedName()!.Left;
        }
        if (IsThisIdentifier(left!) || !IsIdentifier(left!))
        {
            return;
        }
        var parentSymbol = GetResolvedSymbolWorker(left!);
        if (parentSymbol == null || parentSymbol == _unknownSymbol)
        {
            return;
        }
        // In `Foo.Bar.Baz`, 'Foo' is not referenced if 'Bar' is a const enum or a module containing only const enums.
        // `Foo` is also not referenced in `enum FooCopy { Bar = Foo.Bar }`, because the enum member value gets inlined
        // here even if `Foo` is not a const enum.
        //
        // The exceptions are:
        //   1. if 'isolatedModules' is enabled, because the const enum value will not be inlined, and
        //   2. if 'preserveConstEnums' is enabled and the expression is itself an export, e.g. `export = Foo.Bar.Baz`.
        //
        // The property lookup is deferred as much as possible, in as many situations as possible, to avoid alias marking
        // pulling on types/symbols it doesn't strictly need to.
        if (_compilerOptions.GetIsolatedModules() || (_compilerOptions.ShouldPreserveConstEnums() && IsExportOrExportExpression(location)))
        {
            MarkAliasReferenced(parentSymbol, location);
            return;
        }
        // Hereafter, this relies on type checking - but every check prior to this only used symbol information
        var leftType = parentType;
        if (leftType == null)
        {
            leftType = CheckExpressionCached(left!);
        }
        if (IsTypeAny(leftType) || leftType == _silentNeverType)
        {
            MarkAliasReferenced(parentSymbol, location);
            return;
        }
        var prop = propSymbol;
        if (prop == null && parentType == null)
        {
            Node? right;
            if (IsPropertyAccessExpression(location))
            {
                right = location.AsPropertyAccessExpression()!.Name();
            }
            else
            {
                right = location.AsQualifiedName()!.Right;
            }
            Symbol? lexicallyScopedSymbol = null;
            if (IsPrivateIdentifier(right!))
            {
                lexicallyScopedSymbol = LookupSymbolForPrivateIdentifierDeclaration(right!.Text(), right);
            }
            var assignmentKind = GetAssignmentTargetKind(location);
            Type apparentType;
            if (assignmentKind != AssignmentKind.None || IsMethodAccessForCall(location))
            {
                apparentType = GetApparentTypeWorker(GetWidenedTypeWorker(leftType));
            }
            else
            {
                apparentType = GetApparentTypeWorker(leftType);
            }
            if (IsPrivateIdentifier(right!))
            {
                if (lexicallyScopedSymbol != null)
                {
                    prop = GetPrivateIdentifierPropertyOfType(apparentType, lexicallyScopedSymbol);
                }
            }
            else
            {
                prop = GetPropertyOfTypeWorker(apparentType, right!.Text());
            }
        }
        if (!(prop != null && (IsConstEnumOrConstEnumOnlyModule(prop) || (prop.Flags & SymbolFlags.EnumMember) != 0 && location.Parent!.Kind == Kind.EnumMember)))
        {
            MarkAliasReferenced(parentSymbol, location);
        }
    }

    internal static bool IsPartOfImportEqualsModuleReference(Node location)
    {
        var importEquals = FindAncestorKind(location, Kind.ImportEqualsDeclaration);
        if (importEquals == null)
        {
            return false;
        }
        for (var node = location; node != null && node != importEquals; node = node.Parent)
        {
            if (node == importEquals.AsImportEqualsDeclaration()!.ModuleReference)
            {
                return true;
            }
        }
        return false;
    }

    internal void MarkExportAssignmentAliasReferenced(Node location /*ExportAssignment*/)
    {
        var id = location.Expression();
        if (IsIdentifier(id!))
        {
            var sym = GetExportSymbolOfValueSymbolIfExported(ResolveEntityName(id!, SymbolFlags.All, true /*ignoreErrors*/, true /*dontResolveAlias*/, location));
            if (sym != null)
            {
                MarkAliasReferenced(sym, id!);
            }
        }
    }

    internal void MarkJsxAliasReferenced(Node node /*JsxOpeningLikeElement | JsxOpeningFragment*/)
    {
        if (GetJsxNamespaceContainerForImplicitImport(node) != null)
        {
            return;
        }
        // The reactNamespace/jsxFactory's root symbol should be marked as 'used' so we don't incorrectly elide its import.
        // And if there is no reactNamespace/jsxFactory's symbol in scope when targeting React emit, we should issue an error.
        var jsxFactoryRefErr = IfElse(_compilerOptions.Jsx == JsxEmit.React, This_JSX_tag_requires_0_to_be_in_scope_but_it_could_not_be_found, (Message?)null);
        var jsxFactoryNamespace = GetJsxNamespaceWorker(node);
        var jsxFactoryLocation = node;
        if (IsJsxOpeningLikeElement(node))
        {
            jsxFactoryLocation = node.TagName()!;
        }
        var shouldFactoryRefErr = _compilerOptions.Jsx != JsxEmit.Preserve && _compilerOptions.Jsx != JsxEmit.ReactNative;
        // #38720/60122, allow null as jsxFragmentFactory
        Symbol? jsxFactorySym = null;
        if (!(IsJsxOpeningFragment(node) && jsxFactoryNamespace == "null"))
        {
            var flags = SymbolFlags.Value;
            if (!shouldFactoryRefErr)
            {
                flags &= ~SymbolFlags.Enum;
            }
            jsxFactorySym = _resolveName(jsxFactoryLocation, jsxFactoryNamespace, flags, jsxFactoryRefErr, true /*isUse*/, false /*excludeGlobals*/);
        }
        if (jsxFactorySym != null)
        {
            // Mark local symbol as referenced here because it might not have been marked
            // if jsx emit was not jsxFactory as there wont be error being emitted
            SymbolReferenced(jsxFactorySym, SymbolFlags.All);
            // If react/jsxFactory symbol is alias, mark it as referenced
            if (_canCollectSymbolAliasAccessibilityData && (jsxFactorySym.Flags & SymbolFlags.Alias) != 0 && GetTypeOnlyAliasDeclarationWorker(jsxFactorySym) == null)
            {
                MarkAliasSymbolAsReferenced(jsxFactorySym);
            }
        }
        // if JsxFragment, additionally mark jsx pragma as referenced, since `getJsxNamespace` above would have resolved to only the fragment factory if they are distinct
        if (IsJsxOpeningFragment(node))
        {
            var file = GetSourceFileOfNode(node);
            var entity = GetJsxFactoryEntity(file);
            if (entity != null)
            {
                var localJsxNamespace = GetFirstIdentifier(entity)!.Text();
                var flags = SymbolFlags.Value;
                if (!shouldFactoryRefErr)
                {
                    flags &= ~SymbolFlags.Enum;
                }
                _resolveName(jsxFactoryLocation, localJsxNamespace, flags, jsxFactoryRefErr, true /*isUse*/, false /*excludeGlobals*/);
            }
        }
    }

    internal void MarkImportEqualsAliasReferenced(Node location /*ImportEqualsDeclaration*/)
    {
        if (HasSyntacticModifier(location, ModifierFlags.Export))
        {
            MarkExportAsReferenced(location);
        }
    }

    internal void MarkExportSpecifierAliasReferenced(Node location /*ExportSpecifierNode*/)
    {
        if (location.Parent!.Parent!.ModuleSpecifier() == null && !location.IsTypeOnly() && !location.Parent.Parent.IsTypeOnly())
        {
            var exportedName = location.PropertyNameOrName()!;
            if (exportedName.Kind == Kind.StringLiteral)
            {
                return; // Skip for invalid syntax like this: export { "x" }
            }
            var symbol = _resolveName(exportedName, exportedName.Text(), SymbolFlags.Value | SymbolFlags.Type | SymbolFlags.Namespace | SymbolFlags.Alias, null /*nameNotFoundMessage*/, true /*isUse*/, false /*excludeGlobals*/);
            if (symbol != null && (symbol == _undefinedSymbol || symbol == _globalThisSymbol || symbol.Declarations != null && IsGlobalSourceFile(GetDeclarationContainer(symbol.Declarations[0]))))
            {
                // Do nothing, non-local symbol
            }
            else
            {
                var target = symbol;
                if (target != null && (target.Flags & SymbolFlags.Alias) != 0)
                {
                    target = ResolveAliasWorker(target);
                }
                if (target == null || (GetSymbolFlagsWorker(target) & SymbolFlags.Value) != 0)
                {
                    MarkExportAsReferenced(location);            // marks export as used
                    MarkIdentifierAliasReferenced(exportedName); // marks target of export as used
                }
            }
        }
    }

    internal void CheckExternalEmitHelpers(Node location, ExternalEmitHelpers helpers)
    {
        if (!_compilerOptions.ImportHelpers.IsTrue())
        {
            return;
        }
        var sourceFile = GetSourceFileOfNode(location);
        if (!IsEffectiveExternalModule(sourceFile, _compilerOptions) || (location.Flags & NodeFlags.Ambient) != 0)
        {
            return;
        }
        var helpersModule = ResolveHelpersModule(sourceFile!, location);
        if (helpersModule == _unknownSymbol)
        {
            return;
        }
        var links = _sourceFileLinks.Get(sourceFile!);
        if ((links._requestedExternalEmitHelpers & helpers) != helpers)
        {
            var uncheckedHelpers = helpers & ~links._requestedExternalEmitHelpers;
            for (var helper = ExternalEmitHelpers.FirstEmitHelper; helper <= ExternalEmitHelpers.LastEmitHelper; helper = (ExternalEmitHelpers)((uint)helper << 1))
            {
                if ((uncheckedHelpers & helper) == 0)
                {
                    continue;
                }
                foreach (var name in GetHelperNames(helper))
                {
                    var symbol = ResolveSymbol(GetSymbol(GetExportsOfModuleWorker(helpersModule), name, SymbolFlags.Value));
                    if (symbol == null)
                    {
                        Error(location, This_syntax_requires_an_imported_helper_named_1_which_does_not_exist_in_0_Consider_upgrading_your_version_of_0, ExternalHelpersModuleNameText, name);
                    }
                    else if ((helper & ExternalEmitHelpers.ClassPrivateFieldGet) != 0)
                    {
                        if (!HasSignatureWithArityGreaterThan(symbol, 3))
                        {
                            Error(location, This_syntax_requires_an_imported_helper_named_1_with_2_parameters_which_is_not_compatible_with_the_one_in_0_Consider_upgrading_your_version_of_0, ExternalHelpersModuleNameText, name, 4);
                        }
                    }
                    else if ((helper & ExternalEmitHelpers.ClassPrivateFieldSet) != 0)
                    {
                        if (!HasSignatureWithArityGreaterThan(symbol, 4))
                        {
                            Error(location, This_syntax_requires_an_imported_helper_named_1_with_2_parameters_which_is_not_compatible_with_the_one_in_0_Consider_upgrading_your_version_of_0, ExternalHelpersModuleNameText, name, 5);
                        }
                    }
                }
            }
        }
        links._requestedExternalEmitHelpers |= helpers;
    }

    internal bool HasSignatureWithArityGreaterThan(Symbol symbol, int arity)
    {
        foreach (var signature in GetSignaturesOfSymbol(symbol))
        {
            if (GetParameterCount(signature) > arity)
            {
                return true;
            }
        }
        return false;
    }

    internal string[] GetHelperNames(ExternalEmitHelpers helper)
    {
        switch (helper)
        {
            case ExternalEmitHelpers.Rest:
                return new[] { "__rest" };
            case ExternalEmitHelpers.Decorate:
                if (_legacyDecorators)
                {
                    return new[] { "__decorate" };
                }
                return new[] { "__esDecorate", "__runInitializers" };
            case ExternalEmitHelpers.Metadata:
                return new[] { "__metadata" };
            case ExternalEmitHelpers.Param:
                return new[] { "__param" };
            case ExternalEmitHelpers.Awaiter:
                return new[] { "__awaiter" };
            case ExternalEmitHelpers.Await:
                return new[] { "__await" };
            case ExternalEmitHelpers.AsyncGenerator:
                return new[] { "__asyncGenerator" };
            case ExternalEmitHelpers.AsyncDelegator:
                return new[] { "__asyncDelegator" };
            case ExternalEmitHelpers.AsyncValues:
                return new[] { "__asyncValues" };
            case ExternalEmitHelpers.ExportStar:
                return new[] { "__exportStar" };
            case ExternalEmitHelpers.ImportStar:
                return new[] { "__importStar" };
            case ExternalEmitHelpers.ImportDefault:
                return new[] { "__importDefault" };
            case ExternalEmitHelpers.MakeTemplateObject:
                return new[] { "__makeTemplateObject" };
            case ExternalEmitHelpers.ClassPrivateFieldGet:
                return new[] { "__classPrivateFieldGet" };
            case ExternalEmitHelpers.ClassPrivateFieldSet:
                return new[] { "__classPrivateFieldSet" };
            case ExternalEmitHelpers.ClassPrivateFieldIn:
                return new[] { "__classPrivateFieldIn" };
            case ExternalEmitHelpers.SetFunctionName:
                return new[] { "__setFunctionName" };
            case ExternalEmitHelpers.PropKey:
                return new[] { "__propKey" };
            case ExternalEmitHelpers.AddDisposableResourceAndDisposeResources:
                return new[] { "__addDisposableResource", "__disposeResources" };
            case ExternalEmitHelpers.RewriteRelativeImportExtension:
                return new[] { "__rewriteRelativeImportExtension" };
            default:
                Debug.Fail("Unrecognized helper");
                return [];
        }
    }

    internal Symbol ResolveHelpersModule(SourceFile file, Node errorNode)
    {
        var links = _sourceFileLinks.Get(file);
        if (links._externalHelpersModule == null)
        {
            var location = _program.GetImportHelpersImportSpecifier(file.Path());
            var helpersModule = ResolveExternalModule(location!, ExternalHelpersModuleNameText, This_syntax_requires_an_imported_helper_but_module_0_cannot_be_found, errorNode, false /*isForAugmentation*/, null /*importAttributesType*/);
            if (helpersModule == null)
            {
                helpersModule = _unknownSymbol;
            }
            links._externalHelpersModule = helpersModule;
        }
        return links._externalHelpersModule;
    }

    internal void MarkDecoratorAliasReferenced(Node node /*HasDecorators*/)
    {
        if (_compilerOptions.EmitDecoratorMetadata.IsFalseOrUnknown())
        {
            return;
        }
        var firstDecorator = FirstOrNil(node.Decorators());
        if (firstDecorator == null)
        {
            return;
        }

        CheckExternalEmitHelpers(firstDecorator, ExternalEmitHelpers.Metadata);

        // we only need to perform these checks if we are emitting serialized type metadata for the target of a decorator.
        switch (node.Kind)
        {
            case Kind.ClassDeclaration:
                {
                    var ctor = GetFirstConstructorWithBody(node);
                    if (ctor != null)
                    {
                        foreach (var p in ctor.Parameters() ?? [])
                        {
                            MarkDecoratorMedataDataTypeNodeAsReferenced(GetParameterTypeNodeForDecoratorCheck(p));
                        }
                    }
                    break;
                }
            case Kind.GetAccessor:
            case Kind.SetAccessor:
                {
                    var otherKind = Kind.SetAccessor;
                    if (node.Kind == Kind.SetAccessor)
                    {
                        otherKind = Kind.GetAccessor;
                    }
                    var otherAccessor = GetDeclarationOfKind(GetSymbolOfDeclaration(node), otherKind);
                    var annotation = GetAnnotatedAccessorTypeNode(node);
                    if (annotation == null && otherAccessor != null)
                    {
                        annotation = GetAnnotatedAccessorTypeNode(otherAccessor);
                    }
                    MarkDecoratorMedataDataTypeNodeAsReferenced(annotation);
                    break;
                }
            case Kind.MethodDeclaration:
                {
                    foreach (var p in node.Parameters() ?? [])
                    {
                        MarkDecoratorMedataDataTypeNodeAsReferenced(GetParameterTypeNodeForDecoratorCheck(p));
                    }
                    MarkDecoratorMedataDataTypeNodeAsReferenced(node.Type());
                    break;
                }
            case Kind.PropertyDeclaration:
                MarkDecoratorMedataDataTypeNodeAsReferenced(node.Type());
                break;
            case Kind.Parameter:
                {
                    MarkDecoratorMedataDataTypeNodeAsReferenced(GetParameterTypeNodeForDecoratorCheck(node));
                    var containingSignature = node.Parent!;
                    foreach (var p in containingSignature.Parameters() ?? [])
                    {
                        MarkDecoratorMedataDataTypeNodeAsReferenced(GetParameterTypeNodeForDecoratorCheck(p));
                    }
                    MarkDecoratorMedataDataTypeNodeAsReferenced(containingSignature.Type());
                    break;
                }
        }
    }

    internal Node? GetParameterTypeNodeForDecoratorCheck(Node node)
    {
        var typeNode = node.Type();
        if (node.AsParameterDeclaration()!.DotDotDotToken != null)
        {
            return GetRestParameterElementType(typeNode);
        }
        return typeNode;
    }

    internal void MarkDecoratorMedataDataTypeNodeAsReferenced(Node? node /*TypeNode*/)
    {
        var entityName = GetEntityNameForDecoratorMetadata(node);
        if (entityName != null && IsEntityName(entityName))
        {
            MarkEntityNameOrEntityExpressionAsReference(entityName, true);
        }
    }

    internal Node? GetEntityNameForDecoratorMetadata(Node? node)
    {
        if (node == null)
        {
            return node;
        }
        switch (node.Kind)
        {
            case Kind.IntersectionType:
                return GetEntityNameForDecoratorMetadataFromTypeList(node.AsIntersectionTypeNode()!.Types!.Nodes!);
            case Kind.UnionType:
                return GetEntityNameForDecoratorMetadataFromTypeList(node.AsUnionTypeNode()!.Types!.Nodes!);
            case Kind.ConditionalType:
                return GetEntityNameForDecoratorMetadataFromTypeList(new[] { node.AsConditionalTypeNode()!.TrueType!, node.AsConditionalTypeNode()!.FalseType! });
            case Kind.ParenthesizedType:
                return GetEntityNameForDecoratorMetadata(node.AsParenthesizedTypeNode()!.Type);
            case Kind.NamedTupleMember:
                return GetEntityNameForDecoratorMetadata(node.AsNamedTupleMember()!.Type);
            case Kind.TypeReference:
                return node.AsTypeReferenceNode()!.TypeName;
        }
        return null;
    }

    internal Node? GetEntityNameForDecoratorMetadataFromTypeList(Node[] typeNodes)
    {
        Node? commonEntityName = null;
        foreach (var typeNode in typeNodes)
        {
            if (typeNode.Kind == Kind.NeverKeyword)
            {
                continue; // Always elide `never` from the union/intersection if possible
            }
            if (!_strictNullChecks && (typeNode.Kind == Kind.LiteralType && typeNode.AsLiteralTypeNode()!.Literal!.Kind == Kind.NullKeyword || typeNode.Kind == Kind.UndefinedKeyword))
            {
                continue; // Elide null and undefined from unions for metadata, just like what we did prior to the implementation of strict null checks
            }
            var individualEntityName = GetEntityNameForDecoratorMetadata(typeNode);
            if (individualEntityName == null)
            {
                // Individual is something like string number
                // So it would be serialized to either that type or object
                // Safe to return here
                return null;
            }

            if (commonEntityName == null)
            {
                commonEntityName = individualEntityName;
            }
            else
            {
                // Note this is in sync with the transformation that happens for type node.
                // Keep this in sync with serializeUnionOrIntersectionType
                // Verify if they refer to same entity and is identifier
                // return undefined if they dont match because we would emit object
                if (!IsIdentifier(commonEntityName) || !IsIdentifier(individualEntityName) || commonEntityName.AsIdentifier()!.Text != individualEntityName.AsIdentifier()!.Text)
                {
                    return null;
                }
            }
        }
        return commonEntityName;
    }

    internal void MarkAliasReferenced(Symbol symbol, Node location)
    {
        if (!_canCollectSymbolAliasAccessibilityData)
        {
            return;
        }
        if (IsNonLocalAlias(symbol, SymbolFlags.Value /*excludes*/) && !IsInTypeQuery(location))
        {
            var target = ResolveAliasWorker(symbol);
            if ((GetSymbolFlagsEx(symbol, true /*excludeTypeOnlyMeanings*/, false /*excludeLocalMeanings*/) & (SymbolFlags.Value | SymbolFlags.ExportValue)) != 0)
            {
                // An alias resolving to a const enum cannot be elided if (1) 'isolatedModules' is enabled
                // (because the const enum value will not be inlined), or if (2) the alias is an export
                // of a const enum declaration that will be preserved.
                if (_compilerOptions.GetIsolatedModules() ||
                    _compilerOptions.ShouldPreserveConstEnums() && IsExportOrExportExpression(location) ||
                    !IsConstEnumOrConstEnumOnlyModule(GetExportSymbolOfValueSymbolIfExported(target)))
                {
                    MarkAliasSymbolAsReferenced(symbol);
                }
            }
        }
    }

    // When an alias symbol is referenced, we need to mark the entity it references as referenced and in turn repeat that until
    // we reach a non-alias or an exported entity (which is always considered referenced). We do this by checking the target of
    // the alias as an expression (which recursively takes us back here if the target references another alias).
    internal void MarkAliasSymbolAsReferenced(Symbol symbol)
    {
        var links = _aliasSymbolLinks.Get(symbol);
        if (!links._referenced)
        {
            links._referenced = true;
            var node = GetDeclarationOfAliasSymbol(symbol);
            if (node == null)
            {
                Debug.Fail("Unexpected nil in markAliasSymbolAsReferenced");
                throw new System.Diagnostics.UnreachableException();
            }
            // We defer checking of the reference of an `import =` until the import itself is referenced,
            // This way a chain of imports can be elided if ultimately the final input is only used in a type
            // position.
            if (IsImportEqualsDeclaration(node) && node.AsImportEqualsDeclaration()!.ModuleReference!.Kind != Kind.ExternalModuleReference)
            {
                if ((GetSymbolFlagsWorker(ResolveSymbol(symbol)!) & SymbolFlags.Value) != 0)
                {
                    // import foo = <symbol>
                    var left = GetFirstIdentifier(node.AsImportEqualsDeclaration()!.ModuleReference!);
                    MarkIdentifierAliasReferenced(left!);
                }
            }
        }
    }

    internal void MarkExportAsReferenced(Node node /*ImportEqualsDeclaration | ExportSpecifier*/)
    {
        var symbol = GetSymbolOfDeclaration(node)!;
        var target = ResolveAliasWorker(symbol);
        if (target != null)
        {
            var markAlias = target == _unknownSymbol ||
                (((GetSymbolFlagsEx(symbol, true /*excludeTypeOnlyMeanings*/, false /*excludeLocalMeanings*/) & SymbolFlags.Value) != 0) && !IsConstEnumOrConstEnumOnlyModule(target));
            if (markAlias)
            {
                MarkAliasSymbolAsReferenced(symbol);
            }
        }
    }

    internal void MarkEntityNameOrEntityExpressionAsReference(Node? typeName /*EntityNameOrEntityNameExpression | nil*/, bool forDecoratorMetadata)
    {
        if (typeName == null)
        {
            return;
        }

        var rootName = GetFirstIdentifier(typeName);
        var meaning = IfElse(typeName.Kind == Kind.Identifier, SymbolFlags.Type, SymbolFlags.Namespace) | SymbolFlags.Alias;
        var rootSymbol = _resolveName(rootName, rootName!.Text(), meaning, null /*nameNotFoundMessage*/, true /*isUse*/, false /*excludeGlobals*/);

        if (rootSymbol != null && (rootSymbol.Flags & SymbolFlags.Alias) != 0)
        {
            if (_canCollectSymbolAliasAccessibilityData &&
                SymbolIsValue(rootSymbol) &&
                !IsConstEnumOrConstEnumOnlyModule(ResolveAliasWorker(rootSymbol)) &&
                GetTypeOnlyAliasDeclarationWorker(rootSymbol) == null)
            {
                MarkAliasSymbolAsReferenced(rootSymbol);
            }
            else if (forDecoratorMetadata &&
                _compilerOptions.GetIsolatedModules() &&
                _compilerOptions.GetEmitModuleKind() >= ModuleKind.ES2015 &&
                !SymbolIsValue(rootSymbol) &&
                !Some(rootSymbol.Declarations, IsTypeOnlyImportOrExportDeclaration))
            {
                var diag = Error(typeName, A_type_referenced_in_a_decorated_signature_must_be_imported_with_import_type_or_a_namespace_import_when_isolatedModules_and_emitDecoratorMetadata_are_enabled);
                Node? aliasDeclaration = null;
                foreach (var decl in rootSymbol.Declarations ?? [])
                {
                    if (IsAliasSymbolDeclaration(decl))
                    {
                        aliasDeclaration = decl;
                        break;
                    }
                }
                if (aliasDeclaration != null)
                {
                    diag.SetRelatedInfo(new[] { CreateDiagnosticForNode(aliasDeclaration, X_0_was_imported_here, rootName!.Text()) });
                }
            }
        }
    }

    internal static Node? GetEntityNameFromTypeNode(Node node)
    {
        switch (node.Kind)
        {
            case Kind.TypeReference:
                return node.AsTypeReferenceNode()!.TypeName;

            case Kind.ExpressionWithTypeArguments:
                if (IsEntityNameExpression(node.Expression()))
                {
                    return node.Expression();
                }
                return null;

            // These aren't valid TypeNodes, but we treat them as such because of `isPartOfTypeNode`, which returns `true` for things that aren't `TypeNode`s.
            case Kind.Identifier:
            case Kind.QualifiedName:
                return node;
        }

        return null;
    }

    // If a TypeNode can be resolved to a value symbol imported from an external module, it is
    // marked as referenced to prevent import elision.
    internal void MarkTypeNodeAsReferenced(Node? node /*TypeNode*/)
    {
        if (node != null)
        {
            MarkEntityNameOrEntityExpressionAsReference(GetEntityNameFromTypeNode(node), false /*forDecoratorMetadata*/);
        }
    }
}
