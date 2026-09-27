// ported from: internal/execute/incremental/host.go
//
// 与 Go 的偏离（docs/architecture.md §2/§3）：
// 1) Go 的 `SetMTime(fileName string, mTime time.Time) error` → C# 无返回值、失败抛异常（§3.2）。
// 2) Go 的 `time.Time` → `DateTime`；Go 的零值 `time.Time{}` → `default(DateTime)`。
//    `GetMTime` 在 stat 失败时返回零值（调用方用 `== default` 表达 Go 的 `IsZero()`）；
//    `SetMTime` 里 Go 传 `time.Time{}` 作为 aTime，这里逐字落成 `default`。
// 3) Go 未导出类型 `host` → C# `HostImpl`（成员名不能与接口 `Host` 同名，同 ContentMapper/HostImpl.cs 的处理）。
namespace TypeScript.Execute.Incremental;

using TypeScript.Compiler;
using TypeScript.Vfs;

/// <summary>Go: <c>type Host interface</c>（host.go:10-14）。</summary>
public interface Host
{
    /// <summary>Go: <c>FS() vfs.FS</c>。</summary>
    FS FS();

    /// <summary>Go: <c>GetMTime(fileName string) time.Time</c>。</summary>
    DateTime GetMTime(string fileName);

    /// <summary>Go: <c>SetMTime(fileName string, mTime time.Time) error</c>。</summary>
    void SetMTime(string fileName, DateTime mTime);
}

/// <summary>Go: <c>type host struct</c>（host.go:16-18）。</summary>
internal sealed class HostImpl(CompilerHost host) : Host
{
    public FS FS() => host.FS();

    public DateTime GetMTime(string fileName) => Incremental.GetMTime(host, fileName);

    public void SetMTime(string fileName, DateTime mTime) => host.FS().Chtimes(fileName, default, mTime);
}

public static partial class Incremental
{
    /// <summary>Go: <c>func CreateHost(compilerHost compiler.CompilerHost) Host</c>（host.go:34）。</summary>
    public static Host CreateHost(CompilerHost compilerHost) => new HostImpl(compilerHost);

    /// <summary>Go: <c>func GetMTime(host compiler.CompilerHost, fileName string) time.Time</c>（host.go:38-45）。</summary>
    public static DateTime GetMTime(CompilerHost host, string fileName)
    {
        var stat = host.FS().Stat(fileName);
        var mTime = default(DateTime);
        if (stat is not null)
        {
            mTime = stat.ModifiedTimeUtc;
        }
        return mTime;
    }
}
