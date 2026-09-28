// ported from: cmd/tsc/isprocessalive_windows.go, isprocessalive_unix.go, isprocessalive_other.go
//
// 与 Go 的偏离（逐条给依据）：
//
// 1) Go 用三个 build-tag 文件（windows / unix / !unix&&!windows）→ C# 一个文件里**运行时三分支**（本仓既有做法：
//    FsWatch/Canonicalize.cs 把 canonicalize_other.go + canonicalize_darwin.go 合成一个文件再按
//    `OperatingSystem.*` 判定；.NET 没有 build tag）。`const processAliveSupported` → <see cref="ProcessAlive.Supported"/>
//    （internal static readonly，值在静态初始化时按平台定死）。
// 2) `panic("isProcessAlive is not supported on this platform")` → `TypeScript.Debug.Fail`（§3.2 的 panic 映射）。
//    只有 Supported == false 时才会走到；调用点（lsp.go 的 newParentProcessWatchdog）已先判 Supported ⇒ 与 Go 一样
//    不可达。
// 3) Windows 分支逐函数对应 Go 的 syscall（`OpenProcess(SYNCHRONIZE, false, pid)` + `WaitForSingleObject(h, 0)`
//    判 WAIT_TIMEOUT + `CloseHandle`）→ kernel32 的 DllImport。**不用** `System.Diagnostics.Process.GetProcessById`：
//    它在 Windows 上要多建一个 Process 对象、在 Unix 上走另一套路径，而 Go 的实现就是最裸的两个系统调用——
//    DllImport 版本零分配、零反射，AOT 与行为都更贴近 Go（本工程同样禁止 `using System.Diagnostics;`，见
//    docs/architecture.md §2 的 Debug.Fail 坑）。
// 4) `syscall.WaitForSingleObject` 的 Go 实现**只在 WAIT_FAILED 时**给非 nil error（x/sys 的 `event == WAIT_FAILED`
//    判定）→ 这里也只在 `0xFFFF_FFFF` 时返回 false，然后才比 WAIT_TIMEOUT（不能只看 SetLastError：该 API 的
//    last-error 只在 WAIT_FAILED 时有意义）。
// 5) Unix 分支：Go 的 `os.FindProcess(pid)`（Unix 上恒成功）+ `proc.Signal(syscall.Signal(0))` → 就是 libc 的
//    `kill(pid, 0)`；Go 的 `err == nil || errors.Is(err, syscall.EPERM)` → 返回值 0 或 errno == EPERM(1)。
// 6) Go 的 `unix` build tag 覆盖 linux/darwin/freebsd/netbsd/openbsd/dragonfly/solaris/aix/android/ios 等；.NET 的
//    `OperatingSystem.Is*` 里可判定的是 linux/darwin(macos)/freebsd/android/ios/tvos/maccatalyst/watchos
//    → 这些归 unix 分支；Windows 归 windows 分支；其余（browser/wasi 等"other"）归不支持分支。
using global::System.Runtime.InteropServices;

namespace TypeScript.Tsc;

/// <summary>Go: <c>isProcessAlive(pid int) bool</c> + <c>const processAliveSupported</c>（三个平台文件）。</summary>
internal static class ProcessAlive
{
    /// <summary>Go: <c>const processAliveSupported</c>（isprocessalive_{windows,unix,other}.go:5/7/11）。</summary>
    internal static readonly bool Supported = OperatingSystem.IsWindows() || IsUnix();

    /// <summary>Go: <c>func isProcessAlive(pid int) bool</c>。</summary>
    internal static bool IsProcessAlive(int pid)
    {
        if (OperatingSystem.IsWindows())
        {
            return WindowsIsProcessAlive(pid);
        }
        if (IsUnix())
        {
            return UnixIsProcessAlive(pid);
        }

        // Go: panic("isProcessAlive is not supported on this platform")（见文件头偏离 2）。
        TypeScript.Debug.Fail("isProcessAlive is not supported on this platform");
        return false;
    }

    /// <summary>Go: unix build tag 覆盖的平台（见文件头偏离 6）。</summary>
    private static bool IsUnix() =>
        OperatingSystem.IsLinux()
        || OperatingSystem.IsMacOS()
        || OperatingSystem.IsFreeBSD()
        || OperatingSystem.IsAndroid()
        || OperatingSystem.IsIOS()
        || OperatingSystem.IsTvOS()
        || OperatingSystem.IsMacCatalyst()
        || OperatingSystem.IsWatchOS();

    // ---- Windows（isprocessalive_windows.go:13-26）--------------------------------------------------

    /// <summary>Go: <c>const SYNCHRONIZE = 0x00100000</c>（isprocessalive_windows.go:14）。</summary>
    private const uint _synchronize = 0x00100000;

    /// <summary>Go: <c>const WAIT_TIMEOUT = 258</c>（isprocessalive_windows.go:24）。</summary>
    private const uint _waitTimeout = 258;

    /// <summary>Go: <c>syscall.WAIT_FAILED</c>（见文件头偏离 4）。</summary>
    private const uint _waitFailed = 0xFFFF_FFFF;

    private static bool WindowsIsProcessAlive(int pid)
    {
        var handle = OpenProcess(_synchronize, false, (uint)pid);
        if (handle == 0)
        {
            return false;   // Go: err != nil → false
        }
        try
        {
            var ret = WaitForSingleObject(handle, 0);
            if (ret == _waitFailed)
            {
                return false;   // Go: err != nil → false
            }
            return ret == _waitTimeout;
        }
        finally
        {
            _ = CloseHandle(handle);
        }
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern nint OpenProcess(uint dwDesiredAccess, [MarshalAs(UnmanagedType.Bool)] bool bInheritHandle, uint dwProcessId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint WaitForSingleObject(nint hHandle, uint dwMilliseconds);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(nint hObject);

    // ---- Unix（isprocessalive_unix.go:18-25）-------------------------------------------------------

    /// <summary>Go: <c>syscall.EPERM</c>。</summary>
    private const int _eperm = 1;

    private static bool UnixIsProcessAlive(int pid)
    {
        // Go: err = proc.Signal(syscall.Signal(0)); return err == nil || errors.Is(err, syscall.EPERM)
        var result = kill(pid, 0);
        return result == 0 || Marshal.GetLastPInvokeError() == _eperm;
    }

    [DllImport("libc", SetLastError = true)]
    private static extern int kill(int pid, int sig);
}
