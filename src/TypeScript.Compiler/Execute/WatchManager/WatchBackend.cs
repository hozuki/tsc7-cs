// ported from: internal/execute/watchmanager/watchbackend.go
//
// 与 Go 的偏离（docs/architecture.md §2/§3）：
// 1) `io.Closer` → `TypeScript.FsWatch.Watch`（fswatch 的 watch 句柄；`Close() error` 在 FsWatch 移植里已是 `void Close()`，
//    失败抛异常）；`fswatch.WatchCallback` → `TypeScript.FsWatch.WatchCallback`。
// 2) `(io.Closer, error)` / `([]io.Closer, error)` → 只返回值、失败抛异常（§3.2）：`Watch` / `Watch[]`。于是
//    `WatchDirectory` 里 Go 的 `if err != nil { return nil, err }` 没有对应代码——异常直接从 `WatchDirectories` 传播。
// 3) Go 的 `ignore func(string) bool` 可为 nil → `Func<string, bool>?`；导出字段 `Dir`/`Callback`/`Recursive`/`Ignore`/`Inner`
//    保持 public 字段原名（architecture.md §2），由调用点用对象初始化器赋值（对应 Go 的结构体字面量）。
// 4) 本包自己的 `WatchDirectoryRequest` 与 `TypeScript.FsWatch.WatchDirectoryRequest` 同名（Go 里靠包名区分）→ 本文件用别名
//    `FsWatchDirectoryRequest` 指后者；简单名 `WatchDirectoryRequest` 一律指本包的。
// 5) `strings.HasSuffix`/`strings.Contains`（大小写敏感）→ `EndsWith(..., StringComparison.Ordinal)` /
//    `Contains(..., StringComparison.Ordinal)`；`strings.EqualFold` → `StringUtil.EquateStringCaseInsensitive`（.NET 没有
//    Go 的 SimpleFold 等价物，见 StringUtil/Compare.cs 偏离 3）。
// 6) `tspath.*` → 静态类 `TypeScript.TSPath.TSPath` 的成员（`using static` 后裸名调用，同 Execute/Tsc/Emit.cs）。
//    注意 `Path` 在 TSPath 里是 Go 的 `type Path string`（record struct），不是工具类；`System.IO.Path` 由 ImplicitUsings 提供。
// 7) `fswatch.WithRecursive()/WithIgnore(fn)` → `Fsw.WithRecursive()/Fsw.WithIgnore(fn)`：`FsWatch` 既是命名空间又是同名静态类，
//    用别名 `Fsw` 指静态类，避免"简单名先命中外层命名空间"的老问题（见 Checker.C6.ApparentTypes.cs 偏离 17/18）。
// 8) Go 的 `var opts []fswatch.WatchOption` + append 是 nil/非 nil 两态 → C# 用 `List<WatchOption>` 收集、无选项时把
//    `Options` 留成 `null`（fswatch 侧 nil 与空数组行为相同，这里与 Go 的字面值保持一致）。
// 9) `for i, watch := range watches { closers[i] = watch }`（把 `[]Watch` 逐元素拷进 `[]io.Closer`）在 C# 里没有对应代码：
//    `Watch[]` 就是返回类型，直接返回同一个数组（元素类型相同，不再多一次拷贝）。
// 10) Go 的包级函数 `ShouldIgnoreWatchPath`/`CanWatchDirectory`/`PerceivedOsRootLengthForWatching` 与类型 `WatchManager` 同包，
//     C# 里不能有与类型同名的静态类（Go 的包名不与类型名冲突）→ 按仓库/调用方约定挂在 `WatchManager` **类型**上做 public static
//     （见 WatchManager.cs 偏离 1）。为了保持"一个 Go 文件 ↔ 一个 C# 文件"，本文件把 `WatchManager` 声明成 `partial`。
namespace TypeScript.Execute.WatchManager;

using TypeScript.FsWatch;
using TypeScript.TSPath;
using FsWatchDirectoryRequest = TypeScript.FsWatch.WatchDirectoryRequest;
using Fsw = TypeScript.FsWatch.FsWatch;
using static TypeScript.StringUtil.StringUtil;
using static TypeScript.TSPath.TSPath;

/// <summary>Go: <c>type WatchBackend interface</c>（watchbackend.go:12-15）。抽象 fswatch.Watcher 以便测试。</summary>
public interface WatchBackend
{
    /// <summary>Go: <c>WatchDirectory(dir string, fn fswatch.WatchCallback, recursive bool, ignore func(string) bool) (io.Closer, error)</c>。</summary>
    Watch WatchDirectory(string dir, WatchCallback fn, bool recursive, Func<string, bool>? ignore);

    /// <summary>Go: <c>WatchDirectories(requests []WatchDirectoryRequest) ([]io.Closer, error)</c>。</summary>
    Watch[] WatchDirectories(WatchDirectoryRequest[] requests);
}

/// <summary>Go: <c>type WatchDirectoryRequest struct</c>（watchbackend.go:17-22）。</summary>
public sealed class WatchDirectoryRequest
{
    /// <summary>Go: <c>Dir string</c>。</summary>
    public string Dir = "";

    /// <summary>Go: <c>Callback fswatch.WatchCallback</c>（nil 时后端按 error 拒绝）。</summary>
    public WatchCallback? Callback;

    /// <summary>Go: <c>Recursive bool</c>。</summary>
    public bool Recursive;

    /// <summary>Go: <c>Ignore func(string) bool</c>（nil = 不过滤）。</summary>
    public Func<string, bool>? Ignore;
}

/// <summary>Go: <c>type CommandLineTestingWithWatchBackend interface</c>（watchbackend.go:26-28）：
/// <c>CommandLineTesting</c> 的可选扩展，测试模式下提供 <see cref="WatchBackend"/>。</summary>
public interface CommandLineTestingWithWatchBackend
{
    /// <summary>Go: <c>WatchBackend() WatchBackend</c>。</summary>
    WatchBackend WatchBackend();
}

/// <summary>Go: <c>type FSWatchBackend struct{ Inner fswatch.Watcher }</c>（watchbackend.go:30）。</summary>
public sealed class FSWatchBackend : WatchBackend
{
    /// <summary>Go: <c>Inner fswatch.Watcher</c>（Go 的零值是 nil；调用点总是先赋值再用，同 EnsureDefaultBackend）。</summary>
    public Watcher? Inner;

    /// <summary>Go: <c>func (b *FSWatchBackend) WatchDirectory(dir string, fn fswatch.WatchCallback, recursive bool, ignore func(string) bool) (io.Closer, error)</c>（watchbackend.go:32-43）。</summary>
    public Watch WatchDirectory(string dir, WatchCallback fn, bool recursive, Func<string, bool>? ignore)
    {
        var closers = WatchDirectories(
        [
            new WatchDirectoryRequest { Dir = dir, Callback = fn, Recursive = recursive, Ignore = ignore },
        ]);
        return closers[0];
    }

    /// <summary>Go: <c>func (b *FSWatchBackend) WatchDirectories(requests []WatchDirectoryRequest) ([]io.Closer, error)</c>（watchbackend.go:45-70）。</summary>
    public Watch[] WatchDirectories(WatchDirectoryRequest[] requests)
    {
        var fswatchRequests = new FsWatchDirectoryRequest[requests.Length];
        for (var i = 0; i < requests.Length; i++)
        {
            var request = requests[i];
            var opts = new List<WatchOption>(2);
            if (request.Recursive)
            {
                opts.Add(Fsw.WithRecursive());
            }
            if (request.Ignore is { } ignore)
            {
                opts.Add(Fsw.WithIgnore(ignore));
            }
            fswatchRequests[i] = new FsWatchDirectoryRequest
            {
                Dir = request.Dir,
                Callback = request.Callback,
                Options = opts.Count > 0 ? opts.ToArray() : null,
            };
        }
        return Inner!.WatchDirectories(fswatchRequests);
    }
}

public sealed partial class WatchManager
{
    /// <summary>Go: <c>func ShouldIgnoreWatchPath(path string) bool</c>（watchbackend.go:72-78）。</summary>
    public static bool ShouldIgnoreWatchPath(string path)
    {
        var p = NormalizeSlashes(path);
        return p.EndsWith("/.git", StringComparison.Ordinal)
            || p.Contains("/.git/", StringComparison.Ordinal)
            || p.Contains("/node_modules/.", StringComparison.Ordinal)
            || p.Contains("/.#", StringComparison.Ordinal);
    }

    /// <summary>Go: <c>func CanWatchDirectory(dir string) bool</c>（watchbackend.go:80-88）。</summary>
    public static bool CanWatchDirectory(string dir)
    {
        var components = GetPathComponents(dir, "");
        var length = components.Length;
        if (length <= 2)
        {
            return false;
        }
        var rootLength = PerceivedOsRootLengthForWatching(components);
        return length > rootLength + 1;
    }

    /// <summary>Go: <c>func PerceivedOsRootLengthForWatching(components []string) int</c>（watchbackend.go:90-118）。</summary>
    public static int PerceivedOsRootLengthForWatching(string[] components)
    {
        var length = components.Length;
        if (length <= 1)
        {
            return 1;
        }
        var root = components[0];
        var indexAfterOsRoot = 1;
        var isDosStyle = root.Length >= 2 && IsVolumeCharacter(root[0]) && root[1] == ':';

        if (root != "/" && !isDosStyle && components.Length > 1)
        {
            if (components[1].Length >= 2 && IsVolumeCharacter(components[1][0]) && components[1].EndsWith('$'))
            {
                if (length == 2)
                {
                    return 2;
                }
                indexAfterOsRoot = 2;
                isDosStyle = true;
            }
        }

        if (isDosStyle && (indexAfterOsRoot >= length || !EquateStringCaseInsensitive(components[indexAfterOsRoot], "users")))
        {
            return indexAfterOsRoot;
        }

        if (indexAfterOsRoot < length && EquateStringCaseInsensitive(components[indexAfterOsRoot], "workspaces"))
        {
            return indexAfterOsRoot + 1;
        }

        return indexAfterOsRoot + 2;
    }
}
