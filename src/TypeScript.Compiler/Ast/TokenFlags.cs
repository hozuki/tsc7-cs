// ported from: internal/ast/tokenflags.go
//
// 与 Go 的差异（C# 语言特性，依据 docs/architecture.md §3.3）：
// - `type TokenFlags int32` + 包级常量 → `enum TokenFlags : int`：成员名 = Go 常量名去掉 `TokenFlags`
//   前缀，数值逐条与 Go 一致（Go 用 int32 是为了紧凑，C# 用 int，语义相同）。
namespace TypeScript.Ast;

[Flags]
public enum TokenFlags : int
{
    None = 0,
    PrecedingLineBreak = 1 << 0,
    PrecedingJSDocComment = 1 << 1,
    Unterminated = 1 << 2,
    ExtendedUnicodeEscape = 1 << 3,  // e.g. `\u{10ffff}`
    Scientific = 1 << 4,             // e.g. `10e2`
    Octal = 1 << 5,                  // e.g. `0777`
    HexSpecifier = 1 << 6,           // e.g. `0x00000000`
    BinarySpecifier = 1 << 7,        // e.g. `0b0110010000000000`
    OctalSpecifier = 1 << 8,         // e.g. `0o777`
    ContainsSeparator = 1 << 9,      // e.g. `0b1100_0101`
    UnicodeEscape = 1 << 10,         // e.g. `\u00a0`
    ContainsInvalidEscape = 1 << 11, // e.g. `\uhello`
    HexEscape = 1 << 12,             // e.g. `\xa0`
    ContainsLeadingZero = 1 << 13,   // e.g. `0888`
    ContainsInvalidSeparator = 1 << 14, // e.g. `0_1`
    PrecedingJSDocLeadingAsterisks = 1 << 15,
    SingleQuote = 1 << 16,                  // e.g. `'abc'`
    PrecedingJSDocWithDeprecated = 1 << 17, // Preceding JSDoc comment contains @deprecated
    PrecedingJSDocWithSeeOrLink = 1 << 18,  // Preceding JSDoc comment contains @see or @link
    BinaryOrOctalSpecifier = BinarySpecifier | OctalSpecifier,
    WithSpecifier = HexSpecifier | BinaryOrOctalSpecifier,
    StringLiteralFlags = Unterminated | HexEscape | UnicodeEscape | ExtendedUnicodeEscape | ContainsInvalidEscape | SingleQuote,
    NumericLiteralFlags = Scientific | Octal | ContainsLeadingZero | WithSpecifier | ContainsSeparator | ContainsInvalidSeparator,
    TemplateLiteralLikeFlags = Unterminated | HexEscape | UnicodeEscape | ExtendedUnicodeEscape | ContainsInvalidEscape,
    RegularExpressionLiteralFlags = Unterminated,
    IsInvalid = Octal | ContainsLeadingZero | ContainsInvalidSeparator | ContainsInvalidEscape,
}
