// ported from: internal/module/staticresolver.go
//
// 与 Go 的偏离（docs/architecture.md §2/§3）：
// - Go 的 `NewStaticResolutions(...) (*StaticResolutions, error)` → C# 正常路径返回对象、错误走异常（§3.2）；
//   两个错误（module name 为空 / 重复键）是调用方输入问题 → `ArgumentException`。
// - Go 的 `ResolutionMode *core.ResolutionMode`（可空指针）→ C# `ModuleKind?`。
// - Go 的 `Resolver` 接口的三返回值 → C# 元组（见 Types.cs 文件头）。
using TypeScript.Core;
using TypeScript.PackageJson;
using TypeScript.TSPath;
using static TypeScript.TSPath.TSPath;

namespace TypeScript.Module;

/// <summary>Go 的 <c>StaticResolutionEntry</c>。</summary>
/// <remarks>字符串字段用"可空后备字段 + <c>?? ""</c> getter"（Go 的零值是 <c>""</c>，C# 结构体字段是 null，
/// 调用点的 `ContainingDirectory != ""` / `ResolutionMode != nil` 判定依赖零值语义；同 <see cref="PackageId"/>）。</remarks>
public struct StaticResolutionEntry
{
    private string? _moduleName;
    private string? _containingDirectory;

    public string ModuleName
    {
        get => _moduleName ?? "";
        set => _moduleName = value;
    }

    public string ContainingDirectory
    {
        get => _containingDirectory ?? "";
        set => _containingDirectory = value;
    }

    public ModuleKind? ResolutionMode;
    public ResolvedModule? Result;
}

/// <summary>Go 的 <c>staticResolutionKey</c>。</summary>
internal struct StaticResolutionKey
{
    public string ModuleName;
    public TSPath.Path Directory;
    public ModuleKind Mode;
    public bool HasDirectory;
    public bool HasMode;
}

/// <summary>Go 的 <c>StaticResolutions</c>。</summary>
public sealed class StaticResolutions
{
    private readonly bool _fallbackToResolver;
    private readonly Dictionary<StaticResolutionKey, ResolvedModule?> _entries;
    private readonly string _currentDirectory;
    private readonly bool _useCaseSensitiveFileNames;

    private StaticResolutions(
        int sizeHint,
        bool fallbackToResolver,
        string currentDirectory,
        bool useCaseSensitiveFileNames)
    {
        _fallbackToResolver = fallbackToResolver;
        _entries = new Dictionary<StaticResolutionKey, ResolvedModule?>(sizeHint);
        _currentDirectory = currentDirectory;
        _useCaseSensitiveFileNames = useCaseSensitiveFileNames;
    }

    internal ResolvedModule? Lookup(string moduleName, string containingDirectory, ModuleKind resolutionMode, out bool found)
    {
        var directory = ToPath(containingDirectory, _currentDirectory, _useCaseSensitiveFileNames);
        Span<StaticResolutionKey> keys =
        [
            new StaticResolutionKey { ModuleName = moduleName, Directory = directory, Mode = resolutionMode, HasDirectory = true, HasMode = true },
            new StaticResolutionKey { ModuleName = moduleName, Directory = directory, HasDirectory = true },
            new StaticResolutionKey { ModuleName = moduleName, Mode = resolutionMode, HasMode = true },
            new StaticResolutionKey { ModuleName = moduleName },
        ];
        foreach (var key in keys)
        {
            if (_entries.TryGetValue(key, out var result))
            {
                found = true;
                return result;
            }
        }

        found = false;
        return null;
    }

    internal bool FallbackToResolver => _fallbackToResolver;

    public static StaticResolutions NewStaticResolutions(
        StaticResolutionEntry[] entries,
        bool fallbackToResolver,
        string currentDirectory,
        bool useCaseSensitiveFileNames)
    {
        var resolutions = new StaticResolutions(
            entries.Length,
            fallbackToResolver,
            currentDirectory,
            useCaseSensitiveFileNames);
        foreach (var entry in entries)
        {
            if (entry.ModuleName == "")
            {
                throw new ArgumentException("module name is empty", nameof(entries));
            }

            var key = new StaticResolutionKey { ModuleName = entry.ModuleName };
            if (entry.ContainingDirectory != "")
            {
                key.Directory = ToPath(entry.ContainingDirectory, currentDirectory, useCaseSensitiveFileNames);
                key.HasDirectory = true;
            }

            if (entry.ResolutionMode is { } mode)
            {
                key.Mode = mode;
                key.HasMode = true;
            }

            if (!resolutions._entries.TryAdd(key, entry.Result))
            {
                throw new ArgumentException($"duplicate static module resolution for \"{entry.ModuleName}\"", nameof(entries));
            }
        }

        return resolutions;
    }
}

/// <summary>Go 的 <c>StaticResolver</c>。</summary>
public sealed class StaticResolver : Resolver
{
    private readonly Resolver _fallback;
    private readonly StaticResolutions _resolutions;

    private StaticResolver(Resolver fallback, StaticResolutions resolutions)
    {
        _fallback = fallback;
        _resolutions = resolutions;
    }

    public static StaticResolver NewStaticResolver(Resolver fallback, StaticResolutions resolutions)
    {
        return new StaticResolver(fallback, resolutions);
    }

    public (ResolvedModule? Module, DiagAndArgs[] Traces) ResolveModuleName(
        string moduleName,
        string containingFile,
        ModuleKind resolutionMode,
        ResolvedProjectReference? redirectedReference)
    {
        return ResolveModuleName(moduleName, containingFile, GetDirectoryPath(containingFile), resolutionMode, redirectedReference);
    }

    public (ResolvedModule? Module, DiagAndArgs[] Traces) ResolveModuleNameFromDirectory(
        string moduleName,
        string containingDirectory,
        ModuleKind resolutionMode)
    {
        var result = _resolutions.Lookup(moduleName, containingDirectory, resolutionMode, out var found);
        if (found)
        {
            return (result, []);
        }

        if (!_resolutions.FallbackToResolver)
        {
            return (null, []);
        }

        return _fallback.ResolveModuleNameFromDirectory(moduleName, containingDirectory, resolutionMode);
    }

    private (ResolvedModule? Module, DiagAndArgs[] Traces) ResolveModuleName(
        string moduleName,
        string containingFile,
        string containingDirectory,
        ModuleKind resolutionMode,
        ResolvedProjectReference? redirectedReference)
    {
        var result = _resolutions.Lookup(moduleName, containingDirectory, resolutionMode, out var found);
        if (found)
        {
            return (result, []);
        }

        if (!_resolutions.FallbackToResolver)
        {
            return (null, []);
        }

        return _fallback.ResolveModuleName(moduleName, containingFile, resolutionMode, redirectedReference);
    }

    public (ResolvedTypeReferenceDirective? Directive, DiagAndArgs[] Traces) ResolveTypeReferenceDirective(
        string typeReferenceDirectiveName,
        string containingFile,
        ModuleKind resolutionMode,
        ResolvedProjectReference? redirectedReference)
    {
        return _fallback.ResolveTypeReferenceDirective(typeReferenceDirectiveName, containingFile, resolutionMode, redirectedReference);
    }

    public InfoCacheEntry? GetPackageScopeForPath(string directory)
    {
        return _fallback.GetPackageScopeForPath(directory);
    }

    public void PackageJsonCacheEntries(Func<TSPath.Path, InfoCacheEntry?, bool> f)
    {
        _fallback.PackageJsonCacheEntries(f);
    }

    public ResolvedModule? ResolvePackageDirectory(
        string moduleName,
        string containingFile,
        ModuleKind resolutionMode,
        ResolvedProjectReference? redirectedReference)
    {
        return _fallback.ResolvePackageDirectory(moduleName, containingFile, resolutionMode, redirectedReference);
    }
}
