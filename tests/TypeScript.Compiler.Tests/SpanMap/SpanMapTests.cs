// ported from: internal/spanmap/spanmap_test.go
//
// 与 Go 的差异：
// - Go 的 `spanmap.Segment{...}` 字面量 → 本文件的 `Verbatim/Atom/Alias` 三个小助手（字段名照抄 Go，只省掉重复噪音）；
//   Go 里没写 Features 的段是零值（FeatureNone），助手默认给 `Feature.All`，显式写 FeatureNone 的用例照显式传。
// - Go 的 `(T, error)`：`Marshal`/`Unmarshal` 在 C# 里没有 error（出错抛 JsonException），所以 `assert.NilError` 一律消失。
// - `[]byte("...")` → UTF-8 字面量 `"..."u8`（§3.6）。
// - `assert.DeepEqual` 的切片比较 → xunit 的集合相等（元素是 struct，值语义）。
// - `t.Run(name, ...)` 的表驱动用例 → `[Theory]`/`[MemberData]`（docs/architecture.md §5）。
// - **跳过** 4 个 nil 接收者用例（TestVirtualToOriginalSpanNilIdentity / TestMapPositionNilIdentity /
//   TestOriginalToVirtualSpanNilIdentity / TestValidateNilIsValid）与 1 个 Benchmark：C# 的实例方法不能在 null 上
//   分派（Go 的 `var m *spanmap.SpanMap` 恒等映射在 C# 里由调用点的 `map?.X(...) ?? 默认值` 表达，
//   见 SpanMap.cs 文件头第 6 条）；C# 侧没有与 Go benchmark 对应的基础设施。详见报告。
using System.Text;
using TypeScript.Core;
using TypeScript.SpanMap;
using Xunit;

using static TypeScript.Core.Core;
using static TypeScript.SpanMap.SpanMap;

namespace TypeScript.Tests;

public class SpanMapTests
{
    private static Segment Verbatim(int virtualStart, int virtualEnd, int originalStart, int originalEnd, Feature features = Feature.All) =>
        new()
        {
            VirtualStart = virtualStart,
            VirtualEnd = virtualEnd,
            OriginalStart = originalStart,
            OriginalEnd = originalEnd,
            Kind = Kind.Verbatim,
            Features = features,
        };

    private static Segment Atom(int virtualStart, int virtualEnd, int originalStart, int originalEnd, Feature features = Feature.All) =>
        new()
        {
            VirtualStart = virtualStart,
            VirtualEnd = virtualEnd,
            OriginalStart = originalStart,
            OriginalEnd = originalEnd,
            Kind = Kind.Atom,
            Features = features,
        };

    private static Segment Alias(int virtualStart, int virtualEnd, int originalStart, int originalEnd, Feature features = Feature.All) =>
        new()
        {
            VirtualStart = virtualStart,
            VirtualEnd = virtualEnd,
            OriginalStart = originalStart,
            OriginalEnd = originalEnd,
            Kind = Kind.Alias,
            Features = features,
        };

    [Fact]
    public void TestVirtualToOriginalSpanVerbatim()
    {
        // 虚拟 [0,10) 是原文 [100,110) 的逐字拷贝。
        var m = New([Verbatim(0, 10, 100, 110)]);

        var (got, fidelity) = m.VirtualToOriginalSpan(NewTextRange(3, 7));
        Assert.Equal(103, got.Pos());
        Assert.Equal(107, got.End());
        Assert.Equal(Fidelity.Exact, fidelity);
    }

    [Fact]
    public void TestVirtualToOriginalSpanAtom()
    {
        // 虚拟 [0,3) 是合成空隙；[3,14)（"MyComponent"）是原文 [60,71) 的 atom。
        var m = New([Atom(3, 14, 60, 71)]);

        // atom 内部的区间映射到整个 atom 区间。
        var (got, fidelity) = m.VirtualToOriginalSpan(NewTextRange(5, 9));
        Assert.Equal(60, got.Pos());
        Assert.Equal(71, got.End());
        Assert.Equal(Fidelity.Atom, fidelity);
    }

    [Fact]
    public void TestVirtualAlias()
    {
        var m = New([Alias(3, 6, 10, 11)]);

        var (got, fidelity) = m.VirtualToOriginalSpan(NewTextRange(3, 6));
        Assert.Equal(NewTextRange(10, 11), got);
        Assert.Equal(Fidelity.Atom, fidelity);

        Assert.True(m.TryAliasForVirtualSpan(NewTextRange(3, 6), out var alias));
        Assert.Equal(Kind.Alias, alias.Kind);
        Assert.False(m.TryAliasForVirtualSpan(NewTextRange(4, 6), out _));

        var data = m.Marshal();
        var decoded = Unmarshal(data);
        Assert.Equal(Kind.Alias, decoded.Segments()[0].Kind);
    }

    [Fact]
    public void TestVirtualToOriginalSpanSynthesizedGap()
    {
        // 两个逐字段之间的空隙是合成的：映射到插入点（前一段的原文末尾），忠实度为 None。
        var m = New([Verbatim(0, 10, 100, 110), Verbatim(20, 30, 200, 210)]);

        var (got, fidelity) = m.VirtualToOriginalSpan(NewTextRange(12, 15));
        Assert.Equal(110, got.Pos());
        Assert.Equal(110, got.End());
        Assert.Equal(Fidelity.None, fidelity);
    }

    [Fact]
    public void TestOriginalToVirtualIntersectingSpansAllowsUncoveredEndpoints()
    {
        var m = New([Verbatim(10, 20, 100, 110, Feature.SemanticTokens | Feature.InlayHints)]);

        foreach (var feature in new[] { Feature.SemanticTokens, Feature.InlayHints })
        {
            var got = m.OriginalToVirtualIntersectingSpans(NewTextRange(90, 120), feature);
            Assert.Single(got);
            Assert.Equal(NewTextRange(10, 20), got[0].Span);
            Assert.Equal(Fidelity.Exact, got[0].Fidelity);
        }
    }

    [Fact]
    public void TestVirtualToOriginalSpanEmptyIsSynthesized()
    {
        // 空 map 表示输出全是合成的：一切都映射到起点，忠实度为 None。
        var m = New(null);
        var (got, fidelity) = m.VirtualToOriginalSpan(NewTextRange(5, 10));
        Assert.Equal(0, got.Pos());
        Assert.Equal(0, got.End());
        Assert.Equal(Fidelity.None, fidelity);
    }

    [Fact]
    public void TestVirtualToOriginalSpanCrossingSegments()
    {
        var m = New(
        [
            new Segment { VirtualStart = 0, VirtualEnd = 10, OriginalStart = 100, OriginalEnd = 110, Kind = Kind.Verbatim },
            new Segment { VirtualStart = 10, VirtualEnd = 20, OriginalStart = 200, OriginalEnd = 210, Kind = Kind.Verbatim },
        ]);

        var (got, fidelity) = m.VirtualToOriginalSpan(NewTextRange(5, 15));
        Assert.Equal(105, got.Pos());
        Assert.Equal(205, got.End());
        Assert.Equal(Fidelity.Approximate, fidelity);
    }

    public static IEnumerable<object[]> VirtualToOriginalPositionData()
    {
        yield return ["verbatim interpolates", 3, 103, Fidelity.Exact];
        yield return ["atom maps to its start", 25, 200, Fidelity.Atom];
        yield return ["gap maps to insertion point", 15, 110, Fidelity.None];
    }

    [Theory]
    [MemberData(nameof(VirtualToOriginalPositionData))]
    public void TestVirtualToOriginalPosition(string name, int pos, int want, Fidelity wantFidelity)
    {
        _ = name;

        // 虚拟 [0,10) 是原文 [100,110) 的逐字拷贝；[20,30) 是原文 [200,210) 的 atom。
        var m = New([Verbatim(0, 10, 100, 110), Atom(20, 30, 200, 210)]);

        var (got, fidelity) = m.VirtualToOriginalPosition(pos);
        Assert.Equal(want, got.Value);
        Assert.Equal(wantFidelity, fidelity);

        // VirtualToOriginalPosition 必须与零长度区间的 VirtualToOriginalSpan 一致。
        var (span, spanFidelity) = m.VirtualToOriginalSpan(NewTextRange(pos, pos));
        Assert.Equal(got.Value, span.Pos());
        Assert.Equal(fidelity, spanFidelity);
    }

    public static IEnumerable<object[]> VirtualToOriginalPositionExactData()
    {
        yield return [5, 105, true];
        yield return [10, 110, false];
        yield return [15, 110, false];
        yield return [20, 120, false];
        yield return [25, 125, true];
    }

    [Theory]
    [MemberData(nameof(VirtualToOriginalPositionExactData))]
    public void TestVirtualToOriginalPositionExact(int pos, int want, bool ok)
    {
        var m = New([Verbatim(0, 10, 100, 110), Atom(10, 20, 110, 120), Verbatim(20, 30, 120, 130)]);

        var actual = m.TryVirtualToOriginalPositionExact(pos, out var mapped);
        Assert.Equal(want, mapped.Value);
        Assert.Equal(ok, actual);
    }

    [Fact]
    public void TestVirtualToOriginalPositionExactRejectsDiscontinuousBoundary()
    {
        var m = New(
        [
            new Segment { VirtualStart = 0, VirtualEnd = 10, OriginalStart = 0, OriginalEnd = 10, Kind = Kind.Verbatim },
            new Segment { VirtualStart = 10, VirtualEnd = 20, OriginalStart = 100, OriginalEnd = 110, Kind = Kind.Verbatim },
        ]);

        var ok = m.TryVirtualToOriginalPositionExact(10, out var mapped);
        Assert.Equal(100, mapped.Value);
        Assert.False(ok);
    }

    [Fact]
    public void TestZeroLengthSpansAtSegmentEnds()
    {
        var m = New([Verbatim(0, 10, 100, 110, Feature.Hover), Verbatim(20, 30, 200, 210, Feature.Hover)]);

        var (position, fidelity) = m.VirtualToOriginalPosition(30);
        Assert.Equal(210, position.Value);
        Assert.Equal(Fidelity.Exact, fidelity);

        var (virtualSpan, spanFidelity) = m.VirtualToOriginalSpan(NewTextRange(30, 30));
        Assert.Equal(NewTextRange(210, 210), virtualSpan);
        Assert.Equal(Fidelity.Exact, spanFidelity);

        foreach (var originalEnd in new[] { 110, 210 })
        {
            var positions = m.OriginalToVirtualPositions(originalEnd, Feature.Hover);
            var spans = m.OriginalToVirtualSpans(NewTextRange(originalEnd, originalEnd), Feature.Hover);
            Assert.Single(positions);
            Assert.Single(spans);
            Assert.Equal(NewTextRange(positions[0].Position, positions[0].Position), spans[0].Span);
            Assert.Equal(positions[0].Fidelity, spans[0].Fidelity);
        }
    }

    [Fact]
    public void TestOriginalToVirtualSpanVerbatim()
    {
        // 虚拟 [0,10) 是原文 [100,110) 的逐字拷贝。
        var m = New([Verbatim(0, 10, 100, 110)]);

        var results = m.OriginalToVirtualSpans(NewTextRange(103, 107), Feature.All);
        Assert.Single(results);
        Assert.Equal(3, results[0].Span.Pos());
        Assert.Equal(7, results[0].Span.End());
        Assert.Equal(Fidelity.Exact, results[0].Fidelity);
    }

    [Fact]
    public void TestOriginalToVirtualSpanAtom()
    {
        // 虚拟 [3,14) 是原文 [60,71) 的 atom。
        var m = New([Atom(3, 14, 60, 71)]);

        // 原文 atom 内部的区间映射到整个虚拟区间。
        var results = m.OriginalToVirtualSpans(NewTextRange(63, 67), Feature.All);
        Assert.Single(results);
        Assert.Equal(3, results[0].Span.Pos());
        Assert.Equal(14, results[0].Span.End());
        Assert.Equal(Fidelity.Atom, results[0].Fidelity);
    }

    [Fact]
    public void TestOriginalToVirtualSpanGap()
    {
        // 没有段覆盖的原文区间没有虚拟对应物。
        var m = New([Verbatim(0, 10, 100, 110), Verbatim(20, 30, 200, 210)]);

        Assert.Empty(m.OriginalToVirtualSpans(NewTextRange(150, 160), Feature.All));
    }

    public static IEnumerable<object[]> OriginalToVirtualPositionsData()
    {
        yield return ["verbatim interpolates", 103, 3, Fidelity.Exact];
        yield return ["atom maps to its start", 205, 20, Fidelity.Atom];
        yield return ["gap has no projection", 150, 0, Fidelity.None];
    }

    [Theory]
    [MemberData(nameof(OriginalToVirtualPositionsData))]
    public void TestOriginalToVirtualPositions(string name, int pos, int want, Fidelity wantFidelity)
    {
        _ = name;

        // 原文 [100,110) 是虚拟 [0,10) 的逐字拷贝；[200,210) 是虚拟 [20,30) 的 atom。
        var m = New([Verbatim(0, 10, 100, 110), Atom(20, 30, 200, 210)]);

        var positions = m.OriginalToVirtualPositions(pos, Feature.All);
        var spans = m.OriginalToVirtualSpans(NewTextRange(pos, pos), Feature.All);
        if (wantFidelity.IsNone())
        {
            Assert.Empty(positions);
            Assert.Empty(spans);
            return;
        }

        Assert.Single(positions);
        Assert.Equal(want, positions[0].Position.Value);
        Assert.Equal(wantFidelity, positions[0].Fidelity);
        Assert.Single(spans);
        Assert.Equal(want, spans[0].Span.Pos());
        Assert.Equal(wantFidelity, spans[0].Fidelity);
    }

    [Fact]
    public void TestOriginalToVirtualPositionsAtEndpoint()
    {
        var m = New(
        [
            Verbatim(2, 5, 10, 13, Feature.Completion),
            Verbatim(8, 11, 13, 16, Feature.Completion),
            Atom(20, 23, 30, 35, Feature.Completion),
        ]);

        Assert.Equal(
            [new MappedPosition(5, Fidelity.Exact), new MappedPosition(8, Fidelity.Exact)],
            m.OriginalToVirtualPositions(13, Feature.Completion));
        Assert.Equal(
            [new MappedPosition(23, Fidelity.Atom)],
            m.OriginalToVirtualPositions(35, Feature.Completion));

        var filtered = New([Verbatim(20, 23, 10, 13, Feature.Hover), Verbatim(2, 5, 13, 16, Feature.Completion)]);
        Assert.Equal(
            [new MappedPosition(2, Fidelity.Exact), new MappedPosition(23, Fidelity.Exact)],
            filtered.OriginalToVirtualPositions(13, Feature.All));
        Assert.Equal(
            [new MappedPosition(2, Fidelity.Exact)],
            filtered.OriginalToVirtualPositions(13, Feature.Completion));
    }

    [Fact]
    public void TestOriginalToVirtualDuplicateGroup()
    {
        var m = New(
        [
            Verbatim(0, 3, 10, 13, Feature.Definition),
            Verbatim(10, 13, 10, 13, Feature.Hover),
            Atom(20, 25, 10, 13, Feature.Definition),
        ]);

        var semantic = m.OriginalToVirtualPositions(11, Feature.Hover);
        Assert.Single(semantic);
        Assert.Equal(11, semantic[0].Position.Value);
        Assert.Equal(Fidelity.Exact, semantic[0].Fidelity);

        var navigation = m.OriginalToVirtualPositions(11, Feature.Definition);
        Assert.Equal(2, navigation.Length);
        Assert.Equal(1, navigation[0].Position.Value);
        Assert.Equal(Fidelity.Exact, navigation[0].Fidelity);
        Assert.Equal(20, navigation[1].Position.Value);
        Assert.Equal(Fidelity.Atom, navigation[1].Fidelity);

        var spans = m.OriginalToVirtualSpans(NewTextRange(10, 13), Feature.Definition);
        Assert.Equal(2, spans.Length);
        Assert.Equal(0, spans[0].Span.Pos());
        Assert.Equal(3, spans[0].Span.End());
        Assert.Equal(20, spans[1].Span.Pos());
        Assert.Equal(25, spans[1].Span.End());
    }

    [Fact]
    public void TestOriginalToVirtualOverlappingSpans()
    {
        var m = New(
        [
            Verbatim(0, 6, 0, 6, Feature.Hover),
            Verbatim(10, 12, 2, 4, Feature.Hover),
            Verbatim(20, 24, 3, 7, Feature.Hover),
        ]);

        Assert.Equal(
            [
                new MappedPosition(3, Fidelity.Exact),
                new MappedPosition(11, Fidelity.Exact),
                new MappedPosition(20, Fidelity.Exact),
            ],
            m.OriginalToVirtualPositions(3, Feature.Hover));

        var spans = m.OriginalToVirtualSpans(NewTextRange(3, 4), Feature.Hover);
        MappedSpan[] wantSpans =
        [
            new(NewTextRange(3, 4), Fidelity.Exact),
            new(NewTextRange(11, 12), Fidelity.Exact),
            new(NewTextRange(20, 21), Fidelity.Exact),
        ];
        Assert.Equal(wantSpans.Length, spans.Length);
        for (var i = 0; i < spans.Length; i++)
        {
            Assert.Equal(wantSpans[i], spans[i]);
        }
    }

    [Fact]
    public void TestOriginalToVirtualPositionFindsEarlyCoveringSegment()
    {
        // 二分查找会落到 [90,95) 附近，而它不包含 97；区间索引仍必须找到更早的 [0,100) 段，
        // 而不是扫描所有起点在查询之前的段。
        var m = New(
        [
            Verbatim(0, 100, 0, 100, Feature.Hover),
            Verbatim(100, 105, 80, 85, Feature.Hover),
            Verbatim(105, 110, 90, 95, Feature.Hover),
            Verbatim(110, 113, 100, 103, Feature.Hover),
        ]);

        Assert.Equal([new MappedPosition(97, Fidelity.Exact)], m.OriginalToVirtualPositions(97, Feature.Hover));
        var spans = m.OriginalToVirtualSpans(NewTextRange(97, 98), Feature.Hover);
        Assert.Single(spans);
        Assert.Equal(new MappedSpan(NewTextRange(97, 98), Fidelity.Exact), spans[0]);

        // 点查询包含共享端点的两侧（包括通过 max-end 树找到的更早区间）；非空区间查询把段末尾当排他，
        // 只用右边那一段。
        Assert.Equal(
            [new MappedPosition(100, Fidelity.Exact), new MappedPosition(110, Fidelity.Exact)],
            m.OriginalToVirtualPositions(100, Feature.Hover));
        spans = m.OriginalToVirtualSpans(NewTextRange(100, 101), Feature.Hover);
        Assert.Single(spans);
        Assert.Equal(new MappedSpan(NewTextRange(110, 111), Fidelity.Exact), spans[0]);
    }

    [Fact]
    public void TestOriginalToVirtualOverlapFallsBackFromDisabledContainer()
    {
        var m = New(
        [
            Verbatim(0, 6, 0, 6, Feature.Definition),
            Verbatim(10, 13, 0, 3, Feature.Hover),
            Verbatim(13, 16, 3, 6, Feature.Hover),
        ]);

        var spans = m.OriginalToVirtualSpans(NewTextRange(1, 5), Feature.Hover);
        Assert.Single(spans);
        Assert.Equal(new MappedSpan(NewTextRange(11, 15), Fidelity.Approximate), spans[0]);
    }

    [Fact]
    public void TestOriginalToVirtualCrossGroupProjections()
    {
        var m = New(
        [
            Verbatim(0, 2, 0, 2, Feature.Hover),
            Verbatim(2, 4, 2, 4, Feature.Hover),
            Verbatim(10, 12, 0, 2, Feature.Hover),
            Verbatim(12, 14, 2, 4, Feature.Hover),
        ]);

        var spans = m.OriginalToVirtualSpans(NewTextRange(1, 3), Feature.Hover);
        Assert.Equal(2, spans.Length);
        Assert.Equal(NewTextRange(1, 3), spans[0].Span);
        Assert.Equal(NewTextRange(11, 13), spans[1].Span);
        foreach (var mapped in spans)
        {
            Assert.Equal(Fidelity.Approximate, mapped.Fidelity);
        }
    }

    [Fact]
    public void TestOriginalToVirtualExplicitZeroFeatures()
    {
        var m = New([Verbatim(0, 3, 10, 13, Feature.None)]);

        Assert.Empty(m.OriginalToVirtualPositions(11, Feature.Hover));
        Assert.Empty(m.OriginalToVirtualPositions(11, Feature.Definition));
        Assert.Empty(m.OriginalToVirtualSpans(NewTextRange(10, 13), Feature.Hover));

        var data = m.Marshal();
        Assert.Equal("[[0,3,10,3,0,0]]", Encoding.UTF8.GetString(data));
        var decoded = Unmarshal(data);
        Assert.Equal(Feature.None, decoded.Segments()[0].Features);

        // 五元组（旧格式）省略 features，规范化为 FeatureAll。
        var legacy = Unmarshal("[[0,3,10,3,0]]"u8);
        Assert.Equal(Feature.All, legacy.Segments()[0].Features);
        Assert.Single(legacy.OriginalToVirtualPositions(11, Feature.Hover));
    }

    [Fact]
    public void TestFeatureParticipationOriginalAndVirtual()
    {
        var m = New([Verbatim(0, 3, 10, 13, Feature.Hover), Verbatim(3, 6, 20, 23, Feature.Completion)]);

        Assert.Single(m.OriginalToVirtualPositions(11, Feature.Hover));
        Assert.Empty(m.OriginalToVirtualPositions(11, Feature.Completion));

        var (mapped, fidelity) = m.VirtualToOriginalSpanForFeature(NewTextRange(0, 3), Feature.Hover);
        Assert.Equal(NewTextRange(10, 13), mapped);
        Assert.Equal(Fidelity.Exact, fidelity);

        (_, fidelity) = m.VirtualToOriginalSpanForFeature(NewTextRange(0, 3), Feature.Completion);
        Assert.Equal(Fidelity.None, fidelity);

        // 诊断与编辑安全用的是不过滤 feature 的几何，不能被 feature 标记关掉。
        (mapped, fidelity) = m.VirtualToOriginalSpan(NewTextRange(0, 3));
        Assert.Equal(NewTextRange(10, 13), mapped);
        Assert.Equal(Fidelity.Exact, fidelity);
    }

    [Fact]
    public void TestOriginalToVirtualSpanRoundTrip()
    {
        // 原文区间与虚拟区间的顺序相反，用来覆盖反向索引。
        var m = New([Verbatim(0, 10, 200, 210), Verbatim(10, 20, 100, 110)]);

        foreach (var r in new[] { NewTextRange(2, 8), NewTextRange(12, 18) })
        {
            var (orig, fidelity) = m.VirtualToOriginalSpan(r);
            Assert.Equal(Fidelity.Exact, fidelity);
            var back = m.OriginalToVirtualSpans(orig, Feature.All);
            Assert.Single(back);
            Assert.Equal(Fidelity.Exact, back[0].Fidelity);
            Assert.Equal(r.Pos(), back[0].Span.Pos());
            Assert.Equal(r.End(), back[0].Span.End());
        }
    }

    [Fact]
    public void TestMarshalRoundTrip()
    {
        var original = New(
        [
            new Segment { VirtualStart = 3, VirtualEnd = 14, OriginalStart = 60, OriginalEnd = 71, Kind = Kind.Atom },
            new Segment { VirtualStart = 14, VirtualEnd = 24, OriginalStart = 71, OriginalEnd = 81, Kind = Kind.Verbatim },
        ]);

        var data = original.Marshal();
        var decoded = Unmarshal(data);

        foreach (var r in new[] { NewTextRange(1, 2), NewTextRange(4, 10), NewTextRange(16, 20) })
        {
            var (wantRange, wantFidelity) = original.VirtualToOriginalSpan(r);
            var (gotRange, gotFidelity) = decoded.VirtualToOriginalSpan(r);
            Assert.Equal(wantRange, gotRange);
            Assert.Equal(wantFidelity, gotFidelity);
        }
    }

    private const string _transformed = "const greeting = 1;\n";
    private const string _original = "<x>const greeting = 1;\n</x>";

    public static IEnumerable<object[]> ValidateData()
    {
        // original 里 "const" 的下标。
        const int scriptStart = 3;

        yield return
        [
            "valid verbatim",
            new Segment[]
            {
                new() { VirtualStart = 0, VirtualEnd = _transformed.Length, OriginalStart = scriptStart, OriginalEnd = scriptStart + _transformed.Length, Kind = Kind.Verbatim },
            },
            MappingErrorKind.Overlap,
            true,
        ];
        yield return ["empty is valid", null!, MappingErrorKind.Overlap, true];
        yield return
        [
            "gap is allowed",
            new Segment[] { new() { VirtualStart = 3, VirtualEnd = _transformed.Length, OriginalStart = 0, OriginalEnd = 0, Kind = Kind.Atom } },
            MappingErrorKind.Overlap,
            true,
        ];
        yield return
        [
            "overlap",
            new Segment[]
            {
                new() { VirtualStart = 0, VirtualEnd = 10, OriginalStart = 0, OriginalEnd = 0, Kind = Kind.Atom },
                new() { VirtualStart = 5, VirtualEnd = _transformed.Length, OriginalStart = 0, OriginalEnd = 0, Kind = Kind.Atom },
            },
            MappingErrorKind.Overlap,
            false,
        ];
        yield return
        [
            "original out of bounds",
            new Segment[] { new() { VirtualStart = 0, VirtualEnd = _transformed.Length, OriginalStart = 0, OriginalEnd = _original.Length + 10, Kind = Kind.Atom } },
            MappingErrorKind.OutOfBounds,
            false,
        ];
        yield return
        [
            "verbatim text mismatch",
            new Segment[] { new() { VirtualStart = 0, VirtualEnd = _transformed.Length, OriginalStart = 0, OriginalEnd = _transformed.Length, Kind = Kind.Verbatim } },
            MappingErrorKind.VerbatimMismatch,
            false,
        ];
        yield return
        [
            "unknown kind",
            new Segment[] { new() { VirtualStart = 0, VirtualEnd = 1, OriginalStart = 0, OriginalEnd = 1, Kind = (Kind)3 } },
            MappingErrorKind.Kind,
            false,
        ];
    }

    [Theory]
    [MemberData(nameof(ValidateData))]
    public void TestValidate(string name, Segment[] segments, MappingErrorKind wantKind, bool wantOk)
    {
        _ = name;

        var problem = New(segments).Validate(_transformed, _original);
        if (wantOk)
        {
            Assert.Null(problem);
            return;
        }

        Assert.NotNull(problem);
        Assert.Equal(wantKind, problem!.Kind);
    }

    public static IEnumerable<object[]> ValidateOriginalOverlapAndFeaturesData()
    {
        yield return
        [
            "identical duplicate group",
            new Segment[]
            {
                new() { VirtualStart = 0, VirtualEnd = 3, OriginalStart = 0, OriginalEnd = 3, Kind = Kind.Verbatim, Features = Feature.Definition },
                new() { VirtualStart = 3, VirtualEnd = 6, OriginalStart = 0, OriginalEnd = 3, Kind = Kind.Verbatim, Features = Feature.Hover },
            },
            MappingErrorKind.Overlap,
            true,
        ];
        yield return
        [
            "partial original overlap is valid",
            new Segment[]
            {
                new() { VirtualStart = 0, VirtualEnd = 3, OriginalStart = 0, OriginalEnd = 3, Kind = Kind.Atom },
                new() { VirtualStart = 3, VirtualEnd = 6, OriginalStart = 2, OriginalEnd = 5, Kind = Kind.Atom },
            },
            MappingErrorKind.Overlap,
            true,
        ];
        yield return
        [
            "nested original overlap is valid",
            new Segment[]
            {
                new() { VirtualStart = 0, VirtualEnd = 5, OriginalStart = 0, OriginalEnd = 5, Kind = Kind.Atom },
                new() { VirtualStart = 5, VirtualEnd = 6, OriginalStart = 1, OriginalEnd = 4, Kind = Kind.Atom },
            },
            MappingErrorKind.Overlap,
            true,
        ];
        yield return
        [
            "duplicate without explicit features is tolerant",
            new Segment[]
            {
                new() { VirtualStart = 0, VirtualEnd = 3, OriginalStart = 0, OriginalEnd = 3, Kind = Kind.Atom },
                new() { VirtualStart = 3, VirtualEnd = 6, OriginalStart = 0, OriginalEnd = 3, Kind = Kind.Atom, Features = Feature.Definition },
            },
            MappingErrorKind.Overlap,
            true,
        ];
        yield return
        [
            "duplicate with shared feature members is tolerant",
            new Segment[]
            {
                new() { VirtualStart = 0, VirtualEnd = 3, OriginalStart = 0, OriginalEnd = 3, Kind = Kind.Atom, Features = Feature.Hover },
                new() { VirtualStart = 3, VirtualEnd = 6, OriginalStart = 0, OriginalEnd = 3, Kind = Kind.Atom, Features = Feature.Hover | Feature.Definition },
            },
            MappingErrorKind.Overlap,
            true,
        ];
        yield return
        [
            "features on sole cover are valid",
            new Segment[]
            {
                new() { VirtualStart = 0, VirtualEnd = 3, OriginalStart = 0, OriginalEnd = 3, Kind = Kind.Atom, Features = Feature.Definition },
            },
            MappingErrorKind.Overlap,
            true,
        ];
        yield return
        [
            "unknown feature flag",
            new Segment[]
            {
                new() { VirtualStart = 0, VirtualEnd = 3, OriginalStart = 0, OriginalEnd = 3, Kind = Kind.Atom, Features = (Feature)(1 << 22) },
            },
            MappingErrorKind.Feature,
            false,
        ];
    }

    [Theory]
    [MemberData(nameof(ValidateOriginalOverlapAndFeaturesData))]
    public void TestValidateOriginalOverlapAndFeatures(string name, Segment[] segments, MappingErrorKind wantKind, bool valid)
    {
        _ = name;

        var problem = New(segments).Validate("abcabc", "abcdef");
        if (valid)
        {
            Assert.Null(problem);
            return;
        }

        Assert.NotNull(problem);
        Assert.Equal(wantKind, problem!.Kind);
    }
}
