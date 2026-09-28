// ported from: internal/fourslash/baselineutil.go:1-803
//
// 与 Go 的偏离（逐条）：
// 1) `type baselineCommand string`（命名 string 类型；值是 `"def"`/`"rename"` 这类小写串，且 `normalizeCommandName`
//    接受任意 string）→ `public readonly struct BaselineCommand`：20 个常量名照抄 Go（去掉 `Cmd` 后缀），值逐字节
//    照抄；双向 implicit 转换 string ⇔ BaselineCommand，于是 Go 的 `f.addResultToBaseline(t, "workspaceSymbol", …)`
//    （裸字符串）与 `writeToBaseline(codeLensesCmd, …)` 在 C# 里都照旧。值相等（Equals/GetHashCode/==）供
//    `Dictionary<BaselineCommand, StringBuilder>`（`FourslashTest._baselines`）当键用。
//    偏离：Go 的 `switch command { case quickInfoCmd, … }` → `==` 链（结构体不能做 switch 常量模式）。
// 2) 包级函数 → `public static partial class BaselineUtil`（docs/architecture.md §2）：`GetBaselineExtension` /
//    `NormalizeCommandName` / `DropTrailingEmptyLines` / `GetAccessibleFilePaths` / `UniqueFilesInSpanOrder` /
//    `LocationToSpan` / `NewTextWithContext` / `CodeFence` / `SymbolInformationToData` / `LineSplitter`。
//    例外（按 FsCore 的接口冻结表）：`getBaselineFileName` → `FourslashTest` 的 `internal static`，
//    `getBaselineOptions` → `FourslashTest` 的实例方法（Go 里后者本来就是 `func (f *FourslashTest)`）。
//    `getBaseFileNameFromTest`/`isLibFile` 虽然是 fourslash.go 的包级函数，但按冻结表归 FsCore 的
//    `FourslashTest.GetBaseFileNameFromTest`/`IsLibFile`，本文件**不**重复定义，只调用。
// 3) 未导出类型 → PascalCase 顶层类型：`documentSpan` → `DocumentSpan`、`baselineFourslashLocationsOptions` →
//    `BaselineFourslashLocationsOptions`、`detailKind` → `DetailKind`、`baselineDetail` → `BaselineDetail`、
//    `textWithContext` → `TextWithContext`、`markerAndItem[T]` → `MarkerAndItem<T>`。`documentSpan`/`baselineDetail`
//    被 fourslash.go 跨文件构造，因此除 `DetailKind`（internal 枚举）外都是 internal/public 顶层类型。
// 4) `detailKind.isEnd()/isStart()` → `DetailKindExtensions` 的扩展方法（C# 枚举不能带方法）。
// 5) **值类型 vs 引用类型**：Go 的 `lsproto.Position`/`lsproto.Range` 是**值**（`==` 是逐字段比较），C# 侧
//    `Position`/`Range` 是**类**，所以 Go 里所有 `pos1 == pos2` 一律写成 `ComparePositions(pos1, pos2) == 0`
//    （含 `t.posInfo == nil || *t.posInfo != t.pos`）。`new Range { Start = pos, End = pos }` 的两个字段在 C# 里
//    指向同一对象（Go 是两份拷贝）——该 Range 只被只读访问，无行为差异。
// 6) `documentSpan` 作为 `map[documentSpan]int` 的键：Go 的 struct 键按**值**比较（其中 `contextSpan *Range` 按
//    **指针**），故 `DocumentSpan` 显式实现 `Equals`（uri + textSpan 逐字段 + contextSpan 引用相等）。
//    排序比较器里的 `d1.span == d2.span` 在 Go 是**指针**比较 → 必须用 `ReferenceEquals`（不能用 `==`，
//    否则会被上面的 Equals 接管）。
// 7) `slices.SortStableFunc` → LINQ `OrderBy(…, Comparer<T>.Create(…))`（稳定排序，与 Go 一致）；`slices.Clone`
//    省掉（OrderBy 不改原数组）；`slices.Insert` → `List<T>.InsertRange`；`slices.SortStableFunc` 里
//    `int(d1.kind - d2.kind)` → `(int)a.Kind - (int)b.Kind`。
// 8) `strings.Fields` → `Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)`（.NET 的 char.IsWhiteSpace
//    与 Go 的 unicode.IsSpace 覆盖同一组码点）；`strings.Repeat(s, n)` → `new string(c, n)`；
//    `strings.TrimPrefix(name, "Test")` → StartsWith + 切片；`core.LastOrNil(strings.Split(name, "/"))` →
//    `parts[^1]`（strings.Split 至少返回一个元素，LastOrNil 的 nil 分支不可达）。
// 9) `vfs.WalkDir` 在 C# 侧把 error 变成**异常**（Vfs/WalkDir.cs），所以 Go 的 `if err != nil { panic(...) }`
//    在 C# 里没有对应语句（错误自然上抛）；回调里仍然返回 `WalkDirResult.FromError(err)` 保持 Go 的形状。
//    Go 的 `entry.Type().IsRegular()` → `!entry.IsDirectory && !entry.IsSymlink`（DirEntry 只带这两个模式位）。
// 10) `fmt.Sprintf("…%v…", n)`（int）→ `ToString(CultureInfo.InvariantCulture)`/插值；`t.Fatalf("…got %v", r)`
//    里的 `%v`（`*lsproto.Range` 指针）→ `FormatRange` 复刻 Go 的 `&{{1 2} {3 4}}`（Go 的 `%v` 不打印字段名）。
//    该文本只在失败路径出现，不进 baseline。
// 11) `regexp.MustCompile(`\r?\n`)` → `new Regex("\\r?\\n")`（不加 RegexOptions.Compiled：AOT）。
// 12) `textWithContext` 只实现 `lsconv.Script`（FileName/OriginalFileName/Text/OriginalText/SpanMap）。Go 侧它
//    **不**实现 `diagnosticwriter.FileLike`（缺 ECMALineMap），本文件同样不实现。
// 13) `newTestConverters` → 冻结表里的 `FourslashTest.NewTestConverters(Converters)`；`getIndex(any)` 的类型分支
//    只保留调用点实际用到的三种（int / core.TextPos / lsproto.Position），Go 的指针分支不可达。
// 14) `annotateContentWithTooltips[T comparable]` → **无约束**泛型 `T`：Go 的 comparable 只用于 `item != *new(T)`，
//    对应 `!EqualityComparer<T>.Default.Equals(item, default)`；加 `where T : class` 会拒绝调用点实际存在的
//    `T = X?`（CS8634）。`var previous T`（零值 = nil 指针）→ `T? previous = default`。
// 15) `markerAndItem[T]` 的 json tag `marker`/`item` → `MarkerAndItem<T> : IJsonMarshaler` 手写 `MarshalJSONTo`
//    （C# 无反射序列化，docs/json.md §2）；调用点 Go 的 `core.StringifyJson(v, "", "  ")` 对应 C# 的
//    `Json.MarshalIndent(v, "", "  ")`。
// 16) `core.OrElse(options.endMarker, "|]")` → `options.EndMarker ?? "|]"`；`core.Map` → `Core.Map`。
// 17) 可空：`[]T`（nil 切片）→ `T[]?` + 使用处 `?? []`；`*T` → `T?`。`MarkerAndItem<T>[]?` 同理（Go 的
//    `var markersAndItems []markerAndItem[T]` 可能是 nil）。
// 18) `d1.kind == detailKindMarker` 等枚举比较照抄；`DetailKind` 的取值顺序与 Go iota 一致（Marker=0 … ContextEnd=4），
//    因为 Go 用 `int(d1.kind - d2.kind)` 排序。

using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using TypeScript.Collections;
using TypeScript.Core;
using TypeScript.LsConv;
using TypeScript.LsProto;
using TypeScript.TestUtil;
using TypeScript.TestUtil.Baseline;
using TypeScript.Vfs;
using static TypeScript.Bundled.Bundled;
using static TypeScript.Collections.Collections;
using static TypeScript.Core.Core;
using static TypeScript.Json.Json;
using static TypeScript.LsConv.LsConv;
using static TypeScript.LsProto.LsProto;
using static TypeScript.StringUtil.StringUtil;
using static TypeScript.TSPath.TSPath;
using static TypeScript.Vfs.Vfs;
using JsonEncoder = TypeScript.Json.Encoder;
using Range = TypeScript.LsProto.Range;

namespace TypeScript.Harness.Fourslash;

/// <summary>
/// Go: <c>type baselineCommand string</c>（baselineutil.go:29）。baseline 命令名（Go 里是命名 string 类型，
/// 值就是命令名本身，见 <see cref="All"/>）。
/// </summary>
public readonly struct BaselineCommand : IEquatable<BaselineCommand>
{
    private readonly string? _value;

    private BaselineCommand(string value) => _value = value;

    /// <summary>Go 的 <c>string(command)</c>。</summary>
    public string Value => _value ?? "";

    /// <summary>Go 的 <c>baselineCommand("workspaceSymbol")</c>（裸字符串转命名类型）。</summary>
    public static BaselineCommand From(string value) => new(value);

    public static readonly BaselineCommand AutoImports = new("Auto Imports");
    public static readonly BaselineCommand CallHierarchy = new("Call Hierarchy");
    public static readonly BaselineCommand ClosingTag = new("Closing Tag");
    public static readonly BaselineCommand DocumentHighlights = new("documentHighlights");
    public static readonly BaselineCommand FindAllReferences = new("findAllReferences");
    public static readonly BaselineCommand VsFindAllReferences = new("vsFindAllReferences");
    public static readonly BaselineCommand GoToDefinition = new("goToDefinition");
    public static readonly BaselineCommand GoToImplementation = new("goToImplementation");
    public static readonly BaselineCommand GoToSourceDefinition = new("goToSourceDefinition");
    public static readonly BaselineCommand GoToTypeDefinition = new("goToType");
    public static readonly BaselineCommand InlayHints = new("Inlay Hints");
    public static readonly BaselineCommand NonSuggestionDiagnostics = new("Syntax and Semantic Diagnostics");
    public static readonly BaselineCommand QuickInfo = new("QuickInfo");
    public static readonly BaselineCommand VsQuickInfo = new("VSQuickInfo");
    public static readonly BaselineCommand LinkedEditing = new("linkedEditing");
    public static readonly BaselineCommand Rename = new("findRenameLocations");
    public static readonly BaselineCommand SignatureHelp = new("SignatureHelp");
    public static readonly BaselineCommand SmartSelection = new("Smart Selection");
    public static readonly BaselineCommand CodeLenses = new("Code Lenses");
    public static readonly BaselineCommand DocumentSymbols = new("Document Symbols");

    /// <summary>Go 的 20 个 <c>baselineCommand</c> 常量，声明顺序与 Go 常量块一致。</summary>
    public static readonly BaselineCommand[] All =
    [
        AutoImports,
        CallHierarchy,
        ClosingTag,
        DocumentHighlights,
        FindAllReferences,
        VsFindAllReferences,
        GoToDefinition,
        GoToImplementation,
        GoToSourceDefinition,
        GoToTypeDefinition,
        InlayHints,
        NonSuggestionDiagnostics,
        QuickInfo,
        VsQuickInfo,
        LinkedEditing,
        Rename,
        SignatureHelp,
        SmartSelection,
        CodeLenses,
        DocumentSymbols,
    ];

    public static implicit operator string(BaselineCommand command) => command.Value;

    public static implicit operator BaselineCommand(string value) => new(value);

    public bool Equals(BaselineCommand other) => string.Equals(Value, other.Value, StringComparison.Ordinal);

    public override bool Equals(object? obj) => obj is BaselineCommand other && Equals(other);

    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Value);

    public static bool operator ==(BaselineCommand left, BaselineCommand right) => left.Equals(right);

    public static bool operator !=(BaselineCommand left, BaselineCommand right) => !left.Equals(right);

    public override string ToString() => Value;
}

/// <summary>
/// Go: <c>type baselineFourslashLocationsOptions struct</c>（baselineutil.go:112）。字段名 = Go 字段名的
/// PascalCase，全部 public 可写（Go 的字面量 + 字段赋值 → C# 对象初始化器）。
/// </summary>
public sealed class BaselineFourslashLocationsOptions
{
    /// <summary>Go: <c>marker MarkerOrRange</c>（location）。</summary>
    public MarkerOrRange? Marker;

    /// <summary>Go: <c>markerName string</c>（要写进 baseline 的名字）。</summary>
    public string MarkerName = "";

    /// <summary>Go: <c>endMarker string</c>。</summary>
    public string? EndMarker;

    /// <summary>Go: <c>startMarkerPrefix func(span documentSpan) *string</c>。</summary>
    public Func<DocumentSpan, string?>? StartMarkerPrefix;

    /// <summary>Go: <c>endMarkerSuffix func(span documentSpan) *string</c>。</summary>
    public Func<DocumentSpan, string?>? EndMarkerSuffix;

    /// <summary>Go: <c>getLocationData func(span documentSpan) string</c>。</summary>
    public Func<DocumentSpan, string>? GetLocationData;

    /// <summary>Go: <c>additionalSpan *documentSpan</c>。</summary>
    public DocumentSpan? AdditionalSpan;

    /// <summary>Go: <c>preserveResultOrder bool</c>。</summary>
    public bool PreserveResultOrder;

    /// <summary>Go: <c>orderedFiles []lsproto.DocumentUri</c>（由 <see cref="FourslashTest.GetBaselineForSpansWithFileContents"/> 填）。</summary>
    public DocumentUri[]? OrderedFiles;
}

/// <summary>
/// Go: <c>type documentSpan struct</c>（baselineutil.go:107）。Go 里是值类型（会被拷贝进 `map[documentSpan]int`
/// 当键），C# 里用引用类型 + 显式值相等（<see cref="Equals(DocumentSpan)"/>）复刻键语义。
/// </summary>
public sealed class DocumentSpan : IEquatable<DocumentSpan>
{
    public DocumentUri Uri;
    public Range TextSpan = new();
    public Range? ContextSpan;

    /// <summary>
    /// Go 的 <c>documentSpan</c> 作为 map 键时的值相等：uri 与 textSpan 逐字段比，contextSpan 按**指针**比
    /// （Go 的 `*lsproto.Range` 字段）。
    /// </summary>
    public bool Equals(DocumentSpan? other)
        => other != null
        && Uri == other.Uri
        && ComparePositions(TextSpan.Start, other.TextSpan.Start) == 0
        && ComparePositions(TextSpan.End, other.TextSpan.End) == 0
        && ReferenceEquals(ContextSpan, other.ContextSpan);

    public override bool Equals(object? obj) => obj is DocumentSpan other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(
        Uri,
        TextSpan.Start.Line,
        TextSpan.Start.Character,
        TextSpan.End.Line,
        TextSpan.End.Character,
        ContextSpan == null ? 0 : System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(ContextSpan));
}

/// <summary>Go: <c>type detailKind int</c>（baselineutil.go:275）。取值顺序必须与 Go 的 iota 一致。</summary>
internal enum DetailKind
{
    /// <summary>Go: <c>detailKindMarker</c>（/*MARKER*/）。</summary>
    Marker = 0,

    /// <summary>Go: <c>detailKindContextStart</c>（&lt;|）。</summary>
    ContextStart = 1,

    /// <summary>Go: <c>detailKindTextStart</c>（[|）。</summary>
    TextStart = 2,

    /// <summary>Go: <c>detailKindTextEnd</c>（|]）。</summary>
    TextEnd = 3,

    /// <summary>Go: <c>detailKindContextEnd</c>（|&gt;）。</summary>
    ContextEnd = 4,
}

/// <summary>Go 的 <c>detailKind.isEnd()/isStart()</c>（C# 枚举不能带方法 → 扩展方法）。</summary>
internal static class DetailKindExtensions
{
    /// <summary>Go: <c>func (k detailKind) isEnd() bool</c>（baselineutil.go:283）。</summary>
    internal static bool IsEnd(this DetailKind kind) => kind is DetailKind.ContextEnd or DetailKind.TextEnd;

    /// <summary>Go: <c>func (k detailKind) isStart() bool</c>（baselineutil.go:287）。</summary>
    internal static bool IsStart(this DetailKind kind) => kind is DetailKind.ContextStart or DetailKind.TextStart;
}

/// <summary>
/// Go: <c>type baselineDetail struct</c>（baselineutil.go:292）。也被 fourslash.go 的
/// VerifyBaselineLinkedEditing 直接构造（只填 pos/positionMarker），所以是 internal 顶层类型。
/// </summary>
internal sealed class BaselineDetail
{
    public Position Pos = new();
    public string PositionMarker = "";
    public DocumentSpan? Span;
    public DetailKind Kind;

    /// <summary>Go: <c>func (d *baselineDetail) getRange() lsproto.Range</c>（baselineutil.go:299）。</summary>
    public Range GetRange()
    {
        switch (Kind)
        {
            case DetailKind.ContextStart:
            case DetailKind.ContextEnd:
                return Span!.ContextSpan!;
            case DetailKind.TextStart:
            case DetailKind.TextEnd:
                return Span!.TextSpan;
            case DetailKind.Marker:
                // Go 写的是 `lsproto.Range{Start: d.pos, End: d.pos}`（两份值拷贝）。
                return new Range { Start = Pos, End = Pos };
            default:
                Debug.Fail("unknown detail kind");
                throw new System.Diagnostics.UnreachableException();
        }
    }
}

/// <summary>
/// Go: <c>type markerAndItem[T any] struct</c>（baselineutil.go:666）。Go 的 json tag 是 `marker`/`item`
/// （顺序也是 marker 在前），C# 侧手写 <see cref="MarshalJSONTo"/>（无反射序列化）。
/// </summary>
public sealed class MarkerAndItem<T> : TypeScript.Json.IJsonMarshaler
{
    public Marker Marker = null!;
    public T Item = default!;

    public void MarshalJSONTo(JsonEncoder encoder)
    {
        encoder.WriteToken(BeginObject);
        MarshalEncode(encoder, "marker");
        MarshalEncode(encoder, Marker);
        MarshalEncode(encoder, "item");
        MarshalEncode(encoder, Item);
        encoder.WriteToken(EndObject);
    }
}

/// <summary>
/// Go: <c>type textWithContext struct</c>（baselineutil.go:531）。实现 <c>lsconv.Script</c>（Go 侧同样只实现这个接口）。
/// </summary>
internal sealed class TextWithContext : Script
{
    /// <summary>Go: <c>nLinesContext</c>（要写进 baseline 的上下文行数）。</summary>
    internal int NLinesContext;

    /// <summary>Go: <c>readableContents</c>（要写进 baseline 的最终内容）。</summary>
    internal readonly StringBuilder ReadableContents = new();

    /// <summary>Go: <c>newContent</c>（details 之间的原文片段）。</summary>
    internal readonly StringBuilder NewContent = new();

    /// <summary>Go: <c>pos</c>。</summary>
    internal Position Pos = new();

    /// <summary>Go: <c>isLibFile</c>。</summary>
    internal bool IsLibFile;

    /// <summary>Go: <c>lineStarts</c>。</summary>
    internal LSPLineMap LineStarts;

    /// <summary>Go: <c>converters</c>。</summary>
    internal TestConverters Converters;

    /// <summary>Go: <c>posInfo</c>（上一次 add 的位置，null 表示还没有）。</summary>
    private Position? _posInfo;

    /// <summary>Go: <c>lineInfo</c>。</summary>
    private int _lineInfo;

    private readonly string _fileName;
    private readonly string _content;

    internal TextWithContext(string fileName, string content)
    {
        _fileName = fileName;
        _content = content;
        NLinesContext = 4;
        IsLibFile = FourslashTest.IsLibFile(fileName);
        LineStarts = ComputeLSPLineStarts(content);
        Converters = FourslashTest.NewTestConverters(NewConverters(PositionEncodingKind.UTF8, _ => LineStarts));
        ReadableContents.Append("// === ");
        ReadableContents.Append(fileName);
        ReadableContents.Append(" ===");
    }

    /// <summary>Go: <c>func (t *textWithContext) FileName() string</c>（实现 lsconv.Script）。</summary>
    public string FileName() => _fileName;

    /// <summary>Go: <c>func (t *textWithContext) OriginalFileName() string</c>（实现 lsconv.Script）。</summary>
    public string OriginalFileName() => _fileName;

    /// <summary>Go: <c>func (t *textWithContext) Text() string</c>（实现 lsconv.Script）。</summary>
    public string Text() => _content;

    /// <summary>Go: <c>func (t *textWithContext) OriginalText() string</c>（实现 lsconv.Script）。</summary>
    public string OriginalText() => _content;

    /// <summary>Go: <c>func (t *textWithContext) SpanMap() *spanmap.SpanMap</c>（实现 lsconv.Script）。</summary>
    public TypeScript.SpanMap.SpanMap? SpanMap() => null;

    /// <summary>Go: <c>func (t *textWithContext) readableJsoncBaseline(text string)</c>（baselineutil.go:658）。</summary>
    internal void ReadableJsoncBaseline(string text)
    {
        var lines = BaselineUtil.LineSplitter.Split(text);
        for (var i = 0; i < lines.Length; i++)
        {
            if (i > 0)
            {
                ReadableContents.Append("\n");
            }

            ReadableContents.Append("// ");
            ReadableContents.Append(lines[i]);
        }
    }

    /// <summary>Go: <c>func (t *textWithContext) add(detail *baselineDetail)</c>（baselineutil.go:594）。</summary>
    internal void Add(BaselineDetail? detail)
    {
        if (NewContent.Length == 0 && detail == null)
        {
            Debug.Fail("Unsupported");
            throw new System.Diagnostics.UnreachableException();
        }

        if (detail == null || (detail.Kind != DetailKind.TextEnd && detail.Kind != DetailKind.ContextEnd))
        {
            // 计算 pos 到 location 的行号
            var posLineIndex = _lineInfo;
            if (_posInfo == null || ComparePositions(_posInfo, Pos) != 0)
            {
                posLineIndex = LineStarts.ComputeIndexOfLineStart(Converters.LineAndCharacterToPosition(this, Pos));
            }

            var locationLineIndex = LineStarts.LineStarts.Length - 1;
            if (detail != null)
            {
                locationLineIndex = LineStarts.ComputeIndexOfLineStart(Converters.LineAndCharacterToPosition(this, detail.Pos));
                _posInfo = detail.Pos;
                _lineInfo = locationLineIndex;
            }

            var nLines = 0;
            if (NewContent.Length != 0)
            {
                nLines += NLinesContext + 1;
            }

            if (detail != null)
            {
                nLines += NLinesContext + 1;
            }

            // 首尾各 nLinesContext 行
            if (locationLineIndex - posLineIndex > nLines)
            {
                if (NewContent.Length != 0)
                {
                    string skippedString;
                    if (IsLibFile)
                    {
                        skippedString = "--- (line: --) skipped ---\n";
                    }
                    else
                    {
                        skippedString = "--- (line: " + (posLineIndex + NLinesContext + 1).ToString(CultureInfo.InvariantCulture) + ") skipped ---";
                    }

                    ReadableContents.Append("\n");
                    ReadableJsoncBaseline(NewContent.ToString() + SliceOfContent(
                        GetIndex(Pos),
                        GetIndex(LineStarts.LineStarts[posLineIndex + NLinesContext])) + skippedString);

                    if (detail != null)
                    {
                        ReadableContents.Append("\n");
                    }

                    NewContent.Clear();
                }

                if (detail != null)
                {
                    if (IsLibFile)
                    {
                        NewContent.Append("--- (line: --) skipped ---\n");
                    }
                    else
                    {
                        NewContent.Append("--- (line: " + (locationLineIndex - NLinesContext + 1).ToString(CultureInfo.InvariantCulture) + ") skipped ---\n");
                    }

                    NewContent.Append(SliceOfContent(
                        GetIndex(LineStarts.LineStarts[locationLineIndex - NLinesContext + 1]),
                        GetIndex(detail.Pos)));
                }

                return;
            }
        }

        if (detail == null)
        {
            NewContent.Append(SliceOfContent(GetIndex(Pos), null));
        }
        else
        {
            NewContent.Append(SliceOfContent(GetIndex(Pos), GetIndex(detail.Pos)));
        }
    }

    /// <summary>
    /// Go: <c>func (t *textWithContext) sliceOfContent(start *int, end *int) string</c>（baselineutil.go:755）。
    /// </summary>
    internal string SliceOfContent(int? start, int? end)
    {
        var s = start ?? 0;
        if (s < 0)
        {
            s = 0;
        }

        var e = end ?? _content.Length;
        if (e > _content.Length)
        {
            e = _content.Length;
        }

        if (s > e)
        {
            return "";
        }

        return _content[s..e];
    }

    /// <summary>
    /// Go: <c>func (t *textWithContext) getIndex(i any) *int</c>（baselineutil.go:769）。只保留调用点用到的三种
    /// 形态（int / core.TextPos / lsproto.Position）；Go 的指针分支在本文件里不可达。
    /// </summary>
    internal int? GetIndex(int i) => i;

    /// <summary>Go 的 <c>case core.TextPos</c>/<c>case int</c>。</summary>
    internal int? GetIndex(TextPos i) => (int)i;

    /// <summary>Go 的 <c>case lsproto.Position</c>：先换算成原文偏移。</summary>
    internal int? GetIndex(Position i) => GetIndex(Converters.LineAndCharacterToPosition(this, i));
}

/// <summary>Go: <c>package fourslash</c> 里 baselineutil.go 的包级函数（docs/architecture.md §2）。</summary>
public static partial class BaselineUtil
{
    /// <summary>Go: <c>var lineSplitter = regexp.MustCompile(`\r?\n`)</c>（baselineutil.go:529）。</summary>
    internal static readonly Regex LineSplitter = new("\\r?\\n");

    /// <summary>Go: <c>func normalizeCommandName(command string) string</c>（baselineutil.go:102）。</summary>
    public static string NormalizeCommandName(string command)
    {
        var words = Fields(command);
        return LowerFirstChar(string.Concat(words));
    }

    /// <summary>Go: <c>func getBaselineExtension(command baselineCommand) string</c>（baselineutil.go:82）。</summary>
    public static string GetBaselineExtension(BaselineCommand command)
    {
        // Go 的 switch 是按命名 string 的值比较 → 这里用 == 链（常量名逐一对应）。
        if (command == BaselineCommand.QuickInfo
            || command == BaselineCommand.VsQuickInfo
            || command == BaselineCommand.SignatureHelp
            || command == BaselineCommand.SmartSelection
            || command == BaselineCommand.InlayHints
            || command == BaselineCommand.NonSuggestionDiagnostics
            || command == BaselineCommand.DocumentSymbols
            || command == BaselineCommand.ClosingTag
            || command == BaselineCommand.VsFindAllReferences)
        {
            return "baseline";
        }

        if (command == BaselineCommand.CallHierarchy)
        {
            return "callHierarchy.txt";
        }

        if (command == BaselineCommand.AutoImports)
        {
            return "baseline.md";
        }

        if (command == BaselineCommand.LinkedEditing)
        {
            return "linkedEditing.txt";
        }

        return "baseline.jsonc";
    }

    /// <summary>Go: <c>func dropTrailingEmptyLines(ss []string) []string</c>（baselineutil.go:98）。</summary>
    public static string[] DropTrailingEmptyLines(string[] ss)
        => ss[..(FindLastIndex(ss, s => s != "") + 1)];

    /// <summary>Go: <c>func locationToSpan(loc lsproto.Location) documentSpan</c>（baselineutil.go:126）。</summary>
    public static DocumentSpan LocationToSpan(Location loc) => new() { Uri = loc.Uri, TextSpan = loc.Range };

    /// <summary>Go: <c>func getAccessibleFilePaths(fileSystem vfs.FS, root string) []string</c>（baselineutil.go:229）。</summary>
    public static string[]? GetAccessibleFilePaths(FS fileSystem, string root)
    {
        if (!fileSystem.DirectoryExists(root))
        {
            return null;
        }

        var files = new List<string>();
        // Go 的 `if err != nil { panic("walkdir error during fourslash baseline: " + err.Error()) }`：C# 侧
        // WalkDir 直接把错误抛出来（Vfs/WalkDir.cs），所以这里没有对应的 panic。
        WalkDir(fileSystem, root, (path, entry, err) =>
        {
            if (err != null)
            {
                return WalkDirResult.FromError(err);
            }

            // Go: entry.Type().IsRegular()
            if (entry != null && !entry.IsDirectory && !entry.IsSymlink)
            {
                files.Add(path);
            }

            return WalkDirResult.Ok;
        });
        return files.ToArray();
    }

    /// <summary>Go: <c>func uniqueFilesInSpanOrder(spans []documentSpan) []lsproto.DocumentUri</c>（baselineutil.go:247）。</summary>
    public static DocumentUri[]? UniqueFilesInSpanOrder(DocumentSpan[] spans)
    {
        if (spans.Length == 0)
        {
            return null;
        }

        var seen = new HashSet<DocumentUri>();
        var result = new List<DocumentUri>(spans.Length);
        foreach (var span in spans)
        {
            if (!seen.Add(span.Uri))
            {
                continue;
            }

            result.Add(span.Uri);
        }

        return result.ToArray();
    }

    /// <summary>Go: <c>func codeFence(lang string, code string) string</c>（baselineutil.go:787）。</summary>
    public static string CodeFence(string lang, string code) => "```" + lang + "\n" + code + "\n```";

    /// <summary>Go: <c>func symbolInformationToData(symbol *lsproto.SymbolInformation) string</c>（baselineutil.go:791）。</summary>
    public static string SymbolInformationToData(SymbolInformation symbol)
        => "{| name: " + symbol.Name + ", kind: " + symbol.Kind.String() + " |}";

    /// <summary>Go: <c>func newTextWithContext(fileName string, content string) *textWithContext</c>（baselineutil.go:562）。</summary>
    internal static TextWithContext NewTextWithContext(string fileName, string content) => new(fileName, content);

    /// <summary>Go 的 <c>strings.Fields</c>：按 Unicode 空白切分并丢掉空串。</summary>
    private static string[] Fields(string s) => s.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
}

/// <summary>Go: <c>func (f *FourslashTest)</c> 的 baselineutil.go 部分。</summary>
public sealed partial class FourslashTest
{
    /// <summary>Go: <c>func (f *FourslashTest) addResultToBaseline(t *testing.T, command baselineCommand, actual string)</c>（baselineutil.go:47）。</summary>
    internal void AddResultToBaseline(IHarnessTest t, BaselineCommand command, string actual)
    {
        StringBuilder b;
        if (_testData!.IsStateBaseliningEnabled())
        {
            // 所有命令共用一个 baseline
            b = _stateBaseline!._baseline;
        }
        else if (_baselines.TryGetValue(command, out var builder))
        {
            b = builder;
        }
        else
        {
            _baselines[command] = new StringBuilder();
            b = _baselines[command];
        }

        if (b.Length != 0)
        {
            b.Append("\n\n\n\n");
        }

        b.Append("// === ");
        b.Append(command.Value);
        b.Append(" ===\n");
        b.Append(actual);
    }

    /// <summary>Go: <c>func (f *FourslashTest) writeToBaseline(command baselineCommand, content string)</c>（baselineutil.go:68）。</summary>
    internal void WriteToBaseline(BaselineCommand command, string content)
    {
        if (!_baselines.TryGetValue(command, out var b))
        {
            _baselines[command] = new StringBuilder();
            b = _baselines[command];
        }

        b.Append(content);
    }

    /// <summary>Go: <c>func getBaselineFileName(t *testing.T, command baselineCommand) string</c>（baselineutil.go:77）。</summary>
    internal static string GetBaselineFileName(IHarnessTest t, BaselineCommand command)
        => GetBaseFileNameFromTest(t) + "." + BaselineUtil.GetBaselineExtension(command);

    /// <summary>Go: <c>func (f *FourslashTest) getBaselineOptions(command baselineCommand, testPath string) baseline.Options</c>（baselineutil.go:93）。</summary>
    internal BaselineOptions GetBaselineOptions(BaselineCommand command, string testPath)
    {
        return new BaselineOptions
        {
            Subfolder = "fourslash/" + BaselineUtil.NormalizeCommandName(command.Value),
        };
    }

    /// <summary>Go: <c>func (f *FourslashTest) getBaselineForLocationsWithFileContents(locations []lsproto.Location, options baselineFourslashLocationsOptions) string</c>（baselineutil.go:131）。</summary>
    internal string GetBaselineForLocationsWithFileContents(Location[] locations, BaselineFourslashLocationsOptions options)
        => GetBaselineForSpansWithFileContents(Map(locations, BaselineUtil.LocationToSpan), options);

    /// <summary>Go: <c>func (f *FourslashTest) getBaselineForSpansWithFileContents(spans []documentSpan, options baselineFourslashLocationsOptions) string</c>（baselineutil.go:138）。</summary>
    internal string GetBaselineForSpansWithFileContents(DocumentSpan[] spans, BaselineFourslashLocationsOptions options)
    {
        var spansByFile = GroupBy(spans, span => span.Uri);
        if (options.PreserveResultOrder)
        {
            options.OrderedFiles = BaselineUtil.UniqueFilesInSpanOrder(spans);
        }

        return GetBaselineForGroupedSpansWithFileContents(spansByFile, options);
    }

    /// <summary>Go: <c>func (f *FourslashTest) getBaselineForGroupedSpansWithFileContents(groupedRanges *collections.MultiMap[lsproto.DocumentUri, documentSpan], options baselineFourslashLocationsOptions) string</c>（baselineutil.go:151）。</summary>
    internal string GetBaselineForGroupedSpansWithFileContents(
        MultiMap<DocumentUri, DocumentSpan> groupedRanges,
        BaselineFourslashLocationsOptions options)
    {
        // 必须总是打印包含 marker 的文件，但如果它已经出现在有 ranges 的文件里，就不要在末尾再打印一次。
        var foundMarker = false;
        var foundAdditionalLocation = false;
        var spanToContextId = new Dictionary<DocumentSpan, int>();

        var baselineEntries = new List<string>();
        void AddFileEntry(string path)
        {
            var fileName = FileNameToDocumentURI(path);
            var ranges = groupedRanges.Get(fileName);
            if (ranges == null || ranges.Count == 0)
            {
                return;
            }

            if (!TryTextOfFile(path, out var content))
            {
                return;
            }

            if (options.Marker != null && options.Marker.FileName() == path)
            {
                foundMarker = true;
            }

            if (options.AdditionalSpan != null && options.AdditionalSpan.Uri == fileName)
            {
                foundAdditionalLocation = true;
            }

            baselineEntries.Add(GetBaselineContentForFile(path, content, ranges.ToArray(), spanToContextId, options));
        }

        if (options.PreserveResultOrder)
        {
            foreach (var uri in options.OrderedFiles ?? [])
            {
                AddFileEntry(uri.FileName());
            }
        }
        else
        {
            foreach (var path in BaselineUtil.GetAccessibleFilePaths(_vfs, "/") ?? [])
            {
                AddFileEntry(path);
            }

            foreach (var path in BaselineUtil.GetAccessibleFilePaths(_vfs, LibPath()) ?? [])
            {
                AddFileEntry(path);
            }
        }

        // 在 Strada 里有个 bug：只有在还没有把文件加进 baseline 时，才会把 additional span 加进去。
        if (options.AdditionalSpan != null && !foundAdditionalLocation)
        {
            var fileName = options.AdditionalSpan.Uri.FileName();
            if (TryTextOfFile(fileName, out var content))
            {
                baselineEntries.Add(
                    GetBaselineContentForFile(fileName, content, [options.AdditionalSpan], spanToContextId, options));
                if (options.Marker != null && options.Marker.FileName() == fileName)
                {
                    foundMarker = true;
                }
            }
        }

        if (!foundMarker && options.Marker != null)
        {
            // 如果任何文件里都没找到 marker，就把它加进去。
            var markerFileName = options.Marker.FileName();
            if (TryTextOfFile(markerFileName, out var content))
            {
                baselineEntries.Add(GetBaselineContentForFile(markerFileName, content, null, spanToContextId, options));
            }
        }

        // !!! skipDocumentContainingOnlyMarker

        return string.Join("\n\n", baselineEntries);
    }

    /// <summary>Go: <c>func (f *FourslashTest) textOfFile(fileName string) (string, bool)</c>（baselineutil.go:266）。</summary>
    internal bool TryTextOfFile(string fileName, out string content)
    {
        if (_openFiles.Contains(fileName))
        {
            content = GetScriptInfo(fileName)!._content;
            return true;
        }

        return _vfs.TryReadFile(fileName, out content);
    }

    /// <summary>Go: <c>func (f *FourslashTest) getBaselineContentForFile(fileName string, content string, spansInFile []documentSpan, spanToContextId map[documentSpan]int, options baselineFourslashLocationsOptions) string</c>（baselineutil.go:307）。</summary>
    private string GetBaselineContentForFile(
        string fileName,
        string content,
        DocumentSpan[]? spansInFile,
        Dictionary<DocumentSpan, int> spanToContextId,
        BaselineFourslashLocationsOptions options)
    {
        var details = new List<BaselineDetail>();
        // Go: map[*baselineDetail]string —— 指针键 ⇒ C# 引用相等（BaselineDetail 没有重写 Equals）。
        var detailPrefixes = new Dictionary<BaselineDetail, string>();
        var detailSuffixes = new Dictionary<BaselineDetail, string>();
        var canDetermineContextIdInline = true;

        if (options.Marker != null && options.Marker.FileName() == fileName)
        {
            details.Add(new BaselineDetail { Pos = options.Marker.LSPos(), PositionMarker = options.MarkerName });
        }

        foreach (var span in spansInFile ?? [])
        {
            var contextSpanIndex = details.Count;

            // 有 context span 时补上它的标记
            if (span.ContextSpan != null)
            {
                details.Add(new BaselineDetail
                {
                    Pos = span.ContextSpan.Start,
                    PositionMarker = "<|",
                    Span = span,
                    Kind = DetailKind.ContextStart,
                });

                // context span 的起点在 text span 之后？
                if (ComparePositions(span.ContextSpan.Start, span.TextSpan.Start) > 0)
                {
                    canDetermineContextIdInline = false;
                }
            }

            var textSpanIndex = details.Count;
            var startMarker = "[|";
            if (options.GetLocationData != null)
            {
                startMarker += options.GetLocationData(span);
            }

            details.Add(new BaselineDetail { Pos = span.TextSpan.Start, PositionMarker = startMarker, Span = span, Kind = DetailKind.TextStart });
            details.Add(new BaselineDetail { Pos = span.TextSpan.End, PositionMarker = options.EndMarker ?? "|]", Span = span, Kind = DetailKind.TextEnd });

            if (span.ContextSpan != null)
            {
                details.Add(new BaselineDetail
                {
                    Pos = span.ContextSpan.End,
                    PositionMarker = "|>",
                    Span = span,
                    Kind = DetailKind.ContextEnd,
                });
            }

            if (options.StartMarkerPrefix != null)
            {
                var startPrefix = options.StartMarkerPrefix(span);
                if (startPrefix != null)
                {
                    // 特例：如果这个 span 与给定 marker 同起点，prefix 要写在 marker 名字之前。
                    // 即想要 `/*START PREFIX*/A: /*RENAME*/[|ARENAME|]`，
                    // 而不是 `/*RENAME*//*START PREFIX*/A: [|ARENAME|]`
                    if (options.Marker != null
                        && fileName == options.Marker.FileName()
                        && ComparePositions(span.TextSpan.Start, options.Marker.LSPos()) == 0)
                    {
                        var ok = detailPrefixes.ContainsKey(details[0]);
                        Debug.Assert(!ok, "Expected only single prefix at marker location");
                        detailPrefixes[details[0]] = startPrefix;
                    }
                    else if (span.ContextSpan != null && ComparePositions(span.ContextSpan.Start, span.TextSpan.Start) == 0)
                    {
                        detailPrefixes[details[contextSpanIndex]] = startPrefix;
                    }
                    else
                    {
                        detailPrefixes[details[textSpanIndex]] = startPrefix;
                    }
                }
            }

            if (options.EndMarkerSuffix != null)
            {
                var endSuffix = options.EndMarkerSuffix(span);
                if (endSuffix != null)
                {
                    // 与上面的 suffix 同理：
                    if (options.Marker != null
                        && fileName == options.Marker.FileName()
                        && ComparePositions(span.TextSpan.End, options.Marker.LSPos()) == 0)
                    {
                        detailSuffixes[details[0]] = endSuffix;
                    }
                    else if (span.ContextSpan != null && ComparePositions(span.ContextSpan.End, span.TextSpan.End) == 0)
                    {
                        detailSuffixes[details[textSpanIndex + 2]] = endSuffix;
                    }
                    else
                    {
                        detailSuffixes[details[textSpanIndex + 1]] = endSuffix;
                    }
                }
            }
        }

        // 我们偏好的 marker 写法是
        // /*MARKER*/[| some text |]
        // [| some /*MARKER*/ text |]
        // [| some text |]/*MARKER*/
        details = details
            .OrderBy(detail => detail, Comparer<BaselineDetail>.Create(CompareDetails))
            .ToList();
        // !!! if canDetermineContextIdInline

        var textWithContext = BaselineUtil.NewTextWithContext(fileName, content);
        for (var index = 0; index < details.Count; index++)
        {
            var detail = details[index];
            textWithContext.Add(detail);
            textWithContext.Pos = detail.Pos;

            // Prefix
            var prefix = detailPrefixes.TryGetValue(detail, out var p) ? p : "";
            if (prefix != "")
            {
                textWithContext.NewContent.Append(prefix);
            }

            textWithContext.NewContent.Append(detail.PositionMarker);
            if (detail.Span != null)
            {
                switch (detail.Kind)
                {
                    case DetailKind.TextStart:
                        var text = "";
                        if (spanToContextId.TryGetValue(detail.Span, out var contextId))
                        {
                            var isAfterContextStart = false;
                            for (var textStartIndex = index - 1; textStartIndex >= 0; textStartIndex--)
                            {
                                var textStartDetail = details[textStartIndex];
                                if (textStartDetail.Kind == DetailKind.ContextStart && ReferenceEquals(textStartDetail.Span, detail.Span))
                                {
                                    isAfterContextStart = true;
                                    break;
                                }

                                // 可以跳过 marker
                                if (textStartDetail.Span != null)
                                {
                                    break;
                                }
                            }

                            // 紧跟在 context span 后面的 span 不打 contextId
                            if (!isAfterContextStart)
                            {
                                if (text == "")
                                {
                                    text = "contextId: " + contextId.ToString(CultureInfo.InvariantCulture);
                                }
                                else
                                {
                                    text = "contextId: " + contextId.ToString(CultureInfo.InvariantCulture) + ", " + text;
                                }
                            }
                        }

                        if (text != "")
                        {
                            textWithContext.NewContent.Append("{ ");
                            textWithContext.NewContent.Append(text);
                            textWithContext.NewContent.Append(" |}");
                        }

                        break;
                    case DetailKind.ContextStart:
                        if (canDetermineContextIdInline)
                        {
                            spanToContextId[detail.Span] = spanToContextId.Count;
                        }

                        break;
                }
            }

            if (detailSuffixes.TryGetValue(detail, out var suffix))
            {
                textWithContext.NewContent.Append(suffix);
            }
        }

        textWithContext.Add(null);
        if (textWithContext.NewContent.Length != 0)
        {
            textWithContext.ReadableContents.Append("\n");
            textWithContext.ReadableJsoncBaseline(textWithContext.NewContent.ToString());
        }

        return textWithContext.ReadableContents.ToString();
    }

    /// <summary>Go: getBaselineContentForFile 里传给 slices.SortStableFunc 的比较器（baselineutil.go:409-474）。</summary>
    private static int CompareDetails(BaselineDetail d1, BaselineDetail d2)
    {
        var c = ComparePositions(d1.Pos, d2.Pos);
        if (c != 0 || (d1.Kind == DetailKind.Marker && d2.Kind == DetailKind.Marker))
        {
            return c;
        }

        // /*MARKER*/[| some text |]
        if (d1.Kind == DetailKind.Marker && d2.Kind.IsStart())
        {
            return -1;
        }

        if (d2.Kind == DetailKind.Marker && d1.Kind.IsStart())
        {
            return 1;
        }

        // [| some text |]/*MARKER*/
        if (d1.Kind == DetailKind.Marker && d2.Kind.IsEnd())
        {
            return 1;
        }

        if (d2.Kind == DetailKind.Marker && d1.Kind.IsEnd())
        {
            return -1;
        }

        // [||] 或 <||>
        if (ReferenceEquals(d1.Span, d2.Span))
        {
            return (int)d1.Kind - (int)d2.Kind;
        }

        // ...|><|...
        if (d1.Kind.IsStart() && d2.Kind.IsEnd())
        {
            return 1;
        }

        if (d1.Kind.IsEnd() && d2.Kind.IsStart())
        {
            return -1;
        }

        // <| ... [| ... |]|>
        if (d1.Kind.IsEnd() && d2.Kind.IsEnd())
        {
            c = ComparePositions(d2.GetRange().Start, d1.GetRange().Start);
            if (c != 0)
            {
                return c;
            }

            return (int)d1.Kind - (int)d2.Kind;
        }

        // <|[| ... |] ... |>
        if (d1.Kind.IsStart() && d2.Kind.IsStart())
        {
            c = ComparePositions(d2.GetRange().End, d2.GetRange().End);
            if (c != 0)
            {
                return c;
            }

            return (int)d1.Kind - (int)d2.Kind;
        }

        return 0;
    }

    /// <summary>Go: <c>func (f *FourslashTest) annotateContentWithTooltips[T comparable](t *testing.T, markersAndItems []markerAndItem[T], opName string, getRange func(item T) *lsproto.Range, getTooltipLines func(item T, prev T) []string) string</c>（baselineutil.go:671）。</summary>
    internal string AnnotateContentWithTooltips<T>(
        IHarnessTest t,
        MarkerAndItem<T>[]? markersAndItems,
        string opName,
        Func<T, Range?> getRange,
        Func<T, T?, string[]> getTooltipLines)
    {
        var barWithGutter = "| " + new string('-', 70);

        // 先按文件、再按文件内**逆序**的位置排序，这样同一行可以多次插入而不用重新计数。
        var sorted = (markersAndItems ?? [])
            .OrderBy(item => item, Comparer<MarkerAndItem<T>>.Create((a, b) =>
            {
                var c = string.CompareOrdinal(a.Marker.FileName(), b.Marker.FileName());
                if (c != 0)
                {
                    return c;
                }

                return -a.Marker.Position.CompareTo(b.Marker.Position);
            }))
            .ToList();

        var filesToLines = NewOrderedMapWithSizeHint<string, List<string>>(1);
        T? previous = default;
        foreach (var itemAndMarker in sorted)
        {
            var marker = itemAndMarker.Marker;
            var item = itemAndMarker.Item;

            var textRange = getRange(item);
            if (textRange == null)
            {
                // Go 的 `start := marker.LSPosition; end := start; end.Character++` 是两份值拷贝 →
                // C# 的 Position 是类，必须新建一个（否则会改到 marker.LSPosition 本身）。
                var start = marker.LSPosition;
                var end = new Position { Line = start.Line, Character = start.Character + 1 };
                textRange = new Range { Start = start, End = end };
            }

            if (textRange.Start.Line != textRange.End.Line)
            {
                t.Fatal($"Expected text range to be on a single line, got {FormatRange(textRange)}");
            }

            var underline = new string(' ', (int)textRange.Start.Character)
                + new string('^', (int)(textRange.End.Character - textRange.Start.Character));

            var fileName = marker.FileName();
            var existing = filesToLines.Get(fileName);
            List<string> lines;
            if (!existing.Ok)
            {
                lines = new List<string>(BaselineUtil.LineSplitter.Split(GetScriptInfo(fileName)!._content));
            }
            else
            {
                lines = existing.Value!;
            }

            string[] tooltipLines;
            // Go: `if item != *new(T)`（T 是指针类型 ⇒ 与 nil 比较）
            if (!EqualityComparer<T>.Default.Equals(item, default!))
            {
                tooltipLines = getTooltipLines(item, previous);
            }
            else
            {
                tooltipLines = [];
            }

            if (tooltipLines.Length == 0)
            {
                tooltipLines = [$"No {opName} at /*{marker.GetName()}*/."];
            }

            tooltipLines = Map(tooltipLines, line => "| " + line);

            var linesToInsert = new string[tooltipLines.Length + 3];
            linesToInsert[0] = underline;
            linesToInsert[1] = barWithGutter;
            Array.Copy(tooltipLines, 0, linesToInsert, 2, tooltipLines.Length);
            linesToInsert[^1] = barWithGutter;

            lines.InsertRange((int)textRange.Start.Line + 1, linesToInsert);
            filesToLines.Set(fileName, lines);

            previous = item;
        }

        var builder = new StringBuilder();
        var seenFirst = false;
        foreach (var (fileName, lines) in filesToLines.Entries())
        {
            builder.Append($"=== {fileName} ===\n");
            foreach (var line in lines!)
            {
                builder.Append("// ");
                builder.Append(line);
                builder.Append('\n');
            }

            if (seenFirst)
            {
                builder.Append("\n\n");
            }
            else
            {
                seenFirst = true;
            }
        }

        return builder.ToString();
    }

    /// <summary>
    /// Go 的 <c>fmt.Sprintf("…%v", r)</c>（<c>*lsproto.Range</c>）：Go 的 <c>%v</c> 对 struct 打印 <c>{1 2}</c>
    /// （不带字段名），指针再加 <c>&amp;</c>。
    /// </summary>
    private static string FormatRange(Range range)
        => "&{{"
            + range.Start.Line.ToString(CultureInfo.InvariantCulture)
            + " "
            + range.Start.Character.ToString(CultureInfo.InvariantCulture)
            + "} {"
            + range.End.Line.ToString(CultureInfo.InvariantCulture)
            + " "
            + range.End.Character.ToString(CultureInfo.InvariantCulture)
            + "}}";
}
