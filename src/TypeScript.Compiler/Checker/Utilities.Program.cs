// ported from: internal/checker/utilities.go:1598-1910（名字/导入语法/包映射/诊断细节/程序辅助）
//
// 与 Go 的机械改写（通用规则见 Utilities.cs 文件头）：
// 1) Go 的 `core.ResolutionMode` 是 `ModuleKind` 的类型别名（Core/CompilerOptions.cs:526-528 已裁定 C# 一律写
//    `ModuleKind`）——`CreateModuleNotFoundChain` 的 `mode` 参数即此。
// 2) Go 的 `c.ctx != nil && c.ctx.Err() != nil`（context.Context）→ C# 的 `_ctx is not null &&
//    _ctx.CancellationToken.IsCancellationRequested`（`Context` 是 TypeScript.Core.Context，ChkC1 已确认）。
// 3) `getPackagesMap` 里 Go 的 `for _, module := range resolvedModulesInFile` 把 `module` **包名**遮蔽成局部变量；
//    C# 侧把局部变量改名为 `resolvedModule`（否则 `module.PackageId` 会与命名空间 `TypeScript.Module` 无关地
//    遮蔽 `Module` 静态类，且同名的局部变量会挡住 `Module.GetTypesPackageName` 之类的静态导入）。语义不变。
// 4) `map[K]V` 读缺失键 → `GetValueOrDefault`（§3.2）；`map[K]V` 的 `_, ok := m[k]` → `ContainsKey`。
// 5) `strings.Contains(s, sub)` → `string.Contains(sub)`（.NET 的 string 重载默认序数比较，与 Go 的字节包含一致）。
// 6) `Switch` 型类型分支（`ValueToString` 的 Go type switch）→ C# 的 `switch (value) { case T t: ... }` 模式匹配。
// 7) `ast.IsClassLike` 等 Ast 谓词、`Diagnostics.*` 消息常量、`TSPath.Extension*`、`Module.GetTypesPackageName`
//    都用 `using static` 后的裸名（禁止写 `Ast.X`/`Module.X`：会先解析到同级命名空间，见 Parser 文件头第 4 条）。
namespace TypeScript.Checker;

using TypeScript.Ast;
using TypeScript.Core;
using TypeScript.Diagnostics;
using TypeScript.JsNum;
using TypeScript.Printer;
using static TypeScript.Ast.Ast;
using static TypeScript.Core.Core;
using static TypeScript.Diagnostics.Diagnostics;
using static TypeScript.Module.Module;
using static TypeScript.Printer.Printer;
using static TypeScript.Scanner.Scanner;
using static TypeScript.TSPath.TSPath;

/// <summary>
/// Go: <c>type DiagnosticDetails struct { Message *diagnostics.Message; Args []any }</c>（utilities.go:1819-1822）。
/// 解析后的诊断消息 + 实参，用于在 checker 与增量 builder 之间共享"诊断链计算"的结果。
/// </summary>
public sealed class DiagnosticDetails
{
    // Go 的导出字段 → C# 公开同名字段（api-parity）。构造点用对象初始化器（Go 的具名字段复合字面量），
    // 所以 `Message` 给 `null!` 的默认值以满足非空字段的初始化要求（用法上与 Go 的零值一致：构造点必填）。
    public Message Message = null!;
    public object?[]? Args;
}

public sealed partial class Checker
{
    /// <summary>Go: <c>func rangeOfTypeParameters(sourceFile *ast.SourceFile, typeParameters *ast.NodeList) core.TextRange</c>（utilities.go:1598-1600）。</summary>
    internal static TextRange RangeOfTypeParameters(SourceFile sourceFile, NodeList typeParameters)
    {
        return NewTextRange(typeParameters.Pos() - 1, Math.Min(sourceFile.Text().Length, SkipTrivia(sourceFile.Text(), typeParameters.End()) + 1));
    }

    /// <summary>Go: <c>func tryGetPropertyAccessOrIdentifierToString(expr *ast.Node) string</c>（utilities.go:1602-1620）。</summary>
    internal static string TryGetPropertyAccessOrIdentifierToString(Node expr)
    {
        if (IsPropertyAccessExpression(expr))
        {
            var baseStr = TryGetPropertyAccessOrIdentifierToString(expr.Expression()!);
            if (baseStr != "")
            {
                return baseStr + "." + EntityNameToString(expr.Name()!);
            }
        }
        else if (IsElementAccessExpression(expr))
        {
            var baseStr = TryGetPropertyAccessOrIdentifierToString(expr.Expression()!);
            if (baseStr != "" && IsPropertyName(expr.AsElementAccessExpression()!.ArgumentExpression))
            {
                return baseStr + "." + GetPropertyNameForPropertyNameNode(expr.AsElementAccessExpression()!.ArgumentExpression!);
            }
        }
        else if (IsIdentifier(expr))
        {
            return expr.Text();
        }
        else if (IsJsxNamespacedName(expr))
        {
            return EntityNameToString(expr);
        }
        return "";
    }

    /// <summary>Go: <c>func allDeclarationsInSameSourceFile(symbol *ast.Symbol) bool</c>（utilities.go:1622-1634）。</summary>
    internal static bool AllDeclarationsInSameSourceFile(Symbol symbol)
    {
        var declarations = symbol.Declarations;
        if ((declarations?.Length ?? 0) > 1)
        {
            SourceFile? sourceFile = null;
            for (var i = 0; i < declarations!.Length; i++)
            {
                var d = declarations[i];
                if (i == 0)
                {
                    sourceFile = GetSourceFileOfNode(d);
                }
                else if (GetSourceFileOfNode(d) != sourceFile)
                {
                    return false;
                }
            }
        }
        return true;
    }

    /// <summary>Go: <c>func containsNonMissingUndefinedType(c *Checker, t *Type) bool</c>（utilities.go:1636-1644）。</summary>
    internal static bool ContainsNonMissingUndefinedType(Checker c, Type t)
    {
        var candidate = (t._flags & TypeFlags.Union) != 0 ? t.AsUnionType()!._types![0] : t;
        return (candidate._flags & TypeFlags.Undefined) != 0 && candidate != c._missingType;
    }

    /// <summary>Go: <c>func getAnyImportSyntax(node *ast.Node) *ast.Node</c>（utilities.go:1646-1664）。</summary>
    internal static Node? GetAnyImportSyntax(Node node)
    {
        Node? importNode;
        switch (node.Kind)
        {
            case Kind.ImportEqualsDeclaration:
                importNode = node;
                break;
            case Kind.ImportClause:
                importNode = node.Parent;
                break;
            case Kind.NamespaceImport:
                importNode = node.Parent!.Parent;
                break;
            case Kind.ImportSpecifier:
                importNode = node.Parent!.Parent!.Parent;
                break;
            default:
                return null;
        }
        return importNode;
    }

    /// <summary>
    /// Go: <c>func isReservedMemberName(name string) bool</c>（utilities.go:1666-1668）。
    /// 保留成员名由哨兵字符（Go 侧是非法 UTF-8 字节 0xFE，C# 侧见 Ast.InternalSymbolNamePrefix = U+FFFD）
    /// + 一个非 '@'/'#' 字符开头；'@' 表示知名 ES Symbol，'#' 表示私有标识符。
    /// </summary>
    internal static bool IsReservedMemberName(string name)
    {
        return name.Length >= 2 && name[0] == '\uFFFD' && name[1] != '@' && name[1] != '#';
    }

    /// <summary>Go: <c>func introducesArgumentsExoticObject(node *ast.Node) bool</c>（utilities.go:1670-1677）。</summary>
    internal static bool IntroducesArgumentsExoticObject(Node node)
    {
        switch (node.Kind)
        {
            case Kind.MethodDeclaration:
            case Kind.MethodSignature:
            case Kind.Constructor:
            case Kind.GetAccessor:
            case Kind.SetAccessor:
            case Kind.FunctionDeclaration:
            case Kind.FunctionExpression:
                return true;
        }
        return false;
    }

    /// <summary>Go: <c>func symbolsToArray(symbols ast.SymbolTable) []*ast.Symbol</c>（utilities.go:1679-1687）。</summary>
    internal static Symbol[] SymbolsToArray(SymbolTable symbols)
    {
        // Go 的 nil 切片与空切片在调用点等价（len/range），C# 统一返回空数组。
        List<Symbol>? result = null;
        foreach (var entry in symbols)
        {
            if (!IsReservedMemberName(entry.Key))
            {
                (result ??= []).Add(entry.Value);
            }
        }
        return result is null ? [] : result.ToArray();
    }

    /// <summary>Go: <c>func SkipAlias(symbol *ast.Symbol, checker *Checker) *ast.Symbol</c>（utilities.go:1689-1695）。</summary>
    public static Symbol SkipAlias(Symbol symbol, Checker checker)
    {
        if ((symbol.Flags & SymbolFlags.Alias) != 0)
        {
            return checker.GetAliasedSymbol(symbol);
        }
        return symbol;
    }

    /// <summary>Go: <c>func IsExternalModuleSymbol(moduleSymbol *ast.Symbol) bool</c>（utilities.go:1697-1699）。符号是外部模块（而非命名空间）时为真。</summary>
    public static bool IsExternalModuleSymbol(Symbol moduleSymbol)
    {
        return moduleSymbol.IsExternalModule();
    }

    /// <summary>Go: <c>func (c *Checker) isCanceled() bool</c>（utilities.go:1701-1703）。</summary>
    internal bool IsCanceled()
    {
        return _ctx is not null && _ctx.CancellationToken.IsCancellationRequested;
    }

    /// <summary>Go: <c>func (c *Checker) checkNotCanceled()</c>（utilities.go:1705-1709）。</summary>
    internal void CheckNotCanceled()
    {
        if (_wasCanceled)
        {
            Debug.Fail("Checker was previously cancelled");
        }
    }

    /// <summary>Go: <c>func (c *Checker) getPackagesMap() map[string]bool</c>（utilities.go:1711-1724）。</summary>
    internal Dictionary<string, bool> GetPackagesMap()
    {
        var packagesMap = _packagesMap;
        if (packagesMap is null)
        {
            packagesMap = [];
            var resolvedModules = _program.GetResolvedModules();
            foreach (var resolvedModulesInFile in resolvedModules.Values)
            {
                foreach (var resolvedModule in resolvedModulesInFile.Values)
                {
                    if (resolvedModule.PackageId.Name != "")
                    {
                        packagesMap[resolvedModule.PackageId.Name] = packagesMap.GetValueOrDefault(resolvedModule.PackageId.Name) || resolvedModule.Extension == ExtensionDts;
                    }
                }
            }
            _packagesMap = packagesMap;
        }
        return packagesMap;
    }

    /// <summary>Go: <c>func (c *Checker) typesPackageExists(packageName string) bool</c>（utilities.go:1726-1730）。</summary>
    internal bool TypesPackageExists(string packageName)
    {
        var packagesMap = GetPackagesMap();
        return packagesMap.ContainsKey(GetTypesPackageName(packageName));
    }

    /// <summary>Go: <c>func (c *Checker) packageBundlesTypes(packageName string) bool</c>（utilities.go:1732-1736）。</summary>
    internal bool PackageBundlesTypes(string packageName)
    {
        var packagesMap = GetPackagesMap();
        return packagesMap.GetValueOrDefault(packageName);
    }

    /// <summary>Go: <c>func ValueToString(value any) string</c>（utilities.go:1738-1750）。</summary>
    public static string ValueToString(object? value)
    {
        switch (value)
        {
            case string s:
                return "\"" + EscapeString(s, QuoteChar.DoubleQuote) + "\"";
            case Number n:
                return n.String();
            case bool b:
                return IfElse(b, "true", "false");
            case PseudoBigInt bigInt:
                return bigInt.String() + "n";
        }
        Debug.Fail("unhandled value type in valueToString");
        throw new System.Diagnostics.UnreachableException();
    }

    /// <summary>Go: <c>func nodeStartsNewLexicalEnvironment(node *ast.Node) bool</c>（utilities.go:1752-1764）。</summary>
    internal static bool NodeStartsNewLexicalEnvironment(Node node)
    {
        switch (node.Kind)
        {
            case Kind.Constructor:
            case Kind.FunctionExpression:
            case Kind.FunctionDeclaration:
            case Kind.ArrowFunction:
            case Kind.MethodDeclaration:
            case Kind.GetAccessor:
            case Kind.SetAccessor:
            case Kind.ModuleDeclaration:
            case Kind.SourceFile:
                return true;
        }
        return false;
    }

    /// <summary>
    /// Go: <c>func (c *Checker) isUncheckedJSSuggestion(node *ast.Node, suggestion *ast.Symbol, excludeClasses bool) bool</c>（utilities.go:1766-1794）。
    /// 判断"你是不是想写"错误在未检查的 JS 文件里是否应降级为 suggestion。仅适用于没有 checkJS / `// @ts-check` /
    /// `// @ts-nocheck` 的未检查 JS 文件；不适用于：(1) 来自与引用文件不同的全局文件的建议；(2)（可选）类或 `this.x`
    /// 属性访问表达式。
    /// </summary>
    internal bool IsUncheckedJSSuggestion(Node? node, Symbol? suggestion, bool excludeClasses)
    {
        var file = GetSourceFileOfNode(node);
        if (file is not null)
        {
            if (_compilerOptions.CheckJs.IsUnknown() && file.CheckJsDirective is null && (file.ScriptKind == ScriptKind.JS || file.ScriptKind == ScriptKind.JSX))
            {
                SourceFile? declarationFile = null;
                if (suggestion is not null)
                {
                    var firstDeclaration = FirstOrNil(suggestion.Declarations);
                    if (firstDeclaration is not null)
                    {
                        declarationFile = GetSourceFileOfNode(firstDeclaration);
                    }
                }
                var suggestionHasNoExtendsOrDecorators = suggestion is null ||
                    suggestion.ValueDeclaration is null ||
                    !IsClassLike(suggestion.ValueDeclaration) ||
                    (GetExtendsHeritageClauseElements(suggestion.ValueDeclaration)?.Length ?? 0) != 0 ||
                    ClassOrConstructorParameterIsDecorated(false, suggestion.ValueDeclaration);
                return !(file != declarationFile && declarationFile is not null && IsGlobalSourceFile(declarationFile.AsNode())) &&
                    !(excludeClasses && suggestion is not null && (suggestion.Flags & SymbolFlags.Class) != 0 && suggestionHasNoExtendsOrDecorators) &&
                    !(node is not null && excludeClasses && IsPropertyAccessExpression(node) && node.Expression()!.Kind == Kind.ThisKeyword && suggestionHasNoExtendsOrDecorators);
            }
        }
        return false;
    }

    /// <summary>
    /// Go: <c>func (c *Checker) isJSLiteralType(t *Type) bool</c>（utilities.go:1796-1817）。
    /// 判断类型是否是（或由）JSLiteral 对象类型（构成）：字面量本身、每个成员都是 JSLiteral 的 union、
    /// 至少一个成员是 JSLiteral 的 intersection、被约束到 JSLiteral 的 instantiable 类型——都算字面量，
    /// 从而不对可能存在的属性访问/赋值报错（与索引签名传播的行为一致，但不影响可赋值性与推断）。
    /// </summary>
    private bool IsJSLiteralType(Type t)
    {
        if (_noImplicitAny)
        {
            // Flag is meaningless under `noImplicitAny` mode
            return false;
        }
        if ((t._objectFlags & ObjectFlags.JSLiteral) != 0)
        {
            return true;
        }
        if ((t._flags & TypeFlags.Union) != 0)
        {
            return Every(t.AsUnionType()!._types, IsJSLiteralType);
        }
        if ((t._flags & TypeFlags.Intersection) != 0)
        {
            return Some(t.AsIntersectionType()!._types, IsJSLiteralType);
        }
        if ((t._flags & TypeFlags.Instantiable) != 0)
        {
            var constraint = GetResolvedBaseConstraint(t, null);
            return constraint != t && IsJSLiteralType(constraint);
        }
        return false;
    }

    /// <summary>
    /// Go: <c>func CreateModuleNotFoundChain(program Program, file *ast.SourceFile, moduleReference string, mode core.ResolutionMode, packageName string) DiagnosticDetails</c>（utilities.go:1828-1862）。
    /// 计算"找不到模块"错误链条目的诊断消息与实参；checker（初次诊断）与增量 builder（重填缓存诊断）共用。
    /// 对应 TypeScript 编译器 utilities.ts 的 createModuleNotFoundChain。
    /// </summary>
    public static DiagnosticDetails CreateModuleNotFoundChain(Program program, SourceFile file, string moduleReference, ModuleKind mode, string packageName)
    {
        var resolvedModule = program.GetResolvedModule(file, moduleReference, mode);

        if (resolvedModule is not null && resolvedModule.AlternateResult != "")
        {
            if (resolvedModule.AlternateResult.Contains("/node_modules/@types/"))
            {
                packageName = "@types/" + MangleScopedPackageName(packageName);
            }
            return new DiagnosticDetails
            {
                Message = There_are_types_at_0_but_this_result_could_not_be_resolved_when_respecting_package_json_exports_The_1_library_may_need_to_update_its_package_json_or_typings,
                Args = [resolvedModule.AlternateResult, packageName],
            };
        }

        var packagesMap = program.GetPackagesMap();
        if (packagesMap.ContainsKey(GetTypesPackageName(packageName)))
        {
            return new DiagnosticDetails
            {
                Message = If_the_0_package_actually_exposes_this_module_consider_sending_a_pull_request_to_amend_https_Colon_Slash_Slashgithub_com_SlashDefinitelyTyped_SlashDefinitelyTyped_Slashtree_Slashmaster_Slashtypes_Slash_1,
                Args = [packageName, MangleScopedPackageName(packageName)],
            };
        }
        if (packagesMap.GetValueOrDefault(packageName))
        {
            return new DiagnosticDetails
            {
                Message = If_the_0_package_actually_exposes_this_module_try_adding_a_new_declaration_d_ts_file_containing_declare_module_1,
                Args = [packageName, moduleReference],
            };
        }
        return new DiagnosticDetails
        {
            Message = Try_npm_i_save_dev_types_Slash_1_if_it_exists_or_add_a_new_declaration_d_ts_file_containing_declare_module_0,
            Args = [moduleReference, MangleScopedPackageName(packageName)],
        };
    }

    /// <summary>
    /// Go: <c>func CreateModeMismatchDetails(program Program, file *ast.SourceFile) DiagnosticDetails</c>（utilities.go:1864-1893）。
    /// 计算"模块格式不匹配"错误链条目的诊断消息与实参；checker 与增量 builder 共用。
    /// 对应 TypeScript 编译器 utilities.ts 的 createModeMismatchDetails。
    /// </summary>
    public static DiagnosticDetails CreateModeMismatchDetails(Program program, SourceFile file)
    {
        var ext = TryGetExtensionFromPath(file.FileName());
        var targetExt = IfElse(ext == ExtensionTs, ExtensionMts, IfElse(ext == ExtensionJs, ExtensionMjs, ""));
        var meta = program.GetSourceFileMetaData(file.Path());
        var packageJsonType = meta.PackageJsonType;
        var packageJsonDirectory = meta.PackageJsonDirectory;

        if (packageJsonDirectory != "" && packageJsonType == "")
        {
            if (targetExt != "")
            {
                return new DiagnosticDetails
                {
                    Message = To_convert_this_file_to_an_ECMAScript_module_change_its_file_extension_to_0_or_add_the_field_type_Colon_module_to_1,
                    Args = [targetExt, CombinePaths(packageJsonDirectory, "package.json")],
                };
            }
            return new DiagnosticDetails
            {
                Message = To_convert_this_file_to_an_ECMAScript_module_add_the_field_type_Colon_module_to_0,
                Args = [CombinePaths(packageJsonDirectory, "package.json")],
            };
        }
        if (targetExt != "")
        {
            return new DiagnosticDetails
            {
                Message = To_convert_this_file_to_an_ECMAScript_module_change_its_file_extension_to_0_or_create_a_local_package_json_file_with_type_Colon_module,
                Args = [targetExt],
            };
        }
        return new DiagnosticDetails
        {
            Message = To_convert_this_file_to_an_ECMAScript_module_create_a_local_package_json_file_with_type_Colon_module,
            Args = null,
        };
    }

    /// <summary>Go: <c>func walkUpOuterExpressions(node *ast.Node) *ast.Node</c>（utilities.go:1895-1901）。</summary>
    internal static Node? WalkUpOuterExpressions(Node node)
    {
        var parent = node.Parent;
        while (parent is not null && IsOuterExpression(parent, OuterExpressionKinds.All))
        {
            parent = parent.Parent;
        }
        return parent;
    }

    /// <summary>Go: <c>func GetSetAccessorValueParameter(accessor *ast.Node) *ast.Node</c>（utilities.go:1903-1910）。</summary>
    public static Node? GetSetAccessorValueParameter(Node accessor)
    {
        var parameters = accessor.Parameters();
        if ((parameters?.Length ?? 0) > 0)
        {
            var hasThis = parameters!.Length == 2 && IsThisParameter(parameters[0]);
            return parameters[IfElse(hasThis, 1, 0)];
        }
        return null;
    }
}
