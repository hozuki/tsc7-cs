// ported from: internal/fswatch/eventlist_test.go
//
// 说明：
// - Go 的 `el.drain() ([]Event, error)` 在 C# 里是元组 `(Event[] Events, Exception? Err)`（error 是数据，不能抛），
//   所以断言写成 `var (events, err) = el.Drain();` 后分别检查。
// - Go 的 `t.Fatalf("… %v", got)` 里 got 是 []Event → C# 的错误文本用 `Describe(events)` 拼出来（Go 的 %v 等价物）。
// - Go 测试是从同一个 package 里访问未导出类型的；C# 靠 `Properties/AssemblyInfo.cs` 的 InternalsVisibleTo。
using TypeScript.FsWatch;

using Xunit;

namespace TypeScript.Tests;

public class EventListTests
{
    [Fact]
    public void TestEventListCreateThenDelete()
    {
        var el = new EventList();
        el.Create("a");
        el.Remove("a");

        Assert.Equal(1, el.Size());
        Assert.Empty(el.GetEvents());
    }

    [Fact]
    public void TestEventListDeleteThenCreate()
    {
        var el = new EventList();
        el.Remove("a");
        el.Create("a");

        var events = el.GetEvents();
        Assert.Single(events);

        // "Assume update event when rapidly removed and created".
        Assert.Equal(EventKind.Update, events[0].Kind);
    }

    [Fact]
    public void TestEventListCreateDeleteCreate()
    {
        var el = new EventList();
        el.Create("a");
        el.Remove("a");
        el.Create("a");

        var events = el.GetEvents();
        Assert.Single(events);
        Assert.Equal(EventKind.Update, events[0].Kind);
    }

    [Fact]
    public void TestEventListErrorIsLatchedAndCleared()
    {
        var el = new EventList();
        Assert.False(el.HasError());
        Assert.Null(el.GetError());

        el.SetError(new InvalidOperationException("first"));
        el.SetError(new InvalidOperationException("second")); // only first wins
        Assert.True(el.HasError());
        Assert.Equal("first", el.GetError()!.Message);

        el.Clear();
        Assert.False(el.HasError());
        Assert.Null(el.GetError());
    }

    [Fact]
    public void TestEventListDrainIsAtomic()
    {
        var el = new EventList();
        el.Create("a");
        el.Update("b");
        el.SetError(new InvalidOperationException("oops"));

        var (events, err) = el.Drain();
        Assert.NotNull(err);
        Assert.Equal(2, events.Length);

        var (events2, err2) = el.Drain();
        Assert.Null(err2);
        Assert.Empty(events2);
    }

    [Fact]
    public void TestEventListDrainReturnsErrorWithEvents()
    {
        var el = new EventList();
        el.Create("file.txt");
        el.SetError(new InvalidOperationException("overflow"));

        var (events, err) = el.Drain();
        Assert.NotNull(err);
        Assert.Single(events);
    }

    [Fact]
    public void TestEventListDrainForSequences()
    {
        var el = new EventList();
        el.Create("file.txt");
        var startAfterCreate = el.Sequence();
        el.Remove("file.txt");

        var (eventsByCallback, err) = el.DrainForSequences([0, startAfterCreate]);
        Assert.Null(err);
        Assert.Empty(eventsByCallback[0]);
        Assert.Single(eventsByCallback[1]);
        Assert.Equal(EventKind.Delete, eventsByCallback[1][0].Kind);
        Assert.Equal("file.txt", eventsByCallback[1][0].Path);
    }
}
