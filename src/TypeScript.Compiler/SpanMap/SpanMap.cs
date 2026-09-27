// ported from: internal/spanmap/spanmap.go
//
// 与 Go 的差异（C# 语言特性；依据 docs/architecture.md §2、§3.2、§3.3、§3.8）：
// 1) 目录/命名空间按 §2：Go package spanmap → 目录 SpanMap/ + 命名空间 TypeScript.SpanMap。
//    **不能**放 TypeScript.Ast（曾考虑放 Ast/）：本 package 有自己的 `Kind` 枚举，与 ast 的 `Kind` 撞名（CS0101）。
// 2) 包级函数：Go 的 `New`/`Unmarshal` 落成类型 `SpanMap` 上的静态工厂（`SpanMap.New(...)` /
//    `SpanMap.Unmarshal(...)`），而不是"与 package 同名的静态类"——静态类也叫 SpanMap 会与类型重名（CS0101）。
//    调用点文本与 Go 同形（`spanmap.New(...)` → `SpanMap.New(...)`）；其余未导出函数是本文件里的 private static。
// 3) 枚举方法 `Fidelity.IsExact/IsSingleSegment/IsNone` 在 C# 的 enum 上不存在 → 静态类
//    `SpanMapExtensions` 里的扩展方法（§3.3），调用点 `f.IsExact()` 不变。
// 4) Go 的 `(T, bool)` → `bool TryX(..., out T)`（Core/BinarySearch.cs 文件头定下的项目约定）：
//    AliasForVirtualSpan → TryAliasForVirtualSpan、VirtualToOriginalPositionExact →
//    TryVirtualToOriginalPositionExact、segmentIndexAt/segmentsAtOriginalPosition 同理。
//    `(core.TextRange, Fidelity)` 这种"两个分量都有效"的返回保持元组（与 Go 的字面形状一致）。
// 5) Go 的 `(T, error)`：`Unmarshal` 的形状错误 → 抛 `JsonException`（§3.2）；`Marshal() ([]byte, error)` 收成
//    `byte[] Marshal()`（错误路径抛异常）。
//    `Marshal` 用 `Json2.Marshal`（别名 `using Json2 = TypeScript.Json.Json`）：裸写 `Json` 会先解析到命名空间
//    `TypeScript.Json`，而裸写 `Marshal` 会被本类的实例方法 `Marshal()` 遮蔽（§2 的别名惯例）。
//    `Unmarshal` 是**显式**逐 token 解码（`Decoder.PeekKind`/`ReadToken`/`Decode<int>`）：Go 直接解进 `[][]int32`
//    那种变长内层数组，C# 的 JSON 引擎（静态分派、无反射）无法凭空创建嵌套容器实例，所以按元组形状手写循环，
//    错误仍然抛 `JsonException`（与 Go 的 error 对应）。
// 6) nil 接收者：Go 允许 `(*SpanMap)(nil).VirtualToOriginalSpan(r)`（nil 映射 = 恒等）。C# 实例方法不能在 null 上
//    分派，这些分支删除；调用点写 `map?.X(...) ?? 默认` 或先判空（Diagnostic.displayMessageArgs 的移植即如此）。
//    **参数**为 nil 的分支照 Go 保留。
// 7) `sync.Once`（origOnce）→ 字段 + lock + `Volatile.Read` 双重检查（Go 只在这里用 once，语义相同）。
// 8) `core.TextPos` 没有运算符重载（见 Core/Text.cs），Go 里 TextPos 的自由算术在 C# 侧写成 `.Value` 上的 `int`
//    运算再包回 TextPos；比较用 `==`/`<`（隐式转到 int，候选唯一）。max/min 用本文件助手（Math.Max 的重载
//    在 TextPos 上会 CS0121）。
// 9) `slices.Clone`/`slices.SortFunc`/`slices.Sort`/`slices.BinarySearchFunc`/`sort.Search` 的等价物在 C# 里没有
//    （Core 只有 TryBinarySearchUniqueFunc）→ 本文件底部的 `SpanMap` 私有助手，按 Go 的语义逐条实现。
// 10) `Validate` 里 Go 的字节切片比较 `virtual[a:b] == original[c:d]` → `AsSpan(...).SequenceEqual(...)`（§3.8）；
//    长度比较用 UTF-16 代码单元数（C# 的位置语义，见 Core/Text.cs）。
using TypeScript.Core;
using TypeScript.Json;
using static TypeScript.Core.Core;
using Json2 = TypeScript.Json.Json;

namespace TypeScript.SpanMap;

/// <summary>
/// Go: <c>type Kind int32</c>。描述段内位置如何关联虚拟文本与原文（成员名去 Go 的 <c>Kind</c> 前缀，§3.3）。
/// </summary>
public enum Kind : int
{
    /// <summary>长度保持：虚拟与原文长度相同，内部位置 1:1（<c>OriginalPos = pos - VirtualStart + OriginalStart</c>）。</summary>
    Verbatim = 0,

    /// <summary>整段映射：内部位置不可插值（长度可不同），位置钳制到段端点。用于重命名的标识符或短表达式。</summary>
    Atom = 1,

    /// <summary>几何形状同 Atom，但额外断言虚拟与原文是同一逻辑实体的名字（诊断展示可替换成原始名字）。</summary>
    Alias = 2,
}

/// <summary>
/// Go: <c>type Feature int32</c>。选择哪些 language-service 操作可以使用某个段。诊断刻意不在其中
/// （虚拟文本上的诊断不能选择不上报）；文本编辑另外要求严格的 verbatim 几何。
/// </summary>
[Flags]
public enum Feature : int
{
    Hover = 1 << 0,
    SignatureHelp = 1 << 1,
    Completion = 1 << 2,
    Definition = 1 << 3,
    TypeDefinition = 1 << 4,
    Implementation = 1 << 5,
    References = 1 << 6,
    DocumentHighlights = 1 << 7,
    Rename = 1 << 8,
    CallHierarchy = 1 << 9,
    CodeActions = 1 << 10,
    Formatting = 1 << 11,
    InlayHints = 1 << 12,
    SemanticTokens = 1 << 13,
    FoldingRanges = 1 << 14,
    SelectionRanges = 1 << 15,
    LinkedEditing = 1 << 16,
    AutoInsert = 1 << 17,
    DocumentSymbols = 1 << 18,
    CodeLens = 1 << 19,
    None = 0,

    // Go: FeatureAll = (FeatureCodeLens << 1) - 1。C# 的 enum 成员不能对前面的成员做移位（enum 类型上的
    // 算术不合法），这里写成等值字面量。
    All = (1 << 20) - 1,
}

/// <summary>Go: <c>type Fidelity int32</c>。映射结果与原文的忠实程度。</summary>
public enum Fidelity : int
{
    /// <summary>整段落在同一个 verbatim 段内，精确映射。</summary>
    Exact = 0,

    /// <summary>整段落在同一个 atom 段内，映射到该 atom 的整段。</summary>
    Atom = 1,

    /// <summary>跨段边界；端点已映射并钳制。</summary>
    Approximate = 2,

    /// <summary>没有原文对应物（完全是合成内容）。</summary>
    None = 3,
}

/// <summary>
/// Go: <c>type Segment struct</c>。把半开虚拟区间 <c>[VirtualStart, VirtualEnd)</c> 映射到半开原文区间
/// <c>[OriginalStart, OriginalEnd)</c>。值语义（Go 的结构体）→ C# 可变 struct，字段名照抄 Go 的导出字段。
/// </summary>
public struct Segment
{
    public TextPos VirtualStart;
    public TextPos VirtualEnd;
    public TextPos OriginalStart;
    public TextPos OriginalEnd;
    public Kind Kind;
    public Feature Features;
}

/// <summary>Go: <c>type MappedPosition struct</c>：原文位置的一个虚拟投影及其忠实度。</summary>
public readonly struct MappedPosition
{
    public readonly TextPos Position;
    public readonly Fidelity Fidelity;

    public MappedPosition(TextPos position, Fidelity fidelity)
    {
        Position = position;
        Fidelity = fidelity;
    }
}

/// <summary>Go: <c>type MappedSpan struct</c>：原文区间的一个虚拟投影及其忠实度。</summary>
public readonly struct MappedSpan
{
    public readonly TextRange Span;
    public readonly Fidelity Fidelity;

    public MappedSpan(TextRange span, Fidelity fidelity)
    {
        Span = span;
        Fidelity = fidelity;
    }
}

/// <summary>
/// Go: <c>type SpanMap struct</c>。内容映射器虚拟文本上的稀疏有序段集合；未被任何段覆盖的虚拟位置是合成的
/// （没有原文对应物），所以空 SpanMap 表示"虚拟文本全是合成的"。
/// </summary>
public sealed class SpanMap
{
    private readonly Segment[] _segments;

    // Go: origOnce sync.Once + originalIndex *originalIndex（原文→虚拟查询用的区间索引，首次使用时构建）。
    private readonly object _origLock = new();
    private OriginalIndex? _originalIndex;

    private SpanMap(Segment[] segments)
    {
        _segments = segments;
    }

    /// <summary>
    /// Go: <c>func New(segments []Segment) *SpanMap</c>。段按虚拟起点排序；只描述与原文对应的部分。
    /// </summary>
    public static SpanMap New(Segment[]? segments)
    {
        var sorted = CloneSegments(segments);
        Array.Sort(sorted, static (a, b) => a.VirtualStart.Value - b.VirtualStart.Value);
        return new SpanMap(sorted);
    }

    /// <summary>Go: <c>func (m *SpanMap) Segments() []Segment</c>（返回副本；Go 的 nil 接收者返回 nil 的分支已删）。</summary>
    public Segment[] Segments() => CloneSegments(_segments);

    /// <summary>
    /// Go: <c>func (m *SpanMap) VirtualToOriginalSpan(r) (core.TextRange, Fidelity)</c>。完全落在段间空隙（或空 map）
    /// 的虚拟区间是合成的：映射到原文插入点并返回 <see cref="Fidelity.None"/>。
    /// </summary>
    public (TextRange Span, Fidelity Fidelity) VirtualToOriginalSpan(TextRange r)
    {
        var virtualStart = r.Pos();
        var virtualEnd = r.End() > virtualStart ? r.End() : virtualStart;
        if (virtualStart == virtualEnd)
        {
            var (position, fidelity) = VirtualToOriginalPosition(virtualStart);
            return (NewTextRange(position, position), fidelity);
        }

        var startIn = SegmentIndexAt(virtualStart, out var startIdx);
        var endProbe = virtualEnd - 1;
        var endIn = SegmentIndexAt(endProbe, out var endIdx);

        if (startIdx == endIdx && startIn == endIn)
        {
            if (startIn)
            {
                var seg = _segments[startIdx];
                if (seg.Kind == Kind.Verbatim)
                {
                    var origStart = Clamp(seg.OriginalStart.Value + (virtualStart - seg.VirtualStart.Value), seg.OriginalStart, seg.OriginalEnd);
                    var origEnd = Clamp(seg.OriginalStart.Value + (virtualEnd - seg.VirtualStart.Value), origStart, seg.OriginalEnd);
                    return (NewTextRange(origStart, origEnd), Fidelity.Exact);
                }

                return (NewTextRange(seg.OriginalStart, seg.OriginalEnd), Fidelity.Atom);
            }

            // Entirely within a single synthesized gap.
            var pos = InsertionPoint(startIdx);
            return (NewTextRange(pos, pos), Fidelity.None);
        }

        var mappedStart = MapLow(virtualStart, startIdx, startIn);
        var mappedEnd = MaxPos(MapHigh(virtualEnd, endIdx, endIn), mappedStart);
        return (NewTextRange(mappedStart, mappedEnd), Fidelity.Approximate);
    }

    /// <summary>
    /// Go: <c>func (m *SpanMap) VirtualToOriginalSpanForFeature(r, feature)</c>。只有非空区间里每个虚拟位置都被
    /// 参与 feature 的连续段覆盖时才映射；零长度区间要求它所在的段参与。
    /// </summary>
    public (TextRange Span, Fidelity Fidelity) VirtualToOriginalSpanForFeature(TextRange r, Feature feature)
    {
        var (mapped, fidelity) = VirtualToOriginalSpan(r);
        if (VirtualSpanSupportsFeature(r, feature))
        {
            return (mapped, fidelity);
        }

        return (mapped, Fidelity.None);
    }

    private bool VirtualSpanSupportsFeature(TextRange r, Feature feature)
    {
        var start = r.Pos();
        var end = r.End() > start ? r.End() : start;
        if (start == end)
        {
            var inside = SegmentIndexAt(start, out var index);
            return inside && SupportsFeature(_segments[index], feature);
        }

        var startInside = SegmentIndexAt(start, out var cursor);
        if (!startInside)
        {
            return false;
        }

        var coveredThrough = start;
        while (cursor < _segments.Length && coveredThrough < end)
        {
            var segment = _segments[cursor];
            if (segment.VirtualStart.Value > coveredThrough || segment.VirtualEnd.Value <= coveredThrough || !SupportsFeature(segment, feature))
            {
                return false;
            }

            coveredThrough = segment.VirtualEnd.Value;
            cursor++;
        }

        return coveredThrough >= end;
    }

    /// <summary>
    /// Go: <c>func (m *SpanMap) VirtualToOriginalPosition(pos) (core.TextPos, Fidelity)</c>。空隙里的位置是合成的，
    /// 映射到插入点并返回 <see cref="Fidelity.None"/>。
    /// </summary>
    public (TextPos Position, Fidelity Fidelity) VirtualToOriginalPosition(TextPos pos)
    {
        var inside = SegmentIndexAt(pos, out var idx);
        if (!inside)
        {
            return (InsertionPoint(idx), Fidelity.None);
        }

        var seg = _segments[idx];
        if (seg.Kind == Kind.Verbatim)
        {
            return (Clamp(seg.OriginalStart.Value + (pos.Value - seg.VirtualStart.Value), seg.OriginalStart, seg.OriginalEnd), Fidelity.Exact);
        }

        return (seg.OriginalStart, Fidelity.Atom);
    }

    /// <summary>
    /// Go: <c>func (m *SpanMap) VirtualToOriginalPositionExact(pos) (core.TextPos, bool)</c>。只有位置明确落在
    /// verbatim 内容里才映射；贴着 atom 的边界被拒绝（同一虚拟位置可以描述两侧）。
    /// </summary>
    public bool TryVirtualToOriginalPositionExact(TextPos pos, out TextPos mappedPos)
    {
        var (mapped, fidelity) = VirtualToOriginalPosition(pos);
        mappedPos = mapped;
        if (fidelity != Fidelity.Exact)
        {
            return false;
        }

        var inside = SegmentIndexAt(pos, out var index);
        if (!inside || _segments[index].Kind != Kind.Verbatim)
        {
            return false;
        }

        if (index > 0)
        {
            var previous = _segments[index - 1];
            if (previous.VirtualEnd.Value == pos.Value &&
                (previous.Kind != Kind.Verbatim || previous.OriginalEnd.Value != _segments[index].OriginalStart.Value))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Go: <c>func (m *SpanMap) VirtualToOriginalPositionForFeature(pos, feature)</c>。位置的虚拟段必须参与 feature。
    /// </summary>
    public (TextPos Position, Fidelity Fidelity) VirtualToOriginalPositionForFeature(TextPos pos, Feature feature)
    {
        var (mapped, fidelity) = VirtualToOriginalPosition(pos);
        var inside = SegmentIndexAt(pos, out var index);
        if (!inside || !SupportsFeature(_segments[index], feature))
        {
            return (mapped, Fidelity.None);
        }

        return (mapped, fidelity);
    }

    /// <summary>
    /// Go: <c>func (m *SpanMap) AliasForVirtualSpan(r) (Segment, bool)</c>。返回恰好覆盖 r 的 alias 段；
    /// 部分重叠不算（诊断文本只在识别出完整虚拟别名时才替换）。
    /// </summary>
    public bool TryAliasForVirtualSpan(TextRange r, out Segment segment)
    {
        segment = default;
        var inside = SegmentIndexAt(r.Pos(), out var index);
        if (!inside)
        {
            return false;
        }

        var candidate = _segments[index];
        segment = candidate;
        return candidate.Kind == Kind.Alias && r.Pos() == candidate.VirtualStart.Value && r.End() == candidate.VirtualEnd.Value;
    }

    /// <summary>
    /// Go: <c>func (m *SpanMap) segmentIndexAt(pos) (int, bool)</c>。返回包含 pos 的段下标与 true；pos 落在空隙时
    /// 返回它前面的段（没有则 -1）与 false。
    /// </summary>
    private bool SegmentIndexAt(TextPos pos, out int index)
    {
        index = BinarySearchFunc(_segments, pos, static (s, p) => s.VirtualStart.Value - p.Value, out var found);
        if (found)
        {
            return true;
        }

        var prev = index - 1;
        if (prev >= 0 &&
            (pos.Value < _segments[prev].VirtualEnd.Value ||
             (prev == _segments.Length - 1 && pos.Value == _segments[prev].VirtualEnd.Value)))
        {
            index = prev;
            return true;
        }

        index = prev;
        return false;
    }

    /// <summary>
    /// Go: <c>func (m *SpanMap) insertionPoint(prev) core.TextPos</c>。prev 段之后的合成内容所在的原文偏移
    /// （该段的 OriginalEnd；第一个段之前是 0）。
    /// </summary>
    private TextPos InsertionPoint(int prev)
    {
        if (prev < 0)
        {
            return 0;
        }

        return _segments[prev].OriginalEnd;
    }

    /// <summary>Go: <c>func (m *SpanMap) mapLow(pos, idx, in)</c>：虚拟下边界 → 原文坐标。</summary>
    private TextPos MapLow(TextPos pos, int idx, bool inside)
    {
        if (!inside)
        {
            return InsertionPoint(idx);
        }

        var seg = _segments[idx];
        if (seg.Kind == Kind.Verbatim)
        {
            return Clamp(seg.OriginalStart.Value + (pos.Value - seg.VirtualStart.Value), seg.OriginalStart, seg.OriginalEnd);
        }

        return seg.OriginalStart;
    }

    /// <summary>Go: <c>func (m *SpanMap) mapHigh(pos, idx, in)</c>：虚拟上边界 → 原文坐标。</summary>
    private TextPos MapHigh(TextPos pos, int idx, bool inside)
    {
        if (!inside)
        {
            return InsertionPoint(idx);
        }

        var seg = _segments[idx];
        if (seg.Kind == Kind.Verbatim)
        {
            return Clamp(seg.OriginalStart.Value + (pos.Value - seg.VirtualStart.Value), seg.OriginalStart, seg.OriginalEnd);
        }

        return seg.OriginalEnd;
    }

    /// <summary>
    /// Go: <c>func (m *SpanMap) OriginalToVirtualPositions(pos, feature) []MappedPosition</c>。原文位置的所有虚拟投影
    /// （段的结束位置对点映射是包含的），按虚拟位置排序。
    /// </summary>
    public MappedPosition[] OriginalToVirtualPositions(TextPos pos, Feature feature)
    {
        var groups = OrigIndex().SegmentGroupsAtOriginalPosition(pos);
        if (groups.Length == 0)
        {
            return [];
        }

        var results = new List<MappedPosition>();
        foreach (var group in groups)
        {
            foreach (var segment in group._segments)
            {
                if (!SupportsFeature(segment, feature))
                {
                    continue;
                }

                MappedPosition mapped;
                if (segment.Kind == Kind.Verbatim)
                {
                    mapped = new MappedPosition(
                        Clamp(segment.VirtualStart.Value + (pos.Value - segment.OriginalStart.Value), segment.VirtualStart, segment.VirtualEnd),
                        Fidelity.Exact);
                }
                else if (group._atEnd)
                {
                    mapped = new MappedPosition(segment.VirtualEnd, Fidelity.Atom);
                }
                else
                {
                    mapped = new MappedPosition(segment.VirtualStart, Fidelity.Atom);
                }

                if (!ContainsPosition(results, mapped))
                {
                    results.Add(mapped);
                }
            }
        }

        results.Sort(static (a, b) => a.Position.Value - b.Position.Value);
        return results.ToArray();
    }

    private static bool ContainsPosition(List<MappedPosition> results, MappedPosition candidate)
    {
        foreach (var existing in results)
        {
            if (existing.Position.Value == candidate.Position.Value && existing.Fidelity == candidate.Fidelity)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Go: <c>func (m *SpanMap) OriginalToVirtualSpans(r, feature) []MappedSpan</c>。原文区间所有与 feature 兼容的
    /// 虚拟投影；跨组的结果是近似忠实度（Go 的注释见源文件）。
    /// </summary>
    public MappedSpan[] OriginalToVirtualSpans(TextRange r, Feature feature)
    {
        var start = r.Pos();
        var end = r.End() > start ? r.End() : start;
        if (start == end)
        {
            return Map(OriginalToVirtualPositions(start, feature), static position =>
                new MappedSpan(NewTextRange(position.Position, position.Position), position.Fidelity));
        }

        var lastCharacter = end - 1;
        var index = OrigIndex();
        var startInside = index.TrySegmentsAtOriginalPosition(start, out var startSegments);
        var endInside = index.TrySegmentsAtOriginalPosition(lastCharacter, out var endSegments);
        if (!startInside || !endInside)
        {
            return [];
        }

        var containing = new List<Segment>();
        foreach (var segment in startSegments)
        {
            if (end <= segment.OriginalEnd.Value)
            {
                containing.Add(segment);
            }
        }

        if (containing.Count > 0)
        {
            var containedResults = OriginalToVirtualSpansInSegments(containing.ToArray(), start, end, feature);
            if (containedResults.Length > 0)
            {
                Array.Sort(containedResults, static (a, b) => a.Span.Pos() - b.Span.Pos());
                return containedResults;
            }
        }

        var starts = OriginalStartProjections(startSegments, start, feature);
        var ends = OriginalEndProjections(endSegments, end, feature);
        if (starts.Length == 0 || ends.Length == 0)
        {
            return [];
        }

        // Go: slices.Sort(starts)/slices.Sort(ends)（TextPos 没有 IComparable，显式给比较器）。
        Array.Sort(starts, static (a, b) => a.Value - b.Value);
        Array.Sort(ends, static (a, b) => a.Value - b.Value);
        var results = new List<MappedSpan>(Math.Min(starts.Length, ends.Length));
        for (var i = 0; i < starts.Length; i++)
        {
            var virtualStart = starts[i];
            var endIndex = BinarySearchFunc(ends, virtualStart, static (a, b) => a.Value - b.Value, out _);
            if (endIndex == ends.Length || (i + 1 < starts.Length && starts[i + 1].Value <= ends[endIndex].Value))
            {
                continue;
            }

            results.Add(new MappedSpan(NewTextRange(virtualStart, ends[endIndex]), Fidelity.Approximate));
        }

        return results.ToArray();
    }

    /// <summary>
    /// Go: <c>func (m *SpanMap) OriginalToVirtualIntersectingSpans(r, feature) []MappedSpan</c>。每个参与 feature 的段
    /// 与 r 的交集；与 OriginalToVirtualSpans 不同，未覆盖的区间端点不会压制已覆盖的内部段。
    /// </summary>
    public MappedSpan[] OriginalToVirtualIntersectingSpans(TextRange r, Feature feature)
    {
        if (r.Pos() == r.End())
        {
            return OriginalToVirtualSpans(r, feature);
        }

        var results = new List<MappedSpan>();
        foreach (var segment in _segments)
        {
            if (!SupportsFeature(segment, feature))
            {
                continue;
            }

            var start = MaxPos(r.Pos(), segment.OriginalStart);
            var end = MinPos(r.End(), segment.OriginalEnd);
            if (start.Value >= end.Value)
            {
                continue;
            }

            if (segment.Kind == Kind.Verbatim)
            {
                results.Add(new MappedSpan(
                    NewTextRange(
                        segment.VirtualStart.Value + (start.Value - segment.OriginalStart.Value),
                        segment.VirtualStart.Value + (end.Value - segment.OriginalStart.Value)),
                    Fidelity.Exact));
            }
            else
            {
                results.Add(new MappedSpan(NewTextRange(segment.VirtualStart, segment.VirtualEnd), Fidelity.Atom));
            }
        }

        return results.ToArray();
    }

    /// <summary>
    /// Go: <c>func originalStartProjections(segments, start, feature) []core.TextPos</c>。原文区间包含式起点在
    /// 每个匹配段上的投影（verbatim 保留段内偏移，atom 用虚拟起点）。
    /// </summary>
    private static TextPos[] OriginalStartProjections(Segment[] segments, TextPos start, Feature feature)
    {
        var results = new List<TextPos>(segments.Length);
        foreach (var segment in segments)
        {
            if (!SupportsFeature(segment, feature))
            {
                continue;
            }

            if (segment.Kind == Kind.Verbatim)
            {
                results.Add(Clamp(
                    segment.VirtualStart.Value + (start.Value - segment.OriginalStart.Value),
                    segment.VirtualStart,
                    segment.VirtualEnd));
            }
            else
            {
                results.Add(segment.VirtualStart);
            }
        }

        return results.ToArray();
    }

    /// <summary>
    /// Go: <c>func originalEndProjections(segments, end, feature) []core.TextPos</c>。原文区间排他终点在每个匹配段上的
    /// 投影（verbatim 保留该边界，atom 用虚拟终点）。
    /// </summary>
    private static TextPos[] OriginalEndProjections(Segment[] segments, TextPos end, Feature feature)
    {
        var results = new List<TextPos>(segments.Length);
        foreach (var segment in segments)
        {
            if (!SupportsFeature(segment, feature))
            {
                continue;
            }

            if (segment.Kind == Kind.Verbatim)
            {
                results.Add(Clamp(
                    segment.VirtualStart.Value + (end.Value - segment.OriginalStart.Value),
                    segment.VirtualStart,
                    segment.VirtualEnd));
            }
            else
            {
                results.Add(segment.VirtualEnd);
            }
        }

        return results.ToArray();
    }

    /// <summary>Go: <c>func originalToVirtualSpansInSegments(segments, start, end, feature) []MappedSpan</c>。</summary>
    private static MappedSpan[] OriginalToVirtualSpansInSegments(Segment[] segments, TextPos start, TextPos end, Feature feature)
    {
        var results = new List<MappedSpan>(segments.Length);
        foreach (var segment in segments)
        {
            if (!SupportsFeature(segment, feature))
            {
                continue;
            }

            if (segment.Kind == Kind.Verbatim)
            {
                var virtualStart = Clamp(
                    segment.VirtualStart.Value + (start.Value - segment.OriginalStart.Value),
                    segment.VirtualStart,
                    segment.VirtualEnd);
                var virtualEnd = Clamp(
                    segment.VirtualStart.Value + (end.Value - segment.OriginalStart.Value),
                    virtualStart,
                    segment.VirtualEnd);
                results.Add(new MappedSpan(NewTextRange(virtualStart, virtualEnd), Fidelity.Exact));
            }
            else
            {
                results.Add(new MappedSpan(NewTextRange(segment.VirtualStart, segment.VirtualEnd), Fidelity.Atom));
            }
        }

        return results.ToArray();
    }

    /// <summary>Go: <c>func sameOriginalRange(left, right Segment) bool</c>：两个段是否属于同一重复组。</summary>
    private static bool SameOriginalRange(Segment left, Segment right) =>
        left.OriginalStart.Value == right.OriginalStart.Value && left.OriginalEnd.Value == right.OriginalEnd.Value;

    /// <summary>
    /// Go: <c>func (m *SpanMap) origIndex() *originalIndex</c>。首次使用时构建不可变的原文区间索引
    /// （Go 的 sync.Once → 字段 + lock + Volatile 双重检查）。
    /// </summary>
    private OriginalIndex OrigIndex()
    {
        var index = Volatile.Read(ref _originalIndex);
        if (index is not null)
        {
            return index;
        }

        lock (_origLock)
        {
            return _originalIndex ??= OriginalIndex.Build(_segments);
        }
    }

    /// <summary>Go: <c>func (m *SpanMap) Validate(virtual, original string) *MappingError</c>（nil 返回 = 合法）。</summary>
    public MappingError? Validate(string @virtual, string original)
    {
        var virtualLen = @virtual.Length;
        var origLen = original.Length;
        var previousVirtualEnd = 0;
        foreach (var s in _segments)
        {
            if (s.VirtualStart.Value < previousVirtualEnd || s.VirtualEnd.Value < s.VirtualStart.Value || s.VirtualEnd.Value > virtualLen)
            {
                return new MappingError(MappingErrorKind.Overlap, s.VirtualStart, default);
            }

            previousVirtualEnd = s.VirtualEnd.Value;
            if (s.OriginalStart.Value < 0 || s.OriginalEnd.Value < s.OriginalStart.Value || s.OriginalEnd.Value > origLen)
            {
                return new MappingError(MappingErrorKind.OutOfBounds, s.VirtualStart, s.OriginalEnd);
            }

            if (s.Kind != Kind.Verbatim && s.Kind != Kind.Atom && s.Kind != Kind.Alias)
            {
                return new MappingError(MappingErrorKind.Kind, s.VirtualStart, s.OriginalStart);
            }

            if (s.Kind == Kind.Verbatim)
            {
                var virtualLength = s.VirtualEnd.Value - s.VirtualStart.Value;
                var originalLength = s.OriginalEnd.Value - s.OriginalStart.Value;
                if (virtualLength != originalLength ||
                    !@virtual.AsSpan(s.VirtualStart.Value, virtualLength)
                        .SequenceEqual(original.AsSpan(s.OriginalStart.Value, originalLength)))
                {
                    return new MappingError(MappingErrorKind.VerbatimMismatch, s.VirtualStart, s.OriginalStart);
                }
            }

            if ((s.Features & ~_featureMask) != 0)
            {
                return new MappingError(MappingErrorKind.Feature, s.VirtualStart, s.OriginalStart);
            }
        }

        return null;
    }

    private const Feature _featureMask = Feature.All;

    /// <summary>
    /// Go: <c>func Unmarshal(data []byte) (*SpanMap, error)</c>。五元组省略 features 并规范化为
    /// <see cref="Feature.All"/>；六元组保留显式 feature 掩码（含 <see cref="Feature.None"/>）。
    /// </summary>
    /// <remarks>
    /// **偏离**：Go 用 `json.Unmarshal(data, &amp;tuples)` 直接解进 `[][]int32`（变长内层数组）。C# 的 JSON 引擎
    /// （docs/json.md：静态分派、无反射）不能凭空创建数组/嵌套容器的实例，所以这里按 Go 的元组形状**显式**逐 token
    /// 解码（仍然是 `TypeScript.Json` 的 Decoder，不引反射）；形状错误与 Go 的 error 一样抛 <see cref="JsonException"/>。
    /// 元素个数在上限 6 处提前判错，语义与 Go 的 `len(t) != 5 &amp;&amp; len(t) != 6` 一致。
    /// </remarks>
    public static SpanMap Unmarshal(ReadOnlySpan<byte> data)
    {
        var decoder = new Decoder(data);
        if (decoder.PeekKind() != JsonKind.BeginArray)
        {
            throw new JsonException("span map: expected an array of segments");
        }

        decoder.ReadToken();
        var segments = new List<Segment>();
        var index = 0;
        while (decoder.PeekKind() != JsonKind.EndArray)
        {
            if (decoder.PeekKind() != JsonKind.BeginArray)
            {
                throw new JsonException($"span map segment {index}: expected an array of 5 or 6 values");
            }

            decoder.ReadToken();
            var values = new int[6];
            var count = 0;
            while (decoder.PeekKind() != JsonKind.EndArray)
            {
                if (count == values.Length || decoder.PeekKind() != JsonKind.Number)
                {
                    throw new JsonException($"span map segment {index}: expected 5 or 6 integer values");
                }

                values[count++] = decoder.Decode<int>();
            }

            decoder.ReadToken();
            if (count != 5 && count != 6)
            {
                throw new JsonException($"span map segment {index}: expected 5 or 6 values, got {count}");
            }

            var segment = new Segment
            {
                VirtualStart = values[0],
                VirtualEnd = values[0] + values[1],
                OriginalStart = values[2],
                OriginalEnd = values[2] + values[3],
                Kind = (Kind)values[4],
                Features = Feature.All,
            };
            if (count == 6)
            {
                segment.Features = (Feature)values[5];
            }

            segments.Add(segment);
            index++;
        }

        decoder.ReadToken();
        return New(segments.ToArray());
    }

    /// <summary>
    /// Go: <c>func (m *SpanMap) Marshal() ([]byte, error)</c>。<see cref="Feature.All"/> 用向后兼容的五元组，
    /// 其它掩码多一个第六个元素。
    /// </summary>
    public byte[] Marshal()
    {
        var tuples = new int[_segments.Length][];
        for (var i = 0; i < _segments.Length; i++)
        {
            var s = _segments[i];
            if (s.Features != Feature.All)
            {
                tuples[i] =
                [
                    s.VirtualStart.Value,
                    s.VirtualEnd.Value - s.VirtualStart.Value,
                    s.OriginalStart.Value,
                    s.OriginalEnd.Value - s.OriginalStart.Value,
                    (int)s.Kind,
                    (int)s.Features,
                ];
            }
            else
            {
                tuples[i] =
                [
                    s.VirtualStart.Value,
                    s.VirtualEnd.Value - s.VirtualStart.Value,
                    s.OriginalStart.Value,
                    s.OriginalEnd.Value - s.OriginalStart.Value,
                    (int)s.Kind,
                ];
            }
        }

        return Json2.Marshal(tuples);
    }

    // ── 助手（Go 的 slices/sort 内建在 C# 里的等价物）──

    /// <summary>Go: <c>slices.Clone</c>（nil 切片 → 空数组，见 Core.cs 文件头的约定）。</summary>
    private static Segment[] CloneSegments(Segment[]? segments) =>
        segments is null ? [] : (Segment[])segments.Clone();

    /// <summary>
    /// Go: <c>slices.BinarySearchFunc</c>（元素类型与目标类型可以不同，例如在 <c>[]Segment</c> 里查
    /// <c>core.TextPos</c>）：返回插入点，<paramref name="found"/> 表示是否命中。
    /// </summary>
    private static int BinarySearchFunc<T, TTarget>(T[] s, TTarget target, Func<T, TTarget, int> cmp, out bool found)
    {
        var lo = 0;
        var hi = s.Length;
        while (lo < hi)
        {
            var mid = lo + ((hi - lo) >> 1);
            if (cmp(s[mid], target) < 0)
            {
                lo = mid + 1;
            }
            else
            {
                hi = mid;
            }
        }

        found = lo < s.Length && cmp(s[lo], target) == 0;
        return lo;
    }

    /// <summary>Go: <c>sort.Search</c>：第一个使 predicate 为真的下标（predicate 必须单调）。</summary>
    private static int LowerBound(int n, Func<int, bool> predicate)
    {
        var lo = 0;
        var hi = n;
        while (lo < hi)
        {
            var mid = lo + ((hi - lo) >> 1);
            if (predicate(mid))
            {
                hi = mid;
            }
            else
            {
                lo = mid + 1;
            }
        }

        return lo;
    }

    /// <summary>Go 的 <c>max</c>（TextPos 上没有运算符，也不能用 Math.Max —— 重载在隐式转换下会 CS0121）。</summary>
    private static TextPos MaxPos(TextPos a, TextPos b) => a.Value > b.Value ? a : b;

    /// <summary>Go 的 <c>min</c>。</summary>
    private static TextPos MinPos(TextPos a, TextPos b) => a.Value < b.Value ? a : b;

    /// <summary>Go: <c>func clamp(v, lo, hi core.TextPos) core.TextPos</c>。</summary>
    private static TextPos Clamp(int v, TextPos lo, TextPos hi) =>
        new(v < lo.Value ? lo.Value : v > hi.Value ? hi.Value : v);

    /// <summary>Go: <c>func supportsFeature(segment, feature) bool</c>。</summary>
    private static bool SupportsFeature(Segment segment, Feature feature) => (segment.Features & feature) != 0;

    /// <summary>
    /// Go: <c>type originalIndex struct</c>。按原文顺序存放段，外加一棵完全二叉树的 max-end 表（内部节点存子树里
    /// 最大 OriginalEnd），点查询时可整棵剪掉不可能命中的子树。
    /// </summary>
    internal sealed class OriginalIndex
    {
        internal readonly Segment[] _segments;
        internal readonly int _leafCount;
        internal readonly TextPos[] _maxEnds;

        private OriginalIndex(Segment[] segments, int leafCount, TextPos[] maxEnds)
        {
            _segments = segments;
            _leafCount = leafCount;
            _maxEnds = maxEnds;
        }

        /// <summary>Go: <c>func (m *SpanMap) origIndex()</c> 里的索引构建部分。</summary>
        internal static OriginalIndex Build(Segment[] segments)
        {
            var sorted = (Segment[])segments.Clone();
            Array.Sort(sorted, static (a, b) =>
            {
                var c = a.OriginalStart.Value - b.OriginalStart.Value;
                if (c != 0)
                {
                    return c;
                }

                c = a.OriginalEnd.Value - b.OriginalEnd.Value;
                if (c != 0)
                {
                    return c;
                }

                return a.VirtualStart.Value - b.VirtualStart.Value;
            });
            var leafCount = 1;
            while (leafCount < sorted.Length)
            {
                leafCount *= 2;
            }

            var maxEnds = new TextPos[2 * leafCount];
            for (var i = 0; i < sorted.Length; i++)
            {
                maxEnds[leafCount + i] = sorted[i].OriginalEnd;
            }

            for (var i = leafCount - 1; i > 0; i--)
            {
                maxEnds[i] = MaxPos(maxEnds[2 * i], maxEnds[2 * i + 1]);
            }

            return new OriginalIndex(sorted, leafCount, maxEnds);
        }

        /// <summary>
        /// Go: <c>func (i *originalIndex) segmentsAtOriginalPosition(pos) ([]Segment, bool)</c>。包含原文位置 pos 的
        /// 每个段（段的结束位置是排他的；段起点——含零长度段——算包含）。
        /// </summary>
        internal bool TrySegmentsAtOriginalPosition(TextPos pos, out Segment[] segments)
        {
            var start = LowerBound(_segments.Length, index => _segments[index].OriginalStart.Value >= pos.Value);
            var results = new List<Segment>();
            SegmentsEndingAfterPosition(start, pos, results);
            var end = LowerBound(_segments.Length, index => _segments[index].OriginalStart.Value > pos.Value);
            for (var i = start; i < end; i++)
            {
                results.Add(_segments[i]);
            }

            segments = results.ToArray();
            return segments.Length > 0;
        }

        /// <summary>Go: <c>func (i *originalIndex) segmentsEndingAfterPosition(limit, pos) []Segment</c>。</summary>
        private void SegmentsEndingAfterPosition(int limit, TextPos pos, List<Segment> results) =>
            CollectSegmentsEndingAtOrAfter(1, 0, _leafCount, limit, pos, false, results);

        /// <summary>
        /// Go: <c>func (i *originalIndex) collectSegmentsEndingAtOrAfter(...)</c>。自左向右遍历 max-end 树，
        /// 保持原文顺序；超出 limit 或最大 end 够不到的节点整棵丢弃。
        /// </summary>
        private void CollectSegmentsEndingAtOrAfter(int node, int start, int end, int limit, TextPos pos, bool includeEnd, List<Segment> results)
        {
            if (start >= limit ||
                _maxEnds[node].Value < pos.Value ||
                (!includeEnd && _maxEnds[node].Value == pos.Value))
            {
                return;
            }

            if (end - start == 1)
            {
                results.Add(_segments[start]);
                return;
            }

            var middle = start + ((end - start) / 2);
            CollectSegmentsEndingAtOrAfter(2 * node, start, middle, limit, pos, includeEnd, results);
            CollectSegmentsEndingAtOrAfter((2 * node) + 1, middle, end, limit, pos, includeEnd, results);
        }

        /// <summary>
        /// Go: <c>func (i *originalIndex) segmentGroupsAtOriginalPosition(pos) []segmentGroupAtOriginalPosition</c>。
        /// 包含或贴着原文位置 pos 的等长段分组（点映射时段的结束位置算包含）。
        /// </summary>
        internal SegmentGroupAtOriginalPosition[] SegmentGroupsAtOriginalPosition(TextPos pos)
        {
            var limit = LowerBound(_segments.Length, index => _segments[index].OriginalStart.Value > pos.Value);
            var segments = new List<Segment>();
            CollectSegmentsEndingAtOrAfter(1, 0, _leafCount, limit, pos, true, segments);

            var groups = new List<SegmentGroupAtOriginalPosition>();
            for (var start = 0; start < segments.Count;)
            {
                var end = start + 1;
                while (end < segments.Count && SameOriginalRange(segments[start], segments[end]))
                {
                    end++;
                }

                var segment = segments[start];
                if (pos.Value <= segment.OriginalEnd.Value)
                {
                    var groupSegments = new Segment[end - start];
                    segments.CopyTo(start, groupSegments, 0, end - start);
                    groups.Add(new SegmentGroupAtOriginalPosition(
                        groupSegments,
                        pos.Value == segment.OriginalEnd.Value && pos.Value != segment.OriginalStart.Value));
                }

                start = end;
            }

            return groups.ToArray();
        }
    }

    /// <summary>Go: <c>type segmentGroupAtOriginalPosition struct</c>（未导出类型 → C# internal + PascalCase）。</summary>
    internal readonly struct SegmentGroupAtOriginalPosition
    {
        internal readonly Segment[] _segments;
        internal readonly bool _atEnd;

        internal SegmentGroupAtOriginalPosition(Segment[] segments, bool atEnd)
        {
            _segments = segments;
            _atEnd = atEnd;
        }
    }
}

/// <summary>
/// Go: <c>type MappingErrorKind int32</c>。内容映射器必须提供合法 span map；这些是 map 可能的不合法形态，
/// 让编译器能精确定位到映射器作者要改的地方。
/// </summary>
public enum MappingErrorKind : int
{
    /// <summary>段重叠、逆序，或越过虚拟文本末尾（在虚拟空间里必须有序且不相交）。</summary>
    Overlap = 0,

    /// <summary>段的原文区间落在原文文本之外。</summary>
    OutOfBounds = 1,

    /// <summary>verbatim 段的虚拟文本与原文文本不一致。</summary>
    VerbatimMismatch = 2,

    /// <summary>段用了不支持的映射 Kind。</summary>
    Kind = 3,

    /// <summary>feature 标注里含不支持的位。</summary>
    Feature = 4,
}

/// <summary>
/// Go: <c>type MappingError struct</c>。单条 span map 校验失败，带上涉及的偏移量。
/// Go 的 <c>Error() string</c>（error 接口）→ C# 的异常（docs/architecture.md §3.2：Go 的 error 值在 C# 侧就是异常，
/// <c>errors.AsType[*spanmap.MappingError]</c> → <c>catch (MappingError)</c>）；<c>Error()</c> 的文本进 <c>Message</c>。
/// </summary>
public sealed class MappingError : Exception
{
    public MappingErrorKind Kind;
    public TextPos VirtualPos;
    public TextPos OriginalPos;

    public MappingError(MappingErrorKind kind, TextPos virtualPos, TextPos originalPos)
    {
        Kind = kind;
        VirtualPos = virtualPos;
        OriginalPos = originalPos;
    }

    public override string Message => ErrorText();

    public override string ToString() => ErrorText();

    private string ErrorText()
    {
        switch (Kind)
        {
            case MappingErrorKind.Overlap:
                return $"content mapper position mappings overlap or are out of order near virtual offset {VirtualPos.Value}";
            case MappingErrorKind.OutOfBounds:
                return $"content mapper position mapping points outside the original content at original offset {OriginalPos.Value}";
            case MappingErrorKind.VerbatimMismatch:
                return $"content mapper verbatim mapping does not match the original content at virtual offset {VirtualPos.Value}, original offset {OriginalPos.Value}";
            case MappingErrorKind.Kind:
                return $"content mapper position mapping has an invalid kind at virtual offset {VirtualPos.Value}";
            case MappingErrorKind.Feature:
                return $"content mapper position mappings have invalid features near original offset {OriginalPos.Value}";
            default:
                return "content mapper produced an invalid position mapping";
        }
    }
}

/// <summary>
/// Go 的 <c>func (f Fidelity) IsExact/IsSingleSegment/IsNone() bool</c>：C# 的 enum 不能声明方法，落成扩展方法
/// （§3.3；调用点 <c>f.IsExact()</c> 与 Go 同形）。静态类不能叫 <c>SpanMap</c>（与类型重名），故用本名。
/// </summary>
public static class SpanMapExtensions
{
    /// <summary>映射是否完全忠实（输入落在单个 verbatim 段内），可以承载写回原文的文本编辑。</summary>
    public static bool IsExact(this Fidelity f) => f == Fidelity.Exact;

    /// <summary>输入是否只落在一个段内（verbatim 或 atom），即结果是具体位置而不是跨边界的近似或合成空隙。</summary>
    public static bool IsSingleSegment(this Fidelity f) => f == Fidelity.Exact || f == Fidelity.Atom;

    /// <summary>输入是否没有原文对应物（结果是合成空隙）。</summary>
    public static bool IsNone(this Fidelity f) => f == Fidelity.None;
}
