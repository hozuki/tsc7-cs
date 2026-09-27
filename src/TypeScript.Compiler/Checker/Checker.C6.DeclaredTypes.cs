// ported from: internal/checker/checker.go:24036-24427
//   （getTypeAliasInstantiation / isLocalTypeAlias / getDeclaredTypeOfSymbol / tryGetDeclaredTypeOfSymbol /
//    getTypeReferenceName / getAliasForTypeNode / getAliasSymbolForTypeNode / getTypeArgumentsForAliasSymbol /
//    getOuterTypeParametersOfClassOrInterface / getClassOrInterfaceLikeDeclaration /
//    canGetTypeParametersOfClassOrInterface / getOuterTypeParameters / getInferTypeParameters /
//    getLocalTypeParametersOfClassOrInterfaceOrTypeAlias / appendLocalTypeParametersOfClassOrInterfaceOrTypeAlias /
//    appendTypeParameters / getDeclaredTypeOfTypeParameter / getDeclaredTypeOfTypeAlias / getDeclaredTypeOfEnum /
//    getEnumMemberValue / createComputedEnumType / getDeclaredTypeOfEnumMember / computeEnumMemberValues /
//    computeEnumMemberValue / computeConstantEnumMemberValue：类型别名实例化入口、各类声明类型（类型参数/类型别名/枚举）、
//    枚举成员常量求值）
//
// 与 Go 的偏离（逐条；其余与 Go 逐行对应）：
// 1) Go 的 `func (c *Checker) x()` → 实例方法（PascalCase；未导出方法 → `internal`）；Go 的**包级函数**
//    （`isLocalTypeAlias`、`getTypeReferenceName`）→ 同一个 `public sealed partial class Checker` 的
//    `internal static` 成员，调用点裸名（docs/checker-plan.md §5.2，Parser/Binder/Printer 先例）。
// 2) 命名：Go 未导出**字段** → `_camelCase`（`links.declaredType` → `links._declaredType`、
//    `links.typeParameters` → `links._typeParameters`、`links.instantiations` → `links._instantiations`、
//    `t.flags` → `t._flags`、`t.symbol` → `t._symbol`、`t.freshType` → `t._freshType`、
//    `interfaceType.thisType` → `interfaceType._thisType`、`links.value` → `links._value`）；
//    Go 导出字段/属性保持原名（`result.Value`、`result.ResolvedOtherFiles`、`symbol.Name`、`symbol.Flags`、
//    `symbol.Declarations`、`symbol.ValueDeclaration`）。枚举成员名 = Go 常量名去类型前缀
//    （`TypeFlagsUnion` → `TypeFlags.Union`、`TypeFlagsEnum`/`TypeFlagsEnumLiteral` → `TypeFlags.Enum`/`.EnumLiteral`、
//    `UnionReductionLiteral` → `UnionReduction.Literal`、`NodeCheckFlagsEnumValuesComputed` →
//    `NodeCheckFlags.EnumValuesComputed`、`SymbolFlags*`/`Kind*` 同理）。
// 3) 同名冲突（artifacts/checker-method-collisions.md）：`getDeclaredTypeOfSymbol` → `GetDeclaredTypeOfSymbolWorker`、
//    `getLocalTypeParametersOfClassOrInterfaceOrTypeAlias` → `GetLocalTypeParametersOfClassOrInterfaceOrTypeAliasWorker`
//    （定义点与所有调用点都用新名）。调用别片的同名冲突成员也用 Worker 名：
//    `getMinTypeArgumentCount`→`GetMinTypeArgumentCountWorker`、`fillMissingTypeArguments`→`FillMissingTypeArgumentsWorker`、
//    `getTypeOfSymbol`→`GetTypeOfSymbolWorker`、`getSignaturesOfType`→`GetSignaturesOfTypeWorker`、
//    `isContextSensitive`→`IsContextSensitiveWorker`、`symbolToString`→`SymbolToStringWorker`、
//    `getUnionTypeEx`→`GetUnionTypeExWorker`。
// 4) Go 的 `switch { case cond: … }`（无 tag 的 switch）→ C# `if/else if` 链（`tryGetDeclaredTypeOfSymbol`、
//    `computeConstantEnumMemberValue`）；Go 的 `switch node.Kind`/`switch typeKind`（值 switch）保持 `switch`/`if`：
//    `getTypeReferenceName` 用 `switch (node.Kind)`，`getTypeAliasInstantiation` 里
//    `switch typeKind { case …: default: … }` 只有两个分支 → `if/else`（语义一致）。
// 5) Go 的 `range nil-slice`/`range nil-map` 是零次迭代，C# 的 `foreach` 对 null 抛异常 → 遍历处写 `?? []`
//    （`symbol.Declarations`、`node.Members()`、`declarations` 参数、`node.Locals()` 先取局部再判空）。
//    可空解引用按 C# 习惯补 `!`（Go 那边是"逻辑上非 nil"但类型允许 nil 的指针）。
// 6) Go 的 `var memberTypeList []*Type` + `append` → C# `List<Type>` + 传参处 `.ToArray()`（与 C4/C5 片同款）。
//    需要区分"未设置(nil)"与"空"的切片返回值保持可空：`GetInferTypeParameters`、
//    `GetTypeArgumentsForAliasSymbol`、`AppendTypeParameters`、`AppendLocalTypeParametersOfClassOrInterfaceOrTypeAlias`、
//    `GetOuterTypeParameters`、`GetOuterTypeParametersOfClassOrInterface` 都返回 `Type[]?`，
//    Go 的 `nil` ↔ C# 的 `null`（Go 侧 jsx.go:970 就是靠 `typeParams != nil` 判定，不能折成空数组）。
//    例外：`GetLocalTypeParametersOfClassOrInterfaceOrTypeAliasWorker` 的返回标注按已落地的
//    Exports.cs:152 的 public 包装写成非空 `Type[]`（那边是 `Type[] GetLocal… => GetLocal…Worker(symbol);`，
//    写成 `Type[]?` 会让那个文件报 CS8603），但**运行时仍返回 Go 的 nil**；本文件内的使用点多一次判空。
// 7) `evaluator.Result` → `TypeScript.Evaluator.Result`（readonly struct：`Value`/`IsSyntacticallyString`/
//    `ResolvedOtherFiles`/`HasExternalReferences`）；`evaluator.NewResult(...)` → `NewResult(...)`
//    （`using static TypeScript.Evaluator.EvaluatorFunctions;`，见该文件头第 1 条：Evaluator 包的包级函数放在
//    `EvaluatorFunctions` 静态类）；`c.evaluate` 是 Checker 的**字段**（`internal Evaluator _evaluate`，Go 里是
//    `func` 类型字段不是方法）→ `_evaluate(expr, location)`。
// 8) `jsnum.Number` → `TypeScript.JsNum.Number`（readonly struct，有 `implicit operator Number(int)`，所以
//    Go 的 `value + 1` 可直接写 `value + 1`）；Go 的 `new(jsnum.Number)`（指向零值的指针）→
//    `Number? autoValue = new Number();`；`*autoValue` → `autoValue.Value`（Nullable<T>.Value）；
//    `autoValue = nil` → `autoValue = null`；`v.(jsnum.Number)` 的两值断言 → `v is Number value`。
// 9) `&TypeAlias{symbol: x, typeArguments: y}` → `new TypeAlias { _symbol = x, _typeArguments = y }`
//    （Go 未导出字段 → `_camelCase`；与 Checker.C6.Tuples.cs 的既有写法一致）。
// 10) `c.error(node, diagnostics.X, args…)` → `Error(node, X, args…)`（错误 API 归 C4），诊断消息名用
//     `using static TypeScript.Diagnostics.Diagnostics;` 后的裸名（`Diagnostics.X` 会解析到命名空间）。
// 11) `debug.Assert(cond, msg)` → `Debug.Assert(cond, msg)`（`TypeScript.Debug` 在根命名空间，不经 using 解析；
//     禁止 `using System.Diagnostics;`）。
// 12) 包级/核心工具一律**裸名**调用（`using static TypeScript.Core.Core;`）：`core.Find`→`Find`、
//     `core.FirstOrNil`→`FirstOrNil`、`core.AppendIfUnique`→`AppendIfUnique`、`core.IfElse`→`IfElse`、
//     `append(a, b...)`→`Concatenate(a, b)`。**不能**写 `Core.Find(...)`：在 `TypeScript.Checker` 里简单名 `Core`
//     先绑定到命名空间 `TypeScript.Core` → CS0234（仓库既有文件也都是裸名）。
// 13) `getTypeAliasInstantiation` 里 Go 的 `instantiation := links.instantiations[key]`（nil map/缺失键都返回零值）
//     → `TryGetValue`（C# 索引器读缺失键抛异常）。
namespace TypeScript.Checker;

using System.Collections.Generic;
using TypeScript.Ast;
using TypeScript.Core;
using TypeScript.Diagnostics;
using TypeScript.Evaluator;
using TypeScript.JsNum;
using static TypeScript.Ast.Ast;
using static TypeScript.Core.Core;
using static TypeScript.Diagnostics.Diagnostics;
using static TypeScript.Evaluator.EvaluatorFunctions;

// 消歧（探针实测）：usings 在 file-scoped namespace 之前时，`Core.X` 会先在外层命名空间链里命中
// **命名空间** TypeScript.Core（CS0234）。命名空间声明体内的 using 别名优先于外层命名空间 →
// `Core.Map/OrElse/IfElse/...` 绑到静态类；`using static TypeScript.Core.Core;` 的裸名调用不受影响。
using Core = TypeScript.Core.Core;

public sealed partial class Checker
{
    /// <summary>Go: <c>func (c *Checker) getTypeAliasInstantiation(symbol *ast.Symbol, typeArguments []*Type, alias *TypeAlias) *Type</c>（checker.go:24036）。</summary>
    internal Type GetTypeAliasInstantiation(Symbol symbol, Type[] typeArguments, TypeAlias? alias)
    {
        var t = GetDeclaredTypeOfSymbolWorker(symbol);
        if (t == _intrinsicMarkerType)
        {
            if (IntrinsicTypeKinds.TryGetValue(symbol.Name, out var typeKind) && typeArguments.Length == 1)
            {
                if (typeKind == IntrinsicTypeKind.NoInfer)
                {
                    return GetNoInferType(typeArguments[0]);
                }
                else
                {
                    return GetStringMappingType(symbol, typeArguments[0]);
                }
            }
        }
        var links = _typeAliasLinks.Get(symbol);
        var typeParameters = links._typeParameters!;
        var key = GetTypeAliasInstantiationKey(typeArguments, alias);
        Type? instantiation = null;
        var instantiations = links._instantiations;
        if (instantiations != null)
        {
            instantiations.TryGetValue(key, out instantiation);
        }
        if (instantiation == null)
        {
            var mapper = NewTypeMapper(typeParameters, FillMissingTypeArgumentsWorker(typeArguments, typeParameters, GetMinTypeArgumentCountWorker(typeParameters), IsInJSFile(symbol.ValueDeclaration))!);
            instantiation = InstantiateTypeWithAlias(t, mapper, alias);
            links._instantiations![key] = instantiation;
        }
        return instantiation;
    }

    /// <summary>Go: <c>func isLocalTypeAlias(symbol *ast.Symbol) bool</c>（checker.go:24060；包级函数 → static 成员）。</summary>
    internal static bool IsLocalTypeAlias(Symbol symbol)
    {
        Node? declaration = Find(symbol.Declarations, IsTypeAlias);
        return declaration != null && GetContainingFunction(declaration) != null;
    }

    /// <summary>Go: <c>func (c *Checker) getDeclaredTypeOfSymbol(symbol *ast.Symbol) *Type</c>（checker.go:24065；Worker 改名见文件头 3）。</summary>
    internal Type GetDeclaredTypeOfSymbolWorker(Symbol symbol)
    {
        var result = TryGetDeclaredTypeOfSymbol(symbol);
        if (result == null)
        {
            result = _errorType;
        }
        return result;
    }

    /// <summary>Go: <c>func (c *Checker) tryGetDeclaredTypeOfSymbol(symbol *ast.Symbol) *Type</c>（checker.go:24073）。</summary>
    internal Type? TryGetDeclaredTypeOfSymbol(Symbol symbol)
    {
        if ((symbol.Flags & (SymbolFlags.Class | SymbolFlags.Interface)) != 0)
        {
            return GetDeclaredTypeOfClassOrInterface(symbol);
        }
        if ((symbol.Flags & SymbolFlags.TypeParameter) != 0)
        {
            return GetDeclaredTypeOfTypeParameter(symbol);
        }
        if ((symbol.Flags & SymbolFlags.TypeAlias) != 0)
        {
            return GetDeclaredTypeOfTypeAlias(symbol);
        }
        if ((symbol.Flags & SymbolFlags.Enum) != 0)
        {
            return GetDeclaredTypeOfEnum(symbol);
        }
        if ((symbol.Flags & SymbolFlags.EnumMember) != 0)
        {
            return GetDeclaredTypeOfEnumMember(symbol);
        }
        if ((symbol.Flags & SymbolFlags.Alias) != 0)
        {
            return GetDeclaredTypeOfAlias(symbol);
        }
        return null;
    }

    /// <summary>Go: <c>func getTypeReferenceName(node *ast.Node) *ast.Node</c>（checker.go:24091；包级函数 → static 成员）。</summary>
    internal static Node? GetTypeReferenceName(Node node)
    {
        switch (node.Kind)
        {
            case Kind.TypeReference:
                return node.AsTypeReferenceNode()!.TypeName;
            case Kind.ExpressionWithTypeArguments:
            {
                // We only support expressions that are simple qualified names. For other
                // expressions this produces nil
                var expr = node.Expression();
                if (IsEntityNameExpression(expr))
                {
                    return expr;
                }
                break;
            }
        }
        return null;
    }

    /// <summary>Go: <c>func (c *Checker) getAliasForTypeNode(node *ast.Node) *TypeAlias</c>（checker.go:24106）。</summary>
    internal TypeAlias? GetAliasForTypeNode(Node node)
    {
        var symbol = GetAliasSymbolForTypeNode(node);
        if (symbol != null)
        {
            return new TypeAlias { _symbol = symbol, _typeArguments = GetTypeArgumentsForAliasSymbol(symbol) };
        }
        return null;
    }

    /// <summary>Go: <c>func (c *Checker) getAliasSymbolForTypeNode(node *ast.Node) *ast.Symbol</c>（checker.go:24114）。</summary>
    internal Symbol? GetAliasSymbolForTypeNode(Node node)
    {
        var host = node.Parent!;
        while (IsParenthesizedTypeNode(host) || IsTypeOperatorNode(host) && host.AsTypeOperatorNode()!.Operator == Kind.ReadonlyKeyword)
        {
            host = host.Parent!;
        }
        if (IsTypeAlias(host))
        {
            return GetSymbolOfDeclaration(host);
        }
        return null;
    }

    /// <summary>Go: <c>func (c *Checker) getTypeArgumentsForAliasSymbol(symbol *ast.Symbol) []*Type</c>（checker.go:24125）。</summary>
    internal Type[]? GetTypeArgumentsForAliasSymbol(Symbol? symbol)
    {
        if (symbol != null)
        {
            return GetLocalTypeParametersOfClassOrInterfaceOrTypeAliasWorker(symbol);
        }
        return null;
    }

    /// <summary>Go: <c>func (c *Checker) getOuterTypeParametersOfClassOrInterface(symbol *ast.Symbol) []*Type</c>（checker.go:24132）。</summary>
    internal Type[]? GetOuterTypeParametersOfClassOrInterface(Symbol symbol)
    {
        var declaration = GetClassOrInterfaceLikeDeclaration(symbol);
        Debug.Assert(declaration != null, "Class was missing valueDeclaration -OR- non-class had no interface declarations");
        return GetOuterTypeParameters(declaration!, false /*includeThisTypes*/);
    }

    /// <summary>Returns the declaration used to obtain a class, interface, or function symbol's outer type parameters.
    /// Go: <c>func (c *Checker) getClassOrInterfaceLikeDeclaration(symbol *ast.Symbol) *ast.Node</c>（checker.go:24139）。</summary>
    internal Node? GetClassOrInterfaceLikeDeclaration(Symbol symbol)
    {
        if ((symbol.Flags & (SymbolFlags.Class | SymbolFlags.Function)) != 0)
        {
            return symbol.ValueDeclaration;
        }
        return Find(symbol.Declarations, d =>
        {
            if (IsInterfaceDeclaration(d))
            {
                return true;
            }
            if (!IsVariableDeclaration(d))
            {
                return false;
            }
            var initializer = d.Initializer();
            return initializer != null && IsFunctionExpressionOrArrowFunction(initializer);
        });
    }

    /// <summary>Go: <c>func (c *Checker) canGetTypeParametersOfClassOrInterface(symbol *ast.Symbol) bool</c>（checker.go:24155）。</summary>
    internal bool CanGetTypeParametersOfClassOrInterface(Symbol symbol)
    {
        return GetClassOrInterfaceLikeDeclaration(symbol) != null;
    }

    /// <summary>Return the outer type parameters of a node or undefined if the node has no outer type parameters.
    /// Go: <c>func (c *Checker) getOuterTypeParameters(node *ast.Node, includeThisTypes bool) []*Type</c>（checker.go:24160）。</summary>
    internal Type[]? GetOuterTypeParameters(Node node, bool includeThisTypes)
    {
        while (true)
        {
            node = node.Parent!;
            if (node == null)
            {
                return null;
            }
            var kind = node.Kind;
            if (kind == Kind.ClassDeclaration || kind == Kind.ClassExpression || kind == Kind.InterfaceDeclaration || kind == Kind.CallSignature
                || kind == Kind.ConstructSignature || kind == Kind.MethodSignature || kind == Kind.FunctionType || kind == Kind.ConstructorType
                || kind == Kind.FunctionDeclaration || kind == Kind.MethodDeclaration || kind == Kind.FunctionExpression || kind == Kind.ArrowFunction
                || kind == Kind.TypeAliasDeclaration || kind == Kind.JSTypeAliasDeclaration || kind == Kind.MappedType || kind == Kind.ConditionalType)
            {
                var outerTypeParameters = GetOuterTypeParameters(node, includeThisTypes);
                if ((kind == Kind.FunctionExpression || kind == Kind.ArrowFunction || IsObjectLiteralMethod(node)) && IsContextSensitiveWorker(node))
                {
                    var signature = FirstOrNil(GetSignaturesOfTypeWorker(GetTypeOfSymbolWorker(GetSymbolOfDeclaration(node)!), SignatureKind.Call));
                    if (signature != null && (signature._typeParameters?.Length ?? 0) != 0)
                    {
                        return Concatenate(outerTypeParameters, signature._typeParameters);
                    }
                }
                if (kind == Kind.MappedType)
                {
                    return Concatenate(outerTypeParameters, [GetDeclaredTypeOfTypeParameter(GetSymbolOfDeclaration(node.AsMappedTypeNode()!.TypeParameter!)!)]);
                }
                if (kind == Kind.ConditionalType)
                {
                    return Concatenate(outerTypeParameters, GetInferTypeParameters(node));
                }
                var outerAndOwnTypeParameters = AppendTypeParameters(outerTypeParameters, node.TypeParameters());
                Type? thisType = null;
                if (includeThisTypes && (kind == Kind.ClassDeclaration || kind == Kind.ClassExpression || kind == Kind.InterfaceDeclaration))
                {
                    thisType = GetDeclaredTypeOfClassOrInterface(GetSymbolOfDeclaration(node)!).AsInterfaceType()!._thisType;
                }
                if (thisType != null)
                {
                    return Concatenate(outerAndOwnTypeParameters, [thisType]);
                }
                return outerAndOwnTypeParameters;
            }
        }
    }

    /// <summary>Go: <c>func (c *Checker) getInferTypeParameters(node *ast.Node) []*Type</c>（checker.go:24198）。</summary>
    internal Type[]? GetInferTypeParameters(Node node)
    {
        List<Type>? result = null;
        var locals = node.Locals();
        if (locals != null)
        {
            foreach (var symbol in locals.Values)
            {
                if ((symbol.Flags & SymbolFlags.TypeParameter) != 0)
                {
                    result ??= [];
                    result.Add(GetDeclaredTypeOfSymbolWorker(symbol));
                }
            }
        }
        return result?.ToArray();
    }

    /// <summary>The local type parameters are the combined set of type parameters from all declarations of the class,
    /// interface, or type alias.
    /// Go: <c>func (c *Checker) getLocalTypeParametersOfClassOrInterfaceOrTypeAlias(symbol *ast.Symbol) []*Type</c>（checker.go:24210；Worker 改名见文件头 3/6）。</summary>
    internal Type[] GetLocalTypeParametersOfClassOrInterfaceOrTypeAliasWorker(Symbol symbol)
    {
        return AppendLocalTypeParametersOfClassOrInterfaceOrTypeAlias(null, symbol)!;
    }

    /// <summary>Go: <c>func (c *Checker) appendLocalTypeParametersOfClassOrInterfaceOrTypeAlias(types []*Type, symbol *ast.Symbol) []*Type</c>（checker.go:24214）。</summary>
    internal Type[]? AppendLocalTypeParametersOfClassOrInterfaceOrTypeAlias(Type[]? types, Symbol symbol)
    {
        foreach (var node in symbol.Declarations ?? [])
        {
            if (NodeKindIs(node, Kind.InterfaceDeclaration, Kind.ClassDeclaration, Kind.ClassExpression) || IsTypeAlias(node))
            {
                types = AppendTypeParameters(types, node.TypeParameters());
            }
        }
        return types;
    }

    /// <summary>Appends the type parameters given by a list of declarations to a set of type parameters and returns the resulting set.
    /// The function allocates a new array if the input type parameter set is undefined, but otherwise it modifies the set
    /// in-place and returns the same array.
    /// Go: <c>func (c *Checker) appendTypeParameters(typeParameters []*Type, declarations []*ast.Node) []*Type</c>（checker.go:24226）。</summary>
    internal Type[]? AppendTypeParameters(Type[]? typeParameters, Node[]? declarations)
    {
        foreach (var declaration in declarations ?? [])
        {
            typeParameters = AppendIfUnique(typeParameters, GetDeclaredTypeOfTypeParameter(GetSymbolOfDeclaration(declaration)!));
        }
        return typeParameters;
    }

    /// <summary>Go: <c>func (c *Checker) getDeclaredTypeOfTypeParameter(symbol *ast.Symbol) *Type</c>（checker.go:24233）。</summary>
    internal Type GetDeclaredTypeOfTypeParameter(Symbol symbol)
    {
        var links = _declaredTypeLinks.Get(symbol);
        if (links._declaredType == null)
        {
            links._declaredType = NewTypeParameter(symbol);
        }
        return links._declaredType;
    }

    /// <summary>Go: <c>func (c *Checker) getDeclaredTypeOfTypeAlias(symbol *ast.Symbol) *Type</c>（checker.go:24241）。</summary>
    internal Type GetDeclaredTypeOfTypeAlias(Symbol symbol)
    {
        var links = _typeAliasLinks.Get(symbol);
        if (links._declaredType == null)
        {
            // Note that we use the links object as the target here because the symbol object is used as the unique
            // identity for resolution of the 'type' property in SymbolLinks.
            if (!PushTypeResolution(symbol, TypeSystemPropertyName.DeclaredType))
            {
                return _errorType;
            }
            var declaration = Find(symbol.Declarations, IsTypeOrJSTypeAliasDeclaration);
            var typeNode = declaration.Type()!;
            var t = GetTypeFromTypeNodeWorker(typeNode);
            if (PopTypeResolution())
            {
                var typeParameters = GetLocalTypeParametersOfClassOrInterfaceOrTypeAliasWorker(symbol);
                if (typeParameters != null && typeParameters.Length != 0)
                {
                    // Initialize the instantiation cache for generic type aliases. The declared type corresponds to
                    // an instantiation of the type alias with the type parameters supplied as type arguments.
                    links._typeParameters = typeParameters;
                    links._instantiations = new Dictionary<CacheHashKey, Type>();
                    links._instantiations[GetTypeListKey(typeParameters)] = t;
                }
                if (t == _intrinsicMarkerType && symbol.Name == "BuiltinIteratorReturn")
                {
                    t = GetBuiltinIteratorReturnType();
                }
            }
            else
            {
                var errorNode = declaration.Name();
                if (errorNode == null)
                {
                    errorNode = declaration;
                }
                Error(errorNode, Type_alias_0_circularly_references_itself, SymbolToStringWorker(symbol));
                t = _errorType;
            }
            if (links._declaredType == null)
            {
                links._declaredType = t;
            }
        }
        return links._declaredType!;
    }

    /// <summary>Go: <c>func (c *Checker) getDeclaredTypeOfEnum(symbol *ast.Symbol) *Type</c>（checker.go:24279）。</summary>
    internal Type GetDeclaredTypeOfEnum(Symbol symbol)
    {
        var links = _declaredTypeLinks.Get(symbol);
        if (links._declaredType == null)
        {
            var memberTypeList = new List<Type>();
            foreach (var declaration in symbol.Declarations ?? [])
            {
                if (declaration.Kind == Kind.EnumDeclaration)
                {
                    foreach (var member in declaration.Members() ?? [])
                    {
                        if (!HasDynamicName(member))
                        {
                            var memberSymbol = GetSymbolOfDeclaration(member)!;
                            var value = GetEnumMemberValue(member).Value;
                            Type memberType;
                            if (value != null)
                            {
                                memberType = GetEnumLiteralType(value, symbol, memberSymbol);
                            }
                            else
                            {
                                memberType = CreateComputedEnumType(memberSymbol);
                            }
                            _declaredTypeLinks.Get(memberSymbol)._declaredType = GetFreshTypeOfLiteralType(memberType);
                            memberTypeList.Add(memberType);
                        }
                    }
                }
            }
            Type enumType;
            if (memberTypeList.Count != 0)
            {
                enumType = GetUnionTypeExWorker(memberTypeList.ToArray(), UnionReduction.Literal, new TypeAlias { _symbol = symbol }, null /*origin*/);
            }
            else
            {
                enumType = CreateComputedEnumType(symbol);
            }
            if ((enumType._flags & TypeFlags.Union) != 0)
            {
                enumType._flags |= TypeFlags.EnumLiteral;
                enumType._symbol = symbol;
            }
            links._declaredType = enumType;
        }
        return links._declaredType;
    }

    /// <summary>Go: <c>func (c *Checker) getEnumMemberValue(node *ast.Node) evaluator.Result</c>（checker.go:24316）。</summary>
    internal Result GetEnumMemberValue(Node node)
    {
        ComputeEnumMemberValues(node.Parent!);
        return _enumMemberLinks.Get(node)._value;
    }

    /// <summary>Go: <c>func (c *Checker) createComputedEnumType(symbol *ast.Symbol) *Type</c>（checker.go:24321）。</summary>
    internal Type CreateComputedEnumType(Symbol symbol)
    {
        var regularType = NewLiteralType(TypeFlags.Enum, null, null);
        regularType._symbol = symbol;
        var freshType = NewLiteralType(TypeFlags.Enum, null, regularType);
        freshType._symbol = symbol;
        regularType.AsLiteralType()!._freshType = freshType;
        freshType.AsLiteralType()!._freshType = freshType;
        return regularType;
    }

    /// <summary>Go: <c>func (c *Checker) getDeclaredTypeOfEnumMember(symbol *ast.Symbol) *Type</c>（checker.go:24331）。</summary>
    internal Type GetDeclaredTypeOfEnumMember(Symbol symbol)
    {
        var links = _declaredTypeLinks.Get(symbol);
        if (links._declaredType == null)
        {
            var enumType = GetDeclaredTypeOfEnum(GetParentOfSymbol(symbol)!);
            if (links._declaredType == null)
            {
                links._declaredType = enumType;
            }
        }
        return links._declaredType;
    }

    /// <summary>Go: <c>func (c *Checker) computeEnumMemberValues(node *ast.Node)</c>（checker.go:24342）。</summary>
    internal void ComputeEnumMemberValues(Node node)
    {
        var nodeLinks = _nodeLinks.Get(node);
        if ((nodeLinks._flags & NodeCheckFlags.EnumValuesComputed) == 0)
        {
            nodeLinks._flags |= NodeCheckFlags.EnumValuesComputed;
            // Go: autoValue := new(jsnum.Number)（指向零值的指针；nil 表示"没有自动值"）
            Number? autoValue = new Number();
            Node? previous = null;
            foreach (var member in node.Members() ?? [])
            {
                var result = ComputeEnumMemberValue(member, autoValue, previous);
                _enumMemberLinks.Get(member)._value = result;
                if (result.Value is Number value)
                {
                    Number nextValue = value + 1;
                    autoValue = nextValue;
                }
                else
                {
                    autoValue = null;
                }
                previous = member;
            }
        }
    }

    /// <summary>Go: <c>func (c *Checker) computeEnumMemberValue(member *ast.Node, autoValue *jsnum.Number, previous *ast.Node) evaluator.Result</c>（checker.go:24362）。</summary>
    internal Result ComputeEnumMemberValue(Node member, Number? autoValue, Node? previous)
    {
        if (IsComputedNonLiteralName(member.Name()))
        {
            Error(member.Name(), Computed_property_names_are_not_allowed_in_enums);
        }
        else if (IsBigIntLiteral(member.Name()!))
        {
            Error(member.Name(), An_enum_member_cannot_have_a_numeric_name);
        }
        else
        {
            var text = GetTextOfPropertyName(member.Name());
            if (IsNumericLiteralName(text) && !IsInfinityOrNaNString(text))
            {
                Error(member.Name(), An_enum_member_cannot_have_a_numeric_name);
            }
        }
        if (member.Initializer() != null)
        {
            return ComputeConstantEnumMemberValue(member);
        }
        // In ambient non-const numeric enum declarations, enum members without initializers are
        // considered computed members (as opposed to having auto-incremented values).
        if ((member.Parent!.Flags & NodeFlags.Ambient) != 0 && !IsEnumConst(member.Parent))
        {
            return NewResult(null, false, false, false);
        }
        // If the member declaration specifies no value, the member is considered a constant enum member.
        // If the member is the first member in the enum declaration, it is assigned the value zero.
        // Otherwise, it is assigned the value of the immediately preceding member plus one, and an error
        // occurs if the immediately preceding member is not a constant enum member.
        if (autoValue == null)
        {
            Error(member.Name(), Enum_member_must_have_initializer);
            return NewResult(null, false, false, false);
        }
        if (_compilerOptions.GetIsolatedModules() && previous != null && previous.Initializer() != null)
        {
            var prevValue = GetEnumMemberValue(previous);
            var prevIsNum = prevValue.Value is Number;
            if (!prevIsNum || prevValue.ResolvedOtherFiles)
            {
                Error(member.Name(), Enum_member_following_a_non_literal_numeric_member_must_have_an_initializer_when_isolatedModules_is_enabled);
            }
        }
        return NewResult(autoValue.Value, false, false, false);
    }

    /// <summary>Go: <c>func (c *Checker) computeConstantEnumMemberValue(member *ast.Node) evaluator.Result</c>（checker.go:24399）。</summary>
    internal Result ComputeConstantEnumMemberValue(Node member)
    {
        var isConstEnum = IsEnumConst(member.Parent);
        var initializer = member.Initializer()!;
        var result = _evaluate(initializer, member);
        if (result.Value != null)
        {
            if (isConstEnum)
            {
                if (result.Value is Number numValue && (numValue.IsInf() || numValue.IsNaN()))
                {
                    Error(initializer, IfElse(numValue.IsNaN(),
                        X_const_enum_member_initializer_was_evaluated_to_disallowed_value_NaN,
                        X_const_enum_member_initializer_was_evaluated_to_a_non_finite_value));
                }
            }
            if (_compilerOptions.GetIsolatedModules())
            {
                if (result.Value is string && !result.IsSyntacticallyString)
                {
                    var memberName = member.Parent!.Name()!.Text() + "." + member.Name()!.Text();
                    Error(initializer, X_0_has_a_string_type_but_must_have_syntactically_recognizable_string_syntax_when_isolatedModules_is_enabled, memberName);
                }
            }
        }
        else if (isConstEnum)
        {
            Error(initializer, X_const_enum_member_initializers_must_be_constant_expressions);
        }
        else if ((member.Parent!.Flags & NodeFlags.Ambient) != 0)
        {
            Error(initializer, In_ambient_enum_declarations_member_initializer_must_be_constant_expression);
        }
        else
        {
            CheckTypeAssignableTo(CheckExpression(initializer), _numberType!, initializer, Type_0_is_not_assignable_to_type_1_as_required_for_computed_enum_member_values);
        }
        return result;
    }
}
