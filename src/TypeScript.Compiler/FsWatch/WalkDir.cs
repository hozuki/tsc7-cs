// ported from: internal/fswatch/walkdir.go, walkdir_unix.go, walkdir_windows.go, walkdir_other.go,
//              walkdir_dirent_linux.go, walkdir_dirent_darwin.go, walkdir_dirent_fileno.go, walkdir_dirent_noreclen.go
//
// C# 映射与偏离（逐条）：
// 1) Go 用构建标签选 `walkDir` 的实现（linux/darwin/BSDs = walkdir_unix.go、windows = walkdir_windows.go、
//    其它 = walkdir_other.go 转发到 walkDirGeneric）→ C# 用 `OperatingSystem.Is*()` 在 `WalkDir` 里分发
//    （.NET 没有 build tag）。
// 2) **Unix 原生实现用 libc 的 `opendir`/`readdir`/`closedir`，不等价于 Go 的原始系统调用**：Go 用
//    getdents(2)/getdirentries(2) 直接读内核结构体（`unix.Dirent`，字段 Reclen/Ino/Type）。C# 侧没有对应的
//    托管 API，改为 libc `readdir(3)`（它内部就是同一个系统调用），`dirent` 的字段偏移按平台硬编码：
//      - Linux (glibc, _FILE_OFFSET_BITS=64)：d_ino @0(i64)、d_off @8(i64)、d_reclen @16(u16)、
//        d_type @18(u8)、d_name @19；
//      - Darwin：d_ino @0(i64)、d_seekoff @8(i64)、d_reclen @16(u16)、d_namlen @18(u16)、d_type @20(u8)、
//        d_name @21。
//    两者都先按 `d_type <= DT_WHT(14)` 做合法性校验，任何越界值（说明偏移猜错了）与 DT_UNKNOWN 一律落回
//    `File.GetAttributes`，宁慢不错。
// 3) **未移植**：FreeBSD/OpenBSD/NetBSD/DragonFly 的原生实现（Go 里同一份 walkdir_unix.go + dir entry 的
//    reclenOf/inoOf 差异，即 walkdir_dirent_fileno.go / walkdir_dirent_noreclen.go）。这些平台的 dirent 布局
//    无法在本机（win32）验证，本移植让它们走 `WalkDirGeneric`（语义相同，只是每个条目多一次属性查询）。
//    DragonFly 的 `alignUp`/Namlen 推算因此也不需要了。
// 4) **Windows 原生实现用 .NET 的 `Directory.EnumerateFileSystemEntries`**（它内部就是 FindFirstFile/FindNextFile），
//    而不是 P/Invoke FindFirstFile：语义逐条保留——根目录先用属性校验（缺失/非目录/重解析点 → 报错），
//    逐条目按属性判定 isDir（`Directory` 且**非** `ReparsePoint`，对应 Go 的 FILE_ATTRIBUTE_REPARSE_POINT），
//    子目录失败时跳过（根目录失败则上抛），LIFO 栈做深度优先。
// 5) Go 的回调 `fn func(path string, isDir bool) error` 返回错误即中止（没有 fs.SkipDir 那套控制流）→ C# 用
//    `WalkDirFunc` 委托 + **异常**表达中止（§3.2）；`errors.Is(err, sentinel)` → `catch` 同一异常实例。
// 6) Go 的裸 errno 错误值（`syscall.ENOTDIR`）→ C# 的 `IOException`：ENOTDIR 的消息固定为
//    `"<path>: not a directory"`，其余用 `strerror` 文本。
namespace TypeScript.FsWatch;

using System.Runtime.InteropServices;

/// <summary>Go: <c>fn func(path string, isDir bool) error</c>（返回错误即中止 → C# 抛异常）。</summary>
internal delegate void WalkDirFunc(string path, bool isDir);

public static partial class FsWatch
{
    /// <summary>Go: <c>func walkDir(dir string, recursive bool, fn func(path string, isDir bool) error) error</c>。</summary>
    internal static void WalkDir(string dir, bool recursive, WalkDirFunc? fn)
    {
        if (OperatingSystem.IsWindows())
        {
            WalkDirWindows(dir, recursive, fn);
            return;
        }

        if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
        {
            WalkDirUnix(dir, recursive, fn);
            return;
        }

        WalkDirGeneric(dir, recursive, fn);
    }

    /// <summary>
    /// Go: <c>func walkDirGeneric(dir string, recursive bool, fn func(path string, isDir bool) error) error</c>。
    /// 可移植实现；在没有原生实现的平台上作为主实现，在所有平台上都被测试。
    /// </summary>
    internal static void WalkDirGeneric(string dir, bool recursive, WalkDirFunc? fn)
    {
        if (!IsDirectoryNoFollow(dir))
        {
            throw new IOException($"{dir}: not a directory");
        }

        WalkDirGenericVisit(dir, recursive, fn);
    }

    private static void WalkDirGenericVisit(string dir, bool recursive, WalkDirFunc? fn)
    {
        string[] entries;
        try
        {
            // Go 的 os.ReadDir 按文件名排序（逐字节序）。
            entries = Directory.GetFileSystemEntries(dir);
            Array.Sort(entries, static (a, b) => string.CompareOrdinal(Path.GetFileName(a), Path.GetFileName(b)));
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or DirectoryNotFoundException)
        {
            // Go: fs.ErrPermission / fs.ErrNotExist → 静默跳过（返回 nil）。
            return;
        }

        fn?.Invoke(dir, true);

        foreach (var path in entries)
        {
            if (IsDirectoryNoFollow(path))
            {
                if (recursive)
                {
                    WalkDirGenericVisit(path, recursive, fn);
                }
                else
                {
                    fn?.Invoke(path, true);
                }
            }
            else
            {
                fn?.Invoke(path, false);
            }
        }
    }

    /// <summary>
    /// Go: <c>func walkDir(dir string, recursive bool, fn …) error</c>（walkdir_windows.go）。
    /// </summary>
    private static void WalkDirWindows(string dir, bool recursive, WalkDirFunc? fn)
    {
        FileAttributes rootAttributes;
        try
        {
            rootAttributes = File.GetAttributes(dir);
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            throw new IOException($"error opening directory: {ex.Message}", ex);
        }

        if (!rootAttributes.HasFlag(FileAttributes.Directory) || rootAttributes.HasFlag(FileAttributes.ReparsePoint))
        {
            throw new IOException($"{dir}: not a directory");
        }

        fn?.Invoke(dir, true);

        var stack = new Stack<string>();
        stack.Push(dir);
        while (stack.Count != 0)
        {
            var path = stack.Pop();
            string[] entries;
            try
            {
                entries = Directory.GetFileSystemEntries(path);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                if (path == dir)
                {
                    throw new IOException($"error opening directory: {ex.Message}", ex);
                }

                continue;
            }

            foreach (var fullPath in entries)
            {
                var name = Path.GetFileName(fullPath);
                if (name is "." or "..")
                {
                    continue;
                }

                var isDir = IsDirectoryNoFollow(fullPath);
                if (fn is not null)
                {
                    fn(fullPath, isDir);
                }

                if (isDir && recursive)
                {
                    stack.Push(fullPath);
                }
            }
        }
    }

    /// <summary>
    /// Go: <c>func walkDir(dir string, recursive bool, fn …) error</c>（walkdir_unix.go，Linux/Darwin 部分；
    /// 见文件头第 2、3 条的偏离说明）。
    /// </summary>
    private static void WalkDirUnix(string dir, bool recursive, WalkDirFunc? fn)
    {
        var handle = OpenDir(dir);
        if (handle == IntPtr.Zero)
        {
            ThrowErrno(dir);
        }

        try
        {
            IterateDir(handle, dir, recursive, fn);
        }
        finally
        {
            _ = Closedir(handle);
        }
    }

    private static void IterateDir(IntPtr handle, string dirname, bool recursive, WalkDirFunc? fn)
    {
        fn?.Invoke(dirname, true);

        while (true)
        {
            var entry = ReadDir(handle);
            if (entry == IntPtr.Zero)
            {
                if (Marshal.GetLastPInvokeError() != 0)
                {
                    ThrowErrno(dirname);
                }

                return;
            }

            var name = DirentName(entry);
            if (name.Length == 0 || name is "." or "..")
            {
                continue;
            }

            var fullPath = dirname + Path.DirectorySeparatorChar + name;
            var isDir = DirentIsDir(entry, fullPath);

            if (!isDir)
            {
                fn?.Invoke(fullPath, false);
                continue;
            }

            if (!recursive)
            {
                fn?.Invoke(fullPath, true);
                continue;
            }

            var child = OpenDir(fullPath);
            if (child == IntPtr.Zero)
            {
                // Go: EACCES / ENOTDIR / ENOENT → 跳过该子目录；其余错误上抛。
                var errno = Marshal.GetLastPInvokeError();
                if (errno is _eacces or _enotdir or _enoent)
                {
                    continue;
                }

                ThrowErrno(fullPath);
            }

            try
            {
                IterateDir(child, fullPath, recursive, fn);
            }
            finally
            {
                _ = Closedir(child);
            }
        }
    }

    /// <summary>Go: <c>walkdir_unix.go</c> 里按 <c>d_type</c> 判目录、DT_UNKNOWN 时回退 lstat。</summary>
    private static bool DirentIsDir(IntPtr entry, string fullPath)
    {
        var type = Marshal.ReadByte(entry, _direntTypeOffset);
        if (type <= _dtWht && type != _dtUnknown)
        {
            return type == _dtDir;
        }

        // DT_UNKNOWN（以及偏移猜错时的越界值）→ 与 Go 的 lstat 回退同义。
        return IsDirectoryNoFollow(fullPath);
    }

    /// <summary>Go: <c>walkdir_unix.go</c> 里从 <c>dirent</c> 记录里取 NUL 结尾的名字。</summary>
    private static string DirentName(IntPtr entry)
    {
        var name = Marshal.PtrToStringUTF8(entry + _direntNameOffset);
        return name ?? "";
    }

    /// <summary>
    /// Go: <c>os.Lstat(path).IsDir()</c> 的等价物（**不跟随**符号链接/重解析点）。Go 在 windows 与 unix 上都用
    /// "重解析点/符号链接不算目录" 的判定，见 walkdir_windows.go 与 d_type == DT_LNK。
    /// </summary>
    internal static bool IsDirectoryNoFollow(string path)
    {
        FileAttributes attrs;
        try
        {
            attrs = File.GetAttributes(path);
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException or UnauthorizedAccessException)
        {
            return false;
        }

        return attrs.HasFlag(FileAttributes.Directory) && !attrs.HasFlag(FileAttributes.ReparsePoint);
    }

    /// <summary>Go 的裸 errno → C# 异常（见文件头第 6 条）。</summary>
    private static void ThrowErrno(string path)
    {
        var errno = Marshal.GetLastPInvokeError();
        throw errno == _enotdir
            ? new IOException($"{path}: not a directory")
            : new IOException($"{path}: {ErrnoText(errno)}");
    }

    private static string ErrnoText(int errno) => new System.ComponentModel.Win32Exception(errno).Message;

    // Linux (glibc) / Darwin 的 struct dirent 字段偏移；两者都在此硬编码（见文件头第 2 条）。
    private static readonly int _direntNameOffset = OperatingSystem.IsLinux() ? 19 : 21;
    private static readonly int _direntTypeOffset = OperatingSystem.IsLinux() ? 18 : 20;

    private const int _eacces = 13;
    private const int _enoent = 2;
    private const int _enotdir = 20; // Linux 的 ENOTDIR；Darwin 也是 20。
    private const int _dtUnknown = 0;
    private const int _dtDir = 4;
    private const int _dtWht = 14;

    [DllImport("libc", EntryPoint = "opendir", SetLastError = true)]
    private static extern IntPtr OpenDirNative([MarshalAs(UnmanagedType.LPUTF8Str)] string name);

    [DllImport("libc", EntryPoint = "readdir", SetLastError = true)]
    private static extern IntPtr ReadDirNative(IntPtr dirp);

    [DllImport("libc", EntryPoint = "closedir")]
    private static extern int Closedir(IntPtr dirp);

    private static IntPtr OpenDir(string name)
    {
        Marshal.SetLastPInvokeError(0);
        return OpenDirNative(name);
    }

    private static IntPtr ReadDir(IntPtr dirp)
    {
        Marshal.SetLastPInvokeError(0);
        return ReadDirNative(dirp);
    }
}
