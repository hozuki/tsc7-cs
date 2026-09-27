// ported from: internal/compiler/projectreferencefilemapper.go
//
// Go→C# 改写（逐条，都不改变语义）：
// 1) `module.GetCompilerOptionsWithRedirect` → `using CompilerModule = TypeScript.Module.Module;` 后的
//    `CompilerModule.GetCompilerOptionsWithRedirect(...)`（裸写 `Module.X` 会命中命名空间 `TypeScript.Module`）。
// 2) Go 的 nil 切片（`getResolvedProjectReferences` 在 `!ok` 时返回 nil）→ C# 空数组；元素本身可能是 nil，
//    所以元素类型是 `ParsedCommandLine?`（`configToProjectReference` 的键存在但值为 nil 时就是这样）。
// 3) `(T, bool)` 形态的 `getResolvedReferenceFor` → `TryGetResolvedReferenceFor(..., out T?)`（§3.2）。
// 4) `realpathDtsToSource.Store(path, nil)` 要存 Go 的 nil 值 → `SyncMap<Path, SourceOutputAndProjectReference?>`
//    （V 必须可空），`Load` 的 `ok` 与 Go 的 `ok` 完全一致。
// 5) `strings.Contains(fileName, "/node_modules/")` → `Contains(fileName, "/node_modules/")`
//    （`using static TypeScript.StringUtil.StringUtil;`）。
// 6) `tspath.Path` 零值 `""` → `(Path)""`（`default(Path)` 的 Value 是 null，与 `""` 不等）。
using TypeScript.Ast;
using TypeScript.Collections;
using TypeScript.Core;
using TypeScript.Module;
using TypeScript.TSPath;
using TypeScript.TsOptions;
using static TypeScript.Collections.Collections;
using static TypeScript.Core.Core;
using static TypeScript.StringUtil.StringUtil;
using CompilerModule = TypeScript.Module.Module;
using Path = TypeScript.TSPath.Path;

namespace TypeScript.Compiler;

/// <summary>Go: <c>type projectReferenceFileMapper struct</c>。</summary>
internal sealed class ProjectReferenceFileMapper
{
    internal ProgramOptions _opts = null!;

    /// <summary>Go: <c>host module.ResolutionHost</c>（Program 建好后被置回 nil）。</summary>
    internal ResolutionHost? _host;

    /// <summary>Go: <c>loader *fileLoader</c>——只在填充 mapper 与解析期间存在，之后释放。</summary>
    internal FileLoader? _loader;

    /// <summary>所有已解析的引用。</summary>
    internal Dictionary<Path, ParsedCommandLine?> _configToProjectReference = new();

    /// <summary>配置文件名 → 它引用的配置文件名列表。</summary>
    internal Dictionary<Path, Path[]> _referencesInConfigFile = new();

    internal Dictionary<Path, SourceOutputAndProjectReference> _sourceToProjectReference = new();
    internal Dictionary<Path, SourceOutputAndProjectReference> _outputDtsToProjectReference = new();

    /// <summary>node_modules 里 .d.ts 的 realpath → 工程引用的源文件（解析期间用得到的映射）。</summary>
    internal readonly SyncMap<Path, SourceOutputAndProjectReference?> _realpathDtsToSource = new();

    internal Path RootConfigPath()
    {
        if (_opts.Config.ConfigFile is null)
        {
            return (Path)"";
        }

        return _opts.Config.ConfigFile.SourceFile.Path();
    }

    internal string GetParseFileRedirect(HasFileName file)
    {
        if (_opts.CanUseProjectReferenceSource())
        {
            // Map to source file from project reference
            var source = GetProjectReferenceFromOutputDts(file.Path());
            source ??= GetSourceToDtsIfSymlink(file);
            if (source is not null)
            {
                return source.Source;
            }
        }
        else
        {
            // Map to dts file from project reference
            var output = GetProjectReferenceFromSource(file.Path());
            if (output is not null && output.OutputDts != "")
            {
                return output.OutputDts;
            }
        }

        return "";
    }

    internal ParsedCommandLine?[] GetResolvedProjectReferences()
    {
        var result = new List<ParsedCommandLine?>();
        if (_referencesInConfigFile.TryGetValue(RootConfigPath(), out var refs))
        {
            foreach (var refPath in refs)
            {
                result.Add(_configToProjectReference.GetValueOrDefault(refPath));
            }
        }

        return result.ToArray();
    }

    internal SourceOutputAndProjectReference? GetProjectReferenceFromSource(Path path) =>
        _sourceToProjectReference.GetValueOrDefault(path);

    internal SourceOutputAndProjectReference? GetProjectReferenceFromOutputDts(Path path) =>
        _outputDtsToProjectReference.GetValueOrDefault(path);

    internal bool IsSourceFromProjectReference(Path path) =>
        _opts.CanUseProjectReferenceSource() && GetProjectReferenceFromSource(path) is not null;

    internal CompilerOptions GetCompilerOptionsForFile(HasFileName file)
    {
        var redirect = GetRedirectParsedCommandLineForResolution(file);
        return CompilerModule.GetCompilerOptionsWithRedirect(_opts.Config.CompilerOptions()!, redirect);
    }

    internal ParsedCommandLine? GetRedirectParsedCommandLineForResolution(HasFileName file)
    {
        var (redirect, _) = GetRedirectForResolution(file);
        return redirect;
    }

    internal (ParsedCommandLine? Redirect, string FileName) GetRedirectForResolution(HasFileName file)
    {
        var path = file.Path();
        // Check if outputdts of source file from project reference
        var output = GetProjectReferenceFromSource(path);
        if (output is not null)
        {
            return (output.Resolved, output.Source);
        }

        // Source file from project reference
        var resultFromDts = GetProjectReferenceFromOutputDts(path);
        if (resultFromDts is not null)
        {
            return (resultFromDts.Resolved, resultFromDts.Source);
        }

        var realpathDtsToSource = GetSourceToDtsIfSymlink(file);
        if (realpathDtsToSource is not null)
        {
            return (realpathDtsToSource.Resolved, realpathDtsToSource.Source);
        }

        return (null, file.FileName());
    }

    internal bool TryGetResolvedReferenceFor(Path path, out ParsedCommandLine? config)
    {
        // 键可能存在但值为 nil（Go 的 map 语义），此时返回 true + null。
        return _configToProjectReference.TryGetValue(path, out config);
    }

    internal bool RangeResolvedProjectReference(
        Func<Path, ParsedCommandLine?, ParsedCommandLine?, int, bool> f)
    {
        if (_opts.Config.ProjectReferences() is not { Length: > 0 })
        {
            return false;
        }

        var seenRef = NewSetWithSizeHint<Path>(_referencesInConfigFile.Count);
        var rootConfigPath = RootConfigPath();
        seenRef.Add(rootConfigPath);
        var refs = _referencesInConfigFile.GetValueOrDefault(rootConfigPath) ?? [];
        return RangeResolvedReferenceWorker(refs, f, _opts.Config, seenRef);
    }

    internal bool RangeResolvedReferenceWorker(
        Path[] references,
        Func<Path, ParsedCommandLine?, ParsedCommandLine?, int, bool> f,
        ParsedCommandLine? parent,
        Set<Path> seenRef)
    {
        for (var index = 0; index < references.Length; index++)
        {
            var path = references[index];
            if (!seenRef.AddIfAbsent(path))
            {
                continue;
            }

            var config = _configToProjectReference.GetValueOrDefault(path);
            if (!f(path, config, parent, index))
            {
                return false;
            }

            if (!RangeResolvedReferenceWorker(_referencesInConfigFile.GetValueOrDefault(path) ?? [], f, config, seenRef))
            {
                return false;
            }
        }

        return true;
    }

    internal bool RangeResolvedProjectReferenceInChildConfig(
        ParsedCommandLine? childConfig,
        Func<Path, ParsedCommandLine?, ParsedCommandLine?, int, bool> f)
    {
        if (childConfig?.ConfigFile is null)
        {
            return false;
        }

        var seenRef = NewSetWithSizeHint<Path>(_referencesInConfigFile.Count);
        seenRef.Add(childConfig.ConfigFile.SourceFile.Path());
        var refs = _referencesInConfigFile.GetValueOrDefault(childConfig.ConfigFile.SourceFile.Path()) ?? [];
        return RangeResolvedReferenceWorker(refs, f, _opts.Config, seenRef);
    }

    internal SourceOutputAndProjectReference? GetSourceToDtsIfSymlink(HasFileName file)
    {
        // If preserveSymlinks is true, module resolution wont jump the symlink
        // but the resolved real path may be the .d.ts from project reference
        // Note:: Currently we try the real path only if the
        // file is from node_modules to avoid having to run real path on all file paths
        var path = file.Path();
        var (realpathDtsToSource, ok) = _realpathDtsToSource.Load(path);
        if (ok)
        {
            return realpathDtsToSource;
        }

        if (_loader is not null && _opts.Config.CompilerOptions()!.PreserveSymlinks == Tristate.TSTrue)
        {
            var fileName = file.FileName();
            if (!fileName.Contains("/node_modules/", StringComparison.Ordinal))
            {
                _realpathDtsToSource.Store(path, null);
            }
            else
            {
                // Go 里 host 与 loader 同生共死（processing 结束后一起置 nil）。
                var realDeclarationPath = _loader.ToPath(_host!.FS().Realpath(fileName));
                if (realDeclarationPath == path)
                {
                    _realpathDtsToSource.Store(path, null);
                }
                else
                {
                    var realpathDtsToSourceFromDts = GetProjectReferenceFromOutputDts(realDeclarationPath);
                    if (realpathDtsToSourceFromDts is not null)
                    {
                        _realpathDtsToSource.Store(path, realpathDtsToSourceFromDts);
                        return realpathDtsToSourceFromDts;
                    }

                    _realpathDtsToSource.Store(path, null);
                }
            }
        }

        return null;
    }
}
