// tools/TypeScript.CodeGen 的诊断消息生成器（对应 TSGO 的 tsc/internal/diagnostics/generate.go）。
//
// 输入（相对 <TSGO_ROOT>）：
//   tsc/internal/diagnostics/diagnosticMessages.json   语言中立的消息表（2213 条；键=英文文本，值=code/category/flags）
//   tsc/internal/diagnostics/loc/*.generated.json      各语言的交回文件（这里只用来校验"声明的语言都有数据"）
//   tsc/internal/diagnostics/loc/*.json.gz             TSGO 已提交的压缩本地化数据（逐字节复制进本仓库）
//   tools/LocProject.json                              本地化工程文件（声明语言与顺序 = Go 的 matcher 顺序）
//
// 输出（--diagnostics-out <dir>，缺省 src/TypeScript.Compiler/Diagnostics）：
//   Messages.Generated.cs       ← internal/diagnostics/diagnostics_generated.go
//   Localizations.Generated.cs  ← internal/diagnostics/loc_generated.go
//   loc/<lang>.json.gz          ← TSGO 的 loc/*.json.gz（csproj 以 EmbeddedResource 嵌入，见 TypeScript.Compiler.csproj）
//
// 判定依据：generate.go 的 readRawMessages / convertPropertyName / generateDiagnostics / readLocaleNames /
// generateLocalizations 逐条搬运（只把输出语法从 Go 换成 C#，并补上 .editorconfig/公开 API 的命名要求）。
//
// 为什么复制 .json.gz 而不是自己压缩 .generated.json：Go 侧 .json.gz 是 generate.go 用 .generated.json 重新
// 压出来的（过滤掉已删除的诊断键）。本机没有 Go 工具链，也没有本地化流水线，逐字节复制 TSGO 已提交的产物才能
// 保证运行时数据与 TSGO 出货逐位一致；数据是否与当前消息表同步由
// tests/TypeScript.Compiler.Tests/Diagnostics/DiagnosticsTests.cs 的 TestLocaleFiles 对照 .generated.json 校验
// （与 Go 的 TestLocaleFiles 同一条断言）。
//
// 幂等：文本产物按内容比较后再写；.json.gz 按字节比较后再复制。连续两次运行不产生任何改动。
//
// 用法：
//   dotnet run --project tools/TypeScript.CodeGen -- [--tsgo-root <path>] [--diagnostics-out <dir>]
//   TSGO 根目录优先级：--tsgo-root > 环境变量 TSGO_ROOT > D:\Source\GitHub\TypeScript-Go
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace TypeScript.CodeGen;

internal static class DiagnosticsGenerator
{
    private const string _rawMessagesRelativePath = "tsc/internal/diagnostics/diagnosticMessages.json";
    private const string _locDirRelativePath = "tsc/internal/diagnostics/loc";
    private const string _locProjectRelativePath = "tools/LocProject.json";

    /// <summary>LocProject.json 里指向的消息源文件（Go 的 -locsource 参数）——用来挑出属于本生成器的 LocItem。</summary>
    private const string _locSourceRelativePath = "tsc/internal/diagnostics/diagnosticMessages.generated.json";

    private const string _messagesResourcePrefix = "TypeScript.Diagnostics.loc.";

    // convertPropertyName 用的三个正则（与 generate.go 逐字对应）。
    private static readonly Regex _multipleUnderscoreRegexp = new("_+", RegexOptions.CultureInvariant);
    private static readonly Regex _leadingUnderscoreUnlessDigitRegexp = new(@"^_+(\D)", RegexOptions.CultureInvariant);
    private static readonly Regex _trailingUnderscoreRegexp = new("_$", RegexOptions.CultureInvariant);

    // Go 的 readLocaleNames 用 language.Parse + `tag.String() != localeName` 断言"声明的名字已是规范形式"。
    // 本生成器不引用 TypeScript.Compiler（生成器不该依赖被生成的目标），所以用等价的形状检查：语言(2..3 个小写
    // 字母) + 地区(2 个大写字母或 3 位数字)。LocProject.json 当前声明的 13 个名字都符合；将来若声明别的形状，
    // 这里会显式报错提示扩展（而不是静默生成错误的资源名）。
    private static readonly Regex _canonicalLocaleRegex = new(@"^[a-z]{2,3}(-[A-Z]{2}|-[0-9]{3})$", RegexOptions.CultureInvariant);

    /// <summary>生成诊断消息与本地化表，返回实际被重写的文件路径（内容未变化则为空）。</summary>
    public static List<string> Generate(string tsgoRoot, string outDir)
    {
        var rawMessagesPath = Path.Combine(tsgoRoot, _rawMessagesRelativePath.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(rawMessagesPath))
        {
            throw new FileNotFoundException(
                $"找不到 TSGO 输入文件：{rawMessagesPath}。用 --tsgo-root <path> 或环境变量 TSGO_ROOT 指定 TSGO 仓库根目录。",
                rawMessagesPath);
        }

        var messages = ReadRawMessages(rawMessagesPath);
        var localeNames = ReadLocaleNames(tsgoRoot);

        Directory.CreateDirectory(outDir);
        var written = new List<string>();

        WriteIfChanged(Path.Combine(outDir, "Messages.Generated.cs"), RenderMessages(messages), written);
        WriteIfChanged(Path.Combine(outDir, "Localizations.Generated.cs"), RenderLocalizations(localeNames), written);
        written.AddRange(CopyLocaleArchives(tsgoRoot, outDir, localeNames));

        return written;
    }

    // ── 消息表 ────────────────────────────────────────────────────────────────────────────────────────

    private sealed class DiagnosticMessage
    {
        public required string Text { get; init; }

        public required string Category { get; init; }

        public required int Code { get; init; }

        public bool ReportsUnnecessary { get; init; }

        public bool ReportsDeprecated { get; init; }

        public bool ElidedInCompatibilityPyramid { get; init; }
    }

    private static List<DiagnosticMessage> ReadRawMessages(string path)
    {
        var json = MiniJson.ParseObject(File.ReadAllText(path));
        var byCode = new Dictionary<int, DiagnosticMessage>(json.Count);
        var messages = new List<DiagnosticMessage>(json.Count);

        foreach (var (text, value) in json.Items)
        {
            if (value is not JsonObject entry)
            {
                throw new InvalidDataException($"诊断 {text} 的值不是对象。");
            }

            var message = new DiagnosticMessage
            {
                Text = text,
                Category = ReadString(entry, "category", text),
                Code = ReadInt(entry, "code", text),
                ReportsUnnecessary = ReadBool(entry, "reportsUnnecessary"),
                ReportsDeprecated = ReadBool(entry, "reportsDeprecated"),
                // 注意拼写：[sic]，与 Strada/Go 的 "elidedInCompatabilityPyramid" 一致。
                ElidedInCompatibilityPyramid = ReadBool(entry, "elidedInCompatabilityPyramid"),
            };

            if (byCode.TryGetValue(message.Code, out var existing))
            {
                throw new InvalidDataException($"诊断 {existing.Text} 与 {message.Text} 使用了同一个 code {message.Code}。");
            }

            byCode.Add(message.Code, message);
            messages.Add(message);
        }

        // Go: slices.SortFunc(diagnosticMessages, cmp.Compare(a.Code, b.Code))。
        messages.Sort(static (a, b) => a.Code.CompareTo(b.Code));
        return messages;
    }

    private static string ReadString(JsonObject entry, string name, string text)
    {
        return entry[name] as string ?? throw new InvalidDataException($"诊断 {text} 缺少字符串字段 {name}。");
    }

    private static int ReadInt(JsonObject entry, string name, string text)
    {
        return entry[name] is double value
            ? (int)value
            : throw new InvalidDataException($"诊断 {text} 缺少数字字段 {name}。");
    }

    private static bool ReadBool(JsonObject entry, string name)
    {
        return entry[name] is bool value && value;
    }

    /// <summary>Go 的 <c>convertPropertyName</c>：返回（导出变量名, 本地化键）。</summary>
    private static (string VarName, string Key) ConvertPropertyName(string origName, int code)
    {
        var builder = new StringBuilder(origName.Length);
        foreach (var rune in origName.EnumerateRunes())
        {
            switch (rune.Value)
            {
                case '*':
                    builder.Append("_Asterisk");
                    break;
                case '/':
                    builder.Append("_Slash");
                    break;
                case ':':
                    builder.Append("_Colon");
                    break;
                default:
                    builder.Append(Rune.IsLetter(rune) || Rune.IsDigit(rune) ? rune.ToString() : "_");
                    break;
            }
        }

        var varName = builder.ToString();
        // 合并连续下划线。
        varName = _multipleUnderscoreRegexp.Replace(varName, "_");
        // 去掉开头下划线，除非它后面是数字（"_0_expected" 保留前导下划线）。
        varName = _leadingUnderscoreUnlessDigitRegexp.Replace(varName, "$1");
        // 去掉结尾下划线。
        varName = _trailingUnderscoreRegexp.Replace(varName, string.Empty);

        // 键用的是"去掉前导下划线"之后、加 X 前缀之前的名字（Go 的 `key = varName`）。
        // 注意 Go 的 len() 是字节长度：这些名字全是 ASCII，所以字符长度与字节长度相同。
        var key = varName;
        if (key.Length > 100)
        {
            key = key[..100];
        }

        key = key + "_" + code.ToString(CultureInfo.InvariantCulture);

        if (!IsExported(varName))
        {
            // Go 的 `if varName[0] == '_' { "X" } else { "X_" }`。
            varName = varName[0] == '_' ? "X" + varName : "X_" + varName;
        }

        if (!IsExported(varName))
        {
            throw new InvalidDataException($"无法把属性名 {origName} 转成合法的 C# 标识符（得到 {varName}）。");
        }

        return (varName, key);
    }

    /// <summary>Go 的 <c>token.IsExported</c>：首字符是大写字母。</summary>
    private static bool IsExported(string name)
    {
        return name.Length != 0 && char.IsUpper(name, 0);
    }

    private static string RenderMessages(List<DiagnosticMessage> messages)
    {
        var sb = new StringBuilder();
        AppendHeader(
            sb,
            "internal/diagnostics/diagnostics_generated.go",
            "tsc/internal/diagnostics/diagnosticMessages.json",
            "dotnet run --project tools/TypeScript.CodeGen -- --diagnostics-out src/TypeScript.Compiler/Diagnostics",
            "判定依据：TSGO 的 tsc/internal/diagnostics/generate.go（generateDiagnostics/convertPropertyName 逐条对应）");
        sb.Append("#nullable enable\n");
        sb.Append("//\n");
        sb.Append("// Go 的 `var X = &Message{code: …, category: CategoryError, key: \"…\", text: \"…\"}` → C#：\n");
        sb.Append("//   * 消息变量是 public static readonly Message（名字逐字照 Go；.editorconfig 的命名规则只约束\n");
        sb.Append("//     非公开成员，公开字段不受影响）；\n");
        sb.Append("//   * Message 是 class（Go 用 *Message），构造器 internal；三个可选开关用命名参数表达\n");
        sb.Append("//     （Go 生成物里缺省字段不出现，C# 同样只在为 true 时写出来）；\n");
        sb.Append("//   * Go 的 `var allMessages = [...]**Message{&X, …}` → internal Message[] AllMessages。\n");
        sb.Append("namespace TypeScript.Diagnostics;\n\n");
        sb.Append("public static partial class Diagnostics\n{\n");

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var varNames = new List<string>(messages.Count);
        foreach (var message in messages)
        {
            var (varName, key) = ConvertPropertyName(message.Text, message.Code);
            if (!seen.Add(varName))
            {
                throw new InvalidDataException($"诊断变量名重复：{varName}（code {message.Code}）。");
            }

            varNames.Add(varName);
            sb.Append("    public static readonly Message ").Append(varName).Append(" = new(")
                .Append(message.Code.ToString(CultureInfo.InvariantCulture)).Append(", Category.")
                .Append(CategoryMember(message.Category, message.Text)).Append(", ")
                .Append(Quote(key)).Append(", ").Append(Quote(message.Text));
            if (message.ReportsUnnecessary)
            {
                sb.Append(", reportsUnnecessary: true");
            }

            if (message.ElidedInCompatibilityPyramid)
            {
                sb.Append(", elidedInCompatibilityPyramid: true");
            }

            if (message.ReportsDeprecated)
            {
                sb.Append(", reportsDeprecated: true");
            }

            sb.Append(");\n");
        }

        sb.Append("\n    /// <summary>Go 的 <c>var allMessages = [...]**Message{…}</c>（unexported → internal）。</summary>\n");
        sb.Append("    internal static readonly Message[] AllMessages =\n    [\n");
        foreach (var varName in varNames)
        {
            sb.Append("        ").Append(varName).Append(",\n");
        }

        sb.Append("    ];\n");
        sb.Append("}\n");
        return sb.ToString();
    }

    private static string CategoryMember(string category, string text)
    {
        return category switch
        {
            "Warning" => "Warning",
            "Error" => "Error",
            "Suggestion" => "Suggestion",
            "Message" => "Message",
            _ => throw new InvalidDataException($"诊断 {text} 的 category 未知：{category}。"),
        };
    }

    // ── 本地化数据 ────────────────────────────────────────────────────────────────────────────────────

    /// <summary>Go 的 <c>readLocaleNames</c>：从 LocProject.json 取出声明顺序，并校验交回文件齐备。</summary>
    private static List<string> ReadLocaleNames(string tsgoRoot)
    {
        var projectPath = Path.Combine(tsgoRoot, _locProjectRelativePath.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(projectPath))
        {
            throw new FileNotFoundException($"找不到 TSGO 本地化工程文件：{projectPath}。", projectPath);
        }

        var project = MiniJson.ParseObject(File.ReadAllText(projectPath));
        var projectRoot = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(projectPath)!, ".."));
        var sourcePath = Path.GetFullPath(Path.Combine(tsgoRoot, _locSourceRelativePath.Replace('/', Path.DirectorySeparatorChar)));

        string? languagesValue = null;
        if (project["Projects"] is List<object?> projects)
        {
            foreach (var value in projects)
            {
                if (value is not JsonObject entry || entry["LocItems"] is not List<object?> locItems)
                {
                    continue;
                }

                foreach (var itemValue in locItems)
                {
                    if (itemValue is not JsonObject item || item["SourceFile"] is not string sourceFile)
                    {
                        continue;
                    }

                    var itemPath = Path.GetFullPath(Path.Combine(projectRoot, sourceFile.Replace('\\', '/')));
                    if (!string.Equals(itemPath, sourcePath, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    if (languagesValue is not null)
                    {
                        throw new InvalidDataException($"{projectPath} 里有重复的本地化项：{sourcePath}。");
                    }

                    languagesValue = item["Languages"] as string;
                }
            }
        }

        if (languagesValue is null)
        {
            throw new InvalidDataException($"{projectPath} 里找不到 {sourcePath} 的本地化项。");
        }

        var locDir = Path.Combine(tsgoRoot, _locDirRelativePath.Replace('/', Path.DirectorySeparatorChar));
        var localeNames = new List<string>();
        var declared = new HashSet<string>(StringComparer.Ordinal);
        foreach (var localeName in languagesValue.Split(';'))
        {
            if (!_canonicalLocaleRegex.IsMatch(localeName))
            {
                throw new InvalidDataException(
                    $"{projectPath} 声明的 locale {localeName} 不是本生成器认识的规范形式（语言-地区）；请扩展 DiagnosticsGenerator 的规范检查。");
            }

            if (!declared.Add(localeName))
            {
                throw new InvalidDataException($"{projectPath} 里 locale {localeName} 重复声明。");
            }

            localeNames.Add(localeName);
        }

        // Go 的 readLocaleNames 要求：声明的每个语言都有交回文件，且 loc/ 里没有未声明的交回文件。
        var pending = new HashSet<string>(declared, StringComparer.Ordinal);
        foreach (var file in Directory.EnumerateFiles(locDir))
        {
            var name = Path.GetFileName(file);
            if (!name.EndsWith(".json", StringComparison.Ordinal))
            {
                continue;
            }

            if (!name.EndsWith(".generated.json", StringComparison.Ordinal))
            {
                throw new InvalidDataException($"本地化文件 {file} 必须使用 .generated.json 后缀。");
            }

            var localeName = name[..^".generated.json".Length];
            if (!declared.Contains(localeName))
            {
                throw new InvalidDataException($"本地化文件 {file} 没有在 {projectPath} 里声明。");
            }

            pending.Remove(localeName);
        }

        if (pending.Count != 0)
        {
            var missing = string.Join(", ", pending.OrderBy(static name => name, StringComparer.Ordinal));
            throw new InvalidDataException($"{projectPath} 声明的语言缺少交回文件：{missing}。");
        }

        return localeNames;
    }

    /// <summary>把 TSGO 已提交的 loc/*.json.gz 逐字节复制到 &lt;out&gt;/loc/（内容相同则不动）。</summary>
    private static List<string> CopyLocaleArchives(string tsgoRoot, string outDir, List<string> localeNames)
    {
        var sourceDir = Path.Combine(tsgoRoot, _locDirRelativePath.Replace('/', Path.DirectorySeparatorChar));
        var outLocDir = Path.Combine(outDir, "loc");
        Directory.CreateDirectory(outLocDir);

        var written = new List<string>();
        foreach (var localeName in localeNames)
        {
            var archiveName = localeName + ".json.gz";
            var sourcePath = Path.Combine(sourceDir, archiveName);
            if (!File.Exists(sourcePath))
            {
                throw new FileNotFoundException($"找不到 TSGO 本地化产物：{sourcePath}。", sourcePath);
            }

            var outPath = Path.Combine(outLocDir, archiveName);
            var content = File.ReadAllBytes(sourcePath);
            if (File.Exists(outPath) && File.ReadAllBytes(outPath).AsSpan().SequenceEqual(content))
            {
                continue;
            }

            File.WriteAllBytes(outPath, content);
            written.Add(outPath);
        }

        return written;
    }

    private static string RenderLocalizations(List<string> localeNames)
    {
        var sb = new StringBuilder();
        AppendHeader(
            sb,
            "internal/diagnostics/loc_generated.go",
            "tools/LocProject.json（语言与顺序）+ tsc/internal/diagnostics/loc/*.json.gz（数据）",
            "dotnet run --project tools/TypeScript.CodeGen -- --diagnostics-out src/TypeScript.Compiler/Diagnostics",
            "判定依据：TSGO 的 tsc/internal/diagnostics/generate.go（readLocaleNames/generateLocalizations 逐条对应）");
        sb.Append("#nullable enable\n");
        sb.Append("//\n");
        sb.Append("// Go 的 loc_generated.go 里有三样东西：language.NewMatcher 的标签表、localeFuncs（index → 惰性加载函数）、\n");
        sb.Append("// 以及每个语言的 //go:embed 数据 + sync.OnceValue。C# 侧的对应物：\n");
        sb.Append("//   * `_supportedLocales`：标签表（下标 0 = English，对应 Go 的 localeFuncs[0] == nil，即没有翻译数据）；\n");
        sb.Append("//   * `LoadLocale(index)`：index → 解压后的键值表（数据来自 EmbeddedResource；0/越界返回 null）。\n");
        sb.Append("//     资源名是硬编码常量表（不由反射枚举，AOT 安全），csproj 里用 LogicalName 固定成同样的名字。\n");
        sb.Append("// 惰性 + 缓存由手写部分 Diagnostics.cs 的 _localizedMessagesCache 承担（Go 侧是 sync.OnceValue + sync.Map）。\n");
        sb.Append("// 注意 `using Loc = TypeScript.Locale.Locale;`：类型 Locale 与命名空间 TypeScript.Locale 同名，\n");
        sb.Append("// 在 TypeScript.* 命名空间里裸写 Locale 会解析到命名空间（见 Locale.cs 的头部说明）。\n");
        sb.Append("using Loc = TypeScript.Locale.Locale;\n\n");
        sb.Append("namespace TypeScript.Diagnostics;\n\n");
        sb.Append("public static partial class Diagnostics\n{\n");
        sb.Append("    private static readonly Loc[] _supportedLocales =\n    [\n");
        sb.Append("        Loc.Default, // English（默认语言；没有翻译数据，直接用英文原文）\n");
        foreach (var localeName in localeNames)
        {
            sb.Append("        Loc.FromTag(\"").Append(localeName).Append("\"),\n");
        }

        sb.Append("    ];\n\n");
        sb.Append("    private static Dictionary<Key, string>? LoadLocale(int index)\n    {\n");
        sb.Append("        return index switch\n        {\n");
        for (var i = 0; i < localeNames.Count; i++)
        {
            sb.Append("            ").Append(i + 1).Append(" => LoadLocaleData(\"")
                .Append(_messagesResourcePrefix).Append(localeNames[i]).Append(".json.gz\"),\n");
        }

        sb.Append("            _ => null,\n");
        sb.Append("        };\n    }\n");
        sb.Append("}\n");
        return sb.ToString();
    }

    // ── 写入 ──────────────────────────────────────────────────────────────────────────────────────────

    // 内容相同则不写、不改时间戳（与 UnicodeTableGenerator/GoAstGenerator 一致）。
    private static void WriteIfChanged(string path, string content, List<string> written)
    {
        if (File.Exists(path) && File.ReadAllText(path) == content)
        {
            return;
        }

        File.WriteAllText(path, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        written.Add(path);
    }

    private static void AppendHeader(StringBuilder sb, string portedFrom, string input, string command, string rationale)
    {
        sb.Append("// <auto-generated>\n");
        sb.Append("// This file was generated by tools/TypeScript.CodeGen (DiagnosticsGenerator.cs). DO NOT EDIT.\n");
        sb.Append("// 请勿手工编辑：修改输入后重新运行\n");
        sb.Append("//   ").Append(command).Append('\n');
        sb.Append("// 输入：").Append(input).Append('\n');
        sb.Append("// ").Append(rationale).Append('\n');
        sb.Append("// </auto-generated>\n");
        sb.Append("// ported from: ").Append(portedFrom).Append('\n');
    }

    /// <summary>把字符串写成 C# 字面量（转义反斜杠/引号/控制字符；其余字符原样保留，文件是 UTF-8）。</summary>
    private static string Quote(string text)
    {
        var sb = new StringBuilder(text.Length + 2);
        sb.Append('"');
        foreach (var c in text)
        {
            switch (c)
            {
                case '"':
                    sb.Append("\\\"");
                    break;
                case '\\':
                    sb.Append("\\\\");
                    break;
                case '\n':
                    sb.Append("\\n");
                    break;
                case '\r':
                    sb.Append("\\r");
                    break;
                case '\t':
                    sb.Append("\\t");
                    break;
                default:
                    if (char.IsControl(c))
                    {
                        sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                    }
                    else
                    {
                        sb.Append(c);
                    }

                    break;
            }
        }

        sb.Append('"');
        return sb.ToString();
    }
}
