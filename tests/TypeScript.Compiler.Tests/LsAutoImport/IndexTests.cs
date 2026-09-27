// ported from: internal/ls/autoimport/index_test.go
//
// 与 Go 测试的差异（逐条）：
// - Go 的 `idx.entries`（未导出字段）→ C# 的 `idx._entries`；`Index`/`testEntry` 都是 internal/同包可见性，
//   测试程序集靠 `Properties/AssemblyInfo.cs` 的 `InternalsVisibleTo`（与 Go 的"同 package"等价）。
// - Go 的 `assert.Assert(t, cloned == nil)`（nil Index）→ C# 里 `Index` 是 class，Go 的 nil 接收者分支已删除，
//   对应的 C# 表达式是"引用为 null"：这里断言 `idx is null` 的 Clone 由调用方用 `?.` 表达（见下第 2 条）。
// - Go 的 `t.Run(name, …)` 子测试 → 每个子测试一个 `[Fact]`（xunit 的并行粒度）。
using TypeScript.LsAutoImport;
using TypeScript.Collections;
using Xunit;

namespace TypeScript.Tests;

public class LsAutoImportIndexTests
{
    private sealed class TestEntry : INamed
    {
        public string _name = "";

        public string _package = "";

        public string Name() => _name;
    }

    [Fact]
    public void CloneFiltersEntriesByPackage()
    {
        var idx = new Index<TestEntry>();
        idx.InsertAsWords(new TestEntry { _name = "fooBar", _package = "pkg-a" });
        idx.InsertAsWords(new TestEntry { _name = "bazQux", _package = "pkg-b" });
        idx.InsertAsWords(new TestEntry { _name = "fooQux", _package = "pkg-a" });

        // Clone excluding pkg-b
        var cloned = idx.Clone(e => e._package != "pkg-b");

        // Original should have all 3 entries
        Assert.Equal(3, idx._entries.Count);

        // Cloned should have 2 entries (only pkg-a)
        Assert.Equal(2, cloned._entries.Count);

        // Search should work on cloned index
        var results = cloned.Find("fooBar", true);
        Assert.Single(results);
        Assert.Equal("fooBar", results[0]._name);

        // bazQux should not be in cloned index
        results = cloned.Find("bazQux", true);
        Assert.Empty(results);

        // Word prefix search should work
        results = cloned.SearchWordPrefix("foo");
        Assert.Equal(2, results.Length);
    }

    [Fact]
    public void CloneOfNullIndexIsNullViaNullConditional()
    {
        // Go 的 `var idx *Index[*testEntry]; idx.Clone(...)` 靠 nil 接收者返回 nil。C# 的实例方法不能在 null 上分派，
        // 调用点写成 `?.`（同一个 Go 分支的两种写法），所以这里断言的是"该表达式的结果为 null"。
        Index<TestEntry>? idx = null;
        var cloned = idx?.Clone(e => true);
        Assert.Null(cloned);
    }

    [Fact]
    public void CloneOfEmptyIndexIsEmpty()
    {
        var idx = new Index<TestEntry>();
        var cloned = idx.Clone(e => true);
        Assert.Empty(cloned._entries);
    }

    [Fact]
    public void CloneCanFilterOutAllEntries()
    {
        var idx = new Index<TestEntry>();
        idx.InsertAsWords(new TestEntry { _name = "fooBar", _package = "pkg-a" });
        idx.InsertAsWords(new TestEntry { _name = "bazQux", _package = "pkg-b" });

        var cloned = idx.Clone(e => false);
        Assert.Empty(cloned._entries);
        Assert.Empty(cloned._index!);
    }
}
