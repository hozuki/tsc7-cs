// ported from: internal/modulespecifiers/types.go（125 行，整文件）
//
// 与 Go 的偏离（都是 C# 语言特性，逐条依据 docs/architecture.md §2/§3）：
// 1) Go 的 `type ImportModuleSpecifierPreference string` / `ImportModuleSpecifierEndingPreference string`
//    是**定义在 string 上的具名类型**（不是别名），带一组常量、可由任意字符串构造（LSP 传进来的值原样保留）。
//    C# 没有"具名字符串类型"，用 `readonly record struct` 包一层 string（与 TSPath.Path 同一个做法）：
//    显式转换 `ImportModuleSpecifierPreference(s)` / `(string)pref` 对应 Go 的转换；常量按 Go 的名字
//    放进静态类 `ModuleSpecifiers`，类型是 `const string`（C# 的 switch case 标签必须是编译期常量，
//    而 Go 的常量正好就是那 5/5 个字面量），于是 `switch (pref.ImportModuleSpecifierEnding.Value)` 的
//    分支名与 Go 逐字相同。为了比较处也能逐字移植，结构体额外提供了与 string 的 `==`/`!=`。
// 2) Go 的 `type ResultKind uint8` 等 4 个枚举 → C# `enum : byte`，成员名去 Go 的类型前缀（§3.3）、数值照抄。
// 3) Go 的结构体（`ModuleSpecifiersResult`/`ModulePath`/`Info`/`UserPreferences`/`ModuleSpecifierOptions`）
//    → C# 见各自声明处的注释。主要是两点：可变的小值对象用 struct（`UserPreferences`/`ModuleSpecifierOptions`），
//    而**带 slice 字段、又要避免 null 的**用 class（`ModuleSpecifiersResult`/`ModulePath`/`Info`）——
//    struct 的字段不能有初始化器，Go 的零值 `""`/`nil slice` 在 C# 里会变成 null（§3.2 的 nil slice 约定）。
// 4) Go 的 `tspath.Path` → `TSPath.Path`（Path 与 System.IO.Path 撞名，见 TSPath/Path.cs 的注释）。
// 5) Go 的 `*ast.StringLiteralLike` 是 `*ast.Node` 的别名（ast.go:1263）→ C# 一律写 `Node`。
// 6) 接口里的两个注释掉的成员（`GetModuleResolutionCache`/`GetFileIncludeReasons`）在 Go 里也是注释，
//    不移植（Go 的 `// !!! TODO` 注释同样不抄，见交付报告的"跳过"清单）。
// 7) `ModuleSpecifierGenerationHost : OutputPathsHost`：Go 里 `ModuleSpecifierGenerationHost` 只是**结构上**
//    满足 `outputpaths.OutputPathsHost`（模块说明符生成时会把 host 直接传给
//    `outputpaths.GetOutputJSFileNameWorker`），C# 没有结构满足，用接口继承显式表达。
using TypeScript.Ast;
using TypeScript.Core;
using TypeScript.Module;
using TypeScript.OutputPaths;
using TypeScript.PackageJson;
using TypeScript.Symlinks;
using TypeScript.TsOptions;
using Path = TypeScript.TSPath.Path;

using TypeScript.TSPath;

namespace TypeScript.ModuleSpecifiers;

/// <summary>Go: <c>type SourceFileForSpecifierGeneration interface</c>（能被生成模块说明符的文件；`*ast.SourceFile` 满足它）。</summary>
/// <remarks>
/// Go 里 `SourceFileForSpecifierGeneration` **结构上**满足 `ast.HasFileName`（它有 FileName()/Path()），
/// 于是调用点可以直接把 `SourceFileForSpecifierGeneration` 传给要 `HasFileName` 的函数
/// （`GetDefaultResolutionModeForFile` 等）。C# 没有结构满足，必须写出继承关系——这就是这一行的作用。
/// `Path()`/`FileName()` 仍按 Go 的声明面重复声明（api-parity 逐成员核对），用 `new` 显式隐藏基接口成员。
/// </remarks>
public interface SourceFileForSpecifierGeneration : HasFileName
{
    new Path Path();

    new string FileName();

    Node[] Imports();

    bool IsJS();
}

/// <summary>Go: <c>type CheckerShape interface</c>（只用到 checker 的这两个方法，避免循环依赖）。</summary>
public interface CheckerShape
{
    Symbol? GetSymbolAtLocation(Node node);

    Symbol? GetAliasedSymbol(Symbol symbol);
}

/// <summary>Go: <c>type ResultKind uint8</c>。</summary>
public enum ResultKind : byte
{
    None = 0,
    NodeModules = 1,
    Paths = 2,
    Redirect = 3,
    Relative = 4,
    Ambient = 5,
}

/// <summary>Go: <c>type ModuleSpecifiersResult struct</c>。</summary>
/// <remarks>
/// Go 的零值 `ModuleSpecifiersResult{}` 的 `Specifiers` 是 nil slice；§3.2 约定语义等同时用 `[]`，
/// 所以这里用 class + 字段初始化器（struct 不允许字段初始化器），空结果就是 `new ModuleSpecifiersResult()`。
/// </remarks>
public sealed class ModuleSpecifiersResult
{
    public string[] Specifiers = [];

    public ResultKind Kind;

    /// <summary>used to construct an import attributes node, if one is needed</summary>
    public Symbol? AmbientModuleSymbol;
}

/// <summary>Go: <c>type ModulePath struct { FileName string; IsInNodeModules bool; IsRedirect bool }</c>。</summary>
/// <remarks>Go 里是结构体、按值放进切片/映射；调用方从不修改已经建好的项，C# 用 class（同 packagejson 的判定）。</remarks>
public sealed class ModulePath
{
    public string FileName = "";

    public bool IsInNodeModules;

    public bool IsRedirect;
}

/// <summary>Go: <c>type ModuleSpecifierGenerationHost interface</c>。</summary>
/// <remarks>见文件头第 7 条：`: OutputPathsHost` 是 Go 的结构满足在 C# 里的显式表达。</remarks>
public interface ModuleSpecifierGenerationHost : OutputPathsHost
{
    // Go: GetModuleResolutionCache() any // !!! TODO: adapt new resolution cache model —— 不移植
    // Go: GetFileIncludeReasons() any    // !!! TODO: adapt new resolution cache model —— 不移植
    KnownSymlinks? GetSymlinkCache();

    // Go 的接口里这 4 个成员是**自己声明**的；C# 侧它们已由 OutputPathsHost 提供（见文件头第 7 条），
    // 为了保住 Go 的声明面（api-parity 逐成员核对）而重复声明，用 `new` 显式隐藏（签名相同，实现类
    // 一个 public 方法即可同时满足两个接口成员）。
    new string CommonSourceDirectory();

    new string[] ContentMapperExtensions();

    string GetGlobalTypingsCacheLocation();

    new bool UseCaseSensitiveFileNames();

    new string GetCurrentDirectory();

    SourceOutputAndProjectReference? GetProjectReferenceFromSource(Path path);

    string[] GetRedirectTargets(Path path);

    string GetSourceOfProjectReferenceIfOutputIncluded(HasFileName file);

    bool FileExists(string path);

    string GetNearestAncestorDirectoryWithPackageJson(string dirname);

    InfoCacheEntry? GetPackageJsonInfo(string pkgJsonPath);

    ModuleKind GetDefaultResolutionModeForFile(HasFileName file); // Go: core.ResolutionMode

    ResolvedModule? GetResolvedModuleFromModuleSpecifier(HasFileName file, Node moduleSpecifier);

    ModuleKind GetModeForUsageLocation(HasFileName file, Node moduleSpecifier); // Go: core.ResolutionMode
}

/// <summary>Go: <c>type ImportModuleSpecifierPreference string</c>（常量见静态类 <see cref="ModuleSpecifiers"/>）。</summary>
public readonly record struct ImportModuleSpecifierPreference(string Value)
{
    /// <summary>Go 的零值是 ""（None 那一档），这里把 primary 构造参数显式声明成属性并归一化 null。</summary>
    public string Value { get; } = Value ?? "";

    public static explicit operator ImportModuleSpecifierPreference(string value) => new(value);

    public static explicit operator string(ImportModuleSpecifierPreference preference) => preference.Value;

    // 便于与静态类里那些 const string（Go 的常量）逐字比较，见文件头第 1 条。
    public static bool operator ==(ImportModuleSpecifierPreference preference, string value) => preference.Value == value;

    public static bool operator !=(ImportModuleSpecifierPreference preference, string value) => preference.Value != value;

    public override string ToString() => Value;
}

/// <summary>Go: <c>type ImportModuleSpecifierEndingPreference string</c>（常量见静态类 <see cref="ModuleSpecifiers"/>）。</summary>
public readonly record struct ImportModuleSpecifierEndingPreference(string Value)
{
    /// <summary>Go 的零值是 ""（None 那一档），这里把 primary 构造参数显式声明成属性并归一化 null。</summary>
    public string Value { get; } = Value ?? "";

    public static explicit operator ImportModuleSpecifierEndingPreference(string value) => new(value);

    public static explicit operator string(ImportModuleSpecifierEndingPreference preference) => preference.Value;

    public static bool operator ==(ImportModuleSpecifierEndingPreference preference, string value) => preference.Value == value;

    public static bool operator !=(ImportModuleSpecifierEndingPreference preference, string value) => preference.Value != value;

    public override string ToString() => Value;
}

/// <summary>Go: <c>type UserPreferences struct</c>。</summary>
/// <remarks>
/// Go 按值传递，C# 用 struct 照抄（字段与 Go 同名、可写）。`UserPreferences{}` = `new UserPreferences()`
/// （两个偏好类型的 Value 都是 ""，`AutoImportSpecifierExcludeRegexes` 是 null = Go 的 nil slice，
/// 调用方只用 len/for range，见 IsExcludedByRegex）。
/// </remarks>
public struct UserPreferences
{
    public ImportModuleSpecifierPreference ImportModuleSpecifierPreference;

    public ImportModuleSpecifierEndingPreference ImportModuleSpecifierEnding;

    public string[]? AutoImportSpecifierExcludeRegexes;
}

/// <summary>Go: <c>type ModuleSpecifierOptions struct { OverrideImportMode core.ResolutionMode }</c>。</summary>
public struct ModuleSpecifierOptions
{
    public ModuleKind OverrideImportMode; // Go: core.ResolutionMode
}

/// <summary>Go: <c>type RelativePreferenceKind uint8</c>。</summary>
public enum RelativePreferenceKind : byte
{
    Relative = 0,
    NonRelative = 1,
    Shortest = 2,
    ExternalNonRelative = 3,
}

/// <summary>Go: <c>type ModuleSpecifierEnding uint8</c>。</summary>
public enum ModuleSpecifierEnding : byte
{
    Minimal = 0,
    Index = 1,
    JsExtension = 2,
    TsExtension = 3,
}

/// <summary>Go: <c>type MatchingMode uint8</c>。</summary>
public enum MatchingMode : byte
{
    Exact = 0,
    Directory = 1,
    Pattern = 2,
}

/// <summary>Go 的 `modulespecifiers` 包（包级函数/常量放这里，见 docs/architecture.md §2）。</summary>
public static partial class ModuleSpecifiers
{
    public const string ImportModuleSpecifierPreferenceNone = "";

    public const string ImportModuleSpecifierPreferenceShortest = "shortest";

    public const string ImportModuleSpecifierPreferenceProjectRelative = "project-relative";

    public const string ImportModuleSpecifierPreferenceRelative = "relative";

    public const string ImportModuleSpecifierPreferenceNonRelative = "non-relative";

    public const string ImportModuleSpecifierEndingPreferenceNone = "";

    public const string ImportModuleSpecifierEndingPreferenceAuto = "auto";

    public const string ImportModuleSpecifierEndingPreferenceMinimal = "minimal";

    public const string ImportModuleSpecifierEndingPreferenceIndex = "index";

    public const string ImportModuleSpecifierEndingPreferenceJs = "js";
}
