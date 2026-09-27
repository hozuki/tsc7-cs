// ported from: internal/fswatch/event.go
//
// C# 映射与偏离（逐条）：
// 1) `EventKind` 是 Go 的 `iota + 1` 常量（EventUpdate = 1、EventDelete = 2，零值 = 未知）→ C# 的
//    `enum EventKind`（数值与 Go 一致）；Go 的 `func (k EventKind) String() string` 是**手写 switch**
//    （不是 stringer 生成）→ C# 的扩展方法 `FsWatch.KindString(this EventKind)`（enum 不能覆写 ToString，
//    与 Core 里 `String(this ScriptKind)` 同一处理）。
// 2) Go 的 `eventList.drain() ([]Event, error)` 与 `drainForSequences` 里 error 是**数据**（events 与 error
//    必须同时返回），异常表达不了 → C# 返回元组 `(Event[] Events, Exception? Err)`。这里的 `[]Event` 是
//    "nil 切片即空"的位置（消费点不区分 nil 与空），所以映射成非 nullable 的 `[]`。
// 3) `eventEntry` 在 Go 里是显式取地址的 map 值（`map[string]*eventEntry`）→ C# 的引用类型 `EventEntry`。
// 4) `sync.Mutex` → `lock (_mu)`。Go 里约定"调用方已持锁"的 `xxxLocked` 方法保持同名 `XxxLocked` 且**自身不加锁**
//    （调用点都写在 `lock` 块内），与 Go 的契约逐字对应。
// 5) Go 的 `e.kindSince(seq) (EventKind, bool)` → `TryKindSince(seq, out EventKind kind)`（§3.2 的 (T,bool) 映射；
//    Go 在 "false" 分支返回的是 0，不是合法 EventKind）。
namespace TypeScript.FsWatch;

/// <summary>Go: <c>type EventKind int</c>。<c>EventUpdate</c> = 1、<c>EventDelete</c> = 2。</summary>
public enum EventKind
{
    /// <summary>Go 的零值（不是合法的 Kind，只在 "没有事件" 的中间态出现）。</summary>
    Unknown = 0,

    /// <summary>Go: <c>EventUpdate</c>。create 与 update 对外都是它。</summary>
    Update = 1,

    /// <summary>Go: <c>EventDelete</c>。</summary>
    Delete = 2,
}

/// <summary>Go: <c>type Event struct</c>。一次文件系统变化。</summary>
public readonly struct Event
{
    /// <summary>Go: <c>Event{Kind: …, Path: …}</c>（同 package 之外能写的两个字段）。</summary>
    public Event(EventKind kind, string path)
        : this(kind, path, false)
    {
    }

    /// <summary>Go: <c>Event{…, includedWatchRoot: …}</c>（未导出字段，只在本 package 内构造）。</summary>
    internal Event(EventKind kind, string path, bool includedWatchRoot)
    {
        Kind = kind;
        Path = path;
        IncludedWatchRoot = includedWatchRoot;
    }

    public EventKind Kind { get; }

    public string Path { get; }

    /// <summary>Go: <c>Event.includedWatchRoot</c>（未导出）。</summary>
    internal bool IncludedWatchRoot { get; }
}

/// <summary>
/// Go: <c>type eventEntry struct</c>。debounce 窗口内一个路径的合并状态。
/// </summary>
internal sealed class EventEntry
{
    internal ulong CreatedSeq;
    internal ulong UpdatedSeq;
    internal ulong DeletedSeq;
    internal bool IncludedWatchRoot;

    /// <summary>Go: <c>func (e *eventEntry) isDeleted() bool</c>。</summary>
    internal bool IsDeleted => DeletedSeq > CreatedSeq && DeletedSeq > UpdatedSeq;

    /// <summary>Go: <c>func (e *eventEntry) kindSince(startSeq uint64) (EventKind, bool)</c>。</summary>
    internal bool TryKindSince(ulong startSeq, out EventKind kind)
    {
        if (DeletedSeq > startSeq)
        {
            if (CreatedSeq > startSeq && CreatedSeq < DeletedSeq && UpdatedSeq < DeletedSeq)
            {
                kind = EventKind.Unknown;
                return false;
            }

            kind = EventKind.Delete;
            return true;
        }

        var seq = Math.Max(CreatedSeq, UpdatedSeq);
        if (seq > startSeq)
        {
            kind = EventKind.Update;
            return true;
        }

        kind = EventKind.Unknown;
        return false;
    }
}

/// <summary>
/// Go: <c>type eventList struct</c>。按路径合并一个 debounce 窗口内的文件系统事件：
/// <list type="bullet">
/// <item>delete 之后的 create → update（快速 delete+recreate）。</item>
/// <item>getEvents 跳过"既 create 又 delete"的条目。</item>
/// </list>
/// </summary>
internal sealed class EventList
{
    private readonly object _mu = new();
    private Dictionary<string, EventEntry>? _entries;
    private Exception? _err;
    private ulong _seq;

    /// <summary>Go: <c>func (el *eventList) create(path string)</c>。</summary>
    internal void Create(string path)
    {
        lock (_mu)
        {
            var seq = NextSeqLocked();
            CreateLocked(path, seq);
        }
    }

    /// <summary>Go: <c>func (el *eventList) createAt(path string, seq uint64)</c>。</summary>
    internal void CreateAt(string path, ulong seq)
    {
        lock (_mu)
        {
            AdvanceSeqLocked(seq);
            CreateLocked(path, seq);
        }
    }

    /// <summary>Go: <c>func (el *eventList) createLocked(path string, seq uint64)</c>（调用方持锁）。</summary>
    private void CreateLocked(string path, ulong seq)
    {
        var entry = GetOrCreate(path);
        if (entry.IsDeleted)
        {
            // 快速 delete+recreate：清掉两个标记，让该条目按"未删除"的默认路径发 EventUpdate。
            // https://github.com/parcel-bundler/watcher/issues/72
            entry.DeletedSeq = 0;
            entry.CreatedSeq = 0;
            entry.UpdatedSeq = seq;
        }
        else
        {
            entry.CreatedSeq = seq;
        }
    }

    /// <summary>Go: <c>func (el *eventList) update(path string)</c>。</summary>
    internal void Update(string path)
    {
        lock (_mu)
        {
            var seq = NextSeqLocked();
            UpdateLocked(path, seq);
        }
    }

    /// <summary>Go: <c>func (el *eventList) updateAt(path string, seq uint64)</c>。</summary>
    internal void UpdateAt(string path, ulong seq)
    {
        lock (_mu)
        {
            AdvanceSeqLocked(seq);
            UpdateLocked(path, seq);
        }
    }

    /// <summary>Go: <c>func (el *eventList) updateWatchRootAt(path string, seq uint64)</c>。</summary>
    internal void UpdateWatchRootAt(string path, ulong seq)
    {
        lock (_mu)
        {
            AdvanceSeqLocked(seq);
            UpdateLocked(path, seq);
            GetOrCreate(path).IncludedWatchRoot = true;
        }
    }

    private void UpdateLocked(string path, ulong seq)
    {
        GetOrCreate(path).UpdatedSeq = seq;
    }

    /// <summary>Go: <c>func (el *eventList) remove(path string)</c>。</summary>
    internal void Remove(string path)
    {
        lock (_mu)
        {
            var seq = NextSeqLocked();
            RemoveLocked(path, seq);
        }
    }

    /// <summary>Go: <c>func (el *eventList) removeAndGetSequence(path string) uint64</c>。</summary>
    internal ulong RemoveAndGetSequence(string path)
    {
        lock (_mu)
        {
            var seq = NextSeqLocked();
            RemoveLocked(path, seq);
            return seq;
        }
    }

    /// <summary>Go: <c>func (el *eventList) removeAt(path string, seq uint64)</c>。</summary>
    internal void RemoveAt(string path, ulong seq)
    {
        lock (_mu)
        {
            AdvanceSeqLocked(seq);
            RemoveLocked(path, seq);
        }
    }

    /// <summary>Go: <c>func (el *eventList) removeWatchRootAt(path string, seq uint64)</c>。</summary>
    internal void RemoveWatchRootAt(string path, ulong seq)
    {
        lock (_mu)
        {
            AdvanceSeqLocked(seq);
            RemoveLocked(path, seq);
            GetOrCreate(path).IncludedWatchRoot = true;
        }
    }

    private void RemoveLocked(string path, ulong seq)
    {
        GetOrCreate(path).DeletedSeq = seq;
    }

    /// <summary>Go: <c>func (el *eventList) size() int</c>。跟踪的条目数（含会被 getEvents 抵消掉的）。</summary>
    internal int Size()
    {
        lock (_mu)
        {
            return _entries?.Count ?? 0;
        }
    }

    /// <summary>Go: <c>func (el *eventList) snapshotLocked() []Event</c>（调用方持锁）。</summary>
    private Event[] SnapshotLocked() => SnapshotSinceLocked(0);

    private Event[] SnapshotSinceLocked(ulong startSeq)
    {
        var outEvents = new List<Event>(_entries?.Count ?? 0);
        if (_entries is not null)
        {
            foreach (var (path, e) in _entries)
            {
                if (!e.TryKindSince(startSeq, out var kind))
                {
                    continue;
                }

                outEvents.Add(new Event(kind, path, e.IncludedWatchRoot));
            }
        }

        return outEvents.ToArray();
    }

    /// <summary>
    /// Go: <c>func (el *eventList) getEvents() []Event</c>。返回快照，跳过"既 created 又 deleted"的条目；
    /// 顺序不保证。
    /// </summary>
    internal Event[] GetEvents()
    {
        lock (_mu)
        {
            return SnapshotLocked();
        }
    }

    /// <summary>
    /// Go: <c>func (el *eventList) drain() ([]Event, error)</c>。原子地取走全部待发事件与已存的错误再清空，
    /// 避免"分开 getEvents + clear"之间新增的事件被静默丢弃。
    /// </summary>
    internal (Event[] Events, Exception? Err) Drain()
    {
        lock (_mu)
        {
            var outEvents = SnapshotLocked();
            var err = _err;
            _entries = null;
            _err = null;
            return (outEvents, err);
        }
    }

    /// <summary>Go: <c>func (el *eventList) drainForSequences(startSeqs []uint64) ([][]Event, error)</c>。</summary>
    internal (Event[][] EventsByCallback, Exception? Err) DrainForSequences(ulong[] startSeqs)
    {
        lock (_mu)
        {
            var outEvents = new Event[startSeqs.Length][];
            for (var i = 0; i < startSeqs.Length; i++)
            {
                outEvents[i] = SnapshotSinceLocked(startSeqs[i]);
            }

            var err = _err;
            _entries = null;
            _err = null;
            return (outEvents, err);
        }
    }

    /// <summary>Go: <c>func (el *eventList) setError(err error)</c>。只保留第一个错误。</summary>
    internal void SetError(Exception err)
    {
        lock (_mu)
        {
            _err ??= err;
        }
    }

    /// <summary>Go: <c>func (el *eventList) hasError() bool</c>。</summary>
    internal bool HasError()
    {
        lock (_mu)
        {
            return _err is not null;
        }
    }

    /// <summary>Go: <c>func (el *eventList) getError() error</c>。</summary>
    internal Exception? GetError()
    {
        lock (_mu)
        {
            return _err;
        }
    }

    /// <summary>
    /// Go: <c>func (el *eventList) clear()</c>（Go 侧定义在 eventlist_test.go 里，只被测试使用；线上路径一律走
    /// <see cref="Drain"/>，那样快照与重置是一个原子步骤）。
    /// </summary>
    internal void Clear()
    {
        lock (_mu)
        {
            _entries = null;
            _err = null;
        }
    }

    private EventEntry GetOrCreate(string path)
    {
        _entries ??= new Dictionary<string, EventEntry>();
        if (_entries.TryGetValue(path, out var existing))
        {
            return existing;
        }

        var e = new EventEntry();
        _entries[path] = e;
        return e;
    }

    /// <summary>Go: <c>func (el *eventList) sequence() uint64</c>。</summary>
    internal ulong Sequence()
    {
        lock (_mu)
        {
            return _seq;
        }
    }

    private ulong NextSeqLocked()
    {
        _seq++;
        return _seq;
    }

    private void AdvanceSeqLocked(ulong seq)
    {
        if (seq > _seq)
        {
            _seq = seq;
        }
    }
}

public static partial class FsWatch
{
    /// <summary>Go: <c>func (k EventKind) String() string</c>。</summary>
    public static string String(this EventKind k) => k switch
    {
        EventKind.Update => "update",
        EventKind.Delete => "delete",
        _ => "unknown",
    };
}
