// ported from: internal/fourslash/fourslash.go:2519-2930（VerifyBaselineFindAllReferences … VerifyFoldingRangeLines）
//
// 偏离清单（Go → C#）：
//  1) `t.Helper()` 删除（C# 无对应）。
//  2) Go 的 `defer reset()` → `Action? reset = …; try { … } finally { reset?.Invoke(); }`（C# 的 t.Fatal 抛异常，
//     等价于 Go 里 FailNow 仍会执行 defer）。
//  3) `assert.Assert(t, cond)` → `if (!cond) t.Fatal("assert failed")`；`assert.Equal(t, x, y)` → `FsCmp.AssertDeepEqual(t, x, y)`。
//  4) `f.activeFilename` / `f.currentCaretPosition` / `f.openFiles` / `f.isStradaServer` / `f.converters` 等未导出字段
//     按共享成员契约直取 `_activeFilename` / `_currentCaretPosition` / `_openFiles` / `_isStradaServer` / `_converters`。
//  5) Go 的值结构体在 C# 里是 class（`lsproto.Range`/`Position`/`DocumentSpan` 等），因此
//     `*originRange != *link.OriginSelectionRange` 与 `link.TargetRange != link.TargetSelectionRange` 用
//     `Range.Compare(...) != 0` 复刻（Compare 逐字段比 Line/Character，与 Go 的结构体 `!=` 等价）。
//  6) `slices.SortStableFunc` → 索引修饰的 `Array.Sort`（`Array.Sort` 不稳定，用原始下标打破并列以复刻稳定排序）；
//     `slices.Sorted(maps.Keys(m))` → `Array.Sort(keys, StringComparer.Ordinal)`（Go 的 string 比较是序数比较）。
//  7) `core.StringifyJson(result, "", "  ")` 的 `(string, error)`：C# 的 `Core.StringifyJson` 失败时抛异常
//     （Go 的 `t.Fatalf("Failed to stringify …")` 分支在 C# 里不可达）。
//  8) `roundtripThroughJson[[]lsproto.Location]` → `RoundtripThroughJson<Location[]>`（失败抛 JsonException，
//     对应 Go 的 error 分支 t.Fatalf）；返回 null 时按 Go 的零值 nil 切片处理为 `[]`。
//  9) nil 切片（`*result.Locations`、`*result.DefinitionLinks` 等）→ `?? []`；Go 遍历 nil 切片不迭代。
// 10) 元素为 `*T` 的切片在 C# 是 `T?[]`（如 `FoldingRange?[]`、`VSReferenceItem?[]`、`LocationLink?[]`），
//     解引用处用 `!` 保持 Go 的" nil 即 panic"语义。
// 11) `panic(...)` → `Debug.Fail(...); throw new System.Diagnostics.UnreachableException();`（`Debug` = 根命名空间
//     `TypeScript` 里的静态类，本文件命名空间链自带可见性，不写 `TypeScript.Debug.Debug`）。
// 12) `markers ...string` → `params string[] markers`；`FoldingRangeLineExpected` 是顶层 class（Go 值结构体）。
// 13) `lookupMarkersOrGetRanges`（fourslash.go:3850）由 FourslashTest.Hierarchy.cs（FsHoverHierarchy）提供
//     （`internal MarkerOrRange[] LookupMarkersOrGetRanges(IHarnessTest t, string[] markers)`），本文件只调用，
//     不重复定义。
namespace TypeScript.Harness.Fourslash;

using TypeScript.Collections;
using TypeScript.Core;
using TypeScript.LsProto;
using TypeScript.LsUtil;
using TypeScript.SpanMap;
using TypeScript.TestUtil;
using static TypeScript.LsConv.LsConv;
using static TypeScript.LsProto.LsProto;

public sealed partial class FourslashTest
{
    /// <summary>Go: <c>func (f *FourslashTest) VerifyBaselineFindAllReferences(t, markers ...string)</c>（fourslash.go:2519-2547）。</summary>
    public void VerifyBaselineFindAllReferences(IHarnessTest t, params string[] markers)
    {
        var referenceLocations = LookupMarkersOrGetRanges(t, markers);

        foreach (var markerOrRange in referenceLocations)
        {
            // worker in `baselineEachMarkerOrRange`
            GoToMarkerOrRange(t, markerOrRange);

            var @params = new ReferenceParams
            {
                TextDocument = new TextDocumentIdentifier { Uri = FileNameToDocumentURI(_activeFilename) },
                Position = _currentCaretPosition,
                Context = new ReferenceContext { IncludeDeclaration = true },
            };
            var result = SendRequest(t, TextDocumentReferencesInfo, @params);
            AddResultToBaseline(t, BaselineCommand.FindAllReferences, GetBaselineForLocationsWithFileContents(result.Locations ?? [], new BaselineFourslashLocationsOptions
            {
                Marker = markerOrRange,
                MarkerName = "/*FIND ALL REFS*/",
            }));
        }
    }

    /// <summary>Go: <c>func (f *FourslashTest) VerifyBaselineVSFindAllReferences(t, markers ...string)</c>（fourslash.go:2549-2621）。</summary>
    public void VerifyBaselineVSFindAllReferences(IHarnessTest t, params string[] markers)
    {
        var referenceLocations = LookupMarkersOrGetRanges(t, markers);

        foreach (var markerOrRange in referenceLocations)
        {
            GoToMarkerOrRange(t, markerOrRange);

            var @params = new ReferenceParams
            {
                TextDocument = new TextDocumentIdentifier { Uri = FileNameToDocumentURI(_activeFilename) },
                Position = _currentCaretPosition,
                Context = new ReferenceContext { IncludeDeclaration = true },
            };
            var result = SendRequest(t, TextDocumentVSReferencesInfo, @params);

            // Sort cross-project results for deterministic baselines
            if (result.VSReferenceItems is not null && result.VSReferenceItems.Length > 0)
            {
                var items = result.VSReferenceItems;
                var indices = new int[items.Length];
                for (var i = 0; i < indices.Length; i++)
                {
                    indices[i] = i;
                }
                Array.Sort(indices, (x, y) =>
                {
                    var c = CompareVSReferenceItems(items[x]!, items[y]!);
                    return c != 0 ? c : x - y;
                });
                var sorted = new VSReferenceItem?[items.Length];
                for (var i = 0; i < sorted.Length; i++)
                {
                    sorted[i] = items[indices[i]];
                }
                result.VSReferenceItems = sorted;

                // Re-number IDs sequentially after sort
                var idRemap = new Dictionary<int, int>(sorted.Length);
                for (var i = 0; i < sorted.Length; i++)
                {
                    var item = sorted[i]!;
                    idRemap[item.VSId] = i;
                    item.VSId = i;
                }
                foreach (var item in sorted)
                {
                    var referenceItem = item!;
                    if (referenceItem.VSDefinitionId is not null)
                    {
                        // Go: idRemap[*item.VSDefinitionId]（缺失键得零值）
                        idRemap.TryGetValue(referenceItem.VSDefinitionId.Value, out var newDefId);
                        referenceItem.VSDefinitionId = newDefId;
                    }
                }
            }

            // Include file contents with markers
            var locations = new List<Location>();
            foreach (var item in result.VSReferenceItems ?? [])
            {
                locations.Add(item!.VSLocation);
            }
            var fileContents = GetBaselineForLocationsWithFileContents(locations.ToArray(), new BaselineFourslashLocationsOptions
            {
                Marker = markerOrRange,
                MarkerName = "/*FIND ALL REFS*/",
            });

            AddResultToBaseline(t, BaselineCommand.VsFindAllReferences, fileContents + "\n\n" + Core.StringifyJson(result, "", "  "));
        }
    }

    /// <summary>Go: <c>func (f *FourslashTest) VerifyBaselineCodeLens(t, preferences *lsutil.UserPreferences)</c>（fourslash.go:2623-2685）。</summary>
    public void VerifyBaselineCodeLens(IHarnessTest t, UserPreferences? preferences)
    {
        var reset = preferences is null ? null : ConfigureWithReset(t, preferences.Value);
        try
        {
            var foundAtLeastOneCodeLens = false;
            var openFiles = _openFiles.ToArray();
            Array.Sort(openFiles, StringComparer.Ordinal);
            foreach (var openFile in openFiles)
            {
                var @params = new CodeLensParams
                {
                    TextDocument = new TextDocumentIdentifier { Uri = FileNameToDocumentURI(openFile) },
                };

                var unresolvedCodeLensList = SendRequest(t, TextDocumentCodeLensInfo, @params);
                if (unresolvedCodeLensList.CodeLenses is null || unresolvedCodeLensList.CodeLenses.Length == 0)
                {
                    continue;
                }
                foundAtLeastOneCodeLens = true;

                foreach (var unresolvedCodeLens in unresolvedCodeLensList.CodeLenses)
                {
                    if (unresolvedCodeLens is null)
                    {
                        t.Fatal("assert failed");
                    }
                    var resolvedCodeLens = SendRequest(t, CodeLensResolveInfo, unresolvedCodeLens);
                    if (resolvedCodeLens is null)
                    {
                        t.Fatal("assert failed");
                    }
                    if (resolvedCodeLens.Command is null)
                    {
                        t.Fatal("Expected resolved code lens to have a command.");
                    }
                    if (resolvedCodeLens.Command.Value.Length > 0)
                    {
                        FsCmp.AssertDeepEqual(t, resolvedCodeLens.Command.Value, ShowCodeLensLocationsCommandName);
                    }

                    Location[] locations;
                    // commandArgs: (DocumentUri, Position, Location[])
                    if (resolvedCodeLens.Command.Arguments is not null)
                    {
                        locations = RoundtripThroughJson<Location[]>(resolvedCodeLens.Command.Arguments[2]) ?? [];
                    }
                    else
                    {
                        locations = [];
                    }

                    var ranges = _converters.Converters.FromLSPRange(GetScriptInfo(openFile)!, resolvedCodeLens.Range, Feature.All);
                    if (ranges.Length != 1)
                    {
                        continue;
                    }
                    var codeLensRange = ranges[0].Span;
                    AddResultToBaseline(t, BaselineCommand.CodeLenses, GetBaselineForLocationsWithFileContents(locations, new BaselineFourslashLocationsOptions
                    {
                        Marker = new RangeMarker(openFile, codeLensRange, resolvedCodeLens.Range, null),
                        MarkerName = "/*CODELENS: " + resolvedCodeLens.Command.Title + "*/",
                    }));
                }
            }

            if (!foundAtLeastOneCodeLens)
            {
                t.Fatal("Expected at least one code lens in any open file, but got none.");
            }
        }
        finally
        {
            reset?.Invoke();
        }
    }

    /// <summary>Go: <c>func (f *FourslashTest) MarkTestAsStradaServer()</c>（fourslash.go:2687-2689）。</summary>
    public void MarkTestAsStradaServer()
    {
        _isStradaServer = true;
    }

    /// <summary>Go: <c>func (f *FourslashTest) VerifyBaselineGoToDefinition(t, includeOriginalSelectionRange bool, markers ...string)</c>（fourslash.go:2691-2713）。</summary>
    public void VerifyBaselineGoToDefinition(IHarnessTest t, bool includeOriginalSelectionRange, params string[] markers)
    {
        VerifyBaselineDefinitions(
            t,
            BaselineCommand.GoToDefinition,
            "/*GOTO DEF*/", // definitionMarker
            (lambdaT, lambdaF, fileName, position) =>
            {
                var @params = new DefinitionParams
                {
                    TextDocument = new TextDocumentIdentifier { Uri = FileNameToDocumentURI(_activeFilename) },
                    Position = _currentCaretPosition,
                };

                return SendRequest(lambdaT, TextDocumentDefinitionInfo, @params);
            },
            includeOriginalSelectionRange,
            markers);
    }

    /// <summary>Go: <c>func (f *FourslashTest) verifyBaselineDefinitions(t, definitionCommand baselineCommand, definitionMarker string, getDefinitions func(...) …, includeOriginalSelectionRange bool, markers ...string)</c>（fourslash.go:2715-2773）。</summary>
    public void VerifyBaselineDefinitions(
        IHarnessTest t,
        BaselineCommand definitionCommand,
        string definitionMarker,
        Func<IHarnessTest, FourslashTest, string, Position, LocationOrLocationsOrDefinitionLinksOrNull> getDefinitions,
        bool includeOriginalSelectionRange,
        params string[] markers)
    {
        var referenceLocations = LookupMarkersOrGetRanges(t, markers);

        foreach (var markerOrRange in referenceLocations)
        {
            // worker in `baselineEachMarkerOrRange`
            GoToMarkerOrRange(t, markerOrRange);

            var result = getDefinitions(t, this, _activeFilename, _currentCaretPosition);

            var resultAsSpans = Array.Empty<DocumentSpan>();
            DocumentSpan? additionalSpan = null;
            if (result.Locations is not null)
            {
                resultAsSpans = Core.Map(result.Locations, BaselineUtil.LocationToSpan);
            }
            else if (result.Location is not null)
            {
                resultAsSpans = [BaselineUtil.LocationToSpan(result.Location)];
            }
            else if (result.DefinitionLinks is not null)
            {
                Range? originRange = null;
                resultAsSpans = Core.Map(result.DefinitionLinks, link =>
                {
                    var definitionLink = link!;
                    if (originRange is not null && (definitionLink.OriginSelectionRange is null || originRange.Compare(definitionLink.OriginSelectionRange) != 0))
                    {
                        Debug.Fail("multiple different origin ranges in definition links");
                        throw new System.Diagnostics.UnreachableException();
                    }
                    originRange = definitionLink.OriginSelectionRange;
                    Range? contextSpan = null;
                    if (definitionLink.TargetRange.Compare(definitionLink.TargetSelectionRange) != 0 && !_isStradaServer)
                    {
                        contextSpan = definitionLink.TargetRange;
                    }
                    return new DocumentSpan
                    {
                        Uri = definitionLink.TargetUri,
                        TextSpan = definitionLink.TargetSelectionRange,
                        ContextSpan = contextSpan,
                    };
                });
                if (originRange is not null && includeOriginalSelectionRange)
                {
                    additionalSpan = new DocumentSpan
                    {
                        Uri = FileNameToDocumentURI(_activeFilename),
                        TextSpan = originRange,
                    };
                }
            }

            AddResultToBaseline(t, definitionCommand, GetBaselineForSpansWithFileContents(resultAsSpans, new BaselineFourslashLocationsOptions
            {
                Marker = markerOrRange,
                MarkerName = definitionMarker,
                AdditionalSpan = additionalSpan,
                PreserveResultOrder = (string)definitionCommand == (string)BaselineCommand.GoToSourceDefinition,
            }));
        }
    }

    /// <summary>Go: <c>func (f *FourslashTest) VerifyBaselineGoToTypeDefinition(t, markers ...string)</c>（fourslash.go:2775-2796）。</summary>
    public void VerifyBaselineGoToTypeDefinition(IHarnessTest t, params string[] markers)
    {
        VerifyBaselineDefinitions(
            t,
            BaselineCommand.GoToTypeDefinition,
            "/*GOTO TYPE*/", // definitionMarker
            (lambdaT, lambdaF, fileName, position) =>
            {
                var @params = new TypeDefinitionParams
                {
                    TextDocument = new TextDocumentIdentifier { Uri = FileNameToDocumentURI(_activeFilename) },
                    Position = _currentCaretPosition,
                };

                return SendRequest(lambdaT, TextDocumentTypeDefinitionInfo, @params);
            },
            false, // includeOriginalSelectionRange
            markers);
    }

    /// <summary>Go: <c>func (f *FourslashTest) VerifyBaselineGoToSourceDefinition(t, markers ...string)</c>（fourslash.go:2798-2823）。</summary>
    public void VerifyBaselineGoToSourceDefinition(IHarnessTest t, params string[] markers)
    {
        VerifyBaselineDefinitions(
            t,
            BaselineCommand.GoToSourceDefinition,
            "/*GOTO SOURCE DEF*/", // definitionMarker
            (lambdaT, lambdaF, fileName, position) =>
            {
                var @params = new TextDocumentPositionParams
                {
                    TextDocument = new TextDocumentIdentifier { Uri = FileNameToDocumentURI(_activeFilename) },
                    Position = _currentCaretPosition,
                };

                var result = SendRequest(lambdaT, CustomTextDocumentSourceDefinitionInfo, @params);
                if (result is null)
                {
                    return new LocationOrLocationsOrDefinitionLinksOrNull();
                }
                return result;
            },
            false, // includeOriginalSelectionRange
            markers);
    }

    /// <summary>Go: <c>func (f *FourslashTest) VerifyBaselineWorkspaceSymbol(t, query string)</c>（fourslash.go:2825-2849）。</summary>
    public void VerifyBaselineWorkspaceSymbol(IHarnessTest t, string query)
    {
        var result = SendRequest(t, WorkspaceSymbolInfo, new WorkspaceSymbolParams { Query = query });

        var locationToText = new Dictionary<DocumentSpan, SymbolInformation>();
        var groupedRanges = new MultiMap<DocumentUri, DocumentSpan>();
        foreach (var symbol in result.SymbolInformations ?? [])
        {
            var symbolInformation = symbol!;
            var uri = symbolInformation.Location.Uri;
            var span = BaselineUtil.LocationToSpan(symbolInformation.Location);
            groupedRanges.Add(uri, span);
            locationToText[span] = symbolInformation;
        }

        AddResultToBaseline(t, BaselineCommand.From("workspaceSymbol"), GetBaselineForGroupedSpansWithFileContents(
            groupedRanges,
            new BaselineFourslashLocationsOptions
            {
                GetLocationData = span => BaselineUtil.SymbolInformationToData(locationToText[span]),
            }));
    }

    /// <summary>Go: <c>func (f *FourslashTest) VerifyOutliningSpans(t, foldingRangeKind ...lsproto.FoldingRangeKind)</c>（fourslash.go:2851-2906）。</summary>
    public void VerifyOutliningSpans(IHarnessTest t, params FoldingRangeKind[] foldingRangeKind)
    {
        var @params = new FoldingRangeParams
        {
            TextDocument = new TextDocumentIdentifier { Uri = FileNameToDocumentURI(_activeFilename) },
        };
        var result = SendRequest(t, TextDocumentFoldingRangeInfo, @params);
        if (result.FoldingRanges is null)
        {
            t.Fatal("Nil response received for folding range request");
        }

        // Extract actual folding ranges from the result and filter by kind if specified
        FoldingRange?[] actualRanges = result.FoldingRanges;
        if (foldingRangeKind.Length > 0)
        {
            var targetKind = foldingRangeKind[0];
            var filtered = new List<FoldingRange?>();
            foreach (var r in actualRanges)
            {
                if (r is not null && r.Kind is not null && r.Kind == targetKind)
                {
                    filtered.Add(r);
                }
            }
            actualRanges = filtered.ToArray();
        }

        var expectedRanges = Ranges();
        if (actualRanges.Length != expectedRanges.Length)
        {
            t.Fatal($"verifyOutliningSpans failed - expected total spans to be {expectedRanges.Length}, but was {actualRanges.Length}");
        }

        Array.Sort(expectedRanges, (a, b) => ComparePositions(a.LSPos(), b.LSPos()));

        for (var i = 0; i < expectedRanges.Length; i++)
        {
            var actualRange = actualRanges[i]!;
            var expectedRange = expectedRanges[i];
            var startPos = new Position { Line = actualRange.StartLine, Character = actualRange.StartCharacter!.Value };
            var endPos = new Position { Line = actualRange.EndLine, Character = actualRange.EndCharacter!.Value };

            if (ComparePositions(startPos, expectedRange.LSRange.Start) != 0 ||
                ComparePositions(endPos, expectedRange.LSRange.End) != 0)
            {
                t.Fatal($"verifyOutliningSpans failed - span {i + 1} has invalid positions:\n"
                    + $"  actual: start ({actualRange.StartLine},{actualRange.StartCharacter!.Value}), end ({actualRange.EndLine},{actualRange.EndCharacter!.Value})\n"
                    + $"  expected: start ({expectedRange.LSRange.Start.Line},{expectedRange.LSRange.Start.Character}), end ({expectedRange.LSRange.End.Line},{expectedRange.LSRange.End.Character})");
            }
        }
    }

    /// <summary>Go: <c>func (f *FourslashTest) VerifyFoldingRangeLines(t, expected []FoldingRangeLineExpected)</c>（fourslash.go:2913-2930）。</summary>
    public void VerifyFoldingRangeLines(IHarnessTest t, FoldingRangeLineExpected[] expected)
    {
        var @params = new FoldingRangeParams
        {
            TextDocument = new TextDocumentIdentifier { Uri = FileNameToDocumentURI(_activeFilename) },
        };
        var result = SendRequest(t, TextDocumentFoldingRangeInfo, @params);
        if (result.FoldingRanges is null)
        {
            t.Fatal("Nil response received for folding range request");
        }

        var actualRanges = result.FoldingRanges;
        if (actualRanges.Length != expected.Length)
        {
            t.Fatal($"verifyFoldingRangeLines failed - expected {expected.Length} ranges, got {actualRanges.Length}");
        }

        for (var i = 0; i < expected.Length; i++)
        {
            var exp = expected[i];
            var got = actualRanges[i]!;
            if (got.StartLine != exp.StartLine || got.EndLine != exp.EndLine)
            {
                t.Error($"verifyFoldingRangeLines failed - range {i}: expected (startLine={exp.StartLine}, endLine={exp.EndLine}), got (startLine={got.StartLine}, endLine={got.EndLine})");
            }
        }
    }

    /// <summary>非 Go 源：Go <c>VerifyBaselineVSFindAllReferences</c> 内联的 `slices.SortStableFunc` 比较函数（逐行照抄）。</summary>
    private static int CompareVSReferenceItems(VSReferenceItem a, VSReferenceItem b)
    {
        var ap = a.VSProjectName ?? "";
        var bp = b.VSProjectName ?? "";
        if (ap != bp)
        {
            return string.CompareOrdinal(ap, bp) < 0 ? -1 : 1;
        }
        if (a.VSLocation.Uri != b.VSLocation.Uri)
        {
            return string.CompareOrdinal(a.VSLocation.Uri.Value, b.VSLocation.Uri.Value) < 0 ? -1 : 1;
        }
        if (a.VSLocation.Range.Start.Line != b.VSLocation.Range.Start.Line)
        {
            return (int)a.VSLocation.Range.Start.Line - (int)b.VSLocation.Range.Start.Line;
        }
        return (int)a.VSLocation.Range.Start.Character - (int)b.VSLocation.Range.Start.Character;
    }
}

/// <summary>Go: <c>type FoldingRangeLineExpected struct</c>（fourslash.go:2866-2869）：folding range 的期望起止行。</summary>
public sealed class FoldingRangeLineExpected
{
    public uint StartLine;

    public uint EndLine;
}
