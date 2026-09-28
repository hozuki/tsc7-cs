// ported from: internal/execute/tsctests/fs.go
//
// 与 Go 的偏离（逐条）：
// 1) Go 的 `testFs` 内嵌 `vfs.FS`（未覆盖的方法直接透传）→ C# 的 `FS` 是接口，必须显式实现全部 11 个成员
//    （逐条转调 <see cref="Inner"/>）；只有 `ReadFile`/`WriteFile`/`Remove` 有拦截逻辑（与 Go 相同的一批）。
// 2) `json.Unmarshal([]byte(s), &buildInfo)`（Go 把 error 当控制流）→ C# 的 `Json.UnmarshalNew<T>` 失败抛
//    `JsonException`，这里 `catch` 后与 Go 的 `err == nil` 判真逐条对应。
// 3) `json.Marshal(&buildInfo)` → `Json.Marshal(buildInfo)`（字节）→ `Encoding.UTF8.GetString(...)`（同
//    Execute/Incremental/Program.cs:446 的既有写法）。
// 4) `panic(...)` → `Debug.Fail(...)`；`fmt.Errorf` 的包装在 C# 侧由异常承担（本文件没有需要包装的错误）。
// 5) Go 的 `defaultLibs *collections.SyncSet[string]` 可空 → C# `SyncSet<string>?`。
namespace TypeScript.Harness.TscTests;

using global::System;
using global::System.Text;
using TypeScript.Collections;
using TypeScript.Execute.Incremental;
using TypeScript.TestUtil.FsBaselineUtil;
using TypeScript.TestUtil.HarnessUtil;
using TypeScript.Vfs;
using TypeScript.Vfs.Iovfs;

using static TypeScript.Json.Json;
using static TypeScript.TSPath.TSPath;
using VfsFileInfo = TypeScript.Vfs.FileInfo;

/// <summary>Go: <c>type testFs struct</c>（tsctests/fs.go:15-19）。</summary>
public sealed class TestFs : FS
{
    /// <summary>Go: 内嵌的 <c>vfs.FS</c>（实际是 <c>iovfs.FsWithSys</c>）。</summary>
    public readonly FS Inner;

    /// <summary>Go: <c>defaultLibs *collections.SyncSet[string]</c>（nil = 尚未写过 lib）。</summary>
    public SyncSet<string>? DefaultLibs;

    /// <summary>Go: <c>writtenFiles collections.SyncSet[string]</c>（与 FSDiffer 共享同一个实例）。</summary>
    public readonly SyncSet<string> WrittenFiles = new();

    public TestFs(FS inner) => Inner = inner;

    /// <summary>Go: <c>func (f *testFs) removeIgnoreLibPath(path string)</c>。</summary>
    private void RemoveIgnoreLibPath(string path)
    {
        if (DefaultLibs is not null && DefaultLibs.Has(path))
        {
            DefaultLibs.Delete(path);
        }
    }

    /// <summary>Go: <c>func (f *testFs) ReadFile(path string) (contents string, ok bool)</c>。</summary>
    public bool TryReadFile(string path, out string contents)
    {
        RemoveIgnoreLibPath(path);
        return ReadFileHandlingBuildInfo(path, out contents);
    }

    /// <summary>Go: <c>func (f *testFs) readFileHandlingBuildInfo(path string) (contents string, ok bool)</c>。</summary>
    private bool ReadFileHandlingBuildInfo(string path, out string contents)
    {
        var ok = Inner.TryReadFile(path, out contents);
        if (ok && FileExtensionIs(path, ExtensionTsBuildInfo))
        {
            // read buildinfo and modify version
            BuildInfo? buildInfo = null;
            try
            {
                buildInfo = UnmarshalNew<BuildInfo>(Encoding.UTF8.GetBytes(contents));
            }
            catch (Exception ex) when (ex is TypeScript.Json.JsonException or FormatException)
            {
                // Go: err != nil → 不做任何替换
            }

            if (buildInfo is not null && buildInfo.Version == HarnessUtil.FakeTSVersion)
            {
                buildInfo.Version = TypeScript.Core.Core.Version();
                contents = Encoding.UTF8.GetString(Marshal(buildInfo));
            }
        }

        return ok;
    }

    /// <summary>Go: <c>func (f *testFs) WriteFile(path string, data string) error</c>。</summary>
    public void WriteFile(string path, string data)
    {
        RemoveIgnoreLibPath(path);
        WrittenFiles.Add(path);
        WriteFileHandlingBuildInfo(path, data);
    }

    /// <summary>Go: <c>func (f *testFs) writeFileHandlingBuildInfo(path string, data string) error</c>。</summary>
    private void WriteFileHandlingBuildInfo(string path, string data)
    {
        if (FileExtensionIs(path, ExtensionTsBuildInfo))
        {
            BuildInfo? buildInfo = null;
            try
            {
                buildInfo = UnmarshalNew<BuildInfo>(Encoding.UTF8.GetBytes(data));
            }
            catch (Exception ex) when (ex is TypeScript.Json.JsonException or FormatException)
            {
                // Go: err != nil → panic
                Debug.Fail("testFs.WriteFile: failed to unmarshal build info: - use underlying FS's write method if this is intended use for testcase");
            }

            if (buildInfo is not null)
            {
                if (buildInfo.Version == TypeScript.Core.Core.Version())
                {
                    // Change it to harnessutil.FakeTSVersion
                    buildInfo.Version = HarnessUtil.FakeTSVersion;
                    data = Encoding.UTF8.GetString(Marshal(buildInfo));
                }

                // Write readable build info version
                Inner.WriteFile(
                    path + ".readable.baseline.txt",
                    ReadableBuildInfo.ToReadableBuildInfo(buildInfo, FSDiffer.SanitizeInternalSymbolName(data)));
            }
        }

        Inner.WriteFile(path, data);
    }

    /// <summary>Go: <c>func (f *testFs) Remove(path string) error</c>。</summary>
    public void Remove(string path)
    {
        RemoveIgnoreLibPath(path);
        Inner.Remove(path);
    }

    // ---- 以下全部是 Go 里"内嵌 vfs.FS 自动透传"的成员（见文件头偏离 1）----

    public bool UseCaseSensitiveFileNames() => Inner.UseCaseSensitiveFileNames();

    public bool FileExists(string path) => Inner.FileExists(path);

    public void AppendFile(string path, string data) => Inner.AppendFile(path, data);

    public void Chtimes(string path, DateTime accessTime, DateTime modificationTime) =>
        Inner.Chtimes(path, accessTime, modificationTime);

    public bool DirectoryExists(string path) => Inner.DirectoryExists(path);

    public Entries GetAccessibleEntries(string path) => Inner.GetAccessibleEntries(path);

    public VfsFileInfo? Stat(string path) => Inner.Stat(path);

    public string Realpath(string path) => Inner.Realpath(path);

    /// <summary>非 Go 源：`fs.go` 之外的调用点要拿回 <c>iovfs.FsWithSys</c>（等价于 Go 的 <c>f.FS.(…)</c> 断言）。</summary>
    public FsWithSys FsWithSys() => (FsWithSys)Inner;
}
