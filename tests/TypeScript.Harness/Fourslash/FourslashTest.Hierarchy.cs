// ported from: internal/fourslash/fourslash.go:3417-4180
//   VerifyBaselineCallHierarchy 3417-3442、callHierarchyItemDirection 3444-3451、callHierarchyItemKey 3453-3457、
//   symbolKindToLowercase 3459-3461、formatCallHierarchyItem 3463-3610、formatCallHierarchyItemSpan 3612-3711、
//   computeLineStarts 3713-3721、formatCallHierarchyItemSpans 3723-3735、
//   VerifyBaselineDocumentHighlights 3737-3742、VerifyBaselineDocumentHighlightsWithOptions 3744-3770、
//   verifyBaselineDocumentHighlights 3772-3860、lookupMarkersOrGetRanges 3862-3877、
//   roundtripThroughJson 3879-3896、Insert 3898-3903、InsertLine 3905-3909、Backspace 3911-3928、
//   DeleteAtCaret 3930-3941、Paste 3943-3970、ReplaceLine 3972-3977、selectLine 3979-3990、selectRange 3992-3999、
//   getSelection 4001-4016、applyTextEdits 4018-4053、Replace 4055-4059、replaceWorker 4061-4066、
//   typeText 4068-4112、editScriptAndUpdateMarkers 4114-4118、editScriptAndUpdateMarkersWorker 4120-4151、
//   updatePosition 4153-4162、fromLSPRange 4164-4170、editScript 4172-4194、getScriptInfo 4196-4198、
//   getOrLoadScriptInfo 4200-4210（注：Go 侧行号含上方两个函数之间的空行，见"由 FourslashTest.cs 提供"一节）。
//
// **本范围里由 FourslashTest.cs（FsCore 的冻结表）提供的成员，本文件不重复定义，只调用**：
//   `RoundtripThroughJson<T>`（Go roundtripThroughJson）、`ApplyTextEdits`（Go applyTextEdits）、
//   `EditScriptAndUpdateMarkers` / `EditScriptAndUpdateMarkersWorker`（Go 同名）、`UpdatePosition`（Go updatePosition）、
//   `FromLSPRange`（Go fromLSPRange）、`EditScript`（Go editScript，返回 `ScriptInfo?`）、
//   `GetScriptInfo` / `GetOrLoadScriptInfo`（Go 同名，返回 `ScriptInfo?`）。
//
// 与 Go 的偏离（逐条）：
// 1) 基线命令常量 `callHierarchyCmd` / `documentHighlightsCmd` 由 BaselineUtil.cs（FsBaseline）的 `BaselineCommand`
//    提供 → 写 `BaselineCommand.CallHierarchy` / `BaselineCommand.DocumentHighlights`。
// 2) Go 未导出类型：`callHierarchyItemDirection` → `internal enum CallHierarchyItemDirection`（常量 `...Root/...Incoming/
//    ...Outgoing` → `Root/Incoming/Outgoing`）；`callHierarchyItemKey`（Go 的 map 键 struct）→ `internal readonly record
//    struct CallHierarchyItemKey`，并**手写** `Equals`/`GetHashCode`：Go 的 `lsproto.Range` 是值 struct（按 Start/End 逐字段比），
//    而 C# 的 `LsProto.Range`/`Position` 是 class（引用相等）⇒ 必须逐字段比，才能得到与 Go map 键一致的语义。
//    注意 `symbolKindToLowercase`（Go 未导出包级函数）→ `SymbolKindToLowercase`。
// 3) 闭包/包级函数 → `private static`：`ComputeLineStarts`（Go computeLineStarts）、`FormatCallHierarchyItem`、
//    `FormatCallHierarchyItemSpan`、`FormatCallHierarchyItemSpans`、`SymbolKindToLowercase`。
//    `formatCallHierarchyItem` / `formatCallHierarchyItemSpan` 的 `t *testing.T` 参数保留（`SendRequest` 需要 t）。
// 4) `fi, ok := f.testData.MarkerPositions[name]; if !ok { t.Fatalf(...) }` → `TryGetValue` + `t.Fatal`。
// 5) `VerifyBaselineDocumentHighlights` 的 `...MarkerOrRangeOrName`（Go `any`）→ `params object?[]`；`*Marker`/`*RangeMarker`
//    的 type switch → C# 的 `case string` / `case Marker` / `case RangeMarker`。Go 未导出的 `verifyBaselineDocumentHighlights`
//    与导出的 `VerifyBaselineDocumentHighlights` 同名 → C# 加 `Core` 后缀（本仓 §2 约定）。
//    `%T`（错误消息里的类型名）→ C# 的类型名（仅失败路径，不影响基线）。
// 6) `preferences *lsutil.UserPreferences` 在这两个函数里 Go 侧就**未被使用**（纯签名遗留）→ C# 保留参数（`UserPreferences?`，
//    struct 的 nil → `Nullable<UserPreferences>`），同样不使用。
// 7) **字节 vs 代码单元**：Go 的 `computeLineStarts` 用 `for i := range content` 得到 **UTF-8 字节**下标，
//    `file.content[lineStart:lineEnd]` / `len(lineContent)` 也都是字节；而 `LineAndCharacterToPosition` 返回的
//    `core.TextPos` 是 **UTF-16 代码单元**偏移（本仓内部坐标，见 Core/Text.cs 文件头）。Go 在这里混用两种坐标。
//    C# 侧统一按 UTF-16 代码单元：行首表按 `char` 下标、切片用范围运算符、长度用 `Length`（纯 ASCII 文本逐字节等同）。
//    同理 `Paste`/`TypeText` 的 `len(text)` 用 `text.Length`（Go 是 UTF-8 字节数）。
// 8) `utf8.DecodeRuneInString(text[totalSize:])` → `Scanner.DecodeRuneAt(text, totalSize, out size)`（仓库既有映射：
//    size 是消耗的 UTF-16 代码单元数）；孤立代理项在 Go 里会走"非法 UTF-8"路径并插入 U+FFFD，这里用
//    `DecodeRuneAt` 的返回值（代理项自身的值）直接拼回一个代码单元，效果相同（JSON 上同样是一个孤立代理项）。
// 9) `defer func(){ f.reportFormatOnTypeCrash = true }()` → `try/finally`。
// 10) `strings.TrimRight(lineContent, "\r\n")` → `TrimEnd('\r', '\n')`；`strings.Repeat(" ", n)` → `new string(' ', n)`；
//     `max(a, b)` → `Math.Max(a, b)`。
// 11) `fmt.Sprintf` 里的 `%d` → `Int(...)`（InvariantCulture 的十进制；该帮助函数定义在 FourslashTest.Hover.cs
//     里，两个 partial 文件共享同一个类，不能重复定义）。
// 12) `strings.TrimSuffix(result.String(), "\n")` → `EndsWith('\n') ? s[..^1] : s`（只去掉**一个**结尾换行，
//     与 Go 一致；`TrimEnd('\n')` 会在结尾有多个换行时不同）。
using System.Text;
using TypeScript.LsProto;
using TypeScript.LsUtil;
using TypeScript.TestUtil;
using static TypeScript.Core.Core;
using static TypeScript.LsConv.LsConv;
using static TypeScript.LsProto.LsProto;
using static TypeScript.Scanner.Scanner;
using Range = TypeScript.LsProto.Range;

namespace TypeScript.Harness.Fourslash;

public sealed partial class FourslashTest
{
    /// <summary>Go: <c>func (f *FourslashTest) VerifyBaselineCallHierarchy(t *testing.T)</c>（fourslash.go:3417-3442）。</summary>
    public void VerifyBaselineCallHierarchy(IHarnessTest t)
    {
        var fileName = _activeFilename;
        var position = _currentCaretPosition;

        var parameters = new CallHierarchyPrepareParams
        {
            TextDocument = new TextDocumentIdentifier { Uri = FileNameToDocumentURI(fileName) },
            Position = position,
        };

        var prepareResult = SendRequest(t, TextDocumentPrepareCallHierarchyInfo, parameters);
        if (prepareResult.CallHierarchyItems is null || prepareResult.CallHierarchyItems.Length == 0)
        {
            AddResultToBaseline(t, BaselineCommand.CallHierarchy, "No call hierarchy items available");
            return;
        }

        var result = new StringBuilder();

        foreach (var callHierarchyItem in prepareResult.CallHierarchyItems)
        {
            var seen = new HashSet<CallHierarchyItemKey>();
            var itemFileName = callHierarchyItem!.Uri.FileName();
            var script = GetOrLoadScriptInfo(itemFileName)!;
            FormatCallHierarchyItem(t, this, script, result, callHierarchyItem, CallHierarchyItemDirection.Root, seen, "");
        }

        // Go: strings.TrimSuffix(result.String(), "\n")（只去掉**一个**结尾换行，不是 TrimEnd）。
        var baselineText = result.ToString();
        AddResultToBaseline(t, BaselineCommand.CallHierarchy, baselineText.EndsWith('\n') ? baselineText[..^1] : baselineText);
    }

    /// <summary>Go: <c>func symbolKindToLowercase(kind lsproto.SymbolKind) string</c>（fourslash.go:3459-3461）。</summary>
    private static string SymbolKindToLowercase(SymbolKind kind) =>
        TypeScript.StringUtil.StringUtil.ToLowerJS(kind.String());

    /// <summary>Go: <c>func formatCallHierarchyItem(...)</c>（fourslash.go:3463-3610）。</summary>
    private static void FormatCallHierarchyItem(
        IHarnessTest t,
        FourslashTest f,
        ScriptInfo file,
        StringBuilder result,
        CallHierarchyItem callHierarchyItem,
        CallHierarchyItemDirection direction,
        HashSet<CallHierarchyItemKey> seen,
        string prefix)
    {
        var key = new CallHierarchyItemKey(callHierarchyItem.Uri, callHierarchyItem.Range, direction);
        var alreadySeen = seen.Contains(key);
        seen.Add(key);

        var incomingCallsSkip = false;
        var incomingCallsSeen = false;
        CallHierarchyIncomingCall?[] incomingCallsValues = [];
        var outgoingCallsSkip = false;
        var outgoingCallsSeen = false;
        CallHierarchyOutgoingCall?[] outgoingCallsValues = [];

        if (direction == CallHierarchyItemDirection.Outgoing)
        {
            incomingCallsSkip = true;
        }
        else if (alreadySeen)
        {
            incomingCallsSeen = true;
        }
        else
        {
            var incomingParams = new CallHierarchyIncomingCallsParams { Item = callHierarchyItem };
            var incomingResult = f.SendRequest(t, CallHierarchyIncomingCallsInfo, incomingParams);
            if (incomingResult.CallHierarchyIncomingCalls is not null)
            {
                incomingCallsValues = incomingResult.CallHierarchyIncomingCalls;
            }
        }

        if (direction == CallHierarchyItemDirection.Incoming)
        {
            outgoingCallsSkip = true;
        }
        else if (alreadySeen)
        {
            outgoingCallsSeen = true;
        }
        else
        {
            var outgoingParams = new CallHierarchyOutgoingCallsParams { Item = callHierarchyItem };
            var outgoingResult = f.SendRequest(t, CallHierarchyOutgoingCallsInfo, outgoingParams);
            if (outgoingResult.CallHierarchyOutgoingCalls is not null)
            {
                outgoingCallsValues = outgoingResult.CallHierarchyOutgoingCalls;
            }
        }

        var trailingPrefix = prefix;
        result.Append($"{prefix}╭ name: {callHierarchyItem.Name}\n");
        result.Append($"{prefix}├ kind: {SymbolKindToLowercase(callHierarchyItem.Kind)}\n");
        if (callHierarchyItem.Detail is not null && callHierarchyItem.Detail != "")
        {
            result.Append($"{prefix}├ containerName: {callHierarchyItem.Detail}\n");
        }
        result.Append($"{prefix}├ file: {callHierarchyItem.Uri.FileName()}\n");
        result.Append(prefix);
        result.Append("├ span:\n");
        FormatCallHierarchyItemSpan(f, file, result, callHierarchyItem.Range, prefix + "│ ", prefix + "│ ");
        result.Append(prefix);
        result.Append("├ selectionSpan:\n");
        FormatCallHierarchyItemSpan(f, file, result, callHierarchyItem.SelectionRange, prefix + "│ ", prefix + "│ ");

        // Handle incoming calls
        if (incomingCallsSeen)
        {
            if (outgoingCallsSkip)
            {
                result.Append(trailingPrefix);
                result.Append("╰ incoming: ...\n");
            }
            else
            {
                result.Append(prefix);
                result.Append("├ incoming: ...\n");
            }
        }
        else if (!incomingCallsSkip)
        {
            if (incomingCallsValues.Length == 0)
            {
                if (outgoingCallsSkip)
                {
                    result.Append(trailingPrefix);
                    result.Append("╰ incoming: none\n");
                }
                else
                {
                    result.Append(prefix);
                    result.Append("├ incoming: none\n");
                }
            }
            else
            {
                result.Append(prefix);
                result.Append("├ incoming:\n");
                for (var i = 0; i < incomingCallsValues.Length; i++)
                {
                    var incomingCall = incomingCallsValues[i]!;
                    var fromFileName = incomingCall.From!.Uri.FileName();
                    var fromFile = f.GetOrLoadScriptInfo(fromFileName)!;
                    result.Append(prefix);
                    result.Append("│ ╭ from:\n");
                    FormatCallHierarchyItem(t, f, fromFile, result, incomingCall.From, CallHierarchyItemDirection.Incoming, seen, prefix + "│ │ ");
                    result.Append(prefix);
                    result.Append("│ ├ fromSpans:\n");

                    var fromSpansTrailingPrefix = trailingPrefix + "╰ ╰ ";
                    if (i < incomingCallsValues.Length - 1)
                    {
                        fromSpansTrailingPrefix = prefix + "│ ╰ ";
                    }
                    else if (!outgoingCallsSkip && (!outgoingCallsSeen || outgoingCallsValues.Length > 0))
                    {
                        fromSpansTrailingPrefix = prefix + "│ ╰ ";
                    }
                    FormatCallHierarchyItemSpans(f, fromFile, result, incomingCall.FromRanges ?? [], prefix + "│ │ ", fromSpansTrailingPrefix);
                }
            }
        }

        // Handle outgoing calls
        if (outgoingCallsSeen)
        {
            result.Append(trailingPrefix);
            result.Append("╰ outgoing: ...\n");
        }
        else if (!outgoingCallsSkip)
        {
            if (outgoingCallsValues.Length == 0)
            {
                result.Append(trailingPrefix);
                result.Append("╰ outgoing: none\n");
            }
            else
            {
                result.Append(prefix);
                result.Append("├ outgoing:\n");
                for (var i = 0; i < outgoingCallsValues.Length; i++)
                {
                    var outgoingCall = outgoingCallsValues[i]!;
                    var toFileName = outgoingCall.To!.Uri.FileName();
                    var toFile = f.GetOrLoadScriptInfo(toFileName)!;
                    result.Append(prefix);
                    result.Append("│ ╭ to:\n");
                    FormatCallHierarchyItem(t, f, toFile, result, outgoingCall.To, CallHierarchyItemDirection.Outgoing, seen, prefix + "│ │ ");
                    result.Append(prefix);
                    result.Append("│ ├ fromSpans:\n");

                    var fromSpansTrailingPrefix = trailingPrefix + "╰ ╰ ";
                    if (i < outgoingCallsValues.Length - 1)
                    {
                        fromSpansTrailingPrefix = prefix + "│ ╰ ";
                    }
                    FormatCallHierarchyItemSpans(f, file, result, outgoingCall.FromRanges ?? [], prefix + "│ │ ", fromSpansTrailingPrefix);
                }
            }
        }
    }

    /// <summary>Go: <c>func formatCallHierarchyItemSpan(...)</c>（fourslash.go:3612-3711）。</summary>
    private static void FormatCallHierarchyItemSpan(
        FourslashTest f,
        ScriptInfo file,
        StringBuilder result,
        Range span,
        string prefix,
        string closingPrefix)
    {
        var startLc = span.Start;
        var endLc = span.End;
        var startPos = f._converters.LineAndCharacterToPosition(file, span.Start);
        var endPos = f._converters.LineAndCharacterToPosition(file, span.End);

        // Compute line starts for the file
        var lineStarts = ComputeLineStarts(file._content);

        // Find the line boundaries - expand to full lines
        var contextStart = (int)startPos;
        var contextEnd = (int)endPos;

        // Expand to start of first line
        while (contextStart > 0 && file._content[contextStart - 1] != '\n' && file._content[contextStart - 1] != '\r')
        {
            contextStart--;
        }

        // Expand to end of last line
        while (contextEnd < file._content.Length && file._content[contextEnd] != '\n' && file._content[contextEnd] != '\r')
        {
            contextEnd++;
        }

        // Get actual line and character positions for the context
        var contextStartLine = (int)startLc.Line;
        var contextEndLine = (int)endLc.Line;

        // Calculate line number padding
        var lineNumWidth = Int(contextEndLine + 1).Length + 2;

        result.Append($"{prefix}╭ {file.FileName()}:{Int((int)(startLc.Line + 1))}:{Int((int)(startLc.Character + 1))}-{Int((int)(endLc.Line + 1))}:{Int((int)(endLc.Character + 1))}\n");

        for (var lineNum = contextStartLine; lineNum <= contextEndLine; lineNum++)
        {
            var lineStart = lineStarts[lineNum];
            var lineEnd = file._content.Length;
            if (lineNum + 1 < lineStarts.Length)
            {
                lineEnd = lineStarts[lineNum + 1];
            }

            // Get the line content, trimming trailing newlines
            var lineContent = file._content[lineStart..lineEnd];
            lineContent = lineContent.TrimEnd('\r', '\n');

            // Format with line number
            var lineNumStr = Int(lineNum + 1) + ":";
            var paddedLineNum = new string(' ', lineNumWidth - lineNumStr.Length - 1) + lineNumStr;
            if (lineContent == "")
            {
                result.Append($"{prefix}│ {paddedLineNum}\n");
            }
            else
            {
                result.Append($"{prefix}│ {paddedLineNum} {lineContent}\n");
            }

            // Add selection carets if this line contains part of the span
            if (lineNum >= (int)startLc.Line && lineNum <= (int)endLc.Line)
            {
                var selStart = 0;
                var selEnd = lineContent.Length;

                if (lineNum == (int)startLc.Line)
                {
                    selStart = (int)startLc.Character;
                }
                if (lineNum == (int)endLc.Line)
                {
                    selEnd = (int)endLc.Character;
                }

                // Don't show carets for empty selections
                var isEmpty = startLc.Line == endLc.Line && startLc.Character == endLc.Character;
                if (isEmpty)
                {
                    // For empty selections, show a single "<" character
                    var padding = new string(' ', lineNumWidth + selStart);
                    result.Append($"{prefix}│ {padding}<\n");
                }
                else
                {
                    // Calculate selection length (at least 1)
                    var selLength = selEnd - selStart;
                    selLength = Math.Max(selLength, 1); // Trim to actual content on the line
                    if (lineNum < (int)endLc.Line)
                    {
                        // For lines before the last, trim to line content length
                        if (selEnd > lineContent.Length)
                        {
                            selEnd = lineContent.Length;
                            selLength = selEnd - selStart;
                        }
                    }

                    var padding = new string(' ', lineNumWidth + selStart);
                    // Go 的 strings.Repeat 在负count 上 panic；这里钳到 0 以免抛出另一种异常（仅影响拼写错误的输入）。
                    var carets = new string('^', Math.Max(selLength, 0));
                    result.Append($"{prefix}│ {padding}{carets}\n");
                }
            }
        }

        result.Append(closingPrefix);
        result.Append("╰\n");
    }

    /// <summary>Go: <c>func computeLineStarts(content string) []int</c>（fourslash.go:3713-3721）。</summary>
    private static int[] ComputeLineStarts(string content)
    {
        var lineStarts = new List<int> { 0 };
        for (var i = 0; i < content.Length; i++)
        {
            if (content[i] == '\n')
            {
                lineStarts.Add(i + 1);
            }
        }
        return [.. lineStarts];
    }

    /// <summary>Go: <c>func formatCallHierarchyItemSpans(...)</c>（fourslash.go:3723-3735）。</summary>
    private static void FormatCallHierarchyItemSpans(
        FourslashTest f,
        ScriptInfo file,
        StringBuilder result,
        Range[] spans,
        string prefix,
        string trailingPrefix)
    {
        for (var i = 0; i < spans.Length; i++)
        {
            var closingPrefix = prefix;
            if (i == spans.Length - 1)
            {
                closingPrefix = trailingPrefix;
            }
            FormatCallHierarchyItemSpan(f, file, result, spans[i], prefix, closingPrefix);
        }
    }

    /// <summary>Go: <c>func (f *FourslashTest) VerifyBaselineDocumentHighlights(t *testing.T, preferences *lsutil.UserPreferences, markerOrRangeOrNames ...MarkerOrRangeOrName)</c>（fourslash.go:3737-3742）。</summary>
    public void VerifyBaselineDocumentHighlights(
        IHarnessTest t,
        UserPreferences? preferences,
        params object?[] markerOrRangeOrNames) =>
        VerifyBaselineDocumentHighlightsWithOptions(t, preferences, null /*filesToSearch*/, markerOrRangeOrNames);

    /// <summary>Go: <c>func (f *FourslashTest) VerifyBaselineDocumentHighlightsWithOptions(...)</c>（fourslash.go:3744-3770）。</summary>
    public void VerifyBaselineDocumentHighlightsWithOptions(
        IHarnessTest t,
        UserPreferences? preferences,
        string[]? filesToSearch,
        params object?[] markerOrRangeOrNames)
    {
        var markerOrRanges = new List<MarkerOrRange>();
        foreach (var markerOrRangeOrName in markerOrRangeOrNames)
        {
            switch (markerOrRangeOrName)
            {
                case string name:
                    if (!_testData!.MarkerPositions.TryGetValue(name, out var marker))
                    {
                        t.Fatal($"Marker '{name}' not found");
                    }
                    markerOrRanges.Add(marker);
                    break;
                case Marker markerValue:
                    markerOrRanges.Add(markerValue);
                    break;
                case RangeMarker rangeMarker:
                    markerOrRanges.Add(rangeMarker);
                    break;
                default:
                    t.Fatal($"Invalid marker or range type: {markerOrRangeOrName?.GetType().Name ?? "<nil>"}. Expected string, Marker, or RangeMarker.");
                    break;
            }
        }

        VerifyBaselineDocumentHighlightsCore(t, preferences, filesToSearch, [.. markerOrRanges]);
    }

    /// <summary>Go: <c>func (f *FourslashTest) verifyBaselineDocumentHighlights(...)</c>（fourslash.go:3772-3860）。</summary>
    private void VerifyBaselineDocumentHighlightsCore(
        IHarnessTest t,
        UserPreferences? preferences,
        string[]? filesToSearch,
        MarkerOrRange[] markerOrRanges)
    {
        foreach (var markerOrRange in markerOrRanges)
        {
            GoToMarker(t, markerOrRange);

            var spans = new List<Location>();
            var header = "";

            if (filesToSearch is not null && filesToSearch.Length > 0)
            {
                // Multi-file: use the custom method.
                var searchURIs = Map(filesToSearch, static file => FileNameToDocumentURI(file));

                var parameters = new MultiDocumentHighlightParams
                {
                    TextDocument = new TextDocumentIdentifier { Uri = FileNameToDocumentURI(_activeFilename) },
                    Position = _currentCaretPosition,
                    FilesToSearch = searchURIs,
                };
                var result = SendRequest(t, CustomTextDocumentMultiDocumentHighlightInfo, parameters);
                var multiHighlights = result.MultiDocumentHighlights ?? [];

                foreach (var mh in multiHighlights)
                {
                    foreach (var h in mh!.Highlights ?? [])
                    {
                        spans.Add(new Location
                        {
                            Uri = mh.Uri,
                            Range = h!.Range,
                        });
                    }
                }

                var sb = new StringBuilder();
                sb.Append("// filesToSearch:\n");
                foreach (var file in filesToSearch)
                {
                    sb.Append($"//   {file}\n");
                }
                sb.Append('\n');
                header = sb.ToString();
            }
            else
            {
                // Single-file: use the standard LSP method.
                var parameters = new DocumentHighlightParams
                {
                    TextDocument = new TextDocumentIdentifier { Uri = FileNameToDocumentURI(_activeFilename) },
                    Position = _currentCaretPosition,
                };
                var result = SendRequest(t, TextDocumentDocumentHighlightInfo, parameters);
                var highlights = result.DocumentHighlights ?? [];

                foreach (var h in highlights)
                {
                    spans.Add(new Location
                    {
                        Uri = FileNameToDocumentURI(_activeFilename),
                        Range = h!.Range,
                    });
                }
            }

            // Add result to baseline
            AddResultToBaseline(t, BaselineCommand.DocumentHighlights, header + GetBaselineForLocationsWithFileContents(
                [.. spans],
                new BaselineFourslashLocationsOptions
                {
                    Marker = markerOrRange,
                    MarkerName = "/*HIGHLIGHTS*/",
                }));
        }
    }

    /// <summary>Go: <c>func (f *FourslashTest) lookupMarkersOrGetRanges(t *testing.T, markers []string) []MarkerOrRange</c>（fourslash.go:3862-3877）。</summary>
    internal MarkerOrRange[] LookupMarkersOrGetRanges(IHarnessTest t, string[] markers)
    {
        if (markers.Length == 0)
        {
            // Collects all named markers if provided, or defaults to anonymous ranges
            return Map<RangeMarker, MarkerOrRange>(_testData!.Ranges ?? [], static r => r);
        }

        return Map(markers, markerName =>
        {
            if (!_testData!.MarkerPositions.TryGetValue(markerName, out var marker))
            {
                t.Fatal($"Marker '{markerName}' not found");
            }
            return (MarkerOrRange)marker;
        });
    }

    /// <summary>Go: <c>func (f *FourslashTest) Insert(t *testing.T, text string)</c>（fourslash.go:3898-3903）。</summary>
    public void Insert(IHarnessTest t, string text)
    {
        // Insert text at the current caret position.
        BaselineState(t);
        TypeText(t, text);
    }

    /// <summary>Go: <c>func (f *FourslashTest) InsertLine(t *testing.T, text string)</c>（fourslash.go:3905-3909）。</summary>
    public void InsertLine(IHarnessTest t, string text)
    {
        // Insert text and a new line at the current caret position.
        BaselineState(t);
        TypeText(t, text + "\n");
    }

    /// <summary>Go: <c>func (f *FourslashTest) Backspace(t *testing.T, count int)</c>（fourslash.go:3911-3928）。</summary>
    public void Backspace(IHarnessTest t, int count)
    {
        // Removes the text at the current caret position as if the user pressed backspace `count` times.
        var script = GetScriptInfo(_activeFilename)!;
        var offset = (int)_converters.LineAndCharacterToPosition(script, _currentCaretPosition);
        BaselineState(t);

        for (var i = 0; i < count; i++)
        {
            offset--;
            EditScriptAndUpdateMarkers(t, _activeFilename, offset, offset + 1, "");
            _currentCaretPosition = _converters.PositionToLineAndCharacter(script, offset);
            // Don't need to examine formatting because there are no formatting changes on backspace.
        }

        // f.checkPostEditInvariants() // !!! do we need this?
    }

    /// <summary>Go: <c>func (f *FourslashTest) DeleteAtCaret(t *testing.T, count int)</c>（fourslash.go:3930-3941）。</summary>
    public void DeleteAtCaret(IHarnessTest t, int count)
    {
        // DeleteAtCaret removes the text at the current caret position as if the user pressed delete `count` times.
        var script = GetScriptInfo(_activeFilename)!;
        var offset = (int)_converters.LineAndCharacterToPosition(script, _currentCaretPosition);
        BaselineState(t);

        for (var i = 0; i < count; i++)
        {
            EditScriptAndUpdateMarkers(t, _activeFilename, offset, offset + 1, "");
            // Position stays the same after delete (unlike backspace)
        }
    }

    /// <summary>Go: <c>func (f *FourslashTest) Paste(t *testing.T, text string)</c>（fourslash.go:3943-3970）。</summary>
    public void Paste(IHarnessTest t, string text)
    {
        // Enters text as if the user had pasted it.
        var script = GetScriptInfo(_activeFilename)!;
        var start = (int)_converters.LineAndCharacterToPosition(script, _currentCaretPosition);
        BaselineState(t);
        EditScriptAndUpdateMarkers(t, _activeFilename, start, start, text);

        // post-paste fomatting
        if (_stateEnableFormatting)
        {
            var result = SendRequestAndBaselineWorker(t, TextDocumentRangeFormattingInfo, new DocumentRangeFormattingParams
            {
                TextDocument = new TextDocumentIdentifier
                {
                    Uri = FileNameToDocumentURI(_activeFilename),
                },
                Range = new Range
                {
                    Start = _currentCaretPosition,
                    End = _converters.PositionToLineAndCharacter(script, start + text.Length),
                },
                Options = _userPreferences.FormatCodeSettings.ToLSFormatOptions(),
            }, false);
            if (result.TextEdits is not null)
            {
                ApplyTextEdits(t, result.TextEdits);
            }
        }
        // this.checkPostEditInvariants(); // !!! do we need this?
    }

    /// <summary>Go: <c>func (f *FourslashTest) ReplaceLine(t *testing.T, lineIndex int, text string)</c>（fourslash.go:3972-3977）。</summary>
    public void ReplaceLine(IHarnessTest t, int lineIndex, string text)
    {
        // Selects a line and replaces it with a new text.
        BaselineState(t);
        SelectLine(t, lineIndex);
        TypeText(t, text);
    }

    /// <summary>Go: <c>func (f *FourslashTest) selectLine(t *testing.T, lineIndex int)</c>（fourslash.go:3979-3990）。</summary>
    private void SelectLine(IHarnessTest t, int lineIndex)
    {
        var script = GetScriptInfo(_activeFilename)!;
        var start = script._lineMap.LineStarts[lineIndex];
        TypeScript.Core.TextPos end;
        if (lineIndex + 1 >= script._lineMap.LineStarts.Length)
        {
            end = script._content.Length;
        }
        else
        {
            end = script._lineMap.LineStarts[lineIndex + 1] - 1;
        }
        SelectRange(t, NewTextRange(start, end));
    }

    /// <summary>Go: <c>func (f *FourslashTest) selectRange(t *testing.T, textRange core.TextRange)</c>（fourslash.go:3992-3999）。</summary>
    private void SelectRange(IHarnessTest t, TypeScript.Core.TextRange textRange)
    {
        var script = GetScriptInfo(_activeFilename)!;
        var start = _converters.PositionToLineAndCharacter(script, textRange.Pos());
        var end = _converters.PositionToLineAndCharacter(script, textRange.End());
        GoToPosition(t, start);
        _selectionEnd = end;
    }

    /// <summary>Go: <c>func (f *FourslashTest) getSelection() core.TextRange</c>（fourslash.go:4001-4016）。</summary>
    internal TypeScript.Core.TextRange GetSelection()
    {
        var script = GetScriptInfo(_activeFilename)!;
        if (_selectionEnd is null)
        {
            return NewTextRange(
                _converters.LineAndCharacterToPosition(script, _currentCaretPosition),
                _converters.LineAndCharacterToPosition(script, _currentCaretPosition));
        }
        return NewTextRange(
            _converters.LineAndCharacterToPosition(script, _currentCaretPosition),
            _converters.LineAndCharacterToPosition(script, _selectionEnd));
    }

    /// <summary>Go: <c>func (f *FourslashTest) Replace(t *testing.T, start int, length int, text string)</c>（fourslash.go:4055-4059）。</summary>
    public void Replace(IHarnessTest t, int start, int length, string text)
    {
        BaselineState(t);
        ReplaceWorker(t, start, length, text);
    }

    /// <summary>Go: <c>func (f *FourslashTest) replaceWorker(t *testing.T, start int, length int, text string)</c>（fourslash.go:4061-4066）。</summary>
    private void ReplaceWorker(IHarnessTest t, int start, int length, string text)
    {
        EditScriptAndUpdateMarkers(t, _activeFilename, start, start + length, text);
        // f.checkPostEditInvariants() // !!! do we need this?
    }

    /// <summary>Go: <c>func (f *FourslashTest) typeText(t *testing.T, text string)</c>（fourslash.go:4068-4112）。</summary>
    private void TypeText(IHarnessTest t, string text)
    {
        // temprorary -- this disables tests failing if format crashes; this unblocks unrelated tests such as codefixes
        _reportFormatOnTypeCrash = false;
        try
        {
            var script = GetScriptInfo(_activeFilename)!;
            var selection = GetSelection();
            ReplaceWorker(t, selection.Pos(), selection.End() - selection.Pos(), "");

            var totalSize = 0;

            var offset = (int)_converters.LineAndCharacterToPosition(script, _currentCaretPosition);
            while (totalSize < text.Length)
            {
                // Go: `r, size := utf8.DecodeRuneInString(text[totalSize:])`（本仓按 UTF-16 代码单元走位）。
                var r = DecodeRuneAt(text, totalSize, out var size);
                var runeText = r is >= 0xD800 and <= 0xDFFF ? ((char)r).ToString() : char.ConvertFromUtf32(r);
                EditScriptAndUpdateMarkers(t, _activeFilename, offset, offset, runeText);

                totalSize += size;
                offset += size;
                _currentCaretPosition = _converters.PositionToLineAndCharacter(script, offset);

                // Handle post-keystroke formatting
                if (_stateEnableFormatting)
                {
                    var result = SendRequestAndBaselineWorker(t, TextDocumentOnTypeFormattingInfo, new DocumentOnTypeFormattingParams
                    {
                        TextDocument = new TextDocumentIdentifier
                        {
                            Uri = FileNameToDocumentURI(_activeFilename),
                        },
                        Position = _currentCaretPosition,
                        Ch = runeText,
                        Options = _userPreferences.FormatCodeSettings.ToLSFormatOptions(),
                    }, false);
                    if (result.TextEdits is not null)
                    {
                        offset += ApplyTextEdits(t, result.TextEdits);
                    }
                }
            }

            // f.checkPostEditInvariants() // !!! do we need this?
        }
        finally
        {
            _reportFormatOnTypeCrash = true;
        }
    }
}

/// <summary>Go: <c>type callHierarchyItemDirection int</c>（fourslash.go:3444-3451）。</summary>
internal enum CallHierarchyItemDirection
{
    Root,
    Incoming,
    Outgoing,
}

/// <summary>
/// Go: <c>type callHierarchyItemKey struct { uri lsproto.DocumentUri; range_ lsproto.Range; direction callHierarchyItemDirection }</c>
/// （fourslash.go:3453-3457）。Go 的 struct 键按值比较；C# 的 <see cref="DocumentUri"/> 自带值相等，但
/// <see cref="LsProto.Range"/>/<see cref="Position"/> 是 class（引用相等）⇒ 这里手写逐字段比较。
/// </summary>
internal readonly record struct CallHierarchyItemKey(DocumentUri Uri, Range Range, CallHierarchyItemDirection Direction)
{
    public bool Equals(CallHierarchyItemKey other) =>
        Uri.Equals(other.Uri)
        && PositionEquals(Range.Start, other.Range.Start)
        && PositionEquals(Range.End, other.Range.End)
        && Direction == other.Direction;

    public override int GetHashCode() => HashCode.Combine(
        Uri,
        Range.Start.Line,
        Range.Start.Character,
        Range.End.Line,
        Range.End.Character,
        Direction);

    private static bool PositionEquals(Position left, Position right) =>
        left.Line == right.Line && left.Character == right.Character;
}
