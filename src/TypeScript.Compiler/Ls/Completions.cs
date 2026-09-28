// ported from: internal/ls/completions.go:1-6904
//
// 本文件是 `completions.go` 的 **共享类型 + 入口 + getCompletionData** 分节（Go 行 1-1801）。
// `completions.go` 里**所有** type 声明（含 B/C 分节使用的未导出 struct）都在本文件声明一次；
// 分节文件 `Completions.Items.cs`（Go 行 1802-2473 ∪ 4840-6904）与 `Completions.Symbols.cs`
// （Go 行 2474-4839）**不得重复声明**这些类型（CS0101）。
//
// 与 Go 的偏离（逐条说明）：
//
// 1) Go 的 `type SortText string` 命名类型 → C# 直接用 `string`：该类型在 Go 里只承载排序文本的字符串
//    （唯一操作是字符串连接/比较，`SortText(x)`/`string(s)` 转换在 C# 是恒等）。排序常量落成
//    `Ls` 的 `public const string SortText*`，与 Go 的 `SortText*` 逐值一致。
// 2) Go 的 `completionData = any`（5 个具体类型）→ C# 抽象基类 `CompletionData` + 5 个派生类；
//    Go 的 type switch → C# 的 `switch (data) { case CompletionDataData d: … }`。
// 3) Go 的嵌入字段 `*lsproto.CompletionItem` → 组合字段 `_completionItem`（本仓先例 Execute/Tsc/ExtendedConfigCache.cs
//    的 `_entry`；LsProto 生成物是 sealed 类，不能继承）。Go 的 `entry.CompletionItem` → C# `entry._completionItem`。
// 4) `[]*CompletionItem` → `CompletionItem[]`（Go 的 nil 切片 → `?? []`；遍历前判空）。
//    ⚠ `new T[n]` 的零填充是 `null`，Go 的 `make([]T,n)` 是零值 ⇒ 需要零值时要逐元素显式赋值。
// 5) `map[K]V` → `Dictionary<K,V>`（Go 遍历 nil map 不迭代 ⇒ C# 用空字典）；`collections.Set[T]` → `Set<T>`；
//    `collections.SyncMap[K,V]` → `SyncMap<K,V>`（`Load` 返回 `(V? Value, bool Ok)`）。
// 6) `lsproto.GetClientCapabilities(ctx)` → 本仓 `GetClientCapabilities()`（无 ctx 参数，见 LsProto/Lsp.cs:549）。
//    `lsproto.PreferredMarkupKind(...)` 同名。
// 7) `ctx.Err() != nil` → `ctx.CancellationToken.IsCancellationRequested`（Core/Context.cs 只暴露 CancellationToken）。
// 8) Go 的联合类型别名（`*ast.PropertyAccessExpressionNode` / `*ast.CallLikeExpression` / `*ast.Expression` /
//    `*ast.Declaration` … 都是 `= Node`）⇒ C# 参数/返回一律写 `Node`。
// 9) `error` → `Exception?`；`(T, error)` → C# `(T, Exception? Error)`；`(a, b, error)` → `(A, B, Exception? Error)`。
//    `fmt.Errorf("…%s…", x)` → `new Exception($"…{x}…")`。
// 10) Go 的包级**未导出**函数 → `Ls` 的 `internal static` 成员（PascalCase，不加 `_`）；包级未导出 var/const →
//     `_camelCase`（先例 Constants.cs 的 `_moduleSpecifierResolutionLimit`）。Go 的包级**导出**常量/var →
//     `public const` / `public static readonly`。
// 11) `sync.OnceValue` → `Lazy<T>`；`strings.Builder` → `StringBuilder`；`slices.Contains` → `Array.IndexOf(...) >= 0`。
// 12) `file.Text()` 返回 C# string（UTF-16 单元）；`text[i]` 是 `char`（Go 的 `byte`）⇒ 遍历一律 `foreach (var ch in text)`。
//     `unicode.IsSpace(r)` → `char.IsWhiteSpace(ch)`。
// 13) 深递归：`GetCompletionDataWorker`/`GetCompletionEntryDisplayNameForSymbol` 等沿 AST 递归（`ForEachChild`），
//     入口须跑在 64 MiB 大栈线程上（本仓既定做法）。
// 14) Go 的 `for i, x := range slice`（i 为下标）→ `for (var i = 0; i < xs.Length; i++)`；Go 的 `len(xs)` → `xs.Length`。
// 15) 结构体一律落成 `internal sealed class`（Go 用指针传递；值语义的 `jsxInitializer` 用 struct 更贴切，但 Go 里
//     它总是以值拷贝/取地址混用，统一用 class 以免 C# 值拷贝语义差异）。
// 16) ⚠ **同名类型冲突**：Go 的 `lsproto.CompletionItem`/`lsproto.CompletionList` 与本文件声明的本地类型
//     `CompletionItem`/`CompletionList` 同名。C# 里**同命名空间的类型优先于 `using` 导入的类型** ⇒ 本文件里
//     裸写 `CompletionItem` 是本地类型，协议类型必须显式写 `LsProto.CompletionItem`（先例 Ls/Symbols.cs 用 `LsProto.` 前缀）。
// 17) ⚠ Go 的 `codeactions.go:40` 已有一个**导出**的 `CodeAction`（G7 落成 `TypeScript.Ls.CodeAction`），与本文件里
//     未导出的 `codeAction` 在 C# 里撞名 ⇒ 本文件的改名 `CompletionCodeAction`。
// 18) Go 的 `snippetEmitTextWriter` 靠**嵌入** `*printer.ChangeTrackerWriter` 继承其 `EmitTextWriter` 实现并覆写 6 个
//     Write* 方法；本仓 `Printer.ChangeTrackerWriter` 是 **sealed** ⇒ 改为「实现 `Printer.EmitTextWriter` 接口 +
//     组合一个 ChangeTrackerWriter」，6 个 escaping 方法自己实现，其余成员逐一转发（行为等价）。因此
//     `SnippetPrinter.PrintUnescapedNode` 里 `p.writer.String()` 而非 `ToString()`。
// 19) Go 的 `getCompletionsAtPosition`（未导出）与导出的 `GetCompletionsAtPosition` 在 C# 里只差大小写 ⇒
//     未导出者命名 `GetCompletionsAtPositionWorker`（先例 `ProvideDefinitionWorker`/`ProvideImplementationsWorker`）；
//     导出的 `GetCompletionsAtPosition` 因返回 internal 的 `CompletionList` 只能声成 `internal`（否则 CS0050）。
// 20) `core.TextChange` 的 C# 构造是 `(TextRange, string)`（Go 的字面量字段名序相反）；`core.ApplyBulkEdits` 收
//     `ReadOnlySpan<TextChange>` ⇒ 传 `List<TextChange>.ToArray()`。
using System;
using System.Collections.Generic;
using System.Text;
using TypeScript.Ast;
using TypeScript.Collections;
using TypeScript.Compiler;
using TypeScript.Core;
using TypeScript.LsAutoImport;
using TypeScript.LsConv;
using TypeScript.LsProto;
using TypeScript.LsUtil;
using TypeScript.SpanMap;
using static TypeScript.Ast.Ast;
using static TypeScript.Collections.Collections;
using static TypeScript.Core.Core;
using static TypeScript.Format.Format;
using static TypeScript.Ls.Ls;
using static TypeScript.LsProto.LsProto;
using Kind = TypeScript.Ast.Kind;
using Program = TypeScript.Compiler.Program;
using Range = TypeScript.LsProto.Range;

namespace TypeScript.Ls;

public static partial class Ls
{
    /// <summary>Go: <c>var CompletionTriggerCharacters</c>（completions.go:259）。</summary>
    public static readonly string[] CompletionTriggerCharacters = [".", "\"", "'", "`", "/", "@", "<", "#", " ", "*"];

    /// <summary>Go: <c>var allCommitCharacters</c>（completions.go:262）。</summary>
    internal static readonly string[] AllCommitCharacters = [".", ",", ";"];

    /// <summary>Go: <c>var noCommaCommitCharacters</c>（completions.go:265）。</summary>
    internal static readonly string[] NoCommaCommitCharacters = [".", ";"];

    /// <summary>Go: <c>var emptyCommitCharacters</c>（completions.go:267）。</summary>
    internal static readonly string[] EmptyCommitCharacters = [];

    // ── SortText（completions.go:271-293）──────────────────────────────────────────────
    public const string SortTextLocalDeclarationPriority = "10";
    public const string SortTextLocationPriority = "11";
    public const string SortTextOptionalMember = "12";
    public const string SortTextMemberDeclaredBySpreadAssignment = "13";
    public const string SortTextSuggestedClassMembers = "14";
    public const string SortTextGlobalsOrKeywords = "15";
    public const string SortTextAutoImportSuggestions = "16";
    public const string SortTextClassMemberSnippets = "17";
    public const string SortTextJavascriptIdentifiers = "18";

    /// <summary>Go: <c>func DeprecateSortText(original SortText) SortText</c>（completions.go:283-285）。</summary>
    public static string DeprecateSortText(string original) => "z" + original;

    /// <summary>Go: <c>func ObjectLiteralPropertySortText(presetSortText SortText, symbolDisplayName string) SortText</c>（completions.go:287-289）。</summary>
    public static string ObjectLiteralPropertySortText(string presetSortText, string symbolDisplayName) =>
        presetSortText + "\0" + symbolDisplayName + "\0";

    /// <summary>Go: <c>func SortBelow(original SortText) SortText</c>（completions.go:291-293）。</summary>
    public static string SortBelow(string original) => original + "1";

    // ── completionSource（completions.go:352-367 / 5474-5487，Go 里两套同名值的常量）──────
    internal const string _completionSourceThisProperty = "ThisProperty/";
    internal const string _completionSourceClassMemberSnippet = "ClassMemberSnippet/";
    internal const string _completionSourceTypeOnlyAlias = "TypeOnlyAlias/";
    internal const string _completionSourceObjectLiteralMethodSnippet = "ObjectLiteralMethodSnippet/";
    internal const string _completionSourceSwitchCases = "SwitchCases/";
    internal const string _completionSourceObjectLiteralMemberWithComma = "ObjectLiteralMemberWithComma/";

    public const string SourceThisProperty = "ThisProperty/";
    public const string SourceClassMemberSnippet = "ClassMemberSnippet/";
    public const string SourceTypeOnlyAlias = "TypeOnlyAlias/";
    public const string SourceObjectLiteralMethodSnippet = "ObjectLiteralMethodSnippet/";
    public const string SourceSwitchCases = "SwitchCases/";
    public const string SourceObjectLiteralMemberWithComma = "ObjectLiteralMemberWithComma/";

    /// <summary>Go: <c>func escapeSnippetText(text string) string</c>（completions.go:3734-3736）。</summary>
    internal static string EscapeSnippetText(string text) => text.Replace("$", "\\$", StringComparison.Ordinal);

    /// <summary>Go: <c>func clientSupportsItemLabelDetails(ctx context.Context) bool</c>（completions.go:5423-5425）。</summary>
    internal static bool ClientSupportsItemLabelDetails(Context ctx) =>
        GetClientCapabilities().TextDocument.Completion.CompletionItem.LabelDetailsSupport;

    /// <summary>Go: <c>func clientSupportsItemSnippet(ctx context.Context) bool</c>（completions.go:5427-5429）。</summary>
    internal static bool ClientSupportsItemSnippet(Context ctx) =>
        GetClientCapabilities().TextDocument.Completion.CompletionItem.SnippetSupport;

    /// <summary>Go: <c>func clientSupportsItemCommitCharacters(ctx context.Context) bool</c>（completions.go:5431-5433）。</summary>
    internal static bool ClientSupportsItemCommitCharacters(Context ctx) =>
        GetClientCapabilities().TextDocument.Completion.CompletionItem.CommitCharactersSupport;

    /// <summary>Go: <c>func clientSupportsItemInsertReplace(ctx context.Context) bool</c>（completions.go:5435-5437）。</summary>
    internal static bool ClientSupportsItemInsertReplace(Context ctx) =>
        GetClientCapabilities().TextDocument.Completion.CompletionItem.InsertReplaceSupport;

    /// <summary>Go: <c>func clientSupportsDefaultCommitCharacters(ctx context.Context) bool</c>（completions.go:5439-5441）。</summary>
    internal static bool ClientSupportsDefaultCommitCharacters(Context ctx) =>
        Array.IndexOf(GetClientCapabilities().TextDocument.Completion.CompletionList.ItemDefaults ?? [], "commitCharacters") >= 0;

    /// <summary>Go: <c>func clientSupportsDefaultEditRange(ctx context.Context) bool</c>（completions.go:5443-5445）。</summary>
    internal static bool ClientSupportsDefaultEditRange(Context ctx) =>
        Array.IndexOf(GetClientCapabilities().TextDocument.Completion.CompletionList.ItemDefaults ?? [], "editRange") >= 0;

    /// <summary>Go: <c>func ensureItemData(file *ast.SourceFile, pos int, list *lsproto.CompletionList) *lsproto.CompletionList</c>（completions.go:122-137）。</summary>
    internal static LsProto.CompletionList? EnsureItemData(SourceFile file, int pos, LsProto.CompletionList? list)
    {
        if (list is null)
        {
            return null;
        }

        foreach (var item in list.Items ?? [])
        {
            if (item is not null && item.Data is null)
            {
                item.Data = new CompletionItemData
                {
                    FileName = file.OriginalFileName(),
                    Position = pos,
                    SupplementalFileIndex = SupplementalFileIndex(file),
                    Name = item.Label,
                };
            }
        }

        return list;
    }
}

/// <summary>Go: <c>type CompletionItem struct</c>（completions.go:110-113）。</summary>
internal sealed class CompletionItem
{
    /// <summary>Go 的嵌入字段 <c>*lsproto.CompletionItem</c> → 组合字段（见文件头第 3 条）。</summary>
    internal LsProto.CompletionItem _completionItem = new();

    /// <summary>Go: <c>Symbol *ast.Symbol</c>：non-nil for symbol completions when IncludeSymbols is set; nil otherwise。</summary>
    internal Ast.Symbol? Symbol;
}

/// <summary>Go: <c>type CompletionList struct</c>（completions.go:115-120）。</summary>
internal sealed class CompletionList
{
    internal bool IsIncomplete;
    internal CompletionItemDefaults? ItemDefaults;
    internal CompletionItemApplyKinds? ApplyKind;
    internal CompletionItem[] Items = [];

    /// <summary>Go: <c>func (l *CompletionList) toLSP() *lsproto.CompletionList</c>（completions.go:384-400）。
    /// Go 里 nil 接收者返回 nil（completions.go:385-387）⇒ 调用点写 `list?.ToLsp()`。</summary>
    internal LsProto.CompletionList? ToLsp()
    {
        var items = new List<LsProto.CompletionItem>(Items.Length);
        foreach (var entry in Items)
        {
            if (entry is not null && entry._completionItem is not null)
            {
                items.Add(entry._completionItem);
            }
        }

        return new LsProto.CompletionList
        {
            IsIncomplete = IsIncomplete,
            ItemDefaults = ItemDefaults,
            ApplyKind = ApplyKind,
            Items = items.ToArray(),
        };
    }
}

/// <summary>Go: <c>type completionData = any</c>（completions.go:164）：5 个具体类型的公共基类（见文件头第 2 条）。</summary>
internal abstract class CompletionData;

/// <summary>Go: <c>type completionDataData struct</c>（completions.go:166-193）。</summary>
internal sealed class CompletionDataData : CompletionData
{
    internal Ast.Symbol[] _symbols = [];
    internal FixAndExport[] _autoImports = [];
    internal CompletionKind _completionKind;
    internal bool _isInSnippetScope;
    /// <summary>Note that the presence of this alone doesn't mean that we need a conversion. Only do that if the completion is not an ordinary identifier.</summary>
    internal Node? _propertyAccessToConvert;
    internal bool _isNewIdentifierLocation;
    internal Node _location = null!;
    internal KeywordCompletionFilters _keywordFilters;
    internal object[] _literals = [];
    internal Dictionary<int, SymbolOriginInfo> _symbolToOriginInfoMap = [];
    internal Dictionary<SymbolId, string> _symbolToSortTextMap = [];
    internal Ast.Symbol? _recommendedCompletion;
    internal Node? _previousToken;
    internal Node? _contextToken;
    internal JsxInitializer _jsxInitializer = new();
    internal bool _insideJSDocTagTypeExpression;
    internal bool _isTypeOnlyLocation;
    /// <summary>In JSX tag name and attribute names, identifiers like "my-tag" or "aria-name" is valid identifier.</summary>
    internal bool _isJsxIdentifierExpected;
    internal bool _isRightOfOpenTag;
    internal bool _isRightOfDotOrQuestionDot;
    internal ImportStatementCompletionInfo? _importStatementCompletion;
    internal bool _hasUnresolvedAutoImports;
    internal string[]? _defaultCommitCharacters;
}

/// <summary>Go: <c>type completionDataKeyword struct</c>（completions.go:195-198）。</summary>
internal sealed class CompletionDataKeyword : CompletionData
{
    internal CompletionItem[] _keywordCompletions = [];
    internal bool _isNewIdentifierLocation;
}

/// <summary>Go: <c>type completionDataJSDocTagName struct{}</c>（completions.go:200）。</summary>
internal sealed class CompletionDataJsDocTagName : CompletionData;

/// <summary>Go: <c>type completionDataJSDocTag struct{}</c>（completions.go:202）。</summary>
internal sealed class CompletionDataJsDocTag : CompletionData;

/// <summary>Go: <c>type completionDataJSDocParameterName struct</c>（completions.go:204-206）。</summary>
internal sealed class CompletionDataJsDocParameterName : CompletionData
{
    internal JSDocParameterOrPropertyTag _tag = null!;
}

/// <summary>Go: <c>type importStatementCompletionInfo struct</c>（completions.go:208-217）。</summary>
internal sealed class ImportStatementCompletionInfo
{
    internal bool _isKeywordOnlyCompletion;
    internal Kind _keywordCompletion; // TokenKind
    internal bool _isNewIdentifierLocation;
    internal bool _isTopLevelTypeOnly;
    internal bool _couldBeTypeOnlyImportSpecifier;
    internal Range? _replacementSpan;
}

/// <summary>Go: <c>type jsxInitializer struct</c>（completions.go:219-222）。
/// If we're after the `=` sign but no identifier has been typed yet, <c>isInitializer</c> will be true but <c>initializer</c> will be nil。</summary>
internal sealed class JsxInitializer
{
    internal bool _isInitializer;
    internal Identifier? _initializer;
}

/// <summary>Go: <c>type KeywordCompletionFilters int</c>（completions.go:224-237）。</summary>
internal enum KeywordCompletionFilters
{
    None = 0,
    All = 1,
    ClassElementKeywords = 2,
    InterfaceElementKeywords = 3,
    ConstructorParameterKeywords = 4,
    FunctionLikeBodyKeywords = 5,
    TypeAssertionKeywords = 6,
    TypeKeywords = 7,
    TypeKeyword = 8,
    Last = TypeKeyword,
}

/// <summary>Go: <c>type CompletionKind int</c>（completions.go:248-257）。</summary>
internal enum CompletionKind
{
    None = 0,
    ObjectPropertyDeclaration = 1,
    Global = 2,
    PropertyAccess = 3,
    MemberLike = 4,
    String = 5,
}

/// <summary>Go: <c>type symbolOriginInfoKind int</c>（completions.go:295-306）：`1 &lt;&lt; iota` 的位常量。</summary>
[Flags]
internal enum SymbolOriginInfoKind
{
    ThisType = 1 << 0,
    SymbolMember = 1 << 1,
    Promise = 1 << 2,
    Nullable = 1 << 3,
    TypeOnlyAlias = 1 << 4,
    ObjectLiteralMethod = 1 << 5,
    Ignore = 1 << 6,
    ComputedPropertyName = 1 << 7,
}

/// <summary>Go: <c>type symbolOriginInfo struct</c>（completions.go:308-314）。</summary>
internal sealed class SymbolOriginInfo
{
    internal SymbolOriginInfoKind _kind;
    internal bool _isDefaultExport;
    internal bool _isFromPackageJson;
    internal string? _fileName;
    /// <summary>Go: <c>data any</c>（completions.go:313）→ C# `object?`（4 个具体类型之一）。</summary>
    internal object? _data;

    /// <summary>Go: <c>func (origin *symbolOriginInfo) symbolName() string</c>（completions.go:316-323）。</summary>
    internal string SymbolName()
    {
        switch (_data)
        {
            case SymbolOriginInfoComputedPropertyName computed:
                return computed._symbolName;
            default:
                throw new Exception($"symbolOriginInfo: unknown data type for symbolName(): {_data?.GetType().Name}");
        }
    }

    /// <summary>Go: <c>func (s *symbolOriginInfo) asObjectLiteralMethod() *symbolOriginInfoObjectLiteralMethod</c>（completions.go:331-333）。</summary>
    internal SymbolOriginInfoObjectLiteralMethod AsObjectLiteralMethod()
    {
        return (SymbolOriginInfoObjectLiteralMethod)_data!;
    }
}

/// <summary>Go: <c>type symbolOriginInfoObjectLiteralMethod struct</c>（completions.go:325-329）。</summary>
internal sealed class SymbolOriginInfoObjectLiteralMethod
{
    internal string _insertText = "";
    internal CompletionItemLabelDetails? _labelDetails;
    internal bool _isSnippet;
}

/// <summary>Go: <c>type symbolOriginInfoTypeOnlyAlias struct</c>（completions.go:335-337）。</summary>
internal sealed class SymbolOriginInfoTypeOnlyAlias
{
    internal Node _declaration = null!;
}

/// <summary>Go: <c>type symbolOriginInfoComputedPropertyName struct</c>（completions.go:339-341）。</summary>
internal sealed class SymbolOriginInfoComputedPropertyName
{
    internal string _symbolName = "";
}

/// <summary>Go: <c>type globalsSearch int</c>（completions.go:376-382）。</summary>
internal enum GlobalsSearch
{
    Continue = 0,
    Success = 1,
    Fail = 2,
}

/// <summary>Go: <c>type memberCompletionEntry struct</c>（completions.go:2467-2472）。</summary>
internal sealed class MemberCompletionEntry
{
    internal string _insertText = "";
    internal string _filterText = "";
    internal bool _isSnippet;
    internal TextEdit[] _additionalTextEdits = [];
}

/// <summary>Go: <c>type objectLiteralMethodSymbol struct</c>（completions.go:2585-2588）。</summary>
internal sealed class ObjectLiteralMethodSymbol
{
    internal Ast.Symbol _symbol = null!;
    internal SymbolOriginInfo _origin = null!;
}

/// <summary>Go: <c>type presentMemberModifiers struct</c>（completions.go:2760-2764）。</summary>
internal sealed class PresentMemberModifiers
{
    internal ModifierFlags _modifiers;
    internal Node[] _decorators = [];
    internal Range? _eraseRange;
}

/// <summary>Go: <c>type argumentInfoForCompletions struct</c>（completions.go:5447-5451）。</summary>
internal sealed class ArgumentInfoForCompletions
{
    internal Node _invocation = null!;
    internal int _argumentIndex;
    internal int _argumentCount;
}

/// <summary>Go: <c>type detailsData struct</c>（completions.go:5609-5614）。</summary>
internal sealed class DetailsData
{
    internal SymbolDetails? _symbol;
    internal CompletionData? _request;
    /// <summary>Go: <c>literal *literalValue</c>（=`*any`）→ C# `object?`（已装箱）。</summary>
    internal object? _literal;
    internal object? _cases;
}

/// <summary>Go: <c>type symbolDetails struct</c>（completions.go:5616-5624）。</summary>
internal sealed class SymbolDetails
{
    internal Ast.Symbol _symbol = null!;
    internal Node _location = null!;
    internal SymbolOriginInfo? _origin;
    internal Node? _previousToken;
    internal Node? _contextToken;
    internal JsxInitializer _jsxInitializer = new();
    internal bool _isTypeOnlyLocation;
}

/// <summary>Go: <c>type codeAction struct</c>（completions.go:5727-5732）。
/// ⚠ Go 的 `codeactions.go:40` 已有一个**导出**的 `CodeAction`（G7 已落成 `TypeScript.Ls.CodeAction`）⇒
/// 本类型改名 `CompletionCodeAction` 以避开 CS0101（Go 里二者靠大小写区分，C# 不能）。</summary>
internal sealed class CompletionCodeAction
{
    /// <summary>Description of the code action to display in the UI of the editor</summary>
    internal string _description = "";

    /// <summary>Text changes to apply to each file as part of the code action</summary>
    internal TextEdit[] _changes = [];
}

/// <summary>Go: <c>type snippetPrinter struct</c>（completions.go:6785-6791）。</summary>
internal sealed class SnippetPrinter
{
    internal Printer.ChangeTrackerWriter _baseWriter = null!;
    internal Printer.EmitContext _emitContext = null!;
    internal Printer.Printer _printer = null!;
    internal SnippetEmitTextWriter _writer = null!;
    internal NodeFactory _factory = null!;

    /// <summary>Go: <c>func (p *snippetPrinter) printNode(node *ast.Node) string</c>（completions.go:6794-6800）。</summary>
    internal string PrintNode(Node node)
    {
        var unescaped = PrintUnescapedNode(node);
        if (_writer._escapes.Count > 0)
        {
            return ApplyBulkEdits(unescaped, _writer._escapes.ToArray());
        }

        return unescaped;
    }

    /// <summary>Go: <c>func (p *snippetPrinter) printUnescapedNode(node *ast.Node) string</c>（completions.go:6802-6807）。</summary>
    internal string PrintUnescapedNode(Node node)
    {
        _writer._escapes = [];
        _writer.Clear();
        _printer.Write(node, null /*sourceFile*/, _writer, null /*sourceMapGenerator*/);
        return _writer.String();
    }

    /// <summary>Go: <c>func (p *snippetPrinter) printAndFormatNode(ctx, node, sourceFile) string</c>（completions.go:6809-6811）。</summary>
    internal string PrintAndFormatNode(Context ctx, Node node, SourceFile sourceFile) =>
        PrintAndFormatNodeWithSettings(ctx, node, sourceFile, GetFormatCodeSettingsFromContext(ctx));

    /// <summary>Go: <c>func (p *snippetPrinter) printAndFormatNodeWithSettings(...)</c>（completions.go:6813-6834）。</summary>
    internal string PrintAndFormatNodeWithSettings(Context ctx, Node node, SourceFile sourceFile, FormatCodeSettings formatOptions)
    {
        var text = PrintUnescapedNode(node);
        var nodeWithPos = _baseWriter.AssignPositionsToNode(node, _factory);
        var syntheticFile = TypeScript.Printer.Printer.CreateSyntheticSourceFile(_factory, nodeWithPos, text, sourceFile.ParseOptions());
        ctx = WithFormatCodeSettings(ctx, formatOptions, formatOptions.NewLineCharacter);
        var changes = FormatNodeGivenIndentation(
            ctx,
            nodeWithPos,
            syntheticFile,
            sourceFile.LanguageVariant,
            0 /*initialIndentation*/,
            0 /*delta*/);

        var allChanges = changes;
        if (_writer._escapes.Count > 0)
        {
            allChanges = new TextChange[changes.Length + _writer._escapes.Count];
            Array.Copy(changes, allChanges, changes.Length);
            Array.Copy(_writer._escapes.ToArray(), 0, allChanges, changes.Length, _writer._escapes.Count);
            Array.Sort(allChanges, static (a, b) => CompareTextRanges(a.Range, b.Range));
        }

        return ApplyBulkEdits(syntheticFile.Text(), allChanges);
    }
}

/// <summary>Go: <c>type snippetEmitTextWriter struct</c>（completions.go:6855-6858）。
/// Override base writer methods to perform snippet escaping。
/// ⚠ 本仓的 <c>Printer.ChangeTrackerWriter</c> 是 **sealed** 且 Go 的嵌入是"接口实现继承"⇒ C# 改为
/// **实现 <see cref="Printer.EmitTextWriter"/> 接口 + 组合一个 ChangeTrackerWriter**，把 6 个 escaping 方法拦下来，
/// 其余成员逐一转发（语义与 Go 的嵌入一致：除这 6 个方法外的行为完全等于底层 writer）。</summary>
internal sealed class SnippetEmitTextWriter : Printer.EmitTextWriter
{
    internal Printer.ChangeTrackerWriter _changeTrackerWriter = null!;
    internal List<TextChange> _escapes = [];

    public void Write(string s) => EscapingWrite(s, () => _changeTrackerWriter.Write(s));
    public void WriteComment(string text) => EscapingWrite(text, () => _changeTrackerWriter.WriteComment(text));
    public void WriteStringLiteral(string text) => EscapingWrite(text, () => _changeTrackerWriter.WriteStringLiteral(text));
    public void WriteParameter(string text) => EscapingWrite(text, () => _changeTrackerWriter.WriteParameter(text));
    public void WriteProperty(string text) => EscapingWrite(text, () => _changeTrackerWriter.WriteProperty(text));
    public void WriteSymbol(string text, Ast.Symbol? symbol) => EscapingWrite(text, () => _changeTrackerWriter.WriteSymbol(text, symbol));

    public void WriteTrailingSemicolon(string text) => _changeTrackerWriter.WriteTrailingSemicolon(text);
    public void WriteKeyword(string text) => _changeTrackerWriter.WriteKeyword(text);
    public void WriteOperator(string text) => _changeTrackerWriter.WriteOperator(text);
    public void WritePunctuation(string text) => _changeTrackerWriter.WritePunctuation(text);
    public void WriteSpace(string text) => _changeTrackerWriter.WriteSpace(text);
    public void WriteLine() => _changeTrackerWriter.WriteLine();
    public void WriteLineForce(bool force) => _changeTrackerWriter.WriteLineForce(force);
    public void IncreaseIndent() => _changeTrackerWriter.IncreaseIndent();
    public void DecreaseIndent() => _changeTrackerWriter.DecreaseIndent();
    public void Clear() => _changeTrackerWriter.Clear();
    public string String() => _changeTrackerWriter.String();
    public void RawWrite(string s) => _changeTrackerWriter.RawWrite(s);
    public void WriteLiteral(string s) => _changeTrackerWriter.WriteLiteral(s);
    public int GetTextPos() => _changeTrackerWriter.GetTextPos();
    public int GetLine() => _changeTrackerWriter.GetLine();
    public UTF16Offset GetColumn() => _changeTrackerWriter.GetColumn();
    public int GetIndent() => _changeTrackerWriter.GetIndent();
    public bool IsAtStartOfLine() => _changeTrackerWriter.IsAtStartOfLine();
    public bool HasTrailingComment() => _changeTrackerWriter.HasTrailingComment();
    public bool HasTrailingWhitespace() => _changeTrackerWriter.HasTrailingWhitespace();

    /// <summary>The formatter/scanner will have issues with snippet-escaped text, so instead of writing the escaped text
    /// directly to the writer, generate a set of changes that can be applied to the unescaped text to escape it post-formatting。</summary>
    private void EscapingWrite(string s, Action write)
    {
        var escaped = EscapeSnippetText(s);
        if (escaped != s)
        {
            var start = _changeTrackerWriter.GetTextPos();
            write();
            var end = _changeTrackerWriter.GetTextPos();
            _escapes.Add(new TextChange(NewTextRange(start, end), escaped));
        }
        else
        {
            write();
        }
    }
}

public sealed partial class LanguageService
{
    /// <summary>Go: <c>func (l *LanguageService) ProvideCompletion(ctx, documentURI, LSPPosition, context) (lsproto.CompletionResponse, error)</c>（completions.go:37-76）。</summary>
    public (CompletionItemsOrListOrNull Response, Exception? Error) ProvideCompletion(
        Context ctx,
        DocumentUri documentURI,
        Position lspPosition,
        CompletionContext? context)
    {
        var (program, file) = GetProgramAndFile(documentURI);
        string? triggerCharacter = null;
        if (context is not null)
        {
            triggerCharacter = context.TriggerCharacter;
        }

        ctx = WithFormatCodeSettings(ctx, FormatOptions(), FormatOptions().NewLineCharacter);
        var positions = _converters!.FromLSPPositionForSourceFile(file, lspPosition, Feature.Completion);
        if (positions.Length == 0 || !positions[0].Fidelity.IsExact())
        {
            // In a content-mapped file the cursor is outside a verbatim span, so any completion committed here
            // could not be applied to the original text. Offer nothing rather than edits at a bogus location.
            return (new CompletionItemsOrListOrNull(), null);
        }

        file = positions[0].Script;
        var position = (int)positions[0].Position;
        var (completionListInternal, err) = GetCompletionsAtPositionWorker(
            ctx,
            file,
            position,
            triggerCharacter,
            false /*includeSymbols*/);
        if (err is not null)
        {
            return (new CompletionItemsOrListOrNull(), err);
        }

        var completionList = EnsureItemData(file, position, completionListInternal?.ToLsp());
        if (file.SpanMap() is not null)
        {
            FilterContentMappedAutoImports(ctx, program!, file, completionList!);
        }

        return (new CompletionItemsOrListOrNull { List = completionList }, null);
    }

    /// <summary>Go: <c>func (l *LanguageService) filterContentMappedAutoImports(ctx, program, file, list)</c>（completions.go:78-104）。
    /// filterContentMappedAutoImports eagerly resolves auto-import edits for a content-mapped file and drops any
    /// completion whose import edit cannot be placed entirely within verbatim spans。</summary>
    internal void FilterContentMappedAutoImports(Context ctx, Program program, SourceFile file, LsProto.CompletionList list)
    {
        var filtered = new List<LsProto.CompletionItem>(list.Items?.Length ?? 0);
        foreach (var item in list.Items ?? [])
        {
            if (item is null || item.Data is null || item.Data.AutoImport is null)
            {
                if (item is not null)
                {
                    filtered.Add(item);
                }

                continue;
            }

            var (edits, description, safe) = new Fix { AutoImportFix = item.Data.AutoImport }.Edits(
                ctx,
                file,
                program.Options(),
                FormatOptions(),
                _converters!,
                UserPreferences());
            if (!safe)
            {
                continue;
            }

            item.AdditionalTextEdits = edits;
            item.Detail = description;
            filtered.Add(item);
        }

        list.Items = filtered.ToArray();
    }

    /// <summary>Go: <c>func (l *LanguageService) GetCompletionsAtPosition(ctx, file, position, triggerCharacter, includeSymbols) (*CompletionList, error)</c>（completions.go:106-108）。
    /// ⚠ Go 这里是**导出**方法（`api/session.go:5005` 调用），但返回类型 `CompletionList` 在 C# 里是 internal
    /// ⇒ C# 只能是 `internal`（同程序集的 `api` 切片可直接调用；`public` 会触发 CS0050）。</summary>
    internal (CompletionList? List, Exception? Error) GetCompletionsAtPosition(
        Context ctx,
        SourceFile file,
        int position,
        string? triggerCharacter,
        bool includeSymbols)
    {
        return GetCompletionsAtPositionWorker(ctx, file, position, triggerCharacter, includeSymbols);
    }

    /// <summary>Go: <c>func (l *LanguageService) getCompletionsAtPosition(ctx, file, position, triggerCharacter, includeSymbols) (*CompletionList, error)</c>（completions.go:402-526）。
    /// ⚠ Go 的 `defer done()` → C# `try/finally`（Go 的导出包装 `GetCompletionsAtPosition` 与本方法同名只差大小写，C# 加 `Worker` 后缀，先例 `ProvideDefinitionWorker`）。</summary>
    internal (CompletionList? List, Exception? Error) GetCompletionsAtPositionWorker(
        Context ctx,
        SourceFile file,
        int position,
        string? triggerCharacter,
        bool includeSymbols)
    {
        var (_, previousToken) = GetRelevantTokens(position, file);
        if (triggerCharacter is not null
            && !IsInString(file, position, previousToken)
            && !IsValidTrigger(file, triggerCharacter, previousToken, position))
        {
            return (null, null);
        }

        if (triggerCharacter is not null && triggerCharacter == " ")
        {
            // `isValidTrigger` ensures we are at `import |`
            if (UserPreferences().IncludeCompletionsForImportStatements.IsTrue())
            {
                return (new CompletionList { IsIncomplete = true }, null);
            }

            return (null, null);
        }

        if (GetJSDocSnippetCompletion(ctx, file, position) is { } jsDocSnippetCompletion)
        {
            return (jsDocSnippetCompletion, null);
        }

        var compilerOptions = GetProgram()!.Options();

        // !!! see if incomplete completion list and continue or clean

        var (checker, done) = GetProgram()!.GetTypeCheckerForFile(ctx, file);
        try
        {
            var stringCompletions = GetStringLiteralCompletions(
                ctx,
                file,
                position,
                previousToken,
                checker,
                compilerOptions,
                includeSymbols);
            if (stringCompletions is not null)
            {
                return (stringCompletions, null);
            }

            if (previousToken is not null
                && (previousToken.Kind == Kind.BreakKeyword
                    || previousToken.Kind == Kind.ContinueKeyword
                    || previousToken.Kind == Kind.Identifier)
                && IsBreakOrContinueStatement(previousToken.Parent))
            {
                return (
                    GetLabelCompletionsAtPosition(
                        ctx,
                        previousToken.Parent!,
                        file,
                        position,
                        GetOptionalReplacementSpan(previousToken, file)),
                    null);
            }

            var preferences = UserPreferences();
            var (data, err) = GetCompletionDataWorker(ctx, checker, file, position, preferences, false /*forItemResolve*/);
            if (err is not null)
            {
                return (null, err);
            }

            if (data is null)
            {
                return (null, null);
            }

            switch (data)
            {
                case CompletionDataData dataData:
                {
                    var optionalReplacementSpan = GetOptionalReplacementSpan(dataData._location, file);
                    var (response, err2) = CompletionInfoFromData(
                        ctx,
                        checker,
                        file,
                        compilerOptions,
                        dataData,
                        position,
                        optionalReplacementSpan,
                        includeSymbols);
                    if (err2 is not null)
                    {
                        return (null, err2);
                    }

                    return (response, null);
                }

                case CompletionDataKeyword dataKeyword:
                {
                    var optionalReplacementSpan = GetOptionalReplacementSpan(previousToken!, file);
                    return (
                        SpecificKeywordCompletionInfo(
                            ctx,
                            position,
                            file,
                            dataKeyword._keywordCompletions,
                            dataKeyword._isNewIdentifierLocation,
                            optionalReplacementSpan),
                        null);
                }

                case CompletionDataJsDocTagName:
                {
                    // If the current position is a jsDoc tag name, only tag names should be provided for completion
                    CompletionItem[] items = [.. GetJSDocTagNameCompletions(), .. GetJSDocParameterCompletions(
                        ctx,
                        file,
                        position,
                        checker,
                        compilerOptions,
                        preferences,
                        true /*tagNameOnly*/)];
                    return (JsDocCompletionInfo(ctx, position, file, items), null);
                }

                case CompletionDataJsDocTag:
                {
                    // If the current position is a jsDoc tag, only tags should be provided for completion
                    CompletionItem[] items = [.. GetJSDocTagCompletions(), .. GetJSDocParameterCompletions(
                        ctx,
                        file,
                        position,
                        checker,
                        compilerOptions,
                        preferences,
                        false /*tagNameOnly*/)];
                    return (JsDocCompletionInfo(ctx, position, file, items), null);
                }

                case CompletionDataJsDocParameterName jsDocParameterName:
                    return (JsDocCompletionInfo(ctx, position, file, GetJSDocParameterNameCompletions(jsDocParameterName._tag)), null);

                default:
                    throw new Exception($"getCompletionData() returned unexpected type: {data.GetType().Name}");
            }
        }
        finally
        {
            done();
        }
    }
}

// ════════════════════════════════════════════════════════════════════════════════════════════════════
// G8 分节 A（续）：completions.go:239-247 `keywordFiltersFromSyntaxKind`
//                 + completions.go:528-1801 `(l *LanguageService) getCompletionData`
//
// 本段与 Go 的偏离（逐条；文件头的通用偏离同样适用）：
// 1) 本文件的 using 区在文件头（file-scoped namespace ⇒ 不能在其后追加 using 别名），所以 Go 的
//    `*checker.Checker` 形参写全限定名 `TypeScript.Checker.Checker`（同一类型；裸写 `Checker` 会被
//    命名空间 `TypeScript.Checker` 遮蔽）。
// 2) 类型名 `Ast`/`Ls` 同样被同名命名空间遮蔽（C# 名字解析：外层命名空间的成员优先于 using 导入）⇒
//    与 `Ls`（CallHierarchy.cs 的 `IsVariableLike`）同名的那一个写全限定 `TypeScript.Ast.Ast.IsVariableLike`；
//    其余包级函数沿用 `using static` 裸名。
// 3) Go 的可被闭包追加的切片 `symbols` → C# `List<Ast.Symbol>`：局部函数捕获的是同一个变量，
//    `len(symbols)`→`symbols.Count`、下标语义与 Go 逐字一致；返回前 `ToArray()` 落进 `_symbols`。
// 4) Go 的 `panic("…")` → `throw new Exception("…")`（本文件既有代码已是此约定）。
// 5) Go 的闭包 → C# **局部函数**（PascalCase），捕获同一批状态（`symbols` 用可变局部变量 + 显式增删）。
// 6) Go 的 `switch … fallthrough`（BinaryExpression → JsxSelfClosingElement/…）→ C# `goto case`（等价）。
// 7) Go 的联合类型别名（`*ast.IdentifierNode` 等 `= Node`）在本文件里只有 `jsxInitializer.initializer`
//    需要贴住既有字段类型 `Identifier?` ⇒ 显式转换 `(Identifier)`（Go 侧是同一类型的直接赋值）。
// 8) 跨分节桩里少数形参/返回的可空标注与 Go 的 nil 语义不符（`IsCompletionListBlocker`/
//    `TryGetObjectLikeCompletionContainer`/`GetScopeNode`/`TryGetContainingJsxElement`/
//    `TryGetFunctionLikeBodyCompletionContainer`/`TryGetConstructorLikeCompletionContainer`/
//    `TryGetObjectTypeDeclarationCompletionContainer`/`ComputeCommitCharactersAndIsNewIdentifier` 的 token 形参、
//    `GetCompletionEntryDisplayNameForSymbol` 的 origin、`GetContextualType`/`GetConstraintOfTypeArgumentProperty`
//    的返回）⇒ 调用点用 `!` / `null!` 保持「可能传 nil / 可能返回 nil」的运行时语义（见交付说明）。
// 9) `file.Text()[lineStart:position]`（Go 的字符串切片，无拷贝）→ C# 按索引遍历 `text[i]`（不切子串）。
// ════════════════════════════════════════════════════════════════════════════════════════════════════

public static partial class Ls
{
    /// <summary>Go: <c>func keywordFiltersFromSyntaxKind(keywordCompletion ast.Kind) KeywordCompletionFilters</c>（completions.go:239-247）。</summary>
    internal static KeywordCompletionFilters KeywordFiltersFromSyntaxKind(Kind keywordCompletion)
    {
        switch (keywordCompletion)
        {
            case Kind.TypeKeyword:
                return KeywordCompletionFilters.TypeKeyword;
            default:
                throw new Exception("Unknown mapping from ast.Kind `" + keywordCompletion.String() + "` to KeywordCompletionFilters");
        }
    }
}

public sealed partial class LanguageService
{
    /// <summary>Go: <c>func (l *LanguageService) getCompletionData(ctx context.Context, typeChecker *checker.Checker,
    /// file *ast.SourceFile, position int, preferences lsutil.UserPreferences, forItemResolve bool) (completionData, error)</c>（completions.go:528-1801）。</summary>
    internal (CompletionData? Data, Exception? Error) GetCompletionDataWorker(
        Context ctx,
        TypeScript.Checker.Checker typeChecker,
        SourceFile file,
        int position,
        UserPreferences preferences,
        bool forItemResolve)
    {
        var inCheckedFile = IsCheckedFile(file, GetProgram()!.Options());

        Node? currentToken = TypeScript.AstNav.AstNav.GetTokenAtPosition(file, position);

        var insideComment = IsInComment(file, position, currentToken);

        var insideJSDocTagTypeExpression = false;
        var insideJsDocImportTag = false;
        var isInSnippetScope = false;
        if (insideComment is not null)
        {
            if (HasDocComment(file, position))
            {
                if (position > 0 && file.Text()[position - 1] == '@')
                {
                    // 当前位置紧挨着 '@' 号，还没有提供 tag 名。给出完整的 tag 名列表。
                    return (new CompletionDataJsDocTagName(), null);
                }
                else
                {
                    // 没有 "@" 请求补全时，要确保请求位置之前没有注释前缀。只允许 "*" 和空白。
                    // 例如：
                    //   /** |c| /*
                    //
                    //   /**
                    //     |c|
                    //    */
                    //
                    //   /**
                    //    * |c|
                    //    */
                    //
                    //   /**
                    //    *         |c|
                    //    */
                    var lineStart = GetLineStartPositionForPosition(position, file);
                    var noCommentPrefix = true;
                    var text = file.Text();
                    for (var i = lineStart; i < position; i++)
                    {
                        var r = text[i];
                        if (!(TypeScript.StringUtil.StringUtil.IsWhiteSpaceSingleLine(r) || r == '*' || r == '/' || r == '(' || r == ')' || r == '|'))
                        {
                            noCommentPrefix = false;
                            break;
                        }
                    }

                    if (noCommentPrefix)
                    {
                        return (new CompletionDataJsDocTag(), null);
                    }
                }
            }

            // 补全应当在某些 JSDoc tag 内部工作。例如：
            //     /** @type {number | string} */
            // 补全应在大括号内工作。
            var jsDocTag = GetJSDocTagAtPosition(currentToken!, position);
            if (jsDocTag is not null)
            {
                if (jsDocTag.TagName()!.Pos() <= position && position <= jsDocTag.TagName()!.End())
                {
                    return (new CompletionDataJsDocTagName(), null);
                }

                if (IsJSDocImportTag(jsDocTag))
                {
                    insideJsDocImportTag = true;
                }
                else
                {
                    var typeExpression = TryGetTypeExpressionFromTag(jsDocTag);
                    if (typeExpression is not null)
                    {
                        currentToken = TypeScript.AstNav.AstNav.GetTokenAtPosition(file, position);
                        if (currentToken is null ||
                            (!IsDeclarationName(currentToken) &&
                                (currentToken.Parent!.Kind != Kind.JSDocPropertyTag ||
                                    currentToken.Parent!.Name() != currentToken)))
                        {
                            // 若在 tag 的类型表达式内部，则当作类型位置使用。
                            insideJSDocTagTypeExpression = IsCurrentlyEditingNode(typeExpression, file, position);
                        }
                    }

                    if (!insideJSDocTagTypeExpression &&
                        IsJSDocParameterTag(jsDocTag) &&
                        (NodeIsMissing(jsDocTag.Name()) || jsDocTag.Name()!.Pos() <= position && position <= jsDocTag.Name()!.End()))
                    {
                        return (new CompletionDataJsDocParameterName
                        {
                            _tag = jsDocTag.AsJSDocParameterOrPropertyTag()!,
                        }, null);
                    }
                }
            }

            if (!insideJSDocTagTypeExpression && !insideJsDocImportTag)
            {
                // 只有当当前位置在 JSDoc tag 表达式内才继续；否则是普通注释或 JSDoc 注释的纯文本部分，
                // 不应提供任何补全。
                return (null, null);
            }
        }

        // 是否提供补全取决于 contextToken，而它由 previousToken 决定。
        // 注意：在文件开头时 `previousToken`（进而 `contextToken`）可能是空。
        var isJSOnlyLocation = !insideJSDocTagTypeExpression && !insideJsDocImportTag && IsSourceFileJS(file);
        var (contextTokenValue, previousTokenValue) = GetRelevantTokens(position, file);
        Node? contextToken = contextTokenValue;
        Node? previousToken = previousTokenValue;

        // 找到请求补全所在的节点。
        // 同时判断我们是要补全该节点的成员，还是 JSX 标签的属性。
        Node? node = currentToken;
        Node? propertyAccessToConvert = null;
        var isRightOfDot = false;
        var isRightOfQuestionDot = false;
        var isRightOfOpenTag = false;
        var isStartingCloseTag = false;
        var jsxInitializer = new JsxInitializer();
        var isJsxIdentifierExpected = false;
        ImportStatementCompletionInfo? importStatementCompletion = null;
        var location = TypeScript.AstNav.AstNav.GetTouchingPropertyName(file, position);
        var keywordFilters = KeywordCompletionFilters.None;
        var isNewIdentifierLocation = false;
        // !!! flags := CompletionInfoFlagsNone
        string[]? defaultCommitCharacters = null;

        if (contextToken is not null)
        {
            var importStatementCompletionInfo = GetImportStatementCompletionInfo(contextToken, file);
            if (importStatementCompletionInfo._keywordCompletion != Kind.Unknown)
            {
                if (importStatementCompletionInfo._isKeywordOnlyCompletion)
                {
                    return (new CompletionDataKeyword
                    {
                        _keywordCompletions =
                        [
                            new CompletionItem
                            {
                                _completionItem = new LsProto.CompletionItem
                                {
                                    Label = TypeScript.Scanner.Scanner.TokenToString(importStatementCompletionInfo._keywordCompletion),
                                    Kind = LsProto.CompletionItemKind.Keyword,
                                    SortText = SortTextGlobalsOrKeywords,
                                },
                            },
                        ],
                        _isNewIdentifierLocation = importStatementCompletionInfo._isNewIdentifierLocation,
                    }, null);
                }

                keywordFilters = KeywordFiltersFromSyntaxKind(importStatementCompletionInfo._keywordCompletion);
            }

            if (importStatementCompletionInfo._replacementSpan is not null && preferences.IncludeCompletionsForImportStatements.IsTrue())
            {
                // !!! flags |= CompletionInfoFlags.IsImportStatementCompletion;
                importStatementCompletion = importStatementCompletionInfo;
                isNewIdentifierLocation = importStatementCompletionInfo._isNewIdentifierLocation;
            }

            // 如果是已知的非法补全位置，直接退出。
            if (importStatementCompletionInfo._replacementSpan is null
                && IsCompletionListBlocker(contextToken, previousToken!, location, file, position, typeChecker))
            {
                if (keywordFilters != KeywordCompletionFilters.None)
                {
                    var (shadowedIsNewIdentifierLocation, _) = ComputeCommitCharactersAndIsNewIdentifier(contextToken, file, position);
                    return (KeywordCompletionData(keywordFilters, isJSOnlyLocation, shadowedIsNewIdentifierLocation), null);
                }

                return (null, null);
            }

            var parent = contextToken.Parent;
            if (contextToken.Kind == Kind.DotToken || contextToken.Kind == Kind.QuestionDotToken)
            {
                isRightOfDot = contextToken.Kind == Kind.DotToken;
                isRightOfQuestionDot = contextToken.Kind == Kind.QuestionDotToken;
                switch (parent!.Kind)
                {
                    case Kind.PropertyAccessExpression:
                        propertyAccessToConvert = parent;
                        node = propertyAccessToConvert.Expression();
                        var leftMostAccessExpression = GetLeftmostAccessExpression(parent);
                        if (NodeIsMissing(leftMostAccessExpression) ||
                            ((IsCallExpression(node!) || IsFunctionLike(node)) &&
                                node!.End() == contextToken.Pos() &&
                                TypeScript.LsUtil.LsUtil.GetLastChild(node, file)!.Kind != Kind.CloseParenToken))
                        {
                            // 这很可能是解析错误造成的点号，用户刚开始写 spread：
                            // eg: Math.min(./**/)
                            // const x = function (./**/) {}
                            // ({./**/})
                            return (null, null);
                        }

                        break;
                    case Kind.QualifiedName:
                        node = parent.AsQualifiedName()!.Left;
                        break;
                    case Kind.ModuleDeclaration:
                        node = parent.Name();
                        break;
                    case Kind.ImportType:
                        node = parent;
                        break;
                    case Kind.MetaProperty:
                        node = TypeScript.LsUtil.LsUtil.GetFirstToken(parent, file);
                        if (node!.Kind != Kind.ImportKeyword && node.Kind != Kind.NewKeyword)
                        {
                            throw new Exception("Unexpected token kind: " + node.Kind.String());
                        }

                        break;
                    default:
                        // 点号前面没有东西，大概只是一个孤立的字符，或者正在输入 '...'。直接退出。
                        return (null, null);
                }
            }
            else if (importStatementCompletion is null)
            {
                // <UI.Test /* completion position */ />
                // 若标签名是属性访问表达式，就向上走到最顶层的属性访问表达式，
                // 然后尝试拿到 JSX 容器及其属性类型。
                if (parent is not null && parent.Kind == Kind.PropertyAccessExpression)
                {
                    contextToken = parent;
                    parent = parent.Parent;
                }

                // 修正 location
                if (parent == location)
                {
                    switch (currentToken!.Kind)
                    {
                        case Kind.GreaterThanToken:
                            if (parent!.Kind == Kind.JsxElement || parent.Kind == Kind.JsxOpeningElement)
                            {
                                location = currentToken;
                            }

                            break;
                        case Kind.LessThanSlashToken:
                            if (parent!.Kind == Kind.JsxSelfClosingElement)
                            {
                                location = currentToken;
                            }

                            break;
                    }
                }

                switch (parent!.Kind)
                {
                    case Kind.JsxClosingElement:
                        if (contextToken!.Kind == Kind.LessThanSlashToken)
                        {
                            isStartingCloseTag = true;
                            location = contextToken;
                        }

                        break;
                    case Kind.BinaryExpression:
                        if (!BinaryExpressionMayBeOpenTag(parent.AsBinaryExpression()!))
                        {
                            break;
                        }

                        goto case Kind.JsxSelfClosingElement;
                    case Kind.JsxSelfClosingElement:
                    case Kind.JsxElement:
                    case Kind.JsxOpeningElement:
                        isJsxIdentifierExpected = true;
                        if (contextToken!.Kind == Kind.LessThanToken)
                        {
                            isRightOfOpenTag = true;
                            location = contextToken;
                        }

                        break;
                    case Kind.JsxExpression:
                    case Kind.JsxSpreadAttribute:
                        // 第一种情况是 `<div foo={true} [||] />` 或 `<div foo={true} [||] ></div>`，
                        // 此时 `parent` 是 `{true}` 而 `previousToken` 是 `}`。
                        // 第二种情况是 `<div foo={true} t[||] ></div>`。
                        // 第二种情况对 `<div foo={undefine[||]}></div>` 不能匹配。
                        if (previousToken!.Kind == Kind.CloseBraceToken ||
                            previousToken.Kind == Kind.Identifier && previousToken.Parent!.Kind == Kind.JsxAttribute)
                        {
                            isJsxIdentifierExpected = true;
                        }

                        break;
                    case Kind.JsxAttribute:
                        // 对 `<div className="x" [||] ></div>`，`parent` 是 JsxAttribute 而 `previousToken` 是它的 initializer。
                        if (parent.Initializer() == previousToken && previousToken!.End() < position)
                        {
                            isJsxIdentifierExpected = true;
                        }
                        else
                        {
                            switch (previousToken!.Kind)
                            {
                                case Kind.EqualsToken:
                                    jsxInitializer._isInitializer = true;
                                    break;
                                case Kind.Identifier:
                                    isJsxIdentifierExpected = true;
                                    // 对 `<div x=[|f/**/|]`，`parent` 是 `x` 而 `previousToken.parent` 是 `f`（它自己的 JsxAttribute）。
                                    // 注意 `<div someBool f>` 不应被当作 jsx initializer，那是属性名。
                                    if (parent != previousToken.Parent &&
                                        parent.Initializer() is null &&
                                        TypeScript.AstNav.AstNav.FindChildOfKind(parent, Kind.EqualsToken, file) is not null)
                                    {
                                        jsxInitializer._initializer = (Identifier)previousToken;
                                    }

                                    break;
                            }
                        }

                        break;
                }
            }
        }

        var completionKind = CompletionKind.None;
        var hasUnresolvedAutoImports = false;
        // 这一项在返回之后还会被嵌套函数改写。
        var symbols = new List<Ast.Symbol>();
        var autoImports = Array.Empty<FixAndExport>();
        // 键是 `symbols` 的下标。
        var symbolToOriginInfoMap = new Dictionary<int, SymbolOriginInfo>();
        var symbolToSortTextMap = new Dictionary<SymbolId, string>();
        var seenPropertySymbols = new Set<SymbolId>();
        var isTypeOnlyLocation = insideJSDocTagTypeExpression || insideJsDocImportTag ||
            importStatementCompletion is not null && location.Parent is not null && IsTypeOnlyImportOrExportDeclaration(location.Parent) ||
            !(contextToken is not null && IsContextTokenValueLocation(contextToken)) &&
                ((contextToken is not null && IsPossiblyTypeArgumentPosition(contextToken, file, typeChecker)) ||
                    IsPartOfTypeNode(location) ||
                    (contextToken is not null && IsContextTokenTypeLocation(contextToken)));

        void AddSymbolOriginInfo(Ast.Symbol symbol, bool insertQuestionDot, bool insertAwait)
        {
            var symbolId = GetSymbolId(symbol);
            if (insertAwait && seenPropertySymbols.AddIfAbsent(symbolId))
            {
                symbolToOriginInfoMap[symbols.Count - 1] = new SymbolOriginInfo { _kind = GetNullableSymbolOriginInfoKind(SymbolOriginInfoKind.Promise, insertQuestionDot) };
            }
            else if (insertQuestionDot)
            {
                symbolToOriginInfoMap[symbols.Count - 1] = new SymbolOriginInfo { _kind = SymbolOriginInfoKind.Nullable };
            }
        }

        void AddSymbolSortInfo(Ast.Symbol symbol)
        {
            var symbolId = GetSymbolId(symbol);
            if (IsStaticProperty(symbol))
            {
                symbolToSortTextMap[symbolId] = SortTextLocalDeclarationPriority;
            }
        }

        void AddPropertySymbol(Ast.Symbol symbol, bool insertAwait, bool insertQuestionDot)
        {
            // 对于像 `Symbol.iterator` 这样有可访问名字的计算属性，我们为**名字** `Symbol` 添加补全，
            // 而不是为属性本身。例如 [Symbol.iterator] 时，为 `Symbol` 添加补全。
            var computedPropertyName = FirstNonNil(symbol.Declarations, decl =>
            {
                var name = GetNameOfDeclaration(decl);
                if (name is not null && name.Kind == Kind.ComputedPropertyName)
                {
                    return name;
                }

                return null;
            });

            if (computedPropertyName is not null)
            {
                var leftMostName = GetLeftMostName(computedPropertyName.Expression()!); // 补全的是 `Symbol`，不是 `iterator`。
                Ast.Symbol? nameSymbol = null;
                if (leftMostName is not null)
                {
                    nameSymbol = typeChecker.GetSymbolAtLocation(leftMostName);
                }

                // 若这是嵌套的，例如 `namespace N { export const sym = Symbol(); }`，则为 `N` 添加补全。
                Ast.Symbol? firstAccessibleSymbol = null;
                if (nameSymbol is not null)
                {
                    firstAccessibleSymbol = GetFirstSymbolInChain(nameSymbol, contextToken!, typeChecker);
                }

                var firstAccessibleSymbolId = default(SymbolId);
                if (firstAccessibleSymbol is not null)
                {
                    firstAccessibleSymbolId = GetSymbolId(firstAccessibleSymbol);
                }

                if (firstAccessibleSymbolId != 0 && seenPropertySymbols.AddIfAbsent(firstAccessibleSymbolId))
                {
                    symbols.Add(firstAccessibleSymbol!);
                    symbolToSortTextMap[firstAccessibleSymbolId] = SortTextGlobalsOrKeywords;
                    var moduleSymbol = firstAccessibleSymbol!.Parent;
                    if (moduleSymbol is null ||
                        !TypeScript.Checker.Checker.IsExternalModuleSymbol(moduleSymbol) ||
                        typeChecker.TryGetMemberInModuleExportsAndProperties(firstAccessibleSymbol.Name, moduleSymbol) != firstAccessibleSymbol)
                    {
                        symbolToOriginInfoMap[symbols.Count - 1] = new SymbolOriginInfo { _kind = GetNullableSymbolOriginInfoKind(SymbolOriginInfoKind.SymbolMember, insertQuestionDot) };
                    }
                    else
                    {
                        // !!! auto-import symbol
                    }
                }
                else if (firstAccessibleSymbolId == 0 || !seenPropertySymbols.Has(firstAccessibleSymbolId))
                {
                    symbols.Add(symbol);
                    AddSymbolOriginInfo(symbol, insertQuestionDot, insertAwait);
                    AddSymbolSortInfo(symbol);
                }
            }
            else
            {
                symbols.Add(symbol);
                AddSymbolOriginInfo(symbol, insertQuestionDot, insertAwait);
                AddSymbolSortInfo(symbol);
            }
        }

        void AddTypeProperties(TypeScript.Checker.Type t, bool insertAwait, bool insertQuestionDot)
        {
            if (typeChecker.GetStringIndexType(t) is not null)
            {
                isNewIdentifierLocation = true;
                defaultCommitCharacters = [];
            }

            if (isRightOfQuestionDot && typeChecker.GetCallSignatures(t).Length != 0)
            {
                isNewIdentifierLocation = true;
                if (defaultCommitCharacters is null)
                {
                    defaultCommitCharacters = (string[])AllCommitCharacters.Clone(); // 这里唯一非法的 commit character 是 `(`。
                }
            }

            Node propertyAccess;
            if (node!.Kind == Kind.ImportType)
            {
                propertyAccess = node;
            }
            else
            {
                propertyAccess = node.Parent!;
            }

            if (inCheckedFile)
            {
                foreach (var symbol in typeChecker.GetApparentProperties(t))
                {
                    if (typeChecker.IsValidPropertyAccessForCompletions(propertyAccess, t, symbol))
                    {
                        AddPropertySymbol(symbol, false /*insertAwait*/, insertQuestionDot);
                    }
                }
            }
            else
            {
                // 在 JavaScript 文件里，对联合类型我们不只是取各个类型的公共成员，
                // 而是把每个类型各自拥有的成员也包含进来。这是因为我们无论如何都会把所有标识符加进去。
                // 所以我们不如把那些至少属于某个具体类型的成员提升到更高的优先级，因为我们已经知道它们是什么。
                foreach (var symbol in GetPropertiesForCompletion(t, typeChecker))
                {
                    if (typeChecker.IsValidPropertyAccessForCompletions(propertyAccess, t, symbol))
                    {
                        symbols.Add(symbol);
                    }
                }
            }

            if (insertAwait)
            {
                var promiseType = typeChecker.GetPromisedTypeOfPromise(t);
                if (promiseType is not null)
                {
                    foreach (var symbol in typeChecker.GetApparentProperties(promiseType))
                    {
                        if (typeChecker.IsValidPropertyAccessForCompletions(propertyAccess, promiseType, symbol))
                        {
                            AddPropertySymbol(symbol, true /*insertAwait*/, insertQuestionDot);
                        }
                    }
                }
            }
        }

        void GetTypeScriptMemberSymbols()
        {
            // 点号右侧的成员补全列表
            completionKind = CompletionKind.PropertyAccess;

            // 由于这是限定名，检查它是否是类型节点位置
            var isImportType = IsLiteralImportTypeNode(node);
            var isTypeLocation = (isImportType && !node!.AsImportTypeNode()!.IsTypeOf) ||
                IsPartOfTypeNode(node!.Parent) ||
                IsPossiblyTypeArgumentPosition(contextToken!, file, typeChecker);
            var isRhsOfImportDeclaration = IsInRightSideOfInternalImportEqualsDeclaration(node);
            if (IsEntityName(node) || isImportType || IsPropertyAccessExpression(node!))
            {
                var isNamespaceName = IsModuleDeclaration(node!.Parent!);
                if (isNamespaceName)
                {
                    isNewIdentifierLocation = true;
                    defaultCommitCharacters = [];
                }

                var symbol = typeChecker.GetSymbolAtLocation(node);
                if (symbol is not null)
                {
                    symbol = TypeScript.Checker.Checker.SkipAlias(symbol, typeChecker);
                    if ((symbol.Flags & (SymbolFlags.Module | SymbolFlags.Enum)) != 0)
                    {
                        Node valueAccessNode;
                        if (isImportType)
                        {
                            valueAccessNode = node;
                        }
                        else
                        {
                            valueAccessNode = node.Parent!;
                        }

                        // 提取模块或枚举的成员
                        var exportedSymbols = typeChecker.GetExportsOfModule(symbol);
                        foreach (var exportedSymbol in exportedSymbols)
                        {
                            if (exportedSymbol is null)
                            {
                                throw new Exception("getExporsOfModule() should all be defined");
                            }

                            bool IsValidValueAccess(Ast.Symbol s) => typeChecker.IsValidPropertyAccess(valueAccessNode, s.Name);
                            bool IsValidTypeAccess(Ast.Symbol s) => SymbolCanBeReferencedAtTypeLocation(s, typeChecker, new Set<SymbolId>());

                            bool isValidAccess;
                            if (isNamespaceName)
                            {
                                // 在 `namespace N.M/**/` 处，如果这是 `M` 的唯一声明，不要把 `M` 作为补全项包含进来。
                                isValidAccess = (exportedSymbol.Flags & SymbolFlags.Namespace) != 0 &&
                                    !Every(exportedSymbol.Declarations, declaration => declaration.Parent == node.Parent);
                            }
                            else if (isRhsOfImportDeclaration)
                            {
                                // 在内部 import equals 声明里点命名空间时，任何种类都允许。
                                isValidAccess = IsValidTypeAccess(exportedSymbol) || IsValidValueAccess(exportedSymbol);
                            }
                            else if (isTypeLocation || insideJSDocTagTypeExpression)
                            {
                                isValidAccess = IsValidTypeAccess(exportedSymbol);
                            }
                            else
                            {
                                isValidAccess = IsValidValueAccess(exportedSymbol);
                            }

                            if (isValidAccess)
                            {
                                symbols.Add(exportedSymbol);
                            }
                        }

                        // 若模块与某个值合并了，必须取该 class 的类型并加入它的属性（用于继承来的静态方法）。
                        if (!isTypeLocation && !insideJSDocTagTypeExpression &&
                            Some(
                                symbol.Declarations,
                                decl => decl.Kind != Kind.SourceFile && decl.Kind != Kind.ModuleDeclaration && decl.Kind != Kind.EnumDeclaration))
                        {
                            var t = typeChecker.GetNonOptionalType(typeChecker.GetTypeOfSymbolAtLocation(symbol, node))!;
                            var insertQuestionDot = false;
                            if (typeChecker.IsNullableType(t))
                            {
                                var canCorrectToQuestionDot = isRightOfDot && !isRightOfQuestionDot &&
                                    !preferences.IncludeAutomaticOptionalChainCompletions.IsFalse();
                                if (canCorrectToQuestionDot || isRightOfQuestionDot)
                                {
                                    t = typeChecker.GetNonNullableType(t);
                                    if (canCorrectToQuestionDot)
                                    {
                                        insertQuestionDot = true;
                                    }
                                }
                            }

                            AddTypeProperties(t, (node!.Flags & NodeFlags.AwaitContext) != 0, insertQuestionDot);
                        }

                        return;
                    }
                }
            }

            if (!isTypeLocation || TypeScript.Checker.Checker.IsInTypeQuery(node!))
            {
                // microsoft/TypeScript#39946. 在带上下文 `this` 形参的函数内部对节点取类型，如果该 `node` 是
                // `yield` 或 `return` 表达式的一部分，可能造成循环。编译期不存在该循环，因为我们会在检查该节点类型
                // **之前**先检查（并缓存）`this` 的类型。
                typeChecker.TryGetThisTypeAtEx(node!, false /*includeGlobalThis*/, null);
                var t = typeChecker.GetNonOptionalType(typeChecker.GetTypeAtLocation(node!))!;

                if (!isTypeLocation)
                {
                    var insertQuestionDot = false;
                    if (typeChecker.IsNullableType(t))
                    {
                        var canCorrectToQuestionDot = isRightOfDot && !isRightOfQuestionDot &&
                            !preferences.IncludeAutomaticOptionalChainCompletions.IsFalse();

                        if (canCorrectToQuestionDot || isRightOfQuestionDot)
                        {
                            t = typeChecker.GetNonNullableType(t);
                            if (canCorrectToQuestionDot)
                            {
                                insertQuestionDot = true;
                            }
                        }
                    }

                    AddTypeProperties(t, (node!.Flags & NodeFlags.AwaitContext) != 0, insertQuestionDot);
                }
                else
                {
                    AddTypeProperties(typeChecker.GetNonNullableType(t), false /*insertAwait*/, false /*insertQuestionDot*/);
                }
            }
        }

        // 聚合类型实参位置的对象字面量补全所需的相关符号。
        (GlobalsSearch, Exception?) TryGetObjectTypeLiteralInTypeArgumentCompletionSymbols()
        {
            var typeLiteralNode = TryGetTypeLiteralNode(contextToken!);
            if (typeLiteralNode is null)
            {
                return (GlobalsSearch.Continue, null);
            }

            Node? intersectionTypeNode = IfElse(
                IsIntersectionTypeNode(typeLiteralNode.Parent!),
                typeLiteralNode.Parent,
                (Node?)null);
            var containerTypeNode = IfElse(
                intersectionTypeNode is not null,
                intersectionTypeNode,
                (Node)typeLiteralNode)!;

            var containerExpectedType = GetConstraintOfTypeArgumentProperty(containerTypeNode, typeChecker);
            if (containerExpectedType is null)
            {
                return (GlobalsSearch.Continue, null);
            }

            var containerActualType = typeChecker.GetTypeFromTypeNode(containerTypeNode);

            var members = GetPropertiesForCompletion(containerExpectedType, typeChecker);
            var existingMembers = GetPropertiesForCompletion(containerActualType, typeChecker);

            var existingMemberNames = new Set<string>();
            foreach (var member in existingMembers)
            {
                existingMemberNames.Add(member.Name);
            }

            symbols.AddRange(Filter(members, member => !existingMemberNames.Has(member.Name)));

            completionKind = CompletionKind.ObjectPropertyDeclaration;
            isNewIdentifierLocation = true;

            return (GlobalsSearch.Success, null);
        }

        // 聚合对象字面量与对象绑定模式补全所需的相关符号。
        // 相关符号存放在被捕获的 'symbols' 变量里。
        (GlobalsSearch, Exception?) TryGetObjectLikeCompletionSymbols()
        {
            if (contextToken is not null && contextToken.Kind == Kind.DotDotDotToken)
            {
                return (GlobalsSearch.Continue, null);
            }

            var objectLikeContainer = TryGetObjectLikeCompletionContainer(contextToken!, position, file);
            if (objectLikeContainer is null)
            {
                return (GlobalsSearch.Continue, null);
            }

            // 我们是从上下文类型/推断类型/声明类型里查找可能的属性名。
            completionKind = CompletionKind.ObjectPropertyDeclaration;

            Ast.Symbol[] typeMembers = [];
            Node[] existingMembers = [];

            if (objectLikeContainer.Kind == Kind.ObjectLiteralExpression)
            {
                var instantiatedType = TryGetObjectLiteralContextualType((ObjectLiteralExpression)objectLikeContainer, typeChecker);

                // 检查对象属性值简写的补全
                if (instantiatedType is null)
                {
                    if ((objectLikeContainer.Flags & NodeFlags.InWithStatement) != 0)
                    {
                        return (GlobalsSearch.Fail, null);
                    }

                    return (GlobalsSearch.Continue, null);
                }

                var completionsType = typeChecker.GetContextualType(objectLikeContainer, TypeScript.Checker.ContextFlags.IgnoreNodeInferences);
                var t = completionsType ?? instantiatedType;
                var stringIndexType = typeChecker.GetStringIndexType(t);
                var numberIndexType = typeChecker.GetNumberIndexType(t);
                isNewIdentifierLocation = stringIndexType is not null || numberIndexType is not null;
                typeMembers = GetPropertiesForObjectExpression(instantiatedType, completionsType!, objectLikeContainer, typeChecker);
                existingMembers = objectLikeContainer.Properties() ?? [];

                if (typeMembers.Length == 0)
                {
                    // 边界情况：如果存在 number index type
                    if (numberIndexType is null)
                    {
                        return (GlobalsSearch.Continue, null);
                    }
                }
            }
            else
            {
                if (objectLikeContainer.Kind != Kind.ObjectBindingPattern)
                {
                    throw new Exception("Expected 'objectLikeContainer' to be an object binding pattern.");
                }

                // 我们**只**补全被解构类型的属性。
                isNewIdentifierLocation = false;
                var rootDeclaration = GetRootDeclaration(objectLikeContainer.Parent);
                if (!TypeScript.Ast.Ast.IsVariableLike(rootDeclaration))
                {
                    throw new Exception("Root declaration is not variable-like.");
                }

                // 我们不想用绑定模式的结构得到的类型来补全；我们只关心由类型声明或推断得到的类型。
                // 另外，如果 rootDeclaration 是形参且其所在的函数表达式/箭头函数是上下文类型的，也继续 ——
                // 形参的类型会从函数的上下文类型流出。
                var canGetType = HasInitializer(rootDeclaration) ||
                    GetTypeAnnotationNode(rootDeclaration) is not null ||
                    rootDeclaration!.Parent!.Parent!.Kind == Kind.ForOfStatement;
                if (!canGetType && rootDeclaration!.Kind == Kind.Parameter)
                {
                    if (IsExpression(rootDeclaration.Parent))
                    {
                        canGetType = typeChecker.GetContextualType(rootDeclaration.Parent!, TypeScript.Checker.ContextFlags.None) is not null;
                    }
                    else if (rootDeclaration.Parent!.Kind == Kind.MethodDeclaration ||
                        rootDeclaration.Parent!.Kind == Kind.SetAccessor)
                    {
                        canGetType = IsExpression(rootDeclaration.Parent.Parent) &&
                            typeChecker.GetContextualType(rootDeclaration.Parent.Parent!, TypeScript.Checker.ContextFlags.None) is not null;
                    }
                }

                if (canGetType)
                {
                    var typeForObject = typeChecker.GetTypeAtLocation(objectLikeContainer);
                    if (typeForObject is null)
                    {
                        return (GlobalsSearch.Fail, null);
                    }

                    typeMembers = Filter(
                        typeChecker.GetPropertiesOfType(typeForObject),
                        propertySymbol => typeChecker.IsPropertyAccessible(
                            objectLikeContainer,
                            false, /*isSuper*/
                            false, /*isWrite*/
                            typeForObject,
                            propertySymbol));
                    existingMembers = objectLikeContainer.Elements() ?? [];
                }
            }

            if (typeMembers.Length > 0)
            {
                // 把过滤后的项加入补全列表。
                var (filteredMembers, spreadMemberNames) = FilterObjectMembersList(
                    typeMembers,
                    CheckEachDefined(existingMembers, "object like properties or elements should all be defined"),
                    file,
                    position,
                    typeChecker);
                symbols.AddRange(filteredMembers);

                // 设置 sort text。
                foreach (var member in filteredMembers)
                {
                    var symbolId = GetSymbolId(member);
                    if (spreadMemberNames.Has(member.Name))
                    {
                        symbolToSortTextMap[symbolId] = SortTextMemberDeclaredBySpreadAssignment;
                    }

                    if ((member.Flags & SymbolFlags.Optional) != 0)
                    {
                        if (!symbolToSortTextMap.ContainsKey(symbolId))
                        {
                            symbolToSortTextMap[symbolId] = SortTextOptionalMember;
                        }
                    }

                    if (objectLikeContainer.Kind == Kind.ObjectLiteralExpression && preferences.IncludeCompletionsWithObjectLiteralMethodSnippets.IsTrue())
                    {
                        var (displayName, _) = GetCompletionEntryDisplayNameForSymbol(file, preferences, member, null! /*origin*/, CompletionKind.ObjectPropertyDeclaration, false /*isJsxIdentifierExpected*/);
                        if (displayName != "")
                        {
                            // Go: completions.go:1185 `originalSortText := core.OrElse(symbolToSortTextMap[symbolId], SortTextLocationPriority)`。
                            // Go 的 map 读缺键给出零值 ""，OrElse[T comparable] 的零值判据也是 ""（*new(string)），
                            // 两者合起来等价于 C# 的 `GetValueOrDefault(symbolId, "")` + Core.OrElse 的 string 重载。
                            var originalSortText = OrElse(symbolToSortTextMap.GetValueOrDefault(symbolId, ""), SortTextLocationPriority)!;
                            symbolToSortTextMap[symbolId] = ObjectLiteralPropertySortText(originalSortText, displayName);
                        }
                    }
                }

                if (objectLikeContainer.Kind == Kind.ObjectLiteralExpression && preferences.IncludeCompletionsWithObjectLiteralMethodSnippets.IsTrue())
                {
                    foreach (var entry in CollectObjectLiteralMethodSymbols(ctx, typeChecker, filteredMembers, objectLikeContainer, file))
                    {
                        symbolToOriginInfoMap[symbols.Count] = entry._origin;
                        symbols.Add(entry._symbol);
                    }
                }
            }

            return (GlobalsSearch.Success, null);
        }

        bool ShouldOfferImportCompletions()
        {
            if (TypeScript.TSPath.TSPath.IsDynamicFileName(file.FileName()))
            {
                return false;
            }

            // 如果已经在输入 import 语句，就为它提供补全。
            if (importStatementCompletion is not null)
            {
                return true;
            }

            // 如果不是模块，必须启用了 modules。
            if (preferences.IncludeCompletionsForModuleExports.IsFalse())
            {
                return false;
            }

            // 6.0+ 一直使用 ES modules
            return true;
        }

        // 会改写 `symbols`、`symbolToOriginInfoMap` 和 `symbolToSortTextMap`
        Exception? CollectAutoImports()
        {
            // 自动导入补全项的 `completionItem/resolve` 应通过 completion item data 解析，
            // 所以不需要再次收集自动导入项。
            if (forItemResolve)
            {
                return null;
            }

            if (!ShouldOfferImportCompletions())
            {
                return null;
            }

            // import { type | -> token 文本应为空
            var lowerCaseTokenText = "";
            var (usagePosition, fidelity) = CreateLspPosition(position, file);
            if (!fidelity.IsExact())
            {
                return null;
            }

            if (previousToken is not null && IsIdentifier(previousToken))
            {
                (usagePosition, fidelity) = CreateLspPosition(TypeScript.Scanner.Scanner.GetTokenPosOfNode(previousToken, file, false /*includeJSDoc*/), file);
                if (!fidelity.IsExact())
                {
                    return null;
                }

                if (!(previousToken == contextToken && importStatementCompletion is not null))
                {
                    lowerCaseTokenText = previousToken.Text().ToLowerInvariant();
                }
            }

            var (view, err) = GetPreparedAutoImportView(file, typeChecker);
            if (err is not null)
            {
                return err;
            }

            if (view is null)
            {
                return null;
            }

            autoImports = view.GetCompletions(lowerCaseTokenText, usagePosition, isRightOfOpenTag, isTypeOnlyLocation);
            return null;
        }

        (GlobalsSearch, Exception?) TryGetImportCompletionSymbols()
        {
            if (importStatementCompletion is null)
            {
                return (GlobalsSearch.Continue, null);
            }

            isNewIdentifierLocation = true;
            var collectError = CollectAutoImports();
            if (collectError is not null)
            {
                return (GlobalsSearch.Fail, collectError);
            }

            return (GlobalsSearch.Success, null);
        }

        // 聚合 import 子句与 export 子句（其声明带模块说明符）的补全相关符号；例如
        //
        //      import { | } from "moduleName";
        //      export { a as foo, | } from "moduleName";
        //
        // 但不包含
        //
        //      export { | };
        //
        // 相关符号存放在被捕获的 'symbols' 变量里。
        (GlobalsSearch, Exception?) TryGetImportOrExportClauseCompletionSymbols()
        {
            if (contextToken is null)
            {
                return (GlobalsSearch.Continue, null);
            }

            // `import { |` 或 `import { a as 0, | }` 或 `import { type | }`
            Node? namedImportsOrExports = null;
            if (contextToken.Kind == Kind.OpenBraceToken || contextToken.Kind == Kind.CommaToken)
            {
                namedImportsOrExports = IfElse(IsNamedImportsOrExports(contextToken.Parent!), contextToken.Parent, (Node?)null);
            }
            else if (IsTypeKeywordTokenOrIdentifier(contextToken))
            {
                namedImportsOrExports = IfElse(
                    IsNamedImportsOrExports(contextToken.Parent!.Parent!),
                    contextToken.Parent.Parent,
                    (Node?)null);
            }

            if (namedImportsOrExports is null)
            {
                return (GlobalsSearch.Continue, null);
            }

            // 在 `import { |` 处至少可以提示 `type`
            if (!IsTypeKeywordTokenOrIdentifier(contextToken))
            {
                keywordFilters = KeywordCompletionFilters.TypeKeyword;
            }

            // 尝试为导入/再导出的模块显示导出的成员
            var moduleSpecifier = (namedImportsOrExports.Kind == Kind.NamedImports
                ? namedImportsOrExports.Parent!.Parent!
                : namedImportsOrExports.Parent!).ModuleSpecifier();
            if (moduleSpecifier is null)
            {
                isNewIdentifierLocation = true;
                if (namedImportsOrExports.Kind == Kind.NamedImports)
                {
                    return (GlobalsSearch.Fail, null);
                }

                return (GlobalsSearch.Continue, null);
            }

            var moduleSpecifierSymbol = typeChecker.GetSymbolAtLocation(moduleSpecifier);
            if (moduleSpecifierSymbol is null)
            {
                isNewIdentifierLocation = true;
                return (GlobalsSearch.Fail, null);
            }

            completionKind = CompletionKind.MemberLike;
            isNewIdentifierLocation = false;
            var exports = typeChecker.GetExportsAndPropertiesOfModule(moduleSpecifierSymbol);

            var existing = new Set<string>();
            foreach (var element in namedImportsOrExports.Elements() ?? [])
            {
                if (IsCurrentlyEditingNode(element, file, position))
                {
                    continue;
                }

                existing.Add(element.PropertyNameOrName()!.Text());
            }

            var uniques = Filter(exports, symbol => SymbolName(symbol) != InternalSymbolNameDefault && !existing.Has(SymbolName(symbol)));

            symbols.AddRange(uniques);
            if (uniques.Length == 0)
            {
                // 如果没有别的可导入项，也不要提示 `type`。
                keywordFilters = KeywordCompletionFilters.None;
            }

            return (GlobalsSearch.Success, null);
        }

        // import { x } from "foo" with { | }
        (GlobalsSearch, Exception?) TryGetImportAttributesCompletionSymbols()
        {
            if (contextToken is null)
            {
                return (GlobalsSearch.Continue, null);
            }

            Node? importAttributes = null;
            switch (contextToken.Kind)
            {
                case Kind.OpenBraceToken:
                case Kind.CommaToken:
                    importAttributes = contextToken.Parent;
                    break;
                case Kind.ColonToken:
                    importAttributes = contextToken.Parent!.Parent;
                    break;
            }

            if (importAttributes is null || !IsImportAttributes(importAttributes))
            {
                return (GlobalsSearch.Continue, null);
            }

            Node[] elements = [];
            if (importAttributes.AsImportAttributes()!.Attributes is not null)
            {
                elements = importAttributes.AsImportAttributes()!.Attributes!.Nodes ?? [];
            }

            var attributeNames = Map(elements, el => el.AsImportAttribute()!.Name()!.Text());
            var existing = NewSetFromItems(attributeNames);
            var uniques = Filter(
                typeChecker.GetApparentProperties(typeChecker.GetTypeAtLocation(importAttributes)),
                symbol => !existing.Has(SymbolName(symbol)));
            symbols.AddRange(uniques);
            return (GlobalsSearch.Success, null);
        }

        // 为具名导出补全添加本地声明：
        //   export { | };
        // 不检查模块说明符是否缺失（`export {} from "./other"`），
        // 因为 `TryGetImportOrExportClauseCompletionSymbols` 先运行并处理了它，从而阻止本函数运行。
        (GlobalsSearch, Exception?) TryGetLocalNamedExportCompletionSymbols()
        {
            if (contextToken is null)
            {
                return (GlobalsSearch.Continue, null);
            }

            Node? namedExports = null;
            if (contextToken.Kind == Kind.OpenBraceToken || contextToken.Kind == Kind.CommaToken)
            {
                namedExports = IfElse(IsNamedExports(contextToken.Parent!), contextToken.Parent, (Node?)null);
            }

            if (namedExports is null)
            {
                return (GlobalsSearch.Continue, null);
            }

            var localsContainer = FindAncestor(namedExports, node => IsSourceFile(node) || IsModuleDeclaration(node));
            completionKind = CompletionKind.None;
            isNewIdentifierLocation = false;
            var localSymbol = localsContainer!.Symbol;
            var localExports = localSymbol?.Exports;
            foreach (var (name, symbol) in localsContainer.Locals() ?? new SymbolTable())
            {
                symbols.Add(symbol);
                if (localExports is not null && localExports.ContainsKey(name))
                {
                    var symbolId = GetSymbolId(symbol);
                    symbolToSortTextMap[symbolId] = SortTextOptionalMember;
                }
            }

            return (GlobalsSearch.Success, null);
        }

        (GlobalsSearch, Exception?) TryGetConstructorCompletion()
        {
            if (TryGetConstructorLikeCompletionContainer(contextToken!) is null)
            {
                return (GlobalsSearch.Continue, null);
            }

            // 没有成员，只有关键字
            completionKind = CompletionKind.None;
            // 正在声明新的属性/方法/访问器
            isNewIdentifierLocation = true;
            // 有构造器形参的关键字
            keywordFilters = KeywordCompletionFilters.ConstructorParameterKeywords;
            return (GlobalsSearch.Success, null);
        }

        // 聚合 class 声明补全所需的相关符号。
        // 相关符号存放在被捕获的 'symbols' 变量里。
        (GlobalsSearch, Exception?) TryGetClassLikeCompletionSymbols()
        {
            var decl = TryGetObjectTypeDeclarationCompletionContainer(file, contextToken!, location, position);
            if (decl is null)
            {
                return (GlobalsSearch.Continue, null);
            }

            // 我们是从父类型查找可能的属性名。
            completionKind = CompletionKind.MemberLike;
            // 正在声明新的属性/方法/访问器
            isNewIdentifierLocation = true;
            if (contextToken!.Kind == Kind.AsteriskToken)
            {
                keywordFilters = KeywordCompletionFilters.None;
            }
            else if (IsClassLike(decl))
            {
                keywordFilters = KeywordCompletionFilters.ClassElementKeywords;
            }
            else
            {
                keywordFilters = KeywordCompletionFilters.InterfaceElementKeywords;
            }

            // 如果在 interface 里，你不会想重复 super-interface 的东西。所以到此为止。
            if (!IsClassLike(decl))
            {
                return (GlobalsSearch.Success, null);
            }

            Node? classElement;
            if (contextToken!.Kind == Kind.SemicolonToken)
            {
                classElement = contextToken.Parent!.Parent;
            }
            else
            {
                classElement = contextToken.Parent;
            }

            var classElementModifierFlags = ModifierFlags.None;
            if (IsClassElement(classElement!))
            {
                classElementModifierFlags = classElement!.ModifierFlags;
            }

            // 如果 context token 不是我们正在编辑的东西，考虑它是否会导致成为修饰符。
            if (contextToken!.Kind == Kind.Identifier && !IsCurrentlyEditingNode(contextToken, file, position))
            {
                switch (contextToken.Text())
                {
                    case "private":
                        classElementModifierFlags |= ModifierFlags.Private;
                        break;
                    case "static":
                        classElementModifierFlags |= ModifierFlags.Static;
                        break;
                    case "override":
                        classElementModifierFlags |= ModifierFlags.Override;
                        break;
                }
            }

            if (IsClassStaticBlockDeclaration(classElement!))
            {
                classElementModifierFlags |= ModifierFlags.Static;
            }

            // 私有方法没有成员列表
            if ((classElementModifierFlags & ModifierFlags.Private) == 0)
            {
                // 基类型中非私有且已实现的属性符号列表
                Node[] baseTypeNodes;
                if (IsClassLike(decl) && (classElementModifierFlags & ModifierFlags.Override) != 0)
                {
                    baseTypeNodes = SingleElementSlice(GetClassExtendsHeritageElement(decl));
                }
                else
                {
                    baseTypeNodes = GetAllSuperTypeNodes(decl) ?? [];
                }

                var baseSymbols = new List<Ast.Symbol>();
                foreach (var baseTypeNode in baseTypeNodes)
                {
                    var t = typeChecker.GetTypeAtLocation(baseTypeNode);
                    if ((classElementModifierFlags & ModifierFlags.Static) != 0)
                    {
                        if (t.Symbol() is not null)
                        {
                            baseSymbols.AddRange(
                                typeChecker.GetPropertiesOfType(typeChecker.GetTypeOfSymbolAtLocation(t.Symbol()!, decl)));
                        }
                    }
                    else if (t is not null)
                    {
                        baseSymbols.AddRange(typeChecker.GetPropertiesOfType(t));
                    }
                }

                symbols.AddRange(
                    FilterClassMembersList(baseSymbols.ToArray(), decl.Members() ?? [], classElementModifierFlags, file, position));

                for (var index = 0; index < symbols.Count; index++)
                {
                    var symbol = symbols[index];
                    var declaration = symbol.ValueDeclaration;
                    if (declaration is not null && IsClassElement(declaration) &&
                        declaration.Name() is not null &&
                        IsComputedPropertyName(declaration.Name()!))
                    {
                        var origin = new SymbolOriginInfo
                        {
                            _kind = SymbolOriginInfoKind.ComputedPropertyName,
                            _data = new SymbolOriginInfoComputedPropertyName { _symbolName = typeChecker.SymbolToString(symbol) },
                        };
                        symbolToOriginInfoMap[index] = origin;
                    }
                }
            }

            return (GlobalsSearch.Success, null);
        }

        (GlobalsSearch, Exception?) TryGetJsxCompletionSymbols()
        {
            var jsxContainer = TryGetContainingJsxElement(contextToken!, file);
            if (jsxContainer is null)
            {
                return (GlobalsSearch.Continue, null);
            }

            // 光标位于 JSX 自闭合元素或开标签内部。
            var attrsType = typeChecker.GetContextualType(jsxContainer.Attributes()!, TypeScript.Checker.ContextFlags.None);
            if (attrsType is null)
            {
                return (GlobalsSearch.Continue, null);
            }

            var completionsType = typeChecker.GetContextualType(jsxContainer.Attributes()!, TypeScript.Checker.ContextFlags.IgnoreNodeInferences);
            var (filteredSymbols, spreadMemberNames) = FilterJsxAttributes(
                GetPropertiesForObjectExpression(attrsType, completionsType!, jsxContainer.Attributes()!, typeChecker),
                jsxContainer.Attributes()!.Properties() ?? [],
                file,
                position,
                typeChecker);

            symbols.AddRange(filteredSymbols);

            // 设置 sort text。
            foreach (var symbol in filteredSymbols)
            {
                var symbolId = GetSymbolId(symbol);
                if (spreadMemberNames.Has(SymbolName(symbol)))
                {
                    symbolToSortTextMap[symbolId] = SortTextMemberDeclaredBySpreadAssignment;
                }

                if ((symbol.Flags & SymbolFlags.Optional) != 0)
                {
                    if (!symbolToSortTextMap.ContainsKey(symbolId))
                    {
                        symbolToSortTextMap[symbolId] = SortTextOptionalMember;
                    }
                }
            }

            completionKind = CompletionKind.MemberLike;
            isNewIdentifierLocation = false;
            return (GlobalsSearch.Success, null);
        }

        (GlobalsSearch, Exception?) GetGlobalCompletions()
        {
            if (TryGetFunctionLikeBodyCompletionContainer(contextToken!) is not null)
            {
                keywordFilters = KeywordCompletionFilters.FunctionLikeBodyKeywords;
            }
            else
            {
                keywordFilters = KeywordCompletionFilters.All;
            }

            // 取当前作用域内的所有实体。
            completionKind = CompletionKind.Global;
            (isNewIdentifierLocation, defaultCommitCharacters) = ComputeCommitCharactersAndIsNewIdentifier(contextToken!, file, position);

            if (previousToken != contextToken)
            {
                if (previousToken is null)
                {
                    throw new Exception("Expected 'contextToken' to be defined when different from 'previousToken'.");
                }
            }

            // 我们需要找到能给出合适作用域的节点，以便开始聚合补全候选。这由 'GetScopeNode' 完成：
            // 它找到第一个包含该位置的节点，并考虑节点是否“完整”来决定某个位置是否属于该节点。
            //
            // 然而，在标识符末尾时我们关心的是标识符自身的作用域，但位置落在标识符之外。例如：
            //
            //      xyz => x$
            //
            // 光标既不在 'x' 内，也不在箭头函数 'xyz => x' 内，所以 'xyz' 不会出现在结果里。
            //
            // 我们定义 'adjustedPosition' 来正确处理位于标识符末尾的情况。意图是：在标识符末尾请求补全，
            // 应当等价于在标识符内部/开头请求补全。所以上一例中 'adjustedPosition' 会表现得像在下面这样请求补全：
            //
            //      xyz => $x
            //
            // 若 previousToken !== contextToken，则
            //   - 'contextToken' 被调整为 'previousToken' 之前的那个 token（因为我们位于标识符末尾）。
            //   - 'previousToken' 有定义。
            int adjustedPosition;
            if (previousToken != contextToken)
            {
                adjustedPosition = TypeScript.AstNav.AstNav.GetStartOfNode(previousToken!, file, false /*includeJSDoc*/);
            }
            else
            {
                adjustedPosition = position;
            }

            var scopeNode = GetScopeNode(contextToken!, adjustedPosition, file);
            scopeNode ??= file.AsNode();
            isInSnippetScope = IsSnippetScope(scopeNode);

            var symbolMeanings = IfElse(isTypeOnlyLocation, SymbolFlags.None, SymbolFlags.Value) |
                SymbolFlags.Type | SymbolFlags.Namespace | SymbolFlags.Alias;
            var typeOnlyAliasNeedsPromotion = previousToken is not null && !IsValidTypeOnlyAliasUseSite(previousToken);

            symbols.AddRange(typeChecker.GetSymbolsInScope(scopeNode, symbolMeanings));
            CheckEachDefined(symbols.ToArray(), "getSymbolsInScope() should all be defined");
            for (var index = 0; index < symbols.Count; index++)
            {
                var symbol = symbols[index];
                var symbolId = GetSymbolId(symbol);
                if (!typeChecker.IsArgumentsSymbol(symbol) &&
                    !Some(symbol.Declarations, decl => GetSourceFileOfNode(decl) == file))
                {
                    symbolToSortTextMap[symbolId] = SortTextGlobalsOrKeywords;
                }

                if (typeOnlyAliasNeedsPromotion && (symbol.Flags & SymbolFlags.Value) == 0)
                {
                    var typeOnlyAliasDeclaration = Find(symbol.Declarations, TypeScript.Ast.Ast.IsTypeOnlyImportDeclaration);
                    if (typeOnlyAliasDeclaration is not null)
                    {
                        var origin = new SymbolOriginInfo
                        {
                            _kind = SymbolOriginInfoKind.TypeOnlyAlias,
                            _data = new SymbolOriginInfoTypeOnlyAlias { _declaration = typeOnlyAliasDeclaration },
                        };
                        symbolToOriginInfoMap[index] = origin;
                    }
                }
            }

            // 需要在 `this` 类型的属性前插入 'this.'。
            if (scopeNode.Kind != Kind.SourceFile)
            {
                var thisType = typeChecker.TryGetThisTypeAtEx(
                    scopeNode,
                    false, /*includeGlobalThis*/
                    IfElse(IsClassLike(scopeNode.Parent), scopeNode, (Node?)null));
                if (thisType is not null && !IsProbablyGlobalType(thisType, file, typeChecker))
                {
                    foreach (var symbol in GetPropertiesForCompletion(thisType, typeChecker))
                    {
                        var symbolId = GetSymbolId(symbol);
                        symbols.Add(symbol);
                        symbolToOriginInfoMap[symbols.Count - 1] = new SymbolOriginInfo { _kind = SymbolOriginInfoKind.ThisType };
                        symbolToSortTextMap[symbolId] = SortTextSuggestedClassMembers;
                    }
                }
            }

            var collectAutoImportsError = CollectAutoImports();
            if (collectAutoImportsError is not null)
            {
                return (GlobalsSearch.Fail, collectAutoImportsError);
            }

            if (isTypeOnlyLocation)
            {
                if (contextToken is not null && IsAssertionExpression(contextToken.Parent!))
                {
                    keywordFilters = KeywordCompletionFilters.TypeAssertionKeywords;
                }
                else
                {
                    keywordFilters = KeywordCompletionFilters.TypeKeywords;
                }
            }

            return (GlobalsSearch.Success, null);
        }

        (bool, Exception?) TryGetGlobalSymbols()
        {
            var result = GlobalsSearch.Continue;
            Exception? err = null;
            Func<(GlobalsSearch, Exception?)>[] globalSearchFuncs =
            [
                TryGetObjectTypeLiteralInTypeArgumentCompletionSymbols,
                TryGetObjectLikeCompletionSymbols,
                TryGetImportCompletionSymbols,
                TryGetImportOrExportClauseCompletionSymbols,
                TryGetImportAttributesCompletionSymbols,
                TryGetLocalNamedExportCompletionSymbols,
                TryGetConstructorCompletion,
                TryGetClassLikeCompletionSymbols,
                TryGetJsxCompletionSymbols,
                GetGlobalCompletions,
            ];
            foreach (var globalSearchFunc in globalSearchFuncs)
            {
                (result, err) = globalSearchFunc();
                if (err is not null)
                {
                    return (false, err);
                }

                if (result != GlobalsSearch.Continue)
                {
                    break;
                }
            }

            return (result == GlobalsSearch.Success, null);
        }

        if (isRightOfDot || isRightOfQuestionDot)
        {
            GetTypeScriptMemberSymbols();
        }
        else if (isRightOfOpenTag)
        {
            symbols.Clear();
            symbols.AddRange(typeChecker.GetJsxIntrinsicTagNamesAt(location));
            CheckEachDefined(symbols.ToArray(), "GetJsxIntrinsicTagNamesAt() should all be defined");
            var (_, jsxIntrinsicError) = TryGetGlobalSymbols();
            if (jsxIntrinsicError is not null)
            {
                return (null, jsxIntrinsicError);
            }

            completionKind = CompletionKind.Global;
            keywordFilters = KeywordCompletionFilters.None;
        }
        else if (isStartingCloseTag)
        {
            var tagName = contextToken!.Parent!.Parent!.AsJsxElement()!.OpeningElement!.TagName()!;
            var tagSymbol = typeChecker.GetSymbolAtLocation(tagName);
            symbols.Clear();
            if (tagSymbol is not null)
            {
                symbols.Add(tagSymbol);
            }

            completionKind = CompletionKind.Global;
            keywordFilters = KeywordCompletionFilters.None;
        }
        else
        {
            // 对 JavaScript 或 TypeScript，如果我们不是在点号之后，就尝试取得作用域内的全局符号。
            // 这些结果对两种语言都有效，因为可以从该位置引用的符号集合是一样的。
            var (ok, err) = TryGetGlobalSymbols();
            if (!ok)
            {
                if (err is not null)
                {
                    return (null, err);
                }

                if (keywordFilters != KeywordCompletionFilters.None)
                {
                    return (KeywordCompletionData(keywordFilters, isJSOnlyLocation, isNewIdentifierLocation), null);
                }

                return (null, null);
            }
        }

        TypeScript.Checker.Type? contextualTypeOrConstraint = null;
        if (previousToken is not null)
        {
            contextualTypeOrConstraint = GetContextualType(previousToken, position, file, typeChecker);
            if (contextualTypeOrConstraint is null)
            {
                contextualTypeOrConstraint = GetConstraintOfTypeArgumentProperty(previousToken, typeChecker);
            }
        }

        // 排除 `<input type="text" [||] />` 之后的字面量建议（microsoft/TypeScript#51667）以及闭合引号之后（microsoft/TypeScript#52675）
        // 字符串的补全由 GetStringLiteralCompletions 处理
        var isLiteralExpected = !(previousToken is not null && IsStringLiteralLike(previousToken)) && !isJsxIdentifierExpected;
        object[] literals = [];
        if (isLiteralExpected)
        {
            TypeScript.Checker.Type[] types = [];
            if (contextualTypeOrConstraint is not null && contextualTypeOrConstraint.IsUnion())
            {
                types = contextualTypeOrConstraint.Types();
            }
            else if (contextualTypeOrConstraint is not null)
            {
                types = [contextualTypeOrConstraint];
            }

            literals = MapNonNil<TypeScript.Checker.Type, object>(types, t => IsLiteral(t) && !t.IsEnumLiteral() ? t.AsLiteralType()!.Value()! : null!);
        }

        Ast.Symbol? recommendedCompletion = null;
        if (previousToken is not null && contextualTypeOrConstraint is not null)
        {
            recommendedCompletion = GetRecommendedCompletion(previousToken, contextualTypeOrConstraint, typeChecker);
        }

        if (defaultCommitCharacters is null)
        {
            defaultCommitCharacters = GetDefaultCommitCharacters(isNewIdentifierLocation);
        }

        return (new CompletionDataData
        {
            _symbols = symbols.ToArray(),
            _autoImports = autoImports,
            _completionKind = completionKind,
            _isInSnippetScope = isInSnippetScope,
            _propertyAccessToConvert = propertyAccessToConvert,
            _isNewIdentifierLocation = isNewIdentifierLocation,
            _location = location,
            _keywordFilters = keywordFilters,
            _literals = literals,
            _symbolToOriginInfoMap = symbolToOriginInfoMap,
            _symbolToSortTextMap = symbolToSortTextMap,
            _recommendedCompletion = recommendedCompletion,
            _previousToken = previousToken,
            _contextToken = contextToken,
            _jsxInitializer = jsxInitializer,
            _insideJSDocTagTypeExpression = insideJSDocTagTypeExpression,
            _isTypeOnlyLocation = isTypeOnlyLocation,
            _isJsxIdentifierExpected = isJsxIdentifierExpected,
            _isRightOfOpenTag = isRightOfOpenTag,
            _isRightOfDotOrQuestionDot = isRightOfDot || isRightOfQuestionDot,
            _importStatementCompletion = importStatementCompletion,
            _hasUnresolvedAutoImports = hasUnresolvedAutoImports,
            _defaultCommitCharacters = defaultCommitCharacters,
        }, null);
    }
}
