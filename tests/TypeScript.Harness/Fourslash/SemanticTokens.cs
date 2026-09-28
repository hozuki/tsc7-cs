// ported from: internal/fourslash/semantictokens.go:1-130
//
// 偏离清单（逐条）：
// 1) Go 的 `*testing.T` → `IHarnessTest`（TypeScript.TestUtil）；`t.Helper()` 无对应物，删除。
//    `t.Fatalf(fmt, args...)` → `t.Fatal($"...")`；`t.Errorf` → `t.Error`（同样不终止，继续循环）。
// 2) Go 的 `panic(fmt.Sprintf(...))` → `TypeScript.Debug.Fail("...")` + `throw new System.Diagnostics.UnreachableException();`
//    （Debug.Fail 签名是 `void`，需要显式 throw 让控制流终止 / 满足可空性分析）。
// 3) Go 的包级函数 `decodeSemanticTokens` / `formatSemanticTokens` → 放在 `FourslashTest` 的 partial 里做
//    `private static` 方法。偏离 docs/architecture.md §2 的"包级函数进与 package 同名的静态类"：这两个函数要读
//    `FourslashTest` 的私有字段（`_scriptInfos` / `_activeFilename`），C# 的 `private` 只在同一个 class 内可见
//    （partial 跨文件可见，跨 class 不可见），放进 `Fourslash` 静态类就无法访问而未导出字段又不能放宽可见性，
//    故就近放在同一个 partial class 内。
// 4) `*T` → `T?`；`[]T`（nil 切片）→ `T[]?` + 使用处 `?? []`（Go 的 nil 切片在 len/range 下等价于空切片）：
//    形参 `expected`、`tokenTypes`、`tokenModifiers`；`result.SemanticTokens.Data` 在 C# 侧是 `uint[]?`
//    （JSON 可选字段）故 `?? []`。
// 5) Go 的 `f.scriptInfos[f.activeFilename]`（缺键得 nil 指针）→ C# 的 `Dictionary` 索引器（缺键抛
//    KeyNotFoundException）。两者都是"立刻失败"，后续 nil 解引用/异常都不会被测试逻辑观察到。
// 6) `scriptInfo.content[startOffset:endOffset]` → `_content[startOffset..endOffset]`；`ScriptInfo` 的
//    `content` / `lineMap` 两个未导出字段 → `_content` / `_lineMap`（本仓未导出字段命名规范）。
// 7) Go 的 `var char uint32` 的标识符 `char` 是 C# 关键字 → 改名 `chr`（形状不变）。
// 8) `fmt.Sprintf("...%d...")` → `$"..."` 插值；`strings.Join(modifiers, ".")` → `string.Join(".", modifiers)`；
//    `strings.Join(lines, "\n")` → `string.Join("\n", lines)`。
// 9) `%q`（= `strconv.Quote`）→ 本文件的私有静态方法 `GoQuoteSemanticToken` / `AppendEscapedRuneForGoQuote` /
//    `IsGoPrintRune`（方法与 Go 的 strconv.quote.go 同名逻辑；加 `SemanticToken`/`ForGoQuote`/`Rune` 后缀是为了
//    避免与其它切片可能放进同一个 partial class 的同名 helper 冲突）：按 rune 遍历，`"`/`\` 反斜杠转义、
//    Go `unicode.IsPrint` 的 rune 原样、其余按 `\a \b \f \n \r \t \v` / `\xHH` / `\uHHHH` / `\UHHHHHHHH`
//    （小写十六进制）。已知偏差：Go 的入参是 UTF-8 字节串，非法 UTF-8 会逐字节输出 `\xHH`；C# 的 string 是
//    UTF-16，孤立的代理项（unpaired surrogate）无法表达成 Go 的那种字节序列，这里按非可打印 rune 输出
//    `\uHHHH`。此外 .NET 的 Unicode 数据版本与 Go 的略有出入（只影响尚未分配码位的可打印性判定）。
// 10) `SemanticToken`（Go 导出类型）→ `public sealed class SemanticToken`，导出字段 `Type`/`Text` →
//     public 字段（与生成代码的 struct 字段风格一致）；Go 的零值 "" → 显式 `= ""`（Nullable=enable 下
//     未初始化引用字段是错误）。
//
// 依赖的共享契约（由 FsCore 的 FourslashTest.cs 提供）：
//   * `FourslashTest._semanticTokenTypes` / `_semanticTokenModifiers`（string[]）
//   * `FourslashTest._scriptInfos`（Dictionary<string, ScriptInfo>）、`FourslashTest._activeFilename`（string）
//   * `FourslashTest.SendRequest<Params, Resp>(IHarnessTest t, RequestInfo<Params, Resp> info, Params parameters) → Resp`
//   * `ScriptInfo`：`_content`（internal string）、`_lineMap`（internal LSPLineMap）
//   * `FourslashTest.NewTestConverters(Converters) → TestConverters`（Go 包级 `newTestConverters`）与
//     `TestConverters.LineAndCharacterToPosition(Script script, Position position) → TextPos`
//     （见 `local://fscore-freeze.md` 的接口冻结表）

using System.Collections.Generic;
using System.Globalization;
using System.Text;
using TypeScript.LsConv;
using TypeScript.LsProto;
using TypeScript.TestUtil;

namespace TypeScript.Harness.Fourslash;

/// <summary>Go: <c>type SemanticToken struct</c>（semantictokens.go:11-14）。</summary>
public sealed class SemanticToken
{
    public string Type = "";

    public string Text = "";
}

public sealed partial class FourslashTest
{
    /// <summary>
    /// Go: <c>func (f *FourslashTest) VerifySemanticTokens(t *testing.T, expected []SemanticToken)</c>（semantictokens.go:16-53）。
    /// </summary>
    public void VerifySemanticTokens(IHarnessTest t, SemanticToken[]? expected)
    {
        var expectedTokens = expected ?? [];

        var @params = new SemanticTokensParams
        {
            TextDocument = new TextDocumentIdentifier
            {
                Uri = LsConv.LsConv.FileNameToDocumentURI(_activeFilename),
            },
        };

        var result = SendRequest(t, LsProto.LsProto.TextDocumentSemanticTokensFullInfo, @params);

        if (result.SemanticTokens is null)
        {
            if (expectedTokens.Length == 0)
            {
                return;
            }

            t.Fatal("Expected semantic tokens but got nil");
        }

        // Decode the semantic tokens using token types/modifiers from the test configuration
        var actual = DecodeSemanticTokens(f: this, result.SemanticTokens.Data ?? [], _semanticTokenTypes, _semanticTokenModifiers);

        // Compare with expected
        if (actual.Length != expectedTokens.Length)
        {
            t.Fatal(
                $"Expected {expectedTokens.Length} semantic tokens, got {actual.Length}\n\n"
                + $"Expected:\n{FormatSemanticTokens(expectedTokens)}\n\n"
                + $"Actual:\n{FormatSemanticTokens(actual)}");
        }

        for (var i = 0; i < expectedTokens.Length; i++)
        {
            var exp = expectedTokens[i];
            var act = actual[i];
            if (exp.Type != act.Type || exp.Text != act.Text)
            {
                t.Error(
                    $"Token {i} mismatch:\n"
                    + $"  Expected: {{Type: {GoQuoteSemanticToken(exp.Type)}, Text: {GoQuoteSemanticToken(exp.Text)}}}\n"
                    + $"  Actual:   {{Type: {GoQuoteSemanticToken(act.Type)}, Text: {GoQuoteSemanticToken(act.Text)}}}");
            }
        }
    }

    /// <summary>
    /// Go: <c>func decodeSemanticTokens(f *FourslashTest, data []uint32, tokenTypes, tokenModifiers []string) []SemanticToken</c>
    /// （semantictokens.go:55-122）。
    /// </summary>
    private static SemanticToken[] DecodeSemanticTokens(FourslashTest f, uint[] data, string[]? tokenTypes, string[]? tokenModifiers)
    {
        var types = tokenTypes ?? [];
        var modifiersList = tokenModifiers ?? [];

        if (data.Length % 5 != 0)
        {
            Debug.Fail($"Invalid semantic tokens data length: {data.Length}");
            throw new System.Diagnostics.UnreachableException();
        }

        var scriptInfo = f._scriptInfos[f._activeFilename];
        var converters = NewTestConverters(LsConv.LsConv.NewConverters(PositionEncodingKind.UTF8, _ => scriptInfo._lineMap));

        var tokens = new List<SemanticToken>();
        uint prevLine = 0;
        uint prevChar = 0;

        for (var i = 0; i < data.Length; i += 5)
        {
            var deltaLine = data[i];
            var deltaChar = data[i + 1];
            var length = data[i + 2];
            var tokenTypeIdx = data[i + 3];
            var tokenModifierMask = data[i + 4];

            // Calculate absolute position
            var line = prevLine + deltaLine;
            uint chr;
            if (deltaLine == 0)
            {
                chr = prevChar + deltaChar;
            }
            else
            {
                chr = deltaChar;
            }

            // Get token type
            if ((int)tokenTypeIdx >= types.Length)
            {
                Debug.Fail($"Token type index out of range: {tokenTypeIdx}");
                throw new System.Diagnostics.UnreachableException();
            }

            var tokenType = types[tokenTypeIdx];

            // Get modifiers
            var modifiers = new List<string>();
            for (var modIndex = 0; modIndex < modifiersList.Length; modIndex++)
            {
                if ((tokenModifierMask & (1u << modIndex)) != 0)
                {
                    modifiers.Add(modifiersList[modIndex]);
                }
            }

            // Build full type string (type.modifier1.modifier2)
            var typeStr = tokenType;
            if (modifiers.Count > 0)
            {
                typeStr = typeStr + "." + string.Join(".", modifiers);
            }

            // Get the text
            var startPos = new Position { Line = line, Character = chr };
            var endPos = new Position { Line = line, Character = chr + length };
            var startOffset = (int)converters.LineAndCharacterToPosition(scriptInfo, startPos);
            var endOffset = (int)converters.LineAndCharacterToPosition(scriptInfo, endPos);
            var text = scriptInfo._content[startOffset..endOffset];

            tokens.Add(new SemanticToken
            {
                Type = typeStr,
                Text = text,
            });

            prevLine = line;
            prevChar = chr;
        }

        return tokens.ToArray();
    }

    /// <summary>Go: <c>func formatSemanticTokens(tokens []SemanticToken) string</c>（semantictokens.go:124-130）。</summary>
    private static string FormatSemanticTokens(SemanticToken[]? tokens)
    {
        var tokenList = tokens ?? [];
        var lines = new List<string>();
        for (var i = 0; i < tokenList.Length; i++)
        {
            var tok = tokenList[i];
            lines.Add($"  [{i}] {{Type: {GoQuoteSemanticToken(tok.Type)}, Text: {GoQuoteSemanticToken(tok.Text)}}}");
        }

        return string.Join("\n", lines);
    }

    /// <summary>
    /// Go 的 <c>strconv.Quote</c>（= <c>fmt</c> 的 <c>%q</c>）：首尾 <c>"</c>，<c>"</c>/<c>\</c> 反斜杠转义，
    /// <c>unicode.IsPrint</c> 的 rune 原样，其余按 <c>\a \b \f \n \r \t \v</c> / <c>\xHH</c> / <c>\uHHHH</c> / <c>\UHHHHHHHH</c>
    /// （小写十六进制）。
    /// </summary>
    private static string GoQuoteSemanticToken(string text)
    {
        var builder = new StringBuilder(text.Length + 2);
        builder.Append('"');
        var index = 0;
        while (index < text.Length)
        {
            int r;
            var width = 1;
            if (char.IsHighSurrogate(text[index]) && index + 1 < text.Length && char.IsLowSurrogate(text[index + 1]))
            {
                r = char.ConvertToUtf32(text[index], text[index + 1]);
                width = 2;
            }
            else
            {
                r = text[index];
            }

            AppendEscapedRuneForGoQuote(builder, r);
            index += width;
        }

        builder.Append('"');
        return builder.ToString();
    }

    /// <summary>Go: <c>strconv.appendEscapedRune</c>（<c>strconv</c> 的 quote.go；<c>quote</c> = <c>'"'</c>、非 ASCIIonly）。</summary>
    private static void AppendEscapedRuneForGoQuote(StringBuilder builder, int r)
    {
        if (r == '"' || r == '\\')
        {
            builder.Append('\\').Append((char)r);
            return;
        }

        if (IsGoPrintRune(r))
        {
            builder.Append(char.ConvertFromUtf32(r));
            return;
        }

        switch (r)
        {
            case '\a':
                builder.Append("\\a");
                return;
            case '\b':
                builder.Append("\\b");
                return;
            case '\f':
                builder.Append("\\f");
                return;
            case '\n':
                builder.Append("\\n");
                return;
            case '\r':
                builder.Append("\\r");
                return;
            case '\t':
                builder.Append("\\t");
                return;
            case '\v':
                builder.Append("\\v");
                return;
        }

        if (r < ' ' || r == 0x7F)
        {
            builder.Append("\\x").Append(r.ToString("x2", CultureInfo.InvariantCulture));
        }
        else if (r < 0x10000)
        {
            builder.Append("\\u").Append(r.ToString("x4", CultureInfo.InvariantCulture));
        }
        else
        {
            builder.Append("\\U").Append(r.ToString("x8", CultureInfo.InvariantCulture));
        }
    }

    /// <summary>
    /// Go 的 <c>unicode.IsPrint</c>：拉丁 1 段用属性表（<c>0x20</c>-<c>0x7E</c> 可打印、<c>0x7F</c>-<c>0xA0</c> 不可打印、
    /// <c>0xA1</c>-<c>0xFF</c> 可打印），其余按类别 L/M/N/P/S 判定。
    /// </summary>
    private static bool IsGoPrintRune(int r)
    {
        if (r < 0x80)
        {
            return r >= 0x20 && r != 0x7F;
        }

        if (r <= 0xA0)
        {
            return false;
        }

        if (r <= 0xFF)
        {
            return true;
        }

        var category = CharUnicodeInfo.GetUnicodeCategory(char.ConvertFromUtf32(r), 0);
        return category switch
        {
            UnicodeCategory.UppercaseLetter or UnicodeCategory.LowercaseLetter or UnicodeCategory.TitlecaseLetter
                or UnicodeCategory.ModifierLetter or UnicodeCategory.OtherLetter => true,
            UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark or UnicodeCategory.EnclosingMark => true,
            UnicodeCategory.DecimalDigitNumber or UnicodeCategory.LetterNumber or UnicodeCategory.OtherNumber => true,
            UnicodeCategory.ConnectorPunctuation or UnicodeCategory.DashPunctuation or UnicodeCategory.OpenPunctuation
                or UnicodeCategory.ClosePunctuation or UnicodeCategory.InitialQuotePunctuation
                or UnicodeCategory.FinalQuotePunctuation or UnicodeCategory.OtherPunctuation => true,
            UnicodeCategory.MathSymbol or UnicodeCategory.CurrencySymbol or UnicodeCategory.ModifierSymbol
                or UnicodeCategory.OtherSymbol => true,
            _ => false,
        };
    }
}
