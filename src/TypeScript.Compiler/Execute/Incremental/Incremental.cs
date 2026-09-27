// ported from: internal/execute/incremental/incremental.go
//
// 与 Go 的偏离（docs/architecture.md §2/§3）：
// 1) Go 的 `BuildInfoReader` 是包级接口 → C# 顶层接口 `BuildInfoReader`；实现类是 Go 的未导出 `buildInfoReader`
//    → `internal BuildInfoReaderImpl`。
// 2) `fs.ReadFile(name) (string, bool)` → `FS.TryReadFile(name, out contents)`（§3.2）。
// 3) `json.Unmarshal([]byte(data), &buildInfo)` 的 error → C# 里是异常；失败返回 nil/null（与 Go 的可观察行为一致）。
// 4) `ContentMapperIdentities` 的 error → 抛异常（§3.2）；`host.ContentMapperProject()` 可能是 nil。
// 5) `ContentMapperIdentitiesMatch(err != nil || ...)`：Go 的 `err != nil` 短路在 C# 里是 try/catch。
namespace TypeScript.Execute.Incremental;

using System.Text;
using TypeScript.Compiler;
using TypeScript.Json;
using TypeScript.TsOptions;

/// <summary>Go: <c>type BuildInfoReader interface</c>（incremental.go:10-12）。</summary>
public interface BuildInfoReader
{
    /// <summary>Go: <c>ReadBuildInfo(config *tsoptions.ParsedCommandLine) *BuildInfo</c>。</summary>
    BuildInfo? ReadBuildInfo(ParsedCommandLine config);
}

/// <summary>Go: <c>type buildInfoReader struct</c>（incremental.go:15-17）。</summary>
internal sealed class BuildInfoReaderImpl(CompilerHost host) : BuildInfoReader
{
    public BuildInfo? ReadBuildInfo(ParsedCommandLine config)
    {
        var buildInfoFileName = config.GetBuildInfoFileName();
        if (buildInfoFileName == "")
        {
            return null;
        }

        // Read build info file
        if (!host.FS().TryReadFile(buildInfoFileName, out var data))
        {
            return null;
        }
        try
        {
            return Json.UnmarshalNew<BuildInfo>(Encoding.UTF8.GetBytes(data));
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

public static partial class Incremental
{
    /// <summary>Go: <c>func NewBuildInfoReader(host compiler.CompilerHost) BuildInfoReader</c>（incremental.go:38）。</summary>
    public static BuildInfoReader NewBuildInfoReader(CompilerHost host) => new BuildInfoReaderImpl(host);

    /// <summary>Go: <c>func ReadBuildInfoProgram(config, reader, host) *Program</c>（incremental.go:44）。</summary>
    public static Program? ReadBuildInfoProgram(ParsedCommandLine config, BuildInfoReader reader, CompilerHost host)
    {
        // Read buildInfo file
        var buildInfo = reader.ReadBuildInfo(config);
        if (buildInfo is null || !buildInfo.IsValidVersion() || !buildInfo.IsIncremental())
        {
            return null;
        }
        // If any configured content mapper's identity has changed, files it produced may be stale, so the
        // old program cannot be reused.
        List<string>? contentMapperIdentities;
        try
        {
            contentMapperIdentities = ContentMapperIdentities(host.ContentMapperProject());
        }
        catch (Exception)
        {
            return null;
        }
        if (!buildInfo.ContentMapperIdentitiesMatch(contentMapperIdentities))
        {
            return null;
        }

        // Convert to information that can be used to create incremental program
        var incrementalProgram = new Program
        {
            _snapshot = BuildInfoToSnapshot(buildInfo, config, host),
        };
        return incrementalProgram;
    }
}
