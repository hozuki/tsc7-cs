// ported from: internal/fourslash/fourslash.go:4179-4652
//
// 偏离清单（相对 Go 原文逐条；行号均指 tsc/internal/fourslash/fourslash.go）：
//  1. `*testing.T` → `IHarnessTest`（TypeScript.TestUtil）：`t.Helper()` 删除；`t.Fatalf(fmt, args...)` →
//     `t.Fatal($"...")`；`t.Errorf` → `t.Error`。
//  2. 未导出方法 → PascalCase：`getQuickInfoAtCurrentPosition` → `GetQuickInfoAtCurrentPosition`、
//     `verifyHoverContent` → `VerifyHoverContent`、`verifyHoverMarkdown` → `VerifyHoverMarkdown`、
//     `quickInfoIsEmpty` → `QuickInfoIsEmpty`、`verifySignatureHelp` → `VerifySignatureHelp`（重载，见 3）、
//     `verifySignatureHelpResult` → `VerifySignatureHelpResult`。
//     `getCurrentPositionPrefix` → `GetCurrentPositionPrefix`（**public**：FsCore 的 SendRequestAndBaselineWorker
//     也调用它，见 local://fscore-freeze.md §4；本文件是它唯一的定义处）。
//  3. Go 的导出 `VerifySignatureHelp(t, VerifySignatureHelpOptions)` 与未导出
//     `verifySignatureHelp(t, *lsproto.SignatureHelpContext, *lsproto.SignatureHelp)` 签名不同，
//     C# 用重载表达（无需 `Core` 后缀）。
//  4. Go 包级 `assertDeepEqual(t, actual, expected, prefix, opts...)`（fourslash.go:1661）没有 C# 共享实现
//     （FsCore 只提供 2 参的 `FsCmp.AssertDeepEqual`），此处就地复刻其输出形状：
//     Go `t.Fatalf("%s:\n%s", prefix, diff)` ⇔ `FsCmp.Diff` + `t.Fatal(prefix + ":\n" + diff)`。
//  5. Go 的 `VerifySignatureHelpOptions` / `SignatureHelpCase`（结构体）→ C# `sealed class`（字段名照抄；
//     字符串字段显式 `= ""` 以匹配 Go 的零值）。
//  6. `MarkerInput = any` → `object?`；`switch` 分支顺序与 Go 的 type switch 一致（`case nil:` → `case null:`）。
//  7. Go 的 `switch { case actual.MarkupContent != nil: ... default: }` → C# `if/else`。
//  8. `%q`（Go 的 `strconv.Quote`）→ 本文件私有 `QuickInfoText.GoQuote`（与 JsonRpc/BaseProto.cs 的 GoQuote
//     同一实现）。`VerifyBaselineClosingTags` 的 `%q` 进 baseline，故必须逐字节等同。
//  9. `%v` 作用在 bool 上输出 `true`/`false` → C# 三元表达式（`$"{b}"` 会得到 `True`/`False`）。
// 10. `%T`（Go 类型名）→ `GetType().Name`；只出现在失败文本里（不进 baseline）。
// 11. `strings.TrimPrefix(s, p)` → `StartsWith + 切片`；`strings.Cut(s, sep)` →
//     `IndexOf(sep, StringComparison.Ordinal) + 切片`；`strings.TrimSpace` → `string.Trim()`（仓库统一映射）。
// 12. `core.MapFiltered` → `Core.Core.MapFiltered`；`markerAndItem[T]` → `MarkerAndItem<T>`（FsBaseline）。
// 13. `f.goToMarker(t, marker)` → `GoToMarker(t, marker)`（`MarkerOrRange` 重载，见冻结表 §3）。
// 14. `%d` 的 uint 参数 → `ToString(CultureInfo.InvariantCulture)`（拼出的字符串与 Go 一致）。
// 15. C# 特有写法：`LsConv` 会先命中命名空间 `TypeScript.LsConv`（`TypeScript` 是包围命名空间），
//     故包级函数按仓库既有写法写成 `LsConv.LsConv.FileNameToDocumentURI(...)`（同 FourslashTest.cs）。

using System.Globalization;
using System.Text;
using TypeScript.LsConv;
using TypeScript.LsProto;
using TypeScript.TestUtil;
using static TypeScript.Core.Core;
using static TypeScript.LsProto.LsProto;
using Range = TypeScript.LsProto.Range;

namespace TypeScript.Harness.Fourslash;

/// <summary>Go: <c>type VerifySignatureHelpOptions struct</c>（fourslash.go:4277-4300）。所有字段可选，只校验显式给出的。</summary>
public sealed class VerifySignatureHelpOptions
{
    /// <summary>Go: <c>Text</c>：完整签名文本（如 <c>"fn(x: string, y: number): void"</c>）。</summary>
    public string Text = "";

    /// <summary>Go: <c>DocComment</c>：签名的文档注释。</summary>
    public string DocComment = "";

    /// <summary>Go: <c>ParameterCount</c>：期望的参数个数。</summary>
    public int ParameterCount;

    /// <summary>Go: <c>ParameterName</c>：当前活动参数的期望名字。</summary>
    public string ParameterName = "";

    /// <summary>Go: <c>ParameterSpan</c>：当前活动参数的期望 label（如 <c>"x: string"</c>）。</summary>
    public string ParameterSpan = "";

    /// <summary>Go: <c>ParameterDocComment</c>：当前活动参数的文档。</summary>
    public string ParameterDocComment = "";

    /// <summary>Go: <c>OverloadsCount</c>：期望的重载（signature）个数。</summary>
    public int OverloadsCount;

    /// <summary>Go: <c>OverrideSelectedItemIndex</c>：覆盖要检查的 signature 下标（缺省用 ActiveSignature）。</summary>
    public int OverrideSelectedItemIndex;

    /// <summary>Go: <c>IsVariadic</c>：该签名是否含 rest 参数。</summary>
    public bool IsVariadic;

    /// <summary>Go: <c>IsVariadicSet</c>：<c>IsVariadic</c> 是否被显式设置（用来区分默认 false）。</summary>
    public bool IsVariadicSet;
}

/// <summary>Go: <c>type SignatureHelpCase struct</c>（fourslash.go:4584-4588）。</summary>
public sealed class SignatureHelpCase
{
    public SignatureHelpContext? Context;

    /// <summary>Go: <c>MarkerInput</c>（<c>= any</c>）：string | *Marker | []string | []*Marker | nil。</summary>
    public object? MarkerInput;

    public SignatureHelp? Expected;
}

/// <summary>`%q`（Go 的 <c>strconv.Quote</c>）在本切片的私有实现。放进嵌套类型，避免与其它 partial 切片的私有成员同名冲突。</summary>
internal static class QuickInfoText
{
    /// <summary>
    /// Go 的 <c>fmt</c> 对字符串用 <c>%q</c> 时的形状（= <c>strconv.Quote</c>）：<c>"</c>/<c>\</c> 转义，
    /// <c>\a \b \f \n \r \t \v</c> 用字母转义，其它 ASCII 控制字符用 <c>\xHH</c>，可打印字符（含非 ASCII）原样。
    /// 偏差：非 ASCII 里不可打印的符文 Go 会写 <c>\uXXXX</c>、非法 UTF-8 字节 Go 会写 <c>\xHH</c>，这里原样输出
    /// （与 JsonRpc/BaseProto.cs 的同名实现一致；只有畸形内容才会踩到）。
    /// </summary>
    public static string GoQuote(string text)
    {
        var builder = new StringBuilder(text.Length + 2);
        builder.Append('"');
        foreach (var c in text)
        {
            switch (c)
            {
                case '"': builder.Append("\\\""); break;
                case '\\': builder.Append("\\\\"); break;
                case '\a': builder.Append("\\a"); break;
                case '\b': builder.Append("\\b"); break;
                case '\f': builder.Append("\\f"); break;
                case '\n': builder.Append("\\n"); break;
                case '\r': builder.Append("\\r"); break;
                case '\t': builder.Append("\\t"); break;
                case '\v': builder.Append("\\v"); break;
                default:
                    if (c < 0x20 || c == 0x7F)
                    {
                        builder.Append("\\x").Append(((int)c).ToString("x2", CultureInfo.InvariantCulture));
                    }
                    else
                    {
                        builder.Append(c);
                    }

                    break;
            }
        }

        builder.Append('"');
        return builder.ToString();
    }
}

public sealed partial class FourslashTest
{
    /// <summary>Go: <c>func (f *FourslashTest) VerifyQuickInfoAt(t, marker, expectedText, expectedDocumentation)</c>（4179）。</summary>
    public void VerifyQuickInfoAt(IHarnessTest t, string marker, string expectedText, string expectedDocumentation)
    {
        GoToMarker(t, marker);
        var hover = GetQuickInfoAtCurrentPosition(t);
        if (hover is null)
        {
            t.Fatal($"Expected hover result at marker '{_lastKnownMarkerName}' but got nil");
        }

        VerifyHoverContent(t, hover.Contents, expectedText, expectedDocumentation, GetCurrentPositionPrefix());
    }

    /// <summary>Go: <c>func (f *FourslashTest) getQuickInfoAtCurrentPosition(t) *lsproto.Hover</c>（4187）。</summary>
    internal Hover? GetQuickInfoAtCurrentPosition(IHarnessTest t)
    {
        var parameters = new HoverParams
        {
            TextDocument = new TextDocumentIdentifier
            {
                Uri = LsConv.LsConv.FileNameToDocumentURI(_activeFilename),
            },
            Position = _currentCaretPosition,
        };
        var result = SendRequest(t, TextDocumentHoverInfo, parameters);
        return result.Hover;
    }

    /// <summary>Go: <c>func (f *FourslashTest) verifyHoverContent(...)</c>（4197）。</summary>
    internal void VerifyHoverContent(
        IHarnessTest t,
        MarkupContentOrStringOrMarkedStringWithLanguageOrMarkedStrings actual,
        string expectedText,
        string expectedDocumentation,
        string prefix)
    {
        if (actual.MarkupContent is not null)
        {
            VerifyHoverMarkdown(t, actual.MarkupContent.Value, expectedText, expectedDocumentation, prefix);
        }
        else
        {
            t.Fatal(prefix + "Expected markup content, got: " + FsCmp.Diff(actual, null));
        }
    }

    /// <summary>Go: <c>func (f *FourslashTest) verifyHoverMarkdown(...)</c>（4210）。</summary>
    internal void VerifyHoverMarkdown(
        IHarnessTest t,
        string actual,
        string expectedText,
        string expectedDocumentation,
        string prefix)
    {
        var expected = $"```typescript\n{expectedText}\n```\n{expectedDocumentation}";
        var diff = FsCmp.Diff(actual, expected);
        if (diff != "")
        {
            t.Fatal(prefix + "Hover markdown content mismatch:\n" + diff);
        }
    }

    /// <summary>Go: <c>func (f *FourslashTest) VerifyQuickInfoExists(t)</c>（4223）。</summary>
    public void VerifyQuickInfoExists(IHarnessTest t)
    {
        var (isEmpty, _) = QuickInfoIsEmpty(t);
        if (isEmpty)
        {
            t.Fatal($"Expected non-nil hover content at marker '{_lastKnownMarkerName}'");
        }
    }

    /// <summary>Go: <c>func (f *FourslashTest) VerifyNotQuickInfoExists(t)</c>（4229）。</summary>
    public void VerifyNotQuickInfoExists(IHarnessTest t)
    {
        var (isEmpty, hover) = QuickInfoIsEmpty(t);
        if (!isEmpty)
        {
            t.Fatal($"Expected empty hover content at marker '{_lastKnownMarkerName}', got '{FsCmp.Diff(hover, null)}'");
        }
    }

    /// <summary>Go: <c>func (f *FourslashTest) quickInfoIsEmpty(t) (bool, *lsproto.Hover)</c>（4235）。</summary>
    internal (bool IsEmpty, Hover? Hover) QuickInfoIsEmpty(IHarnessTest t)
    {
        var hover = GetQuickInfoAtCurrentPosition(t);
        if (hover is null ||
            (hover.Contents.MarkupContent is null && hover.Contents.MarkedStrings is null && hover.Contents.String is null))
        {
            return (true, null);
        }

        return (false, hover);
    }

    /// <summary>Go: <c>func (f *FourslashTest) VerifyQuickInfoIs(t, expectedText, expectedDocumentation)</c>（4243）。</summary>
    public void VerifyQuickInfoIs(IHarnessTest t, string expectedText, string expectedDocumentation)
    {
        var hover = GetQuickInfoAtCurrentPosition(t);
        // Go 此处不做 nil 检查（nil 时 panic）；C# 用 `!` 保持同样的"直接崩"语义。
        VerifyHoverContent(t, hover!.Contents, expectedText, expectedDocumentation, GetCurrentPositionPrefix());
    }

    /// <summary>Go: <c>func (f *FourslashTest) VerifyJsxClosingTag(t, markersToNewText map[string]*string)</c>（4248）。</summary>
    public void VerifyJsxClosingTag(IHarnessTest t, Dictionary<string, string?> markersToNewText)
    {
        foreach (var (marker, expectedText) in markersToNewText)
        {
            GoToMarker(t, marker);
            var parameters = new VSOnAutoInsertParams
            {
                VSTextDocument = new TextDocumentIdentifier
                {
                    Uri = LsConv.LsConv.FileNameToDocumentURI(_activeFilename),
                },
                VSPosition = _currentCaretPosition,
                VSCh = ">",
            };

            var requestResult = SendRequest(t, TextDocumentVSOnAutoInsertInfo, parameters);

            string? actualText = null;
            var item = requestResult.VSOnAutoInsertResponseItem;
            if (item is not null && item.VSTextEdit is not null)
            {
                var newText = item.VSTextEdit.NewText;
                if (item.VSTextEditFormat == InsertTextFormat.Snippet)
                {
                    if (!newText.StartsWith("$0", StringComparison.Ordinal))
                    {
                        t.Fatal(
                            $"{GetCurrentPositionPrefix()}expected JSX closing tag snippet to begin with $0, got {QuickInfoText.GoQuote(item.VSTextEdit.NewText)}");
                    }

                    newText = newText["$0".Length..];
                }

                actualText = newText;
            }

            var diff = FsCmp.Diff(actualText, expectedText);
            if (diff != "")
            {
                t.Fatal(GetCurrentPositionPrefix() + "JSX closing tag text mismatch:\n" + diff);
            }
        }
    }

    /// <summary>Go: <c>func (f *FourslashTest) VerifyBaselineClosingTags(t)</c>（4282）：为所有 marker 生成 JSX closing tag 补全的 baseline。</summary>
    public void VerifyBaselineClosingTags(IHarnessTest t)
    {
        var markersAndItems = MapFiltered<Marker, MarkerAndItem<VSOnAutoInsertResponseItem?>>(Markers(), marker =>
        {
            if (marker.Name is null)
            {
                return (Value: new MarkerAndItem<VSOnAutoInsertResponseItem?>(), Ok: false);
            }

            var parameters = new VSOnAutoInsertParams
            {
                VSTextDocument = new TextDocumentIdentifier
                {
                    Uri = LsConv.LsConv.FileNameToDocumentURI(marker.FileName()),
                },
                VSPosition = marker.LSPosition,
                VSCh = ">",
            };

            var result = SendRequest(t, TextDocumentVSOnAutoInsertInfo, parameters);
            return (
                Value: new MarkerAndItem<VSOnAutoInsertResponseItem?>
                {
                    Marker = marker,
                    Item = result.VSOnAutoInsertResponseItem,
                },
                Ok: true);
        });

        // Go 的 getRange 返回 nil：让 annotateContentWithTooltips 在 marker 位置画 caret 标记
        // （text edit 的 range 是零宽光标位置，画出来会是空下划线）。
        var result2 = AnnotateContentWithTooltips(
            t,
            markersAndItems,
            "closing tag",
            item => null,
            (item, prev) =>
            {
                if (item is null || item.VSTextEdit is null)
                {
                    return ["No closing tag"];
                }

                var format = "plaintext";
                if (item.VSTextEditFormat == InsertTextFormat.Snippet)
                {
                    format = "snippet";
                }

                return [$"{format}: {QuickInfoText.GoQuote(item.VSTextEdit.NewText)}"];
            });
        AddResultToBaseline(t, BaselineCommand.ClosingTag, result2);
    }

    /// <summary>Go: <c>func (f *FourslashTest) VerifySignatureHelp(t, expected VerifySignatureHelpOptions)</c>（4321）。</summary>
    public void VerifySignatureHelp(IHarnessTest t, VerifySignatureHelpOptions expected)
    {
        var prefix = GetCurrentPositionPrefix();
        var parameters = new SignatureHelpParams
        {
            TextDocument = new TextDocumentIdentifier
            {
                Uri = LsConv.LsConv.FileNameToDocumentURI(_activeFilename),
            },
            Position = _currentCaretPosition,
        };
        var result = SendRequest(t, TextDocumentSignatureHelpInfo, parameters);
        var help = result.SignatureHelp;
        if (help is null)
        {
            t.Fatal(prefix + "Could not get signature help");
        }

        // Determine which signature to check
        var selectedIndex = 0;
        if (expected.OverrideSelectedItemIndex > 0)
        {
            selectedIndex = expected.OverrideSelectedItemIndex;
        }
        else if (help.ActiveSignature is not null)
        {
            selectedIndex = (int)help.ActiveSignature.Value;
        }

        var signatures = help.Signatures ?? [];
        if (selectedIndex >= signatures.Length)
        {
            t.Fatal($"{prefix}Selected signature index {selectedIndex} out of range (have {signatures.Length} signatures)");
        }

        var selectedSig = signatures[selectedIndex]!;

        // Verify overloads count
        if (expected.OverloadsCount > 0)
        {
            if (signatures.Length != expected.OverloadsCount)
            {
                t.Error($"{prefix}Expected {expected.OverloadsCount} overloads, got {signatures.Length}");
            }
        }

        // Verify signature text
        if (expected.Text != "")
        {
            if (selectedSig.Label != expected.Text)
            {
                t.Error($"{prefix}Expected signature text {QuickInfoText.GoQuote(expected.Text)}, got {QuickInfoText.GoQuote(selectedSig.Label)}");
            }
        }

        // Verify doc comment
        if (expected.DocComment != "")
        {
            var actualDoc = "";
            if (selectedSig.Documentation is not null)
            {
                if (selectedSig.Documentation.MarkupContent is not null)
                {
                    actualDoc = selectedSig.Documentation.MarkupContent.Value;
                }
                else if (selectedSig.Documentation.String is not null)
                {
                    actualDoc = selectedSig.Documentation.String;
                }
            }

            if (actualDoc != expected.DocComment)
            {
                t.Error($"{prefix}Expected doc comment {QuickInfoText.GoQuote(expected.DocComment)}, got {QuickInfoText.GoQuote(actualDoc)}");
            }
        }

        // Verify parameter count
        if (expected.ParameterCount > 0)
        {
            var paramCount = 0;
            if (selectedSig.Parameters is not null)
            {
                paramCount = selectedSig.Parameters.Length;
            }

            if (paramCount != expected.ParameterCount)
            {
                t.Error($"{prefix}Expected {expected.ParameterCount} parameters, got {paramCount}");
            }
        }

        // Get active parameter
        var activeParamIndex = 0;
        if (selectedSig.ActiveParameter is { } selectedActiveParameter && selectedActiveParameter.Uinteger is uint selectedUint)
        {
            activeParamIndex = (int)selectedUint;
        }
        else if (help.ActiveParameter is { } helpActiveParameter && helpActiveParameter.Uinteger is uint helpUint)
        {
            activeParamIndex = (int)helpUint;
        }

        ParameterInformation? activeParam = null;
        if (selectedSig.Parameters is not null && activeParamIndex < selectedSig.Parameters.Length)
        {
            activeParam = selectedSig.Parameters[activeParamIndex];
        }

        // Verify parameter name
        if (expected.ParameterName != "")
        {
            if (activeParam is null)
            {
                t.Error($"{prefix}Expected parameter name {QuickInfoText.GoQuote(expected.ParameterName)}, but no active parameter");
            }
            else
            {
                // Parameter name is extracted from the label
                var actualName = "";
                if (activeParam.Label.String is not null)
                {
                    // Extract name from label like "x: string" -> "x" or "T extends Foo" -> "T" or "...x: any[]" -> "x"
                    var label = activeParam.Label.String;
                    // Strip leading "..." for rest parameters
                    if (label.StartsWith("...", StringComparison.Ordinal))
                    {
                        label = label["...".Length..];
                    }

                    var colonIndex = label.IndexOf(':', StringComparison.Ordinal);
                    var extendsIndex = label.IndexOf(" extends ", StringComparison.Ordinal);
                    if (colonIndex >= 0)
                    {
                        actualName = label[..colonIndex].Trim();
                    }
                    else if (extendsIndex >= 0)
                    {
                        actualName = label[..extendsIndex].Trim();
                    }
                    else
                    {
                        actualName = label;
                    }
                }

                if (actualName != expected.ParameterName)
                {
                    t.Error($"{prefix}Expected parameter name {QuickInfoText.GoQuote(expected.ParameterName)}, got {QuickInfoText.GoQuote(actualName)}");
                }
            }
        }

        // Verify parameter span (label)
        if (expected.ParameterSpan != "")
        {
            if (activeParam is null)
            {
                t.Error($"{prefix}Expected parameter span {QuickInfoText.GoQuote(expected.ParameterSpan)}, but no active parameter");
            }
            else
            {
                var actualSpan = "";
                if (activeParam.Label.String is not null)
                {
                    actualSpan = activeParam.Label.String;
                }

                if (actualSpan != expected.ParameterSpan)
                {
                    t.Error($"{prefix}Expected parameter span {QuickInfoText.GoQuote(expected.ParameterSpan)}, got {QuickInfoText.GoQuote(actualSpan)}");
                }
            }
        }

        // Verify parameter doc comment
        if (expected.ParameterDocComment != "")
        {
            if (activeParam is null)
            {
                t.Error($"{prefix}Expected parameter doc comment {QuickInfoText.GoQuote(expected.ParameterDocComment)}, but no active parameter");
            }
            else
            {
                var actualDoc = "";
                if (activeParam.Documentation is not null)
                {
                    if (activeParam.Documentation.MarkupContent is not null)
                    {
                        actualDoc = activeParam.Documentation.MarkupContent.Value;
                    }
                    else if (activeParam.Documentation.String is not null)
                    {
                        actualDoc = activeParam.Documentation.String;
                    }
                }

                if (actualDoc != expected.ParameterDocComment)
                {
                    t.Error($"{prefix}Expected parameter doc comment {QuickInfoText.GoQuote(expected.ParameterDocComment)}, got {QuickInfoText.GoQuote(actualDoc)}");
                }
            }
        }

        // Verify isVariadic (check if any parameter starts with "...")
        if (expected.IsVariadicSet)
        {
            var actualIsVariadic = false;
            if (selectedSig.Parameters is not null)
            {
                foreach (var param in selectedSig.Parameters)
                {
                    var label = param?.Label.String;
                    if (label is not null && label.StartsWith("...", StringComparison.Ordinal))
                    {
                        actualIsVariadic = true;
                        break;
                    }
                }
            }

            if (actualIsVariadic != expected.IsVariadic)
            {
                t.Error(
                    $"{prefix}Expected isVariadic={(expected.IsVariadic ? "true" : "false")}, got {(actualIsVariadic ? "true" : "false")}");
            }
        }
    }

    /// <summary>Go: <c>func (f *FourslashTest) VerifyNoSignatureHelp(t)</c>（4477）。</summary>
    public void VerifyNoSignatureHelp(IHarnessTest t)
    {
        var prefix = GetCurrentPositionPrefix();
        var parameters = new SignatureHelpParams
        {
            TextDocument = new TextDocumentIdentifier
            {
                Uri = LsConv.LsConv.FileNameToDocumentURI(_activeFilename),
            },
            Position = _currentCaretPosition,
        };
        var result = SendRequest(t, TextDocumentSignatureHelpInfo, parameters);
        var signatures = result.SignatureHelp?.Signatures;
        if (signatures is not null && signatures.Length > 0)
        {
            t.Error($"{prefix}Expected no signature help, but got {signatures.Length} signatures");
        }
    }

    /// <summary>Go: <c>func (f *FourslashTest) VerifyNoSignatureHelpWithContext(t, context)</c>（4494）。</summary>
    public void VerifyNoSignatureHelpWithContext(IHarnessTest t, SignatureHelpContext? context)
    {
        var prefix = GetCurrentPositionPrefix();
        var parameters = new SignatureHelpParams
        {
            TextDocument = new TextDocumentIdentifier
            {
                Uri = LsConv.LsConv.FileNameToDocumentURI(_activeFilename),
            },
            Position = _currentCaretPosition,
            Context = context,
        };
        var result = SendRequest(t, TextDocumentSignatureHelpInfo, parameters);
        var signatures = result.SignatureHelp?.Signatures;
        if (signatures is not null && signatures.Length > 0)
        {
            t.Error($"{prefix}Expected no signature help, but got {signatures.Length} signatures");
        }
    }

    /// <summary>Go: <c>func (f *FourslashTest) VerifyNoSignatureHelpForMarkersWithContext(t, context, markers ...string)</c>（4511）。</summary>
    public void VerifyNoSignatureHelpForMarkersWithContext(IHarnessTest t, SignatureHelpContext? context, params string[] markers)
    {
        foreach (var marker in markers)
        {
            GoToMarker(t, marker);
            VerifyNoSignatureHelpWithContext(t, context);
        }
    }

    /// <summary>Go: <c>func (f *FourslashTest) VerifySignatureHelpPresent(t, context)</c>（4520）。</summary>
    public void VerifySignatureHelpPresent(IHarnessTest t, SignatureHelpContext? context)
    {
        var prefix = GetCurrentPositionPrefix();
        var parameters = new SignatureHelpParams
        {
            TextDocument = new TextDocumentIdentifier
            {
                Uri = LsConv.LsConv.FileNameToDocumentURI(_activeFilename),
            },
            Position = _currentCaretPosition,
            Context = context,
        };
        var result = SendRequest(t, TextDocumentSignatureHelpInfo, parameters);
        if (result.SignatureHelp is null || (result.SignatureHelp.Signatures?.Length ?? 0) == 0)
        {
            t.Error(prefix + "Expected signature help to be present, but got none");
        }
    }

    /// <summary>Go: <c>func (f *FourslashTest) VerifySignatureHelpPresentForMarkers(t, context, markers ...string)</c>（4537）。</summary>
    public void VerifySignatureHelpPresentForMarkers(IHarnessTest t, SignatureHelpContext? context, params string[] markers)
    {
        foreach (var marker in markers)
        {
            GoToMarker(t, marker);
            VerifySignatureHelpPresent(t, context);
        }
    }

    /// <summary>Go: <c>func (f *FourslashTest) VerifyNoSignatureHelpForMarkers(t, markers ...string)</c>（4546）。</summary>
    public void VerifyNoSignatureHelpForMarkers(IHarnessTest t, params string[] markers)
    {
        foreach (var marker in markers)
        {
            GoToMarker(t, marker);
            VerifyNoSignatureHelp(t);
        }
    }

    /// <summary>Go: <c>func (f *FourslashTest) VerifySignatureHelpWithCases(t, signatureHelpCases ...*SignatureHelpCase)</c>（4564）。</summary>
    public void VerifySignatureHelpWithCases(IHarnessTest t, params SignatureHelpCase[] signatureHelpCases)
    {
        foreach (var option in signatureHelpCases)
        {
            switch (option.MarkerInput)
            {
                case string markerName:
                    GoToMarker(t, markerName);
                    VerifySignatureHelp(t, option.Context, option.Expected);
                    break;
                case Marker marker:
                    GoToMarker(t, marker);
                    VerifySignatureHelp(t, option.Context, option.Expected);
                    break;
                case string[] markerNames:
                    foreach (var name in markerNames)
                    {
                        GoToMarker(t, name);
                        VerifySignatureHelp(t, option.Context, option.Expected);
                    }

                    break;
                case Marker[] markers:
                    foreach (var marker in markers)
                    {
                        GoToMarker(t, marker);
                        VerifySignatureHelp(t, option.Context, option.Expected);
                    }

                    break;
                case null:
                    VerifySignatureHelp(t, option.Context, option.Expected);
                    break;
                default:
                    t.Fatal(
                        $"Invalid marker input type: {option.MarkerInput.GetType().Name}. Expected string, *Marker, []string, or []*Marker.");
                    break;
            }
        }
    }

    /// <summary>Go: <c>func (f *FourslashTest) verifySignatureHelp(t, context, expected)</c>（4617）。</summary>
    internal void VerifySignatureHelp(IHarnessTest t, SignatureHelpContext? context, SignatureHelp? expected)
    {
        var prefix = GetCurrentPositionPrefix();
        var parameters = new SignatureHelpParams
        {
            TextDocument = new TextDocumentIdentifier
            {
                Uri = LsConv.LsConv.FileNameToDocumentURI(_activeFilename),
            },
            Position = _currentCaretPosition,
            Context = context,
        };
        var result = SendRequest(t, TextDocumentSignatureHelpInfo, parameters);
        VerifySignatureHelpResult(t, result.SignatureHelp, expected, prefix);
    }

    /// <summary>Go: <c>func (f *FourslashTest) verifySignatureHelpResult(t, actual, expected, prefix)</c>（4633）。</summary>
    internal void VerifySignatureHelpResult(IHarnessTest t, SignatureHelp? actual, SignatureHelp? expected, string prefix)
    {
        var diff = FsCmp.Diff(actual, expected);
        if (diff != "")
        {
            t.Fatal(prefix + " SignatureHelp mismatch:\n" + diff);
        }
    }

    /// <summary>
    /// Go: <c>func (f *FourslashTest) getCurrentPositionPrefix() string</c>（4646）。
    /// FsCore 的 SendRequestAndBaselineWorker 也调用它，故为 public（见 local://fscore-freeze.md §4）。
    /// </summary>
    public string GetCurrentPositionPrefix()
    {
        if (_lastKnownMarkerName is not null)
        {
            return $"At marker '{_lastKnownMarkerName}': ";
        }

        return $"At position {_activeFilename}(Ln {_currentCaretPosition.Line.ToString(CultureInfo.InvariantCulture)}, Col {_currentCaretPosition.Character.ToString(CultureInfo.InvariantCulture)}): ";
    }
}
