// ported from: internal/fswatch/canonicalize_other.go, canonicalize_darwin.go
//              （+ fsevents_darwin_ffi.go 里的 nativePathFolding / foldNativePath / normalizeNFC）
//
// C# 映射与偏离（逐条）：
// 1) Go 用构建标签分两个文件（`!(darwin && (amd64||arm64))` 与 `darwin && (amd64||arm64)`）→ C# 一个文件里按
//    `OperatingSystem.IsMacOS()` 运行时二选一（.NET 没有 build tag；与 NativePath/Realpath.cs、Ipc/Transport.cs
//    同一处理）。`nativePathFolding` 这个编译期常量 → `NativePathFolding`（internal static readonly；只在
//    darwin x64/arm64 为 true）。
// 2) Go 在非 darwin 用 `panic` 表示"此处不可达" → C# `Debug.Fail`（§3.2 的 panic → Debug.Fail）。
// 3) `foldNativePath`/`normalizeNFC` 在 Go 里手写 FFI（cgo_import_dynamic + 汇编写跳板）调 CoreFoundation；
//    C# 用 `DllImport` 直接声明同一批 CF 函数（CoreFoundation.framework），语义逐行对应：
//    CFStringCreateWithCString(UTF-8 NUL 结尾) → CFStringCreateMutableCopy → CFStringNormalize(NFC=2) →
//    CFStringFold(kCFCompareCaseInsensitive=1) → 再 NFC → CFStringGetCString(UTF-8) 取回。
// 4) `PathComparerForPath` 在 darwin 调 `pathconf(_PC_CASE_SENSITIVE = 11)` 查询**被监听卷**是否大小写敏感，
//    Go 的 `(PathComparer, error)` → C# 正常返回、失败抛 `IOException`（§3.2）。非 darwin 恒为"逐字节精确"。
// 5) Go 的 `os.PathError{Op:"pathconf"}` → C# `IOException("pathconf \"path\": <errno 文本>")`。
namespace TypeScript.FsWatch;

using System.ComponentModel;
using System.Runtime.InteropServices;

/// <summary>
/// Go: <c>fsevents_darwin_ffi.go</c> 里的 CoreFoundation 导入（cgo_import_dynamic + 汇编跳板）→ C# 的
/// <c>DllImport</c>。只有 macOS 上才会被调用。
/// </summary>
internal static class CoreFoundation
{
    /// <summary>Go: <c>cfStringEncodingUTF8</c>（kCFStringEncodingUTF8）。</summary>
    internal const uint StringEncodingUtf8 = 0x0800_0100;

    /// <summary>Go: <c>cfStringNormalizationFormC</c>（kCFStringNormalizationFormC）。</summary>
    internal const nint NormalizationFormC = 2;

    /// <summary>Go: <c>cfCompareCaseInsensitive</c>（kCFCompareCaseInsensitive）。</summary>
    internal const uint CompareCaseInsensitive = 1;

    private const string _library = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";

    /// <summary>Go: <c>cfStringCreate(0, cstr, cfStringEncodingUTF8)</c>。</summary>
    internal static IntPtr StringCreate(string s) =>
        CFStringCreateWithCString(0, s, StringEncodingUtf8);

    /// <summary>Go: <c>cfRelease(ref)</c>（nil 安全：CFRelease 不接受 NULL，所以这里先判空）。</summary>
    internal static void Release(IntPtr reference)
    {
        if (reference != 0)
        {
            CFRelease(reference);
        }
    }

    /// <summary>Go: <c>cfStringCreateMutableCopy(0, 0, src)</c>。</summary>
    internal static IntPtr StringCreateMutableCopy(IntPtr str) => CFStringCreateMutableCopy(0, 0, str);

    /// <summary>Go: <c>cfStringNormalize(mutStr, cfStringNormalizationFormC)</c>。</summary>
    internal static void StringNormalize(IntPtr mutStr, nint form) => CFStringNormalize(mutStr, form);

    /// <summary>Go: <c>CFStringFold(mutStr, cfCompareCaseInsensitive, 0)</c>。</summary>
    internal static void StringFold(IntPtr mutStr, uint flags) => CFStringFold(mutStr, flags, 0);

    /// <summary>Go: <c>cfStringToGo(src)</c>。取 CFString 的 UTF-8 内容（失败返回 ""）。</summary>
    internal static string StringToUtf8(IntPtr src)
    {
        if (src == 0)
        {
            return "";
        }

        var length = (int)CFStringGetLength(src);
        var bufSize = (int)CFStringGetMaximumSizeForEncoding(length, StringEncodingUtf8) + 1;
        var buffer = Marshal.AllocHGlobal(bufSize);
        try
        {
            if (CFStringGetCString(src, buffer, bufSize, StringEncodingUtf8) == 0)
            {
                return "";
            }

            return Marshal.PtrToStringUTF8(buffer) ?? "";
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    /// <summary>Go: <c>cfStringNormalizedToGo(src)</c>（NFC 后取回，失败返回 ""）。</summary>
    internal static string StringNormalizedToUtf8(IntPtr src)
    {
        var mut = StringCreateMutableCopy(src);
        if (mut == 0)
        {
            return "";
        }

        try
        {
            StringNormalize(mut, NormalizationFormC);
            return StringToUtf8(mut);
        }
        finally
        {
            Release(mut);
        }
    }

    /// <summary>Go: <c>cfStringToNFC(src)</c>（NFC 失败时退回未归一化内容）。</summary>
    internal static string StringToNFC(IntPtr src)
    {
        if (src == 0)
        {
            return "";
        }

        var normalized = StringNormalizedToUtf8(src);
        return normalized.Length != 0 ? normalized : StringToUtf8(src);
    }

    [DllImport(_library, EntryPoint = "CFStringCreateWithCString")]
    private static extern IntPtr CFStringCreateWithCString(
        IntPtr allocator,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string cStr,
        uint encoding);

    [DllImport(_library, EntryPoint = "CFStringCreateMutableCopy")]
    private static extern IntPtr CFStringCreateMutableCopy(IntPtr allocator, nint maxLength, IntPtr str);

    [DllImport(_library, EntryPoint = "CFRelease")]
    private static extern void CFRelease(IntPtr reference);

    [DllImport(_library, EntryPoint = "CFStringNormalize")]
    private static extern void CFStringNormalize(IntPtr theString, nint theForm);

    [DllImport(_library, EntryPoint = "CFStringFold")]
    private static extern void CFStringFold(IntPtr theString, uint theFlags, IntPtr theLocale);

    [DllImport(_library, EntryPoint = "CFStringGetLength")]
    private static extern nint CFStringGetLength(IntPtr theString);

    [DllImport(_library, EntryPoint = "CFStringGetMaximumSizeForEncoding")]
    private static extern nint CFStringGetMaximumSizeForEncoding(nint length, uint encoding);

    [DllImport(_library, EntryPoint = "CFStringGetCString")]
    private static extern byte CFStringGetCString(IntPtr theString, IntPtr buffer, nint bufferSize, uint encoding);
}

public static partial class FsWatch
{
    /// <summary>Go: <c>const nativePathFolding</c>（仅 darwin x64/arm64 为 true）。</summary>
    internal static readonly bool NativePathFolding =
        OperatingSystem.IsMacOS()
        && (RuntimeInformation.ProcessArchitecture == Architecture.X64
            || RuntimeInformation.ProcessArchitecture == Architecture.Arm64);

    /// <summary>Go: <c>func isASCII(s string) bool</c>。</summary>
    internal static bool IsAscii(string s)
    {
        foreach (var ch in s)
        {
            if (ch > 0x7F)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Go: <c>func canonicalizePath(p string) string</c>。darwin 上把 watch key、订阅文件名、FSEvents 传来的路径
    /// 统一到 NFC；其它平台是恒等函数（那些平台的 watcher 报的路径就是调用方给的字节）。
    /// </summary>
    internal static string CanonicalizePath(string p) => NativePathFolding ? NormalizeNFC(p) : p;

    /// <summary>Go: <c>func normalizeNFC(s string) string</c>（仅在 darwin 编译；失败时原样返回）。</summary>
    internal static string NormalizeNFC(string s)
    {
        // Go 把字节原样交给 CFStringCreateWithCString(UTF8)：含非法 UTF-8 时 CF 返回 NULL，Go 于是原样返回 s。
        // C# 的 LPUTF8Str 编组会把孤立代理项替换成 U+FFFD（CF 就不会失败），所以这里先按同样的条件短路，
        // 保证"非法路径原样返回"这一行为一致。
        if (!NativePathFolding || IsAscii(s) || !IsValidUtf8Equivalent(s))
        {
            return s;
        }

        var src = CoreFoundation.StringCreate(s);
        if (src == 0)
        {
            return s;
        }

        try
        {
            var normalized = CoreFoundation.StringToNFC(src);
            return normalized.Length == 0 ? s : normalized;
        }
        finally
        {
            CoreFoundation.Release(src);
        }
    }

    /// <summary>
    /// Go: <c>func foldNativePath(s string) string</c>。这是**比较形式**，绝不是用于展示或打开的路径：
    /// 大小写折叠会展开 sharp s 与连字，但不会让变音符号、无点 i、圈号字母、全角/半角互换。
    /// </summary>
    internal static string FoldNativePath(string s)
    {
        if (IsAscii(s))
        {
            return s.ToLowerInvariant();
        }

        if (!NativePathFolding)
        {
            Debug.Fail("fswatch: native path folding is only available on Darwin");
        }

        if (!IsValidUtf8Equivalent(s) || s.Contains('\0'))
        {
            return "";
        }

        var src = CoreFoundation.StringCreate(s);
        if (src == 0)
        {
            Debug.Fail("fswatch: cannot create CFString for path folding");
        }

        try
        {
            var mut = CoreFoundation.StringCreateMutableCopy(src);
            if (mut == 0)
            {
                Debug.Fail("fswatch: cannot copy CFString for path folding");
            }

            try
            {
                // 折叠前也先归一化：分解形式的"带点大写 I"必须与预组合形式有相同的比较形式。
                CoreFoundation.StringNormalize(mut, CoreFoundation.NormalizationFormC);
                CoreFoundation.StringFold(mut, CoreFoundation.CompareCaseInsensitive);
                CoreFoundation.StringNormalize(mut, CoreFoundation.NormalizationFormC);
                var folded = CoreFoundation.StringToUtf8(mut);
                if (folded.Length == 0)
                {
                    Debug.Fail("fswatch: cannot extract folded CFString");
                }

                return folded;
            }
            finally
            {
                CoreFoundation.Release(mut);
            }
        }
        finally
        {
            CoreFoundation.Release(src);
        }
    }

    /// <summary>
    /// Go: <c>func PathComparerForPath(path string) (PathComparer, error)</c>。查询已存在路径所在卷的大小写语义；
    /// 失败必须抛给调用方，绝不能静默地开/关原生折叠。
    /// </summary>
    public static PathComparer PathComparerForPath(string path)
    {
        if (!NativePathFolding)
        {
            return default;
        }

        // _PC_CASE_SENSITIVE from sys/unistd.h. 查询被监听的卷，而不是假设 macOS 上每个卷都大小写不敏感。
        const int pcCaseSensitive = 11;
        var sensitive = Pathconf(path, pcCaseSensitive);
        if (sensitive < 0)
        {
            var errno = Marshal.GetLastPInvokeError();
            throw new IOException($"pathconf \"{path}\": {new Win32Exception(errno).Message}");
        }

        return new PathComparer(new VolumePathComparer(sensitive == 0));
    }

    /// <summary>
    /// Go 的 `utf8.ValidString`：C# 字符串本来就是 UTF-16 代码单元序列，"非法 UTF-8" 的等价物是含孤立代理项
    /// （无法往返 UTF-8）。NUL 由调用点单独判断（Go 里也是分开的两条检查）。
    /// </summary>
    internal static bool IsValidUtf8Equivalent(string s)
    {
        for (var i = 0; i < s.Length; i++)
        {
            if (char.IsHighSurrogate(s[i]))
            {
                if (i + 1 >= s.Length || !char.IsLowSurrogate(s[i + 1]))
                {
                    return false;
                }

                i++;
            }
            else if (char.IsLowSurrogate(s[i]))
            {
                return false;
            }
        }

        return true;
    }

    [DllImport("libc", EntryPoint = "pathconf", SetLastError = true)]
    private static extern nint Pathconf([MarshalAs(UnmanagedType.LPUTF8Str)] string path, int name);
}

internal sealed partial class FsWatcher
{
    /// <summary>
    /// Go: <c>func (w *watcher) pathComparer(dir string) (pathComparer, error)</c>（canonicalize_other.go 里恒为
    /// 精确比较；canonicalize_darwin.go 里只对 fsevents/kqueue 查询卷大小写语义）。失败抛异常（§3.2）。
    /// </summary>
    internal VolumePathComparer PathComparerFor(string dir)
    {
        if (!FsWatch.NativePathFolding || (_name != "fsevents" && _name != "kqueue"))
        {
            return VolumePathComparer.Exact;
        }

        return FsWatch.PathComparerForPath(dir).Comparer;
    }
}
