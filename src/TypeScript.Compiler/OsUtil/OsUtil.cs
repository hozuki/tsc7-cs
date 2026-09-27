// ported from: internal/osutil/osutil.go, os_other.go, os_android.go
//
// 偏离：Go 的 <c>Executable() (string, error)</c> → C# 返回 <c>string?</c>（取不到时 null，调用点自行处理，
// 见 osvfs 的 isFileSystemCaseSensitive）。
namespace TypeScript.OsUtil;

public static partial class OsUtil
{
    private const string _termuxExecutableEnv = "TERMUX_EXEC__PROC_SELF_EXE";

    /// <summary>Go: <c>Args()</c>：去掉平台特有 launcher 细节后的命令行参数。</summary>
    public static string[] Args()
    {
        var args = Environment.GetCommandLineArgs();
        if (OperatingSystem.IsAndroid())
        {
            var termuxExe = Environment.GetEnvironmentVariable(_termuxExecutableEnv);
            if (!string.IsNullOrEmpty(termuxExe) && args.Length > 1)
            {
                // Termux 通过 Android 的 linker 启动非 cgo 二进制，可执行文件路径落在 argv[1]。
                var exe = Executable();
                if (exe != null && args[1] == exe)
                {
                    var result = new string[args.Length - 1];
                    result[0] = args[0];
                    Array.Copy(args, 2, result, 1, args.Length - 2);
                    return result;
                }
            }
        }

        return args;
    }

    /// <summary>Go: <c>Executable() (string, error)</c>：当前可执行文件路径。</summary>
    public static string? Executable()
    {
        if (OperatingSystem.IsAndroid())
        {
            // Termux 下 /proc/self/exe 指向 Android 的 linker，而不是它加载的可执行文件。
            var termuxExe = Environment.GetEnvironmentVariable(_termuxExecutableEnv);
            if (!string.IsNullOrEmpty(termuxExe))
            {
                try
                {
                    return Path.GetFullPath(termuxExe);
                }
                catch (ArgumentException)
                {
                    return null;
                }
            }
        }

        return Environment.ProcessPath;
    }
}
