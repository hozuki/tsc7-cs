// ported from: internal/fourslash/fourslash.go:4653-5234
//
// 偏离清单（相对 Go 原文逐条；行号均指 tsc/internal/fourslash/fourslash.go）：
//  1. `*testing.T` → `IHarnessTest`：`t.Helper()` 删除；`t.Fatalf(fmt, args...)` → `t.Fatal($"...")`。
//  2. 未导出方法 → PascalCase：`verifyBaselineRename` → `VerifyBaselineRename`（**重载**，与导出的
//     `VerifyBaselineRename(t, prefs, ...MarkerOrRangeOrName)` 形参类型不同，故不需要 `Core` 后缀）、
//     `willRenameFilesWorker` → `WillRenameFilesWorker`、`getPathUpdater` → `GetPathUpdater`、
//     `renameFileOrDirectory` → `RenameFileOrDirectory`。
//  3. `defer f.ConfigureWithReset(t, *preferences)()` → `var reset = ConfigureWithReset(...); try { } finally { reset(); }`
//     （`ConfigureWithReset` 返回 `Action`，见 local://fscore-freeze.md §3）。
//  4. `MarkerOrRangeOrName = any` → `object?`；`%T`（Go 类型名）→ `GetType().Name`（只出现在失败文本里）。
//  5. Go 的 `assert.DeepEqual(t, a, b)` → `FsCmp.AssertDeepEqual(t, a, b)`；`assert.Equal(t, a, b, msg)` →
//     显式比较 + `t.Fatal(msg + "\n" + FsCmp.Diff(a, b))`（失败文本，不进 baseline）。
//  6. `core.Map` → `Core.Core.Map`；`core.TextChange{TextRange: x, NewText: y}` → C# 主构造函数
//     `new TextChange(x, y)`（Readonly struct 的字段只能在构造函数里赋值）。
//  7. `f.vfs.ReadFile(p) (string, bool)` → `_vfs.TryReadFile(p, out var content)`；
//     `f.vfs.WriteFile/Remove` 的 `error` → 抛异常，这里 `catch (Exception e)` 后 `t.Fatal(… e.Message)`
//     （等价于 Go 的 `%v`；仓库其它 TestUtil 也这样吞 vfs 异常）。
//  8. `f.testData` 在 C# 是 `TestData?` → 访问点用 `_testData!`（Go 解引用 nil 指针会 panic）。
//  9. `scriptInfo.content` → `ScriptInfo._content`、`scriptInfos` → `_scriptInfos`（`internal` 字段，见冻结表 §1/§2）。
// 10. `getPathUpdater` 返回的 `func(path string) (string, bool)` → 本文件的 `RenamePathUpdater` 委托
//     （`bool RenamePathUpdater(string path, out string newPath)`；false 时 `newPath` 置 ""，对应 Go 的零值）。
// 11. 包级 `codeFence`/`getAccessibleFilePaths`/`textOfFile` → `BaselineUtil.CodeFence(...)` /
//     `BaselineUtil.GetAccessibleFilePaths(_vfs, path) ?? []`（`string[]?` ← Go 的 nil 切片不迭代）/
//     `TryTextOfFile(out ...)`（都来自 baselineutil.go，FsBaseline 切片，见 local://fscore-freeze.md §4）。
// 12. `slices.SortFunc(allChanges, func(a, b) int { return lsproto.ComparePositions(b.Range.Start, a.Range.Start) })` →
//     `Array.Sort(allChanges, (a, b) => ComparePositions(b.Range.Start, a.Range.Start))`（两者都比较符号）。
// 13. Go 的 `documentSpan{uri, textSpan}` → `DocumentSpan { Uri, TextSpan }`；`map[documentSpan]string` →
//     `Dictionary<DocumentSpan, string>`（C# 的 `DocumentSpan` 实现了 `IEquatable<DocumentSpan>`，按值相等查得到）。
// 14. `strings.Split(text, "?")` → `text.Split("?")`（Go 的 Split 与 .NET 的 string.Split(string) 同语义）。
// 15. `_capabilities.Workspace.FileOperations.WillRename`（`*bool`）→ `_capabilities?.Workspace?.FileOperations?.WillRename == true`。
// 16. `MultiMap[DocumentUri,documentSpan]` → `MultiMap<DocumentUri, DocumentSpan>`（TypeScript.Collections）。

using System.Text;
using TypeScript.Collections;
using TypeScript.Core;
using TypeScript.LsConv;
using TypeScript.LsProto;
using TypeScript.LsUtil;
using TypeScript.TestUtil;
using TypeScript.TSPath;
using static TypeScript.Core.Core;
using static TypeScript.LsProto.LsProto;
using static TypeScript.TSPath.TSPath;
using Range = TypeScript.LsProto.Range;

namespace TypeScript.Harness.Fourslash;

/// <summary>Go: <c>func(path string) (string, bool)</c>（fourslash.go:5084 的 <c>getPathUpdater</c> 返回值）。</summary>
internal delegate bool RenamePathUpdater(string path, out string newPath);

public sealed partial class FourslashTest
{
    /// <summary>Go: <c>func (f *FourslashTest) BaselineAutoImportsCompletions(t, markerNames []string)</c>（4653）。</summary>
    public void BaselineAutoImportsCompletions(IHarnessTest t, string[] markerNames)
    {
        var reset = ConfigureWithReset(t, new UserPreferences
        {
            IncludeCompletionsForModuleExports = Tristate.TSTrue,
            IncludeCompletionsForImportStatements = Tristate.TSTrue,
            ImportModuleSpecifierPreference = _userPreferences.ImportModuleSpecifierPreference,
            ImportModuleSpecifierEnding = _userPreferences.ImportModuleSpecifierEnding,
            AutoImportSpecifierExcludeRegexes = _userPreferences.AutoImportSpecifierExcludeRegexes,
            AutoImportFileExcludePatterns = _userPreferences.AutoImportFileExcludePatterns,
            PreferTypeOnlyAutoImports = _userPreferences.PreferTypeOnlyAutoImports,
            AutoImportEntrypointDirectorySearch = _userPreferences.AutoImportEntrypointDirectorySearch,
        });
        try
        {
            foreach (var markerName in markerNames)
            {
                GoToMarker(t, markerName);
                var parameters = new CompletionParams
                {
                    TextDocument = new TextDocumentIdentifier
                    {
                        Uri = LsConv.LsConv.FileNameToDocumentURI(_activeFilename),
                    },
                    Position = _currentCaretPosition,
                    Context = new CompletionContext(),
                };
                var result = SendRequest(t, TextDocumentCompletionInfo, parameters);

                var prefix = $"At marker '{markerName}': ";

                WriteToBaseline(BaselineCommand.AutoImports, "// === Auto Imports === \n");

                if (!TryTextOfFile(_activeFilename, out var fileContent))
                {
                    t.Fatal(prefix + $"Failed to read file {_activeFilename} for auto-import baseline");
                }

                // Go 读 map 不存在的键得到零值（nil 指针），后续解引用会 panic；C# 的 TryGetValue + `!` 等价。
                _testData!.MarkerPositions.TryGetValue(markerName, out var marker);
                var markerPosition = marker!.Position;
                var ext = GetAnyExtensionFromPath(_activeFilename, [], true);
                if (ext.StartsWith(".", StringComparison.Ordinal))
                {
                    ext = ext[1..];
                }

                var lang = ext is "mts" or "cts" ? "ts" : ext;
                WriteToBaseline(
                    BaselineCommand.AutoImports,
                    BaselineUtil.CodeFence(
                        lang,
                        "// @FileName: " + _activeFilename + "\n"
                        + fileContent[..markerPosition] + "/*" + markerName + "*/" + fileContent[markerPosition..]));

                var currentFile = NewScriptInfo(_activeFilename, fileContent);
                var converters = NewTestConverters(new Converters(
                    PositionEncodingKind.UTF8,
                    _ => currentFile._lineMap));
                CompletionItem?[]? list;
                if (result.Items is null || result.Items.Length == 0)
                {
                    if (result.List is null || result.List.Items is null || result.List.Items.Length == 0)
                    {
                        WriteToBaseline(BaselineCommand.AutoImports, "no autoimport completions found" + "\n\n");

                        continue;
                    }

                    list = result.List.Items;
                }
                else
                {
                    list = result.Items;
                }

                foreach (var itemOrNull in list)
                {
                    var item = itemOrNull!;
                    if (item.Data is null || item.SortText != TypeScript.Ls.Ls.SortTextAutoImportSuggestions)
                    {
                        continue;
                    }

                    var details = SendRequest(t, CompletionItemResolveInfo, item);
                    if (details is null || details.AdditionalTextEdits is null || details.AdditionalTextEdits.Length == 0)
                    {
                        t.Fatal(
                            prefix + $"Entry {item.Label} from {item.Detail} returned no code changes from completion details request");
                    }

                    var allChanges = details.AdditionalTextEdits;

                    // !!! calculate the change provided by the completiontext
                    // completionChange:= &lsproto.TextEdit{}
                    // if details.TextEdit != nil {
                    // 	completionChange = details.TextEdit.TextEdit
                    // } else if details.AdditionalTextEdits != nil && len(*details.AdditionalTextEdits) > 0 {
                    // 	completionChange = (*details.AdditionalTextEdits)[0]
                    // } else {
                    // 	completionChange.Range = lsproto.Range{ Start: marker.LSPosition, End: marker.LSPosition }
                    // 	if item.InsertText != nil {
                    // 		completionChange.NewText = *item.InsertText
                    // 	} else {
                    // 		completionChange.NewText = item.Label
                    // 	}
                    // }
                    // allChanges := append(allChanges, completionChange)
                    // sorted from back-of-file-most to front-of-file-most
                    Array.Sort(allChanges, (a, b) => ComparePositions(b!.Range.Start, a!.Range.Start));
                    var newFileContent = fileContent;
                    foreach (var change in allChanges)
                    {
                        var edit = change!;
                        newFileContent = newFileContent[..(int)converters.LineAndCharacterToPosition(currentFile, edit.Range.Start)]
                            + edit.NewText
                            + newFileContent[(int)converters.LineAndCharacterToPosition(currentFile, edit.Range.End)..];
                    }

                    WriteToBaseline(BaselineCommand.AutoImports, BaselineUtil.CodeFence(lang, newFileContent) + "\n\n");
                }
            }
        }
        finally
        {
            reset();
        }
    }

    /// <summary>Go: <c>func (f *FourslashTest) VerifyBaselineRename(t, preferences, markerOrNameOrRanges ...MarkerOrRangeOrName)</c>（4750）。</summary>
    public void VerifyBaselineRename(IHarnessTest t, UserPreferences? preferences, params object?[] markerOrNameOrRanges)
    {
        var markerOrRanges = new List<MarkerOrRange>();
        foreach (var markerOrNameOrRange in markerOrNameOrRanges)
        {
            switch (markerOrNameOrRange)
            {
                case string markerName:
                    if (!_testData!.MarkerPositions.TryGetValue(markerName, out var foundMarker))
                    {
                        t.Fatal($"Marker '{markerName}' not found");
                    }

                    markerOrRanges.Add(foundMarker);
                    break;
                case Marker marker:
                    markerOrRanges.Add(marker);
                    break;
                case RangeMarker rangeMarker:
                    markerOrRanges.Add(rangeMarker);
                    break;
                default:
                    t.Fatal(
                        $"Invalid marker or range type: {markerOrNameOrRange?.GetType().Name}. Expected string, *Marker, or *RangeMarker.");
                    break;
            }
        }

        VerifyBaselineRename(t, preferences, markerOrRanges);
    }

    /// <summary>Go: <c>func (f *FourslashTest) verifyBaselineRename(t, preferences, markerOrRanges []MarkerOrRange)</c>（4777）。</summary>
    internal void VerifyBaselineRename(IHarnessTest t, UserPreferences? preferences, List<MarkerOrRange> markerOrRanges)
    {
        Action? reset = null;
        if (preferences is not null)
        {
            reset = ConfigureWithReset(t, preferences.Value);
        }

        try
        {
            foreach (var markerOrRange in markerOrRanges)
            {
                GoToMarkerOrRange(t, markerOrRange);

                var parameters = new RenameParams
                {
                    TextDocument = new TextDocumentIdentifier
                    {
                        Uri = LsConv.LsConv.FileNameToDocumentURI(_activeFilename),
                    },
                    Position = _currentCaretPosition,
                    NewName = "?",
                };

                var result = SendRequest(t, TextDocumentRenameInfo, parameters);

                var changes = result.WorkspaceEdit?.Changes;

                var spanToText = new Dictionary<DocumentSpan, string>();
                var fileToSpan = new MultiMap<DocumentUri, DocumentSpan>();
                if (changes is not null)
                {
                    foreach (var (uri, edits) in changes)
                    {
                        if (edits is null)
                        {
                            continue;
                        }

                        foreach (var editOrNull in edits)
                        {
                            var edit = editOrNull!;
                            var span = new DocumentSpan
                            {
                                Uri = uri,
                                TextSpan = edit.Range,
                            };
                            fileToSpan.Add(uri, span);
                            spanToText[span] = edit.NewText;
                        }
                    }
                }

                var renameOptions = new StringBuilder();
                if (preferences is not null)
                {
                    if (preferences.Value.UseAliasesForRename != Tristate.TSUnknown)
                    {
                        renameOptions
                            .Append("// @useAliasesForRename: ")
                            .Append(preferences.Value.UseAliasesForRename.IsTrue() ? "true" : "false")
                            .Append('\n');
                    }

                    if (preferences.Value.QuotePreference != LsUtil.LsUtil.QuotePreferenceUnknown)
                    {
                        renameOptions
                            .Append("// @quotePreference: ")
                            .Append(preferences.Value.QuotePreference.Value)
                            .Append('\n');
                    }
                }

                var baselineFileContent = GetBaselineForGroupedSpansWithFileContents(
                    fileToSpan,
                    new BaselineFourslashLocationsOptions
                    {
                        Marker = markerOrRange,
                        MarkerName = "/*RENAME*/",
                        EndMarker = "RENAME|]",
                        StartMarkerPrefix = span =>
                        {
                            var text = spanToText[span];
                            var prefixAndSuffix = text.Split("?");
                            if (prefixAndSuffix[0] != "")
                            {
                                return "/*START PREFIX*/" + prefixAndSuffix[0];
                            }

                            return null;
                        },
                        EndMarkerSuffix = span =>
                        {
                            var text = spanToText[span];
                            var prefixAndSuffix = text.Split("?");
                            if (prefixAndSuffix[1] != "")
                            {
                                return prefixAndSuffix[1] + "/*END SUFFIX*/";
                            }

                            return null;
                        },
                    });

                var baselineResult = renameOptions.Length > 0
                    ? renameOptions.ToString() + "\n" + baselineFileContent
                    : baselineFileContent;

                AddResultToBaseline(t, BaselineCommand.Rename, baselineResult);
            }
        }
        finally
        {
            reset?.Invoke();
        }
    }

    /// <summary>Go: <c>func (f *FourslashTest) VerifyRenameSucceeded(t, preferences)</c>（4838）。</summary>
    public void VerifyRenameSucceeded(IHarnessTest t, UserPreferences? preferences)
    {
        Action? reset = null;
        if (preferences is not null)
        {
            reset = ConfigureWithReset(t, preferences.Value);
        }

        try
        {
            var parameters = new PrepareRenameParams
            {
                TextDocument = new TextDocumentIdentifier
                {
                    Uri = LsConv.LsConv.FileNameToDocumentURI(_activeFilename),
                },
                Position = _currentCaretPosition,
            };

            var prefix = GetCurrentPositionPrefix();
            var result = SendRequest(t, TextDocumentPrepareRenameInfo, parameters);
            if (result.Range is null && result.PrepareRenamePlaceholder is null && result.PrepareRenameDefaultBehavior is null)
            {
                t.Fatal(prefix + "Expected rename to succeed, but prepareRename returned null");
            }

            // Also verify that textDocument/rename produces edits, since prepareRename is optional.
            var renameResult = SendRequest(t, TextDocumentRenameInfo, new RenameParams
            {
                TextDocument = new TextDocumentIdentifier
                {
                    Uri = LsConv.LsConv.FileNameToDocumentURI(_activeFilename),
                },
                Position = _currentCaretPosition,
                NewName = "RENAME_SUCCEEDED_TEST",
            });
            var workspaceEdit = renameResult.WorkspaceEdit;
            if (workspaceEdit is null || workspaceEdit.Changes is null || workspaceEdit.Changes.Count == 0)
            {
                t.Fatal(prefix + "prepareRename succeeded but textDocument/rename returned no changes");
            }
        }
        finally
        {
            reset?.Invoke();
        }
    }

    /// <summary>Go: <c>func (f *FourslashTest) VerifyRenameRange(t, expectedRange, expectedPlaceholder, preferences)</c>（4865）。</summary>
    public void VerifyRenameRange(IHarnessTest t, Range expectedRange, string expectedPlaceholder, UserPreferences? preferences)
    {
        Action? reset = null;
        if (preferences is not null)
        {
            reset = ConfigureWithReset(t, preferences.Value);
        }

        try
        {
            var parameters = new PrepareRenameParams
            {
                TextDocument = new TextDocumentIdentifier
                {
                    Uri = LsConv.LsConv.FileNameToDocumentURI(_activeFilename),
                },
                Position = _currentCaretPosition,
            };

            var result = SendRequest(t, TextDocumentPrepareRenameInfo, parameters);
            var placeholder = result.PrepareRenamePlaceholder;
            if (placeholder is null)
            {
                t.Fatal(GetCurrentPositionPrefix() + "Expected prepareRename to return a range and placeholder");
            }

            FsCmp.AssertDeepEqual(t, placeholder.Range, expectedRange);
            FsCmp.AssertDeepEqual(t, placeholder.Placeholder, expectedPlaceholder);
        }
        finally
        {
            reset?.Invoke();
        }
    }

    /// <summary>Go: <c>func (f *FourslashTest) RenameAtCaret(t, newName) lsproto.RenameResponse</c>（4886）。</summary>
    public WorkspaceEditOrNull RenameAtCaret(IHarnessTest t, string newName)
    {
        var result = SendRequest(t, TextDocumentRenameInfo, new RenameParams
        {
            TextDocument = new TextDocumentIdentifier
            {
                Uri = LsConv.LsConv.FileNameToDocumentURI(_activeFilename),
            },
            Position = _currentCaretPosition,
            NewName = newName,
        });

        var workspaceEdit = result.WorkspaceEdit;
        if (workspaceEdit is null)
        {
            return result;
        }

        if (workspaceEdit.Changes is not null)
        {
            foreach (var (uri, edits) in workspaceEdit.Changes)
            {
                var fileName = uri.FileName();
                var script = GetOrLoadScriptInfo(fileName)!;
                var changes = Map<TextEdit?, TextChange>(edits, edit =>
                {
                    var e = edit!;
                    return new TextChange(FromLSPRange(script, e.Range), e.NewText);
                });
                EditScriptAndUpdateMarkersWorker(t, fileName, changes);
            }
        }

        var renameFiles = new List<RenameFile>();
        if (workspaceEdit.DocumentChanges is not null)
        {
            foreach (var docChange in workspaceEdit.DocumentChanges)
            {
                if (docChange.TextDocumentEdit is not null)
                {
                    var fileName = docChange.TextDocumentEdit.TextDocument.Uri.FileName();
                    var script = GetOrLoadScriptInfo(fileName)!;
                    var changes = Map<TextEditOrAnnotatedTextEditOrSnippetTextEdit?, TextChange>(docChange.TextDocumentEdit.Edits, edit =>
                    {
                        var textEdit = edit!.TextEdit!;
                        return new TextChange(FromLSPRange(script, textEdit.Range), textEdit.NewText);
                    });
                    EditScriptAndUpdateMarkersWorker(t, fileName, changes);
                }
                else if (docChange.RenameFile is not null)
                {
                    renameFiles.Add(docChange.RenameFile);
                }
            }
        }

        if (renameFiles.Count > 0)
        {
            var fileRenames = new List<FileRename>();
            foreach (var renameFile in renameFiles)
            {
                fileRenames.Add(new FileRename
                {
                    OldUri = renameFile.OldUri,
                    NewUri = renameFile.NewUri,
                });
            }

            if (_capabilities?.Workspace?.FileOperations?.WillRename == true)
            {
                WillRenameFilesWorker(t, fileRenames.ToArray());
            }
            else
            {
                foreach (var renameFile in renameFiles)
                {
                    RenameFileOrDirectory(t, renameFile.OldUri.FileName(), renameFile.NewUri.FileName());
                }
            }
        }

        return result;
    }

    /// <summary>Go: <c>func (f *FourslashTest) WillRenameFiles(t, files ...*lsproto.FileRename) lsproto.WillRenameFilesResponse</c>（5065）。</summary>
    public WorkspaceEditOrNull WillRenameFiles(IHarnessTest t, params FileRename[] files)
    {
        return SendRequest(t, WorkspaceWillRenameFilesInfo, new RenameFilesParams
        {
            Files = files,
        });
    }

    /// <summary>
    /// Go: <c>func (f *FourslashTest) willRenameFilesWorker(t, files ...*lsproto.FileRename)</c>（5073）：
    /// 通过 workspace/willRenameFiles 请求模拟文件重命名，并应用返回的编辑与文件重命名。
    /// </summary>
    internal void WillRenameFilesWorker(IHarnessTest t, params FileRename[] files)
    {
        var result = WillRenameFiles(t, files);

        var workspaceEdit = result.WorkspaceEdit;
        if (workspaceEdit is null)
        {
            foreach (var file in files)
            {
                var oldPath = file.OldUri.FileName();
                var newPath = file.NewUri.FileName();
                RenameFileOrDirectory(t, oldPath, newPath);
            }

            return;
        }

        if (workspaceEdit.Changes is not null)
        {
            foreach (var (uri, edits) in workspaceEdit.Changes)
            {
                var fileName = uri.FileName();
                var script = GetOrLoadScriptInfo(fileName)!;
                var changes = Map<TextEdit?, TextChange>(edits, edit =>
                {
                    var e = edit!;
                    return new TextChange(FromLSPRange(script, e.Range), e.NewText);
                });
                EditScriptAndUpdateMarkersWorker(t, fileName, changes);
            }
        }

        var renameFiles = new List<RenameFile>();
        if (workspaceEdit.DocumentChanges is not null)
        {
            foreach (var docChange in workspaceEdit.DocumentChanges)
            {
                if (docChange.TextDocumentEdit is not null)
                {
                    var fileName = docChange.TextDocumentEdit.TextDocument.Uri.FileName();
                    var script = GetOrLoadScriptInfo(fileName)!;
                    var changes = Map<TextEditOrAnnotatedTextEditOrSnippetTextEdit?, TextChange>(docChange.TextDocumentEdit.Edits, edit =>
                    {
                        var textEdit = edit!.TextEdit!;
                        return new TextChange(FromLSPRange(script, textEdit.Range), textEdit.NewText);
                    });
                    EditScriptAndUpdateMarkersWorker(t, fileName, changes);
                }
                else if (docChange.RenameFile is not null)
                {
                    renameFiles.Add(docChange.RenameFile);
                }
            }
        }

        var fileRenames = new List<FileRename>();
        foreach (var renameFile in renameFiles)
        {
            fileRenames.Add(new FileRename
            {
                OldUri = renameFile.OldUri,
                NewUri = renameFile.NewUri,
            });
        }

        WillRenameFilesWorker(t, fileRenames.ToArray());

        foreach (var file in files)
        {
            var oldPath = file.OldUri.FileName();
            var newPath = file.NewUri.FileName();
            RenameFileOrDirectory(t, oldPath, newPath);
        }
    }

    /// <summary>Go: <c>func (f *FourslashTest) VerifyRename(t, markerName, newName, expectedFileContents map[string]string)</c>（5141）。</summary>
    public void VerifyRename(IHarnessTest t, string markerName, string newName, Dictionary<string, string> expectedFileContents)
    {
        GoToMarker(t, markerName);
        RenameAtCaret(t, newName);
        foreach (var (fileName, expectedContent) in expectedFileContents)
        {
            var script = GetScriptInfo(fileName);
            if (script is null)
            {
                t.Fatal($"Expected script info for {fileName}, but got nil");
            }

            if (script._content != expectedContent)
            {
                t.Fatal(
                    $"File content after rename did not match expected content for {fileName}.\n"
                    + FsCmp.Diff(script._content, expectedContent));
            }
        }
    }

    /// <summary>Go: <c>func (f *FourslashTest) VerifyWillRenameFilesEdits(t, oldPath, newPath, expectedFileContents, preferences)</c>（5154）。</summary>
    public void VerifyWillRenameFilesEdits(
        IHarnessTest t,
        string oldPath,
        string newPath,
        Dictionary<string, string> expectedFileContents,
        UserPreferences? preferences)
    {
        Action? reset = null;
        if (preferences is not null)
        {
            reset = ConfigureWithReset(t, preferences.Value);
        }

        try
        {
            WillRenameFilesWorker(t, new FileRename
            {
                OldUri = LsConv.LsConv.FileNameToDocumentURI(oldPath),
                NewUri = LsConv.LsConv.FileNameToDocumentURI(newPath),
            });

            foreach (var (fileName, expectedContent) in expectedFileContents)
            {
                var script = GetOrLoadScriptInfo(fileName);
                if (script is null)
                {
                    t.Fatal($"Expected script info for {fileName}, but got nil");
                }

                if (script._content != expectedContent)
                {
                    t.Fatal(
                        $"File content after workspace/willRenameFiles edits did not match expected content for {fileName}.\n"
                        + FsCmp.Diff(script._content, expectedContent));
                }
            }
        }
        finally
        {
            reset?.Invoke();
        }
    }

    /// <summary>Go: <c>func (f *FourslashTest) getPathUpdater(oldPath, newPath string) func(path string) (string, bool)</c>（5174）。</summary>
    internal RenamePathUpdater GetPathUpdater(string oldPath, string newPath)
    {
        return (string path, out string updatedPath) =>
        {
            var compareOptions = new ComparePathsOptions(_vfs.UseCaseSensitiveFileNames());
            if (ComparePaths(path, oldPath, compareOptions) == 0)
            {
                updatedPath = newPath;
                return true;
            }

            if (StartsWithDirectory(path, oldPath, _vfs.UseCaseSensitiveFileNames()))
            {
                updatedPath = newPath + path[oldPath.Length..];
                return true;
            }

            updatedPath = "";
            return false;
        };
    }

    /// <summary>Go: <c>func (f *FourslashTest) renameFileOrDirectory(t, oldPath, newPath)</c>（5184）。</summary>
    internal void RenameFileOrDirectory(IHarnessTest t, string oldPath, string newPath)
    {
        var pathUpdater = GetPathUpdater(oldPath, newPath);

        // Collect all file paths that need to be renamed.
        var oldFileNames = new HashSet<string>();
        if (_vfs.TryReadFile(oldPath, out _))
        {
            oldFileNames.Add(oldPath);
        }
        else
        {
            foreach (var path in BaselineUtil.GetAccessibleFilePaths(_vfs, oldPath) ?? [])
            {
                oldFileNames.Add(path);
            }
        }

        if (oldFileNames.Count == 0)
        {
            t.Fatal($"rename source {oldPath} did not exist in test environment");
        }

        // !!! TODO: handle overwrites if we need to.
        // For each file: close if open, update script infos, write to VFS at new path, and collect file-watch events.
        var fileEvents = new List<FileEvent>(oldFileNames.Count * 2);
        var reopenAtNewPath = new Dictionary<string, string>(); // newFileName -> content, for files that were open
        foreach (var oldFileName in oldFileNames)
        {
            if (!pathUpdater(oldFileName, out var newFileName))
            {
                t.Fatal($"failed to compute renamed path for {oldFileName}");
            }

            // Send didClose for open files; get content from the old script info.
            if (_openFiles.Contains(oldFileName))
            {
                var oldScript = _scriptInfos[oldFileName];
                reopenAtNewPath[newFileName] = oldScript._content;
                SendNotification(t, TextDocumentDidCloseInfo, new DidCloseTextDocumentParams
                {
                    TextDocument = new TextDocumentIdentifier
                    {
                        Uri = LsConv.LsConv.FileNameToDocumentURI(oldFileName),
                    },
                });
                _openFiles.Remove(oldFileName);
            }

            _scriptInfos[newFileName] = NewScriptInfo(newFileName, _scriptInfos[oldFileName]._content);
            _scriptInfos.Remove(oldFileName);

            // Write renamed file to VFS.
            if (!_vfs.TryReadFile(oldFileName, out var content))
            {
                t.Fatal($"failed to read content for {oldFileName} during rename to {newFileName}");
            }

            try
            {
                _vfs.WriteFile(newFileName, content);
            }
            catch (Exception e)
            {
                t.Fatal($"failed to write renamed file {newFileName}: {e.Message}");
            }

            fileEvents.Add(new FileEvent
            {
                Uri = LsConv.LsConv.FileNameToDocumentURI(oldFileName),
                Type = FileChangeType.Deleted,
            });
            fileEvents.Add(new FileEvent
            {
                Uri = LsConv.LsConv.FileNameToDocumentURI(newFileName),
                Type = FileChangeType.Created,
            });
        }

        // Remove the old path from VFS and notify the server of all file-system changes.
        try
        {
            _vfs.Remove(oldPath);
        }
        catch (Exception e)
        {
            t.Fatal($"failed to remove old path {oldPath}: {e.Message}");
        }

        SendNotification(t, WorkspaceDidChangeWatchedFilesInfo, new DidChangeWatchedFilesParams
        {
            Changes = fileEvents.ToArray(),
        });

        // Reopen files that were previously open at their new paths.
        foreach (var (newFileName, content) in reopenAtNewPath)
        {
            SendNotification(t, TextDocumentDidOpenInfo, new DidOpenTextDocumentParams
            {
                TextDocument = new TextDocumentItem
                {
                    Uri = LsConv.LsConv.FileNameToDocumentURI(newFileName),
                    LanguageId = GetLanguageKind(newFileName),
                    Text = content,
                },
            });
            _openFiles.Add(newFileName);
        }

        // Update active filename if it was under the renamed path.
        if (pathUpdater(_activeFilename, out var updatedActive))
        {
            _activeFilename = updatedActive;
        }
    }

    /// <summary>Go: <c>func (f *FourslashTest) VerifyRenameFailed(t, preferences)</c>（5240）。</summary>
    public void VerifyRenameFailed(IHarnessTest t, UserPreferences? preferences)
    {
        Action? reset = null;
        if (preferences is not null)
        {
            reset = ConfigureWithReset(t, preferences.Value);
        }

        try
        {
            var parameters = new PrepareRenameParams
            {
                TextDocument = new TextDocumentIdentifier
                {
                    Uri = LsConv.LsConv.FileNameToDocumentURI(_activeFilename),
                },
                Position = _currentCaretPosition,
            };

            var prefix = GetCurrentPositionPrefix();
            BaselineState(t);
            BaselineRequestOrNotification(t, TextDocumentPrepareRenameInfo.Method, parameters);
            var (resMsg, result, _) = _client.SendRequest(t, TextDocumentPrepareRenameInfo, parameters);
            BaselineState(t);

            // prepareRename can reject via an error response (with a localized message) or a null result.
            if (resMsg is not null && resMsg.AsResponse().Error is not null)
            {
                // Error response — rename was rejected with a message. This is expected.
            }
            else if (result.Range is not null || result.PrepareRenamePlaceholder is not null || result.PrepareRenameDefaultBehavior is not null)
            {
                t.Fatal(prefix + "Expected rename to fail, but prepareRename returned a result");
            }

            // Also verify that textDocument/rename does not produce usable edits, since prepareRename is optional.
            var (renameMsg, renameResult, _) = _client.SendRequest(t, TextDocumentRenameInfo, new RenameParams
            {
                TextDocument = new TextDocumentIdentifier
                {
                    Uri = LsConv.LsConv.FileNameToDocumentURI(_activeFilename),
                },
                Position = _currentCaretPosition,
                NewName = "RENAME_FAILED_TEST",
            });
            if (renameMsg is not null && renameMsg.AsResponse().Error is not null)
            {
                return;
            }

            var workspaceEdit = renameResult.WorkspaceEdit;
            if (workspaceEdit is not null && workspaceEdit.Changes is not null && workspaceEdit.Changes.Count > 0)
            {
                t.Fatal(prefix + "prepareRename returned null but textDocument/rename returned changes");
            }
        }
        finally
        {
            reset?.Invoke();
        }
    }

    /// <summary>Go: <c>func (f *FourslashTest) VerifyBaselineRenameAtRangesWithText(t, preferences, texts ...string)</c>（5225）。</summary>
    public void VerifyBaselineRenameAtRangesWithText(IHarnessTest t, UserPreferences? preferences, params string[] texts)
    {
        var markerOrRanges = new List<MarkerOrRange>();
        foreach (var text in texts)
        {
            var ranges = Map(GetRangesByText().Get(text)?.ToArray(), r => (MarkerOrRange)r);
            markerOrRanges.AddRange(ranges);
        }

        VerifyBaselineRename(t, preferences, markerOrRanges);
    }
}
