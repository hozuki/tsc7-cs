// ported from: internal/fourslash/fourslash.go:5694-6046
//
// 偏离清单（相对 Go 原文逐条；C# 语言特性 / 仓库规范 / 兄弟切片约定）：
// 1) `verifyBaselineDefinitions`（fourslash.go:2715）由 FsImportsNav 的 FourslashTest.Navigation.cs 提供，
//    签名 `void VerifyBaselineDefinitions(IHarnessTest t, BaselineCommand definitionCommand, string definitionMarker,
//    Func<IHarnessTest, FourslashTest, string, Position, LocationOrLocationsOrDefinitionLinksOrNull> getDefinitions,
//    bool includeOriginalSelectionRange, params string[] markers)`；本文件只调用。其 `getDefinitions` 回调声明为
//    非空返回，Go 的 `sendRequest` 结果按 FsImportsNav 约定写 `result ?? new LocationOrLocationsOrDefinitionLinksOrNull()`。
// 2) `VerifyWorkspaceSymbolCase.Includes`/`Exact` 在 Go 里是 `*[]*lsproto.SymbolInformation`（"nil 指针" vs
//    "空切片"可区分）⇒ C# 用 `SymbolInformation[]?`（null = 未设置，空数组 = 空切片），语义一一对应；
//    `Preferences *lsutil.UserPreferences` ⇒ `UserPreferences?`（C# 侧 UserPreferences 是 struct）。
// 3) `verifyExactSymbols`/`verifyIncludesSymbols` 是 Go 包级函数 ⇒ C# `internal static` 成员（宿主 FourslashTest，
//    与 FsCore 冻结表里 `IsLibFile`/`GetBaseFileNameFromTest` 等包级函数的落点一致）。
// 4) `assertDeepEqual(t, a, b, prefix)` → `FsCmp.Diff` + `t.Fatal(prefix + ":\n" + diff)`：逐字保留 Go 的
//    `"%s:\n%s"` 形状（FsCmp.AssertDeepEqual 不带消息参数）。
// 5) Go 的 `map[key{name, location}]`（key 是匿名结构体，Location 是值类型、可比较）⇒ C# 用 `Dictionary<string, …>`，
//    键由 `SymbolLocationKey`（Name + Uri 值 + Range 的 line/char）拼出，保持**值**相等语义（C# 的 Location 是类）。
// 6) `%v`（Go 打印结构体字段，如 `{{0 0} {0 5}}`）在 C# 里没有对应 ToString ⇒ 失败消息用 `RangeToGoString`/
//    `LocationToGoString` 手工拼出与 Go 相同的形状（仅失败消息，不进 baseline）。`%q`（strconv.Quote）复用
//    FsCompletions 切片在 FourslashTest.Completions.cs 里定义的 `QuoteGo`（同一 partial class 的 private static，
//    跨文件可见）；本文件不重复定义。
// 7) `documentSpanKey` 是 Go 未导出类型 ⇒ C# `DocumentSpanKey`（`internal sealed`）。Go 的键按**值**比较
//    （uri + textSpan + contextSpan 三个值结构），C# 里 `DocumentUri` 本身就是值结构，两个 `Range` 是引用类型 ⇒
//    `Equals`/`GetHashCode` 按 `CompareRanges`/`RangeKey` 逐字段实现（与 Go 注释里"Deduplicate by value"一致）。
// 8) Go 的 `symbolBySpan` 是 map（遍历顺序随机）；C# 的 `Dictionary` 是插入顺序 ⇒ `spans` 顺序确定（更稳定，
//    且 `GetBaselineForSpansWithFileContents` 会按文件分组渲染，顺序差异不影响 baseline 内容）。
// 9) `_openFiles` 在 Go 里是 map（遍历顺序随机），C# 是 `HashSet<string>`（插入顺序）；`VerifyNoErrors` 只做
//    集合语义判断，不依赖顺序。
// 10) Go 的 `t.Fatalf` 是不返回的（runtime.Goexit），C# 的 `IHarnessTest.Fatal` 抛异常；编译器不把它当
//     `[DoesNotReturn]` 时，后续解引用要写 `!`（`startMarker!` 等），语义与 Go 相同（Fatal 之后不再执行）。
// 11) `updatePositionForTextEdit`/`removeWhitespace`/`assertValidTextRange`/`selectCodeFixDiagnostic`
//     （fourslash.go:6000-6046，落在本行区间尾端）按主 agent 的分工归 FsCompletions 切片 ⇒ 本文件**不移植**。
// 12) `VerifyNoErrors` 里 Go 的 `%v` 对 `[]string` 打印 `[a b]`（空格分隔）⇒ `"[" + string.Join(" ", messages) + "]"`。
// 13) `Strings.Repeat("  ", indent)` → `new string(' ', indent * 2)`（逐字节相同：两个空格 × indent）。
using System.Globalization;
using System.Text;
using TypeScript.LsConv;
using TypeScript.LsProto;
using TypeScript.LsUtil;
using TypeScript.TestUtil;
using TypeScript.TestUtil.HarnessUtil;
using static TypeScript.Core.Core;
using static TypeScript.LsConv.LsConv;
using static TypeScript.LsProto.LsProto;
using Range = TypeScript.LsProto.Range;
using LspDiagnostic = TypeScript.LsProto.Diagnostic;

namespace TypeScript.Harness.Fourslash;

/// <summary>Go: <c>type VerifyWorkspaceSymbolCase struct</c>（fourslash.go:5715-5720）。</summary>
public sealed class VerifyWorkspaceSymbolCase
{
    public string Pattern = "";
    public SymbolInformation[]? Includes;
    public SymbolInformation[]? Exact;
    public UserPreferences? Preferences;
}

/// <summary>
/// Go 的 <c>documentSpanKey</c>（fourslash.go:5858-5863）：按**值**去重 document symbol（不依赖指针身份）。
/// C# 的 `Range` 是引用类型，所以 `Equals`/`GetHashCode` 按字段值实现。
/// </summary>
internal sealed class DocumentSpanKey : IEquatable<DocumentSpanKey>
{
    public DocumentUri Uri;
    public Range TextSpan = new();
    public Range ContextSpan = new();

    public bool Equals(DocumentSpanKey? other)
    {
        return other is not null
            && Uri == other.Uri
            && CompareRanges(TextSpan, other.TextSpan) == 0
            && CompareRanges(ContextSpan, other.ContextSpan) == 0;
    }

    public override bool Equals(object? obj)
    {
        return Equals(obj as DocumentSpanKey);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(Uri, FourslashTest.RangeKey(TextSpan), FourslashTest.RangeKey(ContextSpan));
    }
}

public sealed partial class FourslashTest
{
    /// <summary>Go: <c>func (f *FourslashTest) VerifyBaselineGoToImplementation(t *testing.T, markerNames ...string)</c>（fourslash.go:5694-5713）。</summary>
    public void VerifyBaselineGoToImplementation(IHarnessTest t, params string[] markerNames)
    {
        VerifyBaselineDefinitions(
            t,
            BaselineCommand.GoToImplementation,
            "/*GOTO IMPL*/", /*definitionMarker*/
            static (t2, f, fileName, position) =>
            {
                var @params = new ImplementationParams
                {
                    TextDocument = new TextDocumentIdentifier
                    {
                        Uri = FileNameToDocumentURI(f._activeFilename),
                    },
                    Position = f._currentCaretPosition,
                };

                var result = f.SendRequest(t2, TextDocumentImplementationInfo, @params);
                return result ?? new LocationOrLocationsOrDefinitionLinksOrNull();
            },
            false, /*includeOriginalSelectionRange*/
            markerNames
        );
    }

    /// <summary>`verify.navigateTo` in Strada. Go: <c>func (f *FourslashTest) VerifyWorkspaceSymbol(t, cases)</c>（fourslash.go:5722-5756）。</summary>
    public void VerifyWorkspaceSymbol(IHarnessTest t, VerifyWorkspaceSymbolCase[] cases)
    {
        var originalPreferences = _userPreferences;
        foreach (var testCase in cases)
        {
            var preferences = testCase.Preferences ?? LsUtil.LsUtil.NewDefaultUserPreferences();
            Configure(t, preferences);
            var result = SendRequest(t, WorkspaceSymbolInfo, new WorkspaceSymbolParams
            {
                Query = testCase.Pattern,
                TextDocument = new TextDocumentIdentifier
                {
                    Uri = FileNameToDocumentURI(_activeFilename),
                },
            });
            if (result.SymbolInformations is null)
            {
                t.Fatal("Expected non-nil symbol information array from workspace symbol request");
            }

            if (testCase.Includes is not null)
            {
                if (testCase.Exact is not null)
                {
                    t.Fatal("Test case cannot have both 'Includes' and 'Exact' fields set");
                }

                VerifyIncludesSymbols(t, result.SymbolInformations, testCase.Includes, "Workspace symbols mismatch with pattern '" + testCase.Pattern + "'");
            }
            else
            {
                if (testCase.Exact is null)
                {
                    t.Fatal("Test case must have either 'Includes' or 'Exact' field set");
                }

                VerifyExactSymbols(t, result.SymbolInformations, testCase.Exact, "Workspace symbols mismatch with pattern '" + testCase.Pattern + "'");
            }
        }

        Configure(t, originalPreferences);
    }

    /// <summary>Go: <c>func verifyExactSymbols(t, actual, expected, prefix)</c>（fourslash.go:5758-5771）。</summary>
    internal static void VerifyExactSymbols(IHarnessTest t, SymbolInformation?[] actual, SymbolInformation?[] expected, string prefix)
    {
        if (actual.Length != expected.Length)
        {
            t.Fatal($"{prefix}: Expected {expected.Length} symbols, but got {actual.Length}:\n{FsCmp.Diff(actual, expected)}");
        }

        for (var i = 0; i < actual.Length; i++)
        {
            var diff = FsCmp.Diff(actual[i], expected[i]);
            if (diff != "")
            {
                t.Fatal(prefix + ":\n" + diff);
            }
        }
    }

    /// <summary>Go: <c>func verifyIncludesSymbols(t, actual, includes, prefix)</c>（fourslash.go:5773-5795）。</summary>
    internal static void VerifyIncludesSymbols(IHarnessTest t, SymbolInformation?[] actual, SymbolInformation?[] includes, string prefix)
    {
        var nameAndLocToActualSymbol = new Dictionary<string, SymbolInformation>(actual.Length);
        foreach (var sym in actual)
        {
            nameAndLocToActualSymbol[SymbolLocationKey(sym!)] = sym!;
        }

        foreach (var sym in includes)
        {
            if (!nameAndLocToActualSymbol.TryGetValue(SymbolLocationKey(sym!), out var actualSym))
            {
                t.Fatal($"{prefix}: Expected symbol '{sym!.Name}' at location '{LocationToGoString(sym.Location)}' not found");
            }

            var diff = FsCmp.Diff(actualSym, sym);
            if (diff != "")
            {
                t.Fatal($"{prefix}: Symbol '{sym!.Name}' at location '{LocationToGoString(sym.Location)}' mismatch:\n{diff}");
            }
        }
    }

    /// <summary>Go: <c>func (f *FourslashTest) VerifyBaselineDocumentSymbol(t *testing.T)</c>（fourslash.go:5797-5832）。</summary>
    public void VerifyBaselineDocumentSymbol(IHarnessTest t)
    {
        var @params = new DocumentSymbolParams
        {
            TextDocument = new TextDocumentIdentifier
            {
                Uri = FileNameToDocumentURI(_activeFilename),
            },
        };
        var result = SendRequest(t, TextDocumentDocumentSymbolInfo, @params);
        var uri = FileNameToDocumentURI(_activeFilename);
        var symbolBySpan = new Dictionary<DocumentSpanKey, DocumentSymbol>();
        if (result.DocumentSymbols is not null)
        {
            foreach (var symbol in result.DocumentSymbols)
            {
                CollectDocumentSymbolSpans(uri, symbol!, symbolBySpan);
            }
        }

        var spans = new List<DocumentSpan>(symbolBySpan.Count);
        foreach (var (key, symbol) in symbolBySpan)
        {
            spans.Add(new DocumentSpan { Uri = key.Uri, TextSpan = key.TextSpan, ContextSpan = symbol.Range });
        }

        AddResultToBaseline(
            t,
            BaselineCommand.DocumentSymbols,
            GetBaselineForSpansWithFileContents([.. spans], new BaselineFourslashLocationsOptions
            {
                GetLocationData = span =>
                {
                    var symbol = symbolBySpan[new DocumentSpanKey { Uri = span.Uri, TextSpan = span.TextSpan, ContextSpan = span.ContextSpan! }];
                    return $"{{| name: {symbol.Name}, kind: {symbol.Kind.String()} |}}";
                },
            })
        );

        var detailsBuilder = new StringBuilder();
        if (result.DocumentSymbols is not null)
        {
            WriteDocumentSymbolDetails(result.DocumentSymbols, 0, detailsBuilder);
        }

        WriteToBaseline(BaselineCommand.DocumentSymbols, "\n\n// === Details ===\n" + detailsBuilder.ToString());
    }

    /// <summary>Go: <c>func writeDocumentSymbolDetails(symbols, indent, builder)</c>（fourslash.go:5834-5842）。</summary>
    internal static void WriteDocumentSymbolDetails(DocumentSymbol?[] symbols, int indent, StringBuilder builder)
    {
        foreach (var symbol in symbols)
        {
            builder.Append(new string(' ', indent * 2) + $"({symbol!.Kind.String()}) {symbol.Name}\n");
            if (symbol.Children is not null)
            {
                WriteDocumentSymbolDetails(symbol.Children, indent + 1, builder);
            }
        }
    }

    /// <summary>Go: <c>func collectDocumentSymbolSpans(uri, symbol, symbolBySpan)</c>（fourslash.go:5844-5866）。</summary>
    internal static void CollectDocumentSymbolSpans(DocumentUri uri, DocumentSymbol symbol, Dictionary<DocumentSpanKey, DocumentSymbol> symbolBySpan)
    {
        // Deduplicate by value rather than by the documentSpan key, which holds a pointer to
        // the symbol's Range. The same logical symbol can be reached more than once
        // (e.g. a merged declaration), and depending on transport those occurrences may
        // be the same object (shared *Range) or independent copies (distinct *Range
        // after a JSON round-trip). A value-based key collapses them consistently.
        var key = new DocumentSpanKey { Uri = uri, TextSpan = symbol.SelectionRange, ContextSpan = symbol.Range };
        if (!symbolBySpan.ContainsKey(key))
        {
            symbolBySpan[key] = symbol;
        }

        if (symbol.Children is not null)
        {
            foreach (var child in symbol.Children)
            {
                CollectDocumentSymbolSpans(uri, child!, symbolBySpan);
            }
        }
    }

    /// <summary>VerifyNumberOfErrorsInCurrentFile verifies that the current file has the expected number of errors. Go: fourslash.go:5875-5884。</summary>
    public void VerifyNumberOfErrorsInCurrentFile(IHarnessTest t, int expectedCount)
    {
        var diagnostics = GetDiagnostics(t, _activeFilename);
        // Filter to only include errors (not suggestions/hints)
        var errors = Filter(diagnostics, static d => !IsSuggestionDiagnostic(d));
        if (errors.Length != expectedCount)
        {
            t.Fatal($"Expected {expectedCount} errors in current file, but got {errors.Length}");
        }
    }

    /// <summary>VerifyNoErrors verifies that no errors exist in any open files. Go: fourslash.go:5886-5901。</summary>
    public void VerifyNoErrors(IHarnessTest t)
    {
        foreach (var fileName in _openFiles)
        {
            var diagnostics = GetDiagnostics(t, fileName);
            // Filter to only include errors (not suggestions/hints)
            var errors = Filter(diagnostics, static d => !IsSuggestionDiagnostic(d));
            if (errors.Length > 0)
            {
                var messages = new List<string>();
                foreach (var err in errors)
                {
                    messages.Add(err!.Message.AsString());
                }

                t.Fatal($"Expected no errors but found {errors.Length} in {fileName}: [{string.Join(" ", messages)}]");
            }
        }
    }

    /// <summary>VerifyErrorExistsAtRange verifies that an error with the given code exists at the given range. Go: fourslash.go:5903-5920。</summary>
    public void VerifyErrorExistsAtRange(IHarnessTest t, RangeMarker rangeMarker, int code, string message)
    {
        var diagnostics = GetDiagnostics(t, rangeMarker.FileName());
        foreach (var diag in diagnostics ?? [])
        {
            if (diag!.Code is not null && diag.Code.Integer is not null && diag.Code.Integer.Value == code)
            {
                // Check if the range matches
                if (diag.Range.Start.Line == rangeMarker.LSRange.Start.Line
                    && diag.Range.Start.Character == rangeMarker.LSRange.Start.Character
                    && diag.Range.End.Line == rangeMarker.LSRange.End.Line
                    && diag.Range.End.Character == rangeMarker.LSRange.End.Character)
                {
                    // If message is provided, verify it matches
                    if (message != "" && diag.Message.AsString() != message)
                    {
                        t.Fatal($"Error at range has code {code} but message mismatch. Expected: {QuoteGo(message)}, Got: {QuoteGo(diag.Message.AsString())}");
                    }

                    return;
                }
            }
        }

        t.Fatal($"Expected error with code {code} at range {RangeToGoString(rangeMarker.LSRange)} but it was not found");
    }

    /// <summary>VerifyErrorExistsBetweenMarkers verifies that an error exists between the two markers. Go: fourslash.go:5922-5943。</summary>
    public void VerifyErrorExistsBetweenMarkers(IHarnessTest t, string startMarkerName, string endMarkerName)
    {
        if (!_testData!.MarkerPositions.TryGetValue(startMarkerName, out var startMarker))
        {
            t.Fatal($"Start marker '{startMarkerName}' not found");
        }

        if (!_testData.MarkerPositions.TryGetValue(endMarkerName, out var endMarker))
        {
            t.Fatal($"End marker '{endMarkerName}' not found");
        }

        if (startMarker!.FileName() != endMarker!.FileName())
        {
            t.Fatal($"Markers '{startMarkerName}' and '{endMarkerName}' are in different files");
        }

        var diagnostics = GetDiagnostics(t, startMarker.FileName());
        var startPos = startMarker.Position;
        var endPos = endMarker.Position;

        foreach (var diag in diagnostics ?? [])
        {
            if (!IsSuggestionDiagnostic(diag))
            {
                var scriptInfo = GetScriptInfo(startMarker.FileName())!;
                var diagStart = (int)_converters.LineAndCharacterToPosition(scriptInfo, diag!.Range.Start);
                var diagEnd = (int)_converters.LineAndCharacterToPosition(scriptInfo, diag.Range.End);
                if (diagStart >= startPos && diagEnd <= endPos)
                {
                    return; // Found an error in the range
                }
            }
        }

        t.Fatal($"Expected error between markers '{startMarkerName}' and '{endMarkerName}' but none was found");
    }

    /// <summary>VerifyErrorExistsAfterMarker verifies that an error exists after the given marker. Go: fourslash.go:5945-5970。</summary>
    public void VerifyErrorExistsAfterMarker(IHarnessTest t, string markerName)
    {
        string fileName;
        int markerPos;

        if (markerName == "")
        {
            // Use current position
            fileName = _activeFilename;
            markerPos = _converters.LineAndCharacterToPosition(GetScriptInfo(_activeFilename)!, _currentCaretPosition);
        }
        else
        {
            if (!_testData!.MarkerPositions.TryGetValue(markerName, out var marker))
            {
                t.Fatal($"Marker '{markerName}' not found");
            }

            fileName = marker!.FileName();
            markerPos = marker.Position;
        }

        var diagnostics = GetDiagnostics(t, fileName);

        foreach (var diag in diagnostics ?? [])
        {
            if (!IsSuggestionDiagnostic(diag))
            {
                var diagStart = (int)_converters.LineAndCharacterToPosition(GetScriptInfo(fileName)!, diag!.Range.Start);
                if (diagStart >= markerPos)
                {
                    return; // Found an error after the marker
                }
            }
        }

        t.Fatal($"Expected error after marker '{markerName}' but none was found");
    }

    /// <summary>VerifyErrorExistsBeforeMarker verifies that an error exists before the given marker. Go: fourslash.go:5972-5997。</summary>
    public void VerifyErrorExistsBeforeMarker(IHarnessTest t, string markerName)
    {
        string fileName;
        int markerPos;

        if (markerName == "")
        {
            // Use current position
            fileName = _activeFilename;
            markerPos = _converters.LineAndCharacterToPosition(GetScriptInfo(_activeFilename)!, _currentCaretPosition);
        }
        else
        {
            if (!_testData!.MarkerPositions.TryGetValue(markerName, out var marker))
            {
                t.Fatal($"Marker '{markerName}' not found");
            }

            fileName = marker!.FileName();
            markerPos = marker.Position;
        }

        var diagnostics = GetDiagnostics(t, fileName);

        foreach (var diag in diagnostics ?? [])
        {
            if (!IsSuggestionDiagnostic(diag))
            {
                var diagEnd = (int)_converters.LineAndCharacterToPosition(GetScriptInfo(fileName)!, diag!.Range.End);
                if (diagEnd <= markerPos)
                {
                    return; // Found an error before the marker
                }
            }
        }

        t.Fatal($"Expected error before marker '{markerName}' but none was found");
    }

    /// <summary>Go 的 <c>map[key{name string; loc lsproto.Location}]</c> 的键（C# 的 Location 是类）。</summary>
    private static string SymbolLocationKey(SymbolInformation sym)
    {
        return sym.Name + "\0" + sym.Location.Uri.Value + "\0" + RangeKey(sym.Location.Range);
    }

    /// <summary>Go 的 <c>%v</c> 对 <c>lsproto.Location</c> 的形状（失败消息用，不进 baseline）。</summary>
    private static string LocationToGoString(Location loc)
    {
        return "{" + loc.Uri.Value + " " + RangeToGoString(loc.Range) + "}";
    }

    /// <summary>Go 的 <c>%v</c> 对 <c>lsproto.Range</c> 的形状（失败消息用，不进 baseline）。</summary>
    private static string RangeToGoString(Range range)
    {
        return "{{"
            + range.Start.Line.ToString(CultureInfo.InvariantCulture)
            + " "
            + range.Start.Character.ToString(CultureInfo.InvariantCulture)
            + "} {"
            + range.End.Line.ToString(CultureInfo.InvariantCulture)
            + " "
            + range.End.Character.ToString(CultureInfo.InvariantCulture)
            + "}}";
    }
}
