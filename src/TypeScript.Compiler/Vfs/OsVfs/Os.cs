// ported from: internal/vfs/osvfs/os.go
//
// 偏离（逐条）：
//   - Go 的 <c>nativepath</c>/<c>osutil</c> 是独立包：C# 侧落在 TypeScript.NativePath / TypeScript.OsUtil
//     （同样一个 Go package = 一个同名 C# 包），本文件只用到 Realpath/Executable。
//   - Go 的 <c>os.DirFS(root)</c> + <c>fs.Stat/ReadDir/ReadFile</c> → <see cref="OsRootFs"/>（实现 IIoFs，
//     直接用 System.IO）。
//   - Go 的 <c>filepath.FromSlash(path)</c> 在 C# 不需要：Win32 的 CreateFileW 与 System.IO 都接受 '/'。
//   - Go 的 <c>os.UserCacheDir</c> → <see cref="UserCacheDir"/>（Windows: %LocalAppData%；macOS: ~/Library/Caches；
//     其它: $XDG_CACHE_HOME 或 ~/.cache。.NET 的 SpecialFolder.LocalApplicationData 在 Unix 上是 ~/.local/share，
//     不是 cache 目录，所以不能直接用）。结果统一 NormalizeSlashes（Go 保留 OS 分隔符）。
//   - Go 的 <c>time.Time</c> → <see cref="DateTime"/>；<see cref="FS.Chtimes"/> 传入 Utc 时走 Utc 变体。
//   - Go 的 error 返回值 → 异常（写盘失败抛 IOException 家族；FindAll/读失败是 false/null）。
//   - Go 的 <c>isReparsePoint</c>（传给 internal.Common 的 OS 兜底）不需要：IIoFs.ReadDir 直接用
//     FileSystemInfo.Attributes 的 ReparsePoint 位算出 DirEntry.IsSymlink。
namespace TypeScript.Vfs.OsVfs;

using System.Text;
using TypeScript.Core;
using TypeScript.NativePath;
using TypeScript.OsUtil;
using TypeScript.TSPath;
using TypeScript.Vfs;
using TypeScript.Vfs.Internal;

using static TypeScript.Core.Core;
using static TypeScript.TSPath.TSPath;

public static partial class Osvfs
{
    private static readonly UTF8Encoding _utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    // Go: 对"实际上就是系统调用"的操作加的信号量。
    private static readonly LimitedSemaphore _blockingOpSema = new(128);
    private static readonly LimitedSemaphore _readSema = new(128);
    private static readonly LimitedSemaphore _writeSema = new(32);

    private static readonly FS _osVfs = new OsFS(new Common(OsRootFor));

    /// <summary>Go: <c>FS() vfs.FS</c>：进程级单例（Go 的包级 var osVFS）。</summary>
    public static FS FS()
    {
        return _osVfs;
    }

    private static IIoFs OsRootFor(string root)
    {
        return new OsRootFs(root);
    }

    /// <summary>Go: <c>isFileSystemCaseSensitive</c>（进程启动时求值一次）。</summary>
    private static readonly bool _isFileSystemCaseSensitive = ComputeIsFileSystemCaseSensitive();

    private static bool ComputeIsFileSystemCaseSensitive()
    {
        // win32/win64 大小写不敏感
        if (OperatingSystem.IsWindows())
        {
            return false;
        }

        if (OperatingSystem.IsBrowser() || OperatingSystem.IsWasi())
        {
            // Go: runtime.GOARCH == "wasm" → true（取决于宿主实现）
            return true;
        }

        // 间接判断：把可执行文件路径换大小写后若不存在，说明大小写敏感。
        // 这不完全正确（不同 OS/不同路径可以有不同的敏感性），但足够用（TS 以前用 __filename 做同样的事）。
        var exe = OsUtil.Executable();
        if (exe == null)
        {
            Debug.Fail("vfs: failed to get executable path");
        }

        // Debug.Fail 必抛，所以 exe 在这里一定非空。
        var swapped = SwapCase(exe!);
        // Go: os.Stat(swapped) 成功 → false；IsNotExist → true；其它错误 → panic。
        return !File.Exists(swapped);
    }

    /// <summary>Go: <c>swapCase</c>：小写转大写、大写转小写（rune 级 simple 映射）。</summary>
    private static string SwapCase(string str)
    {
        var builder = new StringBuilder(str.Length);
        foreach (var rune in str.EnumerateRunes())
        {
            var upper = Rune.ToUpperInvariant(rune);
            builder.Append(upper.Value == rune.Value ? Rune.ToLowerInvariant(rune) : upper);
        }

        return builder.ToString();
    }

    /// <summary>Go: <c>GetGlobalTypingsCacheLocation()</c>。</summary>
    public static string GetGlobalTypingsCacheLocation()
    {
        var cacheDir = UserCacheDir() ?? System.IO.Path.GetTempPath();
        var subdir = OperatingSystem.IsWindows() ? "Microsoft/TypeScript" : "typescript";
        return NormalizeSlashes(CombinePaths(cacheDir, subdir, VersionMajorMinor()));
    }

    /// <summary>Go: <c>os.UserCacheDir()</c>（失败返回 null → 用临时目录）。</summary>
    private static string? UserCacheDir()
    {
        if (OperatingSystem.IsWindows())
        {
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            return string.IsNullOrEmpty(localAppData) ? null : localAppData;
        }

        if (OperatingSystem.IsMacOS())
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return string.IsNullOrEmpty(home) ? null : System.IO.Path.Combine(home, "Library", "Caches");
        }

        var xdgCacheHome = Environment.GetEnvironmentVariable("XDG_CACHE_HOME");
        if (!string.IsNullOrEmpty(xdgCacheHome))
        {
            return xdgCacheHome;
        }

        var userHome = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return string.IsNullOrEmpty(userHome) ? null : System.IO.Path.Combine(userHome, ".cache");
    }

    /// <summary>Go: <c>osFSRealpath</c>（C# 侧也供测试直接调用）。</summary>
    internal static string OsFSRealpath(string path)
    {
        _ = Internal.RootLength(path); // 断言 path 是绝对路径

        var orig = path;
        var native = TypeScript.NativePath.NativePath.Realpath(path);
        if (native == null)
        {
            return orig;
        }

        try
        {
            native = System.IO.Path.GetFullPath(native);
        }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return orig;
        }

        return NormalizeSlashes(native);
    }

    private static void SetAccessTime(string path, DateTime time)
    {
        if (time.Kind == DateTimeKind.Utc)
        {
            File.SetLastAccessTimeUtc(path, time);
        }
        else
        {
            File.SetLastAccessTime(path, time);
        }
    }

    private static void SetWriteTime(string path, DateTime time)
    {
        if (time.Kind == DateTimeKind.Utc)
        {
            File.SetLastWriteTimeUtc(path, time);
        }
        else
        {
            File.SetLastWriteTime(path, time);
        }
    }

    /// <summary>Go 的 <c>osFS</c>（未导出）。</summary>
    private sealed class OsFS(Common common) : FS
    {
        private readonly Common _common = common;

        public bool UseCaseSensitiveFileNames()
        {
            return _isFileSystemCaseSensitive;
        }

        public bool TryReadFile(string path, out string contents)
        {
            var release = _readSema.Acquire();
            try
            {
                return _common.TryReadFile(path, out contents);
            }
            finally
            {
                release();
            }
        }

        public bool DirectoryExists(string path)
        {
            var release = _blockingOpSema.Acquire();
            try
            {
                return _common.DirectoryExists(path);
            }
            finally
            {
                release();
            }
        }

        public bool FileExists(string path)
        {
            var release = _blockingOpSema.Acquire();
            try
            {
                return _common.FileExists(path);
            }
            finally
            {
                release();
            }
        }

        public Entries GetAccessibleEntries(string path)
        {
            var release = _blockingOpSema.Acquire();
            try
            {
                return _common.GetAccessibleEntries(path);
            }
            finally
            {
                release();
            }
        }

        public FileInfo? Stat(string path)
        {
            var release = _blockingOpSema.Acquire();
            try
            {
                return _common.Stat(path);
            }
            finally
            {
                release();
            }
        }

        public string Realpath(string path)
        {
            var release = _blockingOpSema.Acquire();
            try
            {
                return OsFSRealpath(path);
            }
            finally
            {
                release();
            }
        }

        public void Remove(string path)
        {
            var release = _blockingOpSema.Acquire();
            try
            {
                // todo: #701 add retry mechanism?（照 Go 保留）
                // Go 的 os.RemoveAll 不跟随符号链接/重解析点：链接本身被删掉，目标是死是活都不管。
                if (TypeScript.NativePath.NativePath.IsSymlinkOrReparsePoint(path))
                {
                    if (IsDirectoryEntry(path))
                    {
                        Directory.Delete(path, recursive: false);
                    }
                    else
                    {
                        File.Delete(path);
                    }
                }
                else if (Directory.Exists(path))
                {
                    Directory.Delete(path, recursive: true);
                }
                else if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            finally
            {
                release();
            }
        }

        /// <summary>Go 的 <c>os.Lstat</c> 语义下"目录条目"（不跟随链接；断链也返回其自身属性）。</summary>
        private static bool IsDirectoryEntry(string path)
        {
            try
            {
                return (File.GetAttributes(path) & FileAttributes.Directory) != 0;
            }
            catch (IOException)
            {
                return false;
            }
            catch (UnauthorizedAccessException)
            {
                return false;
            }
        }

        public void Chtimes(string path, DateTime accessTime, DateTime modificationTime)
        {
            var release = _blockingOpSema.Acquire();
            try
            {
                SetAccessTime(path, accessTime);
                SetWriteTime(path, modificationTime);
            }
            finally
            {
                release();
            }
        }

        public void WriteFile(string path, string content)
        {
            WriteFileEnsuringDir(path, content, FileMode.Create);
        }

        public void AppendFile(string path, string content)
        {
            WriteFileEnsuringDir(path, content, FileMode.Append);
        }

        private void WriteFileWithFlag(string path, string content, FileMode mode)
        {
            var release = _writeSema.Acquire();
            try
            {
                using var stream = new FileStream(path, mode, FileAccess.Write, FileShare.Read);
                using var writer = new StreamWriter(stream, _utf8NoBom);
                writer.Write(content);
            }
            finally
            {
                release();
            }
        }

        private void EnsureDirectoryExists(string directoryPath)
        {
            var release = _blockingOpSema.Acquire();
            try
            {
                Directory.CreateDirectory(directoryPath);
            }
            finally
            {
                release();
            }
        }

        private void WriteFileEnsuringDir(string path, string content, FileMode mode)
        {
            _ = Internal.RootLength(path); // 断言 path 是绝对路径
            try
            {
                WriteFileWithFlag(path, content, mode);
                return;
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }

            EnsureDirectoryExists(GetDirectoryPath(NormalizePath(path)));
            WriteFileWithFlag(path, content, mode);
        }
    }

    /// <summary>Go 的 <c>os.DirFS(root)</c>（+ <c>fs.Stat/ReadDir/ReadFile</c>）：把系统文件系统的一个根挂到
    /// <see cref="IIoFs"/> 上。<paramref name="root"/> 形如 <c>C:/</c> 或 <c>/</c>（带尾分隔符）。</summary>
    private sealed class OsRootFs(string root) : IIoFs
    {
        private readonly string _root = root;

        private string Resolve(string name)
        {
            // Go 的 fs 路径：根是 "."，其余是相对根的 '/' 分隔路径（SplitPath 已保证没有前导/尾随分隔符）。
            return name == "." ? _root : _root + name;
        }

        public FileInfo? Stat(string name)
        {
            var info = StatNoThrow(Resolve(name));
            return info == null ? null : FileInfo.FromFileSystemInfo(info);
        }

        public DirEntry[]? ReadDir(string name)
        {
            var full = Resolve(name);
            try
            {
                var infos = new DirectoryInfo(full).GetFileSystemInfos();
                var entries = new DirEntry[infos.Length];
                for (var i = 0; i < infos.Length; i++)
                {
                    var info = infos[i];
                    var isSymlink = IsReparsePoint(info);
                    entries[i] = new DirEntry(
                        info.Name,
                        info is DirectoryInfo && !isSymlink,
                        isSymlink,
                        () => FileInfo.FromFileSystemInfo(info));
                }

                // Go: fs.ReadDir 按名字序数序排序。
                Array.Sort(entries, static (a, b) => string.CompareOrdinal(a.Name, b.Name));
                return entries;
            }
            catch (IOException)
            {
                return null;
            }
            catch (UnauthorizedAccessException)
            {
                return null;
            }
        }

        public bool TryReadFile(string name, out string contents)
        {
            var full = Resolve(name);
            try
            {
                contents = Internal.DecodeBytes(File.ReadAllBytes(full));
                return true;
            }
            catch (IOException)
            {
                contents = "";
                return false;
            }
            catch (UnauthorizedAccessException)
            {
                contents = "";
                return false;
            }
        }

        private static bool IsReparsePoint(System.IO.FileSystemInfo info)
        {
            try
            {
                return (info.Attributes & FileAttributes.ReparsePoint) != 0;
            }
            catch (IOException)
            {
                return false;
            }
            catch (UnauthorizedAccessException)
            {
                return false;
            }
        }

        private static System.IO.FileSystemInfo? StatNoThrow(string full)
        {
            // Go 的 os.Stat 跟随符号链接；Directory.Exists/File.Exists 同样跟随。
            if (Directory.Exists(full))
            {
                return new DirectoryInfo(full);
            }

            return File.Exists(full) ? new System.IO.FileInfo(full) : null;
        }
    }
}
