// ported from: internal/project/dirty/syncmap_test.go
//
// 说明（与 Go 测试的差异，逐条）：
//
// - Go 的 `assert.Assert(t, ok, "…")` / `assert.Equal(t, want, got)` → xunit 的 `Assert.True(ok)` /
//   `Assert.Equal(got, want)`（xunit 的参数顺序是 (expected, actual)）。
// - Go 的 `*testValue` 指针条目比较用 `assert.Equal(t, entry2, entry1.proxyFor)`（Go 里是指针相等）；
//   C# 侧是引用类型，对应 `Assert.Same(entry2, entry1._proxyFor)`。
// - Go 的 `sync.WaitGroup` + goroutine → `Task.Run` + `Task.WaitAll`（语义一致：等两个并发动作都完成）。
//   这两个子测试本来就是**并发**测试（Go 侧靠 -race 抓数据竞争），这里同样保留并发形态。
// - Go 的同 package 直接读未导出字段 `e.proxyFor` → C# 靠 Properties/AssemblyInfo.cs 的
//   InternalsVisibleTo("TypeScript.Compiler.Tests") 读 `_proxyFor`（内部字段，测试可见）。
// - 每个 Go 子测试（t.Run）落成一个 [Fact]。
using TypeScript.Project.Dirty;
using Xunit;
using static TypeScript.Project.Dirty.Dirty;

namespace TypeScript.Tests;

public class ProjectDirtySyncMapTests
{
    /// <summary>Go 的 <c>testValue</c>（dirty 包测试里最简单的可克隆值）。</summary>
    private sealed class TestValue(string data) : Cloneable<TestValue>
    {
        internal string Data = data;

        public TestValue Clone()
        {
            return new TestValue(Data);
        }
    }

    [Fact]
    public async Task TestSyncMapProxyForRaceCondition()
    {
        // Create a sync map with a base value
        var @base = new Dictionary<string, TestValue> { ["key1"] = new("original") };
        var syncMap = NewSyncMap(@base);

        // Load the same entry from multiple goroutines to simulate race condition
        SyncMapEntry<string, TestValue>? entry1 = null;
        SyncMapEntry<string, TestValue>? entry2 = null;
        var loads = new[]
        {
            Task.Run(() =>
            {
                var (entry, ok) = syncMap.Load("key1");
                Assert.True(ok, "entry1 should be loaded");
                entry1 = entry;
            }),
            Task.Run(() =>
            {
                var (entry, ok) = syncMap.Load("key1");
                Assert.True(ok, "entry2 should be loaded");
                entry2 = entry;
            }),
        };
        await Task.WhenAll(loads);

        // Both entries should exist and have the same initial value
        Assert.Equal("original", entry1!.Value().Data);
        Assert.Equal("original", entry2!.Value().Data);
        Assert.False(entry1.Dirty());
        Assert.False(entry2.Dirty());

        // Now try to change both entries concurrently to trigger the proxy mechanism.
        // (This change doesn't actually have to be concurrent to test the proxy behavior,
        // but might exercise concurrency safety.)
        var changes = new[]
        {
            Task.Run(() => entry1.Change(v => v.Data = "changed_by_entry1")),
            Task.Run(() => entry2.Change(v => v.Data = "changed_by_entry2")),
        };
        await Task.WhenAll(changes);

        // After the race, one entry should have proxyFor set and both should reflect the same final state
        // The exact final value depends on which goroutine wins the race, but both entries should be consistent
        var finalValue1 = entry1.Value().Data;
        var finalValue2 = entry2.Value().Data;
        Assert.Equal(finalValue1, finalValue2);

        // Both entries should be marked as dirty
        Assert.True(entry1.Dirty());
        Assert.True(entry2.Dirty());

        // At least one entry should have proxyFor set (the one that lost the race)
        var hasProxy = entry1._proxyFor != null || entry2._proxyFor != null;
        Assert.True(hasProxy, "at least one entry should have proxyFor set");

        // If entry1 has a proxy, it should point to entry2, and vice versa
        if (entry1._proxyFor != null)
        {
            Assert.Same(entry2, entry1._proxyFor);
        }

        if (entry2._proxyFor != null)
        {
            Assert.Same(entry1, entry2._proxyFor);
        }
    }

    [Fact]
    public void TestSyncMapProxyOperationsDelegation()
    {
        var @base = new Dictionary<string, TestValue> { ["key1"] = new("original") };
        var syncMap = NewSyncMap(@base);

        // Load two entries for the same key
        var (entry1, ok1) = syncMap.Load("key1");
        Assert.True(ok1);
        var (entry2, ok2) = syncMap.Load("key1");
        Assert.True(ok2);

        // Force one to become a proxy by making them both dirty in sequence
        entry1!.Change(v => v.Data = "changed_by_entry1");
        entry2!.Change(v => v.Data = "changed_by_entry2");

        // Determine which is the proxy and which is the target
        SyncMapEntry<string, TestValue> proxy;
        SyncMapEntry<string, TestValue> target;
        if (entry1._proxyFor != null)
        {
            proxy = entry1;
            target = entry2;
        }
        else
        {
            proxy = entry2;
            target = entry1;
        }

        // Test that proxy operations are delegated to the target
        // Change through proxy should affect target
        proxy.Change(v => v.Data = "changed_through_proxy");
        Assert.Equal("changed_through_proxy", target.Value().Data);
        Assert.Equal("changed_through_proxy", proxy.Value().Data);

        // ChangeIf through proxy should work
        var changed = proxy.ChangeIf(
            v => v.Data == "changed_through_proxy",
            v => v.Data = "conditional_change");
        Assert.True(changed);
        Assert.Equal("conditional_change", target.Value().Data);
        Assert.Equal("conditional_change", proxy.Value().Data);

        // Dirty status should be consistent
        Assert.Equal(target.Dirty(), proxy.Dirty());

        // Locked operations should work through proxy
        proxy.Locked(v => v.Change(val => val.Data = "locked_change"));
        Assert.Equal("locked_change", target.Value().Data);
        Assert.Equal("locked_change", proxy.Value().Data);
    }

    [Fact]
    public void TestSyncMapProxyDeleteOperations()
    {
        var @base = new Dictionary<string, TestValue> { ["key1"] = new("original") };
        var syncMap = NewSyncMap(@base);

        // Load two entries and make one a proxy
        var (entry1, _) = syncMap.Load("key1");
        var (entry2, _) = syncMap.Load("key1");

        entry1!.Change(v => v.Data = "modified");
        entry2!.Change(v => v.Data = "modified2");

        // Determine which is the proxy
        var proxy = entry1._proxyFor != null ? entry1 : entry2;

        // Delete through proxy should affect target
        proxy.Delete();

        // Both should reflect the deletion
        var (_, exists) = syncMap.Load("key1");
        Assert.False(exists, "key should be deleted from sync map");

        // DeleteIf through proxy should work
        var @base2 = new Dictionary<string, TestValue> { ["key2"] = new("test") };
        var syncMap2 = NewSyncMap(@base2);

        var (entry3, _) = syncMap2.Load("key2");
        var (entry4, _) = syncMap2.Load("key2");

        entry3!.Change(v => v.Data = "modified");
        entry4!.Change(v => v.Data = "modified2");

        var proxy2 = entry3._proxyFor != null ? entry3 : entry4;

        proxy2.DeleteIf(v => v.Data == "modified2" || v.Data == "modified");

        var (_, exists2) = syncMap2.Load("key2");
        Assert.False(exists2, "key2 should be deleted conditionally");
    }

    [Fact]
    public void TestSyncMapNoProxyWhenNoRace()
    {
        var @base = new Dictionary<string, TestValue> { ["key1"] = new("original") };
        var syncMap = NewSyncMap(@base);

        // Load and modify a single entry - no race condition
        var (entry, ok) = syncMap.Load("key1");
        Assert.True(ok);

        entry!.Change(v => v.Data = "changed");

        // Should not have a proxy since there was no race
        Assert.True(entry._proxyFor == null, "entry should not have proxyFor when no race occurs");
        Assert.True(entry.Dirty());
        Assert.Equal("changed", entry.Value().Data);
    }
}
