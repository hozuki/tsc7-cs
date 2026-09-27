// tools/TypeScript.CodeGen 的 Unicode 表生成器。
//
// 背景（见 docs/architecture.md §4）：TSGO 的 identifier_parts_generated.go 与 js_case_generated.go 由
// tsc/internal/stringutil/_scripts/generate-unicode-data.mts 从 npm 包 @unicode/unicode-15.1.0 生成。
// 本机既没有 node_modules 也没有 Go 工具链，所以本生成器改为**解析 TSGO 已提交的两份生成文件**：
// 与 TSGO 出货行为逐位一致、可离线复跑、且不依赖上游包的下载。
//
// 用法：
//   dotnet run --project tools/TypeScript.CodeGen -- [--tsgo-root <path>] [--out <dir>]
//   TSGO 根目录优先级：--tsgo-root > 环境变量 TSGO_ROOT > D:\Source\GitHub\TypeScript-Go
//
// 幂等：输出只由输入文件内容决定（头部写的是相对 TSGO 的路径，不含绝对路径），连续两次运行字节一致；
// 且内容未变化时不重写文件（不改时间戳）。
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace TypeScript.CodeGen;

internal static class UnicodeTableGenerator
{
    // 输入：TSGO 里已提交的生成文件（相对 TSGO 根目录）。
    private const string _identifierGoPath = "tsc/internal/stringutil/identifier_parts_generated.go";
    private const string _jsCaseGoPath = "tsc/internal/stringutil/js_case_generated.go";

    // 对应 TSGO 仓库内路径（写进生成文件头部与 // ported from:）。
    private const string _identifierPortedFrom = "internal/stringutil/identifier_parts_generated.go";
    private const string _jsCasePortedFrom = "internal/stringutil/js_case_generated.go";

    private static readonly Regex _rangeEntryRegex = new(
        @"\{\s*0x([0-9A-Fa-f]+)\s*,\s*0x([0-9A-Fa-f]+)\s*,\s*([0-9]+)\s*\}",
        RegexOptions.CultureInvariant);

    private static readonly Regex _latinOffsetRegex = new(@"LatinOffset:\s*([0-9]+)", RegexOptions.CultureInvariant);

    private static readonly Regex _mapEntryRegex = new(
        @"^0x([0-9A-Fa-f]+):\s*\{(.*)\},?$",
        RegexOptions.CultureInvariant);

    private static readonly Regex _mapFieldRegex = new(
        @"([A-Za-z_][A-Za-z0-9_]*):\s*(""(?:[^""\\]|\\.)*""|[A-Za-z_][A-Za-z0-9_]*)",
        RegexOptions.CultureInvariant);

    /// <summary>生成两个文件，返回实际被重写的文件路径（内容未变化则为空）。</summary>
    public static List<string> Generate(string tsgoRoot, string outDir)
    {
        Directory.CreateDirectory(outDir);
        var identifierOutput = Path.Combine(outDir, "IdentifierParts.Generated.cs");
        var jsCaseOutput = Path.Combine(outDir, "SpecialCasing.Generated.cs");

        var identifierText = RenderIdentifierFile(ReadInput(tsgoRoot, _identifierGoPath));
        var jsCaseText = RenderJsCaseFile(ReadInput(tsgoRoot, _jsCaseGoPath));

        var written = new List<string>();
        if (WriteIfChanged(identifierOutput, identifierText))
        {
            written.Add(identifierOutput);
        }

        if (WriteIfChanged(jsCaseOutput, jsCaseText))
        {
            written.Add(jsCaseOutput);
        }

        return written;
    }

    private static string ReadInput(string tsgoRoot, string relativePath)
    {
        var fullPath = Path.Combine(tsgoRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException(
                $"找不到 TSGO 输入文件：{fullPath}。用 --tsgo-root <path> 或环境变量 TSGO_ROOT 指定 TSGO 仓库根目录。",
                fullPath);
        }

        return File.ReadAllText(fullPath);
    }

    // 内容相同则不写、不改时间戳（便于将来接入构建）。
    private static bool WriteIfChanged(string path, string content)
    {
        if (File.Exists(path) && File.ReadAllText(path) == content)
        {
            return false;
        }

        File.WriteAllText(path, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        return true;
    }

    private static string RenderIdentifierFile(string goText)
    {
        var start = ParseRangeTable(goText, "unicodeESNextIdentifierStart");
        var part = ParseRangeTable(goText, "unicodeESNextIdentifierPart");

        var sb = new StringBuilder();
        AppendHeader(sb, _identifierGoPath, _identifierPortedFrom);
        sb.Append("namespace TypeScript.StringUtil;\n\n");
        sb.Append("public static partial class StringUtil\n{\n");
        AppendRangeTable(sb, "UnicodeESNextIdentifierStart", start);
        sb.Append('\n');
        AppendRangeTable(sb, "UnicodeESNextIdentifierPart", part);
        sb.Append("}\n");
        return sb.ToString();
    }

    private static string RenderJsCaseFile(string goText)
    {
        var entries = ParseSpecialCasingMappings(goText);
        var cased = ParseRangeTable(goText, "unicodeCasedRanges");
        var caseIgnorable = ParseRangeTable(goText, "unicodeCaseIgnorableRanges");

        var sb = new StringBuilder();
        AppendHeader(sb, _jsCaseGoPath, _jsCasePortedFrom);
        sb.Append("namespace TypeScript.StringUtil;\n\n");
        // Go: type specialCasingCondition uint8
        sb.Append("internal enum SpecialCasingCondition : byte\n{\n");
        sb.Append("    None = 0,\n");
        sb.Append("    FinalSigma = 1,\n");
        sb.Append("}\n\n");
        // Go: type specialCasingMapping struct { lower, upper, conditionalLower string; condition specialCasingCondition }
        sb.Append("internal readonly struct SpecialCasingMapping(");
        sb.Append("string lower, string upper, string conditionalLower, SpecialCasingCondition condition)\n{\n");
        sb.Append("    public readonly string Lower = lower;\n");
        sb.Append("    public readonly string Upper = upper;\n");
        sb.Append("    public readonly string ConditionalLower = conditionalLower;\n");
        sb.Append("    public readonly SpecialCasingCondition Condition = condition;\n");
        sb.Append("}\n\n");
        sb.Append("public static partial class StringUtil\n{\n");
        sb.Append("    internal static readonly Dictionary<int, SpecialCasingMapping> SpecialCasingMappings = new()\n    {\n");
        foreach (var entry in entries)
        {
            sb.Append("        [0x").Append(Hex(entry.CodePoint)).Append("] = new(")
                .Append(entry.Lower).Append(", ")
                .Append(entry.Upper).Append(", ")
                .Append(entry.ConditionalLower).Append(", SpecialCasingCondition.")
                .Append(entry.Condition).Append("),\n");
        }

        sb.Append("    };\n\n");
        AppendRangeTable(sb, "UnicodeCasedRanges", cased);
        sb.Append('\n');
        AppendRangeTable(sb, "UnicodeCaseIgnorableRanges", caseIgnorable);
        sb.Append("}\n");
        return sb.ToString();
    }

    private static void AppendHeader(StringBuilder sb, string goRelativePath, string portedFrom)
    {
        sb.Append("// <auto-generated>\n");
        sb.Append("// This file was generated by tools/TypeScript.CodeGen (UnicodeTableGenerator.cs). DO NOT EDIT.\n");
        sb.Append("// 请勿手工编辑：修改输入后重新运行\n");
        sb.Append("//   dotnet run --project tools/TypeScript.CodeGen -- --out src/TypeScript.Compiler/StringUtil\n");
        sb.Append("// 输入：").Append(goRelativePath).Append("（相对 TSGO 根目录）\n");
        sb.Append("// </auto-generated>\n");
        sb.Append("// ported from: ").Append(portedFrom).Append('\n');
    }

    private static void AppendRangeTable(StringBuilder sb, string name, RangeTable table)
    {
        sb.Append("    internal static readonly UnicodeRangeTable ").Append(name).Append(" = new(\n");
        sb.Append("        r16: [\n");
        foreach (var range in table.R16)
        {
            sb.Append("            new Range16(0x").Append(Hex(range.Lo)).Append(", 0x").Append(Hex(range.Hi))
                .Append(", ").Append(range.Stride.ToString(CultureInfo.InvariantCulture)).Append("),\n");
        }

        sb.Append("        ],\n");
        sb.Append("        r32: [\n");
        foreach (var range in table.R32)
        {
            sb.Append("            new Range32(0x").Append(Hex(range.Lo)).Append(", 0x").Append(Hex(range.Hi))
                .Append(", ").Append(range.Stride.ToString(CultureInfo.InvariantCulture)).Append("),\n");
        }

        sb.Append("        ],\n");
        sb.Append("        latinOffset: ").Append(table.LatinOffset.ToString(CultureInfo.InvariantCulture)).Append(");\n");
    }

    private static RangeTable ParseRangeTable(string goText, string variableName)
    {
        var tableBody = SliceBracedBlock(goText, $"var {variableName} = &unicode.RangeTable{{");
        var table = new RangeTable();
        ParseRanges(SliceBracedBlock(tableBody, "R16: []unicode.Range16{"), table.R16);
        ParseRanges(SliceBracedBlock(tableBody, "R32: []unicode.Range32{"), table.R32);
        var latinOffsetMatch = _latinOffsetRegex.Match(tableBody);
        if (!latinOffsetMatch.Success)
        {
            throw new InvalidDataException($"{variableName}: 找不到 LatinOffset。");
        }

        table.LatinOffset = int.Parse(latinOffsetMatch.Groups[1].Value, CultureInfo.InvariantCulture);
        return table;
    }

    private static void ParseRanges(string body, List<RangeEntry> ranges)
    {
        foreach (Match match in _rangeEntryRegex.Matches(body))
        {
            ranges.Add(new RangeEntry(
                ParseHex(match.Groups[1].Value),
                ParseHex(match.Groups[2].Value),
                uint.Parse(match.Groups[3].Value, CultureInfo.InvariantCulture)));
        }
    }

    private static List<SpecialCasingEntry> ParseSpecialCasingMappings(string goText)
    {
        var mapBody = SliceBracedBlock(goText, "var specialCasingMappings = map[rune]specialCasingMapping{");
        var entries = new List<SpecialCasingEntry>();
        foreach (var rawLine in mapBody.Split('\n'))
        {
            var line = rawLine.Trim();
            if (line.Length == 0)
            {
                continue;
            }

            var entryMatch = _mapEntryRegex.Match(line);
            if (!entryMatch.Success)
            {
                throw new InvalidDataException($"无法解析 specialCasingMappings 条目：{line}");
            }

            var entry = new SpecialCasingEntry
            {
                CodePoint = ParseHex(entryMatch.Groups[1].Value),
                Lower = "\"\"",
                Upper = "\"\"",
                ConditionalLower = "\"\"",
                Condition = "None", // Go 的零值是 specialCasingConditionNone
            };

            foreach (Match field in _mapFieldRegex.Matches(entryMatch.Groups[2].Value))
            {
                var value = field.Groups[2].Value;
                switch (field.Groups[1].Value)
                {
                    case "lower":
                        entry.Lower = ValidateGoStringLiteral(value);
                        break;
                    case "upper":
                        entry.Upper = ValidateGoStringLiteral(value);
                        break;
                    case "conditionalLower":
                        entry.ConditionalLower = ValidateGoStringLiteral(value);
                        break;
                    case "condition":
                        entry.Condition = value switch
                        {
                            "specialCasingConditionNone" => "None",
                            "specialCasingConditionFinalSigma" => "FinalSigma",
                            _ => throw new InvalidDataException($"未知的 condition：{value}"),
                        };
                        break;
                    default:
                        throw new InvalidDataException($"specialCasingMapping 里未知字段：{field.Groups[1].Value}");
                }
            }

            entries.Add(entry);
        }

        return entries;
    }

    // Go 的字符串字面量与 C# 的转义写法一致（\uXXXX / \UXXXXXXXX / \\ / \" / \n / \r / \t / \'），
    // 所以校验后原样输出即可；遇到不认识的转义直接报错，避免生成出语法错误的 C#。
    private static string ValidateGoStringLiteral(string literal)
    {
        if (literal.Length < 2 || literal[0] != '"' || literal[^1] != '"')
        {
            throw new InvalidDataException($"不是合法的字符串字面量：{literal}");
        }

        for (var i = 1; i < literal.Length - 1; i++)
        {
            var ch = literal[i];
            if (ch == '"')
            {
                throw new InvalidDataException($"字符串字面量里有未转义的引号：{literal}");
            }

            if (ch != '\\')
            {
                continue;
            }

            i++;
            if (i >= literal.Length - 1)
            {
                throw new InvalidDataException($"字符串字面量以反斜杠结尾：{literal}");
            }

            var escape = literal[i];
            switch (escape)
            {
                case 'u':
                    EnsureHexDigits(literal, i + 1, 4);
                    i += 4;
                    break;
                case 'U':
                    EnsureHexDigits(literal, i + 1, 8);
                    i += 8;
                    break;
                case '\\':
                case '"':
                case '\'':
                case 'n':
                case 'r':
                case 't':
                    break;
                default:
                    throw new InvalidDataException($"不支持的转义 \\{escape}：{literal}");
            }
        }

        return literal;
    }

    private static void EnsureHexDigits(string literal, int start, int count)
    {
        if (start + count > literal.Length - 1)
        {
            throw new InvalidDataException($"转义序列不完整：{literal}");
        }

        for (var i = start; i < start + count; i++)
        {
            if (!Uri.IsHexDigit(literal[i]))
            {
                throw new InvalidDataException($"转义序列里有非十六进制字符：{literal}");
            }
        }
    }

    // 取 `prefix`（必须以 { 结尾）之后成对花括号内的文本。
    private static string SliceBracedBlock(string text, string prefix)
    {
        var index = text.IndexOf(prefix, StringComparison.Ordinal);
        if (index < 0)
        {
            throw new InvalidDataException($"找不到 `{prefix}`。");
        }

        var open = index + prefix.Length - 1;
        var depth = 0;
        for (var i = open; i < text.Length; i++)
        {
            switch (text[i])
            {
                case '{':
                    depth++;
                    break;
                case '}':
                    depth--;
                    if (depth == 0)
                    {
                        return text[(open + 1)..i];
                    }

                    break;
            }
        }

        throw new InvalidDataException($"`{prefix}` 的花括号不配对。");
    }

    private static uint ParseHex(string hex) => uint.Parse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture);

    private static string Hex(uint value) => value.ToString("X", CultureInfo.InvariantCulture);

    private sealed class RangeTable
    {
        public readonly List<RangeEntry> R16 = [];

        public readonly List<RangeEntry> R32 = [];

        public int LatinOffset;
    }

    private readonly record struct RangeEntry(uint Lo, uint Hi, uint Stride);

    private sealed class SpecialCasingEntry
    {
        public uint CodePoint;

        public string Lower = "\"\"";

        public string Upper = "\"\"";

        public string ConditionalLower = "\"\"";

        public string Condition = "None";
    }
}
