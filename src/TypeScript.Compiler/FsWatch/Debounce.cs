// ported from: internal/fswatch/debounce.go
//
// C# 映射与偏离（逐条）：
// 1) Go 的 `go d.loop()` 是普通 goroutine（进程退出不依赖它）→ C# 用一条 `IsBackground = true` 的专用线程
//    （前台线程会阻止进程退出）。回调因此在**库线程**上执行，与 Go 的 `loop` goroutine 语义一致：既不是调用者
//    线程，也不是后端事件循环线程。
// 2) Go 用 `chan struct{}` 实现的"可复位闩锁"（waitCh 关闭 = 已通知；triggerCh 每次 trigger 换新）→ C# 用
//    `TaskCompletionSource` 一一对应：`TrySetResult()` = `close(ch)`，`new TaskCompletionSource()` = `make(chan
//    struct{})`，`task.Wait()` = `<-ch`，`task.Wait(timeout)` = `select { case <-ch: …; case <-time.After(…): … }`。
//    （Go 的 channel 关闭语义是"永久就绪"，TCS 完成后的 `Wait` 同样立即返回，行为一致。）
// 3) Go 的 `time.Now()/Sub` 比较 → `Environment.TickCount64`（毫秒；窗口是 50ms/500ms 量级，够用）。零值起点与
//    Go 的 `time.Time{}` 一样让首次 `gap > maxWaitTime` 成立（首次 trigger 立即触发一轮）。
// 4) 包级可变 `minWaitTime`/`maxWaitTime`（测试可改）→ 静态字段 `MinWaitTime`/`MaxWaitTime`（毫秒）。
// 5) Go 的 `map[any]func()` → `Dictionary<object, Action>`（key 是 `DirWatch` 引用，引用相等语义一致）。
namespace TypeScript.FsWatch;

/// <summary>
/// Go: <c>type debounce struct</c>。每个 <see cref="FsWatcher"/> 一个实例（懒创建，随进程存活；空闲时后台线程
/// 阻塞在闩锁上，不消耗 CPU）。按后端隔离，保证某个后端的慢回调不会拖住其它后端的事件投递。
/// </summary>
internal sealed class Debounce
{
    private const long _defaultMinWaitTime = 50;
    private const long _defaultMaxWaitTime = 500;

    /// <summary>Go: 包级 <c>var minWaitTime</c>。</summary>
    internal static long MinWaitTime = _defaultMinWaitTime;

    /// <summary>Go: 包级 <c>var maxWaitTime</c>。</summary>
    internal static long MaxWaitTime = _defaultMaxWaitTime;

    private readonly object _mu = new();
    private readonly Dictionary<object, Action> _callbacks = new();
    private long _lastTime;

    // 闩锁状态：_waitTcs 是常驻闸门（已完成 = 已通知），_triggerTcs 每次 trigger 换新。
    private readonly object _latchMu = new();
    private TaskCompletionSource? _waitTcs;
    private TaskCompletionSource? _triggerTcs;
    private bool _notified;

    internal Debounce()
    {
        var thread = new Thread(Loop)
        {
            IsBackground = true,
            Name = "fswatch-debounce",
        };
        thread.Start();
    }

    /// <summary>Go: <c>func (d *debounce) add(key any, cb func())</c>。</summary>
    internal void Add(object key, Action cb)
    {
        lock (_mu)
        {
            _callbacks[key] = cb;
        }
    }

    /// <summary>Go: <c>func (d *debounce) remove(key any)</c>。</summary>
    internal void Remove(object key)
    {
        lock (_mu)
        {
            _callbacks.Remove(key);
        }
    }

    /// <summary>Go: <c>func (d *debounce) trigger()</c>。唤醒 debounce 线程。</summary>
    internal void Trigger()
    {
        lock (_latchMu)
        {
            if (!_notified)
            {
                _notified = true;
                WaitChLocked().TrySetResult();
            }

            TriggerChLocked().TrySetResult();
            _triggerTcs = new TaskCompletionSource();
        }
    }

    private void Loop()
    {
        while (true)
        {
            LatchWait();
            NotifyIfReady();
        }
    }

    private void NotifyIfReady()
    {
        bool fireNow;
        lock (_mu)
        {
            var now = Environment.TickCount64;
            var gap = now - _lastTime;
            if (gap > MaxWaitTime)
            {
                _lastTime = now;
                fireNow = true;
            }
            else
            {
                fireNow = false;
            }
        }

        if (fireNow)
        {
            FireCallbacks();
            return;
        }

        CoalesceWait();
    }

    private void CoalesceWait()
    {
        TaskCompletionSource trigger;
        lock (_latchMu)
        {
            trigger = TriggerChLocked();
        }

        // select { case <-ch: 什么也不做（下一轮再发）; case <-time.After(minWaitTime): fireCallbacks() }
        if (trigger.Task.Wait((int)MinWaitTime))
        {
            return;
        }

        FireCallbacks();
    }

    /// <summary>Go: <c>func (d *debounce) fireCallbacks()</c>。快照并调用全部已注册回调（回调不在任何锁内执行）。</summary>
    private void FireCallbacks()
    {
        List<Action> cbs;
        lock (_mu)
        {
            _lastTime = Environment.TickCount64;
            cbs = new List<Action>(_callbacks.Count);
            cbs.AddRange(_callbacks.Values);
        }

        LatchReset();

        foreach (var cb in cbs)
        {
            cb();
        }
    }

    private TaskCompletionSource WaitChLocked() => _waitTcs ??= new TaskCompletionSource();

    private TaskCompletionSource TriggerChLocked() => _triggerTcs ??= new TaskCompletionSource();

    private void LatchWait()
    {
        TaskCompletionSource wait;
        lock (_latchMu)
        {
            wait = WaitChLocked();
        }

        wait.Task.Wait();
    }

    private void LatchReset()
    {
        lock (_latchMu)
        {
            if (_notified)
            {
                _notified = false;
                _waitTcs = new TaskCompletionSource();
            }
        }
    }
}
