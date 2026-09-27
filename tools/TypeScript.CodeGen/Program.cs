// tools/TypeScript.CodeGen 的入口。
//
// 用法：
//   dotnet run --project tools/TypeScript.CodeGen -- [--tsgo-root <path>] [--out <dir>]
//     --tsgo-root  TSGO 仓库根目录；缺省时取环境变量 TSGO_ROOT，再缺省取 D:\Source\GitHub\TypeScript-Go
//     --out        生成的 .cs 输出目录；缺省 src/TypeScript.Compiler/StringUtil
//
// 生成的文件只在内容变化时被重写（幂等，见 UnicodeTableGenerator）。
namespace TypeScript.CodeGen;

internal static class Program
{
    private const string DefaultTsgoRoot = @"D:\Source\GitHub\TypeScript-Go";

    private static int Main(string[] args)
    {
        string? tsgoRoot = null;
        string? outDir = null;
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
                    if (!TryReadValue(args, ref i, out outDir))
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
            tsgoRoot = DefaultTsgoRoot;
        }

        outDir ??= Path.Combine("src", "TypeScript.Compiler", "StringUtil");

        try
        {
            var written = UnicodeTableGenerator.Generate(tsgoRoot, outDir);
            if (written.Count == 0)
            {
                Console.WriteLine("Unicode 表已是最新，未重写任何文件。");
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
        Console.Error.WriteLine("用法：TypeScript.CodeGen [--tsgo-root <path>] [--out <dir>]");
    }
}
