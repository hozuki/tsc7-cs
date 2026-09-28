// ported from: internal/lsp/server.go:2389-2539（开发者/调试命令 + projectInfo + content mapper 贡献解析）
// 见 Server.cs 文件头。
//
// 与 Go 的偏离（逐条说明）：
//
// 1) `pprof.SaveHeapProfile`/`SaveAllocProfile`/`CPUProfiler.StartCPUProfile`/`StopCPUProfile` 在 C# 侧是
//    `bool TryX(..., out ...)`（Execute/Pprof.cs 文件头第 3/5 条：.NET 没有 runtime/pprof，这些操作永远失败）。
//    Go 的 `if err != nil { return nil, err }` 因此落成 `if (!TryX(...)) throw new InvalidOperationException(...)`。
//
// 2) `valueOrZero[T](value *T) T` → 本文件的 `CloneArray<T>`/`?? default` 组合：C# 的生成物用
//    `string?`/`bool?`/`string[]?` 表达 Go 的 `*T`，而 `valueOrZero` 对**值类型**的 `*bool` 需要 `bool?`，
//    对引用类型是 `??`，两者无法用一个泛型方法覆盖（C# 的 `T?` 在无约束泛型上不等于 `Nullable<T>`），
//    所以逐字段展开。`slices.Clone(x)` → `(T[])x.Clone()`；`Mapper` 的字段是非空的 `T[]`，
//    因此 nil 落到 `[]`（Go 的 nil 切片与空切片在 JSON 上不同，但这些值只进 `project.ContentMapperContributions`，
//    不直接发给客户端）。
//
// 3) `errors.New`/`fmt.Errorf` → C# 的 `InvalidOperationException`（这些是"客户端传了坏贡献"的普通错误，
//    不是 Go 的哨兵错误）。
using System.Text;
using TypeScript.Collections;
using TypeScript.Core;
using TypeScript.LsProto;
using TypeScript.TsOptions;
using static TypeScript.Core.Core;

namespace TypeScript.Lsp;

public sealed partial class Server
{
    /// <summary>Go: <c>func (s *Server) handleRunGC(_, _, _) (lsproto.RunGCResponse, error)</c>（server.go:2389-2393）。</summary>
    private Null HandleRunGC(Context _, NoParams __, RequestMessage ___)
    {
        global::TypeScript.Execute.Pprof.RunGC();
        _logger.Info("GC triggered");
        return new Null();
    }

    /// <summary>Go: <c>func (s *Server) handleSaveHeapProfile(_, params, _) (*lsproto.ProfileResult, error)</c>（server.go:2395-2403）。</summary>
    private ProfileResult? HandleSaveHeapProfile(Context _, ProfileParams? @params, RequestMessage __)
    {
        if (!global::TypeScript.Execute.Pprof.TrySaveHeapProfile(@params!.Dir, out var filePath))
        {
            throw new InvalidOperationException("failed to save heap profile");
        }

        _logger.Info("Heap profile saved to: ", filePath);
        return new ProfileResult { File = filePath };
    }

    /// <summary>Go: <c>func (s *Server) handleSaveAllocProfile(_, params, _) (*lsproto.ProfileResult, error)</c>（server.go:2405-2413）。</summary>
    private ProfileResult? HandleSaveAllocProfile(Context _, ProfileParams? @params, RequestMessage __)
    {
        if (!global::TypeScript.Execute.Pprof.TrySaveAllocProfile(@params!.Dir, out var filePath))
        {
            throw new InvalidOperationException("failed to save allocation profile");
        }

        _logger.Info("Allocation profile saved to: ", filePath);
        return new ProfileResult { File = filePath };
    }

    /// <summary>Go: <c>func (s *Server) handleStartCPUProfile(_, params, _) (lsproto.StartCPUProfileResponse, error)</c>（server.go:2415-2422）。</summary>
    private Null HandleStartCPUProfile(Context _, ProfileParams? @params, RequestMessage __)
    {
        if (!_cpuProfiler.TryStartCPUProfile(@params!.Dir))
        {
            throw new InvalidOperationException("failed to start CPU profile");
        }

        _logger.Info("CPU profiling started, will save to: ", @params.Dir);
        return new Null();
    }

    /// <summary>Go: <c>func (s *Server) handleStopCPUProfile(_, _, _) (*lsproto.ProfileResult, error)</c>（server.go:2424-2431）。</summary>
    private ProfileResult? HandleStopCPUProfile(Context _, NoParams __, RequestMessage ___)
    {
        if (!_cpuProfiler.TryStopCPUProfile(out var filePath))
        {
            throw new InvalidOperationException("failed to stop CPU profile");
        }

        _logger.Info("CPU profile saved to: ", filePath);
        return new ProfileResult { File = filePath };
    }

    /// <summary>Go: <c>func (s *Server) handleProjectInfo(ctx, params, _) (lsproto.CustomProjectInfoResponse, error)</c>（server.go:2433-2446）。</summary>
    private ProjectInfoResult? HandleProjectInfo(Context ctx, ProjectInfoParams? @params, RequestMessage _)
    {
        var uri = @params!.TextDocument.Uri;
        var (defaultProject, _, _, err) = _session!.GetLanguageServiceAndProjectsForFile(ctx, uri);
        if (err is not null)
        {
            throw err;
        }

        var configFilePath = "";
        if (defaultProject is not null && defaultProject.Kind == global::TypeScript.Project.Kind.KindConfigured)
        {
            configFilePath = defaultProject.ConfigFileName();
        }

        return new ProjectInfoResult
        {
            ConfigFilePath = configFilePath,
        };
    }

    /// <summary>Go: <c>func (s *Server) handleSetContentMapperContributions(ctx, params, _)</c>（server.go:2448-2456）。</summary>
    private Null HandleSetContentMapperContributions(Context ctx, SetContentMapperContributionsParams? @params, RequestMessage _)
    {
        var (contributions, err) = ParseContentMapperContributions(@params!.Contributions);
        if (err is not null)
        {
            throw err;
        }

        var documents = Map(@params.OpenDocuments, static document => document.Uri);
        _session!.SetContentMapperContributions(ctx, contributions, documents);
        return new Null();
    }

    /// <summary>Go: <c>func parseContentMapperContributions(values []*lsproto.ContentMapperContribution) (project.ContentMapperContributions, error)</c>（server.go:2458-2514）。</summary>
    private static (global::TypeScript.Project.ContentMapperContributions Result, Exception? Error) ParseContentMapperContributions(
        ContentMapperContribution?[]? values)
    {
        var result = new global::TypeScript.Project.ContentMapperContributions();
        var claimedExtensions = new Set<string>();
        var index = -1;
        foreach (var value in values ?? [])
        {
            index++;
            if (value is null || value.ContributorId == "")
            {
                return (result, new InvalidOperationException("content mapper contribution requires a contributorId"));
            }

            var identity = $"{value.ContributorId}[{index}]";
            var validExtensions = new List<string>();
            foreach (var extension in value.Extensions ?? [])
            {
                if (!IsValidContributedContentMapperExtension(extension))
                {
                    return (result, new InvalidOperationException($"content mapper contribution \"{identity}\" has invalid extension \"{extension}\""));
                }

                validExtensions.Add(extension);
            }

            if (value.InferredProjectContribution is null)
            {
                continue;
            }

            var inferredProject = value.InferredProjectContribution;
            var manifest = inferredProject.Manifest!;
            if (manifest.Name == "" || manifest.Exec is not { Length: > 0 })
            {
                return (result, new InvalidOperationException($"content mapper contribution \"{identity}\" requires a manifest name and exec"));
            }

            foreach (var option in manifest.CompilerOptions ?? [])
            {
                if (TsOptions.TsOptions.CommandLineCompilerOptionsMap.Get(option) is null)
                {
                    return (result, new InvalidOperationException($"content mapper contribution \"{identity}\" requests unknown compiler option \"{option}\""));
                }
            }

            foreach (var extension in validExtensions)
            {
                if (!claimedExtensions.AddIfAbsent(extension.ToLowerInvariant()))
                {
                    return (result, new InvalidOperationException($"content mapper contributions both claim extension \"{extension}\""));
                }

                result.Extensions = [.. result.Extensions, extension];
            }

            byte[] options = "{}"u8.ToArray();
            if (inferredProject.Options is not null)
            {
                options = Json.Json.Marshal(inferredProject.Options);
            }

            var mapper = new global::TypeScript.ContentMapper.Mapper
            {
                Package = identity,
                Extensions = [.. validExtensions],
                Options = options,
                Name = manifest.Name,
                Version = manifest.Version ?? "",
                Exec = CloneArray(manifest.Exec) ?? [],
                CompilerOptions = CloneArray(manifest.CompilerOptions) ?? [],
                DynamicConfig = manifest.DynamicConfig ?? false,
                ContributionID = identity,
            };
            if (manifest.Cwd is not null)
            {
                if (!TSPath.TSPath.PathIsAbsolute(manifest.Cwd))
                {
                    return (result, new InvalidOperationException($"content mapper contribution \"{identity}\" has non-absolute cwd"));
                }

                mapper.PackageDirectory = manifest.Cwd;
            }

            result.Mappers = [.. result.Mappers, mapper];
        }

        Array.Sort(result.Extensions, StringComparer.Ordinal);
        return (result, null);
    }

    /// <summary>Go: <c>func isValidContributedContentMapperExtension(extension string) bool</c>（server.go:2516-2522）。</summary>
    private static bool IsValidContributedContentMapperExtension(string extension)
    {
        if (extension.Length <= 1
            || extension[0] != '.'
            || TSPath.TSPath.GetAnyExtensionFromPath("file" + extension, [], false) != extension)
        {
            return false;
        }

        return !Some(
            Flatten(TSPath.TSPath.AllSupportedExtensionsWithJson),
            nativeExtension => string.Equals(nativeExtension, extension, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Go 的 <c>slices.Clone</c>（nil 保持 nil；见文件头第 2 条）。</summary>
    private static T[]? CloneArray<T>(T[]? value) => value is null ? null : (T[])value.Clone();
}
