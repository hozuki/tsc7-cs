// C# 侧新增的哨兵测试（scanner_test.go 只有 3 个用例，覆盖率远不够；这里按交付要求补齐"可观察契约"）。
// 每条断言的期望值都由两类取证之一得到：
//   1) 逐行对回 Go 源码（internal/scanner/scanner.go / regexp.go 的同一分支）；
//   2) 与真 tsc（TSGO 7.0.2）对拍 —— artifacts/scanner-diff 的 oracle 模式（20 个用例的 code+位置逐一相等），
//      以及 diagnostics 生成的 Message.Code()。
// 覆盖：ASCII/非 ASCII/astral 标识符、字符串转义与孤立代理项、模板字面量、注释与 trivia（CRLF/LS/PS）、
// 数字字面量（十进制/十六进制/二进制/八进制/大整数/分隔符）、正则字面量（含 \p{...} 与拼写建议）、
// Mark/Rewind 往返、TokenText 与 TokenValue 的差异、错误回调、以及 Scanner 的包级工具函数。
using TypeScript.Ast;
using TypeScript.Core;
using TypeScript.Diagnostics;
using TypeScript.Scanner;
using static TypeScript.Ast.Ast;
using static TypeScript.Core.Core;
using static TypeScript.Diagnostics.Diagnostics;
using static TypeScript.Scanner.Scanner;
using Xunit;

namespace TypeScript.Tests;

public class ScannerSentinelTests
{
    private sealed record ScanError(int Code, int Start, int Length, object?[] Arguments);

    private static (TypeScript.Scanner.Scanner Scanner, List<ScanError> Errors) NewTextScanner(string text, bool skipTrivia = true)
    {
        var s = NewScanner();
        var errors = new List<ScanError>();
        s.SetOnError((msg, start, length, args) => errors.Add(new ScanError(msg.Code(), start, length, args)));
        s.SetText(text);
        s.SetSkipTrivia(skipTrivia);
        return (s, errors);
    }

    // ───────────────────────────── 标识符：ASCII / 非 ASCII / astral / 转义 ─────────────────────────────

    public static TheoryData<string, Kind, string, int> IdentifierCases => new()
    {
        // 源码, 期望 kind, 期望 TokenValue, 期望 TokenEnd（UTF-16 代码单元）
        { "const abc123_$ = 1;", Kind.ConstKeyword, "const", 5 },
        { "変数 = 1;", Kind.Identifier, "変数", 2 },              // BMP 非 ASCII：2 个代码单元
        { "\uD835\uDC00x = 1;", Kind.Identifier, "\uD835\uDC00x", 3 }, // U+1D400（astral）：代理对 2 单元 + 'x'
        { "\uD835\uDC00 = 1;", Kind.Identifier, "\uD835\uDC00", 2 },    // 单个 astral 标识符
        { "\\u0061bc = 1;", Kind.Identifier, "abc", 8 },          // 转义 \u0061bc：TokenValue 解转义、TokenEnd 是源码长度（8 个代码单元）
        { "\uD835\uDC00\uD835\uDC01", Kind.Identifier, "\uD835\uDC00\uD835\uDC01", 4 },
    };

    [Theory]
    [MemberData(nameof(IdentifierCases))]
    public void ScanIdentifiers(string text, Kind expectedKind, string expectedValue, int expectedEnd)
    {
        var (s, errors) = NewTextScanner(text);
        Assert.Equal(expectedKind, s.Scan());
        Assert.Equal(expectedValue, s.TokenValue());
        Assert.Equal(expectedEnd, s.TokenEnd());
        Assert.Empty(errors);
    }

    [Fact]
    public void ScanEscapedIdentifierSetsUnicodeEscapeFlagAndKeepsSourceText()
    {
        var (s, _) = NewTextScanner("\\u0061bc = 1;");
        Assert.Equal(Kind.Identifier, s.Scan());
        Assert.Equal("abc", s.TokenValue());
        Assert.True(s.HasUnicodeEscape());
        Assert.Equal("\\u0061bc", s.TokenText());
        Assert.Equal(Kind.EqualsToken, s.Scan()); // 空白被跳过
    }

    [Fact]
    public void ScanJsxIdentifierAllowsDashes()
    {
        var (s, _) = NewTextScanner("a-b");
        Assert.Equal(Kind.Identifier, s.Scan());
        // Go: scanJsxIdentifier 复用 scanIdentifierParts(identifierVariantJSX) 把 `-b` 接到当前 token 上。
        Assert.Equal(Kind.Identifier, s.ScanJsxIdentifier());
        Assert.Equal("a-b", s.TokenValue());
        Assert.Equal("a-b", s.TokenText());
    }

    // ───────────────────────────── 字符串：转义、孤立代理项、astral ─────────────────────────────

    [Theory]
    [InlineData("\"hello\"", "hello", TokenFlags.None)]
    [InlineData("'hello'", "hello", TokenFlags.SingleQuote)]
    [InlineData("\"a\\nb\"", "a\nb", TokenFlags.None)]
    [InlineData("\"\\u{1F980}\"", "\U0001F980", TokenFlags.ExtendedUnicodeEscape)]
    [InlineData("\"\\uD83E\\uDD80\"", "\U0001F980", TokenFlags.UnicodeEscape)]
    [InlineData("\"\U0001F980\"", "\U0001F980", TokenFlags.None)]            // 源码里字面量 astral 字符
    public void ScanStrings(string text, string expectedValue, TokenFlags expectedFlags)
    {
        var (s, errors) = NewTextScanner(text);
        Assert.Equal(Kind.StringLiteral, s.Scan());
        Assert.Equal(expectedValue, s.TokenValue());
        Assert.Equal(expectedFlags, s.TokenFlags);
        Assert.Empty(errors);
    }

    [Fact]
    public void ScanStringPreservesLoneSurrogatesFromEscapes()
    {
        // 孤立代理项的期望值不能放进 [InlineData]：xunit 的用例参数要经序列化往返，孤立代理项会被替换掉
        // （表现为 Expected 变成 U+FFFD），所以这里用 Fact 就地比较。
        var (s, errors) = NewTextScanner("\"\\uD800\"");
        Assert.Equal(Kind.StringLiteral, s.Scan());
        Assert.Equal("\uD800", s.TokenValue());
        Assert.Equal(1, s.TokenValue().Length);
        Assert.Equal(TokenFlags.UnicodeEscape, s.TokenFlags);
        Assert.Empty(errors);

        var (s2, errors2) = NewTextScanner("\"\\u{D800}\"");
        Assert.Equal(Kind.StringLiteral, s2.Scan());
        Assert.Equal("\uD800", s2.TokenValue());
        Assert.Equal(1, s2.TokenValue().Length);
        Assert.Equal(TokenFlags.ExtendedUnicodeEscape, s2.TokenFlags);
        Assert.Empty(errors2);
    }

    [Fact]
    public void ScanStringPreservesLiteralLoneSurrogateFromSource()
    {
        // 源码里就是孤立代理项（C# string 可以直接持有）——Go 侧要靠 CESU-8 哨兵才能做到同一件事。
        var (s, errors) = NewTextScanner("\"\uD800\uDC00\uD801\"");
        Assert.Equal(Kind.StringLiteral, s.Scan());
        Assert.Equal("\uD800\uDC00\uD801", s.TokenValue()); // 代理对 + 孤立代理项，逐代码单元保留
        Assert.Equal(3, s.TokenValue().Length);
        Assert.Empty(errors);
    }

    [Fact]
    public void ScanStringReportsUnterminated()
    {
        var (s, errors) = NewTextScanner("\"abc");
        Assert.Equal(Kind.StringLiteral, s.Scan());
        Assert.Equal("abc", s.TokenValue());
        Assert.True((s.TokenFlags & TokenFlags.Unterminated) != 0);
        Assert.Equal(new[] { 1002 }, errors.Select(e => e.Code).ToArray()); // Unterminated_string_literal

        // 字符串里的真实换行同样算未终止（非 JSX 属性字符串）。
        var (s2, errors2) = NewTextScanner("\"a\nb\"");
        Assert.Equal(Kind.StringLiteral, s2.Scan());
        Assert.Equal("a", s2.TokenValue());
        Assert.Equal(new[] { 1002 }, errors2.Select(e => e.Code).ToArray());
    }

    [Fact]
    public void ScanStringInvalidEscapes()
    {
        // \x41 合法、\x7G 第二位不是十六进制数字 → ContainsInvalidEscape + 1125，值保留原文 "\\x7"
        var (s, errors) = NewTextScanner("\"\\x41\\x7G\"");
        Assert.Equal(Kind.StringLiteral, s.Scan());
        Assert.Equal("A\\x7G", s.TokenValue());
        Assert.True((s.TokenFlags & TokenFlags.HexEscape) != 0);
        Assert.True((s.TokenFlags & TokenFlags.ContainsInvalidEscape) != 0);
        Assert.Equal(new[] { 1125 }, errors.Select(e => e.Code).ToArray()); // Hexadecimal_digit_expected
        Assert.Equal(new[] { 8 }, errors.Select(e => e.Start).ToArray());
    }

    [Fact]
    public void ScanStringLineSeparatorIsNotAStringTerminator()
    {
        // Go 的 scanString 只对 '\n'/'\r' 报未终止；U+2028 只是普通字符（逐行对应 Go 的字节级判断）。
        var (s, errors) = NewTextScanner("'a\u2028b'");
        Assert.Equal(Kind.StringLiteral, s.Scan());
        Assert.Equal("a\u2028b", s.TokenValue());
        Assert.Equal(TokenFlags.SingleQuote, s.TokenFlags);
        Assert.Empty(errors);
    }

    // ───────────────────────────── 模板字面量 ─────────────────────────────

    [Fact]
    public void ScanTemplateParts()
    {
        var (s, errors) = NewTextScanner("`a${b}c`");
        Assert.Equal(Kind.TemplateHead, s.Scan());
        Assert.Equal("a", s.TokenValue());
        Assert.Equal(Kind.Identifier, s.Scan());
        Assert.Equal("b", s.TokenValue());
        Assert.Equal(Kind.CloseBraceToken, s.Scan());
        // Go: parser 在 '}' 处调 reScanTemplateToken(false)（= 允许报非法转义错误）→ TemplateTail。
        Assert.Equal(Kind.TemplateTail, s.ReScanTemplateToken(false /*isTaggedTemplate*/));
        Assert.Equal("c", s.TokenValue());
        Assert.Equal(Kind.EndOfFile, s.Scan());
        Assert.Empty(errors);

        var (s2, _) = NewTextScanner("`abc`");
        Assert.Equal(Kind.NoSubstitutionTemplateLiteral, s2.Scan());
        Assert.Equal("abc", s2.TokenValue());
    }

    [Fact]
    public void ScanTemplateNormalizesCrlfAndReportsUnterminated()
    {
        // <CR><LF> → <LF>（ES2015 11.8.6.1）
        var (s, _) = NewTextScanner("`a\r\nb`");
        Assert.Equal(Kind.NoSubstitutionTemplateLiteral, s.Scan());
        Assert.Equal("a\nb", s.TokenValue());

        var (s2, errors) = NewTextScanner("`abc");
        Assert.Equal(Kind.NoSubstitutionTemplateLiteral, s2.Scan());
        Assert.Equal("abc", s2.TokenValue());
        Assert.True((s2.TokenFlags & TokenFlags.Unterminated) != 0);
        Assert.Equal(new[] { 1160 }, errors.Select(e => e.Code).ToArray()); // Unterminated_template_literal
    }

    [Fact]
    public void ScanTemplateInvalidEscapeIsReportedOnReScanOnly()
    {
        // Scan() 的 '`' 分支用 shouldEmitInvalidEscapeError=false（照 Go scanner.go）；parser 对非 tagged
        // 模板会调 reScanTemplateToken(false)（internal/parser/parser.go:3745），那时才报 1125。
        // 真 tsc 对 `var s = `\u`;` 报 TS1125，位置就是转义处 —— 由 oracle 用例覆盖。
        var (s, errors) = NewTextScanner("`\\u`");
        Assert.Equal(Kind.NoSubstitutionTemplateLiteral, s.Scan());
        Assert.Equal("\\u", s.TokenValue());
        Assert.True((s.TokenFlags & TokenFlags.ContainsInvalidEscape) != 0);
        Assert.Empty(errors);

        Assert.Equal(Kind.NoSubstitutionTemplateLiteral, s.ReScanTemplateToken(false /*isTaggedTemplate*/));
        Assert.Equal(new[] { 1125 }, errors.Select(e => e.Code).ToArray());
        Assert.Equal(new[] { 3 }, errors.Select(e => e.Start).ToArray()); // 转义结束后 s.pos 落在这个反引号上（oracle 用例 tpl_invalid_escape 与 tsc 的列号一致）
    }

    [Fact]
    public void ScanTemplateTaggedSuppressesInvalidEscapeErrorButKeepsFlag()
    {
        var (s, errors) = NewTextScanner("tag`\\u`");
        Assert.Equal(Kind.Identifier, s.Scan());
        Assert.Equal(Kind.NoSubstitutionTemplateLiteral, s.Scan());
        Assert.Equal("\\u", s.TokenValue());

        // tagged 模板：reScanTemplateToken(true) → shouldEmitInvalidEscapeError=false → 不报错，只置 flags。
        Assert.Equal(Kind.NoSubstitutionTemplateLiteral, s.ReScanTemplateToken(true /*isTaggedTemplate*/));
        Assert.Empty(errors);
        Assert.True((s.TokenFlags & TokenFlags.ContainsInvalidEscape) != 0);
        Assert.True((s.TokenFlags & TokenFlags.UnicodeEscape) != 0);
    }

    // ───────────────────────────── 注释与 trivia（CRLF / LS / PS） ─────────────────────────────

    [Fact]
    public void ScanTriviaWithSkipTriviaDisabled()
    {
        var (s, errors) = NewTextScanner("//a\nb", skipTrivia: false);
        Assert.Equal(Kind.SingleLineCommentTrivia, s.Scan());
        Assert.Equal("//a", s.TokenText());
        Assert.Equal(Kind.NewLineTrivia, s.Scan());
        Assert.Equal("\n", s.TokenText());
        Assert.Equal(Kind.Identifier, s.Scan());
        Assert.Equal("b", s.TokenValue());
        Assert.Empty(errors);
    }

    [Fact]
    public void ScanMultiLineCommentSetsPrecedingLineBreak()
    {
        var (s, _) = NewTextScanner("/*a\r\nb*/x", skipTrivia: false);
        Assert.Equal(Kind.MultiLineCommentTrivia, s.Scan());
        Assert.Equal("/*a\r\nb*/", s.TokenText());
        Assert.True((s.TokenFlags & TokenFlags.PrecedingLineBreak) != 0);
        Assert.Equal(Kind.Identifier, s.Scan());
        Assert.Equal("x", s.TokenValue());
    }

    [Fact]
    public void ScanJSDocCommentSetsFlags()
    {
        var (s, _) = NewTextScanner("/** @deprecated */x");
        Assert.Equal(Kind.Identifier, s.Scan());
        Assert.True(s.HasPrecedingJSDocComment());
        Assert.True(s.HasPrecedingJSDocWithDeprecatedTag());
        Assert.False(s.HasPrecedingJSDocWithSeeOrLink());
    }

    [Theory]
    [InlineData("a\r\nb", 3)]     // CRLF 是 2 个代码单元
    [InlineData("a\rb", 2)]       // CR
    [InlineData("a\nb", 2)]       // LF
    [InlineData("a\u2028b", 2)]   // LS
    [InlineData("a\u2029b", 2)]   // PS
    public void ScanLineBreaksSetPrecedingLineBreak(string text, int expectedStart)
    {
        var (s, _) = NewTextScanner(text);
        Assert.Equal(Kind.Identifier, s.Scan());
        Assert.Equal("a", s.TokenValue());
        Assert.Equal(Kind.Identifier, s.Scan());
        Assert.Equal("b", s.TokenValue());
        Assert.True(s.HasPrecedingLineBreak());
        Assert.Equal(expectedStart, s.TokenStart());
    }

    [Fact]
    public void ScanShebangIsTriviaAtStartOfFile()
    {
        Assert.Equal("#!shebang", GetShebang("#!shebang\nx"));
        Assert.Equal("", GetShebang("x"));

        var (s, errors) = NewTextScanner("#!shebang\nx");
        Assert.Equal(Kind.Identifier, s.Scan());
        Assert.Equal("x", s.TokenValue());
        Assert.Empty(errors);
    }

    [Fact]
    public void ScanConflictMarkerTrivia()
    {
        Assert.True(IsConflictMarkerTrivia("<<<<<<< HEAD", 0));
        Assert.False(IsConflictMarkerTrivia("<<<<<<<HEAD", 0)); // 七个之后必须是空格或 '='
        Assert.False(IsConflictMarkerTrivia("x\n<<<<<<<y", 2));

        // Go 的"行首"判断看的是 text[pos-1] 这**一个字节**（`IsLineBreak(rune(...))`），所以 U+2028/U+2029
        // 这种多字节行分隔符不算行首（只有 LF/CR 算）—— 与 Go 逐字对应，见 IsLineBreakByte 的注释。
        Assert.True(IsConflictMarkerTrivia("/**/\n<<<<<<< HEAD", 5));
        Assert.False(IsConflictMarkerTrivia("/**/\u2028<<<<<<< HEAD", 5));

        var (s, errors) = NewTextScanner("<<<<<<< HEAD\nvar x = 1;\n", skipTrivia: false);
        Assert.Equal(Kind.ConflictMarkerTrivia, s.Scan());
        Assert.Equal("<<<<<<< HEAD", s.TokenText());
        Assert.Equal(new[] { 1185 }, errors.Select(e => e.Code).ToArray()); // Merge_conflict_marker_encountered
        Assert.Equal(new[] { 0 }, errors.Select(e => e.Start).ToArray());
    }

    // ───────────────────────────── 数字字面量 ─────────────────────────────

    public static TheoryData<string, Kind, string, TokenFlags> NumberCases => new()
    {
        { "123;", Kind.NumericLiteral, "123", TokenFlags.None },
        { "1_000;", Kind.NumericLiteral, "1000", TokenFlags.ContainsSeparator },
        { "0x1F;", Kind.NumericLiteral, "31", TokenFlags.HexSpecifier },
        { "0X1f;", Kind.NumericLiteral, "31", TokenFlags.HexSpecifier },
        { "0b1010;", Kind.NumericLiteral, "10", TokenFlags.BinarySpecifier },
        { "0o17;", Kind.NumericLiteral, "15", TokenFlags.OctalSpecifier },
        { "0o17n;", Kind.BigIntLiteral, "15n", TokenFlags.OctalSpecifier },
        { "123n;", Kind.BigIntLiteral, "123n", TokenFlags.None },
        { "1.5e3;", Kind.NumericLiteral, "1500", TokenFlags.Scientific },
        { ".5;", Kind.NumericLiteral, "0.5", TokenFlags.None },
        { "1e;", Kind.NumericLiteral, "1", TokenFlags.Scientific },  // Digit_expected，值取源码片段
    };

    [Theory]
    [MemberData(nameof(NumberCases))]
    public void ScanNumbers(string text, Kind expectedKind, string expectedValue, TokenFlags expectedFlags)
    {
        var (s, _) = NewTextScanner(text);
        Assert.Equal(expectedKind, s.Scan());
        Assert.Equal(expectedValue, s.TokenValue());
        Assert.Equal(expectedFlags, s.TokenFlags);
        Assert.Equal(text.IndexOf(';'), s.TokenEnd());
    }

    [Fact]
    public void ScanLegacyOctalAndLeadingZeroNumbers()
    {
        // 0777：legacy octal → 值是十进制 "511"，并带 Octal 标志 + 1121 诊断（参数是 "0o777"）
        var (s, errors) = NewTextScanner("0777;");
        Assert.Equal(Kind.NumericLiteral, s.Scan());
        Assert.Equal("511", s.TokenValue());
        Assert.True((s.TokenFlags & TokenFlags.Octal) != 0);
        Assert.Equal(new[] { 1121 }, errors.Select(e => e.Code).ToArray());
        Assert.Equal(new[] { 0 }, errors.Select(e => e.Start).ToArray());
        Assert.Equal("0o777", Assert.Single(errors).Arguments[0] as string);

        // 08：不是 legacy octal，但前导零仍非法 → 1489，值按 JS 数值归一化成 "8"
        var (s2, errors2) = NewTextScanner("08;");
        Assert.Equal(Kind.NumericLiteral, s2.Scan());
        Assert.Equal("8", s2.TokenValue());
        Assert.True((s2.TokenFlags & TokenFlags.ContainsLeadingZero) != 0);
        Assert.Equal(new[] { 1489 }, errors2.Select(e => e.Code).ToArray());
    }

    [Theory]
    [InlineData("1_;", 6188)]  // 尾部分隔符：Numeric_separators_are_not_allowed_here
    [InlineData("0_1;", 6188)] // 数字紧跟在 0 后面的分隔符
    [InlineData("0x;", 1125)]  // Hexadecimal_digit_expected
    public void ScanNumbersReportErrors(string text, int expectedCode)
    {
        var (s, errors) = NewTextScanner(text);
        Assert.Equal(Kind.NumericLiteral, s.Scan());
        Assert.Contains(expectedCode, errors.Select(e => e.Code));
        Assert.True((s.TokenFlags & TokenFlags.ContainsSeparator) != 0 || (s.TokenFlags & TokenFlags.HexSpecifier) != 0);
    }

    // ───────────────────────────── 正则字面量（含 \p{...} 与拼写建议） ─────────────────────────────

    private static (Kind Kind, string Value, List<ScanError> Errors) ReScan(string text)
    {
        var (s, errors) = NewTextScanner(text);
        var kind = s.Scan();
        Assert.Equal(Kind.SlashToken, kind);
        kind = s.ReScanSlashToken(shouldReportErrors: true);
        return (kind, s.TokenValue(), errors);
    }

    [Fact]
    public void ReScanSlashTokenScansSimpleRegex()
    {
        var (kind, value, errors) = ReScan("/abc/gi");
        Assert.Equal(Kind.RegularExpressionLiteral, kind);
        Assert.Equal("/abc/gi", value);
        Assert.Empty(errors);
    }

    [Theory]
    [InlineData("/abc/gg", 1500)]   // Duplicate_regular_expression_flag
    [InlineData("/abc/z", 1499)]    // Unknown_regular_expression_flag
    [InlineData("/abc/uv", 1502)]   // u 与 v 不能同时设置
    [InlineData("/[z-a]/", 1517)]   // Range_out_of_order_in_character_class
    public void ReScanSlashTokenReportsFlagAndRangeErrors(string text, int expectedCode)
    {
        var (kind, _, errors) = ReScan(text);
        Assert.Equal(Kind.RegularExpressionLiteral, kind);
        Assert.Contains(expectedCode, errors.Select(e => e.Code));
    }

    [Fact]
    public void ReScanSlashTokenReportsUnterminated()
    {
        var (kind, value, errors) = ReScan("/[/");
        Assert.Equal(Kind.RegularExpressionLiteral, kind);
        Assert.Equal("/[/", value);
        Assert.Equal(new[] { 1161 }, errors.Select(e => e.Code).ToArray()); // Unterminated_regular_expression_literal
    }

    [Fact]
    public void ReScanSlashTokenUnicodeProperties()
    {
        // 合法属性 → 无诊断
        var (kind, value, errors) = ReScan("/\\p{Script=Greek}/u");
        Assert.Equal(Kind.RegularExpressionLiteral, kind);
        Assert.Equal("/\\p{Script=Greek}/u", value);
        Assert.Empty(errors);

        var (_, _, errors2) = ReScan("/\\p{Lu}/u");
        Assert.Empty(errors2);

        // 未知属性名/值 → 1529（名字或值）+ 1369（拼写建议 "Greek"）
        var (_, _, errors3) = ReScan("/\\p{Nope}/u");
        Assert.Contains(1529, errors3.Select(e => e.Code));

        var (_, _, errors4) = ReScan("/\\p{Script=Greel}/u");
        Assert.Equal(new[] { 1526, 1369 }, errors4.Select(e => e.Code).ToArray());
        Assert.Equal("Greek", errors4[1].Arguments[0] as string);
    }

    [Fact]
    public void ReScanSlashTokenTreatsOnlyLfCrAsLineTerminators()
    {
        // Go 的这一遍是**逐字节**扫的（`ch := rune(s.text[p])`），所以 U+2028/U+2029 不会终止正则字面量；
        // 真 tsc 对 `/a<U+2028>b/` 也不报错（oracle 用例 regex_ls_in_body / regex_ps_in_body）。
        var (kind, value, errors) = ReScan("/a\u2028b/");
        Assert.Equal(Kind.RegularExpressionLiteral, kind);
        Assert.Equal("/a\u2028b/", value);
        Assert.Empty(errors);

        var (kind2, _, errors2) = ReScan("/a\u2029b/");
        Assert.Equal(Kind.RegularExpressionLiteral, kind2);
        Assert.Empty(errors2);

        // 真正的换行仍然是未终止 + 1161
        var (kind3, _, errors3) = ReScan("/a\nb/");
        Assert.Equal(Kind.RegularExpressionLiteral, kind3);
        Assert.Equal(new[] { 1161 }, errors3.Select(e => e.Code).ToArray());
    }

    [Fact]
    public void ReScanSlashTokenNamedGroupReferences()
    {
        var (kind, _, errors) = ReScan("/(?<n>a)\\k<n>/");
        Assert.Equal(Kind.RegularExpressionLiteral, kind);
        Assert.Empty(errors);

        var (_, _, errors2) = ReScan("/(?<n>a)\\k<m>/");
        Assert.Equal(new[] { 1532 }, errors2.Select(e => e.Code).ToArray());
        Assert.Equal("m", errors2[0].Arguments[0] as string);
    }

    // ───────────────────────────── Mark / Rewind ─────────────────────────────

    [Fact]
    public void MarkRewindRestoresTokenState()
    {
        var (s, _) = NewTextScanner("a b c");
        Assert.Equal(Kind.Identifier, s.Scan());
        Assert.Equal("a", s.TokenValue());
        var mark = s.Mark();

        Assert.Equal(Kind.Identifier, s.Scan());
        Assert.Equal("b", s.TokenValue());
        Assert.Equal(Kind.Identifier, s.Scan());
        Assert.Equal("c", s.TokenValue());

        s.Rewind(mark);
        Assert.Equal(Kind.Identifier, s.Token());
        Assert.Equal("a", s.TokenValue());
        Assert.Equal(0, s.TokenStart());

        Assert.Equal(Kind.Identifier, s.Scan());
        Assert.Equal("b", s.TokenValue());
    }

    [Fact]
    public void MarkRewindDoesNotDuplicateCommentDirectives()
    {
        var (s, errors) = NewTextScanner("// @ts-ignore\nx", skipTrivia: false);
        Assert.Equal(Kind.SingleLineCommentTrivia, s.Scan());
        var directives = s.CommentDirectives();
        Assert.Single(directives);
        Assert.Equal(CommentDirectiveKind.Ignore, directives[0].Kind);
        Assert.Equal(0, directives[0].Loc.Pos());
        Assert.Equal(13, directives[0].Loc.End());

        var mark = s.Mark();
        Assert.Equal(Kind.NewLineTrivia, s.Scan());
        Assert.Equal(Kind.Identifier, s.Scan());
        s.Rewind(mark);

        // Go 的 Rewind 恢复切片长度 → 指令不会因为回退重扫而重复。
        Assert.Single(s.CommentDirectives());
        Assert.Equal(Kind.NewLineTrivia, s.Scan());
        Assert.Empty(errors);
    }

    [Fact]
    public void CommentDirectivesAreCollectedWhileSkippingTrivia()
    {
        var (s, _) = NewTextScanner("// @ts-expect-error\n/* @ts-ignore */\nx");
        Assert.Equal(Kind.Identifier, s.Scan());
        Assert.Equal("x", s.TokenValue());
        var directives = s.CommentDirectives();
        Assert.Equal(2, directives.Length);
        Assert.Equal(CommentDirectiveKind.ExpectError, directives[0].Kind);
        Assert.Equal(CommentDirectiveKind.Ignore, directives[1].Kind);
    }

    // ───────────────────────────── TokenText 与 TokenValue 的差异 ─────────────────────────────

    [Fact]
    public void TokenTextIsSourceSliceWhileTokenValueIsDecoded()
    {
        // 字面量 astral：TokenText 是源码切片（含引号，4 个代码单元），TokenValue 是解出的值（2 个代码单元）
        var (s, _) = NewTextScanner("'\U0001F980'");
        Assert.Equal(Kind.StringLiteral, s.Scan());
        Assert.Equal(4, s.TokenText().Length);
        Assert.Equal(2, s.TokenValue().Length);
        Assert.Equal("'\U0001F980'", s.TokenText());

        // 转义成对：TokenText 是 12 个代码单元的源码，TokenValue 是合并后的 astral 字符（2 个代码单元）
        var (s2, _) = NewTextScanner("'\\uD83E\\uDD80'");
        Assert.Equal(Kind.StringLiteral, s2.Scan());
        Assert.Equal(14, s2.TokenText().Length);
        Assert.Equal(2, s2.TokenValue().Length);
        Assert.Equal("\U0001F980", s2.TokenValue());

        // 孤立代理项：源码里就是它 → TokenText 3 个代码单元、TokenValue 1 个代码单元且逐位相等
        var (s3, _) = NewTextScanner("'\uD800'");
        Assert.Equal(Kind.StringLiteral, s3.Scan());
        Assert.Equal(3, s3.TokenText().Length);
        Assert.Equal("\uD800", s3.TokenValue());
        Assert.Equal(1, s3.TokenValue().Length);
    }

    // ───────────────────────────── 错误回调 ─────────────────────────────

    [Fact]
    public void ErrorCallbackReceivesMessageCodePositionsAndArgs()
    {
        var (s, errors) = NewTextScanner("0777;");
        s.Scan();
        var error = Assert.Single(errors);
        Assert.Equal(1121, error.Code);
        Assert.Equal(0, error.Start);
        Assert.Equal(4, error.Length);
        Assert.Equal("0o777", error.Arguments[0]);
    }

    [Fact]
    public void ScannerDoesNotThrowWithoutErrorCallback()
    {
        var s = NewScanner();
        s.SetText("0777; /* unterminated");
        Assert.Equal(Kind.NumericLiteral, s.Scan());
        Assert.Equal(Kind.SemicolonToken, s.Scan());
        Assert.Equal(Kind.EndOfFile, s.Scan()); // 没有回调时错误被静默丢弃（Go 的 onError == nil 分支）
    }

    [Fact]
    public void UnsetErrorCallbackRecordsNothingButScannerKeepsGoing()
    {
        var (s, errors) = NewTextScanner("\"abc");
        Assert.Equal(Kind.StringLiteral, s.Scan());
        Assert.Single(errors);

        s.SetOnError(null);
        s.SetText("\"abc");
        Assert.Equal(Kind.StringLiteral, s.Scan());
        Assert.Single(errors); // 没有新增（回调已置空）
    }

    // ───────────────────────────── 包级工具函数 ─────────────────────────────

    [Fact]
    public void IdentifierAndTokenHelpers()
    {
        Assert.Equal(Kind.IfKeyword, GetIdentifierToken("if"));
        Assert.Equal(Kind.Identifier, GetIdentifierToken("iff"));
        Assert.Equal(Kind.Identifier, GetIdentifierToken("a"));
        Assert.Equal(Kind.Identifier, GetIdentifierToken("identifiertoolong"));

        Assert.Equal("if", TokenToString(Kind.IfKeyword));
        Assert.Equal("...", TokenToString(Kind.DotDotDotToken));
        Assert.Equal("", TokenToString(Kind.NumericLiteral)); // 非文本 token 在 Go 的 tokenToText 里是零值 ""
        Assert.Equal(Kind.IfKeyword, StringToToken("if"));
        Assert.Equal(Kind.QuestionQuestionToken, StringToToken("??"));
        Assert.Equal(Kind.Unknown, StringToToken("nope"));

        Assert.True(GetViableKeywordSuggestions().Contains("instanceof"));
        Assert.DoesNotContain("as", GetViableKeywordSuggestions()); // Go: len(text) > 2
    }

    [Fact]
    public void IdentifierPredicates()
    {
        Assert.True(IsIdentifierStart('a'));
        Assert.True(IsIdentifierStart('_'));
        Assert.True(IsIdentifierStart('$'));
        Assert.True(IsIdentifierStart(0x1D400));   // U+1D400（astral，Lu）
        Assert.True(IsIdentifierPart(0x0301));     // combining acute
        Assert.False(IsIdentifierStart('1'));
        Assert.False(IsIdentifierStart('-'));
        Assert.False(IsIdentifierPartEx('-', LanguageVariant.Standard));
        Assert.True(IsIdentifierPartEx('-', LanguageVariant.JSX)); // JSX 标识符允许 '-'

        Assert.True(IsValidIdentifier("abc"));
        Assert.True(IsValidIdentifier("$_\uD835\uDC00"));
        Assert.False(IsValidIdentifier(""));
        Assert.False(IsValidIdentifier("1a"));
        Assert.False(IsValidIdentifier("a b"));
    }

    [Fact]
    public void TriviaHelpers()
    {
        Assert.True(CouldStartTrivia(" x", 0));
        Assert.True(CouldStartTrivia("/*", 0));
        Assert.True(CouldStartTrivia("\u00A0x", 0)); // > maxAsciiCharacter
        Assert.False(CouldStartTrivia("x", 0));
        Assert.True(CouldStartTrivia("#!", 0));
        Assert.False(CouldStartTrivia("a#!", 1));

        Assert.True(IsShebangTrivia("#!", 0));
        Assert.False(IsShebangTrivia("#", 0));

        // SkipTrivia / SkipTriviaEx（含 StopAtComments / StopAfterLineBreak / InJSDoc）
        Assert.Equal(8, SkipTrivia("  // c\n x", 0)); // 跳过两个空格、注释、换行、一个空格 → 'x' 的下标
        Assert.Equal(2, SkipTriviaEx("  // c\n x", 0, new SkipTriviaOptions { StopAtComments = true })); // 停在注释处
        Assert.Equal(7, SkipTriviaEx("  // c\n x", 0, new SkipTriviaOptions { StopAfterLineBreak = true })); // 停在换行之后
        Assert.Equal(2, SkipTriviaEx(" \u00A0x", 0, null));
        Assert.Equal(-1, SkipTriviaEx("x", -1, null)); // Go: PositionIsSynthesized(pos) → 原样返回 pos
    }

    // ───────────────────────────── 依赖 SourceFile 的辅助函数 ─────────────────────────────

    private static SourceFile CreateSourceFile(string text) =>
        NewNodeFactory(new NodeFactoryHooks())
            .NewSourceFile(
                new SourceFileParseOptions { FileName = "/src/file.ts", Path = new TSPath.Path("/src/file.ts") },
                text,
                null,
                null)
            .AsSourceFile()!;

    [Fact]
    public void TokenPositionHelpers()
    {
        var file = CreateSourceFile("  var x = 1;");
        var identifier = new Identifier { Kind = Kind.Identifier, Text = "x", Loc = new TextRange(6, 7) };

        Assert.Equal(6, GetTokenPosOfNode(identifier, file, false));
        Assert.Equal(new TextRange(6, 7), GetRangeOfTokenAtPosition(file, 6));
        Assert.Equal(Kind.Identifier, ScanTokenAtPosition(file, 6));
        Assert.Equal(new TextRange(6, 7), GetErrorRangeForNode(file, identifier));

        // SourceFile 的 Kind 分支：跳过 trivia 后取第一个 token 的范围
        Assert.Equal(new TextRange(2, 5), GetErrorRangeForNode(file, file));
    }

    [Fact]
    public void LineMapHelpers()
    {
        // Go: core.ComputeECMALineStarts("a\nb\r\nc") -> [0, 2, 5]
        TextPos[] lineStarts = [0, 2, 5];
        Assert.Equal(0, ComputeLineOfPosition(lineStarts, 1));
        Assert.Equal(1, ComputeLineOfPosition(lineStarts, 3)); // 落在第 1 行内部
        Assert.Equal(2, ComputeLineOfPosition(lineStarts, 5)); // 正好是第 2 行行首
        Assert.Equal(2, ComputeLineOfPosition(lineStarts, 9)); // 超出末尾 → 最后一行
        Assert.Equal(3, ComputePositionOfLineAndByteOffset(lineStarts, 1, 1));

        var file = CreateSourceFile("a\nb\r\nc");
        Assert.Equal([0, 2, 5], Array.ConvertAll(GetECMALineStarts(file), static p => p.Value));
        Assert.Equal(1, GetECMALineOfPosition(file, 3));
        Assert.Equal((1, new UTF16Offset(1)), GetECMALineAndUTF16CharacterOfPosition(file, 3));
        Assert.Equal((1, 1), GetECMALineAndByteOffsetOfPosition(file, 3));
        Assert.Equal(3, GetECMAPositionOfLineAndUTF16Character(file, 1, new UTF16Offset(1)));
        Assert.Equal(3, GetECMAPositionOfLineAndByteOffset(file, 1, 1));
        Assert.Equal(0, GetECMAEndLinePosition(file, 0)); // 第 0 行 "a" 的最后一个字符下标
        Assert.Equal(2, ComputePositionOfLineAndUTF16Character(lineStarts, 0, new UTF16Offset(5), "a\nb\r\nc", true)); // 夹到行尾
    }

    [Fact]
    public void CommentRangeHelpers()
    {
        var factory = NewNodeFactory(new NodeFactoryHooks());
        var text = "// a\n// b\nx";

        var leading = GetLeadingCommentRanges(factory, text, 0).ToList();
        Assert.Equal(2, leading.Count);
        Assert.Equal(Kind.SingleLineCommentTrivia, leading[0].Kind);
        Assert.Equal(new TextRange(0, 4), leading[0].Range);
        Assert.True(leading[0].HasTrailingNewLine);
        Assert.Equal(new TextRange(5, 9), leading[1].Range);

        // trailing=true：位置在换行处 → 立即结束，不产出注释
        Assert.Empty(GetTrailingCommentRanges(factory, text, 4));

        // 块注释：范围包含 /* 与 */（Go 的 iterateCommentRanges）
        var blockText = "/* a */ x";
        var blockRanges = GetLeadingCommentRanges(factory, blockText, 0).ToList();
        var block = Assert.Single(blockRanges);
        Assert.Equal(Kind.MultiLineCommentTrivia, block.Kind);
        Assert.Equal(new TextRange(0, 7), block.Range);
        Assert.False(block.HasTrailingNewLine);
    }
}
