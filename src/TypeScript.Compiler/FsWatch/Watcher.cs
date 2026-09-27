// ported from: internal/fswatch/watcher.go
//
// C# 映射与偏离（逐条）：
// 1) Go 的哨兵错误 `ErrOverflow`/`ErrWatchTerminated`/`ErrUnavailable`/`ErrFilesystemUnsupported` 以及
//    `errNilCallback`/`errRootPath`/`errNotAbsolute` 都是 `errors.New` 的值，调用点用 `errors.Is` 判定 →
//    C# 统一成 `FsWatchException` + `FsWatchErrorKind`（`errors.Is(err, sentinel)` ⇔ 沿 InnerException 链找
//    `FsWatchException` 的 Kind，见 `FsWatchException.Is`）。**不共享实例**：C# 的异常在抛出时记录栈，跨抛复用同一
//    实例会把首个调用点的栈永久粘住，所以一律新建（`FsWatchException.Overflow(...)` 这类工厂）。
// 2) Go 的接口带未导出方法 `unexported()` 以阻止外部实现 → C# 无法表达；`Watcher`/`Watch` 只靠文档约束
//    （本 package 内只有 `FsWatcher`/`FallbackWatcher`/`WatchHandle` 实现）。
// 3) Go 的 `WatchOption` 是带未导出方法的接口 → C# `abstract class WatchOption` + `private protected` 构造函数
//    （只允许本程序集派生，是 C# 里最接近 "未导出方法" 的表达）。
// 4) Go 的 `WatchCallback func(events []Event, err error)` → `delegate void WatchCallback(Event[]? events,
//    Exception? err)`：只有 error 时 events 是 nil（Go 的 nil 切片 → `Event[]?`）。
// 5) Go 的 `filepath.Clean`/`Dir`/`IsAbs`/`os.IsPathSeparator` → 本文件的 `CleanPath`/`DirPath`/`IsAbsPath`/
//    `IsPathSeparator`（Go 的 stdlib 没有 C# 对应物；`Path.GetFullPath` 语义不同——它会解析 CWD 与真实路径，
//    且保留尾部分隔符）。`CleanPath` 逐行对应 Go 的 `filepath.Clean`（词法清理，含卷名）。
// 6) Go 的 `nativepath.Realpath` → `TypeScript.NativePath.NativePath.Realpath`（已移植，失败返回 null）。
// 7) `*dirWatch`/`*watcher` → 引用类型 `DirWatch`/`FsWatcher`；`w.state any` → `object?`；`w.sequence func() uint64`
//    → `Func<ulong>?`。
// 8) Go 的 `sync.Mutex` → `lock (_mu)`；锁的获取顺序（watcher → dirWatch）与 Go 一致，避免死锁。
// 9) `Watch.Close() error` → C# `void Close()`（失败抛异常，§3.2）；Go 的 `impl.watchRemove` 返回的 error 在 Go 里
//    也被丢弃（`s.impl.watchRemove(s.dw)`），C# 同样不传播。
// 10) Go 的 `Default()` 用 `runtime.GOOS` 覆盖 linux/android/darwin/windows/freebsd/openbsd/netbsd/dragonfly；
//     .NET 的 `OperatingSystem.Is*` 没有 OpenBSD/NetBSD/DragonFly 探测（只有 IsFreeBSD），因此这三个平台会落到
//     `unsupported` 分支（Go 走 kqueue）。它们的 kqueue 后端仍可显式 `Kqueue()` 取用；`Default()` 的这点差别记在此处。
// 11) Go 的 `filepath.Clean`/`Dir` 是 stdlib，C# 没有对应物且 `Path.GetFullPath` 语义不同（会解析 CWD/真实路径、
//     保留尾分隔符）→ 本文件的 `CleanPath`/`DirPath` 逐行照 Go 的算法（含 Windows 卷名与 UNC）重写。
// 12) Go 的未导出字段 `watcher.factory`/`watcher.sequence`、`fallbackWatcher.primary`/`secondary` 在 C# 里落成
//     internal **属性** `Factory`/`Sequence`/`Primary`/`Secondary`：它们只是装配点（平台后端的模块初始化器）
//     与只读断言，属性形态比 `_camelCase` 字段更贴切。`DirWatch` 的字段因为被各平台后端频繁读写，仍按仓库规则
//     用 `internal _camelCase` 字段。
namespace TypeScript.FsWatch;

/// <summary>Go: <c>type WatchCallback func(events []Event, err error)</c>。</summary>
/// <param name="events">批次事件；只有错误时为 null（Go 的 nil 切片）。</param>
/// <param name="err">非空时用 <see cref="FsWatchException.Is"/> 判定 <see cref="FsWatchErrorKind.Overflow"/>
/// （可恢复）或 <see cref="FsWatchErrorKind.WatchTerminated"/>（终止）。</param>
public delegate void WatchCallback(Event[]? events, Exception? err);

/// <summary>Go 的哨兵错误集合（<c>ErrOverflow</c> 等）在 C# 侧的判别标记。</summary>
public enum FsWatchErrorKind
{
    /// <summary>Go: <c>errNilCallback</c>。</summary>
    NilCallback,

    /// <summary>Go: <c>errRootPath</c>。</summary>
    RootPath,

    /// <summary>Go: <c>errNotAbsolute</c>。</summary>
    NotAbsolute,

    /// <summary>Go: <c>ErrOverflow</c>。内核事件队列溢出，部分变更丢失；watch 仍然有效。</summary>
    Overflow,

    /// <summary>Go: <c>ErrWatchTerminated</c>。不可恢复错误（例如被监听目录被删除）导致 watch 终止。</summary>
    WatchTerminated,

    /// <summary>Go: <c>ErrUnavailable</c>。当前平台不支持所请求的 watcher。</summary>
    Unavailable,

    /// <summary>Go: <c>ErrFilesystemUnsupported</c>。后端在平台上可用，但目标文件系统不支持它。</summary>
    FilesystemUnsupported,
}

/// <summary>Go 的 <c>errors.New("fswatch: …")</c> 哨兵在 C# 侧的承载类型（见文件头第 1 条）。</summary>
public sealed class FsWatchException : Exception
{
    public FsWatchException(FsWatchErrorKind kind, string message, Exception? innerException = null)
        : base(message, innerException)
    {
        Kind = kind;
    }

    public FsWatchErrorKind Kind { get; }

    /// <summary>Go: <c>errNilCallback</c>。</summary>
    public static FsWatchException NilCallback() => new(FsWatchErrorKind.NilCallback, "fswatch: callback must not be nil");

    /// <summary>Go: <c>errRootPath</c>。</summary>
    public static FsWatchException RootPath() => new(FsWatchErrorKind.RootPath, "fswatch: cannot watch a root path");

    /// <summary>Go: <c>errNotAbsolute</c>。</summary>
    public static FsWatchException NotAbsolute() => new(FsWatchErrorKind.NotAbsolute, "fswatch: path must be absolute");

    /// <summary>Go: <c>ErrOverflow</c>（带上下文的包装文本；<paramref name="detail"/> 为 Go 里 <c>%w</c> 之前的前缀）。</summary>
    public static FsWatchException Overflow(string? detail = null, Exception? innerException = null) =>
        new(FsWatchErrorKind.Overflow,
            detail is null ? "fswatch: event overflow; some changes were missed" : detail,
            innerException);

    /// <summary>Go: <c>ErrWatchTerminated</c>。</summary>
    public static FsWatchException WatchTerminated(string message = "fswatch: watch terminated", Exception? innerException = null) =>
        new(FsWatchErrorKind.WatchTerminated, message, innerException);

    /// <summary>Go: <c>ErrUnavailable</c>。</summary>
    public static FsWatchException Unavailable() => new(FsWatchErrorKind.Unavailable, "fswatch: watcher not available on this platform");

    /// <summary>Go: <c>ErrFilesystemUnsupported</c>。</summary>
    public static FsWatchException FilesystemUnsupported() =>
        new(FsWatchErrorKind.FilesystemUnsupported, "fswatch: watcher backend unsupported on this filesystem");

    /// <summary>Go: <c>errors.Is(err, sentinel)</c>（沿 InnerException 链找同 Kind 的 <see cref="FsWatchException"/>）。</summary>
    public static bool Is(Exception? err, FsWatchErrorKind kind)
    {
        for (var e = err; e is not null; e = e.InnerException)
        {
            if (e is FsWatchException fsw && fsw.Kind == kind)
            {
                return true;
            }
        }

        return false;
    }
}

/// <summary>Go: <c>type Watch interface</c>。存活的 watch；<see cref="Close"/> 停止监听并释放资源（幂等）。</summary>
public interface Watch
{
    /// <summary>Go: <c>Close() error</c>。失败抛异常。</summary>
    void Close();
}

/// <summary>Go: <c>type WatchOption interface</c>（配置一次监听）。</summary>
public abstract class WatchOption
{
    private protected WatchOption()
    {
    }

    internal abstract void ApplyWatchOption(WatchOptions opts);
}

/// <summary>Go: <c>type watchOptions struct</c>。</summary>
internal sealed class WatchOptions
{
    internal Func<string, bool>? Ignore;
    internal bool Recursive;
    internal string File = "";
}

/// <summary>Go: <c>type fileOption struct</c>。</summary>
internal sealed class FileOption(string path) : WatchOption
{
    internal override void ApplyWatchOption(WatchOptions opts) => opts.File = path;
}

/// <summary>Go: <c>type ignoreOption struct</c>。</summary>
internal sealed class IgnoreOption(Func<string, bool> fn) : WatchOption
{
    internal override void ApplyWatchOption(WatchOptions opts) => opts.Ignore = fn;
}

/// <summary>Go: <c>type recursiveOption struct</c>。</summary>
internal sealed class RecursiveOption : WatchOption
{
    internal override void ApplyWatchOption(WatchOptions opts) => opts.Recursive = true;
}

/// <summary>Go: <c>type WatchDirectoryRequest struct</c>。</summary>
public sealed class WatchDirectoryRequest
{
    public string Dir { get; set; } = "";

    public WatchCallback? Callback { get; set; }

    public WatchOption[]? Options { get; set; }
}

/// <summary>Go: <c>type Watcher interface</c>。文件系统监听实现；用 <see cref="FsWatch.Default"/> 取平台默认。</summary>
public interface Watcher
{
    /// <summary>Go: <c>Name() string</c>。稳定标识（"inotify"、"fsevents"、"kqueue"、"windows"）。</summary>
    string Name();

    /// <summary>Go: <c>Available() bool</c>。</summary>
    bool Available();

    /// <summary>Go: <c>HasFastRecursiveBackend() bool</c>。Windows（ReadDirectoryChangesW 子树模式）与 macOS
    /// FSEvents 为 true；其余后端需要用户态整树遍历。</summary>
    bool HasFastRecursiveBackend();

    /// <summary>Go: <c>WatchDirectory(dir string, fn WatchCallback, opts ...WatchOption) (Watch, error)</c>。</summary>
    Watch WatchDirectory(string dir, WatchCallback fn, params WatchOption[] opts);

    /// <summary>Go: <c>WatchDirectories(requests []WatchDirectoryRequest) ([]Watch, error)</c>。返回顺序与请求一致。</summary>
    Watch[] WatchDirectories(WatchDirectoryRequest[] requests);

    /// <summary>Go: <c>WatchFile(path string, fn WatchCallback) (Watch, error)</c>。文件不必已存在，父目录必须存在。</summary>
    Watch WatchFile(string path, WatchCallback fn);
}

public static partial class FsWatch
{
    // Go 的包级 watcher 实例（各平台的 init() 设置 factory）。
    private static readonly FsWatcher _inotifyWatcher = new("inotify");
    private static readonly FsWatcher _fseventsWatcher = new("fsevents");
    private static readonly FsWatcher _kqueueWatcher = new("kqueue");
    private static readonly FsWatcher _windowsWatcher = new("windows");
    private static readonly FsWatcher _fanotifyWatcher = new("fanotify");
    private static readonly FallbackWatcher _fanotifyFallbackWatcher = new(_fanotifyWatcher, _inotifyWatcher);

    /// <summary>Go 的各平台包级实例（后端模块初始化器把自己的 factory 装进来）。</summary>
    internal static FsWatcher InotifyWatcherInstance => _inotifyWatcher;

    internal static FsWatcher FSEventsWatcherInstance => _fseventsWatcher;

    internal static FsWatcher KqueueWatcherInstance => _kqueueWatcher;

    internal static FsWatcher WindowsWatcherInstance => _windowsWatcher;

    internal static FsWatcher FanotifyWatcherInstance => _fanotifyWatcher;

    /// <summary>Go: <c>func AllWatchers() []Watcher</c>。</summary>
    public static Watcher[] AllWatchers() =>
    [
        _inotifyWatcher,
        _fseventsWatcher,
        _kqueueWatcher,
        _windowsWatcher,
        _fanotifyFallbackWatcher,
    ];

    /// <summary>Go: <c>func Inotify() Watcher</c>（Linux/Android）。</summary>
    public static Watcher Inotify() => _inotifyWatcher;

    /// <summary>Go: <c>func FSEvents() Watcher</c>（macOS）。</summary>
    public static Watcher FSEvents() => _fseventsWatcher;

    /// <summary>Go: <c>func Kqueue() Watcher</c>（macOS 与其它 BSD）。</summary>
    public static Watcher Kqueue() => _kqueueWatcher;

    /// <summary>Go: <c>func Windows() Watcher</c>（ReadDirectoryChangesW）。</summary>
    public static Watcher Windows() => _windowsWatcher;

    /// <summary>Go: <c>func Fanotify() Watcher</c>（Linux 内核 ≥ 5.13；不支持 fanotify 的文件系统自动退回 inotify）。</summary>
    public static Watcher Fanotify() => _fanotifyFallbackWatcher;

    /// <summary>Go: <c>func Default() Watcher</c>。当前 OS 推荐的后端。</summary>
    public static Watcher Default()
    {
        if (OperatingSystem.IsLinux())
        {
            return Fanotify().Available() ? Fanotify() : Inotify();
        }

        if (OperatingSystem.IsAndroid())
        {
            return Inotify();
        }

        if (OperatingSystem.IsMacOS())
        {
            return FSEvents().Available() ? FSEvents() : Kqueue();
        }

        if (OperatingSystem.IsWindows())
        {
            return Windows();
        }

        if (OperatingSystem.IsFreeBSD())
        {
            return Kqueue();
        }

        return new FsWatcher("unsupported");
    }

    /// <summary>Go: <c>func WithIgnore(fn func(path string) bool) WatchOption</c>。</summary>
    public static WatchOption WithIgnore(Func<string, bool> fn) => new IgnoreOption(fn);

    /// <summary>Go: <c>func WithRecursive() WatchOption</c>。</summary>
    public static WatchOption WithRecursive() => new RecursiveOption();

    /// <summary>Go: <c>func validateWatchDirectory(dir string) error</c>（= os.Stat + IsDir）。</summary>
    internal static void ValidateWatchDirectory(string dir)
    {
        if (!Directory.Exists(dir))
        {
            if (File.Exists(dir))
            {
                throw new IOException($"{dir}: not a directory");
            }

            throw new DirectoryNotFoundException($"could not find a part of the path '{dir}'.");
        }
    }

    /// <summary>
    /// Go: <c>func physicalDirFor(dir string) string</c>。dir 或它的某个祖先是符号链接/重解析点时，事件订阅挂在
    /// 真实路径上，而回调仍用 dir。
    /// </summary>
    internal static string PhysicalDirFor(string dir)
    {
        var realpath = TypeScript.NativePath.NativePath.Realpath(dir);
        if (realpath is null || realpath == dir)
        {
            return dir;
        }

        return CanonicalizePath(CleanPath(realpath));
    }

    /// <summary>
    /// Go: <c>func rebasePath(path string, from string, to string) string</c>。前缀匹配必须落在路径分隔符边界上，
    /// 这样 "/foo2" 不会被 "/foo" 重定基。
    /// </summary>
    internal static string RebasePath(string path, string from, string to)
    {
        if (from == to)
        {
            return path;
        }

        if (path == from)
        {
            return to;
        }

        if (!path.StartsWith(from, StringComparison.Ordinal))
        {
            return path;
        }

        var suffix = path[from.Length..];
        if (from.Length > 0 && IsPathSeparator(from[^1]))
        {
            return JoinPathSuffix(to, suffix);
        }

        if (suffix.Length == 0 || !IsPathSeparator(suffix[0]))
        {
            return path;
        }

        return JoinPathSuffix(to, suffix);
    }

    /// <summary>Go: <c>func joinPathSuffix(root string, suffix string) string</c>。</summary>
    internal static string JoinPathSuffix(string root, string suffix)
    {
        if (suffix.Length == 0)
        {
            return root;
        }

        if (IsPathSeparator(suffix[0]))
        {
            if (root.Length > 0 && IsPathSeparator(root[^1]))
            {
                return root + suffix[1..];
            }

            return root + suffix;
        }

        if (root.Length > 0 && IsPathSeparator(root[^1]))
        {
            return root + suffix;
        }

        return root + Path.DirectorySeparatorChar + suffix;
    }

    /// <summary>Go: <c>func isInDirectoryOrSelf(dir, path string) bool</c>。</summary>
    internal static bool IsInDirectoryOrSelf(string dir, string path)
    {
        if (dir.Length == 0)
        {
            return false;
        }

        if (path == dir)
        {
            return true;
        }

        if (!path.StartsWith(dir, StringComparison.Ordinal))
        {
            return false;
        }

        var rest = path[dir.Length..];
        if (rest.Length == 0)
        {
            return false;
        }

        if (IsPathSeparator(dir[^1]))
        {
            return true;
        }

        return IsPathSeparator(rest[0]);
    }

    /// <summary>Go: <c>func isDirectChild(dir, path string) bool</c>。两条路径都必须绝对；path == dir 返回 false。</summary>
    internal static bool IsDirectChild(string dir, string path)
    {
        if (!path.StartsWith(dir, StringComparison.Ordinal))
        {
            return false;
        }

        var rest = path[dir.Length..];
        if (rest.Length == 0)
        {
            return false;
        }

        if (rest[0] != '/' && rest[0] != Path.DirectorySeparatorChar)
        {
            return false;
        }

        rest = rest[1..];
        return rest.Length > 0 && !rest.Contains('/') && !rest.Contains(Path.DirectorySeparatorChar);
    }

    /// <summary>Go: <c>os.IsPathSeparator(c)</c>（Windows 上 '/' 与 '\' 都是分隔符）。</summary>
    internal static bool IsPathSeparator(char c) =>
        c == '/' || (Path.DirectorySeparatorChar != '/' && c == Path.DirectorySeparatorChar);

    /// <summary>Go: <c>filepath.IsAbs(path)</c>（见文件头第 5 条）。</summary>
    internal static bool IsAbsPath(string path)
    {
        if (path.Length == 0)
        {
            return false;
        }

        if (Path.DirectorySeparatorChar == '/')
        {
            return path[0] == '/';
        }

        if (path[0] == '/' || path[0] == '\\')
        {
            return true;
        }

        return path.Length >= 3 && IsVolumeCharacter(path[0]) && path[1] == ':' && IsPathSeparator(path[2]);
    }

    /// <summary>Go: <c>filepath.Dir(path)</c>（见文件头第 5 条；对应 Go 1.20+ 的实现）。</summary>
    internal static string DirPath(string path)
    {
        var volume = path[..VolumeNameLength(path)];
        var i = path.Length - 1;
        while (i >= volume.Length && !IsPathSeparator(path[i]))
        {
            i--;
        }

        var dir = CleanPath(path[volume.Length..(i + 1)]);
        if (dir == "." && volume.Length > 2)
        {
            // 一定是 UNC。
            return volume;
        }

        return volume + dir;
    }

    /// <summary>Go: <c>filepath.Clean(path)</c>（词法清理；见文件头第 5 条）。逐行照 Go 的 lazybuf 模型：
    /// 缓冲区只前进不擦除，`..` 回退时读的是"已写出位置之后"的旧字符——这一点必须保留，否则会少退一级。</summary>
    internal static string CleanPath(string path)
    {
        var originalPath = path;
        var volLen = VolumeNameLength(path);
        path = path[volLen..];
        if (path.Length == 0)
        {
            if (volLen > 1 && originalPath.Length > 1
                && IsPathSeparator(originalPath[0]) && IsPathSeparator(originalPath[1]))
            {
                return originalPath;
            }

            return originalPath + ".";
        }

        var rooted = IsPathSeparator(path[0]);
        var n = path.Length;

        var buffer = new List<char>(n + 1);
        var w = 0;

        void Append(char c)
        {
            if (w == buffer.Count)
            {
                buffer.Add(c);
            }
            else
            {
                buffer[w] = c;
            }

            w++;
        }

        var r = 0;
        var dotdot = 0;
        if (rooted)
        {
            Append(Path.DirectorySeparatorChar);
            r = 1;
            dotdot = 1;
        }

        while (r < n)
        {
            if (IsPathSeparator(path[r]))
            {
                r++;
            }
            else if (path[r] == '.' && (r + 1 == n || IsPathSeparator(path[r + 1])))
            {
                r++;
            }
            else if (path[r] == '.' && r + 1 < n && path[r + 1] == '.'
                && (r + 2 == n || IsPathSeparator(path[r + 2])))
            {
                r += 2;
                if (w > dotdot)
                {
                    w--;
                    while (w > dotdot && !IsPathSeparator(buffer[w]))
                    {
                        w--;
                    }
                }
                else if (!rooted)
                {
                    if (w > 0)
                    {
                        Append(Path.DirectorySeparatorChar);
                    }

                    Append('.');
                    Append('.');
                    dotdot = w;
                }
            }
            else
            {
                if ((rooted && w != 1) || (!rooted && w != 0))
                {
                    Append(Path.DirectorySeparatorChar);
                }

                for (; r < n && !IsPathSeparator(path[r]); r++)
                {
                    Append(path[r]);
                }
            }
        }

        // 空路径变成 "."。
        if (w == 0)
        {
            Append('.');
        }

        // Go 的 lazybuf 以 `path`（**去掉卷名之后**的剩余部分）为底，最后再拼回卷名。
        var cleaned = new string(buffer.ToArray(), 0, w);
        if (volLen == 0)
        {
            return cleaned;
        }

        var volume = originalPath[..volLen];
        if (Path.DirectorySeparatorChar != '/')
        {
            volume = volume.Replace('/', Path.DirectorySeparatorChar);
        }

        return volume + cleaned;
    }

    private static bool IsVolumeCharacter(char ch) => ch is >= 'a' and <= 'z' or >= 'A' and <= 'Z';

    /// <summary>
    /// Go: <c>filepath.volumeNameLen</c> 的可用子集（Windows 的 <c>C:</c> / <c>\\host\share</c>；其它平台为 0）。
    /// </summary>
    private static int VolumeNameLength(string path)
    {
        if (Path.DirectorySeparatorChar == '/')
        {
            return 0;
        }

        if (path.Length < 2)
        {
            return 0;
        }

        if (IsVolumeCharacter(path[0]) && path[1] == ':')
        {
            return 2;
        }

        if (IsPathSeparator(path[0]) && IsPathSeparator(path[1]))
        {
            // UNC：\\host\share
            var i = 2;
            while (i < path.Length && !IsPathSeparator(path[i]))
            {
                i++;
            }

            if (i >= path.Length)
            {
                return path.Length;
            }

            i++;
            var j = i;
            while (j < path.Length && !IsPathSeparator(path[j]))
            {
                j++;
            }

            return j;
        }

        return 0;
    }
}

/// <summary>
/// Go: <c>type fallbackWatcher struct</c>。主后端处理支持的文件系统，遇到
/// <see cref="FsWatchErrorKind.FilesystemUnsupported"/> 时把单个 watch 路由到副后端。
/// </summary>
internal sealed class FallbackWatcher(Watcher primary, Watcher secondary) : Watcher
{
    internal Watcher Primary { get; } = primary;

    internal Watcher Secondary { get; } = secondary;

    public string Name() => Primary.Name();

    public override string ToString() => Primary.Name();

    public bool Available() => Primary.Available();

    public bool HasFastRecursiveBackend() => Primary.HasFastRecursiveBackend();

    public Watch WatchDirectory(string dir, WatchCallback fn, params WatchOption[] opts)
    {
        var watches = WatchDirectories([new WatchDirectoryRequest { Dir = dir, Callback = fn, Options = opts }]);
        return watches[0];
    }

    public Watch[] WatchDirectories(WatchDirectoryRequest[] requests)
    {
        Watch[] watches;
        try
        {
            watches = Primary.WatchDirectories(requests);
        }
        catch (Exception ex) when (FsWatchException.Is(ex, FsWatchErrorKind.FilesystemUnsupported))
        {
            return WatchDirectoriesRouted(requests);
        }

        return watches;
    }

    private Watch[] WatchDirectoriesRouted(WatchDirectoryRequest[] requests)
    {
        var watches = new List<Watch>(requests.Length);
        void Rollback()
        {
            for (var i = watches.Count - 1; i >= 0; i--)
            {
                watches[i].Close();
            }
        }

        foreach (var request in requests)
        {
            Watch watch;
            try
            {
                watch = Primary.WatchDirectory(request.Dir, request.Callback!, request.Options ?? []);
            }
            catch (Exception ex) when (FsWatchException.Is(ex, FsWatchErrorKind.FilesystemUnsupported))
            {
                try
                {
                    watch = Secondary.WatchDirectory(request.Dir, request.Callback!, request.Options ?? []);
                }
                catch (Exception secondaryEx)
                {
                    Rollback();
                    throw new IOException($"fswatch: failed to watch directory \"{request.Dir}\": {secondaryEx.Message}", secondaryEx);
                }
            }
            catch (Exception primaryEx)
            {
                Rollback();
                throw new IOException($"fswatch: failed to watch directory \"{request.Dir}\": {primaryEx.Message}", primaryEx);
            }

            watches.Add(watch);
        }

        return watches.ToArray();
    }

    public Watch WatchFile(string path, WatchCallback fn)
    {
        try
        {
            return Primary.WatchFile(path, fn);
        }
        catch (Exception ex) when (FsWatchException.Is(ex, FsWatchErrorKind.FilesystemUnsupported))
        {
            return Secondary.WatchFile(path, fn);
        }
    }
}

/// <summary>Go: <c>type watch struct</c>。一次订阅的句柄（<see cref="Watch"/> 的实现）。</summary>
internal sealed class WatchHandle(FsWatcher watcher, DirWatch dirWatch, IWatcherImpl impl, ulong id) : Watch
{
    private readonly object _mu = new();
    private bool _cancelled;

    public void Close()
    {
        lock (_mu)
        {
            if (_cancelled)
            {
                return;
            }

            _cancelled = true;
            var last = dirWatch.Unwatch(id);
            if (last)
            {
                impl.WatchRemove(dirWatch);
                dirWatch.Unref(watcher);
            }
        }
    }
}

/// <summary>
/// Go: <c>type watcher struct</c>。<see cref="Watcher"/> 的具体实现；每个平台 watcher 都是包级实例，其
/// factory 由该平台的 <c>init()</c> 设置。
/// </summary>
internal sealed partial class FsWatcher : Watcher
{
    private readonly string _name;
    private readonly object _mu = new();
    private IWatcherImpl? _impl;
    private Func<IWatcherImpl>? _factory;
    private Dictionary<string, DirWatch>? _dirWatches;
    private Debounce? _debounce;
    private Func<ulong>? _sequence;

    internal FsWatcher(string name)
    {
        _name = name;
    }

    internal Func<IWatcherImpl>? Factory
    {
        get => _factory;
        set => _factory = value;
    }

    internal Func<ulong>? Sequence
    {
        get => _sequence;
        set => _sequence = value;
    }

    public string Name() => _name;

    public override string ToString() => _name;

    public bool Available() => _factory is not null;

    public bool HasFastRecursiveBackend() => _name is "windows" or "fsevents";

    /// <summary>Go: <c>func (w *watcher) canShareRecursiveDirWatches() bool</c>。</summary>
    private bool CanShareRecursiveDirWatches()
    {
        // TODO: 等递归 watch 合并有更多真实使用经验后，再对 Windows 打开。
        return _name == "fsevents";
    }

    /// <summary>Go: <c>func (w *watcher) getImpl() (watcherImpl, error)</c>。</summary>
    internal IWatcherImpl GetImpl()
    {
        IWatcherImpl? impl;
        Func<IWatcherImpl>? factory;
        lock (_mu)
        {
            if (_impl is not null)
            {
                return _impl;
            }

            impl = null;
            factory = _factory;
        }

        if (factory is null)
        {
            throw FsWatchException.Unavailable();
        }

        impl = factory();
        impl.Run();

        lock (_mu)
        {
            if (_impl is not null)
            {
                var existing = _impl;
                impl.Shutdown();
                return existing;
            }

            _impl = impl;
            return impl;
        }
    }

    private static string KeyForDirWatch(string dir, bool recursive) => recursive ? dir + "\0recursive" : dir;

    private DirWatch? FindCoveringRecursiveWatchLocked(string dir, string physicalDir, VolumePathComparer comparer)
    {
        DirWatch? best = null;
        foreach (var dw in _dirWatches!.Values)
        {
            if (!dw._recursive || !dw._comparer.Equals(comparer)
                || !FsWatch.IsInDirectoryOrSelf(dw._dir, dir)
                || !FsWatch.IsInDirectoryOrSelf(dw._physicalDir, physicalDir))
            {
                continue;
            }

            if (best is null || dw._dir.Length > best._dir.Length)
            {
                best = dw;
            }
        }

        return best;
    }

    private string FindConsolidationDirLocked(string dir, string physicalDir)
    {
        if (!CanShareRecursiveDirWatches())
        {
            return "";
        }

        var parent = FsWatch.DirPath(dir);
        while (parent != dir && parent != ".")
        {
            if (FsWatch.DirPath(parent) == parent)
            {
                break;
            }

            var physicalParent = FsWatch.PhysicalDirFor(parent);
            if (!FsWatch.IsInDirectoryOrSelf(physicalParent, physicalDir))
            {
                return "";
            }

            var count = 1;
            foreach (var dw in _dirWatches!.Values)
            {
                if (FsWatch.IsInDirectoryOrSelf(parent, dw._dir) && FsWatch.IsInDirectoryOrSelf(physicalParent, dw._physicalDir))
                {
                    count++;
                    if (count >= _recursiveConsolidateThreshold)
                    {
                        return parent;
                    }
                }
            }

            var next = FsWatch.DirPath(parent);
            if (next == parent)
            {
                break;
            }

            dir = parent;
            parent = next;
        }

        return "";
    }

    private const int _recursiveConsolidateThreshold = 10;

    private DirWatch GetOrCreateDirWatch(string dir, string physicalDir, bool recursive, VolumePathComparer comparer)
    {
        lock (_mu)
        {
            _dirWatches ??= new Dictionary<string, DirWatch>();
            _debounce ??= new Debounce();

            if (CanShareRecursiveDirWatches())
            {
                var covering = FindCoveringRecursiveWatchLocked(dir, physicalDir, comparer);
                if (covering is not null)
                {
                    return covering;
                }

                var consolidationDir = FindConsolidationDirLocked(dir, physicalDir);
                if (consolidationDir.Length != 0)
                {
                    var parentComparer = PathComparerFor(consolidationDir);
                    if (parentComparer.Equals(comparer))
                    {
                        dir = consolidationDir;
                        physicalDir = FsWatch.PhysicalDirFor(dir);
                        recursive = true;
                        covering = FindCoveringRecursiveWatchLocked(dir, physicalDir, comparer);
                        if (covering is not null)
                        {
                            return covering;
                        }
                    }
                }
            }

            var key = KeyForDirWatch(dir, recursive);
            if (_dirWatches.TryGetValue(key, out var existing))
            {
                return existing;
            }

            var dw = new DirWatch(dir, physicalDir, _debounce);
            dw.SetComparer(comparer);
            dw._sequence = _sequence;
            dw._recursive = recursive;
            _dirWatches[key] = dw;
            return dw;
        }
    }

    internal void RemoveDirWatch(DirWatch dw)
    {
        lock (_mu)
        {
            var key = KeyForDirWatch(dw._dir, dw._recursive);
            if (_dirWatches is not null && _dirWatches.TryGetValue(key, out var existing) && ReferenceEquals(existing, dw))
            {
                _dirWatches.Remove(key);
                dw.DestroyDebounce();
            }
        }
    }

    public Watch WatchDirectory(string dir, WatchCallback fn, params WatchOption[] opts)
    {
        var watches = WatchDirectories([new WatchDirectoryRequest { Dir = dir, Callback = fn, Options = opts }]);
        return watches[0];
    }

    public Watch[] WatchDirectories(WatchDirectoryRequest[] requests)
    {
        if (!Available())
        {
            throw FsWatchException.Unavailable();
        }

        if (requests.Length == 0)
        {
            return [];
        }

        var prepared = new List<PreparedWatch>(requests.Length);
        var uniqueDirWatches = new List<DirWatch>(requests.Length);
        var seenDirWatches = new HashSet<DirWatch>();
        void Rollback()
        {
            for (var i = prepared.Count - 1; i >= 0; i--)
            {
                prepared[i].Dw.Unwatch(prepared[i].Id);
                prepared[i].Dw.Unref(this);
            }
        }

        foreach (var request in requests)
        {
            var dir = request.Dir;
            var fn = request.Callback;
            if (fn is null)
            {
                Rollback();
                throw FsWatchException.NilCallback();
            }

            dir = FsWatch.CleanPath(dir);
            if (!FsWatch.IsAbsPath(dir))
            {
                Rollback();
                throw FsWatchException.NotAbsolute();
            }

            dir = FsWatch.CanonicalizePath(dir);
            if (CanShareRecursiveDirWatches())
            {
                FsWatch.ValidateWatchDirectory(dir);
            }

            var physicalDir = FsWatch.PhysicalDirFor(dir);

            var sopts = new WatchOptions();
            foreach (var o in request.Options ?? [])
            {
                o.ApplyWatchOption(sopts);
            }

            var comparer = PathComparerFor(dir);
            var dw = GetOrCreateDirWatch(dir, physicalDir, sopts.Recursive, comparer);
            var id = dw.AddCallback(dir, physicalDir, sopts.Recursive, fn, sopts.Ignore, sopts.File);
            prepared.Add(new PreparedWatch(dw, id));
            if (seenDirWatches.Add(dw))
            {
                uniqueDirWatches.Add(dw);
            }
        }

        var impl = GetImpl();
        impl.WatchAddMany(uniqueDirWatches.ToArray());

        var watches = new Watch[prepared.Count];
        for (var i = 0; i < prepared.Count; i++)
        {
            watches[i] = new WatchHandle(this, prepared[i].Dw, impl, prepared[i].Id);
        }

        return watches;
    }

    public Watch WatchFile(string path, WatchCallback fn)
    {
        if (fn is null)
        {
            throw FsWatchException.NilCallback();
        }

        if (!Available())
        {
            throw FsWatchException.Unavailable();
        }

        path = FsWatch.CleanPath(path);
        if (!FsWatch.IsAbsPath(path))
        {
            throw FsWatchException.NotAbsolute();
        }

        path = FsWatch.CanonicalizePath(path);
        var dir = FsWatch.DirPath(path);
        if (dir == path)
        {
            throw FsWatchException.RootPath();
        }

        return WatchDirectory(dir, fn, new FileOption(path));
    }

    private readonly record struct PreparedWatch(DirWatch Dw, ulong Id);
}
