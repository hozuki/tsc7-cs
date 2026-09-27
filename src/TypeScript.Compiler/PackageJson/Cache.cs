// ported from: internal/packagejson/cache.go
//
// 与 Go 的偏离（逐条；其余与 Go 逐行对应）：
//
// 1) **包级函数 → 类型上的 public static 成员**（用户钉死；理由见 PackageJson.cs 文件头第 4 条）：本文件放
//    `NewInfoCache`（Go 的 `packagejson.NewInfoCache` → `PackageJson.NewInfoCache`），与实例方法
//    `GetVersionPaths` 同在一个 `partial class PackageJson` 上（Go 的 cache.go 也是这么分文件的）。
//
// 2) **nil 接收者安全方法**（docs/architecture.md §3.2）：`VersionPaths.Exists()`、`InfoCacheEntry.Exists()/
//    GetContents()/GetDirectory()` 里的 `p != nil` 分支在 C# 里不可达（实例方法不能在 null 上分派），故删除；
//    调用点改写成 `entry?.Exists() ?? false` / `entry?.GetContents()` / `entry?.GetDirectory() ?? ""`。
//    `GetVersionPaths` 因此返回**非 null** 的 `VersionPaths`（Go 返回的是值、恒非 nil；零值对象
//    `Exists()` 为 false），`_versionPaths` 字段初始化为 `new()`。
//
// 3) `sync.Once` → `lock` + bool（AOT 下不用反射，也不借 `Lazy<T>` 的异常缓存语义）。Go 的 `once.Do` 把结果写进
//    `versionPaths`/`versionTraces`，而 **trace 回调在每次调用时都会重放**（在 Once 之外）—— 这里保持一致。
//
// 4) `VersionPaths` / `InfoCacheEntry` 在 Go 里是"值 + 指针接收者方法"，C# 只能是 class：`GetVersionPaths` 返回
//    同一个对象（Go 返回副本，副本上的 `paths` 记忆化会丢，C# 会留下来 —— 只是少算一次，结果相同）。
//    `WithPackageDirectory` 的"相等就返回自身"语义照搬（Go 靠它省分配并保持指针相等）。
//
// 5) `diagnosticAndArgs`（未导出 struct）→ `internal readonly struct DiagnosticAndArgs`（未导出类型 → C# internal +
//    PascalCase；字段按 .editorconfig 的 `_camelCase`）；`message *diagnostics.Message` → `Message`（class，
//    对应 Go 的指针），`args []any` → `object?[]`。
//
// 6) `pathsJSON *collections.OrderedMap[string, JSONValue]` → `OrderedMap<string, JSONValue>?`，
//    `paths *collections.OrderedMap[string, []string]` → `OrderedMap<string, string[]>?`：都是"同程序集其它类要访问的
//    未导出字段" → `internal` + `_camelCase`（docs §2）。
//
// 7) `tspath.ToPath(...)` → `ToPath(...)`（`using static TypeScript.TSPath.TSPath`），返回 `Path`（readonly record struct）。
//    `semver.Version` 用别名 `SemVerVersion` 引入：本文件同时 `using static ...SemVer.SemVer`（拿 `MustParse`）与
//    `using static ...Core.Core`（拿 `Version()`），不用 `using TypeScript.SemVer;` 是为了避免"类型 `Version` 与
//    方法 `Version()` 同名"的解析歧义。
//
// 8) `GetPaths` 里"非字符串元素"的分支：Go 的 `slice := make([]string, n)` 零值是 `""`，C# 的 `new string[n]`
//    零值是 null，故显式写 `""`，保持逐元素与 Go 相同（调用方看到的是空串而不是 null）。
using TypeScript.Collections;
using TypeScript.Diagnostics;
using static TypeScript.Collections.Collections;
using static TypeScript.Core.Core;
using static TypeScript.Diagnostics.Diagnostics;
using static TypeScript.SemVer.SemVer;
using static TypeScript.TSPath.TSPath;
using SemVerVersion = TypeScript.SemVer.Version;

// `Path` 同时被 `System.IO.Path`（ImplicitUsings）与 `TypeScript.TSPath.Path` 提供 → 用别名消歧
// （repo 既有做法：ParserCorpusTests / TSPath 的消费方）。
using Path = TypeScript.TSPath.Path;

namespace TypeScript.PackageJson;

/// <summary>
/// Go: <c>type diagnosticAndArgs struct { message *diagnostics.Message; args []any }</c>（未导出 → C# internal PascalCase）。
/// </summary>
internal readonly struct DiagnosticAndArgs
{
    internal readonly Message _message;

    internal readonly object?[] _args;

    internal DiagnosticAndArgs(Message message, object?[] args)
    {
        _message = message;
        _args = args;
    }
}

public sealed partial class PackageJson
{
    // Go: var typeScriptVersion = semver.MustParse(core.Version())（cache.go:13 的包级 var）
    private static readonly SemVerVersion _typeScriptVersion = MustParse(Version());

    // Go: versionPaths VersionPaths（零值结构体 → C# 里给个非 null 的零值对象，见文件头第 2 条）
    private VersionPaths _versionPaths = new();

    // Go: versionTraces []diagnosticAndArgs
    private readonly List<DiagnosticAndArgs> _versionTraces = [];

    // Go: once sync.Once（见文件头第 3 条）
    private readonly Lock _onceLock = new();

    private bool _onceDone;

    /// <summary>
    /// Go: <c>func (p *PackageJson) GetVersionPaths(trace func(m *diagnostics.Message, args ...any)) VersionPaths</c>。
    /// 计算只做一次；trace 非空时每次调用都重放已记录的诊断（与 Go 一致）。
    /// </summary>
    public VersionPaths GetVersionPaths(Action<Message, object?[]>? trace = null)
    {
        lock (_onceLock)
        {
            if (!_onceDone)
            {
                _onceDone = true;
                ComputeVersionPaths();
            }
        }

        if (trace is not null)
        {
            foreach (var msg in _versionTraces)
            {
                trace(msg._message, msg._args);
            }
        }

        return _versionPaths;
    }

    // Go: once.Do(func() { … }) 的函数体（逐行对应）
    private void ComputeVersionPaths()
    {
        if (PathFields.TypesVersions.Type == JSONValueType.NotPresent)
        {
            _versionTraces.Add(new DiagnosticAndArgs(
                X_package_json_does_not_have_a_0_field,
                ["typesVersions"]));
            return;
        }

        if (PathFields.TypesVersions.Type != JSONValueType.Object)
        {
            _versionTraces.Add(new DiagnosticAndArgs(
                Expected_type_of_0_field_in_package_json_to_be_1_got_2,
                ["typesVersions", "object", PathFields.TypesVersions.Type.String()]));
            return;
        }

        _versionTraces.Add(new DiagnosticAndArgs(
            X_package_json_has_a_typesVersions_field_with_version_specific_path_mappings,
            ["typesVersions"]));

        foreach (var (key, value) in PathFields.TypesVersions.AsObject()!.Entries())
        {
            if (!TryParseVersionRange(key, out var keyRange))
            {
                _versionTraces.Add(new DiagnosticAndArgs(
                    X_package_json_has_a_typesVersions_entry_0_that_is_not_a_valid_semver_range,
                    [key]));
                continue;
            }

            if (keyRange.Test(_typeScriptVersion))
            {
                if (value!.Type != JSONValueType.Object)
                {
                    _versionTraces.Add(new DiagnosticAndArgs(
                        Expected_type_of_0_field_in_package_json_to_be_1_got_2,
                        ["typesVersions['" + key + "']", "object", value.Type.String()]));
                    return;
                }

                _versionPaths = new VersionPaths
                {
                    Version = key,
                    _pathsJSON = value.AsObject(),
                };
                return;
            }
        }

        _versionTraces.Add(new DiagnosticAndArgs(
            X_package_json_does_not_have_a_typesVersions_entry_that_matches_version_0,
            [VersionMajorMinor()]));
    }

    /// <summary>Go: <c>func NewInfoCache(currentDirectory string, useCaseSensitiveFileNames bool) *InfoCache</c>。</summary>
    public static InfoCache NewInfoCache(string currentDirectory, bool useCaseSensitiveFileNames)
    {
        return new InfoCache(currentDirectory, useCaseSensitiveFileNames);
    }
}

/// <summary>
/// Go: <c>type VersionPaths struct { Version string; pathsJSON *OrderedMap[string, JSONValue]; paths *OrderedMap[string, []string] }</c>。
/// 命中当前 TS 版本的 <c>typesVersions</c> 条目（见文件头第 4 条：C# 是 class，返回同一个对象）。
/// </summary>
public sealed class VersionPaths
{
    /// <summary>Go 的导出字段 <c>Version string</c>。</summary>
    public string Version = "";

    internal OrderedMap<string, JSONValue>? _pathsJSON;

    internal OrderedMap<string, string[]>? _paths;

    public bool Exists()
    {
        // Go: return v != nil && v.Version != "" && v.pathsJSON != nil（`v != nil` 见文件头第 2 条）
        return Version != "" && _pathsJSON is not null;
    }

    public OrderedMap<string, string[]>? GetPaths()
    {
        if (!Exists())
        {
            return null;
        }

        if (_paths is not null)
        {
            return _paths;
        }

        var pathsJSON = _pathsJSON!;
        var paths = NewOrderedMapWithSizeHint<string, string[]>(pathsJSON.Size());
        foreach (var (key, value) in pathsJSON.Entries())
        {
            if (value!.Type != JSONValueType.Array)
            {
                continue;
            }

            var array = value.AsArray();
            var slice = new string[array.Length];
            for (var i = 0; i < array.Length; i++)
            {
                var path = array[i];
                if (path.Type != JSONValueType.String)
                {
                    // Go: `slice[i] = path.Value.(string)` 不执行时该位置是切片的零值 ""（见文件头第 8 条）
                    slice[i] = "";
                    continue;
                }

                slice[i] = path.AsString();
            }

            paths.Set(key, slice);
        }

        _paths = paths;
        return _paths;
    }
}

/// <summary>
/// Go: <c>type InfoCacheEntry struct { PackageDirectory string; DirectoryExists bool; Contents *PackageJson }</c>。
/// </summary>
public sealed class InfoCacheEntry
{
    /// <summary>Go 的导出字段 <c>PackageDirectory string</c>。</summary>
    public string PackageDirectory = "";

    /// <summary>Go 的导出字段 <c>DirectoryExists bool</c>。</summary>
    public bool DirectoryExists;

    /// <summary>Go 的导出字段 <c>Contents *PackageJson</c>（对应 Go 的可空指针）。</summary>
    public PackageJson? Contents;

    // Go: return p != nil && p.Contents != nil（`p != nil` 见文件头第 2 条）
    public bool Exists()
    {
        return Contents is not null;
    }

    public PackageJson? GetContents()
    {
        // Go: if p == nil || p.Contents == nil { return nil }
        return Contents;
    }

    public string GetDirectory()
    {
        return PackageDirectory;
    }

    /// <summary>
    /// WithPackageDirectory returns an entry whose PackageDirectory matches the
    /// caller's value. The package.json info cache is keyed by the canonical
    /// path of the package.json file, but multiple callers may look up the same
    /// package.json using directory paths that differ only by a trailing
    /// separator (e.g. "node_modules/preact/compat" vs
    /// "node_modules/preact/compat/"). Because the cache uses first-writer-wins
    /// semantics, a later caller may receive an entry whose PackageDirectory
    /// doesn't match its own candidate path. Downstream code compares the
    /// candidate against PackageDirectory, so we must return a corrected
    /// shallow copy when they diverge.
    /// See https://github.com/microsoft/TypeScript/pull/50740.
    /// </summary>
    public InfoCacheEntry WithPackageDirectory(string packageDirectory)
    {
        if (PackageDirectory == packageDirectory)
        {
            return this;
        }

        return new InfoCacheEntry
        {
            PackageDirectory = packageDirectory,
            DirectoryExists = DirectoryExists,
            Contents = Contents,
        };
    }
}

/// <summary>Go: <c>type InfoCache struct { cache SyncMap[Path, *InfoCacheEntry]; … }</c>（package.json 信息缓存）。</summary>
public sealed class InfoCache
{
    private readonly SyncMap<Path, InfoCacheEntry> _cache = new();

    private readonly string _currentDirectory;

    private readonly bool _useCaseSensitiveFileNames;

    // Go 里唯一的外部构造入口是 NewInfoCache（`PackageJson.NewInfoCache`，见文件头第 1 条）；
    // 同程序集内保留构造器，对应 Go 的 `&InfoCache{…}`。
    internal InfoCache(string currentDirectory, bool useCaseSensitiveFileNames)
    {
        _currentDirectory = currentDirectory;
        _useCaseSensitiveFileNames = useCaseSensitiveFileNames;
    }

    public InfoCacheEntry? Get(string packageJsonPath)
    {
        var key = ToPath(packageJsonPath, _currentDirectory, _useCaseSensitiveFileNames);
        var (value, ok) = _cache.Load(key);
        if (ok)
        {
            return value;
        }

        return null;
    }

    public InfoCacheEntry Set(string packageJsonPath, InfoCacheEntry info)
    {
        var key = ToPath(packageJsonPath, _currentDirectory, _useCaseSensitiveFileNames);

        // Go: actual, _ := p.cache.LoadOrStore(key, info); return actual
        var (actual, _) = _cache.LoadOrStore(key, info);

        // LoadOrStore 的两个分支都非 null（已存在 → 旧值；否则 → 传入的 info），`?? info` 只是兜底，
        // 避免用 `!` 硬断言（SyncMap 的 V? 只是可空注解）。
        return actual ?? info;
    }

    public void Range(Func<Path, InfoCacheEntry?, bool> f)
    {
        _cache.Range(f);
    }
}
