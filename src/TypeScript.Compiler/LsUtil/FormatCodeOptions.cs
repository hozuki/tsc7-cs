// ported from: internal/ls/lsutil/formatcodeoptions.go
//
// **本文件是 `internal/ls/lsutil` 的局部移植**：只搬 `internal/format`（→ `TypeScript.Format`）用到的依赖闭包
// （本文件 + Asi.cs + Children.cs + CompletedNode.cs）。被省略的是 `FromLSFormatOptions`/`ToLSFormatOptions`，
// 它们依赖 `internal/lsp/lsproto`（LSP 协议包，属 M8，尚未移植）；`userpreferences.go`/`organizeimports.go`/
// `symbol_display.go`/`utilities.go` 同样留给 M8。理由与清单见报告。
//
// 与 Go 的差异（C# 语言特性；依据 docs/architecture.md §2/§3.2/§3.3）：
// - `type IndentStyle int` + iota 常量 → C# `enum IndentStyle`（成员名去 `IndentStyle` 前缀，数值一致）。
// - `type SemicolonPreference string` → C# `readonly struct SemicolonPreference`（同 Diagnostics.Key 的做法：
//   Go 里它与 string 靠"无类型字符串常量"隐式转换、`string(x)` 显式转换，C# 侧对应 implicit 入 + explicit 出；
//   `default(SemicolonPreference)` 规范成 ""，与 Go 的零值一致 —— 注意这不等于 `SemicolonPreferenceIgnore`
//   ("ignore")，`FormatCodeSettings{}` 的零值语义因此保持）。
// - `FormatCodeSettings` **内嵌** `EditorSettings`（Go 的字段提升：`settings.TabSize`）。C# 的 struct 不能继承，
//   所以把 `EditorSettings` 的 7 个字段**平铺**进 `FormatCodeSettings`，让 Go 的 `options.TabSize` 逐一对应；
//   `EditorSettings` 类型本身照样保留（Go 的导出类型，M8 的 userpreferences 会用到）。
// - 包级函数 → 静态类 `LsUtil`（与 package 同名，§2）；包级常量同样是 `LsUtil` 的静态字段（struct 不能是 const）。
// - Go 的未导出函数 `parseIndentStyle`/`parseSemicolonPreference` → `internal static` + PascalCase。
// - `printer.GetDefaultIndentSize()` → `TypeScript.Printer.Printer.GetDefaultIndentSize()`（命名空间 `TypeScript.Printer`
//   与外层命名空间同名，裸写 `Printer` 会解析成命名空间，所以这里写全名）。
using TypeScript.Core;

namespace TypeScript.LsUtil;

/// <summary>Go 的 <c>type IndentStyle int</c>（<c>IndentStyleNone</c>/<c>Block</c>/<c>Smart</c>）。</summary>
public enum IndentStyle : int
{
    None = 0,
    Block = 1,
    Smart = 2,
}

/// <summary>Go 的 <c>type SemicolonPreference string</c>（零值是 ""，与 <c>SemicolonPreferenceIgnore</c> 不同）。</summary>
public readonly struct SemicolonPreference : IEquatable<SemicolonPreference>
{
    private readonly string? _value;

    public SemicolonPreference(string value)
    {
        _value = value ?? string.Empty;
    }

    public string Value => _value ?? string.Empty;

    public static implicit operator SemicolonPreference(string value) => new(value);

    public static explicit operator string(SemicolonPreference preference) => preference.Value;

    public override string ToString() => Value;

    public bool Equals(SemicolonPreference other) => string.Equals(Value, other.Value, StringComparison.Ordinal);

    public override bool Equals(object? obj) => obj is SemicolonPreference other && Equals(other);

    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Value);

    public static bool operator ==(SemicolonPreference left, SemicolonPreference right) => left.Equals(right);

    public static bool operator !=(SemicolonPreference left, SemicolonPreference right) => !left.Equals(right);
}

/// <summary>Go 的 <c>type EditorSettings struct</c>（被 <c>FormatCodeSettings</c> 内嵌）。</summary>
public struct EditorSettings
{
    public int BaseIndentSize;
    public int IndentSize;
    public int TabSize;
    public string NewLineCharacter;
    public Tristate ConvertTabsToSpaces;
    public IndentStyle IndentStyle;
    public Tristate TrimTrailingWhitespace;
}

/// <summary>
/// Go 的 <c>type FormatCodeSettings struct { EditorSettings; … }</c>。
/// 内嵌的 7 个字段在这里平铺（见文件头），其余字段顺序与 Go 一致。
/// </summary>
public struct FormatCodeSettings
{
    // ---- 内嵌 EditorSettings 的字段（Go 的字段提升）----
    public int BaseIndentSize;
    public int IndentSize;
    public int TabSize;
    public string NewLineCharacter;
    public Tristate ConvertTabsToSpaces;
    public IndentStyle IndentStyle;
    public Tristate TrimTrailingWhitespace;

    // ---- FormatCodeSettings 自己的字段 ----
    public Tristate InsertSpaceAfterCommaDelimiter;
    public Tristate InsertSpaceAfterSemicolonInForStatements;
    public Tristate InsertSpaceBeforeAndAfterBinaryOperators;
    public Tristate InsertSpaceAfterConstructor;
    public Tristate InsertSpaceAfterKeywordsInControlFlowStatements;
    public Tristate InsertSpaceAfterFunctionKeywordForAnonymousFunctions;
    public Tristate InsertSpaceAfterOpeningAndBeforeClosingNonemptyParenthesis;
    public Tristate InsertSpaceAfterOpeningAndBeforeClosingNonemptyBrackets;
    public Tristate InsertSpaceAfterOpeningAndBeforeClosingNonemptyBraces;
    public Tristate InsertSpaceAfterOpeningAndBeforeClosingEmptyBraces;
    public Tristate InsertSpaceAfterOpeningAndBeforeClosingTemplateStringBraces;
    public Tristate InsertSpaceAfterOpeningAndBeforeClosingJsxExpressionBraces;
    public Tristate InsertSpaceAfterTypeAssertion;
    public Tristate InsertSpaceBeforeFunctionParenthesis;
    public Tristate PlaceOpenBraceOnNewLineForFunctions;
    public Tristate PlaceOpenBraceOnNewLineForControlBlocks;
    public Tristate InsertSpaceBeforeTypeAnnotation;
    public Tristate IndentMultiLineObjectLiteralBeginningOnBlankLine;
    public SemicolonPreference Semicolons;
    public Tristate IndentSwitchCase;
}

public static partial class LsUtil
{
    public static readonly SemicolonPreference SemicolonPreferenceIgnore = new("ignore");
    public static readonly SemicolonPreference SemicolonPreferenceInsert = new("insert");
    public static readonly SemicolonPreference SemicolonPreferenceRemove = new("remove");

    /// <summary>Go 的 <c>func parseIndentStyle(v any) IndentStyle</c>（未导出，落成 internal）。</summary>
    internal static IndentStyle ParseIndentStyle(object? v)
    {
        switch (v)
        {
            case string s:
                switch (s.ToLowerInvariant())
                {
                    case "none":
                        return IndentStyle.None;
                    case "block":
                        return IndentStyle.Block;
                    case "smart":
                        return IndentStyle.Smart;
                }

                break;
            case double d:
                return (IndentStyle)(int)d;
            case int i:
                return (IndentStyle)i;
        }

        return IndentStyle.Smart;
    }

    /// <summary>Go 的 <c>func parseSemicolonPreference(v any) SemicolonPreference</c>（未导出，落成 internal）。</summary>
    internal static SemicolonPreference ParseSemicolonPreference(object? v)
    {
        if (v is string s)
        {
            switch (s.ToLowerInvariant())
            {
                case "ignore":
                    return SemicolonPreferenceIgnore;
                case "insert":
                    return SemicolonPreferenceInsert;
                case "remove":
                    return SemicolonPreferenceRemove;
            }
        }

        return SemicolonPreferenceIgnore;
    }

    /// <summary>Go 的 <c>func GetDefaultFormatCodeSettings() FormatCodeSettings</c>。</summary>
    public static FormatCodeSettings GetDefaultFormatCodeSettings()
    {
        return new FormatCodeSettings
        {
            IndentSize = Printer.Printer.GetDefaultIndentSize(),
            TabSize = Printer.Printer.GetDefaultIndentSize(),
            NewLineCharacter = "\n",
            ConvertTabsToSpaces = Tristate.TSTrue,
            IndentStyle = IndentStyle.Smart,
            TrimTrailingWhitespace = Tristate.TSTrue,
            InsertSpaceAfterConstructor = Tristate.TSFalse,
            InsertSpaceAfterCommaDelimiter = Tristate.TSTrue,
            InsertSpaceAfterSemicolonInForStatements = Tristate.TSTrue,
            InsertSpaceBeforeAndAfterBinaryOperators = Tristate.TSTrue,
            InsertSpaceAfterKeywordsInControlFlowStatements = Tristate.TSTrue,
            InsertSpaceAfterFunctionKeywordForAnonymousFunctions = Tristate.TSFalse,
            InsertSpaceAfterOpeningAndBeforeClosingNonemptyParenthesis = Tristate.TSFalse,
            InsertSpaceAfterOpeningAndBeforeClosingNonemptyBrackets = Tristate.TSFalse,
            InsertSpaceAfterOpeningAndBeforeClosingNonemptyBraces = Tristate.TSTrue,
            InsertSpaceAfterOpeningAndBeforeClosingTemplateStringBraces = Tristate.TSFalse,
            InsertSpaceAfterOpeningAndBeforeClosingJsxExpressionBraces = Tristate.TSFalse,
            InsertSpaceBeforeFunctionParenthesis = Tristate.TSFalse,
            PlaceOpenBraceOnNewLineForFunctions = Tristate.TSFalse,
            PlaceOpenBraceOnNewLineForControlBlocks = Tristate.TSFalse,
            Semicolons = SemicolonPreferenceIgnore,
            IndentSwitchCase = Tristate.TSTrue,
        };
    }
}
