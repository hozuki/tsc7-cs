// ported from: internal/collections/syncmap_test.go
//
// 说明：
// - 这个测试同时是 C# 侧"SyncMap 能不能存 null 值"的守门测试：Go 的 sync.Map 存 any，允许 nil
//   （syncmap_test.go 就有 `m.Store("foo", nil)`），而 ConcurrentDictionary 不允许 null 值，
//   所以 SyncMap 内部用 ConcurrentDictionary<K, Slot>（Slot 是值类型）来表达"键存在但值是 nil"。
// - Go 的 `var m collections.SyncMap[string, any]` → C# 的 `new SyncMap<string, object?>()`
//   （Go 的 `any` = C# 的 `object?`）。
// - Go 的 `assert.Equal(t, got1, nil)` → Assert.Null(got1)。
// - Go 的 `got2, ok := ...`/`too, loaded := ...` 里 ok 被复用；C# 的解构声明不能复用，故用 ok2。
using TypeScript.Collections;
using Xunit;

namespace TypeScript.Tests;

public class CollectionsSyncMapTests
{
    [Fact]
    public void TestSyncMapWithNil()
    {
        var m = new SyncMap<string, object?>();

        var (got1, ok) = m.Load("foo");
        Assert.False(ok);
        Assert.Null(got1);

        m.Store("foo", null);

        var (got2, ok2) = m.Load("foo");
        Assert.True(ok2);
        Assert.Null(got2);

        var (too, loaded) = m.LoadOrStore("too", null);
        Assert.False(loaded);
        Assert.Null(too);

        m.Range((k, v) => true);
    }
}
