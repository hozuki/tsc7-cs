// ported from: internal/checker/exports.go:1-400
//
// 本文件是 `internal/checker/exports.go` 的 1:1 移植：对外的 Checker 查询 API（大多是导出包装器）。
//
// Go→C# 语义改写（逐条）：
//  1) `func (c *Checker) X()` → `public sealed partial class Checker` 的实例方法（名字照抄 Go 的导出名，api-parity 要求）。
//     Go 未导出的同名实现（`getX`）在 C# 里加 `Worker` 后缀（同一个类不能有两个同名方法；先例 `EmitSourceFileWorker`）。
//     例：`GetUnionType`（本文件的导出包装器）调用 `GetUnionTypeWorker`（checker.go 的 `getUnionType`）。
//     完整冲突清单见 `artifacts/checker-method-collisions.md`。
//  2) Go 包级导出函数 → 本类（同一个 partial class）的 `public static` 成员：`IsTypeUsableAsPropertyName`/
//     `GetPropertyNameFromType`/`GetDeclarationModifierFlagsFromSymbol`/`IsTupleType`/`IsTupleTypeTarget`/
//     `IsDistributedTypeParameter`；对应的 Go 未导出同名包级函数加 `Worker`（`IsTypeUsableAsPropertyNameWorker` …）。
//  3) 字段：Go 未导出字段 → `_camelCase`（`c.stringType` → `_stringType`）；Go 导出字段 → PascalCase 原名。
//  4) `*Type`/`*ast.Symbol`/`*ast.Node` 可空处 → `T?`（Go 的 nil）。`t.flags`/`t.objectFlags`/`t.symbol`/`t.alias`
//     是 Type 的内部字段（ChkTypes/types.go 片）→ `t._flags`/`t._objectFlags`/`t._symbol`/`t._alias`（Go 的字段访问逐字对应）。
//  5) `IsTupleTypeTarget`：Go `isTupleType(t) && t.Target() == t` → `IsTupleTypeWorker(t) && t.Target() == t`
//     （Node/Type 不覆写 `==`，引用相等 = Go 的指针相等）。
//  6) `GetJsxFragmentFactory`：Go 用 `entity != nil` 判断 → C# `entity is not null`；`ast.GetFirstIdentifier(entity).Text()`
//     → `Ast.GetFirstIdentifier(entity)!.Text()`。
//  7) `RequiresAddingImplicitUndefined`：Go `node.Symbol()` → C# `node.Symbol`（Node.cs 把该方法做成属性，避免与类型名同名的遮蔽）。
//  8) `GetUnionTypeEx` 的调用点补齐 Go 里省略的尾部实参（`nil, nil`）→ `null, null`。
namespace TypeScript.Checker;

using TypeScript.Ast;
using TypeScript.Diagnostics;
using static TypeScript.Ast.Ast;

public sealed partial class Checker
{
    public Type GetStringType() => _stringType;
    public Type GetNumberType() => _numberType;
    public Type GetBooleanType() => _booleanType;
    public Type GetVoidType() => _voidType;
    public Type GetUndefinedType() => _undefinedType;
    public Type GetNullType() => _nullType;
    public Type GetAnyType() => _anyType;
    public Type GetErrorType() => _errorType;
    public Type GetNeverType() => _neverType;
    public Type GetUnknownType() => _unknownType;
    public Type GetBigIntType() => _bigintType;
    public Type GetESSymbolType() => _esSymbolType;
    public Type GetNonPrimitiveType() => _nonPrimitiveType;

    public Type GetBaseTypeOfLiteralType(Type t) => GetBaseTypeOfLiteralTypeWorker(t);

    public Symbol? GetUnknownSymbol() => _unknownSymbol;
    public Symbol? GetUndefinedSymbol() => _undefinedSymbol;
    public Symbol? GetArgumentsSymbol() => _argumentsSymbol;
    public Signature? GetUnknownSignature() => _unknownSignature;

    public Type GetUnionType(Type[] types) => GetUnionTypeWorker(types);

    public Type? GetNameTypeOfSymbol(Symbol symbol)
    {
        var links = _valueSymbolLinks.TryGet(symbol);
        if (links is not null)
        {
            return links._nameType;
        }
        return null;
    }

    public static bool IsTypeUsableAsPropertyName(Type t) => IsTypeUsableAsPropertyNameWorker(t);

    public static string GetPropertyNameFromType(Type t) => GetPropertyNameFromTypeWorker(t);

    public Symbol? GetGlobalSymbol(string name, SymbolFlags meaning, Message? diagnostic) =>
        GetGlobalSymbolWorker(name, meaning, diagnostic);

    public Symbol? GetMergedSymbol(Symbol? symbol) => GetMergedSymbolWorker(symbol);

    public Symbol? TryFindAmbientModule(string moduleName) =>
        TryFindAmbientModuleWorker(moduleName, true /* withAugmentations */);

    public Symbol? GetImmediateAliasedSymbol(Symbol? symbol) => GetImmediateAliasedSymbolWorker(symbol!);

    public Symbol? GetTargetSymbol(Symbol? symbol) => GetTargetSymbolWorker(symbol);

    public Node? GetTypeOnlyAliasDeclaration(Symbol symbol) => GetTypeOnlyAliasDeclarationWorker(symbol);

    public Symbol? ResolveExternalModuleName(Node moduleSpecifier, Type? importAttributesType) =>
        ResolveExternalModuleNameWorker(moduleSpecifier, moduleSpecifier, true /*ignoreErrors*/, importAttributesType);

    public Symbol? ResolveExternalModuleSymbol(Symbol? moduleSymbol) =>
        ResolveExternalModuleSymbolWorker(moduleSymbol, false /*dontResolveAlias*/);

    public Type GetTypeFromTypeNode(Node node) => GetTypeFromTypeNodeWorker(node);

    public bool IsArrayLikeType(Type t) => IsArrayLikeTypeWorker(t);

    public Symbol[] GetPropertiesOfType(Type t) => GetPropertiesOfTypeWorker(t) ?? [];

    public Symbol? GetPropertyOfType(Type t, string name) => GetPropertyOfTypeWorker(t, name);

    public bool TypeHasCallOrConstructSignatures(Type t) => TypeHasCallOrConstructSignaturesWorker(t);

    // Checks if a property can be accessed in a location.
    // The location is given by the `node` parameter.
    // The node does not need to be a property access.
    // @param node location where to check property accessibility
    // @param isSuper whether to consider this a `super` property access, e.g. `super.foo`.
    // @param isWrite whether this is a write access, e.g. `++foo.x`.
    // @param containingType type where the property comes from.
    // @param property property symbol.
    public bool IsPropertyAccessible(Node node, bool isSuper, bool isWrite, Type containingType, Symbol property) =>
        IsPropertyAccessibleWorker(node, isSuper, isWrite, containingType, property);

    public Type? GetTypeOfPropertyOfContextualType(Type t, string name) =>
        GetTypeOfPropertyOfContextualTypeWorker(t, name);

    public static ModifierFlags GetDeclarationModifierFlagsFromSymbol(Symbol s) =>
        GetDeclarationModifierFlagsFromSymbolWorker(s);

    public bool WasCanceled() => _wasCanceled;

    public Signature[] GetSignaturesOfType(Type t, SignatureKind kind) => GetSignaturesOfTypeWorker(t, kind) ?? [];

    public Type GetDeclaredTypeOfSymbol(Symbol symbol) => GetDeclaredTypeOfSymbolWorker(symbol);

    public Type GetTypeOfSymbol(Symbol symbol) => GetTypeOfSymbolWorker(symbol);

    public Type GetNonMissingTypeOfSymbol(Symbol symbol) => GetNonMissingTypeOfSymbolWorker(symbol);

    public Type? GetConstraintOfTypeParameter(Type typeParameter) =>
        GetConstraintOfTypeParameterWorker(typeParameter);

    public Type? GetTrueTypeOfConditionalType(Type t) => GetTrueTypeFromConditionalType(t);

    public Type? GetFalseTypeOfConditionalType(Type t) => GetFalseTypeFromConditionalType(t);

    public Type? GetDefaultFromTypeParameter(Type typeParameter) => GetDefaultFromTypeParameterWorker(typeParameter);

    public ModifierFlags GetEffectiveDeclarationFlags(Node n, ModifierFlags flagsToCheck) =>
        GetEffectiveDeclarationFlagsWorker(n, flagsToCheck);

    public Type? GetBaseConstraintOfType(Type t) => GetBaseConstraintOfTypeWorker(t);

    public TypePredicate? GetTypePredicateOfSignature(Signature sig) => GetTypePredicateOfSignatureWorker(sig);

    public static bool IsTupleType(Type t) => IsTupleTypeWorker(t);

    public static bool IsTupleTypeTarget(Type t) => IsTupleTypeWorker(t) && t.Target() == t;

    public bool IsArrayType(Type t) => IsArrayTypeWorker(t);

    public bool IsReadonlySymbol(Symbol symbol) => IsReadonlySymbolWorker(symbol);

    public Type GetReturnTypeOfSignature(Signature sig) => GetReturnTypeOfSignatureWorker(sig);

    public bool HasEffectiveRestParameter(Signature signature) => HasEffectiveRestParameterWorker(signature);

    public Type[] GetLocalTypeParametersOfClassOrInterfaceOrTypeAlias(Symbol symbol) =>
        GetLocalTypeParametersOfClassOrInterfaceOrTypeAliasWorker(symbol);

    public Type? GetContextualTypeForObjectLiteralElement(Node element, ContextFlags contextFlags) =>
        GetContextualTypeForObjectLiteralElementWorker(element, contextFlags);

    public string TypePredicateToString(TypePredicate t) => TypePredicateToStringWorker(t);

    public Symbol[][] GetExpandedParameters(Signature signature, bool skipUnionExpanding) =>
        GetExpandedParametersWorker(signature, skipUnionExpanding);

    public Signature? GetResolvedSignature(Node node) => GetResolvedSignatureWorker(node, null, CheckMode.Normal);

    // Return the type of the given property in the given type, or nil if no such property exists
    public Type? GetTypeOfPropertyOfType(Type t, string name) => GetTypeOfPropertyOfTypeWorker(t, name);

    public Type? GetContextualTypeForArgumentAtIndex(Node node, int argIndex) =>
        GetContextualTypeForArgumentAtIndexWorker(node, argIndex);

    public Type? GetAwaitedType(Type t) => GetAwaitedTypeWorker(t);

    public Node[] GetIndexSignaturesAtLocation(Node node) => GetIndexSignaturesAtLocationWorker(node);

    public Symbol? GetResolvedSymbol(Node node) => GetResolvedSymbolWorker(node);

    public string GetJsxNamespace(Node location) => GetJsxNamespaceWorker(location);

    public string GetJsxFragmentFactory(Node location)
    {
        var entity = GetJsxFragmentFactoryEntity(location);
        if (entity is not null)
        {
            return GetFirstIdentifier(entity)!.Text();
        }
        return "";
    }

    public Symbol? ResolveName(string name, Node location, SymbolFlags meaning, bool excludeGlobals) =>
        _resolveName(location, name, meaning, null, true, excludeGlobals);

    public SymbolFlags GetSymbolFlags(Symbol symbol) => GetSymbolFlagsWorker(symbol);

    public Type[] GetBaseTypes(Type t) => GetBaseTypesWorker(t) ?? [];

    public Type GetApparentType(Type t) => GetApparentTypeWorker(t);

    public Type GetReducedType(Type t) => GetReducedTypeWorker(t);

    // GetFullyQualifiedName returns the fully qualified name of a symbol, walking up
    // its parent chain (e.g. `"/path/to/module".Namespace.Name`).
    public string GetFullyQualifiedName(Symbol symbol) => GetFullyQualifiedNameWorker(symbol, null /*containingLocation*/);

    public Type GetBaseConstructorTypeOfClass(Type t) => GetBaseConstructorTypeOfClassWorker(t);

    public MemberOverrideStatus GetMemberOverrideModifierStatus(Node node, Node member, Symbol memberSymbol) =>
        GetMemberOverrideModifierStatusWorker(node, member, memberSymbol);

    public Type GetRestTypeOfSignature(Signature sig) => GetRestTypeOfSignatureWorker(sig);

    public Type[] GetTypeArguments(Type t) => GetTypeArgumentsWorker(t);

    public IndexInfo? GetIndexInfoOfType(Type t, Type keyType) => GetIndexInfoOfTypeWorker(t, keyType);

    public Type? GetIndexTypeOfType(Type t, Type keyType) => GetIndexTypeOfTypeWorker(t, keyType);

    public IndexInfo[] GetIndexInfosOfType(Type t) => GetIndexInfosOfTypeWorker(t) ?? [];

    public bool IsContextSensitive(Node node) => IsContextSensitiveWorker(node);

    public Type[] FillMissingTypeArguments(Type[] typeArguments, Type[] typeParameters, int minTypeArgumentCount, bool isJavaScriptImplicitAny) =>
        FillMissingTypeArgumentsWorker(typeArguments, typeParameters, minTypeArgumentCount, isJavaScriptImplicitAny) ?? [];

    public int GetMinTypeArgumentCount(Type[] typeParameters) => GetMinTypeArgumentCountWorker(typeParameters);

    public Type GetWidenedLiteralType(Type t) => GetWidenedLiteralTypeWorker(t);

    public bool IsTypeAssignableTo(Type source, Type target) => IsTypeAssignableToWorker(source, target);

    public Type GetUnionTypeEx(Type[] types, UnionReduction unionReduction) =>
        GetUnionTypeExWorker(types, unionReduction, null, null);

    public bool RequiresAddingImplicitUndefined(Node node)
    {
        var enclosingDeclaration = FindAncestor(node, n => IsDeclaration(n));
        enclosingDeclaration ??= GetSourceFileOfNode(node)!.AsNode();
        var symbol = node.Symbol;
        if (symbol is null)
        {
            return false;
        }
        return GetEmitResolver().RequiresAddingImplicitUndefined(node, symbol, enclosingDeclaration);
    }

    public Type RemoveMissingOrUndefinedType(Type t) => RemoveMissingOrUndefinedTypeWorker(t);

    public Type GetWidenedType(Type t) => GetWidenedTypeWorker(t);

    public int CompareSymbols(Symbol s1, Symbol s2) => _compareSymbols(s1, s2);

    public static bool IsDistributedTypeParameter(Type t) =>
        (t._flags & TypeFlags.TypeParameter) != 0 && t.AsTypeParameter()!._isDistributed;
}
