// ported from: internal/fourslash/fourslash.go:2928-3420
//   VerifyBaselineHover 2928-2977、VerifyBaselineVSHover 2979-3021、renderVSContainerElement 3023-3045、
//   appendLinesForMarkedStringWithLanguage 3047-3052、hoverWithVerbosity 3054-3057、hoverContentString 3059-3071、
//   VerifyBaselineHoverWithVerbosity 3073-3178、VerifyBaselineSignatureHelp 3180-3306、VerifyBaselineSelectionRanges 3308-3420。
//
// 与 Go 的偏离（逐条）：
// 1) 基线命令常量 `quickInfoCmd` / `vsQuickInfoCmd` / `signatureHelpCmd` / `smartSelectionCmd` 由
//    BaselineUtil.cs（FsBaseline）的 `BaselineCommand` 提供 → 本文件写 `BaselineCommand.QuickInfo` 等，不重复定义。
// 2) `f.sendRequest` → `SendRequest`（FourslashTest.cs）；`f.addResultToBaseline` → `AddResultToBaseline`；
//    `f.writeToBaseline` → `WriteToBaseline`；`f.getScriptInfo` → `GetScriptInfo`（返回 `ScriptInfo?`，Go 为 nil 指针）。
// 3) 闭包签名：Go `getRange func(item T) *lsproto.Range` → `Func<T, Range?>`；Go
//    `getTooltipLines func(item, prev T) []string` → `Func<T, T?, string[]>`（`prev` 的首项是 Go 的零值 nil）。
//    Go 闭包里对 `item` 的解引用都发生在 annotateContentWithTooltips 的 `item != *new(T)` 守卫之后，
//    C# 侧同理由 `AnnotateContentWithTooltips` 的 `item is not null` 守卫 ⇒ 闭包参数 `item` 是非空 T。
// 4) `core.StringifyJson(v, "", "  ")` 的 `(string, error)` → `StringifyJson(v, "", "  ")` 只返回 string
//    （错误路径是 Json.MarshalIndent 抛 JsonException，见 Core.cs:869 与 docs §3.2）⇒ Go 的
//    `t.Fatalf("Failed to stringify markers and items for baseline: %v", err)` 分支在 C# 不存在。
// 5) 包级函数 `renderVSContainerElement` / `appendLinesForMarkedStringWithLanguage` / `hoverContentString`
//    → partial class 的 `private static`（PascalCase）：
//    `RenderVSContainerElement` / `AppendLinesForMarkedStringWithLanguage` / `HoverContentString`。
// 6) Go 的 `type hoverWithVerbosity struct` → 同文件的 `internal sealed class HoverWithVerbosity`，手写
//    `IJsonMarshaler`（AOT 无反射）：字段名 "hover"/"verbosityLevel" 与 Go 的 json tag 逐字一致，且两个键都无条件写出
//    （Go 的 tag 没有 omitempty）。该结构会进 QuickInfo baseline 的 JSON。
// 7) `VerifyBaselineHover` 与 `VerifyBaselineHoverWithVerbosity` 里两段**逐字相同**的 tooltip 提取闭包提成
//    `private static HoverContentsLines(contents)`（Go 侧是两段重复代码；输出不变）。
// 8) `fmt.Sprintf("... %q", s)` 是 Go 的 `strconv.Quote` 语义 → 本文件的 `QuoteString`（逐条复刻：
//    可打印码点原样、`"`/`\` 加反斜杠、控制字符 `\t\n\r\a\b\f\v`、其余 <0x20 与 0x7F 走 `\xNN`、
//    不可打印码点走 `\uNNNN`/`\UNNNNNNNN`，孤立代理项按 Go 的 "非法 UTF-8" 路径输出 `\xED\xA0\x80` 形态）。
// 9) `fmt.Sprintf("Id: 0x%X", int32)` → `HexUpper(int)`：Go 对负数打印 `-` + 绝对值，C# 的 "X" 格式打补码 ⇒ 单独判符号。
// 10) `strings.Replace(s, old, new, 1)` → `ReplaceFirst`（含 Go 的 `old == ""` 语义：在开头插入）。
// 11) `signatureLine[tuple[0]:tuple[1]]`：Go 的字符串下标是 **UTF-8 字节**偏移，本仓内部坐标是 UTF-16 代码单元
//     （Core/Text.cs 文件头）⇒ C# 用 UTF-16 切片（ASCII 标签逐字节等同）。
// 12) `t.Fatalf("Unsupported param label kind.")` / `t.Fatal("Unsupported documentation format.")` →
//     `Debug.Fail(...)` + `throw new System.Diagnostics.UnreachableException()`（仓库既定 panic 映射）。
// 13) `positions := []lsproto.Position{marker.LSPosition}` → 集合表达式 `[marker.LSPosition]`。
// 14) `strings.TrimSuffix(result.String(), "\n")` → `EndsWith('\n') ? s[..^1] : s`（只去掉**一个**结尾换行；
//     用 `TrimEnd('\n')` 会在"结尾恰好有多个换行"时产生不同输出，所以不用它）。
// 15) 本文件里的 `private static Int(int)`（Go 的 `%d`）被同分节的 FourslashTest.Hierarchy.cs 复用；
//     `HoverWithVerbosity` 类也定义在本文件（Go 的包级 struct）。两个 partial 文件共享同一个类，成员不能重复定义。
using System.Globalization;
using System.Text;
using TypeScript.Core;
using TypeScript.Json;
using TypeScript.LsProto;
using TypeScript.TestUtil;
using static TypeScript.Core.Core;
using static TypeScript.LsConv.LsConv;
using static TypeScript.Json.Json;
using static TypeScript.LsProto.LsProto;
using static TypeScript.TsOptions.TsOptions;
using Encoder = TypeScript.Json.Encoder;
using Range = TypeScript.LsProto.Range;

namespace TypeScript.Harness.Fourslash;

public sealed partial class FourslashTest
{
    /// <summary>Go: <c>func (f *FourslashTest) VerifyBaselineHover(t *testing.T)</c>（fourslash.go:2928-2977）。</summary>
    public void VerifyBaselineHover(IHarnessTest t)
    {
        var markersAndItems = MapFiltered<Marker, MarkerAndItem<Hover>>(Markers(), marker =>
        {
            if (marker.Name is null)
            {
                return (null!, false);
            }

            var parameters = new HoverParams
            {
                TextDocument = new TextDocumentIdentifier { Uri = FileNameToDocumentURI(marker.FileName()) },
                Position = marker.LSPosition,
            };

            var result = SendRequest(t, TextDocumentHoverInfo, parameters);
            return (new MarkerAndItem<Hover> { Marker = marker, Item = result.Hover! }, true);
        });

        AddResultToBaseline(t, BaselineCommand.QuickInfo, AnnotateContentWithTooltips(
            t,
            markersAndItems,
            "quickinfo",
            static item => item.Range,
            static (item, _) => HoverContentsLines(item.Contents)));
        WriteToBaseline(BaselineCommand.QuickInfo, StringifyJson(markersAndItems, "", "  "));
    }

    /// <summary>Go: <c>func (f *FourslashTest) VerifyBaselineVSHover(t *testing.T)</c>（fourslash.go:2979-3021）。</summary>
    public void VerifyBaselineVSHover(IHarnessTest t)
    {
        var markersAndItems = MapFiltered<Marker, MarkerAndItem<Hover>>(Markers(), marker =>
        {
            if (marker.Name is null)
            {
                return (null!, false);
            }

            var parameters = new HoverParams
            {
                TextDocument = new TextDocumentIdentifier { Uri = FileNameToDocumentURI(marker.FileName()) },
                Position = marker.LSPosition,
            };

            var result = SendRequest(t, TextDocumentHoverInfo, parameters);
            return (new MarkerAndItem<Hover> { Marker = marker, Item = result.Hover! }, true);
        });

        AddResultToBaseline(t, BaselineCommand.VsQuickInfo, AnnotateContentWithTooltips(
            t,
            markersAndItems,
            "vsquickinfo",
            static item => item.Range,
            static (item, _) =>
            {
                if (item.VSRawContent is null)
                {
                    return ["(no _vs_rawContent; is VSSupportsVisualStudioExtensions set on the test's ClientCapabilities?)"];
                }
                return RenderVSContainerElement(item.VSRawContent, "");
            }));
        WriteToBaseline(BaselineCommand.VsQuickInfo, StringifyJson(markersAndItems, "", "  "));
    }

    /// <summary>
    /// Go: <c>func renderVSContainerElement(el *lsproto.VSContainerElement, indent string) []string</c>（fourslash.go:3023-3045）。
    /// </summary>
    private static string[] RenderVSContainerElement(VSContainerElement el, string indent)
    {
        var lines = new List<string> { $"{indent}ContainerElement (Style={el.Style.String()})" };
        var childIndent = indent + "  ";
        foreach (var child in el.Elements ?? [])
        {
            if (child.ImageElement is not null)
            {
                var imageId = child.ImageElement.ImageId!;
                lines.Add($"{childIndent}ImageElement {{ Guid: {imageId.Guid}, Id: 0x{HexUpper(imageId.Id)} }}");
            }
            else if (child.ClassifiedTextElement is not null)
            {
                lines.Add(childIndent + "ClassifiedTextElement");
                foreach (var run in child.ClassifiedTextElement.Runs ?? [])
                {
                    lines.Add($"{childIndent}  [{run!.ClassificationTypeName}] {QuoteString(run.Text)}");
                }
            }
            else if (child.ContainerElement is not null)
            {
                lines.AddRange(RenderVSContainerElement(child.ContainerElement, childIndent));
            }
            else
            {
                lines.Add(childIndent + "<empty union element>");
            }
        }

        return [.. lines];
    }

    /// <summary>
    /// Go: <c>func appendLinesForMarkedStringWithLanguage(result []string, ms *lsproto.MarkedStringWithLanguage) []string</c>
    /// （fourslash.go:3047-3052）。
    /// </summary>
    private static string[] AppendLinesForMarkedStringWithLanguage(string[] result, MarkedStringWithLanguage ms)
    {
        var lines = new List<string>(result.Length + 3);
        lines.AddRange(result);
        lines.Add("```" + ms.Language);
        lines.Add(ms.Value);
        lines.Add("```");
        return [.. lines];
    }

    /// <summary>Go: <c>func hoverContentString(hover *lsproto.Hover) string</c>（fourslash.go:3059-3071）。</summary>
    private static string HoverContentString(Hover? hover)
    {
        if (hover is null)
        {
            return "";
        }
        if (hover.Contents.MarkupContent is not null)
        {
            return hover.Contents.MarkupContent.Value;
        }
        if (hover.Contents.String is not null)
        {
            return hover.Contents.String;
        }
        return "";
    }

    /// <summary>
    /// Go: <c>func (f *FourslashTest) getTooltipLines</c> 的闭包体（fourslash.go:2954-2975 与 3141-3167 两处逐字相同）。
    /// </summary>
    private static string[] HoverContentsLines(MarkupContentOrStringOrMarkedStringWithLanguageOrMarkedStrings contents)
    {
        string[] result = [];

        if (contents.MarkupContent is not null)
        {
            result = contents.MarkupContent.Value.Split('\n');
        }
        if (contents.String is not null)
        {
            result = contents.String!.Split('\n');
        }
        if (contents.MarkedStringWithLanguage is not null)
        {
            result = AppendLinesForMarkedStringWithLanguage(result, contents.MarkedStringWithLanguage);
        }
        if (contents.MarkedStrings is not null)
        {
            foreach (var ms in contents.MarkedStrings)
            {
                if (ms.MarkedStringWithLanguage is not null)
                {
                    result = AppendLinesForMarkedStringWithLanguage(result, ms.MarkedStringWithLanguage);
                }
                else
                {
                    result = [.. result, ms.String!];
                }
            }
        }

        return result;
    }

    /// <summary>
    /// Go: <c>func (f *FourslashTest) VerifyBaselineHoverWithVerbosity(t *testing.T, verbosityLevels map[string][]int)</c>
    /// （fourslash.go:3073-3178）。`map` 零值 → C# 的 `null`（Go 读 nil map 得零值），调用处按需传 `null`。
    /// </summary>
    public void VerifyBaselineHoverWithVerbosity(IHarnessTest t, Dictionary<string, int[]>? verbosityLevels)
    {
        var markersAndItems = new List<MarkerAndItem<HoverWithVerbosity>>();
        foreach (var marker in Markers())
        {
            if (marker.Name is null)
            {
                continue;
            }
            if (!(verbosityLevels ?? []).TryGetValue(marker.Name, out var levels))
            {
                levels = [0];
            }
            for (var i = 0; i < levels.Length; i++)
            {
                var level = levels[i];
                int? verbLevel = null;
                if (level > 0)
                {
                    verbLevel = level;
                }
                var parameters = new HoverParams
                {
                    TextDocument = new TextDocumentIdentifier { Uri = FileNameToDocumentURI(marker.FileName()) },
                    Position = marker.LSPosition,
                    VerbosityLevel = verbLevel,
                };
                var result = SendRequest(t, TextDocumentHoverInfo, parameters);
                var item = new HoverWithVerbosity
                {
                    Hover = result.Hover,
                    VerbosityLevel = level,
                };
                // If the previous level said it can't expand further, verify the hover
                // content is identical, meaning the flag was accurate.
                if (i > 0 && level > levels[i - 1])
                {
                    var prevItem = markersAndItems[^1].Item;
                    if (prevItem is not null && prevItem.Hover is not null && !prevItem.Hover.CanIncreaseVerbosity)
                    {
                        var prevContent = HoverContentString(prevItem.Hover);
                        var curContent = HoverContentString(item.Hover);
                        if (prevContent != curContent)
                        {
                            t.Error(
                                $"At marker {QuoteString(marker.Name)}: verbosity level {Int(level)} response differs from level " +
                                $"{Int(levels[i - 1])}, but level {Int(levels[i - 1])} had canIncreaseVerbosity=false.\n" +
                                $"  level {Int(levels[i - 1])}: {prevContent}\n  level {Int(level)}: {curContent}");
                        }
                    }
                }
                markersAndItems.Add(new MarkerAndItem<HoverWithVerbosity> { Marker = marker, Item = item });
            }
        }

        AddResultToBaseline(t, BaselineCommand.QuickInfo, AnnotateContentWithTooltips(
            t,
            [.. markersAndItems],
            "quickinfo",
            static item => item.Hover?.Range,
            static (item, _) =>
            {
                if (item.Hover is null)
                {
                    return Array.Empty<string>();
                }
                var result = HoverContentsLines(item.Hover.Contents);
                var lines = new string[result.Length + 1];
                Array.Copy(result, lines, result.Length);
                lines[^1] = $"(verbosity level: {Int(item.VerbosityLevel)})";
                return lines;
            }));
        WriteToBaseline(BaselineCommand.QuickInfo, StringifyJson(markersAndItems.ToArray(), "", "  "));
    }

    /// <summary>Go: <c>func (f *FourslashTest) VerifyBaselineSignatureHelp(t *testing.T)</c>（fourslash.go:3180-3306）。</summary>
    public void VerifyBaselineSignatureHelp(IHarnessTest t)
    {
        var markersAndItems = MapFiltered<Marker, MarkerAndItem<SignatureHelp>>(Markers(), marker =>
        {
            if (marker.Name is null)
            {
                return (null!, false);
            }

            var parameters = new SignatureHelpParams
            {
                TextDocument = new TextDocumentIdentifier { Uri = FileNameToDocumentURI(marker.FileName()) },
                Position = marker.LSPosition,
            };

            var result = SendRequest(t, TextDocumentSignatureHelpInfo, parameters);
            return (new MarkerAndItem<SignatureHelp> { Marker = marker, Item = result.SignatureHelp! }, true);
        });

        AddResultToBaseline(t, BaselineCommand.SignatureHelp, AnnotateContentWithTooltips(
            t,
            markersAndItems,
            "signaturehelp",
            // SignatureHelp doesn't have a range like hover does
            static item => null,
            (item, _) => GetSignatureHelpTooltipLines(t, item)));
        WriteToBaseline(BaselineCommand.SignatureHelp, StringifyJson(markersAndItems, "", "  "));
    }

    /// <summary>Go: <c>func (f *FourslashTest) getTooltipLines</c> 的闭包体（fourslash.go:3203-3303）。</summary>
    private static string[] GetSignatureHelpTooltipLines(IHarnessTest t, SignatureHelp item)
    {
        if (item.Signatures is null || item.Signatures.Length == 0)
        {
            return ["No signature help available"];
        }

        // Show active signature if specified, otherwise first signature
        var activeSignature = 0;
        if (item.ActiveSignature is not null && (int)item.ActiveSignature.Value < item.Signatures.Length)
        {
            activeSignature = (int)item.ActiveSignature.Value;
        }

        var sig = item.Signatures[activeSignature]!;

        // Build signature display
        var signatureLine = sig.Label;
        var activeParamLine = "";

        // Determine active parameter: per-signature takes precedence over top-level per LSP spec
        // "If provided (or `null`), this is used in place of `SignatureHelp.activeParameter`."
        var activeParamPtr = sig.ActiveParameter ?? item.ActiveParameter;

        // Show active parameter if specified, and the signature text.
        if (activeParamPtr is not null && activeParamPtr.Uinteger is not null && sig.Parameters is not null)
        {
            var activeParamIndex = (int)activeParamPtr.Uinteger.Value;
            if (activeParamIndex >= 0 && activeParamIndex < sig.Parameters.Length)
            {
                var activeParam = sig.Parameters[activeParamIndex]!;

                // Get the parameter label and bold the
                // parameter text within the original string.
                var activeParamLabel = "";
                if (activeParam.Label.String is not null)
                {
                    activeParamLabel = activeParam.Label.String;
                }
                else if (activeParam.Label.Tuple is not null)
                {
                    activeParamLabel = signatureLine[(int)activeParam.Label.Tuple[0]..(int)activeParam.Label.Tuple[1]];
                }
                else
                {
                    Debug.Fail("Unsupported param label kind.");
                    throw new System.Diagnostics.UnreachableException();
                }
                signatureLine = ReplaceFirst(signatureLine, activeParamLabel, "**" + activeParamLabel + "**");

                if (activeParam.Documentation is not null)
                {
                    if (activeParam.Documentation.MarkupContent is not null)
                    {
                        activeParamLine = activeParam.Documentation.MarkupContent.Value;
                    }
                    else if (activeParam.Documentation.String is not null)
                    {
                        activeParamLine = activeParam.Documentation.String;
                    }

                    activeParamLine = $"- `{activeParamLabel}`: {activeParamLine}";
                }
            }
        }

        var result = new List<string>(16) { signatureLine };
        if (activeParamLine != "")
        {
            result.Add(activeParamLine);
        }

        // ORIGINALLY we would "only display signature documentation on the last argument when multiple arguments are marked".
        // !!!
        // Note that this is harder than in Strada, because LSP signature help has no concept of
        // applicable spans.
        if (sig.Documentation is not null)
        {
            if (sig.Documentation.MarkupContent is not null)
            {
                result.AddRange(sig.Documentation.MarkupContent.Value.Split('\n'));
            }
            else if (sig.Documentation.String is not null)
            {
                result.AddRange(sig.Documentation.String.Split('\n'));
            }
            else
            {
                Debug.Fail("Unsupported documentation format.");
                throw new System.Diagnostics.UnreachableException();
            }
        }

        return [.. result];
    }

    /// <summary>Go: <c>func (f *FourslashTest) VerifyBaselineSelectionRanges(t *testing.T)</c>（fourslash.go:3308-3420）。</summary>
    public void VerifyBaselineSelectionRanges(IHarnessTest t)
    {
        var markers = Markers();
        var result = new StringBuilder();
        const string newLine = "\n";

        for (var i = 0; i < markers.Length; i++)
        {
            var marker = markers[i];
            if (i > 0)
            {
                result.Append(newLine);
                for (var j = 0; j < 80; j++)
                {
                    result.Append('=');
                }
                result.Append(newLine);
                result.Append(newLine);
            }

            var script = GetScriptInfo(marker.FileName())!;
            var fileContent = script._content;

            // Add the marker position indicator
            var markerPos = marker.Position;
            var baselineContent = fileContent[..markerPos] + "/**/" + fileContent[markerPos..] + newLine;
            result.Append(baselineContent);

            // Get selection ranges at this marker
            var parameters = new SelectionRangeParams
            {
                TextDocument = new TextDocumentIdentifier { Uri = FileNameToDocumentURI(marker.FileName()) },
                Positions = [marker.LSPosition],
            };

            var selectionRangeResult = SendRequest(t, TextDocumentSelectionRangeInfo, parameters);

            if (selectionRangeResult.SelectionRanges is null || selectionRangeResult.SelectionRanges.Length == 0)
            {
                result.Append("No selection ranges available\n");
                continue;
            }

            var selectionRange = selectionRangeResult.SelectionRanges[0];

            // Add blank line after source code section
            result.Append(newLine);

            // Walk through the selection range chain
            while (selectionRange is not null)
            {
                var start = (int)_converters.LineAndCharacterToPosition(script, selectionRange.Range.Start);
                var end = (int)_converters.LineAndCharacterToPosition(script, selectionRange.Range.End);

                // Create a masked version of the file showing only this range
                var runes = ToRunes(fileContent);
                var masked = new int[runes.Length];
                for (var k = 0; k < runes.Length; k++)
                {
                    var ch = runes[k];
                    if (k >= start && k < end)
                    {
                        // Keep characters in the selection range
                        if (ch == ' ')
                        {
                            masked[k] = '•';
                        }
                        else if (ch == '\n' || ch == '\r')
                        {
                            masked[k] = ch; // Keep line breaks as-is, will add arrow later
                        }
                        else
                        {
                            masked[k] = ch;
                        }
                    }
                    else
                    {
                        // Replace characters outside the range
                        if (ch == '\n' || ch == '\r')
                        {
                            masked[k] = ch;
                        }
                        else
                        {
                            masked[k] = ' ';
                        }
                    }
                }

                var maskedStr = FromRunes(masked);

                // Add line break arrows
                maskedStr = maskedStr.Replace("\n", "↲\n");
                maskedStr = maskedStr.Replace("\r", "↲\r");

                // Remove blank lines
                var lines = maskedStr.Split('\n');
                var nonBlankLines = new List<string>();
                foreach (var line in lines)
                {
                    var trimmed = TrimSpace(line);
                    if (trimmed != "" && trimmed != "↲")
                    {
                        nonBlankLines.Add(line);
                    }
                }
                maskedStr = string.Join("\n", nonBlankLines);

                // Find leading and trailing width of non-whitespace characters
                var maskedRunes = ToRunes(maskedStr);

                var leadingWidth = -1;
                for (var k = 0; k < maskedRunes.Length; k++)
                {
                    if (IsRealCharacter(maskedRunes[k]))
                    {
                        leadingWidth = k;
                        break;
                    }
                }

                var trailingWidth = -1;
                for (var k = maskedRunes.Length - 1; k >= 0; k--)
                {
                    if (IsRealCharacter(maskedRunes[k]))
                    {
                        trailingWidth = k;
                        break;
                    }
                }

                if (leadingWidth != -1 && trailingWidth != -1 && leadingWidth <= trailingWidth)
                {
                    // Clean up middle section
                    var prefix = FromRunes(maskedRunes[..leadingWidth]);
                    var middle = FromRunes(maskedRunes[leadingWidth..(trailingWidth + 1)]);
                    var suffix = FromRunes(maskedRunes[(trailingWidth + 1)..]);

                    middle = middle.Replace("•", " ");
                    middle = middle.Replace("↲", "");

                    maskedStr = prefix + middle + suffix;
                }

                // Add blank line before multi-line ranges
                if (maskedStr.Contains('\n'))
                {
                    result.Append(newLine);
                }

                result.Append(maskedStr);
                if (!maskedStr.EndsWith('\n'))
                {
                    result.Append(newLine);
                }

                selectionRange = selectionRange.Parent;
            }
        }
        // Go: strings.TrimSuffix(result.String(), "\n")（只去掉**一个**结尾换行，不是 TrimEnd）。
        var baselineText = result.ToString();
        AddResultToBaseline(t, BaselineCommand.SmartSelection, baselineText.EndsWith('\n') ? baselineText[..^1] : baselineText);
    }

    /// <summary>Go: 闭包 <c>isRealCharacter</c>（fourslash.go:3378-3380）：`ch != '•' &amp;&amp; ch != '↲' &amp;&amp; !IsWhiteSpaceLike(ch)`。</summary>
    private static bool IsRealCharacter(int ch) =>
        // Go 的 IsWhiteSpaceLike 只覆盖 BMP；补充代理对码点分支，避免 `(char)` 截断后误判。
        ch != '•' && ch != '↲' && (ch > 0xFFFF || !TypeScript.StringUtil.StringUtil.IsWhiteSpaceLike((char)ch));

    /// <summary>Go: <c>[]rune(s)</c>（Runef 化；非法 UTF-16 序列按 Go 的非法 UTF-8 路径给出 U+FFFD）。</summary>
    private static int[] ToRunes(string s)
    {
        var runes = new List<int>(s.Length);
        foreach (var rune in s.EnumerateRunes())
        {
            runes.Add(rune.Value);
        }
        return [.. runes];
    }

    /// <summary>Go: <c>string([]rune)</c>。</summary>
    private static string FromRunes(int[] runes)
    {
        var builder = new StringBuilder(runes.Length);
        foreach (var rune in runes)
        {
            builder.Append(char.ConvertFromUtf32(rune));
        }
        return builder.ToString();
    }

    /// <summary>
    /// Go: <c>fmt.Sprintf("%d", n)</c> —— InvariantCulture 的十进制整数（隔离 Go 的 `%d`）。
    /// </summary>
    private static string Int(int n) => n.ToString(CultureInfo.InvariantCulture);

    /// <summary>
    /// Go: <c>fmt.Sprintf("%X", int32)</c> —— 大写十六进制，负数是 Go 的 "-" + 绝对值（C# 的 "X" 会打补码）。
    /// </summary>
    private static string HexUpper(int value) => value < 0
        ? "-" + (-(long)value).ToString("X", CultureInfo.InvariantCulture)
        : value.ToString("X", CultureInfo.InvariantCulture);

    /// <summary>Go: <c>strings.Replace(s, old, new, 1)</c>。</summary>
    private static string ReplaceFirst(string s, string oldValue, string newValue)
    {
        // Go 的 strings.Replace 在 old 为空串时把 new 插到开头（n=1）。
        if (oldValue.Length == 0)
        {
            return newValue + s;
        }
        var index = s.IndexOf(oldValue, StringComparison.Ordinal);
        return index < 0 ? s : s[..index] + newValue + s[(index + oldValue.Length)..];
    }

    /// <summary>Go: <c>strconv.Quote</c>（`fmt` 的 `%q` 对 string 的语义）。</summary>
    private static string QuoteString(string s)
    {
        const string lowerHex = "0123456789abcdef";
        var builder = new StringBuilder(s.Length + 2);
        builder.Append('"');
        var position = 0;
        while (position < s.Length)
        {
            // Rune.DecodeFromUtf16 与 Go 的 utf8.DecodeRuneInString 对孤立代理项都判非法；
            // Go 从**字节**串出发，非法序列按首字节走 `\xNN`，这里按 Go 对代理项写出的 WTF-8 三字节逐个 `\xNN`。
            var status = Rune.DecodeFromUtf16(s.AsSpan(position), out var rune, out var consumed);
            if (status == System.Buffers.OperationStatus.InvalidData)
            {
                var codeUnit = s[position];
                AppendHexByteEscape(builder, (byte)(0xE0 | (codeUnit >> 12)));
                AppendHexByteEscape(builder, (byte)(0x80 | ((codeUnit >> 6) & 0x3F)));
                AppendHexByteEscape(builder, (byte)(0x80 | (codeUnit & 0x3F)));
                position++;
                continue;
            }
            position += consumed;

            var r = rune.Value;
            if (r == '"' || r == '\\')
            {
                builder.Append('\\');
                builder.Append((char)r);
                continue;
            }
            if (IsGoPrint(r))
            {
                builder.Append(char.ConvertFromUtf32(r));
                continue;
            }
            switch (r)
            {
                case '\a': builder.Append("\\a"); continue;
                case '\b': builder.Append("\\b"); continue;
                case '\f': builder.Append("\\f"); continue;
                case '\n': builder.Append("\\n"); continue;
                case '\r': builder.Append("\\r"); continue;
                case '\t': builder.Append("\\t"); continue;
                case '\v': builder.Append("\\v"); continue;
            }
            if (r < ' ' || r == 0x7F)
            {
                builder.Append("\\x");
                builder.Append(lowerHex[(r >> 4) & 0xF]);
                builder.Append(lowerHex[r & 0xF]);
            }
            else if (r < 0x10000)
            {
                builder.Append("\\u");
                for (var shift = 12; shift >= 0; shift -= 4)
                {
                    builder.Append(lowerHex[(r >> shift) & 0xF]);
                }
            }
            else
            {
                builder.Append("\\U");
                for (var shift = 28; shift >= 0; shift -= 4)
                {
                    builder.Append(lowerHex[(r >> shift) & 0xF]);
                }
            }
        }
        builder.Append('"');
        return builder.ToString();
    }

    /// <summary>Go: <c>strconv.Quote</c> 的 `\xNN` 分支（小写十六进制）。</summary>
    private static void AppendHexByteEscape(StringBuilder builder, byte b)
    {
        const string lowerHex = "0123456789abcdef";
        builder.Append("\\x");
        builder.Append(lowerHex[b >> 4]);
        builder.Append(lowerHex[b & 0xF]);
    }

    /// <summary>Go: <c>strconv.Quote</c> 内部的 <c>IsPrint</c>（unicode.L/M/N/P/S + 拉丁-1 表）。</summary>
    private static bool IsGoPrint(int r)
    {
        if (r <= 0xFF)
        {
            // unicode.MaxLatin1 表：空格与 0x21-0x7E 可打印；0xA1-0xAC/0xAE-0xFF 可打印；
            // 其余（含 0xA0 NBSP、0xAD 软连字符）不可打印。
            return r == ' ' || (r >= 0x21 && r <= 0x7E) || (r >= 0xA1 && r <= 0xAC) || (r >= 0xAE && r <= 0xFF);
        }
        var category = System.Text.Rune.GetUnicodeCategory(new System.Text.Rune(r));
        return category
            is UnicodeCategory.UppercaseLetter
            or UnicodeCategory.LowercaseLetter
            or UnicodeCategory.TitlecaseLetter
            or UnicodeCategory.ModifierLetter
            or UnicodeCategory.OtherLetter
            or UnicodeCategory.NonSpacingMark
            or UnicodeCategory.SpacingCombiningMark
            or UnicodeCategory.EnclosingMark
            or UnicodeCategory.DecimalDigitNumber
            or UnicodeCategory.LetterNumber
            or UnicodeCategory.OtherNumber
            or UnicodeCategory.ConnectorPunctuation
            or UnicodeCategory.DashPunctuation
            or UnicodeCategory.OpenPunctuation
            or UnicodeCategory.ClosePunctuation
            or UnicodeCategory.InitialQuotePunctuation
            or UnicodeCategory.FinalQuotePunctuation
            or UnicodeCategory.OtherPunctuation
            or UnicodeCategory.MathSymbol
            or UnicodeCategory.CurrencySymbol
            or UnicodeCategory.ModifierSymbol
            or UnicodeCategory.OtherSymbol;
    }
}

/// <summary>Go: <c>type hoverWithVerbosity struct</c>（fourslash.go:3054-3057）。</summary>
internal sealed class HoverWithVerbosity : IJsonMarshaler
{
    /// <summary>Go: <c>Hover *lsproto.Hover `json:"hover"`</c>。</summary>
    public Hover? Hover;

    /// <summary>Go: <c>VerbosityLevel int `json:"verbosityLevel"`</c>。</summary>
    public int VerbosityLevel;

    public void MarshalJSONTo(Encoder encoder)
    {
        encoder.WriteToken(BeginObject);
        encoder.WriteToken(JsonToken.String("hover"));
        MarshalEncode(encoder, Hover);
        encoder.WriteToken(JsonToken.String("verbosityLevel"));
        MarshalEncode(encoder, VerbosityLevel);
        encoder.WriteToken(EndObject);
    }
}
