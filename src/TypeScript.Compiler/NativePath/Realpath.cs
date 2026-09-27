// ported from: internal/nativepath/realpath_windows.go, realpath_linux.go, realpath_other.go
//
// Go 按平台分文件；C# 侧一个文件里按 OperatingSystem 分支（.NET 没有 build tag）。
//   - Windows：CreateFileW(FILE_FLAG_BACKUP_SEMANTICS) + GetFinalPathNameByHandleW（逐行对应 Go 的
//     syscall 实现，含 `\\?\` 剥离与 UNC 还原）。
//   - Linux/macOS：libc 的 realpath(3)。Go 在 Linux 上用 O_PATH + readlink("/proc/self/fd/N")、在其它
//     平台用 filepath.EvalSymlinks，二者的语义与 glibc 的 realpath(3) 相同（Go 的注释明确说 libuv/Node 就是
//     调 realpath(3)）—— 所以这里直接调 libc 的同一个函数。要改成分量解析时只改这一个函数。
// 失败（返回 error）→ C# 返回 null。
namespace TypeScript.NativePath;

using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

public static partial class NativePath
{
    private const int _pathMax = 4096;

    private const uint _fileAnyAccess = 0;
    private const uint _fileShareRead = 0x01;
    private const uint _fileShareWrite = 0x02;
    private const uint _fileShareDelete = 0x04;
    private const uint _openExisting = 0x03;
    private const uint _fileFlagBackupSemantics = 0x0200_0000;
    private const uint _volumeNameDos = 0;

    /// <summary>Go: <c>Realpath(path) (string, error)</c>：解析符号链接后的规范路径（失败返回 <c>null</c>）。</summary>
    public static string? Realpath(string path)
    {
        return OperatingSystem.IsWindows() ? RealpathWindows(path) : RealpathPosix(path);
    }

    private static string? RealpathWindows(string path)
    {
        // Go：路径 >= 248 时交给 os.Open 走 fixLongPath（加 \\?\ 前缀）。
        var openPath = path;
        if (path.Length >= 248)
        {
            openPath = Path.GetFullPath(path);
            if (!openPath.StartsWith(@"\\?\", StringComparison.Ordinal))
            {
                openPath = @"\\?\" + openPath;
            }
        }

        using var handle = CreateFile(
            openPath,
            _fileAnyAccess,
            _fileShareRead | _fileShareWrite | _fileShareDelete,
            IntPtr.Zero,
            _openExisting,
            _fileFlagBackupSemantics,
            IntPtr.Zero);
        if (handle.IsInvalid)
        {
            return null;
        }

        var buffer = new char[310];
        while (true)
        {
            var n = GetFinalPathNameByHandle(handle, buffer, (uint)buffer.Length, _volumeNameDos);
            if (n == 0)
            {
                return null;
            }

            if (n < buffer.Length)
            {
                var s = new string(buffer, 0, (int)n);
                if (s.Length > 4 && s.StartsWith(@"\\?\", StringComparison.Ordinal))
                {
                    s = s[4..];
                    if (s.Length > 3 && s.StartsWith("UNC", StringComparison.Ordinal))
                    {
                        // 还原成 \\server\share\... 形式
                        return "\\" + s[3..];
                    }

                    return s;
                }

                return null;
            }

            buffer = new char[n];
        }
    }

    private static string? RealpathPosix(string path)
    {
        var buffer = new byte[_pathMax];
        var result = RealpathNative(path, buffer);
        return result == IntPtr.Zero ? null : Marshal.PtrToStringUTF8(result);
    }

    [DllImport("kernel32.dll", EntryPoint = "CreateFileW", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern SafeFileHandle CreateFile(
        string lpFileName,
        uint dwDesiredAccess,
        uint dwShareMode,
        IntPtr lpSecurityAttributes,
        uint dwCreationDisposition,
        uint dwFlagsAndAttributes,
        IntPtr hTemplateFile);

    [DllImport("kernel32.dll", EntryPoint = "GetFinalPathNameByHandleW", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern uint GetFinalPathNameByHandle(
        SafeFileHandle hFile,
        [Out][MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 2)] char[] lpszFilePath,
        uint cchFilePath,
        uint dwFlags);

    [DllImport("libc", EntryPoint = "realpath", SetLastError = true)]
    private static extern IntPtr RealpathNative(
        [MarshalAs(UnmanagedType.LPUTF8Str)] string path,
        [Out][MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 0)] byte[] resolved);
}
