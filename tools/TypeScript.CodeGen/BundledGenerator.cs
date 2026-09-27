// tools/TypeScript.CodeGen 的 bundled libs 生成器（对应 TSGO 的 tsc/internal/bundled/generate.go）。
//
// 输入（相对 <TSGO_ROOT>）：
//   tsc/internal/bundled/libs/*.d.ts           108 个 lib 声明文件（3,784,983 字节）
//   tsc/internal/bundled/CopyrightNotice.txt   版权头；readLibs 用它校验每个 lib 的头部与行尾
//   tsc/internal/bundled/libs_generated.go     权威 lib 名清单（LibNames），用来核对目录内容
//
// 输出（--bundled-libs <dir>，缺省 src/TypeScript.Compiler/Bundled）：
//   libs/<name>.d.ts       ← TSGO 同名文件（逐字节复制；csproj 以 EmbeddedResource 嵌入，见 Bundled/ 的 csproj 条目）
//   Libs.Generated.cs      ← internal/bundled/libs_generated.go
//   Embed.Generated.cs     ← internal/bundled/embed_generated.go
//
// 判定依据：generate.go 的 readLibs / generateLibList / generateEmbedded 逐条搬运。三处偏离（都是语言/工具链差异）：
//   * generateEmbedded 在 Go 里给每个 lib 生成一个 //go:embed 变量，再加一张 `map[string]string`（值=内容）。
//     C# 侧的对应物是 csproj 里的 EmbeddedResource 条目（LogicalName 固定成 "TypeScript.Bundled.libs." + 文件名）
//     + 生成表 `EmbeddedContents`（值=资源名）。不生成 3.8 MB 的字符串字面量（docs/checker-plan.md §3.1）。
//   * generate.go 没有"把 libs 复制进产物目录"这一步（Go 的 go:embed 直接读源码树）。我们需要复制：本仓库必须
//     能脱离 TSGO 检出独立构建（csproj 里不可能指向 D:\Source\GitHub\TypeScript-Go）。
//   * Go 的 writeCode 用 go/format 格式化后**无条件写文件**；这里按内容比较后再写（与本工具其它生成器一致）。
//     所以"重复运行 0 diff"成立，而 Go 侧每次都会重写时间戳。
//
// 用法：
//   dotnet run --project tools/TypeScript.CodeGen -- [--tsgo-root <path>] [--bundled-libs <dir>]
//   TSGO 根目录优先级：--tsgo-root > 环境变量 TSGO_ROOT > D:\Source\GitHub\TypeScript-Go
using System.Text;
using System.Text.RegularExpressions;

namespace TypeScript.CodeGen;

internal static class BundledGenerator
{
    private const string _bundledRelativePath = "tsc/internal/bundled";
    private const string _libsDirName = "libs";
    private const string _copyrightNoticeName = "CopyrightNotice.txt";
    private const string _libNamesRelativePath = "tsc/internal/bundled/libs_generated.go";

    /// <summary>嵌入资源名前缀：csproj 的 LogicalName = 本前缀 + lib 文件名（TypeScript.Compiler.csproj 的对应条目）。</summary>
    private const string _resourcePrefix = "TypeScript.Bundled.libs.";

    /// <summary>Go 的 <c>LibNames = []string{ "lib.d.ts", … }</c> 里的清单行。</summary>
    private static readonly Regex _libNameLineRegex = new("^\"([^\"]+)\",$", RegexOptions.CultureInvariant);

    /// <summary>生成 libs 副本、LibNames 与资源名表；返回实际被重写的文件路径（内容未变化则为空）。</summary>
    public static List<string> Generate(string tsgoRoot, string outDir)
    {
        var bundledDir = Path.Combine(tsgoRoot, _bundledRelativePath.Replace('/', Path.DirectorySeparatorChar));
        var libsDir = Path.Combine(bundledDir, _libsDirName);
        if (!Directory.Exists(libsDir))
        {
            throw new FileNotFoundException(
                $"找不到 TSGO 输入目录：{libsDir}。用 --tsgo-root <path> 或环境变量 TSGO_ROOT 指定 TSGO 仓库根目录。",
                libsDir);
        }

        var libNames = ReadLibs(libsDir, Path.Combine(bundledDir, _copyrightNoticeName));

        // libs_generated.go 是权威清单（Go 侧由 readLibs 排序后生成），目录内容必须与它逐名一致。
        var authoritative = ReadLibNamesFromGeneratedFile(
            Path.Combine(tsgoRoot, _libNamesRelativePath.Replace('/', Path.DirectorySeparatorChar)));
        if (!libNames.SequenceEqual(authoritative, StringComparer.Ordinal))
        {
            throw new InvalidDataException(
                $"libs 目录与 {_libNamesRelativePath} 的清单不一致（目录 {libNames.Count} 个、generated {authoritative.Count} 个）："
                + DescribeDifference(libNames, authoritative));
        }

        var outLibsDir = Path.Combine(outDir, _libsDirName);
        Directory.CreateDirectory(outLibsDir);

        var written = new List<string>();
        foreach (var name in libNames)
        {
            CopyIfChanged(Path.Combine(libsDir, name), Path.Combine(outLibsDir, name), written);
        }

        WriteIfChanged(Path.Combine(outDir, "Libs.Generated.cs"), RenderLibNames(libNames), written);
        WriteIfChanged(Path.Combine(outDir, "Embed.Generated.cs"), RenderEmbeddedContents(libNames), written);
        return written;
    }

    // ── libs 目录 ─────────────────────────────────────────────────────────────────────────────────────

    /// <summary>Go 的 <c>readLibs</c>：列举 + 校验 libs/，返回按名排序的 lib 文件名。</summary>
    private static List<string> ReadLibs(string libsDir, string copyrightNoticePath)
    {
        if (!File.Exists(copyrightNoticePath))
        {
            throw new FileNotFoundException($"找不到 TSGO 输入文件：{copyrightNoticePath}。", copyrightNoticePath);
        }

        // Go: copyright 必须无 '\r' 且以空行（"\n\n"）结尾；header = copyright + '\n'。
        var notice = File.ReadAllBytes(copyrightNoticePath);
        if (notice.AsSpan().Contains((byte)'\r') || !notice.AsSpan().EndsWith("\n\n"u8))
        {
            throw new InvalidDataException($"{copyrightNoticePath} must use LF line endings and end with a blank line");
        }

        var header = new byte[notice.Length + 1];
        notice.CopyTo(header, 0);
        header[^1] = (byte)'\n';

        var names = new List<string>();
        // Go 的 os.ReadDir 按文件名排序；这里用同样的序数序。
        foreach (var entryPath in Directory.EnumerateFileSystemEntries(libsDir).Order(StringComparer.Ordinal))
        {
            var name = Path.GetFileName(entryPath);
            if (Directory.Exists(entryPath) || !name.StartsWith("lib.", StringComparison.Ordinal)
                || !name.EndsWith(".d.ts", StringComparison.Ordinal))
            {
                throw new InvalidDataException($"unexpected entry in {_libsDirName}: {name}");
            }

            var content = File.ReadAllBytes(entryPath);
            if (content.AsSpan().Contains((byte)'\r'))
            {
                throw new InvalidDataException($"{entryPath} must use LF line endings");
            }

            if (!content.AsSpan().StartsWith(header))
            {
                throw new InvalidDataException(
                    $"{entryPath} must start with {_copyrightNoticeName} followed by a blank line");
            }

            if (content.Length == 0 || content[^1] != (byte)'\n')
            {
                throw new InvalidDataException($"{entryPath} must end with a newline");
            }

            names.Add(name);
        }

        if (names.Count == 0)
        {
            throw new InvalidDataException($"{libsDir} 里没有任何 lib 文件");
        }

        return names;
    }

    /// <summary>从 TSGO 的 <c>libs_generated.go</c> 解析权威 lib 名清单（Go 的 <c>LibNames</c>）。</summary>
    private static List<string> ReadLibNamesFromGeneratedFile(string path)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"找不到 TSGO 输入文件：{path}。", path);
        }

        var names = new List<string>();
        var inList = false;
        foreach (var raw in File.ReadLines(path))
        {
            var line = raw.Trim();
            if (!inList)
            {
                if (line.StartsWith("var LibNames = []string{", StringComparison.Ordinal))
                {
                    inList = true;
                }

                continue;
            }

            if (line == "}")
            {
                break;
            }

            var match = _libNameLineRegex.Match(line);
            if (!match.Success)
            {
                throw new InvalidDataException($"无法解析 {path} 的清单行：{raw}");
            }

            names.Add(match.Groups[1].Value);
        }

        if (!inList || names.Count == 0)
        {
            throw new InvalidDataException($"无法从 {path} 解析 LibNames");
        }

        return names;
    }

    private static string DescribeDifference(List<string> directory, List<string> generated)
    {
        var onlyDirectory = directory.Except(generated, StringComparer.Ordinal).ToArray();
        var onlyGenerated = generated.Except(directory, StringComparer.Ordinal).ToArray();
        var parts = new List<string>();
        if (onlyDirectory.Length > 0)
        {
            parts.Add("只在目录里：" + string.Join(", ", onlyDirectory));
        }

        if (onlyGenerated.Length > 0)
        {
            parts.Add("只在 libs_generated.go 里：" + string.Join(", ", onlyGenerated));
        }

        for (var i = 0; i < Math.Min(directory.Count, generated.Count); i++)
        {
            if (!string.Equals(directory[i], generated[i], StringComparison.Ordinal))
            {
                parts.Add($"顺序不同（下标 {i}）：目录 {directory[i]} vs generated {generated[i]}");
                break;
            }
        }

        return parts.Count > 0 ? string.Join("；", parts) : "集合相同但顺序不同";
    }

    // ── 生成物 ───────────────────────────────────────────────────────────────────────────────────────

    /// <summary>Go 的 <c>generateLibList</c>。</summary>
    private static string RenderLibNames(List<string> libNames)
    {
        var sb = new StringBuilder();
        AppendHeader(
            sb,
            "internal/bundled/libs_generated.go",
            "tsc/internal/bundled/libs/（108 个 lib 文件名；与 tsc/internal/bundled/libs_generated.go 逐名核对）",
            "dotnet run --project tools/TypeScript.CodeGen -- --bundled-libs src/TypeScript.Compiler/Bundled",
            "判定依据：TSGO 的 tsc/internal/bundled/generate.go（readLibs 排序 + generateLibList 逐条对应）");
        sb.Append("#nullable enable\n\n");
        sb.Append("namespace TypeScript.Bundled;\n\n");
        sb.Append("public static partial class Bundled\n{\n");
        sb.Append("    /// <summary>LibNames is the list of all bundled lib files, sorted by name.\n");
        sb.Append("    /// For the list of libs sorted by load order, use <c>tsoptions.Libs</c>.</summary>\n");
        sb.Append("    public static readonly string[] LibNames =\n    [\n");
        foreach (var name in libNames)
        {
            sb.Append("        \"").Append(name).Append("\",\n");
        }

        sb.Append("    ];\n}\n");
        return sb.ToString();
    }

    /// <summary>Go 的 <c>generateEmbedded</c>：//go:embed 变量表 → 资源名表（值从"内容"改成"资源名"）。</summary>
    private static string RenderEmbeddedContents(List<string> libNames)
    {
        var sb = new StringBuilder();
        AppendHeader(
            sb,
            "internal/bundled/embed_generated.go",
            "tsc/internal/bundled/libs/（108 个 lib 文件名）+ TypeScript.Compiler.csproj 的 LogicalName 约定",
            "dotnet run --project tools/TypeScript.CodeGen -- --bundled-libs src/TypeScript.Compiler/Bundled",
            "判定依据：TSGO 的 tsc/internal/bundled/generate.go（generateEmbedded 逐条对应）");
        sb.Append("#nullable enable\n\n");
        sb.Append("namespace TypeScript.Bundled;\n\n");
        sb.Append("public static partial class Bundled\n{\n");
        sb.Append("    /// <summary>Go 的 <c>embeddedContents</c>（embed_generated.go）：FS 相对路径（\"libs/&lt;name&gt;\"）→ 嵌入资源名，\n");
        sb.Append("    /// 顺序与 <see cref=\"LibNames\"/> 一致。\n");
        sb.Append("    /// 偏离：Go 的表值是 //go:embed 变量（文件内容本身）；C# 的资源是随程序集发布的独立文件，\n");
        sb.Append("    /// 表里只存资源名，读取走 <c>Assembly.GetManifestResourceStream</c>（AOT 安全、无反射；也不生成 3.8 MB 字面量）。\n");
        sb.Append("    /// 资源名由 TypeScript.Compiler.csproj 的 EmbeddedResource + LogicalName 固定成同样的字符串。\n");
        sb.Append("    /// 属性名保留 Go 的导出名（包级 var embeddedContents 未导出 → internal；名字按 C# 习惯改 PascalCase）。</summary>\n");
        sb.Append("    internal static readonly Dictionary<string, string> EmbeddedContents = new(")
            .Append(libNames.Count).Append(", StringComparer.Ordinal)\n    {\n");
        foreach (var name in libNames)
        {
            sb.Append("        [\"libs/").Append(name).Append("\"] = \"").Append(_resourcePrefix).Append(name).Append("\",\n");
        }

        sb.Append("    };\n}\n");
        return sb.ToString();
    }

    // ── 写入 ──────────────────────────────────────────────────────────────────────────────────────────

    // 内容相同则不写、不改时间戳（与 UnicodeTableGenerator/GoAstGenerator/DiagnosticsGenerator 一致）。
    private static void WriteIfChanged(string path, string content, List<string> written)
    {
        if (File.Exists(path) && File.ReadAllText(path) == content)
        {
            return;
        }

        File.WriteAllText(path, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        written.Add(path);
    }

    // 字节相同则不复制（Go 侧没有这一步；理由见文件头）。
    private static void CopyIfChanged(string sourcePath, string destinationPath, List<string> written)
    {
        var content = File.ReadAllBytes(sourcePath);
        if (File.Exists(destinationPath) && File.ReadAllBytes(destinationPath).AsSpan().SequenceEqual(content))
        {
            return;
        }

        File.WriteAllBytes(destinationPath, content);
        written.Add(destinationPath);
    }

    private static void AppendHeader(StringBuilder sb, string portedFrom, string input, string command, string rationale)
    {
        sb.Append("// <auto-generated>\n");
        sb.Append("// This file was generated by tools/TypeScript.CodeGen (BundledGenerator.cs). DO NOT EDIT.\n");
        sb.Append("// 请勿手工编辑：修改输入后重新运行\n");
        sb.Append("//   ").Append(command).Append('\n');
        sb.Append("// 输入：").Append(input).Append('\n');
        sb.Append("// ").Append(rationale).Append('\n');
        sb.Append("// </auto-generated>\n");
        sb.Append("// ported from: ").Append(portedFrom).Append('\n');
    }
}
