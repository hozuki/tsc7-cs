// ported from: internal/diagnostics/diagnostics_test.go
//
// 移植映射（docs/architecture.md §5）：
//   * Go 的 `[]struct{…}` 表驱动 + t.Run(name, …) → xunit 的 [Theory] + [MemberData]；用例名通过
//     ITestOutputHelper 打进测试输出（见 StringUtil/UtilTests.cs、JsNum/JsNumTests.cs 的说明）。
//     xunit 的 [Theory] 每条用例各自并行，与 Go 的 t.Parallel() 相当。
//   * Go 的 `locale.Locale(language.English)` / `language.MustParse("de-DE")` → `Loc.FromTag("en")` /
//     `Loc.FromTag("de-DE")`（internal 的"must"工厂，对应 Go 生成物里的 language.MustParse）；
//     `language.Und` 就是 language.Tag 的零值，对应 `Loc.Default`（= `Loc.FromTag("und")`，见 Locale.cs）。
//   * Go 的 `TestLocaleFiles` 用 `filepath.Glob("loc/*.generated.json")` 读 TSGO 仓库里的交回文件；C# 侧从
//     TSGO_ROOT（缺省 D:\Source\GitHub\TypeScript-Go，与 docs/architecture.md §5 一致）读同一批文件，
//     逐条复刻它的两件断言：占位符集合一致；getLocalizedMessages 的结果 == "交回文件里过滤掉已删除键"。
//     Go 的 t.Run(每个语言一个子测试) → 这里在 [Fact] 里逐个文件断言（失败信息里带语言名；见 Core/PatternTests.cs
//     的同类做法）。
//   * Go 的 `assert.DeepEqual(t, actual, activeMessages)` → 数量 + 逐键比较（字典无序，逐键比较给出更准的失败信息）。
//   * Go 的 json.UnmarshalRead → 我们自己的 TypeScript.Json（AOT 安全，见 docs/json.md）。
using System.Text.RegularExpressions;
using TypeScript.Diagnostics;
using Xunit;
using Xunit.Abstractions;
using Loc = TypeScript.Locale.Locale;
using static TypeScript.Diagnostics.Diagnostics;
using static TypeScript.Json.Json;

namespace TypeScript.Tests;

public class DiagnosticsTests(ITestOutputHelper output)
{
    public sealed record LocalizeCase(string Name, Message Message, Loc Locale, string[] Args, string Expected);

    public static TheoryData<LocalizeCase> LocalizeTests => new()
    {
        new("english default", Identifier_expected, Loc.FromTag("en"), [], "Identifier expected."),
        new("undefined locale uses english", Identifier_expected, Loc.Default, [], "Identifier expected."),
        new("with single argument", X_0_expected, Loc.FromTag("en"), [")"], "')' expected."),
        new(
            "with multiple arguments",
            The_parser_expected_to_find_a_1_to_match_the_0_token_here,
            Loc.FromTag("en"),
            ["{", "}"],
            "The parser expected to find a '}' to match the '{' token here."),
        new("fallback to english for unknown locale", Identifier_expected, Loc.FromTag("af-ZA"), [], "Identifier expected."),
        new("german", Identifier_expected, Loc.FromTag("de-DE"), [], "Es wurde ein Bezeichner erwartet."),
        new("french", Identifier_expected, Loc.FromTag("fr-FR"), [], "Identificateur attendu."),
        new("spanish", Identifier_expected, Loc.FromTag("es-ES"), [], "Se esperaba un identificador."),
        new("japanese", Identifier_expected, Loc.FromTag("ja-JP"), [], "識別子が必要です。"),
        new("chinese simplified", Identifier_expected, Loc.FromTag("zh-CN"), [], "应为标识符。"),
        new("korean", Identifier_expected, Loc.FromTag("ko-KR"), [], "식별자가 필요합니다."),
        new("russian", Identifier_expected, Loc.FromTag("ru-RU"), [], "Ожидался идентификатор."),
        new("german with args", X_0_expected, Loc.FromTag("de-DE"), [")"], "\")\" wurde erwartet."),
    };

    public sealed record LocalizeByKeyCase(string Name, Key Key, Loc Locale, string[] Args, string Expected);

    public static TheoryData<LocalizeByKeyCase> LocalizeByKeyTests => new()
    {
        new("by key without args", "Identifier_expected_1003", Loc.FromTag("en"), [], "Identifier expected."),
        new("by key with args", "_0_expected_1005", Loc.FromTag("en"), [")"], "')' expected."),
    };

    [Theory]
    [MemberData(nameof(LocalizeTests))]
    public void TestLocalize(LocalizeCase tt)
    {
        output.WriteLine(tt.Name);

        var result = tt.Message.Localize(tt.Locale, tt.Args);

        Assert.Equal(tt.Expected, result);
    }

    [Theory]
    [MemberData(nameof(LocalizeByKeyTests))]
    public void TestLocalize_ByKey(LocalizeByKeyCase tt)
    {
        output.WriteLine(tt.Name);

        // Go: Localize(tt.locale, nil, tt.key, tt.args...)。
        var result = Localize(tt.Locale, null, tt.Key, tt.Args);

        Assert.Equal(tt.Expected, result);
    }

    [Fact]
    public void TestLocaleFiles()
    {
        var locDir = Path.Combine(TsgoRoot, "tsc", "internal", "diagnostics", "loc");
        var files = Directory.GetFiles(locDir, "*.generated.json");
        Assert.True(files.Length > 0, $"在 {locDir} 下找不到 *.generated.json。");

        foreach (var path in files)
        {
            var localeName = Path.GetFileName(path);
            localeName = localeName[..^".generated.json".Length];

            var handback = UnmarshalNew<Dictionary<string, string>>(File.ReadAllBytes(path));
            ValidateLocalizedMessages(handback, localeName);

            var activeMessages = new Dictionary<Key, string>(handback.Count);
            foreach (var (key, text) in handback)
            {
                if (KeyToMessage(key) is not null)
                {
                    activeMessages[new Key(key)] = text;
                }
            }

            var actual = GetLocalizedMessages(Loc.FromTag(localeName));
            Assert.Equal(activeMessages.Count, actual.Count);
            foreach (var (key, text) in activeMessages)
            {
                Assert.True(actual.TryGetValue(key, out var actualText), $"{localeName}: 缺少键 {key.Value}");
                Assert.Equal(text, actualText);
            }
        }
    }

    [Fact]
    public void TestLocaleFilesIgnoreStaleDiagnostics()
    {
        // "Removed_diagnostic_99999" 不在消息表里（keyToMessage 返回 nil）→ 跳过校验，不报错。
        ValidateLocalizedMessages(
            new Dictionary<string, string> { ["Removed_diagnostic_99999"] = "Stale translation." },
            "stale");
    }

    private static void ValidateLocalizedMessages(Dictionary<string, string> localizedMessages, string label)
    {
        foreach (var (key, localizedText) in localizedMessages)
        {
            var message = KeyToMessage(key);
            if (message is null)
            {
                continue;
            }

            var localizedPlaceholders = PlaceholderSet(localizedText);
            var englishPlaceholders = PlaceholderSet(message.ToString());
            Assert.True(
                localizedPlaceholders.Count == englishPlaceholders.Count,
                $"{label}: 占位符数量不一致（{key}）");
            foreach (var placeholder in englishPlaceholders)
            {
                Assert.True(
                    localizedPlaceholders.Contains(placeholder),
                    $"{label}: 本地化消息 {key} 缺少占位符 {placeholder}");
            }
        }
    }

    private static HashSet<string> PlaceholderSet(string text)
    {
        var result = new HashSet<string>(StringComparer.Ordinal);
        foreach (Match placeholder in _placeholderRegexp.Matches(text))
        {
            result.Add(placeholder.Value);
        }

        return result;
    }

    // 与 docs/architecture.md §5 一致：TSGO 仓库根目录由 TSGO_ROOT 指定，缺省探测 D:\Source\GitHub\TypeScript-Go。
    private static string TsgoRoot =>
        Environment.GetEnvironmentVariable("TSGO_ROOT") is { Length: > 0 } root
            ? root
            : @"D:\Source\GitHub\TypeScript-Go";
}
