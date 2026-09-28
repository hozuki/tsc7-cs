// ported from: internal/fourslash/fourslash.go:2131-2518（VerifyOrganizeImports … extractModuleSpecifier）
//
// 偏离清单（Go → C#）：
//  1) `t.Helper()` 删除（C# 无对应）。
//  2) Go 的 `defer reset()` → `Action? reset = …; try { … } finally { reset?.Invoke(); }`（C# 的 t.Fatal 抛异常，
//     等价于 Go 里 FailNow 仍会执行 defer）。
//  3) 未导出的 `verifyOrganizeImports` 与导出的 `VerifyOrganizeImports` 参数个数不同（5 vs 4），在 C# 里作为同名
//     重载（不属于"同名同签名"冲突，故不加 Core 后缀）。
//  4) `assert.Equal(t, x, y[, msg])` → `if (x != y) t.Fatal(msg + 两个值)`：gotest.tools 的 diff 文本不复刻
//     （失败信息不进 baseline）；有 msg 时保留 Go 的 msg 文本。
//  5) `markerName *string` / `options *ApplyCodeActionFromCompletionOptions`：Go 函数体立即解引用，C# 侧改用非空
//     参数（传 nil 的调用点在 Go 里必然 panic）。
//  6) FourslashTest 的未导出字段按共享成员契约直取：`_activeFilename` / `_currentCaretPosition` / `_converters`。
//  7) Go 的 `lsproto.Position` 是值类型，`currentCaretPosition := f.currentCaretPosition` 是值拷贝；C# 的
//     `Position` 是 class，这里 `var currentCaretPosition = _currentCaretPosition;` 是引用别名。等价性依赖
//     "Position 从不被就地修改"（四slash 目录里只有 baselineutil.go 对局部 Range 的 Character 自增）；
//     `_currentCaretPosition = currentCaretPosition;` 的恢复语义因此仍然成立。
//  8) Go 的 `var diagnostics []*lsproto.Diagnostic`（nil 切片）→ C# 保留 `null`（与 Go 发出的 JSON 一致，不发 `[]`）。
//  9) `strings.Repeat("-", 20)` → `new string('-', 20)`；`strings.Builder` → `StringBuilder`。
// 10) `slices.Contains(xs, v)` + append → `List<string>.Contains` / `Add`（EqualityComparer<string>.Default 为序数相等）。
// 11) `%v`（[]string）在失败信息里用 FourslashTest.Completions.cs（FsCompletions）的私有静态
//     `FormatStringSlice(IEnumerable<string>)`（`[a b c]`），不另起一份实现。
// 12) `*item.AdditionalTextEdits` / `changeEdits` 等 nil 切片 → `?? []`（Go 遍历 nil 切片不迭代）。
// 13) `core.Filter(completionsList.Items, …)` 的元素在 C# 是 `CompletionItem?`（Go 是 `*CompletionItem`），谓词里
//     多一条 `item is null` 短路（Go 会 panic，实际不可能出现）。
// 14) `Description` 为空串时不做 detail 过滤（照抄 Go 的 `description != ""`）。
// 15) Go 未导出的 `getCompletions` / `resolveCompletionItem` 由 FourslashTest.Completions.cs（FsCompletions）提供，
//     C# 名 `GetCompletionsCore(IHarnessTest, UserPreferences?) -> CompletionList?` /
//     `ResolveCompletionItemCore(IHarnessTest, CompletionItem) -> CompletionItem?`（本文件只调用，不定义）。
namespace TypeScript.Harness.Fourslash;

using System.Text;
using TypeScript.Core;
using TypeScript.LsProto;
using TypeScript.LsUtil;
using TypeScript.TestUtil;
using static TypeScript.LsConv.LsConv;
using static TypeScript.LsProto.LsProto;
using static TypeScript.LsUtil.LsUtil;

public sealed partial class FourslashTest
{
    /// <summary>Go: <c>func (f *FourslashTest) VerifyOrganizeImports(t, expectedContent string, codeActionKind lsproto.CodeActionKind, preferences *lsutil.UserPreferences)</c>（fourslash.go:2131-2134）。</summary>
    public void VerifyOrganizeImports(IHarnessTest t, string expectedContent, CodeActionKind codeActionKind, UserPreferences? preferences)
    {
        VerifyOrganizeImports(t, expectedContent, codeActionKind, codeActionKind, preferences);
    }

    /// <summary>Go: <c>func (f *FourslashTest) VerifyOrganizeImportsWithRequestKind(t, expectedContent string, requestedKind, expectedKind lsproto.CodeActionKind, preferences *lsutil.UserPreferences)</c>（fourslash.go:2136-2149）。</summary>
    public void VerifyOrganizeImportsWithRequestKind(
        IHarnessTest t,
        string expectedContent,
        CodeActionKind requestedKind,
        CodeActionKind expectedKind,
        UserPreferences? preferences)
    {
        VerifyOrganizeImports(t, expectedContent, requestedKind, expectedKind, preferences);
    }

    /// <summary>Go: <c>func (f *FourslashTest) verifyOrganizeImports(t, expectedContent string, requestedKind, expectedKind lsproto.CodeActionKind, preferences *lsutil.UserPreferences)</c>（fourslash.go:2151-2207）。</summary>
    private void VerifyOrganizeImports(
        IHarnessTest t,
        string expectedContent,
        CodeActionKind requestedKind,
        CodeActionKind expectedKind,
        UserPreferences? preferences)
    {
        var reset = preferences is null ? null : ConfigureWithReset(t, preferences.Value);
        try
        {
            var @params = new CodeActionParams
            {
                TextDocument = new TextDocumentIdentifier { Uri = FileNameToDocumentURI(_activeFilename) },
                Range = new Range
                {
                    Start = new Position { Line = 0, Character = 0 },
                    End = _converters.PositionToLineAndCharacter(
                        GetScriptInfo(_activeFilename)!,
                        new TextPos(GetScriptInfo(_activeFilename)!._content.Length)),
                },
                Context = new CodeActionContext { Only = [requestedKind] },
            };

            var result = SendRequest(t, TextDocumentCodeActionInfo, @params);

            if (result.CommandOrCodeActionArray is null || result.CommandOrCodeActionArray.Length == 0)
            {
                t.Fatal("No organize imports code action found");
            }

            CodeAction? organizeAction = null;
            foreach (var item in result.CommandOrCodeActionArray)
            {
                if (item.CodeAction is not null && item.CodeAction.Kind is not null && item.CodeAction.Kind == expectedKind)
                {
                    organizeAction = item.CodeAction;
                    break;
                }
            }

            if (organizeAction is null)
            {
                t.Fatal("No organize imports code action found");
            }

            var expectedURI = FileNameToDocumentURI(_activeFilename);
            if (organizeAction.Edit is not null && organizeAction.Edit.Changes is not null)
            {
                foreach (var (uri, edits) in organizeAction.Edit.Changes)
                {
                    if (uri != expectedURI)
                    {
                        t.Fatal($"Organize imports changed unexpected file: {uri} (expected {expectedURI})");
                    }
                    ApplyTextEdits(t, edits ?? []);
                }
            }

            var actualContent = GetScriptInfo(_activeFilename)!._content;
            if (actualContent != expectedContent)
            {
                t.Fatal($"Organize imports result doesn't match.\nExpected:\n{expectedContent}\n\nActual:\n{actualContent}");
            }
        }
        finally
        {
            reset?.Invoke();
        }
    }

    /// <summary>Go: <c>func (f *FourslashTest) findCompletionForCodeAction(t, items []*lsproto.CompletionItem, description string) *lsproto.CompletionItem</c>（fourslash.go:2209-2230）。</summary>
    private CompletionItem? FindCompletionForCodeAction(IHarnessTest t, CompletionItem?[] items, string description)
    {
        var direct = Core.Find(items, item => item is not null && item.AdditionalTextEdits is not null && item.AdditionalTextEdits.Length != 0);
        if (direct is not null)
        {
            return direct;
        }

        return Core.FirstNonNil<CompletionItem?, CompletionItem?>(items, item =>
        {
            if (item is null)
            {
                return null;
            }
            var resolvedItem = ResolveCompletionItemCore(t, item);
            if (resolvedItem is null || resolvedItem.AdditionalTextEdits is null || resolvedItem.AdditionalTextEdits.Length == 0)
            {
                return null;
            }
            if (description != "" && (resolvedItem.Detail is null || !resolvedItem.Detail.Contains(description, StringComparison.Ordinal)))
            {
                return null;
            }
            return resolvedItem;
        });
    }

    /// <summary>Go: <c>func (f *FourslashTest) VerifyApplyCodeActionFromCompletion(t, markerName *string, options *ApplyCodeActionFromCompletionOptions)</c>（fourslash.go:2242-2295）。</summary>
    public void VerifyApplyCodeActionFromCompletion(IHarnessTest t, string markerName, ApplyCodeActionFromCompletionOptions options)
    {
        GoToMarker(t, markerName);

        // Default preferences: enables auto-imports
        var userPreferences = options.UserPreferences ?? NewDefaultUserPreferences();

        var reset = ConfigureWithReset(t, userPreferences);
        try
        {
            var completionsList = GetCompletionsCore(t, null); // Already configured, so we do not need to pass it in again
            var items = Core.Filter(completionsList!.Items, item =>
            {
                if (item is null || item.Label != options.Name || item.Data is null)
                {
                    return false;
                }

                var data = item.Data;
                if (options.AutoImportFix is not null)
                {
                    var moduleSpecifier = options.AutoImportFix.ModuleSpecifier;
                    if (moduleSpecifier == "")
                    {
                        moduleSpecifier = options.Source;
                    }
                    return data.AutoImport is not null && data.AutoImport.ModuleSpecifier == moduleSpecifier;
                }
                if (data.AutoImport is null && data.Source != "" && data.Source == options.Source)
                {
                    return true;
                }
                if (data.AutoImport is not null && data.AutoImport.ModuleSpecifier == options.Source)
                {
                    return true;
                }
                return false;
            });

            var item = FindCompletionForCodeAction(t, items, options.Description);
            if (item is null)
            {
                t.Fatal($"Code action '{options.Name}' from source '{options.Source}' not found.");
            }

            // apply the item to the test files
            ApplyTextEdits(t, item.AdditionalTextEdits ?? []);
            if (options.NewFileContent is not null)
            {
                var actual = GetScriptInfo(_activeFilename)!._content;
                if (actual != options.NewFileContent)
                {
                    t.Fatal("File content after applying code action did not match expected content.");
                }
            }
            else if (options.NewRangeContent is not null)
            {
                t.Fatal("!!! TODO");
            }
        }
        finally
        {
            reset?.Invoke();
        }
    }

    /// <summary>Go: <c>func (f *FourslashTest) VerifyImportFixAtPosition(t, expectedTexts []string, preferences *lsutil.UserPreferences)</c>（fourslash.go:2297-2400）。</summary>
    public void VerifyImportFixAtPosition(IHarnessTest t, string[] expectedTexts, UserPreferences? preferences)
    {
        var fileName = _activeFilename;
        var ranges = Ranges();
        var filteredRanges = new List<RangeMarker>();
        foreach (var r in ranges)
        {
            if (r.FileName() == fileName)
            {
                filteredRanges.Add(r);
            }
        }
        if (filteredRanges.Count > 1)
        {
            t.Fatal("Exactly one range should be specified in the testfile.");
        }
        RangeMarker? rangeMarker = null;
        if (filteredRanges.Count == 1)
        {
            rangeMarker = filteredRanges[0];
        }

        var reset = preferences is null ? null : ConfigureWithReset(t, preferences.Value);
        try
        {
            // Get diagnostics at the current position to find errors that need import fixes
            var diagParams = new DocumentDiagnosticParams
            {
                TextDocument = new TextDocumentIdentifier { Uri = FileNameToDocumentURI(_activeFilename) },
            };
            var diagResult = SendRequest(t, TextDocumentDiagnosticInfo, diagParams);

            Diagnostic?[]? diagnostics = null;
            if (diagResult.FullDocumentDiagnosticReport is not null && diagResult.FullDocumentDiagnosticReport.Items is not null)
            {
                diagnostics = diagResult.FullDocumentDiagnosticReport.Items;
            }

            var currentCaretPosition = _currentCaretPosition;
            var @params = new CodeActionParams
            {
                TextDocument = new TextDocumentIdentifier { Uri = FileNameToDocumentURI(_activeFilename) },
                Range = new Range
                {
                    End = currentCaretPosition,
                    Start = currentCaretPosition,
                },
                Context = new CodeActionContext { Diagnostics = diagnostics },
            };
            var result = SendRequest(t, TextDocumentCodeActionInfo, @params);

            // Find all auto-import code actions (fixes with fixId/fixName related to imports)
            // Skip fix-all entries (those without diagnostics attached)
            var importActions = new List<CodeAction>();
            if (result.CommandOrCodeActionArray is not null)
            {
                foreach (var item in result.CommandOrCodeActionArray)
                {
                    if (item.CodeAction is not null && item.CodeAction.Kind is not null && item.CodeAction.Kind == CodeActionKind.QuickFix)
                    {
                        if (item.CodeAction.Diagnostics is not null && item.CodeAction.Diagnostics.Length > 0)
                        {
                            importActions.Add(item.CodeAction);
                        }
                    }
                }
            }

            if (importActions.Count == 0)
            {
                if (expectedTexts.Length != 0)
                {
                    t.Fatal("No codefixes returned.");
                }
                return;
            }

            // Save the original content before any edits
            var script = GetScriptInfo(_activeFilename)!;
            var originalContent = script._content;
            // For each import action, apply it and check the result
            var actualTextArray = new List<string>(importActions.Count);
            foreach (var action in importActions)
            {
                // Apply the code action
                if (action.Edit is not null && action.Edit.Changes is not null)
                {
                    if (action.Edit.Changes.Count != 1)
                    {
                        t.Fatal($"Expected exactly 1 change, got {action.Edit.Changes.Count}");
                    }
                    foreach (var (uri, changeEdits) in action.Edit.Changes)
                    {
                        if (uri != FileNameToDocumentURI(_activeFilename))
                        {
                            t.Fatal($"Expected change to file {_activeFilename}, got {uri}");
                        }
                        ApplyTextEdits(t, changeEdits ?? []);
                    }
                }

                // Get the result text
                string text;
                if (rangeMarker is not null)
                {
                    text = GetRangeText(rangeMarker);
                }
                else
                {
                    text = GetScriptInfo(_activeFilename)!._content;
                }
                actualTextArray.Add(text);

                // Restore original content for next fix
                EditScriptAndUpdateMarkers(t, _activeFilename, 0, script._content.Length, originalContent);
                _currentCaretPosition = currentCaretPosition;
            }

            // Compare results
            if (expectedTexts.Length != actualTextArray.Count)
            {
                var actualJoined = new StringBuilder();
                for (var i = 0; i < actualTextArray.Count; i++)
                {
                    if (i > 0)
                    {
                        actualJoined.Append("\n\n");
                        actualJoined.Append(new string('-', 20));
                        actualJoined.Append("\n\n");
                    }
                    actualJoined.Append(actualTextArray[i]);
                }
                t.Fatal($"Expected {expectedTexts.Length} import fixes, got {actualTextArray.Count}:\n\n{actualJoined}");
            }
            for (var i = 0; i < expectedTexts.Length; i++)
            {
                var actual = actualTextArray[i];
                if (expectedTexts[i] != actual)
                {
                    t.Fatal($"Import fix at index {i} doesn't match.\nExpected: {expectedTexts[i]}\nActual: {actual}");
                }
            }
        }
        finally
        {
            reset?.Invoke();
        }
    }

    /// <summary>Go: <c>func (f *FourslashTest) VerifyImportFixModuleSpecifiers(t, markerName string, expectedModuleSpecifiers []string, preferences *lsutil.UserPreferences)</c>（fourslash.go:2402-2485）。</summary>
    public void VerifyImportFixModuleSpecifiers(
        IHarnessTest t,
        string markerName,
        string[] expectedModuleSpecifiers,
        UserPreferences? preferences)
    {
        GoToMarker(t, markerName);

        var reset = preferences is null ? null : ConfigureWithReset(t, preferences.Value);
        try
        {
            // Get diagnostics at the current position to find errors that need import fixes
            var diagParams = new DocumentDiagnosticParams
            {
                TextDocument = new TextDocumentIdentifier { Uri = FileNameToDocumentURI(_activeFilename) },
            };
            var diagResult = SendRequest(t, TextDocumentDiagnosticInfo, diagParams);

            Diagnostic?[]? diagnostics = null;
            if (diagResult.FullDocumentDiagnosticReport is not null && diagResult.FullDocumentDiagnosticReport.Items is not null)
            {
                diagnostics = diagResult.FullDocumentDiagnosticReport.Items;
            }

            var @params = new CodeActionParams
            {
                TextDocument = new TextDocumentIdentifier { Uri = FileNameToDocumentURI(_activeFilename) },
                Range = new Range
                {
                    Start = _currentCaretPosition,
                    End = _currentCaretPosition,
                },
                Context = new CodeActionContext { Diagnostics = diagnostics },
            };
            var result = SendRequest(t, TextDocumentCodeActionInfo, @params);

            // Extract module specifiers from import fix code actions
            var actualModuleSpecifiers = new List<string>();
            if (result.CommandOrCodeActionArray is not null)
            {
                foreach (var item in result.CommandOrCodeActionArray)
                {
                    if (item.CodeAction is not null && item.CodeAction.Kind is not null && item.CodeAction.Kind == CodeActionKind.QuickFix)
                    {
                        if (item.CodeAction.Edit is not null && item.CodeAction.Edit.Changes is not null)
                        {
                            foreach (var (_, changeEdits) in item.CodeAction.Edit.Changes)
                            {
                                foreach (var edit in changeEdits ?? [])
                                {
                                    if (edit is null)
                                    {
                                        continue;
                                    }
                                    var moduleSpec = ExtractModuleSpecifier(edit.NewText);
                                    if (moduleSpec != "")
                                    {
                                        if (!actualModuleSpecifiers.Contains(moduleSpec))
                                        {
                                            actualModuleSpecifiers.Add(moduleSpec);
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }

            // Compare results
            if (actualModuleSpecifiers.Count != expectedModuleSpecifiers.Length)
            {
                t.Fatal($"Expected {expectedModuleSpecifiers.Length} module specifiers, got {actualModuleSpecifiers.Count}.\n"
                    + $"Expected: {FormatStringSlice(expectedModuleSpecifiers)}\nActual: {FormatStringSlice(actualModuleSpecifiers)}");
            }

            for (var i = 0; i < expectedModuleSpecifiers.Length; i++)
            {
                if (i >= actualModuleSpecifiers.Count || actualModuleSpecifiers[i] != expectedModuleSpecifiers[i])
                {
                    t.Fatal($"Module specifier mismatch at index {i}.\n"
                        + $"Expected: {FormatStringSlice(expectedModuleSpecifiers)}\nActual: {FormatStringSlice(actualModuleSpecifiers)}");
                }
            }
        }
        finally
        {
            reset?.Invoke();
        }
    }

    /// <summary>Go: <c>func extractModuleSpecifier(text string) string</c>（fourslash.go:2487-2517）。</summary>
    private static string ExtractModuleSpecifier(string text)
    {
        // Try to match: from "..." or from '...'
        var idx = text.IndexOf("from \"", StringComparison.Ordinal);
        if (idx != -1)
        {
            var start = idx + 6; // len("from \"")
            var end = text.IndexOf('"', start);
            if (end != -1)
            {
                return text[start..end];
            }
        }
        idx = text.IndexOf("from '", StringComparison.Ordinal);
        if (idx != -1)
        {
            var start = idx + 6; // len("from '")
            var end = text.IndexOf('\'', start);
            if (end != -1)
            {
                return text[start..end];
            }
        }

        // Try to match: require("...") or require('...')
        idx = text.IndexOf("require(\"", StringComparison.Ordinal);
        if (idx != -1)
        {
            var start = idx + 9; // len("require(\"")
            var end = text.IndexOf('"', start);
            if (end != -1)
            {
                return text[start..end];
            }
        }
        idx = text.IndexOf("require('", StringComparison.Ordinal);
        if (idx != -1)
        {
            var start = idx + 9; // len("require('")
            var end = text.IndexOf('\'', start);
            if (end != -1)
            {
                return text[start..end];
            }
        }

        return "";
    }
}

/// <summary>Go: <c>type ApplyCodeActionFromCompletionOptions struct</c>（fourslash.go:2232-2240）。</summary>
public sealed class ApplyCodeActionFromCompletionOptions
{
    public string Name = "";

    public string Source = "";

    public AutoImportFix? AutoImportFix;

    public string Description = "";

    public string? NewFileContent;

    public string? NewRangeContent;

    public UserPreferences? UserPreferences;
}
