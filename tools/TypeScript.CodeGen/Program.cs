// tools/TypeScript.CodeGen 的入口。
//
// 用法：
//   dotnet run --project tools/TypeScript.CodeGen -- [--tsgo-root <path>] [--out <dir>] [--unicode-out <dir>]
//     [--diagnostics-out <dir>]
//     --tsgo-root        TSGO 仓库根目录；缺省时取环境变量 TSGO_ROOT，再缺省取 D:\Source\GitHub\TypeScript-Go
//     --out              AST 生成物（*.Generated.cs）的输出目录；缺省 src/TypeScript.Compiler/Ast
//                        （与 Unicode 生成器的 CLI 形状一致：同一个 --out <dir> 参数名）
//     --unicode-out      Unicode 表生成物的输出目录；缺省 src/TypeScript.Compiler/StringUtil
//     --diagnostics-out  诊断消息/本地化表的输出目录；缺省 src/TypeScript.Compiler/Diagnostics
//                        （该目录下的 loc/*.json.gz 也会从 TSGO 逐字节复制过来）
//
// 生成的文件只在内容变化时被重写（幂等，见 UnicodeTableGenerator / GoAstGenerator / DiagnosticsGenerator）。
namespace TypeScript.CodeGen;

internal static class Program
{
    private const string _defaultTsgoRoot = @"D:\Source\GitHub\TypeScript-Go";

    private static readonly string _defaultAstOutDir = Path.Combine("src", "TypeScript.Compiler", "Ast");
    private static readonly string _defaultUnicodeOutDir = Path.Combine("src", "TypeScript.Compiler", "StringUtil");
    private static readonly string _defaultDiagnosticsOutDir = Path.Combine("src", "TypeScript.Compiler", "Diagnostics");

    private static int Main(string[] args)
    {
        string? tsgoRoot = null;
        string? astOutDir = null;
        string? unicodeOutDir = null;
        string? diagnosticsOutDir = null;
        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--tsgo-root":
                    if (!TryReadValue(args, ref i, out tsgoRoot))
                    {
                        return 1;
                    }

                    break;
                case "--out":
                    if (!TryReadValue(args, ref i, out astOutDir))
                    {
                        return 1;
                    }

                    break;
                case "--unicode-out":
                    if (!TryReadValue(args, ref i, out unicodeOutDir))
                    {
                        return 1;
                    }

                    break;
                case "--diagnostics-out":
                    if (!TryReadValue(args, ref i, out diagnosticsOutDir))
                    {
                        return 1;
                    }

                    break;
                case "-h":
                case "--help":
                    PrintUsage();
                    return 0;
                default:
                    Console.Error.WriteLine($"未知参数：{args[i]}");
                    PrintUsage();
                    return 1;
            }
        }

        tsgoRoot ??= Environment.GetEnvironmentVariable("TSGO_ROOT");
        if (string.IsNullOrEmpty(tsgoRoot))
        {
            tsgoRoot = _defaultTsgoRoot;
        }

        astOutDir ??= _defaultAstOutDir;
        unicodeOutDir ??= _defaultUnicodeOutDir;
        diagnosticsOutDir ??= _defaultDiagnosticsOutDir;

        try
        {
            var written = new List<string>();
            written.AddRange(UnicodeTableGenerator.Generate(tsgoRoot, unicodeOutDir));
            written.AddRange(GoAstGenerator.Generate(tsgoRoot, astOutDir));
            written.AddRange(DiagnosticsGenerator.Generate(tsgoRoot, diagnosticsOutDir));
            if (written.Count == 0)
            {
                Console.WriteLine("所有生成文件都已是最新，未重写任何文件。");
            }
            else
            {
                foreach (var path in written)
                {
                    Console.WriteLine($"已写入 {path}");
                }
            }

            return 0;
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
    }

    private static bool TryReadValue(string[] args, ref int i, out string? value)
    {
        if (i + 1 >= args.Length)
        {
            Console.Error.WriteLine($"参数 {args[i]} 缺少取值。");
            PrintUsage();
            value = null;
            return false;
        }

        value = args[++i];
        return true;
    }

    private static void PrintUsage()
    {
        Console.Error.WriteLine("用法：TypeScript.CodeGen [--tsgo-root <path>] [--out <dir>] [--unicode-out <dir>] [--diagnostics-out <dir>]");
    }
}
