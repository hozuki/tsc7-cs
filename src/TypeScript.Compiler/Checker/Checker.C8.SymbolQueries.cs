// ported from: internal/checker/checker.go:31989-32581
//
// 本文件是 checker 的对外公共查询 API（GetSymbolAtLocation / GetTypeAtLocation / GetEmitResolver /
// GetAliasedSymbol）及其全部支撑函数。
//
// 与 Go 的差异（逐条，一行一条理由；其余逐行对应，含注释与分支顺序）：
// - 类形状：Go 的 `func (c *Checker) x()` → 同一个 `public sealed partial class Checker` 的实例方法；
//   Go 的包级函数（无接收者）→ 该类 `internal static`/`public static` 成员（docs/checker-plan §5.2/§5.5）。
// - Go 未导出字段 → `_camelCase`（§5.5）：`c.errorType`→`_errorType`、`c.unknownSymbol`→`_unknownSymbol`、
//   `c.argumentsSymbol`→`_argumentsSymbol`、`c.globalThisType`→`_globalThisType`、
//   `c.anyBaseTypeIndexInfo`→`_anyBaseTypeIndexInfo`、`c.symbolNodeLinks`→`_symbolNodeLinks`、
//   `c.valueSymbolLinks`→`_valueSymbolLinks`、`c.cachedArgumentsReferenced`→`_cachedArgumentsReferenced`、
//   `c.emitResolver`→`_emitResolver`、`t.flags`→`t._flags`、`t.objectFlags`→`t._objectFlags`、
//   `t.symbol`→`t._symbol`、`links.resolvedSymbol`→`links._resolvedSymbol`、`info.declaration`→`info._declaration`、
//   `info.keyType`/`valueType`/`indexSymbol`→`info._keyType`/`_valueType`/`_indexSymbol`、
//   `sig.thisParameter`→`sig._thisParameter`、`classType.AsInterfaceType().thisType`→`!._thisType`、
//   `t.AsIntersectionType().types`→`t.AsIntersectionType()!._types`、`t.AsTypeReference().target`→`!._target`。
// - Go 的 `t.data.(*X)` 型断言在 C# 里是 `t.AsX()`（失败返回 null）→ 按类型不变量加 `!`（§3.5）；
//   `Type.AsType()` 已折叠（§5.1），对象本身即 Type。
// - Go 的 map 读（缺键返回零值）→ `GetValueOrDefault(k)` / `TryGetValue`
//   （`Dictionary` 索引器缺键会抛；`ObjectType.members` → `_members`，SymbolTable 是 `Dictionary<string, Symbol>`）。
// - 同名方法冲突（artifacts/checker-method-collisions.md）：本窗口未导出的 `getSymbolAtLocation`（Go 32002，与导出的
//   31989 同名）→ `GetSymbolAtLocationWorker`；`getIndexSignaturesAtLocation`（Go 32172，与 exports.go:276 的导出者同名）
//   → `GetIndexSignaturesAtLocationWorker`。调用的未导出同名者也一律用 `Worker`：`getMergedSymbol`、`getImmediateAliasedSymbol`、
//   `getPropertyOfType`、`getSignatureFromDeclaration`、`getDeclaredTypeOfSymbol`、`getBaseTypes`、`getTypeOfSymbol`、
//   `getTypeFromTypeNode`、`getTypeArguments`、`getResolvedSymbol`、`getIndexInfosOfType`、`resolveExternalModuleName`、`resolveAlias`。
// - `getIndexSignaturesAtLocation` 的 Go nil 切片（无签名时返回 nil）→ 返回前 `?? []`
//   （Go 的 len(nil)==0；`Exports.cs:173` 的导出包装已按非空 `Node[]` 声明）。
// - 局部 `append` → `List<Node>` + `ToArray()`（C# 数组没有 len<cap 视图）；Go 的 nil 切片 range → `?? []`。
// - Go 的 `fallthrough` → C# `goto case ...`（Binder/Ast 先例）；各分支内的局部变量名互不相同，与 Go 逐分支等价。
// - `panic("...")` → `Debug.Fail("...")`：两处其后都有 `return`，控制流不需要补 `throw UnreachableException`。
// - Go 的 `sync.Once.Do` → C1 的 `_emitResolverOnce` 标志 + `_emitResolverOnceLock` 双重检查锁（`System.Threading.Lock`）。
// - `getThisTypeFromContextualType` 的闭包参数在 Go 里遮蔽外层参数 `t`（`func(t *Type)`）且 `for _, t := range` 再次遮蔽；
//   C# 禁止遮蔽（CS0136），闭包参数改名 `target`、内层 range 变量改名 `component`（逐行语义不变）。
// - Go 的 `if fn := ...; cond`（if 初始化语句）→ C# 拆成前置 `var fn = ...;` + `if (cond)`（C# 无 if-init）。
// - Go 的 `Node.Parent`/`Node.Symbol()`：C# 是字段 `Parent` 与属性 `Symbol`（`Ast/Node.cs:408`，名字与类型名相同时用属性）；
//   `AsX()` 结果与 Go 里"已由 kind 判定必然非空"的指针一样加 `!`。
namespace TypeScript.Checker;

using System.Collections.Generic;
using TypeScript.Ast;
using TypeScript.Core;
using static TypeScript.Ast.Ast;
using static TypeScript.Core.Core;

public sealed partial class Checker
{
    public Symbol? GetSymbolAtLocation(Node node)
    {
        // !!!
        // const node = getParseTreeNode(nodeIn);

        // set ignoreErrors: true because any lookups invoked by the API shouldn't cause any new errors
        return GetSymbolAtLocationWorker(GetReparsedNodeForNode(node)!, true /*ignoreErrors*/);
    }

    // Returns the symbol associated with a given AST node. Do *not* use this function in the checker itself! It should
    // be used only by the language service and external tools. The semantics of the function are deliberately "fuzzy"
    // and aim to just return *some* symbol for the node. To obtain the symbol associated with a node for type checking
    // purposes, use appropriate function for the context, e.g. `getResolvedSymbol` for an expression identifier,
    // `getSymbolOfDeclaration` for a declaration, etc.
    internal Symbol? GetSymbolAtLocationWorker(Node node, bool ignoreErrors)
    {
        if (IsSourceFile(node))
        {
            if (IsExternalOrCommonJSModule(node.AsSourceFile()))
            {
                return GetMergedSymbolWorker(node.Symbol);
            }
            return null;
        }
        var parent = node.Parent!;
        var grandParent = parent.Parent;

        if ((node.Flags & NodeFlags.InWithStatement) != 0)
        {
            // We cannot answer semantic questions within a with block, do not proceed any further
            return null;
        }

        if (IsDeclarationNameOrImportPropertyName(node))
        {
            // This is a declaration, call getSymbolOfNode
            var parentSymbol = GetSymbolOfDeclaration(parent);
            if (IsImportOrExportSpecifier(parent) && parent.PropertyName() == node)
            {
                return GetImmediateAliasedSymbolWorker(parentSymbol!);
            }
            return parentSymbol;
        }
        else if (IsLiteralComputedPropertyDeclarationName(node))
        {
            return GetSymbolOfDeclaration(grandParent!);
        }

        if (IsIdentifier(node))
        {
            if (IsInRightSideOfImportOrExportAssignment(node))
            {
                return GetSymbolOfNameOrPropertyAccessExpression(node);
            }
            else if (IsBindingElement(parent) && IsObjectBindingPattern(grandParent!) && node == parent.PropertyName())
            {
                var typeOfPattern = GetTypeOfNode(grandParent!);
                var propertyDeclaration = GetPropertyOfTypeWorker(typeOfPattern, node.Text());
                if (propertyDeclaration is not null)
                {
                    return propertyDeclaration;
                }
            }
            else if (IsMetaProperty(parent) && parent.Name() == node)
            {
                var metaProp = parent.AsMetaProperty()!;
                if (metaProp.KeywordToken == Kind.NewKeyword && node.Text() == "target")
                {
                    // `target` in `new.target`
                    return CheckNewTargetMetaProperty(parent).Symbol();
                }
                // The `meta` in `import.meta` could be given `getTypeOfNode(parent).symbol` (the `ImportMeta` interface symbol), but
                // we have a fake expression type made for other reasons already, whose transient `meta`
                // member should more exactly be the kind of (declarationless) symbol we want.
                // (See #44364 and #45031 for relevant implementation PRs)
                if (metaProp.KeywordToken == Kind.ImportKeyword && node.Text() == "meta")
                {
                    return GetGlobalImportMetaExpressionType().AsObjectType()!._members!.GetValueOrDefault("meta");
                }
                // no other meta properties are valid syntax, thus no others should have symbols
                return null;
            }
            else if (IsJSDocParameterTag(parent) && parent.Name() == node)
            {
                // Go 的 `if fn := ...; fn != nil && ...` → C# 拆成两条语句（C# 无 if 初始化语句）。
                var fn = GetNodeAtPosition(GetSourceFileOfNode(node), node.Pos(), false);
                if (fn is not null && IsFunctionLike(fn))
                {
                    foreach (var param in fn.Parameters() ?? [])
                    {
                        if (IsIdentifier(param.Name()!) && param.Name()!.Text() == node.Text())
                        {
                            return GetSymbolOfNode(param);
                        }
                    }
                }
            }
        }

        switch (node.Kind)
        {
            case Kind.Identifier:
            case Kind.PrivateIdentifier:
            case Kind.PropertyAccessExpression:
            case Kind.QualifiedName:
                if (!IsThisInTypeQuery(node))
                {
                    return GetSymbolOfNameOrPropertyAccessExpression(node);
                }
                goto case Kind.ThisKeyword;
            case Kind.ThisKeyword:
                var container = GetThisContainer(node, false /*includeArrowFunctions*/, false /*includeClassComputedPropertyName*/);
                if (IsFunctionLike(container))
                {
                    var sig = GetSignatureFromDeclarationWorker(container);
                    if (sig._thisParameter is not null)
                    {
                        return sig._thisParameter;
                    }
                }
                if (IsInExpressionContext(node))
                {
                    return CheckExpression(node).Symbol();
                }
                goto case Kind.ThisType;
            case Kind.ThisType:
                return GetTypeFromThisTypeNode(node).Symbol();
            case Kind.SuperKeyword:
                return CheckExpression(node).Symbol();
            case Kind.ConstructorKeyword:
                // constructor keyword for an overload, should take us to the definition if it exist
                var constructorDeclaration = parent;
                if (constructorDeclaration is not null && constructorDeclaration.Kind == Kind.Constructor)
                {
                    return constructorDeclaration.Parent!.Symbol;
                }
                return null;
            case Kind.StringLiteral:
            case Kind.NoSubstitutionTemplateLiteral:
                // 1). import x = require("./mo/*gotToDefinitionHere*/d")
                // 2). External module name in an import declaration
                // 3). Require in Javascript
                // 4). type A = import("./f/*gotToDefinitionHere*/oo")
                if ((IsExternalModuleImportEqualsDeclaration(grandParent) && GetExternalModuleImportEqualsDeclarationExpression(grandParent) == node) ||
                    ((parent.Kind == Kind.ImportDeclaration || parent.Kind == Kind.JSImportDeclaration || parent.Kind == Kind.ExportDeclaration) && GetExternalModuleName(parent) == node) ||
                    IsVariableDeclarationInitializedToRequire(grandParent) || IsImportCall(parent) ||
                    (IsLiteralTypeNode(parent) && IsLiteralImportTypeNode(grandParent) && grandParent!.AsImportTypeNode()!.Argument == parent))
                {
                    return ResolveExternalModuleNameWorker(node, node, ignoreErrors, GetImportAttributesTypeForModuleSpecifier(node));
                }
                if (IsCallExpression(parent) && IsBindableObjectDefinePropertyCall(parent) && parent.Arguments()![1] == node)
                {
                    return GetSymbolOfDeclaration(parent);
                }
                goto case Kind.NumericLiteral;
            case Kind.NumericLiteral:
                // index access
                Type? objectType = null;
                if (IsElementAccessExpression(parent))
                {
                    if (parent.AsElementAccessExpression()!.ArgumentExpression == node)
                    {
                        objectType = GetTypeOfExpression(parent.Expression()!);
                    }
                }
                else if (IsLiteralTypeNode(parent) && IsIndexedAccessTypeNode(grandParent!))
                {
                    objectType = GetTypeFromTypeNodeWorker(grandParent!.AsIndexedAccessTypeNode()!.ObjectType!);
                }

                if (objectType is not null)
                {
                    return GetPropertyOfTypeWorker(objectType, node.Text());
                }
                return null;
            case Kind.DefaultKeyword:
            case Kind.FunctionKeyword:
            case Kind.EqualsGreaterThanToken:
            case Kind.ClassKeyword:
                return GetSymbolOfNode(node.Parent!);
            case Kind.ImportType:
                if (IsLiteralImportTypeNode(node))
                {
                    return GetSymbolAtLocationWorker(node.AsImportTypeNode()!.Argument!.AsLiteralTypeNode()!.Literal!, ignoreErrors);
                }
                return null;
            case Kind.ExportKeyword:
                if (IsExportAssignment(parent))
                {
                    if (parent.Symbol is null)
                    {
                        Debug.Fail("Symbol should be defined");
                    }
                    return parent.Symbol;
                }
                return null;
            case Kind.ImportKeyword:
                if (IsMetaProperty(node.Parent!) && node.Parent!.Text() == "defer")
                {
                    return null;
                }
                goto case Kind.NewKeyword;
            case Kind.NewKeyword:
                if (IsMetaProperty(parent))
                {
                    return CheckMetaPropertyKeyword(parent).Symbol();
                }
                return null;
            case Kind.InstanceOfKeyword:
                if (IsBinaryExpression(parent))
                {
                    var t = GetTypeOfExpression(parent.AsBinaryExpression()!.Right!);
                    var hasInstanceMethodType = GetSymbolHasInstanceMethodOfObjectType(t);
                    if (hasInstanceMethodType is not null && hasInstanceMethodType._symbol is not null)
                    {
                        return hasInstanceMethodType._symbol;
                    }
                    return t._symbol;
                }
                return null;
            case Kind.MetaProperty:
                return CheckExpression(node).Symbol();
            case Kind.JsxNamespacedName:
                if (IsJsxTagName(node) && IsJsxIntrinsicTagName(node))
                {
                    var symbol = GetIntrinsicTagSymbol(node.Parent!);
                    if (symbol == _unknownSymbol)
                    {
                        return null;
                    }
                    return symbol;
                }
                goto default;
            default:
                return null;
        }
    }

    internal Node[] GetIndexSignaturesAtLocationWorker(Node node)
    {
        Node[]? signatures = null;
        if (IsIdentifier(node) && IsPropertyAccessExpression(node.Parent!) && node.Parent!.Name() == node)
        {
            var keyType = GetLiteralTypeFromPropertyName(node);
            var objectType = GetTypeOfExpression(node.Parent!.Expression()!);
            foreach (var t in objectType.Distributed())
            {
                foreach (var info in GetApplicableIndexInfos(t, keyType))
                {
                    if (info._declaration is not null)
                    {
                        signatures = AppendIfUnique(signatures, info._declaration!);
                    }
                }
            }
        }
        return signatures ?? [];
    }

    internal Symbol? GetSymbolOfNameOrPropertyAccessExpression(Node name)
    {
        if (IsDeclarationName(name))
        {
            return GetSymbolOfNode(name.Parent!);
        }
        if (name.Parent!.Kind == Kind.ExportAssignment && IsEntityNameExpression(name))
        {
            // Even an entity name expression that doesn't resolve as an entityname may still typecheck as a property access expression
            var success = ResolveEntityName(
                name,
                /*all meanings*/ SymbolFlags.Value | SymbolFlags.Type | SymbolFlags.Namespace | SymbolFlags.Alias,
                true /*ignoreErrors*/, false /*dontResolveAlias*/, null /*location*/
            );
            if (success is not null && success != _unknownSymbol)
            {
                return success;
            }
        }
        else if (IsEntityName(name) && IsInRightSideOfImportOrExportAssignment(name))
        {
            // Since we already checked for ExportAssignment, this really could only be an Import
            var importEqualsDeclaration = FindAncestorKind(name, Kind.ImportEqualsDeclaration);
            if (importEqualsDeclaration is null)
            {
                Debug.Fail("ImportEqualsDeclaration should be defined");
            }
            return GetSymbolOfPartOfRightHandSideOfImportEquals(name);
        }

        if (IsEntityName(name))
        {
            var possibleImportNode = IsImportTypeQualifierPart(name);
            if (possibleImportNode is not null)
            {
                GetTypeFromTypeNodeWorker(possibleImportNode);
                var sym = GetResolvedSymbolOrNil(name);
                return IfElse(sym == _unknownSymbol, null, sym);
            }
        }

        while (IsRightSideOfQualifiedNameOrPropertyAccess(name))
        {
            name = name.Parent!;
        }

        if (IsInNameOfExpressionWithTypeArgumentsOrHeritageTypeReference(name))
        {
            SymbolFlags meaning;
            if (name.Parent!.Kind == Kind.ExpressionWithTypeArguments || name.Parent.Kind == Kind.TypeReference)
            {
                // A heritage element name may appear in type space, value space, or both;
                // ensure the meaning matches its context.
                meaning = IfElse(IsPartOfTypeNode(name), SymbolFlags.Type, SymbolFlags.Value);

                // In a class 'extends' clause we are also looking for a value.
                if (IsExpressionWithTypeArgumentsInClassExtendsClause(name.Parent))
                {
                    meaning = meaning | SymbolFlags.Value;
                }
            }
            else
            {
                meaning = SymbolFlags.Namespace;
            }

            meaning = meaning | SymbolFlags.Alias;
            Symbol? entityNameSymbol = null;
            if (IsEntityNameExpression(name))
            {
                entityNameSymbol = ResolveEntityName(name, meaning, true /*ignoreErrors*/, false /*dontResolveAlias*/, null /*location*/);
            }
            if (entityNameSymbol is not null)
            {
                return entityNameSymbol;
            }
        }

        if (IsExpressionNode(name))
        {
            if (NodeIsMissing(name))
            {
                // Missing entity name.
                return null;
            }
            var isJSDoc = IsJSDocNameReferenceContext(name);
            if (IsIdentifier(name))
            {
                if (IsJsxTagName(name) && IsJsxIntrinsicTagName(name))
                {
                    var symbol = GetIntrinsicTagSymbol(name.Parent!);
                    return IfElse(symbol == _unknownSymbol, null, symbol);
                }
                var meaning = IfElse(isJSDoc, SymbolFlags.Value | SymbolFlags.Type | SymbolFlags.Namespace, SymbolFlags.Value);
                Node? location = null;
                if (isJSDoc)
                {
                    location = GetHostSignatureFromJSDoc(name);
                }
                var result = ResolveEntityName(name, meaning, true /*ignoreErrors*/, true /*dontResolveAlias*/, location);
                if (result is null && isJSDoc)
                {
                    var ancestor = FindAncestor(name, n => IsClassOrInterfaceLike(n));
                    if (ancestor is not null)
                    {
                        var symbol = GetSymbolOfDeclaration(ancestor);
                        // Handle unqualified references to class static members and class or interface instance members
                        if ((result = GetMergedSymbolWorker(GetSymbol(GetExportsOfSymbol(symbol!), name.Text(), meaning))) is null)
                        {
                            result = GetPropertyOfTypeWorker(GetDeclaredTypeOfSymbolWorker(symbol!), name.Text());
                        }
                    }
                }
                return result;
            }
            else if (IsPrivateIdentifier(name))
            {
                return GetSymbolForPrivateIdentifierExpression(name);
            }
            else if (IsPropertyAccessExpression(name) || IsQualifiedName(name))
            {
                var links = _symbolNodeLinks.Get(name);
                if (links._resolvedSymbol is not null)
                {
                    return links._resolvedSymbol;
                }
                if (IsPropertyAccessExpression(name))
                {
                    CheckPropertyAccessExpression(name, CheckMode.Normal, false /*writeOnly*/);
                    if (links._resolvedSymbol is null && !IsPrivateIdentifier(name.Name()!))
                    {
                        links._resolvedSymbol = GetApplicableIndexSymbol(
                            CheckExpressionCached(name.Expression()!),
                            GetLiteralTypeFromPropertyName(name.Name()!)
                        );
                    }
                }
                else
                {
                    CheckQualifiedName(name, CheckMode.Normal);
                }
                if (links._resolvedSymbol is null && isJSDoc && IsQualifiedName(name))
                {
                    return ResolveJSDocMemberName(name);
                }
                return links._resolvedSymbol;
            }
        }
        else if (IsEntityName(name) && IsTypeReferenceIdentifier(name))
        {
            var meaning = IfElse(name.Parent!.Kind == Kind.TypeReference, SymbolFlags.Type, SymbolFlags.Namespace);
            var symbol = ResolveEntityName(name, meaning, true /*ignoreErrors*/, true /*dontResolveAlias*/, null /*location*/);
            if (symbol is not null && symbol != _unknownSymbol)
            {
                return symbol;
            }
            if (IsNameOfHeritageClauseTypeReference(name))
            {
                return null;
            }
            return GetUnresolvedSymbolForEntityName(name);
        }

        if (name.Parent!.Kind == Kind.TypePredicate)
        {
            return ResolveEntityName(
                name,
                SymbolFlags.FunctionScopedVariable, /*meaning*/
                true,                               /*ignoreErrors*/
                false,                              /*dontResolveAlias*/
                null                                /*location*/
            );
        }
        return null;
    }

    internal bool IsThisPropertyAndThisTyped(Node node)
    {
        if (node.Expression()!.Kind == Kind.ThisKeyword)
        {
            var container = GetThisContainer(node, false /*includeArrowFunctions*/, false /*includeClassComputedPropertyName*/);
            if (IsFunctionLike(container))
            {
                var containingLiteral = GetContainingObjectLiteral(container);
                if (containingLiteral is not null)
                {
                    var contextualType = GetApparentTypeOfContextualType(containingLiteral, ContextFlags.None);
                    var t = GetThisTypeOfObjectLiteralFromContextualType(containingLiteral, contextualType);
                    return t is not null && !IsTypeAny(t);
                }
            }
        }
        return false;
    }

    internal Type GetTypeOfNode(Node node)
    {
        if (IsSourceFile(node) && !IsExternalOrCommonJSModule(node.AsSourceFile()))
        {
            return _errorType!;
        }

        if ((node.Flags & NodeFlags.InWithStatement) != 0)
        {
            // We cannot answer semantic questions within a with block, do not proceed any further
            return _errorType!;
        }

        TryGetClassImplementingOrExtendingHeritageClauseElement(node, out var classDecl, out var isImplements);
        Type? classType = null;
        if (classDecl is not null)
        {
            classType = GetDeclaredTypeOfClassOrInterface(GetSymbolOfDeclaration(classDecl)!);
        }

        if (IsPartOfTypeNode(node))
        {
            var typeFromTypeNode = GetTypeFromTypeNodeWorker(node);
            if (classType is not null)
            {
                return GetTypeWithThisArgument(
                    typeFromTypeNode,
                    classType.AsInterfaceType()!._thisType,
                    false /*needApparentType*/
                );
            }

            return typeFromTypeNode;
        }

        if (IsExpressionNode(node))
        {
            return GetRegularTypeOfExpression(node);
        }

        if (classType is not null && !isImplements)
        {
            // A SyntaxKind.ExpressionWithTypeArguments is considered a type node, except when it occurs in the
            // extends clause of a class. We handle that case here.
            var baseType = FirstOrNil(GetBaseTypesWorker(classType));
            if (baseType is not null)
            {
                return GetTypeWithThisArgument(baseType, classType.AsInterfaceType()!._thisType, false /*needApparentType*/);
            }
            return _errorType!;
        }

        if (IsTypeDeclaration(node))
        {
            // In this case, we call getSymbolOfDeclaration instead of getSymbolAtLocation because it is a declaration
            var symbol = GetSymbolOfDeclaration(node);
            return GetDeclaredTypeOfSymbolWorker(symbol!);
        }

        if (IsTypeDeclarationName(node))
        {
            var symbol = GetSymbolAtLocationWorker(node, false /*ignoreErrors*/);
            if (symbol is not null)
            {
                return GetDeclaredTypeOfSymbolWorker(symbol);
            }
            return _errorType!;
        }

        if (IsBindingElement(node))
        {
            var t = GetTypeForVariableLikeDeclaration(node, true /*includeOptionality*/, CheckMode.Normal);
            if (t is not null)
            {
                return t;
            }
            return _errorType!;
        }

        if (IsDeclaration(node))
        {
            // In this case, we call getSymbolOfDeclaration instead of getSymbolLAtocation because it is a declaration
            var symbol = GetSymbolOfDeclaration(node);
            if (symbol is not null)
            {
                return GetTypeOfSymbolWorker(symbol);
            }
            return _errorType!;
        }

        if (IsDeclarationNameOrImportPropertyName(node))
        {
            var symbol = GetSymbolAtLocationWorker(node, false /*ignoreErrors*/);
            if (symbol is not null)
            {
                return GetTypeOfSymbolWorker(symbol);
            }
            return _errorType!;
        }

        if (IsBindingPattern(node))
        {
            var t = GetTypeForVariableLikeDeclaration(node.Parent!, true /*includeOptionality*/, CheckMode.Normal);
            if (t is not null)
            {
                return t;
            }
            return _errorType!;
        }

        if (IsInRightSideOfImportOrExportAssignment(node))
        {
            var symbol = GetSymbolAtLocationWorker(node, false /*ignoreErrors*/);
            if (symbol is not null)
            {
                var declaredType = GetDeclaredTypeOfSymbolWorker(symbol);
                if (!IsErrorType(declaredType))
                {
                    return declaredType;
                }
                return GetTypeOfSymbolWorker(symbol);
            }
        }

        if (IsMetaProperty(node.Parent!) && node.Parent!.AsMetaProperty()!.KeywordToken == node.Kind)
        {
            return CheckMetaPropertyKeyword(node.Parent);
        }

        if (IsImportAttributes(node))
        {
            return CheckImportAttributesExpression(node);
        }

        return _errorType!;
    }

    internal Type? GetThisTypeOfObjectLiteralFromContextualType(Node containingLiteral, Type? contextualType)
    {
        var literal = containingLiteral;
        var t = contextualType;
        while (t is not null)
        {
            var thisType = GetThisTypeFromContextualType(t);
            if (thisType is not null)
            {
                return thisType;
            }
            if (literal.Parent!.Kind != Kind.PropertyAssignment)
            {
                break;
            }
            literal = literal.Parent!.Parent!;
            t = GetApparentTypeOfContextualType(literal, ContextFlags.None);
        }
        return null;
    }

    internal Type? GetThisTypeFromContextualType(Type t)
    {
        // Go 里闭包参数遮蔽外层参数 `t`、内层 range 变量再次遮蔽；C# 禁止遮蔽（CS0136）→ 改名 target/component。
        return MapType(t, target =>
        {
            if ((target._flags & TypeFlags.Intersection) != 0)
            {
                foreach (var component in target.AsIntersectionType()!._types!)
                {
                    var typeArg = GetThisTypeArgument(component);
                    if (typeArg is not null)
                    {
                        return typeArg;
                    }
                }
                // Go 这里返回 nil；C# 端口把 mapType 的 `f func(*Type) *Type` 落成非空 `Func<Type, Type>`
                // （Checker.C7.Widening.cs:155），所以按 Go 的 nil 语义写 `null!`。
                return null!;
            }
            else
            {
                return GetThisTypeArgument(target)!;
            }
        });
    }

    internal Type? GetThisTypeArgument(Type t)
    {
        if ((t._objectFlags & ObjectFlags.Reference) != 0 && t.AsTypeReference()!._target == _globalThisType)
        {
            return GetTypeArgumentsWorker(t)[0];
        }
        return null;
    }

    internal IndexInfo[] GetApplicableIndexInfos(Type t, Type keyType)
    {
        return Filter(GetIndexInfosOfTypeWorker(t), info => IsApplicableIndexType(keyType, info._keyType!));
    }

    internal Symbol? GetApplicableIndexSymbol(Type t, Type keyType)
    {
        var info = GetApplicableIndexInfo(t, keyType);
        if (info is not null && info != _anyBaseTypeIndexInfo)
        {
            if (info._indexSymbol is null)
            {
                List<Node> declarations;
                if (info._declaration is not null)
                {
                    declarations = new List<Node> { info._declaration! };
                }
                else
                {
                    declarations = new List<Node>();
                    foreach (var indexInfo in GetIndexInfosOfTypeWorker(t) ?? [])
                    {
                        if (indexInfo._declaration is not null && IsApplicableIndexType(keyType, indexInfo._keyType!))
                        {
                            declarations.Add(indexInfo._declaration!);
                        }
                    }
                }
                if (declarations.Count != 0)
                {
                    var symbol = NewSymbol(SymbolFlags.Property, InternalSymbolNameIndex);
                    symbol.CheckFlags |= CheckFlags.IndexSymbol;
                    symbol.Declarations = declarations.ToArray();
                    symbol.ValueDeclaration = declarations[0];
                    symbol.Parent = t._symbol;
                    var links = _valueSymbolLinks.Get(symbol);
                    links._resolvedType = info._valueType;
                    info._indexSymbol = symbol;
                }
            }
            return info._indexSymbol;
        }
        return null;
    }

    internal Type GetRegularTypeOfExpression(Node expr)
    {
        if (IsRightSideOfQualifiedNameOrPropertyAccess(expr))
        {
            expr = expr.Parent!;
        }
        return GetRegularTypeOfLiteralType(GetTypeOfExpression(expr));
    }

    internal bool ContainsArgumentsReference(Node node)
    {
        if (node.Body() is null)
        {
            return false;
        }

        if (_cachedArgumentsReferenced.TryGetValue(node, out var containsArguments))
        {
            return containsArguments;
        }

        // Go 的闭包参数遮蔽外层参数 `node`；C# 禁止遮蔽（CS0136）→ 改名 current（逐行语义不变）。
        bool Visit(Node? current)
        {
            if (current is null)
            {
                return false;
            }
            switch (current.Kind)
            {
                case Kind.Identifier:
                    return current.Text() == _argumentsSymbol!.Name && IsArgumentsSymbol(GetResolvedSymbolWorker(current));
                case Kind.PropertyDeclaration:
                case Kind.MethodDeclaration:
                case Kind.GetAccessor:
                case Kind.SetAccessor:
                    if (IsComputedPropertyName(current.Name()!))
                    {
                        return Visit(current.Name());
                    }
                    break;
                case Kind.PropertyAccessExpression:
                case Kind.ElementAccessExpression:
                    return Visit(current.Expression());
                case Kind.PropertyAssignment:
                    return Visit(current.Initializer());
            }
            if (NodeStartsNewLexicalEnvironment(current) || IsPartOfTypeNode(current))
            {
                return false;
            }
            return current.ForEachChild(Visit);
        }

        containsArguments = Visit(node.Body());
        _cachedArgumentsReferenced[node] = containsArguments;
        return containsArguments;
    }

    public Type GetTypeAtLocation(Node node)
    {
        return GetTypeOfNode(GetReparsedNodeForNode(node)!);
    }

    public EmitResolver GetEmitResolver()
    {
        lock (_emitResolverOnceLock)
        {
            if (!_emitResolverOnce)
            {
                _emitResolverOnce = true;
                _emitResolver = EmitResolver.NewEmitResolver(this);
            }
        }

        return _emitResolver!;
    }

    public Symbol GetAliasedSymbol(Symbol symbol)
    {
        return ResolveAliasWorker(symbol);
    }
}
