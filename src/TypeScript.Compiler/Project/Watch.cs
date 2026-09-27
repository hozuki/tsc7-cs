// ported from: internal/project/watch.go:1-496（整文件）
//
// 与 Go 的偏离（逐条；除标注外都语义等价，只为对齐 C# 的语言特性）：
//
// 1) 包级函数/变量（`project` 包的包名与类型 `Project` 同名，不能建 `public static partial class Project`，
//    见仓规第 7 条）→ 本文件里未导出的包级函数统一挂在 `internal static class Watch` 上（与已落地的
//    Project.cs 调用点 `Watch.CreateResolutionLookupGlobMapper(...)` 一致）：
//    `newWatchRegistry` → `WatchRegistry.NewWatchRegistry`（Go 的包级函数挂到它构造的类型上，Session 的调用点
//    写 `WatchRegistry.NewWatchRegistry()`）、`createResolutionLookupGlobMapper` → `Watch.CreateResolutionLookupGlobMapper`、
//    `getTypingsLocationsGlobs` → `Watch.GetTypingsLocationsGlobs`、`getPathComponentsForWatching` →
//    `Watch.GetPathComponentsForWatching`、`perceivedOsRootLengthForWatching` → `Watch.PerceivedOsRootLengthForWatching`、
//    `getRecursiveGlobPattern` → `Watch.GetRecursiveGlobPattern`、`recursiveDirectoryGlobPattern` →
//    `Watch.RecursiveDirectoryGlobPattern`、`newRecursiveDirectoryWatcher` → `Watch.NewRecursiveDirectoryWatcher`、
//    `toFileSystemWatcherKey` → `Watch.ToFileSystemWatcherKey`、`fileSystemWatcherGlobString` →
//    `Watch.FileSystemWatcherGlobString`、包级 `var watcherID atomic.Uint64` → `Watch` 里的静态 long + `Interlocked`。
//    Go 里的**导出**成员仍保留原名：`WatcherID`、`PatternsAndIgnored`、`Watchers`、`WatchedFiles[T]`、
//    `NewWatchedFiles`、`NewWatchedFilesForPaths` 挂在 `WatchedFiles[T]` 上（静态工厂，与 Project.cs 的调用点一致：
//    `WatchedFiles<Collections.SyncSet<Path>>.NewWatchedFiles(...)` / `WatchedFiles<string[]>.NewWatchedFilesForPaths(...)`）。
//    代价：挂在泛型类型上的静态工厂**没有类型推断**，调用点必须显式写 `WatchedFiles<string[]>.…`（Go 能从
//    `func(T) PatternsAndIgnored` 实参推出 T）。这是 C# 的泛型约束，不是语义变化。
//
// 2) 未导出类型：`watchRegistry` → `internal sealed class WatchRegistry`、`fileSystemWatcherKey` →
//    `internal readonly record struct FileSystemWatcherKey(string Pattern, WatchKind Kind)`（Go 的
//    `pattern`/`kind` 两个未导出字段用 record struct 的位置属性承载：C# 需要值相等才能当 Dictionary 键，
//    类型本身 internal ⇒ 命名不会泄漏到公开 API）、`fileSystemWatcherValue` → `internal sealed class
//    FileSystemWatcherValue`（字段 `_count`/`_id`，按未导出字段约定带 `_`，跨文件可见用 internal）。
//
// 3) `sync.Mutex` → `System.Threading.Lock`（本仓统一约定）。
//    `sync.RWMutex` → 同样用 `Lock`：Go 的读锁只是"读 3 个字段/构造返回值"，本文件里读者之间不共享可观察
//    的中间状态（`Watchers()` 在锁内构造返回值），唯一差别是并发读吞吐，不改变任何可观察结果。
//
// 4) `sync.Once`（`computeWatchersOnce`）→ "标志 + Lock 双重检查"，并保持 Go 的"once 的锁**覆盖整个计算**"
//    语义（第二个调用者阻塞到首次计算结束）：`_onceMu` + `_computedWatchers`，计算体内部再取 `_mu`（与 Go
//    先行持 once 内部锁、再持 `w.mu` 完全一致，两把锁不同故不会自锁）；标志在计算**之前**置位，与 Go 的
//    `defer o.done.Store(1)` 相同（计算 panic 也算"已执行"）。
//
// 5) `atomic.Uint64 watcherID` → `Watch` 的静态 `long _watcherID` + `Interlocked.Increment`（返回值语义与
//    `Add(1)` 相同：首个 ID 是 1；`WatcherID` 字符串里的 `%d` → `ToString(CultureInfo.InvariantCulture)`）。
//    静态字段必须放在**非泛型**的 `Watch` 上：C# 泛型类型的静态字段是按类型实参各一份，而 Go 的是包级唯一个。
//
// 6) `(T, bool)` 返回值：`Release` → `(WatcherID Id, bool Removed)`；`Acquire` 仍是 `bool`。
//
// 7) nil 切片/映射：`PatternsAndIgnored` 的三个未导出字段保持 `string[]?` / `HashSet<string>?`（对应 Go 的 nil；
//    跨文件构造的切片用 internal）。消费处按约定 `?? []`（`NewWatchedFilesForPaths` 的 `files` 与
//    `GetTypingsLocationsGlobs` 的 `typingsFiles` 都是"未 Clone/未传时即 nil"，Go 的 `range nil 切片` 是零次
//    迭代，所以这里也 `?? []`）。**唯一可观察差异**：Go 里没 append 过的字段是 nil，
//    这里可能是空数组——两处消费（`slices.Sorted`/`core.Map`/`slices.EqualFunc` 以及 Session 里的遍历）对 nil 与
//    空完全等价（长度 0、无元素），故不可观察。`Watchers.IgnoredPaths` 按 `TSPath.GetCommonParents` 的先例统一成
//    **非 null 空集合**（消费点 `len(w.IgnoredPaths) > 0` 等价）；内部 `_ignored` 仍保持 null 表示"没有忽略项"。
//
// 8) `slices.Sorted` → `Array.Sort(..., CompareStringsCaseSensitive)`（本文件内的 `SortedAndCompacted`）；
//    `slices.Compact` → `Core.DeduplicateSorted`；`slices.EqualFunc` → 手写比较函数。
//    Go 的字符串 `<` 是 UTF-8 字节序，`CompareStringsCaseSensitive` 是 UTF-16 序数序：只在非 BMP 字符
//    （代理对 vs U+E000..U+FFFF）上可能与 Go 给出不同顺序，输入都是路径/glob，实际不可达。
//
// 9) 字符串工具：`strings.Index(s, "/node_modules/")` → `IndexOf(..., StringComparison.Ordinal)`；
//    `strings.HasPrefix(s, "//")` → `StartsWith("//", StringComparison.Ordinal)`；`strings.EqualFold(x, "users")`
//    → `EquateStringCaseInsensitive`（仓规：不用 `StringComparison.OrdinalIgnoreCase`，它不等价于 EqualFold）。
//
// 10) `panic("workspace folder-based relative patterns not implemented")` → `Debug.Fail(...)`（`TypeScript.Debug`）。
//
// 11) nil 接收者：Go 的 `Clone`/`ID` 有 `if w == nil { … }` 分支；C# 实例方法不能在 null 上分派 ⇒ 调用点写
//     `w?.Clone(x)` / `w?.ID() ?? WatcherID.Empty`（先例 Collections/Set.cs 文件头第 3 条，Project.cs 的
//     `_programFilesWatch!.Clone(...)` 就是这样落地的）。
//
// 12) `type WatcherID string` → `public readonly struct WatcherID`（照 LsProto/URI、DocumentUri 的形状：
//     `Value`、string→WatcherID 隐式、WatcherID→string 显式、值相等）。Go 里 `return "", false` 的 `""` →
//     `WatcherID.Empty`。
//
// 13) `Watchers` 由 Go 的值结构体 → `sealed class`（字段保持 Go 导出名的 PascalCase 公开字段）；
//     Go 的 `append(w.WorkspaceWatchers, w.OutsideWorkspaceWatchers...)` 在 C# 调用点写 `[.. a, .. b]`。
//
// 14) `data *collections.SyncSet[tspath.Path]`（可能为 nil）→ `SyncSet<Path>?`；`collections.Set[tspath.Path]`
//     → `Collections.Set<Path>`（未导出的 `seenDirs`/`nodeModulesDirectories`/`externalDirectories` 都是它）。
//
// 15) `maps.Values` + `slices.Collect` → `Dictionary.Values` 的数组（Go 的 map 迭代顺序随机，C# 是插入序；
//     两处结果随后都排序或在 `Watchers()` 内排序，故不可观察）。
//
// 16) `tspath.ComparePathsOptions{ UseCaseSensitiveFileNames: … }` → `new ComparePathsOptions(bool, string?)`
//     （Go 的 `CurrentDirectory` 零值 "" 与 C# 的 null→"" 一致，见 TSPath/Path.cs 文件头）。
//
// 17) **不在本文件**：`watchRequestTimeout`、`Session.updateWatch`（WatchFiles/UnwatchFiles 的 client 交互、
//     超时、回滚、pending 重试）在 session.go 里，属 Session 切片；本文件只提供被它调用的
//     `WatchRegistry`（Acquire/Release/MarkPending/ClearPending/IsPending）、`WatchedFiles<T>.Watchers/ID`、
//     `Watch.FileSystemWatcherGlobString`。
//
// 18) `T any` 无约束 → C# 同样无约束；`_input` 用 `default!`（C# 无法证明 T 非空，与 TSPath/Path.cs 文件头
//     记录的做法一致）。
//
// 19) `PatternsAndIgnored` 保持**值结构体**（Go 的 struct → `public struct` + internal 字段）：
//     `default(PatternsAndIgnored)` 就是 Go 的零值（三个字段为 nil），而换成 class 会让
//     `WatchedFiles<PatternsAndIgnored>` 配 `core.Identity` 时传入/返回 null（Go 的零值是结构体值，不是 null）
//     并在下一行解引用处 NRE。`WatchedFiles.Clone` 里 `id` 按 Go 的结构体字面量语义复位为 0（见该方法注释）。
using System.Globalization;
using TypeScript.Collections;
using TypeScript.LsProto;
using Path = TypeScript.TSPath.Path;
using TypeScript.TSPath;
using static TypeScript.Core.Core;
using static TypeScript.LsConv.LsConv;
using static TypeScript.StringUtil.StringUtil;
using static TypeScript.TSPath.TSPath;

namespace TypeScript.Project;

/// <summary>Go 的 <c>type WatcherID string</c>（watch.go:155）。</summary>
public readonly struct WatcherID : IEquatable<WatcherID>
{
    private readonly string? _value;

    public WatcherID(string value) => _value = value;

    /// <summary>Go 的 <c>string(id)</c>。</summary>
    public string Value => _value ?? "";

    /// <summary>Go 里的 <c>""</c>（没有对应 watcher 时 <c>ID()</c> 的返回值，见 <c>Release</c>）。</summary>
    public static readonly WatcherID Empty = new("");

    // Go 的 WatcherID(x) / string(x) 都是显式转换；入向保持隐式（调用点直接写 WatcherID 字面量更方便，
    // 与 LsProto.URI / DocumentUri 的形状一致）。
    public static implicit operator WatcherID(string value) => new(value);

    public static explicit operator string(WatcherID id) => id.Value;

    public override string ToString() => Value;

    public bool Equals(WatcherID other) => string.Equals(Value, other.Value, StringComparison.Ordinal);

    public override bool Equals(object? obj) => obj is WatcherID other && Equals(other);

    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Value);

    public static bool operator ==(WatcherID left, WatcherID right) => left.Equals(right);

    public static bool operator !=(WatcherID left, WatcherID right) => !left.Equals(right);
}

/// <summary>Go 的 <c>type fileSystemWatcherKey struct</c>（watch.go:22-25）。</summary>
internal readonly record struct FileSystemWatcherKey(string Pattern, WatchKind Kind);

/// <summary>Go 的 <c>type fileSystemWatcherValue struct</c>（watch.go:27-30）。</summary>
internal sealed class FileSystemWatcherValue
{
    internal int _count;
    internal WatcherID _id;
}

/// <summary>
/// Go 的 <c>type watchRegistry struct</c>（watch.go:32-41）：记录当前 watch glob 以及每个 glob 被多少个
/// <c>WatchedFiles</c> 引用，用引用计数决定何时向 client 注册/注销。
/// </summary>
internal sealed class WatchRegistry
{
    // Go: `mu sync.Mutex` / `entries map[fileSystemWatcherKey]*fileSystemWatcherValue` / `pending map[WatcherID]struct{}`。
    private readonly Lock _mu = new();
    private readonly Dictionary<FileSystemWatcherKey, FileSystemWatcherValue> _entries = [];
    private readonly HashSet<WatcherID> _pending = [];

    /// <summary>Go: <c>func newWatchRegistry() *watchRegistry</c>（watch.go:43-48）→ 静态工厂（Go 的包级函数
    /// 挂到它构造的类型上，Session 的调用点写 <c>WatchRegistry.NewWatchRegistry()</c>）。</summary>
    public static WatchRegistry NewWatchRegistry() => new();

    /// <summary>
    /// Go: <c>func (r *watchRegistry) Acquire(watcher *lsproto.FileSystemWatcher, id WatcherID) (isNew bool)</c>
    /// （watch.go:50-67）。引用计数 0→1 时返回 true，调用方据此向 client 注册。
    /// </summary>
    public bool Acquire(LsProto.FileSystemWatcher watcher, WatcherID id)
    {
        lock (_mu)
        {
            var key = Watch.ToFileSystemWatcherKey(watcher);
            if (!_entries.TryGetValue(key, out var value))
            {
                value = new FileSystemWatcherValue { _id = id };
                _entries[key] = value;
            }

            value._count++;
            return value._count == 1;
        }
    }

    /// <summary>
    /// Go: <c>func (r *watchRegistry) Release(watcher *lsproto.FileSystemWatcher) (id WatcherID, removed bool)</c>
    /// （watch.go:69-84）。没有引用后删表并返回当初的 id（调用方据此向 client 注销）。
    /// </summary>
    public (WatcherID Id, bool Removed) Release(LsProto.FileSystemWatcher watcher)
    {
        lock (_mu)
        {
            var key = Watch.ToFileSystemWatcherKey(watcher);
            if (!_entries.TryGetValue(key, out var value))
            {
                return (WatcherID.Empty, false);
            }

            if (value._count <= 1)
            {
                _entries.Remove(key);
                return (value._id, true);
            }

            value._count--;
            return (WatcherID.Empty, false);
        }
    }

    /// <summary>Go: <c>func (r *watchRegistry) MarkPending(id WatcherID)</c>（watch.go:86-91）。</summary>
    public void MarkPending(WatcherID id)
    {
        lock (_mu)
        {
            _pending.Add(id);
        }
    }

    /// <summary>Go: <c>func (r *watchRegistry) ClearPending(id WatcherID)</c>（watch.go:93-98）。</summary>
    public void ClearPending(WatcherID id)
    {
        lock (_mu)
        {
            _pending.Remove(id);
        }
    }

    /// <summary>Go: <c>func (r *watchRegistry) IsPending(id WatcherID) bool</c>（watch.go:100-105）。</summary>
    public bool IsPending(WatcherID id)
    {
        lock (_mu)
        {
            return _pending.Contains(id);
        }
    }
}

/// <summary>Go 的 <c>type PatternsAndIgnored struct</c>（watch.go:107-111）。</summary>
public struct PatternsAndIgnored
{
    // Go 的未导出字段（跨文件构造，故 internal）：directoriesOutsideWorkspace / patternsInsideWorkspace / ignored。
    internal string[]? _directoriesOutsideWorkspace;
    internal string[]? _patternsInsideWorkspace;
    internal HashSet<string>? _ignored;
}

/// <summary>Go 的 <c>type Watchers struct</c>（watch.go:211-216）。</summary>
public sealed class Watchers
{
    public WatcherID WatcherID;

    public LsProto.FileSystemWatcher[] WorkspaceWatchers = [];

    public LsProto.FileSystemWatcher[] OutsideWorkspaceWatchers = [];

    /// <summary>Go 的 <c>IgnoredPaths map[string]struct{}</c>（按 TSPath 先例：nil ↔ 空集合）。</summary>
    public HashSet<string> IgnoredPaths = [];
}

/// <summary>
/// Go 的 <c>type WatchedFiles[T any] struct</c>（watch.go:159-172）与它的方法（watch.go:174-296）。
/// </summary>
public sealed class WatchedFiles<T>
{
    private readonly string _name;
    private readonly WatchKind _watchKind;
    private readonly bool _hasRelativePatternCapability;
    private readonly Func<T, PatternsAndIgnored> _computeGlobPatterns;

    // Go: `mu sync.RWMutex`（见文件头 3）与 `computeWatchersOnce sync.Once`（见文件头 4）。
    private readonly Lock _mu = new();
    private readonly Lock _onceMu = new();
    private bool _computedWatchers;
    private T _input = default!;
    private LsProto.FileSystemWatcher[] _workspaceWatchers = [];
    private LsProto.FileSystemWatcher[] _outsideWorkspaceWatchers = [];
    private HashSet<string>? _ignored;
    private long _id;

    /// <summary>Go: <c>func NewWatchedFiles[T any](…) *WatchedFiles[T]</c>（watch.go:174-184）。</summary>
    public static WatchedFiles<T> NewWatchedFiles(
        string name,
        WatchKind watchKind,
        bool hasRelativePatternCapability,
        Func<T, PatternsAndIgnored> computeGlobPatterns)
        => new(name, watchKind, hasRelativePatternCapability, computeGlobPatterns);

    /// <summary>
    /// Go: <c>func NewWatchedFilesForPaths(…) *WatchedFiles[[]string]</c>（watch.go:186-209）。把工作区外的
    /// 精确文件路径折成"目录级"外部 watcher，客户端支持时用 URI 形式的 RelativePattern。
    /// C# 侧声明在泛型类型上（调用点写作 `WatchedFiles<string[]>.NewWatchedFilesForPaths(...)`，与 Project.cs 一致），
    /// 返回值与 T 无关。
    /// </summary>
    public static WatchedFiles<string[]> NewWatchedFilesForPaths(
        string name,
        WatchKind watchKind,
        bool hasRelativePatternCapability,
        string workspaceDirectory,
        string currentDirectory,
        bool useCaseSensitiveFileNames)
    {
        var comparePathsOptions = new ComparePathsOptions(useCaseSensitiveFileNames, currentDirectory);
        return WatchedFiles<string[]>.NewWatchedFiles(name, watchKind, hasRelativePatternCapability, (string[] files) =>
        {
            var result = new PatternsAndIgnored();
            // Go 的 `for _, file := range files` 对 nil 切片是零次迭代（`Clone` 之前 `input` 就是 nil）。
            foreach (var file in files ?? [])
            {
                if (ContainsPath(workspaceDirectory, file, comparePathsOptions))
                {
                    result._patternsInsideWorkspace = [.. result._patternsInsideWorkspace ?? [], file];
                }
                else
                {
                    result._directoriesOutsideWorkspace = [.. result._directoriesOutsideWorkspace ?? [], GetDirectoryPath(file)];
                }
            }

            return result;
        });
    }

    // Go 的结构体字面量 + 字段初始化（watch.go:175-183）；C# 只能经构造函数。
    internal WatchedFiles(
        string name,
        WatchKind watchKind,
        bool hasRelativePatternCapability,
        Func<T, PatternsAndIgnored> computeGlobPatterns)
    {
        _id = Watch.NextWatcherID();
        _name = name;
        _watchKind = watchKind;
        _hasRelativePatternCapability = hasRelativePatternCapability;
        _computeGlobPatterns = computeGlobPatterns;
    }

    /// <summary>Go: <c>func (w *WatchedFiles[T]) Watchers() Watchers</c>（watch.go:218-264）。</summary>
    public Watchers Watchers()
    {
        EnsureWatchersComputed();

        lock (_mu)
        {
            return new Watchers
            {
                WatcherID = new WatcherID(string.Concat(_name, " watcher ", _id.ToString(CultureInfo.InvariantCulture))),
                WorkspaceWatchers = _workspaceWatchers,
                OutsideWorkspaceWatchers = _outsideWorkspaceWatchers,
                IgnoredPaths = _ignored ?? [],
            };
        }
    }

    // Go: `w.computeWatchersOnce.Do(func() { … })`（watch.go:219-259）：once 的锁覆盖整个计算。
    // 注意 Go 的 once.doSlow 是在调用 f **之前**就 `defer o.done.Store(1)` 的，所以 f panic 也算"已执行"；
    // 这里同样先把标志置位再计算（Debug.Fail 抛出的路径上不会变成"每次重算"）。
    private void EnsureWatchersComputed()
    {
        lock (_onceMu)
        {
            if (_computedWatchers)
            {
                return;
            }

            _computedWatchers = true;
            ComputeWatchers();
        }
    }

    private void ComputeWatchers()
    {
        lock (_mu)
        {
            var result = _computeGlobPatterns(_input);
            var globs = Watch.SortedAndCompacted(result._patternsInsideWorkspace);

            var ignored = result._ignored;
            // ignored 只用于日志，不影响 watcher 的身份。
            _ignored = ignored;
            var changed = false;
            if (!WorkspaceWatchersEqual(_workspaceWatchers, globs))
            {
                _workspaceWatchers = Map(globs, glob => new LsProto.FileSystemWatcher
                {
                    GlobPattern = new LsProto.PatternOrRelativePattern { Pattern = glob },
                    Kind = _watchKind,
                });
                changed = true;
            }

            var dirsOutside = Watch.SortedAndCompacted(result._directoriesOutsideWorkspace);
            if (!OutsideWorkspaceWatchersEqual(_outsideWorkspaceWatchers, dirsOutside, _hasRelativePatternCapability))
            {
                _outsideWorkspaceWatchers = Map(dirsOutside, dir => Watch.NewRecursiveDirectoryWatcher(dir, _watchKind, _hasRelativePatternCapability));
                changed = true;
            }

            if (changed)
            {
                _id = Watch.NextWatcherID();
            }
        }
    }

    // Go: `slices.EqualFunc(w.workspaceWatchers, globs, func(a, b) bool { return *a.GlobPattern.Pattern == b })`。
    // `*a.GlobPattern.Pattern` 在 Go 里对 nil 是 panic；这里的 watcher 都是本文件用 Pattern 构造的，
    // 故用 `!` 表达同样的"不预期为 null"（不额外做容错，与 Go 一致）。
    private static bool WorkspaceWatchersEqual(LsProto.FileSystemWatcher[] watchers, string[] globs)
    {
        if (watchers.Length != globs.Length)
        {
            return false;
        }

        for (var i = 0; i < watchers.Length; i++)
        {
            if (watchers[i].GlobPattern.Pattern! != globs[i])
            {
                return false;
            }
        }

        return true;
    }

    // Go: `slices.EqualFunc(w.outsideWorkspaceWatchers, dirsOutside, func(a, b) bool {
    //     return fileSystemWatcherGlobString(a) == recursiveDirectoryGlobPattern(b, w.hasRelativePatternCapability) })`。
    private static bool OutsideWorkspaceWatchersEqual(LsProto.FileSystemWatcher[] watchers, string[] dirsOutside, bool hasRelativePatternCapability)
    {
        if (watchers.Length != dirsOutside.Length)
        {
            return false;
        }

        for (var i = 0; i < watchers.Length; i++)
        {
            if (Watch.FileSystemWatcherGlobString(watchers[i]) != Watch.RecursiveDirectoryGlobPattern(dirsOutside[i], hasRelativePatternCapability))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Go: <c>func (w *WatchedFiles[T]) ID() WatcherID</c>（watch.go:266-271）。</summary>
    public WatcherID ID() => Watchers().WatcherID;

    /// <summary>Go: <c>func (w *WatchedFiles[T]) Name() string</c>（watch.go:273-275）。</summary>
    public string Name() => _name;

    /// <summary>Go: <c>func (w *WatchedFiles[T]) WatchKind() lsproto.WatchKind</c>（watch.go:277-279）。</summary>
    public WatchKind WatchKind() => _watchKind;

    /// <summary>Go: <c>func (w *WatchedFiles[T]) Clone(input T) *WatchedFiles[T]</c>（watch.go:281-296）。</summary>
    public WatchedFiles<T> Clone(T input)
    {
        lock (_mu)
        {
            return new WatchedFiles<T>(_name, _watchKind, _hasRelativePatternCapability, _computeGlobPatterns)
            {
                // Go 的 Clone 是结构体字面量：`id` **不复制**（保持零值 0）、`computeWatchersOnce` 也是未执行的零值。
                // C# 的构造函数会分配一个新 id，所以这里显式复位成 0：克隆后若 glob 未变，`ComputeWatchers`
                // 的 `changed` 为 false，ID 保持 "… watcher 0"——session.go 的 `old.ID() != new.ID()`
                // 正是靠这个（"glob 未变 ⇒ 身份相同，不必重新注册"）。
                _id = 0,
                _workspaceWatchers = _workspaceWatchers,
                _outsideWorkspaceWatchers = _outsideWorkspaceWatchers,
                _input = input,
            };
        }
    }
}

/// <summary>
/// watch.go 里未导出的包级函数/变量的落点（见文件头 1）。Go 不能再有一个同名的静态类，
/// 但 C# 需要一个非泛型宿主（泛型类型的静态字段是"每个类型实参一份"，而 Go 的包级变量唯一）。
/// </summary>
internal static class Watch
{
    /// <summary>Go: <c>minWatchLocationDepth</c>（watch.go:18-20）。</summary>
    internal const int MinWatchLocationDepth = 2;

    // Go: `var watcherID atomic.Uint64`（watch.go:157）。首个 ID 是 1（与 Go 的 Add(1) 相同）。
    private static long _watcherID;

    internal static long NextWatcherID() => Interlocked.Increment(ref _watcherID);

    /// <summary>
    /// Go: <c>func toFileSystemWatcherKey(w *lsproto.FileSystemWatcher) fileSystemWatcherKey</c>（watch.go:113-137）。
    /// 注意 Go 的注释：key 是 base+pattern 的简单拼接，理论上不同结构（Pattern vs RelativePattern、
    /// URI vs WorkspaceFolder）可能撞车；实际上工作区内用 Pattern+文件系统路径、工作区外用 RelativePattern+file:// URI，
    /// 不会撞。
    /// </summary>
    internal static FileSystemWatcherKey ToFileSystemWatcherKey(LsProto.FileSystemWatcher w)
    {
        var kind = w.Kind ?? (WatchKind.Create | WatchKind.Change | WatchKind.Delete);
        var pattern = "";
        if (w.GlobPattern.Pattern is not null)
        {
            pattern = w.GlobPattern.Pattern;
        }
        else if (w.GlobPattern.RelativePattern is not null)
        {
            var @base = "";
            if (w.GlobPattern.RelativePattern.BaseUri.URI is not null)
            {
                @base = w.GlobPattern.RelativePattern.BaseUri.URI.Value.Value;
            }
            else if (w.GlobPattern.RelativePattern.BaseUri.WorkspaceFolder is not null)
            {
                Debug.Fail("workspace folder-based relative patterns not implemented");
            }

            pattern = string.Concat(@base, "/", w.GlobPattern.RelativePattern.Pattern);
        }

        return new FileSystemWatcherKey(pattern, kind);
    }

    /// <summary>Go: <c>func fileSystemWatcherGlobString(w *lsproto.FileSystemWatcher) string</c>（watch.go:139-153）。</summary>
    internal static string FileSystemWatcherGlobString(LsProto.FileSystemWatcher w)
    {
        if (w.GlobPattern.Pattern is not null)
        {
            return w.GlobPattern.Pattern;
        }

        if (w.GlobPattern.RelativePattern is not null)
        {
            var @base = "";
            if (w.GlobPattern.RelativePattern.BaseUri.URI is not null)
            {
                @base = w.GlobPattern.RelativePattern.BaseUri.URI.Value.Value;
            }
            else if (w.GlobPattern.RelativePattern.BaseUri.WorkspaceFolder is not null)
            {
                Debug.Fail("workspace folder-based relative patterns not implemented");
            }

            return string.Concat(@base, "/", w.GlobPattern.RelativePattern.Pattern);
        }

        return "";
    }

    /// <summary>
    /// Go: <c>func createResolutionLookupGlobMapper(workspaceDirectory, libDirectory, currentDirectory string,
    /// useCaseSensitiveFileNames bool) func(data *collections.SyncSet[tspath.Path]) PatternsAndIgnored</c>
    /// （watch.go:298-378）。
    /// </summary>
    internal static Func<SyncSet<Path>?, PatternsAndIgnored> CreateResolutionLookupGlobMapper(
        string workspaceDirectory,
        string libDirectory,
        string currentDirectory,
        bool useCaseSensitiveFileNames)
    {
        var workspaceDirectoryPath = ToPath(workspaceDirectory, currentDirectory, useCaseSensitiveFileNames);
        var currentDirectoryPath = ToPath(currentDirectory, currentDirectory, useCaseSensitiveFileNames);
        var libDirectoryPath = ToPath(libDirectory, currentDirectory, useCaseSensitiveFileNames);

        return data =>
        {
            HashSet<string>? ignored = null;
            var seenDirs = new Set<Path>();
            var includeWorkspace = false;
            var includeRoot = false;
            var includeLib = false;
            var nodeModulesDirectories = new Set<Path>();
            var externalDirectories = new Set<Path>();

            if (data is not null)
            {
                data.Range(path =>
                {
                    if (IsDynamicFileName(path.Value))
                    {
                        return true;
                    }

                    // 假定输入都是文件路径：每个目录只取一个就够（它们的输出一定相同）。
                    if (!seenDirs.AddIfAbsent(path.GetDirectoryPath()))
                    {
                        return true;
                    }

                    if (workspaceDirectoryPath.ContainsPath(path))
                    {
                        includeWorkspace = true;
                    }
                    else if (currentDirectoryPath.ContainsPath(path))
                    {
                        includeRoot = true;
                    }
                    else if (libDirectoryPath.ContainsPath(path))
                    {
                        includeLib = true;
                    }
                    else
                    {
                        // Go: `else if idx := strings.Index(string(path), "/node_modules/"); idx != -1 {
                        //     nodeModulesDirectories.Add(path[:idx+len("/node_modules")]) } else { … }`
                        var idx = path.Value.IndexOf("/node_modules/", StringComparison.Ordinal);
                        if (idx != -1)
                        {
                            nodeModulesDirectories.Add(new Path(path.Value[..(idx + "/node_modules".Length)]));
                        }
                        else
                        {
                            externalDirectories.Add(path.GetDirectoryPath());
                        }
                    }

                    return true;
                });
            }

            var globs = new List<string>();
            if (includeWorkspace)
            {
                globs.Add(GetRecursiveGlobPattern(workspaceDirectoryPath.Value));
            }

            if (includeRoot)
            {
                globs.Add(GetRecursiveGlobPattern(currentDirectoryPath.Value));
            }

            if (includeLib)
            {
                globs.Add(GetRecursiveGlobPattern(libDirectoryPath.Value));
            }

            if (nodeModulesDirectories.Len() > 0)
            {
                var nodeModulesGlobs = new List<string>(nodeModulesDirectories.Len());
                foreach (var dir in nodeModulesDirectories.Keys())
                {
                    nodeModulesGlobs.Add(GetRecursiveGlobPattern(dir.Value));
                }

                nodeModulesGlobs.Sort(CompareStringsCaseSensitive);
                globs.AddRange(nodeModulesGlobs);
            }

            string[] outsideDirs = [];
            if (externalDirectories.Len() > 0)
            {
                var externalDirStrings = new List<string>(externalDirectories.Len());
                foreach (var dir in externalDirectories.Keys())
                {
                    externalDirStrings.Add(dir.Value);
                }

                var (externalDirectoryParents, ignoredExternalDirs) = GetCommonParents(
                    [.. externalDirStrings],
                    MinWatchLocationDepth,
                    GetPathComponentsForWatching,
                    new ComparePathsOptions(useCaseSensitiveFileNames: true)); // 已经是 tspath.Path 口径
                Array.Sort(externalDirectoryParents, CompareStringsCaseSensitive);
                ignored = ignoredExternalDirs;
                outsideDirs = externalDirectoryParents;
            }

            return new PatternsAndIgnored
            {
                _directoriesOutsideWorkspace = outsideDirs,
                _patternsInsideWorkspace = [.. globs],
                _ignored = ignored,
            };
        };
    }

    /// <summary>
    /// Go: <c>func getTypingsLocationsGlobs(typingsFiles []string, typingsLocation, workspaceDirectory,
    /// currentDirectory string, useCaseSensitiveFileNames bool) PatternsAndIgnored</c>（watch.go:380-422）。
    /// 参数写成 `string[]?`：Go 传 nil 切片时 `range` 是零次迭代（本仓约定，见文件头 3/7）。
    /// </summary>
    internal static PatternsAndIgnored GetTypingsLocationsGlobs(
        string[]? typingsFiles,
        string typingsLocation,
        string workspaceDirectory,
        string currentDirectory,
        bool useCaseSensitiveFileNames)
    {
        var includeTypingsLocation = false;
        var includeWorkspace = false;
        var externalDirectories = new Dictionary<Path, string>();
        var globs = new Dictionary<Path, string>();
        var comparePathsOptions = new ComparePathsOptions(useCaseSensitiveFileNames, currentDirectory);
        foreach (var file in typingsFiles ?? [])
        {
            if (ContainsPath(typingsLocation, file, comparePathsOptions))
            {
                includeTypingsLocation = true;
            }
            else if (!ContainsPath(workspaceDirectory, file, comparePathsOptions))
            {
                var directory = GetDirectoryPath(file);
                externalDirectories[ToPath(directory, currentDirectory, useCaseSensitiveFileNames)] = directory;
            }
            else
            {
                includeWorkspace = true;
            }
        }

        var (externalDirectoryParents, ignored) = GetCommonParents(
            [.. externalDirectories.Values],
            MinWatchLocationDepth,
            GetPathComponentsForWatching,
            comparePathsOptions);
        Array.Sort(externalDirectoryParents, CompareStringsCaseSensitive);
        if (includeWorkspace)
        {
            globs[ToPath(workspaceDirectory, currentDirectory, useCaseSensitiveFileNames)] = GetRecursiveGlobPattern(workspaceDirectory);
        }

        if (includeTypingsLocation)
        {
            globs[ToPath(typingsLocation, currentDirectory, useCaseSensitiveFileNames)] = GetRecursiveGlobPattern(typingsLocation);
        }

        return new PatternsAndIgnored
        {
            _directoriesOutsideWorkspace = externalDirectoryParents,
            _patternsInsideWorkspace = [.. globs.Values],
            _ignored = ignored,
        };
    }

    /// <summary>Go: <c>func getPathComponentsForWatching(path, currentDirectory string) []string</c>（watch.go:424-432）。</summary>
    internal static string[] GetPathComponentsForWatching(string path, string currentDirectory)
    {
        var components = GetPathComponents(path, currentDirectory);
        var rootLength = PerceivedOsRootLengthForWatching(components);
        if (rootLength <= 1)
        {
            return components;
        }

        var newRoot = CombinePaths(components[0], components[1..rootLength]);
        return [newRoot, .. components[rootLength..]];
    }

    /// <summary>Go: <c>func perceivedOsRootLengthForWatching(pathComponents []string) int</c>（watch.go:434-456）。</summary>
    internal static int PerceivedOsRootLengthForWatching(string[] pathComponents)
    {
        var length = pathComponents.Length;
        if (length <= 1)
        {
            return length;
        }

        if (pathComponents[0].StartsWith("//", StringComparison.Ordinal))
        {
            // Group UNC roots (//server/share) into a single component
            return 2;
        }

        if (pathComponents[0].Length == 3 && IsVolumeCharacter(pathComponents[0][0]) && pathComponents[0][1] == ':' && pathComponents[0][2] == '/')
        {
            // Windows-style volume
            if (EquateStringCaseInsensitive(pathComponents[1], "users"))
            {
                // Group C:/Users/username into a single component
                return Math.Min(3, length);
            }

            return 1;
        }

        if (pathComponents[1] == "home")
        {
            // Group /home/username into a single component
            return Math.Min(3, length);
        }

        return 1;
    }

    /// <summary>Go: <c>func getRecursiveGlobPattern(directory string) string</c>（watch.go:458-460）。</summary>
    internal static string GetRecursiveGlobPattern(string directory)
        => string.Concat(RemoveTrailingDirectorySeparator(directory), "/", "**/*");

    /// <summary>
    /// Go: <c>func recursiveDirectoryGlobPattern(directory string, useRelativePattern bool) string</c>
    /// （watch.go:462-471）：<c>newRecursiveDirectoryWatcher</c> 会产出的那个 watcher 的字符串形式。
    /// </summary>
    internal static string RecursiveDirectoryGlobPattern(string directory, bool useRelativePattern)
        => useRelativePattern ? string.Concat(FileNameToDocumentURI(directory).Value, "/**/*") : GetRecursiveGlobPattern(directory);

    /// <summary>
    /// Go: <c>func newRecursiveDirectoryWatcher(directory string, kind lsproto.WatchKind, useRelativePattern bool)
    /// *lsproto.FileSystemWatcher</c>（watch.go:473-496）。
    /// </summary>
    internal static LsProto.FileSystemWatcher NewRecursiveDirectoryWatcher(string directory, WatchKind kind, bool useRelativePattern)
    {
        if (useRelativePattern)
        {
            var baseUri = new LsProto.URI(FileNameToDocumentURI(directory).Value);
            return new LsProto.FileSystemWatcher
            {
                GlobPattern = new LsProto.PatternOrRelativePattern
                {
                    RelativePattern = new LsProto.RelativePattern
                    {
                        BaseUri = new LsProto.WorkspaceFolderOrURI { URI = baseUri },
                        Pattern = "**/*",
                    },
                },
                Kind = kind,
            };
        }

        var glob = GetRecursiveGlobPattern(directory);
        return new LsProto.FileSystemWatcher
        {
            GlobPattern = new LsProto.PatternOrRelativePattern { Pattern = glob },
            Kind = kind,
        };
    }

    /// <summary>
    /// Go: <c>slices.Compact(slices.Sorted(slices.Values(x)))</c>（watch.go:222、249）。
    /// 与 Go 一样**不改动入参**（slices.Sorted 总是新切片），排序用本仓的
    /// `CompareStringsCaseSensitive`（见文件头 8）。
    /// </summary>
    internal static string[] SortedAndCompacted(string[]? values)
    {
        var sorted = values ?? [];
        if (sorted.Length > 1)
        {
            sorted = (string[])sorted.Clone();
            Array.Sort(sorted, CompareStringsCaseSensitive);
        }

        return DeduplicateSorted(sorted, EquateStringCaseSensitive);
    }
}
