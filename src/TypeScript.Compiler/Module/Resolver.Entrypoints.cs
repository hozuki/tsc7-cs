// ported from: internal/module/resolver.go:2141-2367（package.json 导出地图 → 候选 entrypoints）
//
// 与 Go 的偏离（同 Resolver.cs 文件头）：
// - Go 的 `*collections.Set[string]`（可为 nil）→ C# `Set<string>?`（`Clone()` 写 `?.Clone()`，Go 的 Clone 对
//   nil 接收者也是安全的，Go 紧接着就判了 nil）。
// - Go 的 `vfsmatch.ReadDirectory` → `TypeScript.Vfs.VfsMatch.VfsMatch.ReadDirectory`（形状见 Main 的契约广播）。
// - Go 的递归闭包 `loadEntrypointsFromTargetExports` → C# 本地函数（同样捕获 `entrypoints`）。
using TypeScript.Collections;
using TypeScript.Core;
using TypeScript.PackageJson;
using TypeScript.TSPath;
using TypeScript.Vfs;
using static TypeScript.Core.Core;
using static TypeScript.TSPath.TSPath;

namespace TypeScript.Module;

public enum Ending
{
    /// <summary>EndingFixed indicates that the module specifier cannot be changed without changing its resolution.</summary>
    Fixed = 0,

    /// <summary>
    /// EndingExtensionChangeable indicates that the module specifier's extension portion was inferred from a
    /// file on disk, so an interchangeable one could be used instead (e.g. replacing .d.ts with .js).
    /// </summary>
    ExtensionChangeable = 1,

    /// <summary>
    /// EndingChangeable indicates that the module specifier's file name and extension portion were inferred
    /// from a file on disk without being matched as part of an 'exports' pattern, so can be changed according
    /// to the importer's module resolution rules (e.g. an /index.d.ts may be dropped entirely in CommonJS settings).
    /// </summary>
    Changeable = 2,
}

/// <summary>Go 的 <c>ResolvedEntrypoint</c>。</summary>
public sealed class ResolvedEntrypoint
{
    /// <summary>OriginalFileName is the symlink path if the entrypoint was discovered at a symlink. Empty otherwise.</summary>
    public string OriginalFileName = "";

    /// <summary>ResolvedFileName is the real path to the entrypoint file.</summary>
    public string ResolvedFileName = "";

    public string ModuleSpecifier = "";

    /// <summary>Ending indicates whether the file name and extension portion of ModuleSpecifier is fixed or can be changed.</summary>
    public Ending Ending;

    /// <summary>IncludeConditions are the conditions that a resolver must have to reach this entrypoint.</summary>
    public Set<string>? IncludeConditions;

    /// <summary>ExcludeConditions are the conditions that a resolver must not have to reach this entrypoint.</summary>
    public Set<string>? ExcludeConditions;

    public string SymlinkOrRealpath()
    {
        if (OriginalFileName != "")
        {
            return OriginalFileName;
        }

        return ResolvedFileName;
    }
}

public sealed partial class DefaultResolver
{
    public ResolvedEntrypoint[] GetEntrypointsFromPackageJsonInfo(InfoCacheEntry packageJson, string packageName, bool enableDirectorySearch)
    {
        var extensions = Extensions.TypeScript | Extensions.Declaration;
        var features = NodeResolutionFeatures.All;
        var state = new ResolutionState
        {
            _resolver = this,
            _extensions = extensions,
            _features = features,
            _compilerOptions = _compilerOptions,
        };
        var packageJsonContents = packageJson.GetContents();
        if (packageJsonContents is not null && packageJsonContents.PathFields.Exports.IsPresent())
        {
            return state.LoadEntrypointsFromExportMap(packageJson, packageName, packageJsonContents.PathFields.Exports);
        }

        var result = new List<ResolvedEntrypoint>();
        var mainResolution = state.LoadNodeModuleFromDirectoryWorker(
            extensions,
            packageJson.PackageDirectory,
            packageJson);

        if (mainResolution is not null && mainResolution.IsResolved())
        {
            result.Add(CreateResolvedEntrypointHandlingSymlink(
                mainResolution.Path,
                packageName,
                null,
                null,
                Ending.Fixed));
        }

        if (enableDirectorySearch)
        {
            var otherFiles = TypeScript.Vfs.VfsMatch.VfsMatch.ReadDirectory(
                _host.FS(),
                _host.GetCurrentDirectory(),
                packageJson.PackageDirectory,
                extensions.Array(),
                ["node_modules"],
                ["**/*"],
                TypeScript.Vfs.VfsMatch.VfsMatch.UnlimitedDepth);

            var comparePathsOptions = new ComparePathsOptions(_host.FS().UseCaseSensitiveFileNames());
            foreach (var file in otherFiles)
            {
                if (mainResolution is not null && mainResolution.IsResolved() && ComparePaths(file, mainResolution.Path, comparePathsOptions) == 0)
                {
                    continue;
                }

                result.Add(CreateResolvedEntrypointHandlingSymlink(
                    file,
                    ResolvePath(packageName, GetRelativePathFromDirectory(packageJson.PackageDirectory, file, comparePathsOptions)),
                    null,
                    null,
                    Ending.Changeable));
            }
        }

        if (result.Count > 0)
        {
            return [.. result];
        }

        return [];
    }

    internal ResolvedEntrypoint CreateResolvedEntrypointHandlingSymlink(
        string fileName,
        string moduleSpecifier,
        Set<string>? includeConditions,
        Set<string>? excludeConditions,
        Ending ending)
    {
        var originalFileName = "";
        var resolvedFileName = fileName;
        var realPath = _host.FS().Realpath(fileName);
        if (realPath != fileName)
        {
            originalFileName = fileName;
            resolvedFileName = realPath;
        }

        return new ResolvedEntrypoint
        {
            OriginalFileName = originalFileName,
            ResolvedFileName = resolvedFileName,
            ModuleSpecifier = moduleSpecifier,
            IncludeConditions = includeConditions,
            ExcludeConditions = excludeConditions,
            Ending = ending,
        };
    }
}

public static partial class Module
{
    internal static ResolvedEntrypoint[] LoadEntrypointsFromExportMap(
        this ResolutionState r,
        InfoCacheEntry packageJson,
        string packageName,
        ExportsOrImports exports)
    {
        var entrypoints = new List<ResolvedEntrypoint>();

        void LoadEntrypointsFromTargetExports(string subpath, Set<string>? includeConditions, Set<string>? excludeConditions, ExportsOrImports exports)
        {
            if (exports.Type == JSONValueType.String && exports.AsString().StartsWith("./", StringComparison.Ordinal))
            {
                var exportsString = exports.AsString();
                if (exportsString.Contains('*'))
                {
                    if (exportsString.IndexOf('*') != exportsString.LastIndexOf('*'))
                    {
                        return;
                    }

                    var patternPath = ResolvePath(packageJson.PackageDirectory, exportsString);
                    var starIndex = patternPath.IndexOf('*');
                    var leadingSlice = patternPath[..starIndex];
                    var trailingSlice = patternPath[(starIndex + 1)..];
                    var caseSensitive = r._resolver.Host.FS().UseCaseSensitiveFileNames();
                    var files = TypeScript.Vfs.VfsMatch.VfsMatch.ReadDirectory(
                        r._resolver.Host.FS(),
                        r._resolver.Host.GetCurrentDirectory(),
                        packageJson.PackageDirectory,
                        r._extensions.Array(),
                        null,
                        [
                            ChangeFullExtension(ReplaceFirst(exportsString, "*", "**/*"), ".*"),
                        ],
                        TypeScript.Vfs.VfsMatch.VfsMatch.UnlimitedDepth);
                    foreach (var file in files)
                    {
                        if (!r.GetMatchedStarForPatternEntrypoint(file, leadingSlice, trailingSlice, caseSensitive, out var matchedStar))
                        {
                            continue;
                        }

                        var moduleSpecifier = ResolvePath(packageName, ReplaceFirst(subpath, "*", matchedStar));
                        entrypoints.Add(r._resolver.CreateResolvedEntrypointHandlingSymlink(
                            file,
                            moduleSpecifier,
                            includeConditions,
                            excludeConditions,
                            exportsString.EndsWith('*') ? Ending.ExtensionChangeable : Ending.Fixed));
                    }
                }
                else
                {
                    var partsAfterFirst = GetPathComponents(exportsString, "")[2..];
                    if (System.Array.IndexOf(partsAfterFirst, "..") >= 0 || System.Array.IndexOf(partsAfterFirst, ".") >= 0 || System.Array.IndexOf(partsAfterFirst, "node_modules") >= 0)
                    {
                        return;
                    }

                    var resolvedTarget = ResolvePath(packageJson.PackageDirectory, exportsString);
                    var result = r.LoadFileNameFromPackageJSONField(r._extensions, resolvedTarget, exportsString);
                    if (result is not null && result.IsResolved())
                    {
                        entrypoints.Add(r._resolver.CreateResolvedEntrypointHandlingSymlink(
                            result.Path,
                            ResolvePath(packageName, subpath),
                            includeConditions,
                            excludeConditions,
                            exportsString.EndsWith('*') ? Ending.ExtensionChangeable : Ending.Fixed));
                    }
                }
            }
            else if (exports.Type == JSONValueType.Array)
            {
                foreach (var element in exports.AsArray())
                {
                    LoadEntrypointsFromTargetExports(subpath, includeConditions, excludeConditions, element);
                }
            }
            else if (exports.Type == JSONValueType.Object)
            {
                var prevConditions = new List<string>();
                // Go: exports.AsObject()（Type 已判定为 Object，nil 即 panic），故 `!`。
                foreach (var (condition, export) in exports.AsObject()!.Entries())
                {
                    if (export is null)
                    {
                        continue;
                    }

                    if (excludeConditions is not null && excludeConditions.Has(condition))
                    {
                        continue;
                    }

                    var conditionAlwaysMatches = condition == "default" || condition == "types" || IsApplicableVersionedTypesKey(condition);
                    var newIncludeConditions = includeConditions;
                    if (!conditionAlwaysMatches)
                    {
                        newIncludeConditions = includeConditions?.Clone();
                        excludeConditions = excludeConditions?.Clone();
                        newIncludeConditions ??= new Set<string>();
                        excludeConditions ??= new Set<string>();
                        newIncludeConditions.Add(condition);
                        foreach (var prevCondition in prevConditions)
                        {
                            excludeConditions.Add(prevCondition);
                        }
                    }

                    prevConditions.Add(condition);
                    LoadEntrypointsFromTargetExports(subpath, newIncludeConditions, excludeConditions, export);
                    if (conditionAlwaysMatches)
                    {
                        break;
                    }
                }
            }
        }

        switch (exports.Type)
        {
            case JSONValueType.Array:
                foreach (var element in exports.AsArray())
                {
                    LoadEntrypointsFromTargetExports(".", null, null, element);
                }

                break;
            case JSONValueType.Object:
                if (exports.IsSubpaths())
                {
                    // Go: exports.AsObject()（IsSubpaths() 蕴含 Type == Object，nil 即 panic），故 `!`。
                    foreach (var (subpath, export) in exports.AsObject()!.Entries())
                    {
                        if (export is not null)
                        {
                            LoadEntrypointsFromTargetExports(subpath, null, null, export);
                        }
                    }
                }
                else
                {
                    LoadEntrypointsFromTargetExports(".", null, null, exports);
                }

                break;
            default:
                LoadEntrypointsFromTargetExports(".", null, null, exports);
                break;
        }

        return [.. entrypoints];
    }

    internal static bool GetMatchedStarForPatternEntrypoint(
        this ResolutionState r,
        string file,
        string leadingSlice,
        string trailingSlice,
        bool caseSensitive,
        out string matchedStar)
    {
        matchedStar = "";
        if (TypeScript.StringUtil.StringUtil.HasPrefixAndSuffixWithoutOverlap(file, leadingSlice, trailingSlice, caseSensitive))
        {
            matchedStar = file[leadingSlice.Length..(file.Length - trailingSlice.Length)];
            return true;
        }

        var jsExtension = TryGetJSExtensionForFile(file, r._compilerOptions);
        if (jsExtension.Length > 0)
        {
            var swapped = ChangeFullExtension(file, jsExtension);
            if (TypeScript.StringUtil.StringUtil.HasPrefixAndSuffixWithoutOverlap(swapped, leadingSlice, trailingSlice, caseSensitive))
            {
                matchedStar = swapped[leadingSlice.Length..(swapped.Length - trailingSlice.Length)];
                return true;
            }
        }

        return false;
    }
}
