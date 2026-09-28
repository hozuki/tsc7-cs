// ported from: internal/fourslash/tests/util/util.go（函数部分 + 依赖函数的少量 var）
//
// 偏离清单（相对 Go 原文逐条）：
// 1) Go 的 dot-import 包 `tests/util`（`package fourslash_test`）在 C# 里落成 static class
//    `FourslashTestUtil`（partial：数据表在 FourslashTestUtil.cs，由 genutil.mjs 机械生成）。
// 2) `var Ignored = struct{}{}`：C# 侧用既有的 `Ignored` 类（FourslashTest.Completions.cs）的哨兵
//    `Ignored.Value`；生成的用例里 `util.Ignored`/`fourslashUtil.Ignored`/裸 `Ignored` 都指向它。
// 3) `slices.Clone(items)` → `new List<T>(items)`；`slices.SortStableFunc` → 稳定排序
//    （`OrderBy(...).ThenBy(...)` 稳定，与 Go 的 SortStableFunc 同语义）；`slices.Concat` → 集合展开；
//    `append(all, xs...)` → `List.AddRange`。
// 4) `panic(fmt.Sprintf(...))` → `throw new InvalidOperationException(...)`：C# 无 Go 的 `%T`，消息文本
//    与 Go 不同；只在语料写错时可见，不进 baseline。
// 5) `core.OrElse` → `CORE.OrElse`（本文件的 `using CORE = TypeScript.Core.Core;` 别名，避免与命名空间 `Core`
//    遮蔽）；`core.Filter(items, pred)` 用显式循环展开（避免额外闭包/分配）。
// 6) `stringutil.CompareStringsCaseInsensitiveThenSensitive` → C# 同名函数（StringUtil/Compare.cs），
//    经 `SortTextComparer` 适配给 LINQ。
// 7) `switch item := item.(type)` 的联合判别：C# 的 `CompletionsExpectedItem` 是
//    { Item: CompletionItem? , Label: string? } 的显式联合，故 `case *lsproto.CompletionItem` → `Item is not null`。
using F = TypeScript.Harness.Fourslash;
using LS = TypeScript.Ls.Ls;
using LSP = TypeScript.LsProto;
using CORE = TypeScript.Core.Core;

namespace TypeScript.Harness.FourslashCases;

/// <summary>Go 的 <c>stringutil.CompareStringsCaseInsensitiveThenSensitive</c>（util.go 的排序比较函数）。</summary>
internal sealed class SortTextComparer : IComparer<string>
{
    public static readonly SortTextComparer Instance = new();

    public int Compare(string? x, string? y) =>
        TypeScript.StringUtil.StringUtil.CompareStringsCaseInsensitiveThenSensitive(x ?? "", y ?? "");
}

public static partial class FourslashTestUtil
{
    /// <summary>Go: <c>func InsertReplaceTextEdit(newText string, editRange lsproto.Range) *lsproto.TextEditOrInsertReplaceEdit</c>（util.go:18）。</summary>
    public static LSP.TextEditOrInsertReplaceEdit InsertReplaceTextEdit(string newText, LSP.Range editRange)
    {
        return new LSP.TextEditOrInsertReplaceEdit
        {
            InsertReplaceEdit = new LSP.InsertReplaceEdit
            {
                NewText = newText,
                Insert = editRange,
                Replace = editRange,
            },
        };
    }

    // ---- 由数据表派生的 var（Go 里是 var X = f(...) 形式，需函数体，故手写） --------------------
    public static readonly F.CompletionsExpectedItem[] CompletionClassElementInJSKeywords =
        GetInJSKeywords(CompletionClassElementKeywords);

    public static readonly F.CompletionsExpectedItem[] CompletionGlobals = SortCompletionItems(
    [
        .. CompletionGlobalVars,
        .. CompletionGlobalKeywords,
        CompletionGlobalThisItem,
        CompletionUndefinedVarItem,
    ]);

    public static readonly F.CompletionsExpectedItem[] CompletionGlobalTypes = CompletionGlobalTypesPlus(null);

    public static readonly F.CompletionsExpectedItem[] CompletionGlobalInJSKeywords =
        GetInJSKeywords(CompletionGlobalKeywords);

    public static readonly F.CompletionsExpectedItem[] CompletionFunctionMembersWithPrototype = SortCompletionItems(
    [
        .. CompletionFunctionMembers,
        new LSP.CompletionItem { Label = "prototype", Kind = LSP.CompletionItemKind.Field },
    ]);

    public static readonly F.CompletionsExpectedItem[] CompletionTypeAssertionKeywords = CompletionGlobalTypesPlus(
    [
        new LSP.CompletionItem
        {
            Label = "const",
            Kind = LSP.CompletionItemKind.Keyword,
            SortText = LS.SortTextGlobalsOrKeywords,
        },
    ]);

    /// <summary>Go: <c>func sortCompletionItems(items []fourslash.CompletionsExpectedItem) []fourslash.CompletionsExpectedItem</c>（util.go:1364）。</summary>
    public static F.CompletionsExpectedItem[] SortCompletionItems(IEnumerable<F.CompletionsExpectedItem> items)
    {
        var list = new List<F.CompletionsExpectedItem>(items);
        return
        [
            .. list
                .OrderBy(SortTextOf, SortTextComparer.Instance)
                .ThenBy(LabelOf, SortTextComparer.Instance),
        ];
    }

    private static string SortTextOf(F.CompletionsExpectedItem item)
    {
        var defaultSortText = LS.SortTextLocationPriority;
        // Go: util.go:1364-1390 `var aSortText string; if a.SortText != nil { aSortText = *a.SortText }` 后
        // `aSortText = core.OrElse(aSortText, defaultSortText)`。Go 里 item 不是 *lsproto.CompletionItem（即裸 string
        // 条目）或 SortText == nil 时，aSortText 保持 Go string 的零值 ""，OrElse 的零值判据同样是 "" ⇒ 落回
        // defaultSortText。C# 用 `?? ""` 补零值 + Core.OrElse 的 string 重载，与 Go 逐字对应。
        var sortText = item.Item?.SortText ?? "";
        return CORE.OrElse(sortText, defaultSortText)!;
    }

    private static string LabelOf(F.CompletionsExpectedItem item)
    {
        if (item.Item is not null)
        {
            return item.Item.Label;
        }
        if (item.Label is not null)
        {
            return item.Label;
        }
        throw new InvalidOperationException("unexpected completion item type");
    }

    /// <summary>Go: <c>func CompletionGlobalsPlus(items []fourslash.CompletionsExpectedItem, noLib bool) []fourslash.CompletionsExpectedItem</c>（util.go:1410）。</summary>
    public static F.CompletionsExpectedItem[] CompletionGlobalsPlus(
        IReadOnlyList<F.CompletionsExpectedItem>? items,
        bool noLib)
    {
        var list = new List<F.CompletionsExpectedItem>();
        if (noLib)
        {
            if (items is not null)
            {
                list.AddRange(items);
            }
            list.Add(CompletionGlobalThisItem);
            list.Add(CompletionUndefinedVarItem);
            list.AddRange(CompletionGlobalKeywords);
        }
        else
        {
            if (items is not null)
            {
                list.AddRange(items);
            }
            list.AddRange(CompletionGlobals);
        }
        return SortCompletionItems(list);
    }

    /// <summary>Go: <c>func CompletionGlobalTypesPlus(items []fourslash.CompletionsExpectedItem) []fourslash.CompletionsExpectedItem</c>（util.go:1424）。</summary>
    public static F.CompletionsExpectedItem[] CompletionGlobalTypesPlus(
        IReadOnlyList<F.CompletionsExpectedItem>? items)
    {
        var list = new List<F.CompletionsExpectedItem>();
        list.AddRange(CompletionGlobalTypeDecls);
        list.Add(CompletionGlobalThisItem);
        list.AddRange(CompletionTypeKeywords);
        if (items is not null)
        {
            list.AddRange(items);
        }
        return SortCompletionItems(list);
    }

    /// <summary>Go: <c>func getInJSKeywords(keywords []fourslash.CompletionsExpectedItem) []fourslash.CompletionsExpectedItem</c>（util.go:1435）。
    /// Go 未导出 → C# 的 <c>GetInJSKeywords</c>（仅本 partial 内使用）。</summary>
    public static F.CompletionsExpectedItem[] GetInJSKeywords(IReadOnlyList<F.CompletionsExpectedItem> keywords)
    {
        var result = new List<F.CompletionsExpectedItem>();
        foreach (var item in keywords)
        {
            var label = LabelOf(item);
            var keep = label switch
            {
                "enum" or "interface" or "implements" or "private" or "protected" or "public" or "abstract"
                    or "any" or "boolean" or "declare" or "infer" or "is" or "keyof" or "module" or "namespace"
                    or "never" or "readonly" or "number" or "object" or "string" or "symbol" or "type"
                    or "unique" or "override" or "unknown" or "global" or "bigint" => false,
                _ => true,
            };
            if (keep)
            {
                result.Add(item);
            }
        }
        return [.. result];
    }

    /// <summary>Go: <c>func CompletionGlobalsInJSPlus(items []fourslash.CompletionsExpectedItem, noLib bool) []fourslash.CompletionsExpectedItem</c>（util.go:1462）。</summary>
    public static F.CompletionsExpectedItem[] CompletionGlobalsInJSPlus(
        IReadOnlyList<F.CompletionsExpectedItem>? items,
        bool noLib)
    {
        var list = new List<F.CompletionsExpectedItem>();
        if (items is not null)
        {
            list.AddRange(items);
        }
        list.Add(CompletionGlobalThisItem);
        list.Add(CompletionUndefinedVarItem);
        list.AddRange(CompletionGlobalInJSKeywords);
        if (!noLib)
        {
            list.AddRange(CompletionGlobalVars);
        }
        return SortCompletionItems(list);
    }

    /// <summary>Go: <c>func CompletionFunctionMembersPlus(items []fourslash.CompletionsExpectedItem) []fourslash.CompletionsExpectedItem</c>（util.go:1533）。</summary>
    public static F.CompletionsExpectedItem[] CompletionFunctionMembersPlus(
        IReadOnlyList<F.CompletionsExpectedItem>? items)
    {
        var list = new List<F.CompletionsExpectedItem>();
        list.AddRange(CompletionFunctionMembers);
        if (items is not null)
        {
            list.AddRange(items);
        }
        return SortCompletionItems(list);
    }

    /// <summary>Go: <c>func CompletionFunctionMembersWithPrototypePlus(items []fourslash.CompletionsExpectedItem) []fourslash.CompletionsExpectedItem</c>（util.go:1552）。</summary>
    public static F.CompletionsExpectedItem[] CompletionFunctionMembersWithPrototypePlus(
        IReadOnlyList<F.CompletionsExpectedItem>? items)
    {
        var list = new List<F.CompletionsExpectedItem>();
        list.AddRange(CompletionFunctionMembersWithPrototype);
        if (items is not null)
        {
            list.AddRange(items);
        }
        return SortCompletionItems(list);
    }

    /// <summary>Go: <c>func CompletionTypeKeywordsPlus(items []fourslash.CompletionsExpectedItem) []fourslash.CompletionsExpectedItem</c>（util.go:1561）。</summary>
    public static F.CompletionsExpectedItem[] CompletionTypeKeywordsPlus(
        IReadOnlyList<F.CompletionsExpectedItem>? items)
    {
        var list = new List<F.CompletionsExpectedItem>();
        list.AddRange(CompletionTypeKeywords);
        if (items is not null)
        {
            list.AddRange(items);
        }
        return SortCompletionItems(list);
    }

    /// <summary>Go: <c>func ToAny[T any](items []T) []any</c>（util.go:1578）。</summary>
    public static object?[] ToAny<T>(IEnumerable<T> items)
    {
        var result = new List<object?>();
        foreach (var item in items)
        {
            result.Add(item);
        }
        return [.. result];
    }
}
