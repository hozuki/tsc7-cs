// ported from: cmd/tsc/enablevtprocessing_windows.go
//
// 与 Go 的偏离（逐条给依据）：
//
// 1) Go 用 `//go:build windows` 限定整个文件，并用 `func init()` 在 main 之前自动执行；.NET 没有 build tag，
//    本仓的既有做法是**一个文件里运行时判定**（FsWatch/Canonicalize.cs 把 canonicalize_other.go +
//    canonicalize_darwin.go 合成一个文件；Vfs/OsVfs 同理）→ `if (!OperatingSystem.IsWindows()) return;`，
//    并由 Program.Main 首行显式调用（C# 没有"包级 init"，在 Main 首行调用比 module initializer 更直白，
//    且同样覆盖 --lsp/--api 与编译器三条路径）。
// 2) `golang.org/x/sys/windows` 的 GetStdHandle/GetFileType/GetConsoleMode/SetConsoleMode → kernel32 的 DllImport
//    （同 FsWatch/Canonicalize.cs 的既有做法：**纯静态 P/Invoke，不做反射**，NativeAOT 下可用；没用 LibraryImport
//    是因为它要求 partial 方法 + 可能触发 `<AllowUnsafeBlocks>`，而没有实际收益——这几个签名都是 blittable 的）。
// 3) Go 的条件 `if err != nil || fileType == windows.FILE_TYPE_CHAR` 里，`err != nil` 那一支实际上无害：紧接着的
//    GetConsoleMode 对非控制台句柄会失败并 return。**生效的**语义就是"标准输出是控制台（FILE_TYPE_CHAR）时才开
//    VT 处理"。这里逐字保留同一条件（x/sys 的 GetFileType 在 FILE_TYPE_UNKNOWN 时返回非 nil error），不"顺手
//    修正"，以免与 Go 的实际行为分叉。
// 4) Go 的 `windows.InvalidHandle`（= -1）：x/sys 的 GetStdHandle 在句柄为 0（无控制台）或 INVALID_HANDLE_VALUE 时
//    返回 error → 这里对 0/-1 都早退。
// 5) `init()` 里的错误全部被 Go 忽略（`_ = windows.SetConsoleMode(...)`）→ 这里同样不抛（VT 开不起来只是少个颜色）。
using global::System.Runtime.InteropServices;

namespace TypeScript.Tsc;

/// <summary>Go: <c>func init()</c>（enablevtprocessing_windows.go:7-22）——让 Windows 控制台支持 ANSI 转义序列。</summary>
internal static class EnableVtProcessing
{
    /// <summary>Go: <c>windows.STD_OUTPUT_HANDLE</c>。</summary>
    private const int _stdOutputHandle = -11;

    /// <summary>Go: <c>windows.FILE_TYPE_CHAR</c>。</summary>
    private const uint _fileTypeChar = 0x0002;

    /// <summary>Go: <c>windows.ENABLE_VIRTUAL_TERMINAL_PROCESSING</c>。</summary>
    private const uint _enableVirtualTerminalProcessing = 0x0004;

    /// <summary>Go: <c>func init()</c>（enablevtprocessing_windows.go:7-22）。</summary>
    internal static void Run()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;   // Go: 该文件带 //go:build windows（见文件头偏离 1）
        }

        var handle = GetStdHandle(_stdOutputHandle);
        if (handle == 0 || handle == -1)
        {
            return;   // Go: err != nil || h == windows.InvalidHandle
        }

        var fileType = GetFileType(handle);
        var fileTypeKnown = Marshal.GetLastPInvokeError() == 0;
        if (!fileTypeKnown || fileType == _fileTypeChar)
        {
            if (!GetConsoleMode(handle, out var mode))
            {
                return;
            }
            if ((mode & _enableVirtualTerminalProcessing) == 0)
            {
                _ = SetConsoleMode(handle, mode | _enableVirtualTerminalProcessing);
            }
        }
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern nint GetStdHandle(int nStdHandle);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint GetFileType(nint hFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetConsoleMode(nint hConsoleHandle, out uint lpMode);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetConsoleMode(nint hConsoleHandle, uint dwMode);
}
