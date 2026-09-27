// ported from: internal/module/types.go
//
// 与 Go 的偏离（docs/architecture.md §2/§3；每条都不改变语义）：
// - Go 的 `type ResolutionMode = core.ModuleKind` 是类型别名 → C# 没有别名，`ResolutionMode` 出现处一律写
//   `ModuleKind`（`ResolutionModeNone/CommonJS/ESM` 是 Core 的 const，随 `using static` 直接可用）。
// - Go 的 `type extensions int32`（未导出位标志）→ `internal [Flags] enum Extensions`；它的 `String()`/`Array()`
//   在 C# 里是 `Module` 静态类的扩展方法（§3.3：C# 的 enum 不能声明实例方法）。
// - Go 的 `(*ResolvedModule).IsResolved()` / `(*ResolvedTypeReferenceDirective).IsResolved()` 带 nil 接收者检查
//   → C# 实例方法只判 `ResolvedFileName != ""`，调用点写 `x is not null && x.IsResolved()`（§3.2）。
// - Go 的 `Resolver` 接口返回 `(T, []DiagAndArgs, error)`：C# 用元组返回非 error 的第二值，error 走异常（§3.2）；
//   Go 的 nil 切片 → 非空数组（调用方只遍历/判长度）。
// - Go 的 `DiagAndArgs` 是值类型结构体 → C# struct（含构造函数）。
using TypeScript.Ast;
using TypeScript.Core;
using TypeScript.Diagnostics;
using TypeScript.PackageJson;
using TypeScript.TSPath;
using TypeScript.Vfs;
using static TypeScript.TSPath.TSPath;

namespace TypeScript.Module;

/// <summary>Go 的 <c>ResolutionHost</c>：解析器需要的宿主能力（文件系统 + 当前目录）。</summary>
public interface ResolutionHost
{
    FS FS();

    string GetCurrentDirectory();
}

/// <summary>Go 的 <c>Resolver</c>。C# 的 <c>error</c> 返回值落成异常，traces 落成元组第二项。</summary>
public interface Resolver
{
    (ResolvedModule? Module, DiagAndArgs[] Traces) ResolveModuleName(
        string moduleName,
        string containingFile,
        ModuleKind resolutionMode,
        ResolvedProjectReference? redirectedReference);

    (ResolvedModule? Module, DiagAndArgs[] Traces) ResolveModuleNameFromDirectory(
        string moduleName,
        string containingDirectory,
        ModuleKind resolutionMode);

    (ResolvedTypeReferenceDirective? Directive, DiagAndArgs[] Traces) ResolveTypeReferenceDirective(
        string typeReferenceDirectiveName,
        string containingFile,
        ModuleKind resolutionMode,
        ResolvedProjectReference? redirectedReference);

    InfoCacheEntry? GetPackageScopeForPath(string directory);

    void PackageJsonCacheEntries(Func<TSPath.Path, InfoCacheEntry?, bool> f);

    ResolvedModule? ResolvePackageDirectory(
        string moduleName,
        string containingFile,
        ModuleKind resolutionMode,
        ResolvedProjectReference? redirectedReference);
}

/// <summary>Go 的 <c>ModeAwareCacheKey</c>。</summary>
public struct ModeAwareCacheKey
{
    public string Name;

    public ModuleKind Mode;

    public ModeAwareCacheKey(string name, ModuleKind mode)
    {
        Name = name;
        Mode = mode;
    }
}

/// <summary>Go 的 <c>ResolvedProjectReference</c>（`*` 指针 → C# 可空引用）。</summary>
public interface ResolvedProjectReference
{
    string ConfigName();

    CompilerOptions? CompilerOptions();
}

/// <summary>Go 的 <c>NodeResolutionFeatures</c>（位标志，数值与 Go 一致）。</summary>
[Flags]
public enum NodeResolutionFeatures : int
{
    Imports = 1 << 0,
    SelfName = 1 << 1,
    Exports = 1 << 2,
    ExportsPatternTrailers = 1 << 3,
    // allowing `#/` root imports in package.json imports field
    // not supported until mass adoption - https://github.com/nodejs/node/pull/60864
    ImportsPatternRoot = 1 << 4,

    None = 0,
    All = Imports | SelfName | Exports | ExportsPatternTrailers | ImportsPatternRoot,
    Node16Default = Imports | SelfName | Exports | ExportsPatternTrailers,
    NodeNextDefault = All,
    BundlerDefault = Imports | SelfName | Exports | ExportsPatternTrailers | ImportsPatternRoot,
}

/// <summary>Go 的 <c>PackageId</c>（值类型；`String()`/`PackageName()` 是 Go 的指针接收者方法）。</summary>
/// <remarks>
/// Go 的 `string` 零值是 `""`，C# 结构体字段的零值是 `null`；`PackageId{}` 在 Go 里 `Name == ""`，
/// 而 `default(PackageId).Name` 在 C# 里是 null（`null != ""` 为 true，会让调用点的
/// "packageId 非空" 判定误判）。所以四个导出字段落成"可空后备字段 + `?? \"\"` 的 getter"的属性，
/// 与 `TSPath.Path.Text` 的处理一致（docs/architecture.md §3.2 的零值语义）。
/// </remarks>
public struct PackageId
{
    private string? _name;
    private string? _subModuleName;
    private string? _version;
    private string? _peerDependencies;

    public string Name
    {
        get => _name ?? "";
        set => _name = value;
    }

    public string SubModuleName
    {
        get => _subModuleName ?? "";
        set => _subModuleName = value;
    }

    public string Version
    {
        get => _version ?? "";
        set => _version = value;
    }

    public string PeerDependencies
    {
        get => _peerDependencies ?? "";
        set => _peerDependencies = value;
    }

    public override string ToString()
    {
        return PackageName() + "@" + Version + PeerDependencies;
    }

    public string PackageName()
    {
        if (SubModuleName != "")
        {
            return Name + "/" + SubModuleName;
        }

        return Name;
    }
}

/// <summary>Go 的 <c>ResolvedModule</c>（指针类型 → class，见文件头）。</summary>
public sealed class ResolvedModule
{
    public Diagnostic[] ResolutionDiagnostics = [];
    public string ResolvedFileName = "";
    public string OriginalPath = "";
    public string Extension = "";
    public bool ResolvedUsingTsExtension;
    public bool ResolvedUsingExtraExtensions;
    public PackageId PackageId;
    public bool IsExternalLibraryImport;
    public string AlternateResult = "";

    public bool IsResolved()
    {
        return ResolvedFileName != "";
    }
}

/// <summary>Go 的 <c>ResolvedTypeReferenceDirective</c>。</summary>
public sealed class ResolvedTypeReferenceDirective
{
    public Diagnostic[] ResolutionDiagnostics = [];
    public bool Primary;
    public string ResolvedFileName = "";
    public string OriginalPath = "";
    public PackageId PackageId;
    public bool IsExternalLibraryImport;

    public bool IsResolved()
    {
        return ResolvedFileName != "";
    }
}

/// <summary>Go 的 <c>extensions</c>（未导出的位标志）。</summary>
[Flags]
internal enum Extensions : int
{
    TypeScript = 1 << 0,
    JavaScript = 1 << 1,
    Declaration = 1 << 2,
    Json = 1 << 3,

    ImplementationFiles = TypeScript | JavaScript,
}

public static partial class Module
{
    /// <summary>Go 的 <c>func (e extensions) String() string</c>。</summary>
    internal static string String(this Extensions e)
    {
        var result = new List<string>(4);
        if ((e & Extensions.TypeScript) != 0)
        {
            result.Add("TypeScript");
        }

        if ((e & Extensions.JavaScript) != 0)
        {
            result.Add("JavaScript");
        }

        if ((e & Extensions.Declaration) != 0)
        {
            result.Add("Declaration");
        }

        if ((e & Extensions.Json) != 0)
        {
            result.Add("JSON");
        }

        return string.Join(", ", result);
    }

    /// <summary>Go 的 <c>func (e extensions) Array() []string</c>。</summary>
    internal static string[] Array(this Extensions e)
    {
        var result = new List<string>();
        if ((e & Extensions.TypeScript) != 0)
        {
            result.AddRange(SupportedTSImplementationExtensions);
        }

        if ((e & Extensions.JavaScript) != 0)
        {
            result.AddRange(SupportedJSExtensionsFlat);
        }

        if ((e & Extensions.Declaration) != 0)
        {
            result.AddRange(SupportedDeclarationExtensions);
        }

        if ((e & Extensions.Json) != 0)
        {
            result.Add(ExtensionJson);
        }

        return [.. result];
    }
}
