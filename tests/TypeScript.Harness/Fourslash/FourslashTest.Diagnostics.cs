// ported from: internal/fourslash/fourslash.go:5263-5693
//
// 偏离清单（相对 Go 原文逐条；C# 语言特性 / 仓库规范 / 兄弟切片约定）：
// 1) Go 未导出的 `verifyDiagnostics` 与已导出的 `VerifyDiagnostics` 在 C# 里都会写成 `VerifyDiagnostics`
//    ⇒ 按约定给未导出的那个加后缀 `Core`：`VerifyDiagnosticsCore`（仅本文件内调用）。
// 2) `defer reset()`（ConfigureWithReset 的返回值）→ `try/finally`（`t.Fatal` 在 C# 里是抛异常展开，
//    与 Go 的 runtime.Goexit 一样会执行 finally）。
// 3) `core.StringifyJson(hint, "", "  ")` 在 Go 里返回 `(string, error)`，出错时 `t.Fatalf(prefix+...)`；
//    C# 的 `Core.StringifyJson` 只返回 string（出错抛 JsonException，见 Core.cs 文件头）⇒ 错误分支消失，
//    同时 `prefix` 在 C# 里再没有别的用途（Go 只在错误消息里用它）⇒ 不声明该局部变量（否则 CS0219 被
//    TreatWarningsAsErrors 判错）。
// 4) `map[lsproto.Range]bool`（Go 的 Range 是值类型、可比较）→ `HashSet<string>`，键由 `RangeKey` 生成的
//    "line:char:line:char" 串（等价的**值**语义）。C# 的 `LsProto.Range` 是引用类型，不能直接做 map 键。
// 5) `for i := range file.Content`（Go 遍历 string 的**字节**下标，且只在每个 rune 起始处回调）→ C# 按
//    **码点起始**遍历 UTF-16 下标（高位代理 + 低位代理一起前进 2；孤立代理前进 1，与 Go 对非法字节的
//    RuneError(size 1) 行为一致，见 docs/architecture.md §3.1）。
// 6) `slices.SortFunc`（不稳定）→ `Array.Sort`/`List.Sort`（同样不稳定）；`slices.SortStableFunc`（稳定）
//    → `OrderBy`（LINQ 排序稳定，与 Go 一致；先例 Ls/NodeBuilderImpl.Symbols.cs:687）。
// 7) `assertDeepEqual(t, a, b, msg, diagnosticsIgnoreOpts)` 的 cmp 选项在 C# 侧不存在，FsCompletions 切片
//    提供 `IgnoreDiagnosticFields`（把 `.Severity`/`.Source`/`.RelatedInformation` 置空）做等价投影：本文件对
//    **两侧**先投影，再用 `FsCmp.Diff` + `t.Fatal(msg + ":\n" + diff)`，逐字保留 Go 的 `"%s:\n%s"` 形状
//    （`FsCmp.AssertDeepEqual` 不带消息参数，凡 Go 传了 prefix 的地方都用这个形状，含 `VerifyLinkedEditing`）。
// 8) `fourslashDiagnostic` / `fourslashDiagnosticFile` 是 Go 未导出类型 ⇒ C# `FourslashDiagnostic` /
//    `FourslashDiagnosticFile`（`internal sealed`，命名空间顶层）。Go 里 `fourslashDiagnosticFile` 还实现了
//    `OriginalFileName`/`OriginalText`/`SpanMap`，但 `diagnosticwriter.FileLike` 接口只要求
//    `FileName`/`Text`/`ECMALineMap`（见 DiagnosticWriter.cs 的接口与 OriginalTextFile/RenamedFile 实现）
//    ⇒ C# 不实现这三个方法（无任何调用点）。
// 9) Go 的 `fourslashDiagnostic` 有 `reportsUnnecessary`/`reportsDeprecated` 两个字段，但全包内**只有声明、
//    没有任何读写**（fourslash.go:5515-5516）⇒ C# 不声明（否则 CS0169 被 TreatWarningsAsErrors 判错）。
// 10) Go 的 `t.Fatalf("%v", ptr)` 打印 `&{...}`，C# 的 `ToString()` 只打印类型名 ⇒ 失败消息改用
//     `Core.StringifyJson(..., "", "")`/`ComparePositions` 无关的自描述文本。失败消息**不进 baseline**，
//     只影响失败可读性（已在交付说明中标注）。
// 11) `AnyTextEdits` / `NoTextEdits`（fourslash.go:5689-5692）不在本文件的函数清单里，但落在 5263-5693 行区间；
//     按主 agent 与 FsCompletions 的约定由本文件声明（`internal static readonly`，两个**各自独立**的空数组，
//     以还原 Go 里两个不同指针的身份语义）。必须是 `new TextEdit?[0]` 而不能写 `[]`：Roslyn 会把空集合表达式
//     折成同一个 `Array.Empty<T>()` 单例，两个字段就会引用相等，Go 的指针哨兵语义（FsCompletions 用
//     `ReferenceEquals` 比较）随之失效。
// 12) Go 的 `f.testData.Files` 是本包 `[]*TestFileInfo`；C# 侧 `TestData.Files` 是 `TestFileInfo[]?`
//     ⇒ 遍历处写 `_testData!.Files ?? []`（Go 遍历 nil 切片不迭代）。
// 13) `TestConverters` 在 C# 里**不继承** `Converters`（FsCore 冻结表：内层用 `_converters.Converters` 拿）
//     ⇒ Go 靠嵌入提升的 `f.converters.ToLSPRange(...)` 写成 `_converters.Converters.ToLSPRange(...)`。
// 14) `hint.ViewRange`/`Position` 等 LSP 字段的 uint 下标：`Position.Line`/`Character` 是 uint32
//     （Go 也是 uint32），数组下标处显式 `(int)`（与 Go 的隐式转换等价）。
using System.Globalization;
using System.Text;
using TypeScript.Core;
using TypeScript.Diagnostics;
using TypeScript.DiagnosticWriter;
using TypeScript.LsConv;
using TypeScript.LsProto;
using TypeScript.LsUtil;
using TypeScript.TestUtil;
using TypeScript.TestUtil.HarnessUtil;
using TypeScript.TestUtil.TsBaseline;
using static TypeScript.Core.Core;
using static TypeScript.LsConv.LsConv;
using static TypeScript.LsProto.LsProto;
using static TypeScript.TSPath.TSPath;
using Range = TypeScript.LsProto.Range;
using LspDiagnostic = TypeScript.LsProto.Diagnostic;
using TestFile = TypeScript.TestUtil.HarnessUtil.TestFile;

namespace TypeScript.Harness.Fourslash;

public sealed partial class FourslashTest
{
    /// <summary>Go: <c>var AnyTextEdits = new([]*lsproto.TextEdit)</c>（fourslash.go:5689）。
    /// 两个**不同**的空数组实例（不能用 `[]` 集合表达式：Roslyn 会折成同一个 Array.Empty&lt;T&gt;() 单例，
    /// 而 Go 的两个哨兵指针必须靠引用相等区分）。</summary>
    internal static readonly TextEdit?[] AnyTextEdits = new TextEdit?[0];

    /// <summary>Go: <c>var NoTextEdits = new([]*lsproto.TextEdit)</c>（fourslash.go:5690）。见上条注释。</summary>
    internal static readonly TextEdit?[] NoTextEdits = new TextEdit?[0];

    /// <summary>Go: <c>func (f *FourslashTest) VerifyBaselineInlayHints(t, span *lsproto.Range, testPreferences *lsutil.UserPreferences)</c>（fourslash.go:5263-5338）。</summary>
    public void VerifyBaselineInlayHints(IHarnessTest t, Range? span, UserPreferences? testPreferences)
    {
        var fileName = _activeFilename;
        Range lspRange;
        if (span is null)
        {
            (lspRange, _) = _converters.Converters.ToLSPRange(GetScriptInfo(fileName)!, NewTextRange(0, _scriptInfos[fileName]._content.Length));
        }
        else
        {
            lspRange = span;
        }

        var @params = new InlayHintParams
        {
            TextDocument = new TextDocumentIdentifier { Uri = FileNameToDocumentURI(fileName) },
            Range = lspRange,
        };

        var preferences = testPreferences ?? LsUtil.LsUtil.NewDefaultUserPreferences();
        var reset = ConfigureWithReset(t, preferences);
        try
        {
            var result = SendRequest(t, TextDocumentInlayHintInfo, @params);
            var fileLines = GetScriptInfo(fileName)!._content.Split('\n');
            string[] annotations = [];
            if (result.InlayHints is not null)
            {
                var hints = result.InlayHints;
                Array.Sort(hints, static (a, b) => ComparePositions(a!.Position, b!.Position));
                annotations = Map(hints, hint =>
                {
                    var h = hint!;
                    var parts = h.Label.InlayHintLabelParts;
                    if (parts is not null)
                    {
                        foreach (var part in parts)
                        {
                            // Avoid diffs caused by lib file updates.
                            var location = part!.Location;
                            if (location is not null && IsLibFile(location.Uri.FileName()))
                            {
                                location.Range.Start = new Position { Line = 0, Character = 0 };
                                location.Range.End = new Position { Line = 0, Character = 0 };
                            }
                        }
                    }

                    var underline = new string(' ', (int)h.Position.Character) + "^";
                    var hintJson = StringifyJson(h, "", "  ");
                    var annotation = fileLines[(int)h.Position.Line];
                    annotation += "\n" + underline + "\n" + hintJson;
                    return annotation;
                });
            }

            if (annotations.Length == 0)
            {
                annotations = ["=== No inlay hints ==="];
            }

            AddResultToBaseline(t, BaselineCommand.InlayHints, string.Join("\n\n", annotations));
        }
        finally
        {
            reset();
        }
    }

    /// <summary>Go: <c>func (f *FourslashTest) VerifyBaselineLinkedEditing(t *testing.T)</c>（fourslash.go:5340-5418）。</summary>
    public void VerifyBaselineLinkedEditing(IHarnessTest t)
    {
        var baselineBuilder = new StringBuilder();
        var offset = 0;

        // write to baseline in order of file appearance in test data
        foreach (var file in _testData!.Files ?? [])
        {
            baselineBuilder.Append("// === Linked Editing ===\n");
            baselineBuilder.Append($"=== {file.FileName()} ===\n");
            var results = new List<LinkedEditingRanges>();
            var found = new HashSet<string>();

            // request linkedEditing at every position in the file
            var content = file.Content;
            for (var i = 0; i < content.Length; i += char.IsHighSurrogate(content[i]) && i + 1 < content.Length && char.IsLowSurrogate(content[i + 1]) ? 2 : 1)
            {
                var @params = new LinkedEditingRangeParams
                {
                    TextDocument = new TextDocumentIdentifier
                    {
                        Uri = FileNameToDocumentURI(file.FileName()),
                    },
                    Position = _converters.PositionToLineAndCharacter(GetScriptInfo(file.FileName())!, i),
                };
                var result = SendRequest(t, TextDocumentLinkedEditingRangeInfo, @params);
                var linkedEditingRanges = result.LinkedEditingRanges;
                if (linkedEditingRanges is not null
                    && linkedEditingRanges.Ranges is { Length: > 0 }
                    && found.Add(RangeKey(linkedEditingRanges.Ranges[0])))
                {
                    results.Add(linkedEditingRanges);
                }
            }

            if (results.Count == 0)
            {
                baselineBuilder.Append($"{file.Content}\n\n--No linked edits found--\n\n\n");
                continue;
            }

            // sort entries in each file
            results.Sort(static (a, b) => ComparePositions(a.Ranges![0].Start, b.Ranges![0].Start));
            var baselineDetails = new List<BaselineDetail>();
            var foundEditInfoBuilder = new StringBuilder();
            foreach (var edit in results)
            {
                baselineDetails.Add(new BaselineDetail
                {
                    Pos = edit.Ranges![0].Start,
                    PositionMarker = $"[|/*{offset}*/",
                });
                baselineDetails.Add(new BaselineDetail
                {
                    Pos = edit.Ranges![0].End,
                    PositionMarker = "|]",
                });
                baselineDetails.Add(new BaselineDetail
                {
                    Pos = edit.Ranges![1].Start,
                    PositionMarker = $"[|/*{offset}*/",
                });
                baselineDetails.Add(new BaselineDetail
                {
                    Pos = edit.Ranges![1].End,
                    PositionMarker = "|]",
                });

                foundEditInfoBuilder.Append($"\n\n=== {offset} ===\n{StringifyJson(edit, "", "  ")}");
                offset++;
            }

            // sort baselineDetails by position
            var sortedDetails = baselineDetails.OrderBy(static d => d.Pos, Comparer<Position>.Create(static (a, b) => ComparePositions(a, b)));

            // write file content with inline annotations for linked edits
            var lastPosition = 0;
            foreach (var detail in sortedDetails)
            {
                var currentPosition = _converters.LineAndCharacterToPosition(GetScriptInfo(file.FileName())!, detail.Pos);
                baselineBuilder.Append(file.Content[lastPosition..(int)currentPosition]);
                baselineBuilder.Append(detail.PositionMarker);
                lastPosition = (int)currentPosition;
            }

            baselineBuilder.Append(file.Content[lastPosition..]);
            baselineBuilder.Append(foundEditInfoBuilder.ToString() + "\n\n\n");
        }

        WriteToBaseline(BaselineCommand.LinkedEditing, baselineBuilder.ToString());
    }

    /// <summary>Go: <c>func (f *FourslashTest) VerifyLinkedEditing(t *testing.T, markerNamesToExpected map[string][]lsproto.Range)</c>（fourslash.go:5420-5450）。</summary>
    public void VerifyLinkedEditing(IHarnessTest t, Dictionary<string, Range[]> markerNamesToExpected)
    {
        foreach (var (markerName, expectedRanges) in markerNamesToExpected)
        {
            GoToMarker(t, markerName);
            var @params = new LinkedEditingRangeParams
            {
                TextDocument = new TextDocumentIdentifier
                {
                    Uri = FileNameToDocumentURI(_activeFilename),
                },
                Position = _currentCaretPosition,
            };
            var result = SendRequest(t, TextDocumentLinkedEditingRangeInfo, @params);
            var actualRanges = result.LinkedEditingRanges;
            if (expectedRanges.Length == 0)
            {
                if (actualRanges is not null && actualRanges.Ranges is { Length: not 0 })
                {
                    t.Fatal($"Expected no linked editing ranges for marker '{markerName}', but found {StringifyJson(actualRanges, "", "")}");
                }

                continue;
            }

            if (actualRanges?.Ranges is null || actualRanges.Ranges.Length == 0)
            {
                t.Fatal($"Expected linked editing ranges for marker '{markerName}', but found none");
            }

            var openingDiff = FsCmp.Diff(actualRanges.Ranges[0], expectedRanges[0]);
            if (openingDiff != "")
            {
                t.Fatal($"Linked editing ranges for opening element do not match expected for marker '{markerName}':\n{openingDiff}");
            }

            var closingDiff = FsCmp.Diff(actualRanges.Ranges[1], expectedRanges[1]);
            if (closingDiff != "")
            {
                t.Fatal($"Linked editing ranges for closing element do not match expected for marker '{markerName}':\n{closingDiff}");
            }
        }
    }

    /// <summary>Go: <c>func (f *FourslashTest) VerifyDiagnostics(t *testing.T, expected []*lsproto.Diagnostic)</c>（fourslash.go:5452-5454）。</summary>
    public void VerifyDiagnostics(IHarnessTest t, LspDiagnostic[] expected)
    {
        VerifyDiagnosticsCore(t, expected, static _ => true);
    }

    /// <summary>
    /// Similar to `VerifyDiagnostics`, but excludes suggestion diagnostics returned from server.
    /// Go: <c>func (f *FourslashTest) VerifyNonSuggestionDiagnostics</c>（fourslash.go:5456-5459）。
    /// </summary>
    public void VerifyNonSuggestionDiagnostics(IHarnessTest t, LspDiagnostic[] expected)
    {
        VerifyDiagnosticsCore(t, expected, static d => !IsSuggestionDiagnostic(d));
    }

    /// <summary>
    /// Similar to `VerifyDiagnostics`, but includes only suggestion diagnostics returned from server.
    /// Go: <c>func (f *FourslashTest) VerifySuggestionDiagnostics</c>（fourslash.go:5461-5464）。
    /// </summary>
    public void VerifySuggestionDiagnostics(IHarnessTest t, LspDiagnostic[] expected)
    {
        VerifyDiagnosticsCore(t, expected, IsSuggestionDiagnostic);
    }

    /// <summary>Go: <c>func (f *FourslashTest) verifyDiagnostics(t, expected, filterDiagnostics)</c>（fourslash.go:5466-5488）。</summary>
    internal void VerifyDiagnosticsCore(IHarnessTest t, LspDiagnostic[] expected, Func<LspDiagnostic?, bool> filterDiagnostics)
    {
        var actualDiagnostics = GetDiagnostics(t, _activeFilename);
        actualDiagnostics = Filter(actualDiagnostics, filterDiagnostics);
        var emptyRange = new Range();
        var expectedWithRanges = new LspDiagnostic[expected.Length];
        for (var i = 0; i < expected.Length; i++)
        {
            var diag = expected[i];
            if (IsEmptyRange(diag.Range, emptyRange))
            {
                var rangesInFile = GetRangesInFile(_activeFilename);
                if (rangesInFile.Length == 0)
                {
                    t.Fatal($"No ranges found in file {_activeFilename} to assign to diagnostic with empty range");
                }

                expectedWithRanges[i] = CloneDiagnosticWithRange(diag, rangesInFile[0].LSRange);
            }
            else
            {
                expectedWithRanges[i] = diag;
            }
        }

        if (actualDiagnostics.Length == 0 && expectedWithRanges.Length == 0)
        {
            return;
        }

        // Go: assertDeepEqual(t, actualDiagnostics, expectedWithRanges, "Diagnostics do not match expected", diagnosticsIgnoreOpts)
        var diff = FsCmp.Diff(IgnoreDiagnosticFields(actualDiagnostics), IgnoreDiagnosticFields(expectedWithRanges));
        if (diff != "")
        {
            t.Fatal("Diagnostics do not match expected:\n" + diff);
        }
    }

    /// <summary>Go: <c>func (f *FourslashTest) getDiagnostics(t *testing.T, fileName string) []*lsproto.Diagnostic</c>（fourslash.go:5490-5499）。</summary>
    internal LspDiagnostic?[]? GetDiagnostics(IHarnessTest t, string fileName)
    {
        var @params = new DocumentDiagnosticParams
        {
            TextDocument = new TextDocumentIdentifier
            {
                Uri = FileNameToDocumentURI(fileName),
            },
        };
        var result = SendRequest(t, TextDocumentDiagnosticInfo, @params);
        if (result.FullDocumentDiagnosticReport is not null)
        {
            return result.FullDocumentDiagnosticReport.Items;
        }

        return null;
    }

    /// <summary>Go: <c>func isSuggestionDiagnostic(diag *lsproto.Diagnostic) bool</c>（fourslash.go:5481-5483）。</summary>
    internal static bool IsSuggestionDiagnostic(LspDiagnostic? diag)
    {
        return diag!.Severity is not null && diag.Severity == DiagnosticSeverity.Hint;
    }

    /// <summary>Go: <c>func (f *FourslashTest) VerifyBaselineNonSuggestionDiagnostics(t *testing.T)</c>（fourslash.go:5485-5506）。</summary>
    public void VerifyBaselineNonSuggestionDiagnostics(IHarnessTest t)
    {
        var diagnostics = new List<FourslashDiagnostic>();
        var files = new List<TestFile>();
        foreach (var (fileName, scriptInfo) in _scriptInfos)
        {
            if (HasJSONFileExtension(fileName))
            {
                continue;
            }

            files.Add(new TestFile { UnitName = fileName, Content = scriptInfo._content });
            var lspDiagnostics = Filter(
                GetDiagnostics(t, fileName),
                static d => !IsSuggestionDiagnostic(d)
            );
            diagnostics.AddRange(Map(lspDiagnostics, d => ToDiagnostic(scriptInfo, d!)));
        }

        files.Sort(static (a, b) => string.CompareOrdinal(a.UnitName, b.UnitName));
        var result = TsBaseline.GetErrorBaseline(t, [.. files], [.. diagnostics], CompareDiagnostics, false /*pretty*/);
        AddResultToBaseline(t, BaselineCommand.NonSuggestionDiagnostics, result);
    }

    /// <summary>Go: <c>func (f *FourslashTest) toDiagnostic(scriptInfo *scriptInfo, lspDiagnostic *lsproto.Diagnostic) *fourslashDiagnostic</c>（fourslash.go:5554-5607）。</summary>
    internal FourslashDiagnostic ToDiagnostic(ScriptInfo scriptInfo, LspDiagnostic lspDiagnostic)
    {
        var category = lspDiagnostic.Severity!.Value switch
        {
            DiagnosticSeverity.Error => TypeScript.Diagnostics.Category.Error,
            DiagnosticSeverity.Warning => TypeScript.Diagnostics.Category.Warning,
            DiagnosticSeverity.Information => TypeScript.Diagnostics.Category.Message,
            DiagnosticSeverity.Hint => TypeScript.Diagnostics.Category.Suggestion,
            _ => TypeScript.Diagnostics.Category.Error,
        };
        var code = lspDiagnostic.Code!.Integer!.Value;

        FourslashDiagnostic[] relatedDiagnostics = [];
        if (lspDiagnostic.RelatedInformation is not null)
        {
            var related = new List<FourslashDiagnostic>();
            foreach (var info in lspDiagnostic.RelatedInformation)
            {
                var relatedInfo = info!;
                var relatedScriptInfo = GetScriptInfo(relatedInfo.Location.Uri.FileName());
                if (relatedScriptInfo is null)
                {
                    continue;
                }

                related.Add(new FourslashDiagnostic
                {
                    _file = new FourslashDiagnosticFile
                    {
                        _file = new TestFile { UnitName = relatedScriptInfo._fileName, Content = relatedScriptInfo._content },
                    },
                    _loc = FromLSPRange(relatedScriptInfo, relatedInfo.Location.Range),
                    _code = code,
                    _category = category,
                    _message = relatedInfo.Message,
                });
            }

            relatedDiagnostics = [.. related];
        }

        return new FourslashDiagnostic
        {
            _file = new FourslashDiagnosticFile
            {
                _file = new TestFile
                {
                    UnitName = scriptInfo._fileName,
                    Content = scriptInfo._content,
                },
            },
            _loc = FromLSPRange(scriptInfo, lspDiagnostic.Range),
            _code = code,
            _category = category,
            _message = lspDiagnostic.Message.AsString(),
            _relatedDiagnostics = relatedDiagnostics,
        };
    }

    /// <summary>Go: <c>func compareDiagnostics(d1, d2 *fourslashDiagnostic) int</c>（fourslash.go:5609-5630）。</summary>
    internal static int CompareDiagnostics(FourslashDiagnostic d1, FourslashDiagnostic d2)
    {
        var c = string.CompareOrdinal(d1._file.FileName(), d2._file.FileName());
        if (c != 0)
        {
            return c;
        }

        c = d1.Pos() - d2.Pos();
        if (c != 0)
        {
            return c;
        }

        c = d1.End() - d2.End();
        if (c != 0)
        {
            return c;
        }

        c = d1.Code() - d2.Code();
        if (c != 0)
        {
            return c;
        }

        c = string.CompareOrdinal(d1._message, d2._message);
        if (c != 0)
        {
            return c;
        }

        return CompareRelatedDiagnostics(d1._relatedDiagnostics, d2._relatedDiagnostics);
    }

    /// <summary>Go: <c>func compareRelatedDiagnostics(d1, d2 []*fourslashDiagnostic) int</c>（fourslash.go:5632-5642）。</summary>
    internal static int CompareRelatedDiagnostics(FourslashDiagnostic[] d1, FourslashDiagnostic[] d2)
    {
        var c = d2.Length - d1.Length;
        if (c != 0)
        {
            return c;
        }

        for (var i = 0; i < d1.Length; i++)
        {
            c = CompareDiagnostics(d1[i], d2[i]);
            if (c != 0)
            {
                return c;
            }
        }

        return 0;
    }

    /// <summary>
    /// Go 的 <c>lsproto.Range</c> 值比较（Go 直接写 <c>diag.Range == emptyRange</c>）：
    /// C# 的 Range 是引用类型，这里按 Start/End 的 Line/Character 逐字段比较（LSRange 语义）。
    /// </summary>
    private static bool IsEmptyRange(Range range, Range emptyRange)
    {
        return CompareRanges(range, emptyRange) == 0;
    }

    /// <summary>
    /// Go 的 <c>diagWithRange := *diag; diagWithRange.Range = ...</c>（浅拷贝后只替换 Range）：
    /// C# 的 `LsProto.Diagnostic` 是 sealed class，逐字段浅拷贝以保持 Go 的"不动调用方对象"语义。
    /// </summary>
    private static LspDiagnostic CloneDiagnosticWithRange(LspDiagnostic d, Range range)
    {
        return new LspDiagnostic
        {
            Range = range,
            Severity = d.Severity,
            Code = d.Code,
            CodeDescription = d.CodeDescription,
            Source = d.Source,
            Message = d.Message,
            Tags = d.Tags,
            RelatedInformation = d.RelatedInformation,
            Data = d.Data,
        };
    }

    /// <summary>
    /// Go 的 <c>map[lsproto.Range]bool</c> 的键（C# 的 Range 是引用类型，无法直接做 map 键）：
    /// 组合 Start/End 的 Line/Character，得到与 Go 值相等一致的键。
    /// </summary>
    internal static string RangeKey(Range range)
    {
        return string.Concat(
            range.Start.Line.ToString(CultureInfo.InvariantCulture),
            ":",
            range.Start.Character.ToString(CultureInfo.InvariantCulture),
            ":",
            range.End.Line.ToString(CultureInfo.InvariantCulture),
            ":",
            range.End.Character.ToString(CultureInfo.InvariantCulture)
        );
    }
}

/// <summary>Go: <c>type fourslashDiagnosticFile struct =&gt; diagnosticwriter.FileLike</c>（fourslash.go:5519-5542）。</summary>
internal sealed class FourslashDiagnosticFile : FileLike
{
    internal TestFile _file = new();
    private TextPos[]? _ecmaLineMap;

    public string FileName()
    {
        return _file.UnitName;
    }

    public string Text()
    {
        return _file.Content;
    }

    public TextPos[] ECMALineMap()
    {
        if (_ecmaLineMap is null)
        {
            _ecmaLineMap = [.. ComputeECMALineStarts(_file.Content)];
        }

        return _ecmaLineMap;
    }
}

/// <summary>Go: <c>type fourslashDiagnostic struct =&gt; diagnosticwriter.Diagnostic</c>（fourslash.go:5508-5517）。</summary>
internal sealed class FourslashDiagnostic : DiagnosticWriter.Diagnostic
{
    internal FourslashDiagnosticFile _file = new();
    internal TextRange _loc;
    internal int _code;
    internal TypeScript.Diagnostics.Category _category;
    internal string _message = "";
    internal FourslashDiagnostic[] _relatedDiagnostics = [];

    public FileLike File()
    {
        return _file;
    }

    public int Pos()
    {
        return _loc.Pos();
    }

    public int End()
    {
        return _loc.End();
    }

    public int Len()
    {
        return _loc.Len();
    }

    public int Code()
    {
        return _code;
    }

    public TypeScript.Diagnostics.Category Category()
    {
        return _category;
    }

    public string Source()
    {
        return "";
    }

    public string Localize(TypeScript.Locale.Locale locale)
    {
        return _message;
    }

    public DiagnosticWriter.Diagnostic[]? MessageChain()
    {
        return null;
    }

    public DiagnosticWriter.Diagnostic[] RelatedInformation()
    {
        var relatedInfo = new DiagnosticWriter.Diagnostic[_relatedDiagnostics.Length];
        for (var i = 0; i < relatedInfo.Length; i++)
        {
            relatedInfo[i] = _relatedDiagnostics[i];
        }

        return relatedInfo;
    }
}
