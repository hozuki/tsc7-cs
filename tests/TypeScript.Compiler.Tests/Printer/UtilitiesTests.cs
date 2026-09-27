// ported from: internal/printer/utilities_test.go
//
// Go 的表驱动测试 → xunit 的 [Theory]/[MemberData]（docs §5）。
// 说明：Go 测试里 `escapeNonAsciiString` / `escapeJsxAttributeString` 是未导出函数，C# 侧同名 internal 成员，
// 由 Properties/AssemblyInfo.cs 的 InternalsVisibleTo 保证测试可见（与其它 package 的测试一致）。
using TypeScript.Ast;
using TypeScript.Core;
using static TypeScript.Core.Core;
using TypeScript.Printer;
using Xunit;

namespace TypeScript.Tests.Printer;

public class UtilitiesTests
{
    public static TheoryData<string, QuoteChar, string> EscapeStringData => new()
    {
        { "", QuoteChar.DoubleQuote, "" },
        { "abc", QuoteChar.DoubleQuote, "abc" },
        { "ab\"c", QuoteChar.DoubleQuote, "ab\\\"c" },
        { "ab\tc", QuoteChar.DoubleQuote, "ab\\tc" },
        { "ab\nc", QuoteChar.DoubleQuote, "ab\\nc" },
        { "ab'c", QuoteChar.DoubleQuote, "ab'c" },
        { "ab'c", QuoteChar.SingleQuote, "ab\\'c" },
        { "ab\"c", QuoteChar.SingleQuote, "ab\"c" },
        { "ab`c", QuoteChar.Backtick, "ab\\`c" },
        { "\u001f", QuoteChar.Backtick, "\\u001F" },
    };

    [Theory]
    [MemberData(nameof(EscapeStringData))]
    public void EscapeString(string s, QuoteChar quoteChar, string expected)
    {
        var actual = TypeScript.Printer.Printer.EscapeString(s, quoteChar);
        Assert.Equal(expected, actual);
    }

    public static TheoryData<string, QuoteChar, string> EscapeNonAsciiStringData => new()
    {
        { "", QuoteChar.DoubleQuote, "" },
        { "abc", QuoteChar.DoubleQuote, "abc" },
        { "ab\"c", QuoteChar.DoubleQuote, "ab\\\"c" },
        { "ab\tc", QuoteChar.DoubleQuote, "ab\\tc" },
        { "ab\nc", QuoteChar.DoubleQuote, "ab\\nc" },
        { "ab'c", QuoteChar.DoubleQuote, "ab'c" },
        { "ab'c", QuoteChar.SingleQuote, "ab\\'c" },
        { "ab\"c", QuoteChar.SingleQuote, "ab\"c" },
        { "ab`c", QuoteChar.Backtick, "ab\\`c" },
        { "ab\u008fc", QuoteChar.DoubleQuote, "ab\\u008Fc" },
        { "𝟘𝟙", QuoteChar.DoubleQuote, "\\uD835\\uDFD8\\uD835\\uDFD9" },
    };

    [Theory]
    [MemberData(nameof(EscapeNonAsciiStringData))]
    public void EscapeNonAsciiString(string s, QuoteChar quoteChar, string expected)
    {
        var actual = TypeScript.Printer.Printer.EscapeNonAsciiString(s, quoteChar);
        Assert.Equal(expected, actual);
    }

    public static TheoryData<string, QuoteChar, string> EscapeJsxAttributeStringData => new()
    {
        { "", QuoteChar.DoubleQuote, "" },
        { "abc", QuoteChar.DoubleQuote, "abc" },
        { "ab\"c", QuoteChar.DoubleQuote, "ab&quot;c" },
        { "ab\tc", QuoteChar.DoubleQuote, "ab&#x9;c" },
        { "ab\nc", QuoteChar.DoubleQuote, "ab&#xA;c" },
        { "ab'c", QuoteChar.DoubleQuote, "ab'c" },
        { "ab'c", QuoteChar.SingleQuote, "ab&apos;c" },
        { "ab\"c", QuoteChar.SingleQuote, "ab\"c" },
        { "ab\u008fc", QuoteChar.DoubleQuote, "ab\u008Fc" },
        { "𝟘𝟙", QuoteChar.DoubleQuote, "𝟘𝟙" },
    };

    [Theory]
    [MemberData(nameof(EscapeJsxAttributeStringData))]
    public void EscapeJsxAttributeString(string s, QuoteChar quoteChar, string expected)
    {
        var actual = TypeScript.Printer.Printer.EscapeJsxAttributeString(s, quoteChar);
        Assert.Equal(expected, actual);
    }

    public static TheoryData<string, bool> IsRecognizedTripleSlashCommentData => new()
    {
        { "", false },
        { "/a", false },
        { "//", false },
        { "//a", false },
        { "///", false },
        { "///a", false },
        { "///<reference path=\"foo\" />", true },
        { "///<reference types=\"foo\" />", true },
        { "///<reference lib=\"foo\" />", true },
        { "///<reference no-default-lib=\"foo\" />", true },
        { "///<amd-dependency path=\"foo\" />", true },
        { "///<amd-module />", true },
        { "/// <reference path=\"foo\" />", true },
        { "/// <reference types=\"foo\" />", true },
        { "/// <reference lib=\"foo\" />", true },
        { "/// <reference no-default-lib=\"foo\" />", true },
        { "/// <amd-dependency path=\"foo\" />", true },
        { "/// <amd-module />", true },
        { "/// <reference path=\"foo\"/>", true },
        { "/// <reference types=\"foo\"/>", true },
        { "/// <reference lib=\"foo\"/>", true },
        { "/// <reference no-default-lib=\"foo\"/>", true },
        { "/// <amd-dependency path=\"foo\"/>", true },
        { "/// <amd-module/>", true },
        { "/// <reference path='foo' />", true },
        { "/// <reference types='foo' />", true },
        { "/// <reference lib='foo' />", true },
        { "/// <reference no-default-lib='foo' />", true },
        { "/// <amd-dependency path='foo' />", true },
        { "/// <reference path=\"foo\" />  ", true },
        { "/// <reference types=\"foo\" />  ", true },
        { "/// <reference lib=\"foo\" />  ", true },
        { "/// <reference no-default-lib=\"foo\" />  ", true },
        { "/// <amd-dependency path=\"foo\" />  ", true },
        { "/// <amd-module />  ", true },
        { "/// <foo />", false },
        { "/// <reference />", false },
        { "/// <amd-dependency />", false },
    };

    /// <summary>
    /// Go 的测试先按 `Kind` 分流：`KindMultiLineCommentTrivia`/显式给出 Kind 的用例用给定的 Kind 与 Range；
    /// 其余用例（Kind 为零值，Go 里是 KindUnknown）用 `KindSingleLineCommentTrivia` + `[0, len(s))` 的 Range。
    /// 只有 `s: ""` 的两条用例显式写了 Kind（见下表），它们不进入 `[0, 0)` 的默认 Range。
    /// </summary>
    [Fact]
    public void IsRecognizedTripleSlashComment_EmptyRanges()
    {
        var multiLine = new CommentRange { Kind = Kind.MultiLineCommentTrivia };
        Assert.False(TypeScript.Printer.Printer.IsRecognizedTripleSlashComment("", multiLine));

        var singleLine = new CommentRange { Kind = Kind.SingleLineCommentTrivia };
        Assert.False(TypeScript.Printer.Printer.IsRecognizedTripleSlashComment("", singleLine));
    }

    [Theory]
    [MemberData(nameof(IsRecognizedTripleSlashCommentData))]
    public void IsRecognizedTripleSlashComment(string s, bool expected)
    {
        if (s.Length == 0)
        {
            // 空串的两条用例由 IsRecognizedTripleSlashComment_EmptyRanges 覆盖（Go 表里的 Kind 不同）。
            return;
        }

        var commentRange = new CommentRange
        {
            Kind = Kind.SingleLineCommentTrivia,
            Range = NewTextRange(0, s.Length),
        };
        var actual = TypeScript.Printer.Printer.IsRecognizedTripleSlashComment(s, commentRange);
        Assert.Equal(expected, actual);
    }
}
