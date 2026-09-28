// ported from: internal/fourslash/fourslash.go:1111-1660, 1671-2130
//
// 本文件 = Go fourslash.go 的两个切片：completions 期望值/校验（1111-1660）与 code fix 校验（1671-2130）。
//
// 偏离清单（相对 Go 原文逐条）：
// 1) `CompletionsExpectedList.UserPreferences`：Go 的 `*lsutil.UserPreferences` → C# `UserPreferences?`
//    （C# 的 `UserPreferences` 是 struct，`?` 即可空值类型）。取值参与 `ConfigureWithReset` 时写 `.Value`，
//    与 Go 的 `*options.UserPreferences` 解引用同义（值拷贝语义一致：Go 也是解引用后再改 FormatCodeSettings）。
// 2) `Ignored`（Go 的 `type Ignored = struct{}`）→ C# 空类 `Ignored`，哨兵值 = `Ignored.Value`（也接受 `new Ignored()`，
//    与 Go 的 type switch `case Ignored:` 匹配"任意 struct{} 值"等价）。
//    `ExpectedCompletionEditRange`（Go 的 `any`）退化为 `object?`：`EditRange` 实例 / `Ignored` 实例 / `null`，
//    Go 的 type switch 用 C# 模式匹配复现（含 `default:` 的 fatal 分支与 `%T` 的名字）。
// 3) `CompletionsExpectedItem`（Go 的 `*lsproto.CompletionItem | string`）→ C# 载体类：`Label`（string 分支）/
//    `Item`（*CompletionItem 分支）+ 两个隐式转换；Go 的 type switch 换成字段判空（`Label != null` / `Item != null`）。
// 4) `MarkerInput`（Go 的 `string | *Marker | []string | []*Marker`）→ C# 载体类：`MarkerName` / `SingleMarker` /
//    `MarkerNames` / `Markers` + 4 个隐式转换；Go 的 `nil` 分支 → C# 传 `null`。
//    注意：`[]string{...}`/`[]*Marker{...}` 位置的实参必须写成**显式类型**的数组（如 `new[] { "1", "2" }`），
//    因为集合字面量不能经用户定义隐式转换落到 `string[]`/`Marker[]`。
// 5) `VerifyCompletionsResult`：Go 的两个函数字段 → C# 构造参数（`Action<IHarnessTest, CompletionsExpectedCodeAction>`），
//    调用 `VerifyCompletions` 时构造，闭包捕获 `list`/`f`，与 Go 完全一致；`nil` 的 list 也照传（Go 在 `core.Find`
//    里对 nil 切片不迭代，C# 用 `?? []`）。
// 6) Go 的 `GetCompletions`（导出）/`getCompletions`（未导出）同名同签名 → 后者落成 `GetCompletionsCore`；
//    `ResolveCompletionItem`/`resolveCompletionItem` 同理落成 `ResolveCompletionItemCore`。
//    `verifyCompletionsResult` 与**类型** `VerifyCompletionsResult` 同名（C# 里成员名会遮蔽类型名）→ 落成
//    `VerifyCompletionsResultCore`。
// 7) `assertDeepEqual(t, a, b, prefix, opts...)` 不再在本文件里定义：FsCore 的 `FsCmp`（`Diff`/`AssertDeepEqual`）
//    是本仓的替代。带 prefix/忽略项的调用点统一走本文件私有帮助 `AssertDeepEqual(t, actual, expected, prefix)`
//    = `FsCmp.Diff` 非空即 `t.Fatal(prefix + ":\n" + diff)`，与 Go 的 `assertDeepEqual` 输出形状一致。
// 8) cmp 的 `ignorePaths(...)` 选项（`completionIgnoreOpts`/`autoImportIgnoreOpts`/`diagnosticsIgnoreOpts`）→ C# 退化成
//    **显式忽略投影**（Go 的 `cmp.FilterPath(...).Last()` 在任意深度按字段名匹配；LSP 的 CompletionItem/Diagnostic 里
//    这些字段名只出现在顶层 —— `RelatedInformation` 的子项只有 Location/Message，没有 Severity/Source —— 故浅层投影
//    与 Go 等价）：
//      `IgnoreCompletionFields(item)`      ↔ completionIgnoreOpts（.Kind/.SortText/.FilterText/.Data/.AdditionalTextEdits）
//      `IgnoreAutoImportFields(item)`      ↔ autoImportIgnoreOpts（再加 .LabelDetails/.Detail）
//      `IgnoreDiagnosticFields(diagnostic)`↔ diagnosticsIgnoreOpts（.Severity/.Source/.RelatedInformation）
//    比较前对**两边**都套一遍投影，再交给 `FsCmp.Diff`（Go 也是对两边同时忽略）。
//    `IgnoreDiagnosticFields`/`IgnoreAutoImportFields` 是 `internal`（前者跨切片：verifyDiagnostics 也用）。
// 9) Go 的 `var AnyTextEdits / NoTextEdits = new([]*lsproto.TextEdit)` 是"指针哨兵"塞进 AdditionalTextEdits 字段；
//    C# 的字段类型是 `TextEdit?[]?`，承载不了别的对象，故由 FsDiagSymbols 切片定义成**两个不同的空数组单例**
//    （`FourslashTest.AnyTextEdits`/`NoTextEdits`，fourslash.go:5689-5692 在其行范围内），本文件按 Go 的指针相等语义
//    用 `ReferenceEquals` 比较。
// 10) fourslash.go:6009-6046 的 `updatePositionForTextEdit`/`removeWhitespace`/`assertValidTextRange`/
//    `selectCodeFixDiagnostic` 在 Go 里只被本切片调用 ⇒ 由本文件定义（PascalCase 同名）。
// 11) `removeWhitespace`：Go 按 rune 遍历 + `stringutil.IsWhiteSpaceLike`，C# 按 UTF-16 code unit 遍历 +
//    `StringUtil.IsWhiteSpaceLike(int)`（`using static TypeScript.StringUtil.StringUtil;`）。`IsWhiteSpaceLike`
//    只对 BMP 的空白/换行/零宽字符为真（代理对不会被误判），行为一致。
// 12) Go 的 `f.getScriptInfo(x).content` → `GetScriptInfo(x).Text()`（Go 的 `scriptInfo.Text()` 返回的就是同一个
//    `content` 字段），这样不依赖 FsCore 对 ScriptInfo 字段的命名。
// 13) Go 的 `textEditSpan`（未导出字段 start/end/length）→ FsCore 冻结的 `TextEditSpan`（public 字段
//    Start/End/Length，见 local://fscore-freeze.md）；`slices.SortFunc`（不稳定）→ `List<T>.Sort`（同样不稳定）。
// 14) Go 的 `assert.Equal(t, expected, actual, msg)` / `assert.Check(t, cond, msg)` → `t.Fatal`（帮助方法
//    `FatalIfDifferent` / 直接 `if (!cond) t.Fatal(...)`）。Go 的 `assert.Check` 是"记录失败但继续"，C# 没有非致命断言；
//    改成致命只会把 Go 下一行的空指针 panic 提前成可读的失败（成功路径行为不变）。
// 15) Go 的 `%q` → `QuoteGo`（`strconv.Quote` 形状：引号/反斜杠/字母转义 + `\xHH`），`%v` 打印 `[]string` →
//    `FormatStringSlice`（`[a b c]` 形状）——都只出现在失败消息里（不进 baseline）。
// 16) `ls.CompareCompletionEntries` 在 Go 是**导出**函数（internal/ls），C# 侧是 `internal static`（Ls/Completions.Symbols.cs）。
//    本文件按 1:1 调用它（`using LsStatic = TypeScript.Ls.Ls;`，因为裸写 `Ls` 会先命中命名空间 `TypeScript.Ls`）；
//    `TypeScript.Harness` 程序集的可见性由 `[assembly: InternalsVisibleTo("TypeScript.Harness")]`
//    （src/TypeScript.Compiler/Properties/AssemblyInfo.cs）保证 —— 本探针把 src 编进同一程序集，测不到这条，
//    由仓级构建兜底。
// 17) `slices.SortStableFunc(result.List.Items, ls.CompareCompletionEntries)` → `SortCompletionItemsStable`：
//    C# 的 `Array.Sort` 不稳定，故用 (item, 原下标) 配对后以下标做 tie-break（与稳定排序同义）。
// 18) Go 的 `maps.Keys`（无序）→ `Dictionary.Keys`（只用于失败消息）。
// 19) 可空退化：Go 的 `expected.Items == nil` 会 nil 解引用 panic（`verifyCompletionsItems`/`isEmptyExpectedList`）；
//    C# 侧按"无约束"处理（`IsEmptyExpectedList` 视为空、`VerifyCompletionsItems` 直接返回）。
//    `CompletionList.Items` 在 C# 是 `CompletionItem?[]?`，元素可能为 null（Go 侧不可能）⇒ 建索引时跳过 null 元素。
// 20) Go 的 `defer reset()` → C# 的 `try/finally`（主体抽成 `VerifyCodeFixWorker`）。

using System.Globalization;
using System.Text;
using TypeScript.Core;
using TypeScript.LsProto;
using TypeScript.LsUtil;
using TypeScript.TestUtil;
using LsStatic = TypeScript.Ls.Ls;
using static TypeScript.Core.Core;
using static TypeScript.LsConv.LsConv;
using static TypeScript.LsProto.LsProto;
using static TypeScript.LsUtil.LsUtil;
using static TypeScript.StringUtil.StringUtil;

namespace TypeScript.Harness.Fourslash;

/// <summary>Go: <c>type CompletionsExpectedList struct</c>（fourslash.go:1111-1116）。</summary>
public sealed class CompletionsExpectedList
{
    public bool IsIncomplete;

    public CompletionsExpectedItemDefaults? ItemDefaults;

    public CompletionsExpectedItems? Items;

    /// <summary>Go: <c>UserPreferences *lsutil.UserPreferences</c>（C# 的 struct ⇒ `UserPreferences?`）。</summary>
    public UserPreferences? UserPreferences;
}

/// <summary>Go: <c>type Ignored = struct{}</c>（fourslash.go:1118）。哨兵值用 <see cref="Value"/>。</summary>
public sealed class Ignored
{
    public static readonly Ignored Value = new();
}

/// <summary>Go: <c>type ExpectedCompletionEditRange = any（*EditRange | Ignored | nil）</c>（fourslash.go:1121）。
/// C# 侧该联合退化为 <c>object?</c>：<see cref="EditRange"/> / <see cref="Ignored"/> / null。</summary>
public sealed class EditRange
{
    public RangeMarker? Insert;

    public RangeMarker? Replace;
}

/// <summary>Go: <c>type CompletionsExpectedItemDefaults struct</c>（fourslash.go:1128-1131）。</summary>
public sealed class CompletionsExpectedItemDefaults
{
    public string[]? CommitCharacters;

    /// <summary>Go 的 <c>ExpectedCompletionEditRange</c>（= any）⇒ C# 的 <c>object?</c>：EditRange / Ignored / null。</summary>
    public object? EditRange;
}

/// <summary>Go: <c>type CompletionsExpectedItem = any（*lsproto.CompletionItem | string）</c>（fourslash.go:1134）。</summary>
public sealed class CompletionsExpectedItem
{
    /// <summary>Go 的 string 分支。</summary>
    public string? Label;

    /// <summary>Go 的 *lsproto.CompletionItem 分支。</summary>
    public CompletionItem? Item;

    public static implicit operator CompletionsExpectedItem(string label) => new() { Label = label };

    public static implicit operator CompletionsExpectedItem(CompletionItem item) => new() { Item = item };
}

/// <summary>Go: <c>type CompletionsExpectedItems struct</c>（fourslash.go:1136-1141）。</summary>
public sealed class CompletionsExpectedItems
{
    public CompletionsExpectedItem[]? Includes;

    public string[]? Excludes;

    public CompletionsExpectedItem[]? Exact;

    public CompletionsExpectedItem[]? Unsorted;
}

/// <summary>Go: <c>type CompletionsExpectedCodeAction struct</c>（fourslash.go:1143-1148）。</summary>
public sealed class CompletionsExpectedCodeAction
{
    public string Name = "";

    public string Source = "";

    public string Description = "";

    public string NewFileContent = "";
}

/// <summary>Go: <c>type VerifyCompletionsResult struct</c>（fourslash.go:1150-1153）的两个函数字段。
/// Go 的 <c>func(t *testing.T, expectedAction *CompletionsExpectedCodeAction)</c> ⇒ C# 的
/// <c>Action&lt;IHarnessTest, CompletionsExpectedCodeAction&gt;</c>。</summary>
public sealed class VerifyCompletionsResult
{
    public readonly Action<IHarnessTest, CompletionsExpectedCodeAction> AndApplyCodeAction;

    public readonly Action<IHarnessTest, CompletionsExpectedCodeAction> AndHasNoCodeAction;

    public VerifyCompletionsResult(
        Action<IHarnessTest, CompletionsExpectedCodeAction> andApplyCodeAction,
        Action<IHarnessTest, CompletionsExpectedCodeAction> andHasNoCodeAction)
    {
        this.AndApplyCodeAction = andApplyCodeAction;
        this.AndHasNoCodeAction = andHasNoCodeAction;
    }
}

/// <summary>Go: <c>type MarkerInput = any（string | *Marker | []string | []*Marker）</c>（fourslash.go:1156）。</summary>
public sealed class MarkerInput
{
    public string? MarkerName;

    public Marker? SingleMarker;

    public string[]? MarkerNames;

    public Marker[]? Markers;

    public static implicit operator MarkerInput(string markerName) => new() { MarkerName = markerName };

    public static implicit operator MarkerInput(Marker marker) => new() { SingleMarker = marker };

    public static implicit operator MarkerInput(string[] markerNames) => new() { MarkerNames = markerNames };

    public static implicit operator MarkerInput(Marker[] markers) => new() { Markers = markers };
}

/// <summary>Go: <c>type VerifyCodeFixOptions struct</c>（fourslash.go:1671-1679）。</summary>
public sealed class VerifyCodeFixOptions
{
    public string Description = "";

    public string NewFileContent = "";

    public string NewRangeContent = "";

    public int Index;

    public bool ApplyChanges;

    public UserPreferences? UserPreferences;
}

/// <summary>Go: <c>type VerifyCodeFixAllOptions struct</c>（fourslash.go:1681-1684）。</summary>
public sealed class VerifyCodeFixAllOptions
{
    public string FixID = "";

    public string NewFileContent = "";
}

public sealed partial class FourslashTest
{
    // ── VerifyCompletions / 期望值校验（fourslash.go:1111-1660）────────────────────────────────

    /// <summary>Go: <c>func (f *FourslashTest) VerifyCompletions(t *testing.T, markerInput MarkerInput, expected *CompletionsExpectedList) VerifyCompletionsResult</c>（fourslash.go:1160-1186）。</summary>
    public VerifyCompletionsResult VerifyCompletions(IHarnessTest t, MarkerInput? markerInput, CompletionsExpectedList? expected)
    {
        CompletionList? list = null;
        switch (markerInput)
        {
            case null:
                // Go 的 nil 分支：不移动光标，直接在当前光标处请求（Go 的 type switch 里 list 保持 nil）。
                list = VerifyCompletionsWorker(t, expected);
                break;
            case { MarkerName: { } markerName }:
                GoToMarker(t, markerName);
                list = VerifyCompletionsWorker(t, expected);
                break;
            case { SingleMarker: { } marker }:
                // Go: f.goToMarker(t, marker)（= C# 的 GoToMarker(MarkerOrRange) 重载）。
                GoToMarker(t, marker);
                list = VerifyCompletionsWorker(t, expected);
                break;
            case { MarkerNames: { } markerNames }:
                // Go 在 []string / []*Marker 分支里不记 list（保持 nil），只有逐个 worker 调用。
                foreach (var markerName in markerNames)
                {
                    GoToMarker(t, markerName);
                    VerifyCompletionsWorker(t, expected);
                }

                break;
            case { Markers: { } markers }:
                foreach (var marker in markers)
                {
                    GoToMarker(t, marker);
                    VerifyCompletionsWorker(t, expected);
                }

                break;
            default:
                t.Fatal($"Invalid marker input type: {markerInput.GetType().Name}. Expected string, *Marker, []string, or []*Marker.");
                break;
        }

        return VerifyCompletionsActions(list);
    }

    /// <summary>Go: <c>func (f *FourslashTest) verifyCompletionsActions(list *lsproto.CompletionList) VerifyCompletionsResult</c>（fourslash.go:1188-1227）。</summary>
    private VerifyCompletionsResult VerifyCompletionsActions(CompletionList? list)
    {
        return new VerifyCompletionsResult(
            andApplyCodeAction: (t, expectedAction) =>
            {
                var item = Find(list?.Items, candidate =>
                    candidate != null
                    && candidate.Label == expectedAction.Name
                    && candidate.Data?.AutoImport != null
                    && candidate.Data.AutoImport.ModuleSpecifier == expectedAction.Source);
                if (item == null)
                {
                    t.Fatal($"Code action '{expectedAction.Name}' from source '{expectedAction.Source}' not found in completions.");
                }

                // Detail and AdditionalTextEdits for auto-import items are populated by
                // completionItem/resolve, not in the initial completion list.
                item = ResolveCompletionItemCore(t, item);
                if (!(item!.Detail != null && item.Detail.Contains(expectedAction.Description, StringComparison.Ordinal)))
                {
                    t.Fatal("Completion item detail does not contain expected description.");
                }

                ApplyTextEdits(t, item.AdditionalTextEdits ?? []);
                var content = GetScriptInfo(_activeFilename)!.Text();
                FatalIfDifferent(
                    t,
                    expectedAction.NewFileContent,
                    content,
                    $"File content after applying code action '{expectedAction.Name}' did not match expected content.");
            },
            andHasNoCodeAction: (t, unexpectedAction) =>
            {
                var item = Find(list?.Items, candidate =>
                    candidate != null
                    && candidate.Label == unexpectedAction.Name
                    && candidate.Data?.AutoImport != null
                    && candidate.Data.AutoImport.ModuleSpecifier == unexpectedAction.Source);
                if (item != null)
                {
                    t.Fatal($"Unexpected code action '{unexpectedAction.Name}' from source '{unexpectedAction.Source}' found in completions.");
                }
            });
    }

    /// <summary>Go: <c>func (f *FourslashTest) verifyCompletionsWorker(t *testing.T, expected *CompletionsExpectedList) *lsproto.CompletionList</c>（fourslash.go:1229-1239）。</summary>
    private CompletionList? VerifyCompletionsWorker(IHarnessTest t, CompletionsExpectedList? expected)
    {
        UserPreferences? userPreferences = null;
        if (expected != null)
        {
            userPreferences = expected.UserPreferences;
        }

        var prefix = GetCurrentPositionPrefix();
        var list = GetCompletionsCore(t, userPreferences);
        VerifyCompletionsResultCore(t, list, expected, prefix);
        return list;
    }

    /// <summary>Go: <c>func (f *FourslashTest) GetCompletions(t *testing.T, userPreferences *lsutil.UserPreferences) *lsproto.CompletionList</c>（fourslash.go:1241-1244）。</summary>
    public CompletionList? GetCompletions(IHarnessTest t, UserPreferences? userPreferences)
    {
        return GetCompletionsCore(t, userPreferences);
    }

    /// <summary>Go: <c>func (f *FourslashTest) VerifyJSDocCompletion(t *testing.T, markerInput MarkerInput, expectedOffset int, expectedText string, generateReturnInDocTemplate *bool)</c>（fourslash.go:1246-1279）。</summary>
    public void VerifyJSDocCompletion(IHarnessTest t, MarkerInput? markerInput, int expectedOffset, string expectedText, bool? generateReturnInDocTemplate)
    {
        GoToMarkerInput(t, markerInput);

        UserPreferences? userPreferences = null;
        if (generateReturnInDocTemplate != null)
        {
            var prefs = NewDefaultUserPreferences();
            prefs.GenerateReturnInDocTemplate = BoolToTristate(generateReturnInDocTemplate.Value);
            userPreferences = prefs;
        }

        var list = GetCompletionsCore(t, userPreferences);
        var item = FindJSDocCompletionItem(list);
        var script = GetScriptInfo(_activeFilename)!;
        if (item == null)
        {
            var insertStart = (int)_converters.LineAndCharacterToPosition(script, _currentCaretPosition);
            Insert(t, "/**");
            list = GetCompletionsCore(t, userPreferences);
            item = FindJSDocCompletionItem(list);
            EditScriptAndUpdateMarkers(t, _activeFilename, insertStart, insertStart + 3, "");
            _currentCaretPosition = _converters.PositionToLineAndCharacter(script, new TextPos(insertStart));
        }

        if (list == null)
        {
            t.Fatal($"{GetCurrentPositionPrefix()}Expected JSDoc completion, got nil completion list.");
        }

        if (item == null)
        {
            t.Fatal($"{GetCurrentPositionPrefix()}Expected JSDoc completion item, got {DescribeItems(list.Items)}.");
        }

        if (item.TextEdit?.InsertReplaceEdit == null)
        {
            t.Fatal($"{GetCurrentPositionPrefix()}Expected JSDoc completion to have insert/replace edit, got {DescribeValue(item.TextEdit)}.");
        }

        FatalIfDifferent(t, expectedText, item.TextEdit.InsertReplaceEdit.NewText, GetCurrentPositionPrefix());

        // Go: `_ = expectedOffset // The completion path uses snippet placeholders for caret placement.`
        _ = expectedOffset;
    }

    /// <summary>Go: <c>func (f *FourslashTest) VerifyNoJSDocCompletion(t *testing.T, markerInput MarkerInput)</c>（fourslash.go:1281-1300）。</summary>
    public void VerifyNoJSDocCompletion(IHarnessTest t, MarkerInput? markerInput)
    {
        GoToMarkerInput(t, markerInput);

        var list = GetCompletionsCore(t, null /*userPreferences*/);
        if (FindJSDocCompletionItem(list) != null)
        {
            t.Fatal($"{GetCurrentPositionPrefix()}Did not expect JSDoc completion item.");
        }

        var script = GetScriptInfo(_activeFilename)!;
        var insertStart = (int)_converters.LineAndCharacterToPosition(script, _currentCaretPosition);
        Insert(t, "/**");
        list = GetCompletionsCore(t, null /*userPreferences*/);
        var item = FindJSDocCompletionItem(list);
        EditScriptAndUpdateMarkers(t, _activeFilename, insertStart, insertStart + 3, "");
        _currentCaretPosition = _converters.PositionToLineAndCharacter(script, new TextPos(insertStart));
        if (item != null)
        {
            t.Fatal($"{GetCurrentPositionPrefix()}Did not expect JSDoc completion item.");
        }
    }

    /// <summary>Go: <c>func findJSDocCompletionItem(list *lsproto.CompletionList) *lsproto.CompletionItem</c>（fourslash.go:1302-1309）。</summary>
    private static CompletionItem? FindJSDocCompletionItem(CompletionList? list)
    {
        if (list == null)
        {
            return null;
        }

        return Find(list.Items, static item => item != null && item.Label == "/** */");
    }

    /// <summary>Go: <c>func (f *FourslashTest) goToMarkerInput(t *testing.T, markerInput MarkerInput)</c>（fourslash.go:1311-1321）。</summary>
    private void GoToMarkerInput(IHarnessTest t, MarkerInput? markerInput)
    {
        switch (markerInput)
        {
            case { MarkerName: { } markerName }:
                GoToMarker(t, markerName);
                break;
            case { SingleMarker: { } marker }:
                // Go: f.goToMarker(t, marker)（= C# 的 GoToMarker(MarkerOrRange) 重载）。
                GoToMarker(t, marker);
                break;
            default:
                t.Fatal($"Invalid marker input type: {markerInput?.GetType().Name ?? "nil"}. Expected string or *Marker.");
                break;
        }
    }

    /// <summary>Go: <c>func (f *FourslashTest) getCompletions(t *testing.T, userPreferences *lsutil.UserPreferences) *lsproto.CompletionList</c>（fourslash.go:1323-1346）。
    /// `internal`：Go 的未导出方法，`VerifyApplyCodeActionFromCompletion`（fourslash.go:2250）也会调用。</summary>
    internal CompletionList? GetCompletionsCore(IHarnessTest t, UserPreferences? userPreferences)
    {
        var parameters = new CompletionParams
        {
            TextDocument = new TextDocumentIdentifier
            {
                Uri = FileNameToDocumentURI(_activeFilename),
            },
            Position = _currentCaretPosition,
            Context = new CompletionContext(),
        };
        if (userPreferences != null)
        {
            var preferences = userPreferences.Value;
            preferences.FormatCodeSettings = _userPreferences.FormatCodeSettings;
            var reset = ConfigureWithReset(t, preferences);
            try
            {
                return SendCompletionRequestAndSort(t, parameters);
            }
            finally
            {
                reset();
            }
        }

        return SendCompletionRequestAndSort(t, parameters);
    }

    /// <summary>Go: <c>getCompletions</c> 里 <c>sendRequest</c> + <c>slices.SortStableFunc</c> 的两步（便于用 finally 复现 `defer reset()`）。</summary>
    private CompletionList? SendCompletionRequestAndSort(IHarnessTest t, CompletionParams parameters)
    {
        var result = SendRequest(t, TextDocumentCompletionInfo, parameters);

        // For performance, the server may return unsorted completion lists.
        // The client is expected to sort them by SortText and then by Label.
        // We are the client here.
        if (result.List?.Items != null)
        {
            SortCompletionItemsStable(result.List.Items);
        }

        return result.List;
    }

    /// <summary>Go: <c>slices.SortStableFunc(result.List.Items, ls.CompareCompletionEntries)</c>——C# 的 Array.Sort 不稳定，
    /// 用原下标做 tie-break（与稳定排序同义）。</summary>
    private static void SortCompletionItemsStable(CompletionItem?[] items)
    {
        var keyed = new (CompletionItem Item, int Index)[items.Length];
        for (var i = 0; i < items.Length; i++)
        {
            keyed[i] = (items[i]!, i);
        }

        Array.Sort(keyed, static (a, b) =>
        {
            var comparison = LsStatic.CompareCompletionEntries(a.Item, b.Item);
            return comparison != 0 ? comparison : a.Index - b.Index;
        });

        for (var i = 0; i < items.Length; i++)
        {
            items[i] = keyed[i].Item;
        }
    }

    /// <summary>Go: <c>func (f *FourslashTest) verifyCompletionsResult(t, actual, expected, prefix)</c>（fourslash.go:1348-1369）。
    /// 名字加 `Core`：C# 里成员名会遮蔽同名类型 `VerifyCompletionsResult`。</summary>
    private void VerifyCompletionsResultCore(IHarnessTest t, CompletionList? actual, CompletionsExpectedList? expected, string prefix)
    {
        if (actual == null)
        {
            if (!IsEmptyExpectedList(expected))
            {
                t.Fatal(prefix + "Expected completion list but got nil.");
            }

            return;
        }

        if (expected == null)
        {
            if (actual.Items == null || actual.Items.Length == 0)
            {
                return;
            }

            t.Fatal(prefix + "Expected nil completion list but got non-nil: " + FsCmp.Diff(actual, null));
        }

        if (actual.IsIncomplete != expected.IsIncomplete)
        {
            t.Fatal(prefix + "IsIncomplete mismatch");
        }

        VerifyCompletionsItemDefaults(t, actual.ItemDefaults, expected.ItemDefaults, prefix + "ItemDefaults mismatch: ");
        VerifyCompletionsItems(t, prefix, actual.Items, expected.Items);
    }

    /// <summary>Go: <c>func isEmptyExpectedList(expected *CompletionsExpectedList) bool</c>（fourslash.go:1371-1373）。</summary>
    private static bool IsEmptyExpectedList(CompletionsExpectedList? expected)
    {
        if (expected == null)
        {
            return true;
        }

        if (expected.Items == null)
        {
            // 偏离：Go 在这里会 nil 解引用 panic；C# 侧把"没给 Items"当作空期望。
            return true;
        }

        return (expected.Items.Exact?.Length ?? 0) == 0
            && (expected.Items.Includes?.Length ?? 0) == 0
            && (expected.Items.Excludes?.Length ?? 0) == 0
            && (expected.Items.Unsorted?.Length ?? 0) == 0;
    }

    /// <summary>Go: <c>func verifyCompletionsItemDefaults(t *testing.T, actual *lsproto.CompletionItemDefaults, expected *CompletionsExpectedItemDefaults, prefix string)</c>（fourslash.go:1375-1413）。</summary>
    private static void VerifyCompletionsItemDefaults(IHarnessTest t, CompletionItemDefaults? actual, CompletionsExpectedItemDefaults? expected, string prefix)
    {
        if (actual == null)
        {
            if (expected == null)
            {
                return;
            }

            t.Fatal(prefix + "Expected non-nil completion item defaults but got nil: " + FsCmp.Diff(actual, null));
        }

        if (expected == null)
        {
            t.Fatal(prefix + "Expected nil completion item defaults but got non-nil: " + FsCmp.Diff(actual, null));
        }

        AssertDeepEqual(t, actual.CommitCharacters, expected.CommitCharacters, prefix + "CommitCharacters mismatch:");
        switch (expected.EditRange)
        {
            case EditRange editRange:
                if (actual.EditRange == null)
                {
                    t.Fatal(prefix + "Expected non-nil EditRange but got nil");
                }

                var expectedInsert = editRange.Insert!.LSRange;
                var expectedReplace = editRange.Replace!.LSRange;
                AssertDeepEqual(
                    t,
                    actual.EditRange,
                    new RangeOrEditRangeWithInsertReplace
                    {
                        EditRangeWithInsertReplace = new EditRangeWithInsertReplace
                        {
                            Insert = expectedInsert,
                            Replace = expectedReplace,
                        },
                    },
                    prefix + "EditRange mismatch:");
                break;
            case null:
                if (actual.EditRange != null)
                {
                    t.Fatal(prefix + "Expected nil EditRange but got non-nil: " + FsCmp.Diff(actual.EditRange, null));
                }

                break;
            case Ignored:
                // The edit range is intentionally ignored.
                break;
            default:
                t.Fatal($"{prefix}Expected EditRange to be *EditRange or Ignored, got {expected.EditRange.GetType().Name}");
                break;
        }
    }

    /// <summary>Go: <c>func (f *FourslashTest) verifyCompletionsItems(t *testing.T, prefix string, actual []*lsproto.CompletionItem, expected *CompletionsExpectedItems)</c>（fourslash.go:1415-1546）。</summary>
    private void VerifyCompletionsItems(IHarnessTest t, string prefix, CompletionItem?[]? actual, CompletionsExpectedItems? expected)
    {
        if (expected == null)
        {
            // 偏离：Go 在这里会 nil 解引用 panic；C# 侧当作"没给约束"。
            return;
        }

        // Go 的 nil 切片与空切片在下面所有用法（len/range）里等价 ⇒ 统一成空数组。
        actual ??= [];

        if (expected.Exact != null)
        {
            if (expected.Includes != null)
            {
                t.Fatal(prefix + "Expected exact completion list but also specified 'includes'.");
            }

            if (expected.Excludes != null)
            {
                t.Fatal(prefix + "Expected exact completion list but also specified 'excludes'.");
            }

            if (expected.Unsorted != null)
            {
                t.Fatal(prefix + "Expected exact completion list but also specified 'unsorted'.");
            }

            if (actual.Length != expected.Exact.Length)
            {
                t.Fatal($"{prefix}Expected {expected.Exact.Length} exact completion items but got {actual.Length}.");
            }

            if (actual.Length > 0)
            {
                VerifyCompletionsAreExactly(t, prefix, actual, expected.Exact);
            }

            return;
        }

        var nameToActualItems = new Dictionary<string, List<CompletionItem>>();
        foreach (var item in actual)
        {
            if (item == null)
            {
                // 偏离：Go 的 []*CompletionItem 里不会有 nil（会 panic）；C# 的 CompletionItem?[] 可能有。
                continue;
            }

            if (!nameToActualItems.TryGetValue(item.Label, out var items))
            {
                items = [];
                nameToActualItems[item.Label] = items;
            }

            items.Add(item);
        }

        if (expected.Unsorted != null)
        {
            if (expected.Includes != null)
            {
                t.Fatal(prefix + "Expected unsorted completion list but also specified 'includes'.");
            }

            if (expected.Excludes != null)
            {
                t.Fatal(prefix + "Expected unsorted completion list but also specified 'excludes'.");
            }

            foreach (var item in expected.Unsorted)
            {
                if (item.Label != null)
                {
                    var label = item.Label;
                    if (!nameToActualItems.ContainsKey(label))
                    {
                        t.Fatal($"{prefix}Label '{label}' not found in actual items.");
                    }

                    nameToActualItems.Remove(label);
                }
                else if (item.Item != null)
                {
                    var expectedItem = item.Item;
                    if (!nameToActualItems.TryGetValue(expectedItem.Label, out var actualItems))
                    {
                        t.Fatal($"{prefix}Label '{expectedItem.Label}' not found in actual items.");
                    }

                    var mismatchPrefix = actualItems.Count > 1
                        ? prefix + "No completion item match for label " + expectedItem.Label + " (multiple candidates found): "
                        : prefix + "Includes completion item mismatch for label " + expectedItem.Label + ": ";
                    var itemIndex = FindMatchingCompletionItem(t, prefix, actualItems, expectedItem, ref mismatchPrefix);

                    // fail test if no match found
                    if (itemIndex == -1)
                    {
                        t.Fatal(mismatchPrefix);
                    }

                    if (actualItems.Count == 1)
                    {
                        nameToActualItems.Remove(expectedItem.Label);
                    }
                    else if (itemIndex == 0)
                    {
                        nameToActualItems[expectedItem.Label] = actualItems.GetRange(1, actualItems.Count - 1);
                    }
                    else if (itemIndex == actualItems.Count - 1)
                    {
                        nameToActualItems[expectedItem.Label] = actualItems.GetRange(0, itemIndex);
                    }
                    else
                    {
                        var remaining = new List<CompletionItem>(actualItems);
                        remaining.RemoveAt(itemIndex);
                        nameToActualItems[expectedItem.Label] = remaining;
                    }
                }
                else
                {
                    t.Fatal($"{prefix}Expected completion item to be a string or *lsproto.CompletionItem, got {DescribeExpectedItem(item)}");
                }
            }

            if (expected.Unsorted.Length != actual.Length)
            {
                var unmatched = nameToActualItems.Keys;
                t.Fatal($"{prefix}Additional completions found but not included in 'unsorted': {string.Join("\n", unmatched)}");
            }

            return;
        }

        if (expected.Includes != null)
        {
            foreach (var item in expected.Includes)
            {
                if (item.Label != null)
                {
                    if (!nameToActualItems.ContainsKey(item.Label))
                    {
                        t.Fatal($"{prefix}Label '{item.Label}' not found in actual items.");
                    }
                }
                else if (item.Item != null)
                {
                    var expectedItem = item.Item;
                    if (!nameToActualItems.TryGetValue(expectedItem.Label, out var actualItems))
                    {
                        t.Fatal($"{prefix}Label '{expectedItem.Label}' not found in actual items.");
                    }

                    var mismatchPrefix = actualItems.Count > 1
                        ? prefix + "No completion item match for label " + expectedItem.Label + " (multiple candidates found): "
                        : prefix + "Includes completion item mismatch for label " + expectedItem.Label + ": ";
                    var itemIndex = FindMatchingCompletionItem(t, prefix, actualItems, expectedItem, ref mismatchPrefix);

                    // fail test if no match found
                    if (itemIndex == -1)
                    {
                        t.Fatal(mismatchPrefix);
                    }

                    // delete previous entries since we verify entries in order
                    if (actualItems.Count == 1 || itemIndex == actualItems.Count - 1)
                    {
                        nameToActualItems.Remove(expectedItem.Label);
                    }
                    else
                    {
                        nameToActualItems[expectedItem.Label] = actualItems.GetRange(itemIndex, actualItems.Count - itemIndex);
                    }
                }
                else
                {
                    t.Fatal($"{prefix}Expected completion item to be a string or *lsproto.CompletionItem, got {DescribeExpectedItem(item)}");
                }
            }
        }

        foreach (var exclude in expected.Excludes ?? [])
        {
            if (nameToActualItems.ContainsKey(exclude))
            {
                t.Fatal($"{prefix}Label '{exclude}' should not be in actual items but was found.");
            }
        }
    }

    /// <summary>Go: `core.FindIndex(actualItems, func(actualItem) bool { if err := f.verifyCompletionItem(...); err != "" { mismatchPrefix += "\n    " + err; return false }; return true })`
    /// （fourslash.go:1469-1476 / 1519-1526）：返回首个匹配项下标，未找到返回 -1；失败项的差异累积进 <paramref name="mismatchPrefix"/>。</summary>
    private int FindMatchingCompletionItem(IHarnessTest t, string prefix, List<CompletionItem> actualItems, CompletionItem expectedItem, ref string mismatchPrefix)
    {
        for (var i = 0; i < actualItems.Count; i++)
        {
            var error = VerifyCompletionItem(t, prefix, actualItems[i], expectedItem);
            if (error != "")
            {
                mismatchPrefix += "\n    " + error;
                continue;
            }

            return i;
        }

        return -1;
    }

    /// <summary>Go: <c>func (f *FourslashTest) verifyCompletionsAreExactly(t *testing.T, prefix string, actual []*lsproto.CompletionItem, expected []CompletionsExpectedItem)</c>（fourslash.go:1548-1563）。</summary>
    private void VerifyCompletionsAreExactly(IHarnessTest t, string prefix, CompletionItem?[] actual, CompletionsExpectedItem[] expected)
    {
        var labelMismatchPrefix = prefix + "Label mismatch";
        for (var i = 0; i < actual.Length; i++)
        {
            var actualItem = actual[i]!;
            var expectedItem = expected[i];
            if (expectedItem.Label != null)
            {
                AssertDeepEqual(t, actualItem.Label, expectedItem.Label, labelMismatchPrefix);
            }
            else if (expectedItem.Item != null)
            {
                AssertDeepEqual(t, actualItem.Label, expectedItem.Item.Label, labelMismatchPrefix);
                var error = VerifyCompletionItem(t, prefix + "Completion item mismatch for label " + actualItem.Label, actualItem, expectedItem.Item);
                if (error != "")
                {
                    t.Fatal($"{prefix}Completion item mismatch for label {actualItem.Label}:\n{error}");
                }
            }
            else
            {
                t.Fatal($"Expected completion item to be a string or *lsproto.CompletionItem, got {DescribeExpectedItem(expectedItem)}");
            }
        }
    }

    /// <summary>Go: <c>var completionIgnoreOpts = ignorePaths(".Kind", ".SortText", ".FilterText", ".Data", ".AdditionalTextEdits")</c>（fourslash.go:1575）。
    /// 退化成显式忽略投影（见文件头偏离 8）：返回一个浅拷贝，把这些字段置 null。</summary>
    private static CompletionItem IgnoreCompletionFields(CompletionItem item)
    {
        var clone = CloneCompletionItem(item);
        clone.Kind = null;
        clone.SortText = null;
        clone.FilterText = null;
        clone.Data = null;
        clone.AdditionalTextEdits = null;
        return clone;
    }

    /// <summary>Go: <c>var autoImportIgnoreOpts = ignorePaths(".Kind", ".SortText", ".FilterText", ".Data", ".LabelDetails", ".Detail", ".AdditionalTextEdits")</c>（fourslash.go:1576）。</summary>
    internal static CompletionItem IgnoreAutoImportFields(CompletionItem item)
    {
        var clone = CloneCompletionItem(item);
        clone.Kind = null;
        clone.SortText = null;
        clone.FilterText = null;
        clone.Data = null;
        clone.LabelDetails = null;
        clone.Detail = null;
        clone.AdditionalTextEdits = null;
        return clone;
    }

    /// <summary>Go: <c>var diagnosticsIgnoreOpts = ignorePaths(".Severity", ".Source", ".RelatedInformation")</c>（fourslash.go:1577）。</summary>
    internal static Diagnostic IgnoreDiagnosticFields(Diagnostic diagnostic)
    {
        return new Diagnostic
        {
            Range = diagnostic.Range,
            Severity = null,
            Code = diagnostic.Code,
            CodeDescription = diagnostic.CodeDescription,
            Source = null,
            Message = diagnostic.Message,
            Tags = diagnostic.Tags,
            RelatedInformation = null,
            Data = diagnostic.Data,
        };
    }

    /// <summary>数组版 <see cref="IgnoreDiagnosticFields(Diagnostic)"/>（Go 的 opts 作用于整个切片）。</summary>
    internal static Diagnostic?[]? IgnoreDiagnosticFields(Diagnostic?[]? diagnostics)
    {
        if (diagnostics == null)
        {
            return null;
        }

        var result = new Diagnostic?[diagnostics.Length];
        for (var i = 0; i < diagnostics.Length; i++)
        {
            result[i] = diagnostics[i] == null ? null : IgnoreDiagnosticFields(diagnostics[i]!);
        }

        return result;
    }

    /// <summary>`CompletionItem` 的逐字段浅拷贝（C# 侧替代反射；用于忽略投影，不改动调用方的对象）。</summary>
    private static CompletionItem CloneCompletionItem(CompletionItem item)
    {
        return new CompletionItem
        {
            Label = item.Label,
            LabelDetails = item.LabelDetails,
            Kind = item.Kind,
            Tags = item.Tags,
            Detail = item.Detail,
            Documentation = item.Documentation,
            Deprecated = item.Deprecated,
            Preselect = item.Preselect,
            SortText = item.SortText,
            FilterText = item.FilterText,
            InsertText = item.InsertText,
            InsertTextFormat = item.InsertTextFormat,
            InsertTextMode = item.InsertTextMode,
            TextEdit = item.TextEdit,
            TextEditText = item.TextEditText,
            AdditionalTextEdits = item.AdditionalTextEdits,
            CommitCharacters = item.CommitCharacters,
            Command = item.Command,
            Data = item.Data,
        };
    }

    /// <summary>Go: <c>func (f *FourslashTest) verifyCompletionItem(t *testing.T, prefix string, actual *lsproto.CompletionItem, expected *lsproto.CompletionItem) string</c>（fourslash.go:1580-1649）。</summary>
    private string VerifyCompletionItem(IHarnessTest t, string prefix, CompletionItem actual, CompletionItem expected)
    {
        // returns error message if not matched
        var actualAutoImportFix = actual.Data?.AutoImport;
        var expectedAutoImportFix = expected.Data?.AutoImport;
        if ((actualAutoImportFix == null) != (expectedAutoImportFix == null))
        {
            return "Mismatch in auto-import data presence";
        }

        if (expected.Detail != null || expected.Documentation != null || actualAutoImportFix != null)
        {
            actual = ResolveCompletionItemCore(t, actual)!;
        }

        if (actualAutoImportFix != null)
        {
            var diff = FsCmp.Diff(IgnoreAutoImportFields(actual), IgnoreAutoImportFields(expected));
            if (diff != "")
            {
                return diff;
            }

            if (ReferenceEquals(expected.AdditionalTextEdits, AnyTextEdits))
            {
                if (actual.AdditionalTextEdits == null || actual.AdditionalTextEdits.Length == 0)
                {
                    return "Expected non-nil AdditionalTextEdits for auto-import completion item";
                }
            }
            else if (ReferenceEquals(expected.AdditionalTextEdits, NoTextEdits))
            {
                if (actual.AdditionalTextEdits != null && actual.AdditionalTextEdits.Length > 0)
                {
                    return "Expected no AdditionalTextEdits for auto-import completion item";
                }
            }

            if (expected.LabelDetails != null)
            {
                diff = FsCmp.Diff(actual.LabelDetails, expected.LabelDetails);
                if (diff != "")
                {
                    return $"LabelDetailsMismatch:\n{diff}";
                }
            }

            if (actualAutoImportFix.ModuleSpecifier != expectedAutoImportFix!.ModuleSpecifier)
            {
                return "ModuleSpecifier mismatch";
            }
        }
        else
        {
            var diff = FsCmp.Diff(IgnoreCompletionFields(actual), IgnoreCompletionFields(expected));
            if (diff != "")
            {
                return diff;
            }

            if (ReferenceEquals(expected.AdditionalTextEdits, AnyTextEdits))
            {
                if (actual.AdditionalTextEdits == null || actual.AdditionalTextEdits.Length == 0)
                {
                    return "Expected non-empty AdditionalTextEdits for completion item";
                }
            }
            else
            {
                diff = FsCmp.Diff(actual.AdditionalTextEdits, expected.AdditionalTextEdits);
                if (diff != "")
                {
                    return $"AdditionalTextEdits mismatch:\n{diff}";
                }
            }
        }

        if (expected.FilterText != null)
        {
            var diff = FsCmp.Diff(actual.FilterText, expected.FilterText);
            if (diff != "")
            {
                return $"FilterText mismatch:\n{diff}";
            }
        }

        if (expected.Kind != null)
        {
            var diff = FsCmp.Diff(actual.Kind, expected.Kind);
            if (diff != "")
            {
                return $"Kind mismatch:\n{diff}";
            }
        }

        {
            var diff = FsCmp.Diff(actual.SortText, expected.SortText ?? LsStatic.SortTextLocationPriority);
            if (diff != "")
            {
                return $"SortText mismatch:\n{diff}";
            }
        }

        return "";
    }

    /// <summary>Go: <c>func (f *FourslashTest) ResolveCompletionItem(t *testing.T, item *lsproto.CompletionItem) *lsproto.CompletionItem</c>（fourslash.go:1651-1654）。</summary>
    public CompletionItem? ResolveCompletionItem(IHarnessTest t, CompletionItem item)
    {
        return ResolveCompletionItemCore(t, item);
    }

    /// <summary>Go: <c>func (f *FourslashTest) resolveCompletionItem(t *testing.T, item *lsproto.CompletionItem) *lsproto.CompletionItem</c>（fourslash.go:1656-1659）。
    /// `internal`：Go 的未导出方法，`findCompletionForCodeAction`（fourslash.go:2226）也会调用。</summary>
    internal CompletionItem? ResolveCompletionItemCore(IHarnessTest t, CompletionItem item)
    {
        var result = SendRequest(t, CompletionItemResolveInfo, item);
        return result;
    }

    // ── Code fix 校验（fourslash.go:1671-2130）──────────────────────────────────────────────

    /// <summary>Go: <c>func (f *FourslashTest) VerifyCodeFix(t *testing.T, options VerifyCodeFixOptions)</c>（fourslash.go:1687-1691）。
    /// Go 的 `defer reset()` → C# 的 `try/finally`（主体抽成 <see cref="VerifyCodeFixWorker"/>）。</summary>
    public void VerifyCodeFix(IHarnessTest t, VerifyCodeFixOptions options)
    {
        if (options.UserPreferences != null)
        {
            var reset = ConfigureWithReset(t, options.UserPreferences.Value);
            try
            {
                VerifyCodeFixWorker(t, options);
            }
            finally
            {
                reset();
            }

            return;
        }

        VerifyCodeFixWorker(t, options);
    }

    /// <summary>Go: <c>VerifyCodeFix</c> 的函数体（fourslash.go:1692-1762）。</summary>
    private void VerifyCodeFixWorker(IHarnessTest t, VerifyCodeFixOptions options)
    {
        var actions = GetCodeFixActions(t);

        if (actions.Length == 0)
        {
            t.Fatal("No code fixes returned.");
        }

        if (options.Index >= actions.Length)
        {
            t.Fatal($"Code fix index {options.Index} out of range (got {actions.Length} fixes)");
        }

        var matchingAction = actions[options.Index];
        if (matchingAction.Title != options.Description)
        {
            var found = false;
            foreach (var action in actions)
            {
                if (action.Title == options.Description)
                {
                    matchingAction = action;
                    found = true;
                    break;
                }
            }

            if (!found)
            {
                t.Fatal($"No code fix with description {QuoteGo(options.Description)} at index {options.Index} found. Available fixes: {FormatStringSlice(actions.Select(static a => a.Title))}");
            }
        }

        var originalContent = GetScriptInfo(_activeFilename)!.Text();
        var expectedContent = options.NewFileContent;
        if (options.NewRangeContent != "")
        {
            var selection = GetSelection();
            if (selection.Pos() == selection.End())
            {
                var ranges = GetRangesInFile(_activeFilename);
                if (ranges.Length == 0)
                {
                    t.Fatal("Expected a selected range or fourslash range for NewRangeContent verification.");
                }

                selection = ranges[0].Range;
            }

            expectedContent = originalContent[..selection.Pos()] + options.NewRangeContent + originalContent[selection.End()..];
        }

        var changes = matchingAction.Edit?.Changes;
        if (options.ApplyChanges)
        {
            if (changes != null)
            {
                var expectedURI = FileNameToDocumentURI(_activeFilename);
                foreach (var (uri, edits) in changes)
                {
                    if (uri != expectedURI)
                    {
                        t.Fatal($"Code fix returned edits for unexpected URI {QuoteGo(uri.Value)} (expected {QuoteGo(expectedURI.Value)})");
                    }

                    ApplyTextEdits(t, edits ?? []);
                }
            }

            var actual = GetScriptInfo(_activeFilename)!.Text();
            FatalIfDifferent(t, expectedContent, actual, "File content after applying code fix did not match expected content.");
        }
        else
        {
            var actual = GetScriptInfo(_activeFilename)!.Text();
            if (changes != null)
            {
                var expectedURI = FileNameToDocumentURI(_activeFilename);
                foreach (var (uri, edits) in changes)
                {
                    if (uri != expectedURI)
                    {
                        t.Fatal($"Code fix returned edits for unexpected URI {QuoteGo(uri.Value)} (expected {QuoteGo(expectedURI.Value)})");
                    }

                    actual = ApplyEditsToContent(actual, edits ?? []);
                }
            }

            FatalIfDifferent(t, expectedContent, actual, "File content after applying code fix did not match expected content.");
        }
    }

    /// <summary>Go: <c>func (f *FourslashTest) VerifyRangeAfterCodeFix(t *testing.T, expectedText string, includeWhitespace bool, errorCode int, index int)</c>（fourslash.go:1764-1798）。</summary>
    public void VerifyRangeAfterCodeFix(IHarnessTest t, string expectedText, bool includeWhitespace, int errorCode, int index)
    {
        var actions = GetCodeFixActions(t, errorCode);
        if (actions.Length == 0)
        {
            t.Fatal("No code fixes returned.");
        }

        if (index >= actions.Length)
        {
            t.Fatal($"Code fix index {index} out of range (got {actions.Length} fixes)");
        }

        var action = actions[index];
        var ranges = GetRangesInFile(_activeFilename);
        if (ranges.Length != 1)
        {
            t.Fatal($"Expected exactly one range in {QuoteGo(_activeFilename)}, got {ranges.Length}.");
        }

        var edits = GetCodeActionEditsForActiveFile(t, action);
        var updatedRange = UpdateTextRangeForTextEdits(ranges[0].Range, edits);
        AssertValidTextRange(t, updatedRange, $"Code fix {QuoteGo(action.Title)} replaced part of the expected range; unable to compute rangeAfterCodeFix result.");

        ApplyTextEdits(t, edits);
        var actualContent = GetScriptInfo(_activeFilename)!.Text();
        var actualText = actualContent[updatedRange.Pos()..updatedRange.End()];

        if (includeWhitespace)
        {
            FatalIfDifferent(t, expectedText, actualText, "Range content after applying code fix did not match expected content.");
            return;
        }

        actualText = RemoveWhitespace(actualText);
        expectedText = RemoveWhitespace(expectedText);
        FatalIfDifferent(t, expectedText, actualText, "Range content after applying code fix did not match expected content.");
    }

    /// <summary>Go: <c>func (f *FourslashTest) getCodeActionEditsForActiveFile(t *testing.T, action *lsproto.CodeAction) []*lsproto.TextEdit</c>（fourslash.go:1800-1816）。
    /// Go 末尾的 `panic("unreachable")` 是 Go 编译器"函数必须有终止语句"的要求（`t.Fatalf` 不终止）；C# 的
    /// <see cref="IHarnessTest.Fatal"/> 标了 `[DoesNotReturn]`，故省略。</summary>
    private TextEdit?[] GetCodeActionEditsForActiveFile(IHarnessTest t, CodeAction action)
    {
        var changes = action.Edit?.Changes;
        if (changes == null)
        {
            t.Fatal($"Code fix {QuoteGo(action.Title)} did not return text edits.");
        }

        if (changes!.Count != 1)
        {
            t.Fatal($"Code fix {QuoteGo(action.Title)} returned edits for multiple files; rangeAfterCodeFix expects only the active file.");
        }

        if (!changes.TryGetValue(FileNameToDocumentURI(_activeFilename), out var edits))
        {
            t.Fatal($"Code fix {QuoteGo(action.Title)} did not return edits for active file {QuoteGo(_activeFilename)}.");
        }

        return edits ?? [];
    }

    /// <summary>Go: <c>func (f *FourslashTest) VerifyCodeFixAvailable(t *testing.T, expectedDescriptions []string)</c>（fourslash.go:1818-1851）。</summary>
    public void VerifyCodeFixAvailable(IHarnessTest t, string[]? expectedDescriptions)
    {
        var actions = GetCodeFixActions(t);

        if (expectedDescriptions == null)
        {
            if (actions.Length == 0)
            {
                t.Fatal("Expected code fixes to be available, but got none.");
            }

            return;
        }

        if (expectedDescriptions.Length == 0)
        {
            VerifyCodeFixNotAvailable(t);
            return;
        }

        foreach (var expected in expectedDescriptions)
        {
            var found = false;
            foreach (var action in actions)
            {
                if (action.Title == expected)
                {
                    found = true;
                    break;
                }
            }

            if (!found)
            {
                t.Fatal($"Expected code fix with description {QuoteGo(expected)} not found. Available fixes: {FormatStringSlice(actions.Select(static a => a.Title))}");
            }
        }
    }

    /// <summary>Go: <c>func (f *FourslashTest) VerifyCodeFixNotAvailable(t *testing.T, expected ...string)</c>（fourslash.go:1853-1878）。</summary>
    public void VerifyCodeFixNotAvailable(IHarnessTest t, params string[] expected)
    {
        var actions = GetCodeFixActions(t);
        if (expected.Length == 0)
        {
            if (actions.Length == 0)
            {
                return;
            }

            t.Fatal($"Expected no code fixes, but got: {FormatStringSlice(actions.Select(static action => action.Title))}");
        }

        foreach (var title in expected)
        {
            foreach (var action in actions)
            {
                if (action.Title == title)
                {
                    t.Fatal($"Expected code fix with description {QuoteGo(title)} not to be available.");
                }
            }
        }
    }

    /// <summary>Go: <c>func (f *FourslashTest) VerifyCodeFixAvailableExact(t *testing.T, expectedDescriptions []string)</c>（fourslash.go:1880-1912）。</summary>
    public void VerifyCodeFixAvailableExact(IHarnessTest t, string[] expectedDescriptions)
    {
        var actions = GetCodeFixActions(t);

        if (actions.Length != expectedDescriptions.Length)
        {
            t.Fatal($"Expected exactly {expectedDescriptions.Length} code fixes, but got {actions.Length}. Available fixes: {FormatStringSlice(actions.Select(static a => a.Title))}");
        }

        foreach (var expected in expectedDescriptions)
        {
            var found = false;
            foreach (var action in actions)
            {
                if (action.Title == expected)
                {
                    found = true;
                    break;
                }
            }

            if (!found)
            {
                t.Fatal($"Expected code fix with description {QuoteGo(expected)} not found. Available fixes: {FormatStringSlice(actions.Select(static a => a.Title))}");
            }
        }
    }

    /// <summary>Go: <c>func (f *FourslashTest) VerifyCodeFixAll(t *testing.T, options VerifyCodeFixAllOptions)</c>（fourslash.go:1914-1967）。</summary>
    public void VerifyCodeFixAll(IHarnessTest t, VerifyCodeFixAllOptions options)
    {
        var actions = GetAllQuickFixActions(t);
        if (actions.Length == 0)
        {
            t.Fatal($"No code fixes available for fixId {QuoteGo(options.FixID)}");
        }

        // Find fix-all actions. The server returns these as quickfix entries with titles like
        // "Add all missing imports" when multiple diagnostics match the same provider.
        // We look for actions that are NOT single-diagnostic fixes (i.e., have no Diagnostics attached).
        var fixAllCandidates = new List<CodeAction>();
        foreach (var action in actions)
        {
            if (action.Diagnostics == null || action.Diagnostics.Length == 0)
            {
                fixAllCandidates.Add(action);
            }
        }

        CodeAction? fixAllAction = null;
        if (fixAllCandidates.Count == 1)
        {
            fixAllAction = fixAllCandidates[0];
        }
        else
        {
            // If there are multiple fix-all candidates, match by FixID in the title.
            foreach (var action in fixAllCandidates)
            {
                if (ToLowerJS(action.Title).Contains(ToLowerJS(options.FixID), StringComparison.Ordinal))
                {
                    fixAllAction = action;
                    break;
                }
            }
        }

        if (fixAllAction == null)
        {
            t.Fatal($"No fix-all code action found for fixId {QuoteGo(options.FixID)}. Available fixes: {FormatStringSlice(actions.Select(static a => a.Title))}");
        }

        var changes = fixAllAction.Edit?.Changes;
        if (changes != null)
        {
            var expectedURI = FileNameToDocumentURI(_activeFilename);
            foreach (var (uri, edits) in changes)
            {
                if (uri != expectedURI)
                {
                    t.Fatal($"Fix-all code action returned edits for unexpected URI {QuoteGo(uri.Value)} (expected {QuoteGo(expectedURI.Value)})");
                }

                ApplyTextEdits(t, edits ?? []);
            }
        }

        var actual = GetScriptInfo(_activeFilename)!.Text();
        FatalIfDifferent(t, options.NewFileContent, actual, "File content after applying all code fixes did not match expected content.");
    }

    /// <summary>Go: <c>func (f *FourslashTest) VerifySourceFixAll(t *testing.T, expectedContent string)</c>（fourslash.go:1969-2017）。</summary>
    public void VerifySourceFixAll(IHarnessTest t, string expectedContent)
    {
        var only = new[] { CodeActionKind.SourceFixAll };
        var parameters = new CodeActionParams
        {
            TextDocument = new TextDocumentIdentifier
            {
                Uri = FileNameToDocumentURI(_activeFilename),
            },
            Range = new LsProto.Range
            {
                Start = CopyPosition(_currentCaretPosition),
                End = CopyPosition(_currentCaretPosition),
            },
            Context = new CodeActionContext
            {
                Diagnostics = [],
                Only = only,
            },
        };
        var result = SendRequest(t, TextDocumentCodeActionInfo, parameters);

        if (result.CommandOrCodeActionArray == null)
        {
            t.Fatal("No source.fixAll code actions returned");
        }

        CodeAction? selected = null;
        foreach (var item in result.CommandOrCodeActionArray!)
        {
            var codeAction = item.CodeAction;
            if (codeAction?.Kind == null || codeAction.Kind != CodeActionKind.SourceFixAllTs)
            {
                continue;
            }

            selected = codeAction;
            break;
        }

        if (selected == null)
        {
            t.Fatal("No source.fixAll code action found");
        }

        var changes = selected.Edit?.Changes;
        if (changes != null)
        {
            var expectedURI = FileNameToDocumentURI(_activeFilename);
            foreach (var (uri, edits) in changes)
            {
                if (uri != expectedURI)
                {
                    t.Fatal($"source.fixAll returned edits for unexpected URI {QuoteGo(uri.Value)} (expected {QuoteGo(expectedURI.Value)})");
                }

                ApplyTextEdits(t, edits ?? []);
            }
        }

        var actual = GetScriptInfo(_activeFilename)!.Text();
        FatalIfDifferent(t, expectedContent, actual, "File content after source.fixAll did not match expected content.");
    }

    /// <summary>Go: <c>func (f *FourslashTest) getCodeFixActions(t *testing.T, errorCode ...int) []*lsproto.CodeAction</c>（fourslash.go:2019-2031）。</summary>
    private CodeAction[] GetCodeFixActions(IHarnessTest t, params int[] errorCode)
    {
        var all = GetAllQuickFixActions(t, errorCode);
        // Filter to only per-diagnostic fixes (those with diagnostics attached)
        var actions = new List<CodeAction>();
        foreach (var action in all)
        {
            if (action.Diagnostics != null && action.Diagnostics.Length > 0)
            {
                actions.Add(action);
            }
        }

        return [.. actions];
    }

    /// <summary>Go: <c>func (f *FourslashTest) getAllQuickFixActions(t *testing.T, errorCode ...int) []*lsproto.CodeAction</c>（fourslash.go:2033-2081）。</summary>
    private CodeAction[] GetAllQuickFixActions(IHarnessTest t, params int[] errorCode)
    {
        var diagnosticParameters = new DocumentDiagnosticParams
        {
            TextDocument = new TextDocumentIdentifier
            {
                Uri = FileNameToDocumentURI(_activeFilename),
            },
        };
        var diagnosticResult = SendRequest(t, TextDocumentDiagnosticInfo, diagnosticParameters);

        var diagnostics = diagnosticResult.FullDocumentDiagnosticReport?.Items ?? [];
        if (diagnostics.Length == 0)
        {
            return [];
        }

        var diagnostic = SelectCodeFixDiagnostic(diagnostics, FirstOrNil(errorCode));
        if (diagnostic == null)
        {
            return [];
        }

        var parameters = new CodeActionParams
        {
            TextDocument = new TextDocumentIdentifier
            {
                Uri = FileNameToDocumentURI(_activeFilename),
            },
            Range = new LsProto.Range
            {
                Start = CopyPosition(diagnostic.Range.Start),
                End = CopyPosition(diagnostic.Range.End),
            },
            Context = new CodeActionContext
            {
                Diagnostics = diagnostics,
            },
        };
        var result = SendRequest(t, TextDocumentCodeActionInfo, parameters);

        var actions = new List<CodeAction>();
        if (result.CommandOrCodeActionArray != null)
        {
            foreach (var item in result.CommandOrCodeActionArray)
            {
                if (item.CodeAction?.Kind == CodeActionKind.QuickFix)
                {
                    actions.Add(item.CodeAction);
                }
            }
        }

        return [.. actions];
    }

    /// <summary>Go: <c>func (f *FourslashTest) updateTextRangeForTextEdits(textRange core.TextRange, edits []*lsproto.TextEdit) core.TextRange</c>（fourslash.go:2083-2113）。</summary>
    private TextRange UpdateTextRangeForTextEdits(TextRange textRange, TextEdit?[] edits)
    {
        var script = GetScriptInfo(_activeFilename)!;
        var spans = new List<TextEditSpan>(edits.Length);
        foreach (var edit in edits)
        {
            if (edit == null)
            {
                // 偏离：Go 的 []*TextEdit 里不会有 nil（会 panic）；C# 侧跳过。
                continue;
            }

            spans.Add(new TextEditSpan
            {
                Start = (int)_converters.LineAndCharacterToPosition(script, edit.Range.Start),
                End = (int)_converters.LineAndCharacterToPosition(script, edit.Range.End),
                Length = edit.NewText.Length,
            });
        }

        spans.Sort(static (a, b) => a.Start - b.Start);

        var pos = textRange.Pos();
        var end = textRange.End();
        for (var i = 0; i < spans.Count; i++)
        {
            var editSpan = spans[i];
            pos = UpdatePositionForTextEdit(pos, editSpan.Start, editSpan.End, editSpan.Length);
            end = UpdatePositionForTextEdit(end, editSpan.Start, editSpan.End, editSpan.Length);

            var delta = editSpan.Length - (editSpan.End - editSpan.Start);
            for (var j = i + 1; j < spans.Count; j++)
            {
                if (spans[j].Start >= editSpan.Start)
                {
                    spans[j].Start += delta;
                    spans[j].End += delta;
                }
            }
        }

        return NewTextRange(pos, end);
    }

    /// <summary>Go: <c>func (f *FourslashTest) applyEditsToContent(content string, edits []*lsproto.TextEdit) string</c>（fourslash.go:2115-2128）。</summary>
    private string ApplyEditsToContent(string content, TextEdit?[] edits)
    {
        var script = GetScriptInfo(_activeFilename)!;
        StableSortByStart(edits, edit => (int)_converters.LineAndCharacterToPosition(script, edit!.Range.Start));
        for (var i = edits.Length - 1; i >= 0; i--)
        {
            var edit = edits[i]!;
            var start = (int)_converters.LineAndCharacterToPosition(script, edit.Range.Start);
            var end = (int)_converters.LineAndCharacterToPosition(script, edit.Range.End);
            content = string.Concat(content[..start], edit.NewText, content[end..]);
        }

        return content;
    }

    // ── fourslash.go:6009-6046（只被本切片使用 ⇒ 由本文件定义）────────────────────────────────

    /// <summary>Go: <c>func updatePositionForTextEdit(position int, editStart int, editEnd int, newTextLength int) int</c>（fourslash.go:6009-6017）。</summary>
    internal static int UpdatePositionForTextEdit(int position, int editStart, int editEnd, int newTextLength)
    {
        if (position <= editStart)
        {
            return position;
        }

        if (position < editEnd)
        {
            return -1;
        }

        return position + newTextLength - (editEnd - editStart);
    }

    /// <summary>Go: <c>func removeWhitespace(text string) string</c>（fourslash.go:6019-6028）。</summary>
    internal static string RemoveWhitespace(string text)
    {
        var builder = new StringBuilder(text.Length);
        foreach (var ch in text)
        {
            if (IsWhiteSpaceLike(ch))
            {
                continue;
            }

            builder.Append(ch);
        }

        return builder.ToString();
    }

    /// <summary>Go: <c>func assertValidTextRange(t *testing.T, textRange core.TextRange, message string)</c>（fourslash.go:6030-6036）。</summary>
    internal static void AssertValidTextRange(IHarnessTest t, TextRange textRange, string message)
    {
        if (textRange.Pos() >= 0 && textRange.End() >= 0)
        {
            return;
        }

        t.Fatal(message);
    }

    /// <summary>Go: <c>func selectCodeFixDiagnostic(diagnostics []*lsproto.Diagnostic, errorCode int) *lsproto.Diagnostic</c>（fourslash.go:6038-6045）。</summary>
    internal static Diagnostic? SelectCodeFixDiagnostic(Diagnostic?[] diagnostics, int errorCode)
    {
        if (errorCode == 0)
        {
            return diagnostics[0];
        }

        return Find(diagnostics, diagnostic => diagnostic?.Code?.Integer == errorCode);
    }

    // ── 本切片私有帮助 ──────────────────────────────────────────────────────────────────────

    /// <summary>Go: <c>func assertDeepEqual(t *testing.T, actual any, expected any, prefix string, opts ...cmp.Option)</c>（fourslash.go:1661-1668）
    /// 的 C# 退化形式（无 opts；忽略项由调用方先做显式投影，见文件头偏离 7/8）。</summary>
    private static void AssertDeepEqual(IHarnessTest t, object? actual, object? expected, string prefix)
    {
        var diff = FsCmp.Diff(actual, expected);
        if (diff != "")
        {
            t.Fatal($"{prefix}:\n{diff}");
        }
    }

    /// <summary>Go 的 <c>assert.Equal(t, expected, actual, msg)</c>（本文件里都用于字符串内容比较）。</summary>
    private static void FatalIfDifferent(IHarnessTest t, string expected, string actual, string message)
    {
        if (!string.Equals(expected, actual, StringComparison.Ordinal))
        {
            t.Fatal($"{message}\nExpected: {expected}\nActual: {actual}");
        }
    }

    /// <summary>Go: <c>fmt</c> 的 <c>%v</c> 打印 <c>[]string</c> 的形状（失败消息用）。</summary>
    private static string FormatStringSlice(IEnumerable<string> values)
    {
        return "[" + string.Join(" ", values) + "]";
    }

    /// <summary>Go: <c>fmt</c> 的 <c>%q</c>（= <c>strconv.Quote</c>）形状：引号/反斜杠/字母转义 + ASCII 控制字符
    /// <c>\xHH</c>，其余字符原样（失败消息用；与 JsonRpc.Reader.GoQuote 同形状）。</summary>
    private static string QuoteGo(string text)
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

    /// <summary>Go 的结构体值拷贝：`lsproto.Position` 在 C# 是 class，直接赋值是共享引用 ⇒ 逐字段复制
    /// （Go 的 `lsproto.Range{Start: x.Start}` / `Range: diagnostic.Range` 都是值拷贝）。</summary>
    private static Position CopyPosition(Position position)
    {
        return new Position
        {
            Line = position.Line,
            Character = position.Character,
        };
    }

    /// <summary>Go 的 <c>%T</c>（类型名）在失败消息里的可读替代。</summary>
    private static string DescribeExpectedItem(CompletionsExpectedItem item)
    {
        return item.Label != null ? "string" : item.Item != null ? "*lsproto.CompletionItem" : "nil";
    }

    /// <summary>Go 的 <c>%#v</c> 在失败消息里的可读替代（完成列表 items）。</summary>
    private static string DescribeItems(CompletionItem?[]? items)
    {
        if (items == null)
        {
            return "nil";
        }

        return FormatStringSlice(items.Select(static item => item == null ? "nil" : item.Label));
    }

    /// <summary>Go 的 <c>%#v</c> 在失败消息里的可读替代（TextEdit 联合）。</summary>
    private static string DescribeValue(object? value)
    {
        return value == null ? "nil" : value.GetType().Name;
    }
}
