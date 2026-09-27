// ported from: tsc/internal/project/snapshotfs.go:1-901
//
// 与 Go 的偏离（逐条说明）：
//
// 1) **Go 的内嵌**：`cachedLayeredFileSystem` 内嵌 `*cachedvfs.FS`（只为了提升 vfs.FS 的方法）。
//    C# 的 `CachedVfs.From(fs)` 返回的是 **FS 接口**（不是可继承的基类），接口无法"内嵌"，故
//    `CachedLayeredFileSystem` 持有 `_fs`（= `CachedVfs.From(fileSystem)`）并把 FS 的 12 个成员
//    逐一转发（与 `Vfs/WrapVfs` 里包装类的既有做法一致）。`GetFile`/`GetFileByPath`/`Overlays`/
//    `ExpandFileChanges` 四个额外成员按 Go 转发给 `_layered`。
//
// 2) `sync.Mutex` → `System.Threading.Lock` + `lock (...)`；`sync.OnceValue` → `Lazy<T>`
//    （`LazyThreadSafetyMode.ExecutionAndPublication`，与 sync.Once 的"只执行一次并缓存结果"一致；
//    唯一差异是 Go 的 Once 在函数 panic 后仍标记完成，Lazy 会缓存异常 —— 本路径不会 panic）。
//
// 3) `map[K]V` → `Dictionary<K,V>`；Go 的 nil map（`SnapshotFS` 零值、`Finalize` 返回 nil、
//    调用点传 nil 的 `cacheFiles`/`cacheDirectories`/`nodeModulesRealpathAliases`）→ `Dictionary<K,V>?`，
//    读取处一律先判 null（`x != nil` → `x is not null`）。`dirty.Map.Finalize` + `core.FirstResult`
//    → `Finalize().Result`（`FirstResult` 在 C# 里没有对应函数，见 Core/Core.cs 的说明）。
//
// 4) `collections.Set[T]`/`collections.SyncSet[T]` → `TypeScript.Collections.Set<T>`/`SyncSet<T>`；
//    `dirty.SyncMap`/`dirty.Map`/`dirty.CloneableMap`/`dirty.FinalizationHooks` → 同名类型的
//    `TypeScript.Project.Dirty` 命名空间版本（**注意**：`SyncMap` 在 Collections 与 Dirty 里都有，
//    本文件里 Dirty 的一律写成 `Dirty.SyncMap` 全限定以免 CS0104）。
//
// 5) Go 的 `map[tspath.Path]FileHandle`/`map[tspath.Path]*Overlay` 等作为**函数参数**传递时可以是
//    nil（测试与 snapshot.go 都传 nil）→ `Dictionary<K,V>?`，所有读取点判空（`_previousOpenFiles?.ContainsKey(...)`）。
//
// 6) **包级函数落点**（Go 的 `project` 包没有独立静态类可放，见本仓 Locale 的先例）：
//    `newCachedLayeredFileSystem` → 它构造的类型的静态工厂 `CachedLayeredFileSystem.NewCachedLayeredFileSystem`；
//    `newSnapshotFSBuilderFromSource` → `SnapshotFSBuilder.NewSnapshotFSBuilderFromSource`；
//    `newSourceFS` → `SourceFS.NewSourceFS`；`mergeCachedDirectoryEntries`（纯工具函数）→ 挂在本文件最
//    相关的类型上：`SnapshotFS.MergeCachedDirectoryEntries(...)`；`isRelevantExtension`/
//    `isNodeModulesPath`/`hasOpenFileWithin` → `SnapshotFSBuilder` 上的静态成员。
//
// 7) `strings.Contains(string(path), "/node_modules/")` → `path.Value.Contains("/node_modules/", StringComparison.Ordinal)`
//    （本仓 string 是 UTF-16，但这两个字符串都是 ASCII，`strings.Contains` 的字节语义与序数比较等价）。
//    `string(path)` → `path.Value`（`Path` 有显式 string 转换，直接写 `path.Value` 更贴近 Go）。
//
// 8) `tspath.FileExtensionIsOneOf(fileName, extensions)` 在 extensions 为 nil 的 Go 里返回 false
//    （range 空切片）；C# 的 `FileExtensionIsOneOf` 会 NullReferenceException，故**函数入口**先
//    `contentMapperExtensions ?? []`（`[]string` → `string[]?` + 消费处 `?? []` 的仓规）。
//
// 9) `vfs.Entries` 的 `Files`/`Directories` 在 C# 是 `string[]`（Go 的 nil 切片 → 空数组），
//    `Symlinks` 是 `HashSet<string>?`（null = "该 FS 不提供符号链接信息"）。`maps.Clone` →
//    `new HashSet<string>(src, src.Comparer)`（保留源比较器）；`append` → `List<string>` + `ToArray`。
//
// 10) `collections.NewSetWithSizeHint[T](n)` → `TypeScript.Collections.Set<T>.NewSetWithSizeHint(n)`；
//     `change.Changed = *newChanged`（Go 值赋值）→ `change.Changed = newChanged`（C# 的 Set 是引用类型）。
//
// 11) `panic(...)` → `Debug.Fail(...)`。Go 的 `sourceFS.WriteFile/AppendFile/Remove/Chtimes` 是
//     `panic("unimplemented")`，逐条保留为 `Debug.Fail("unimplemented")`（会抛 DebugFailureException）。
//
// 12) Go 的 `sync.RWMutex` 之类本文件没有；`RealpathAliasSet` 的 `sync.Mutex` → `Lock`。
//
// 13) Go 的 `cachedFile`/`realpathAliasSet` 未导出字段（`realpathPath`/`needsReload`/`paths`/
//     `fileName`/`content`/`hash`）在本仓是 internal 且 `_camelCase`，跨文件（snapshotfs.go ↔
//     overlayfs.go ↔ 测试）照 Go 同 package 的无障碍访问。
//
// 14) `tspath.Path` 的空值是 Go 的空串；C# 的 `Path` 是 record struct（`default` 的 `Value` 是 null）。
//     与 Go 的 `realpathPath == ""` 对应处写 `string.IsNullOrEmpty(_realpathPath.Value)`。
//
// 15) ⚠ **已落地物缺口**：`TypeScript.Project.Dirty.SyncMapEntry<K,V>` 没有 `Key()`（Go 由内嵌的未导出
//     `mapEntry` 提升出 `Key()`，`MapEntry` 有、`SyncMapEntry` 漏了），所以 `deleteCacheEntry` /
//     `invalidateNodeModulesCache` 里的 `entry.Key()` 改写为 internal 字段 `entry._key`（同 package 可见，
//     语义完全一致）。已把缺口告知 Dirty 的 owner（M8Project）。
//
// 16) 同样地，`xxh3.HashString128`/`lsconv.FileNameToDocumentURI`/`collections.NewSetWithSizeHint` 在 C# 是
//     **与本命名空间同名的静态类**上的成员（`TypeScript.Xxh3.Xxh3` / `TypeScript.LsConv.LsConv` /
//     `TypeScript.Collections.Collections`），裸写 `Xxh3.HashString128` 会解析到命名空间（CS0234），
//     故用 `using static ...;` 后只写裸成员名。
using TypeScript.Collections;
using TypeScript.Core;
using TypeScript.LsConv;
using TypeScript.LsProto;
using TypeScript.Project.Dirty;
using TypeScript.TSPath;
using TypeScript.Vfs;
using static TypeScript.Collections.Collections;
using static TypeScript.Core.Core;
using static TypeScript.LsConv.LsConv;
using static TypeScript.Project.Dirty.Dirty;
using static TypeScript.TSPath.TSPath;
using static TypeScript.Xxh3.Xxh3;
using Path = TypeScript.TSPath.Path;
using VfsFileInfo = TypeScript.Vfs.FileInfo;

namespace TypeScript.Project;

/// <summary>Go: <c>type FileHandleSource interface</c>（snapshotfs.go:21-24）。</summary>
public interface FileHandleSource
{
    FileHandle? GetFile(string fileName);

    FileHandle? GetFileByPath(string fileName, Path path);
}

/// <summary>Go: <c>type FileSource interface</c>（snapshotfs.go:26-31，内嵌 <see cref="FileHandleSource"/>）。</summary>
public interface FileSource : FileHandleSource
{
    /// <summary>Go: <c>FS() vfs.FS</c>。</summary>
    FS FS();

    bool FileExists(string fileName, Path path);

    Entries GetAccessibleEntries(string path);
}

/// <summary>Go: <c>type cachedLayeredFileSystem struct</c>（snapshotfs.go:33-36，见文件头第 1 条）。</summary>
internal sealed class CachedLayeredFileSystem : LayeredFileSystem
{
    private readonly FS _fs;

    private readonly LayeredFileSystem _layered;

    private CachedLayeredFileSystem(FS fs, LayeredFileSystem layered)
    {
        _fs = fs;
        _layered = layered;
    }

    /// <summary>Go: <c>func newCachedLayeredFileSystem(fileSystem LayeredFileSystem) LayeredFileSystem</c>（snapshotfs.go:38-43）。</summary>
    internal static LayeredFileSystem NewCachedLayeredFileSystem(LayeredFileSystem fileSystem)
    {
        return new CachedLayeredFileSystem(TypeScript.Vfs.CachedVfs.CachedVfs.From(fileSystem), fileSystem);
    }

    public FileHandle? GetFile(string fileName)
    {
        return _layered.GetFile(fileName);
    }

    public FileHandle? GetFileByPath(string fileName, Path path)
    {
        return _layered.GetFileByPath(fileName, path);
    }

    public Dictionary<Path, Overlay> Overlays()
    {
        return _layered.Overlays();
    }

    /// <summary>Go: <c>func (fs *cachedLayeredFileSystem) ExpandFileChanges(change FileChangeSummary) FileChangeSummary</c>
    /// （snapshotfs.go:57-62）：Go 用类型断言探测下层是否实现 <see cref="FileChangeExpander"/>。</summary>
    internal FileChangeSummary ExpandFileChanges(FileChangeSummary change)
    {
        return _layered is FileChangeExpander expander ? expander.ExpandFileChanges(change) : change;
    }

    // vfs.FS 的成员（Go 靠内嵌 *cachedvfs.FS 提升，见文件头第 1 条）。

    public bool UseCaseSensitiveFileNames()
    {
        return _fs.UseCaseSensitiveFileNames();
    }

    public bool FileExists(string path)
    {
        return _fs.FileExists(path);
    }

    public bool TryReadFile(string path, out string contents)
    {
        return _fs.TryReadFile(path, out contents);
    }

    public void WriteFile(string path, string data)
    {
        _fs.WriteFile(path, data);
    }

    public void AppendFile(string path, string data)
    {
        _fs.AppendFile(path, data);
    }

    public void Remove(string path)
    {
        _fs.Remove(path);
    }

    public void Chtimes(string path, DateTime accessTime, DateTime modificationTime)
    {
        _fs.Chtimes(path, accessTime, modificationTime);
    }

    public bool DirectoryExists(string path)
    {
        return _fs.DirectoryExists(path);
    }

    public Entries GetAccessibleEntries(string path)
    {
        return _fs.GetAccessibleEntries(path);
    }

    public VfsFileInfo? Stat(string path)
    {
        return _fs.Stat(path);
    }

    public string Realpath(string path)
    {
        return _fs.Realpath(path);
    }
}

/// <summary>Go: <c>type realpathAliasSet struct</c>（snapshotfs.go:67-71）：单个 realpath 的符号链接路径集合
/// （线程安全，且实现 <c>dirty.Cloneable</c>，可作为 <c>dirty.SyncMap</c> 的值）。</summary>
internal sealed class RealpathAliasSet : Cloneable<RealpathAliasSet>
{
    private readonly Lock _mu = new();

    internal Set<Path> _paths = new();

    internal void Add(Path path)
    {
        lock (_mu)
        {
            _paths.Add(path);
        }
    }

    public RealpathAliasSet Clone()
    {
        lock (_mu)
        {
            var clone = new RealpathAliasSet();
            if (_paths.Len() > 0)
            {
                clone._paths = _paths.Clone();
            }

            return clone;
        }
    }
}

/// <summary>Go: <c>type SnapshotFS struct</c>（snapshotfs.go:73-84）。</summary>
public sealed class SnapshotFS : FileSource
{
    internal Func<string, Path> _toPath = null!;

    internal LayeredFileSystem _fs = null!;

    internal Dictionary<Path, CachedFile>? _cacheFiles;

    internal Dictionary<Path, CloneableMap<Path, string>>? _cacheDirectories;

    // Go: readFiles collections.SyncMap[tspath.Path, memoizedCachedFile]（值字段，零值可用）。
    private readonly TypeScript.Collections.SyncMap<Path, Func<FileHandle?>> _readFiles = new();

    // Go: nodeModulesRealpathAliases map[tspath.Path]*realpathAliasSet（realpath 键 → 一组符号链接键）。
    internal Dictionary<Path, RealpathAliasSet>? _nodeModulesRealpathAliases;

    public FS FS()
    {
        return _fs;
    }

    public FileHandle? GetFile(string fileName)
    {
        return GetFileByPath(fileName, _toPath(fileName));
    }

    public bool FileExists(string fileName, Path path)
    {
        if (_cacheFiles is not null && _cacheFiles.ContainsKey(path))
        {
            return true;
        }

        return _fs.FileExists(fileName);
    }

    public FileHandle? GetFileByPath(string fileName, Path path)
    {
        if (_cacheFiles is not null && _cacheFiles.TryGetValue(path, out var file))
        {
            return file;
        }

        // Go: sync.OnceValue(func() FileHandle { return s.fs.GetFileByPath(fileName, path) })，见文件头第 2 条。
        var lazy = new Lazy<FileHandle?>(
            () => _fs.GetFileByPath(fileName, path),
            LazyThreadSafetyMode.ExecutionAndPublication);
        var (entry, _) = _readFiles.LoadOrStore(path, () => lazy.Value);
        return entry!();
    }

    public Entries GetAccessibleEntries(string directoryName)
    {
        var lowerEntries = _fs.GetAccessibleEntries(directoryName);
        if (_cacheDirectories is null || !_cacheDirectories.TryGetValue(_toPath(directoryName), out var directory))
        {
            return lowerEntries;
        }

        return MergeCachedDirectoryEntries(
            lowerEntries,
            directory,
            path => _cacheFiles is not null && _cacheFiles.ContainsKey(path) || _fs.FileExists(path.Value),
            _fs.UseCaseSensitiveFileNames());
    }

    /// <summary>Go: <c>func mergeCachedDirectoryEntries(...) vfs.Entries</c>（snapshotfs.go:129-166）。</summary>
    internal static Entries MergeCachedDirectoryEntries(
        Entries directoryEntries,
        CloneableMap<Path, string> cachedEntries,
        Func<Path, bool> isCachedFile,
        bool useCaseSensitiveFileNames)
    {
        var symlinks = directoryEntries.Symlinks is null
            ? null
            : new HashSet<string>(directoryEntries.Symlinks, directoryEntries.Symlinks.Comparer);
        var files = new List<string>();
        var directories = new List<string>();

        bool EqualName(string left, string right) =>
            GetCanonicalFileName(left, useCaseSensitiveFileNames) == GetCanonicalFileName(right, useCaseSensitiveFileNames);

        bool HasName(List<string> names, string name) => names.Exists(candidate => EqualName(candidate, name));

        foreach (var (childPath, childName) in cachedEntries)
        {
            symlinks?.RemoveWhere(name => EqualName(name, childName));
            if (isCachedFile(childPath))
            {
                files.Add(childName);
            }
            else
            {
                directories.Add(childName);
            }
        }

        foreach (var fileName in directoryEntries.Files)
        {
            if (!HasName(files, fileName) && !HasName(directories, fileName))
            {
                files.Add(fileName);
            }
        }

        foreach (var directoryName in directoryEntries.Directories)
        {
            if (!HasName(files, directoryName) && !HasName(directories, directoryName))
            {
                directories.Add(directoryName);
            }
        }

        return new Entries([.. files], [.. directories], symlinks);
    }

    /// <summary>Go: <c>func (s *SnapshotFS) expandRealpathAliases(change FileChangeSummary) FileChangeSummary</c>
    /// （snapshotfs.go:578-616）。</summary>
    internal FileChangeSummary ExpandRealpathAliases(FileChangeSummary change)
    {
        if (_nodeModulesRealpathAliases is null || _nodeModulesRealpathAliases.Count == 0)
        {
            return change;
        }

        Set<DocumentUri>? additionalChanged = null;
        foreach (var uri in change.Changed.Keys())
        {
            var path = _toPath(uri.FileName());
            if (_nodeModulesRealpathAliases.TryGetValue(path, out var aliases))
            {
                foreach (var aliasPath in aliases._paths.Keys())
                {
                    additionalChanged ??= new Set<DocumentUri>();
                    additionalChanged.Add(FileNameToDocumentURI(aliasPath.Value));
                }
            }
        }

        if (additionalChanged is not null)
        {
            foreach (var uri in additionalChanged.Keys())
            {
                change.Changed.Add(uri);
            }
        }

        Set<DocumentUri>? additionalDeleted = null;
        foreach (var uri in change.Deleted.Keys())
        {
            var path = _toPath(uri.FileName());
            if (_nodeModulesRealpathAliases.TryGetValue(path, out var aliases))
            {
                foreach (var aliasPath in aliases._paths.Keys())
                {
                    additionalDeleted ??= new Set<DocumentUri>();
                    additionalDeleted.Add(FileNameToDocumentURI(aliasPath.Value));
                }
            }
        }

        if (additionalDeleted is not null)
        {
            foreach (var uri in additionalDeleted.Keys())
            {
                change.Deleted.Add(uri);
            }
        }

        return change;
    }
}

/// <summary>Go: <c>type snapshotFSBuilder struct</c>（snapshotfs.go:168-175）。</summary>
internal sealed class SnapshotFSBuilder : FileSource
{
    internal LayeredFileSystem _fs;

    internal Dirty.SyncMap<Path, CachedFile> _cacheFiles;

    internal Dirty.Map<Path, CloneableMap<Path, string>> _cacheDirectories;

    internal Set<Path> _sourceBackedReplacements;

    internal Dirty.SyncMap<Path, RealpathAliasSet> _nodeModulesRealpathAliases;

    internal Func<string, Path> _toPath;

    private SnapshotFSBuilder(
        LayeredFileSystem fs,
        Dirty.SyncMap<Path, CachedFile> cacheFiles,
        Dirty.Map<Path, CloneableMap<Path, string>> cacheDirectories,
        Set<Path> sourceBackedReplacements,
        Dirty.SyncMap<Path, RealpathAliasSet> nodeModulesRealpathAliases,
        Func<string, Path> toPath)
    {
        _fs = fs;
        _cacheFiles = cacheFiles;
        _cacheDirectories = cacheDirectories;
        _sourceBackedReplacements = sourceBackedReplacements;
        _nodeModulesRealpathAliases = nodeModulesRealpathAliases;
        _toPath = toPath;
    }

    /// <summary>Go: <c>func newSnapshotFSBuilderFromSource(...) *snapshotFSBuilder</c>（snapshotfs.go:186-205）。</summary>
    internal static SnapshotFSBuilder NewSnapshotFSBuilderFromSource(
        LayeredFileSystem fs,
        Dictionary<Path, CachedFile>? cacheFiles,
        Dictionary<Path, CloneableMap<Path, string>>? cacheDirectories,
        Dictionary<Path, RealpathAliasSet>? nodeModulesRealpathAliases,
        Func<string, Path> toPath)
    {
        fs = CachedLayeredFileSystem.NewCachedLayeredFileSystem(fs);

        return new SnapshotFSBuilder(
            fs,
            NewSyncMap(cacheFiles),
            NewMap(cacheDirectories),
            new Set<Path>(),
            NewSyncMap(nodeModulesRealpathAliases),
            toPath);
    }

    public FS FS()
    {
        return _fs;
    }

    /// <summary>Go: <c>func (s *snapshotFSBuilder) Finalize() (*SnapshotFS, bool)</c>（snapshotfs.go:211-301）。</summary>
    internal (SnapshotFS Snapshot, bool Changed) Finalize()
    {
        // Synchronize directory structure based on added and deleted cache entries.
        Dictionary<Path, CachedFile>? deleted = null;

        void OnAddedFile(Path path, string fileName)
        {
            var childPath = path;
            var child = fileName;
            while (true)
            {
                var parentPath = childPath.GetDirectoryPath();
                var parent = GetDirectoryPath(child);
                if (childPath == parentPath)
                {
                    break; // reached root
                }

                var baseName = GetBaseFileName(child);
                var (dirEntry, ok) = _cacheDirectories.Get(parentPath);
                if (ok)
                {
                    dirEntry!.Change(dir => dir[childPath] = baseName);
                    break;
                }

                _cacheDirectories.Add(parentPath, new CloneableMap<Path, string> { [childPath] = baseName });
                childPath = parentPath;
                child = parent;
            }
        }

        void OnDeletedFileOrDirectory(Path path)
        {
            var (dirEntry, ok) = _cacheDirectories.Get(path.GetDirectoryPath());
            if (!ok)
            {
                return;
            }

            dirEntry!.Change(dir =>
            {
                dir.Remove(path);
                if (dir.Count == 0)
                {
                    dirEntry.Delete();
                    OnDeletedFileOrDirectory(dirEntry.Key());
                }
            });
        }

        var (cacheFiles, changed) = _cacheFiles.FinalizeWith(new FinalizationHooks<Path, CachedFile>
        {
            OnDelete = (key, value) =>
            {
                if (_sourceBackedReplacements.Has(key))
                {
                    return;
                }

                deleted ??= [];
                deleted[key] = value;
            },
            OnAdd = (key, value) => OnAddedFile(key, value.FileName()),
        });

        if (deleted is not null)
        {
            foreach (var path in deleted.Keys)
            {
                OnDeletedFileOrDirectory(path);
            }
        }

        // Prune deleted symlink paths from realpath alias sets before finalizing,
        // so that empty sets are dropped during finalization.
        if (deleted is not null)
        {
            foreach (var (deletedPath, deletedFile) in deleted)
            {
                if (string.IsNullOrEmpty(deletedFile._realpathPath.Value))
                {
                    continue;
                }

                var (entry, ok) = _nodeModulesRealpathAliases.Load(deletedFile._realpathPath);
                if (ok)
                {
                    entry!.Locked(e =>
                    {
                        e.Change(aliasSet => aliasSet._paths.Delete(deletedPath));
                        if (e.Value()._paths.Len() == 0)
                        {
                            e.Delete();
                        }
                    });
                }
            }
        }

        var (nodeModulesRealpathAliases, aliasesChanged) = _nodeModulesRealpathAliases.Finalize();

        var snapshot = new SnapshotFS
        {
            _fs = _fs,
            _cacheFiles = cacheFiles,
            _cacheDirectories = _cacheDirectories.Finalize().Result,
            _nodeModulesRealpathAliases = nodeModulesRealpathAliases,
            _toPath = _toPath,
        };
        return (snapshot, changed || aliasesChanged);
    }

    public FileHandle? GetFile(string fileName)
    {
        var path = _toPath(fileName);
        return GetFileByPath(fileName, path);
    }

    /// <summary>Go: <c>func (s *snapshotFSBuilder) deleteCacheEntry(entry *dirty.SyncMapEntry[...])</c>（snapshotfs.go:309-314）。</summary>
    internal void DeleteCacheEntry(SyncMapEntry<Path, CachedFile> entry)
    {
        if (entry.Value() is { } file && _fs.FileExists(file.FileName()))
        {
            _sourceBackedReplacements.Add(entry._key);
        }

        entry.Delete();
    }

    public bool FileExists(string fileName, Path path)
    {
        var (entry, ok) = _cacheFiles.Load(path);
        if (ok)
        {
            if (entry!.Value() is null)
            {
                return false;
            }

            // Entry may be dirty - reload to check current state in the source filesystem.
            return ReloadEntryIfNeeded(entry) is not null;
        }

        // Path never loaded into cacheFiles - use cached stat (no file read).
        return _fs.FileExists(fileName);
    }

    public FileHandle? GetFileByPath(string fileName, Path path)
    {
        var (entry, ok) = _cacheFiles.Load(path);
        if (ok)
        {
            return ReloadEntryIfNeeded(entry!);
        }

        var file = _fs.GetFileByPath(fileName, path);
        if (file is null || file.IsOverlay())
        {
            return file;
        }

        return CacheSourceFile(fileName, path, file);
    }

    public Entries GetAccessibleEntries(string path)
    {
        var lowerEntries = _fs.GetAccessibleEntries(path);
        var (directory, ok) = _cacheDirectories.Get(_toPath(path));
        if (!ok)
        {
            return lowerEntries;
        }

        return SnapshotFS.MergeCachedDirectoryEntries(
            lowerEntries,
            directory!.Value(),
            childPath =>
            {
                var (entry, cached) = _cacheFiles.Load(childPath);
                return (cached && entry!.Value() is not null) || _fs.FileExists(childPath.Value);
            },
            _fs.UseCaseSensitiveFileNames());
    }

    private FileHandle? CacheSourceFile(string fileName, Path path, FileHandle source)
    {
        var file = CachedFile.NewCachedFile(fileName, source.Content());
        file._hash = source.Hash();
        var (entry, loaded) = _cacheFiles.LoadOrStore(path, file);
        if (entry is null)
        {
            return null;
        }

        if (!loaded && path.Value.Contains("/node_modules/", StringComparison.Ordinal))
        {
            RecordRealpathAlias(entry, fileName, path);
        }

        return ReloadEntryIfNeeded(entry);
    }

    private FileHandle? GetCachedFile(string fileName, Path path, bool forceReload)
    {
        var (entry, loaded) = _cacheFiles.LoadOrStore(path, new CachedFile { _fileName = fileName, _needsReload = true });
        if (entry is not null)
        {
            if (!loaded && path.Value.Contains("/node_modules/", StringComparison.Ordinal))
            {
                RecordRealpathAlias(entry, fileName, path);
            }

            return forceReload ? ReloadEntry(entry) : ReloadEntryIfNeeded(entry);
        }

        return null;
    }

    /// <summary>Go: <c>func (s *snapshotFSBuilder) recordRealpathAlias(...)</c>（snapshotfs.go:398-411）：
    /// 检查 fileName 是否经符号链接访问，是则记录 realpath 键 → 符号链接键的映射（只对 node_modules 内的文件调用）。</summary>
    private void RecordRealpathAlias(SyncMapEntry<Path, CachedFile> cachedFileEntry, string symlinkFileName, Path symlinkPath)
    {
        var realpath = _fs.Realpath(symlinkFileName);
        var realpathPath = _toPath(realpath);
        if (realpathPath != symlinkPath)
        {
            cachedFileEntry.Change(file => file._realpathPath = realpathPath);
            var (entry, _) = _nodeModulesRealpathAliases.LoadOrStore(realpathPath, new RealpathAliasSet());
            entry!.Change(aliasSet => aliasSet.Add(symlinkPath));
        }
    }

    private FileHandle? ReloadEntry(SyncMapEntry<Path, CachedFile> entry)
    {
        var fileName = "";
        entry.Locked(e =>
        {
            if (e.Value() is { } value)
            {
                fileName = value._fileName;
            }
        });

        if (fileName == "")
        {
            return null;
        }

        // Read file outside the lock to avoid blocking other goroutines.
        var ok = _fs.TryReadFile(fileName, out var content);
        entry.Locked(e =>
        {
            if (e.Value() is null)
            {
                return;
            }

            if (ok)
            {
                e.Change(file =>
                {
                    file._content = content;
                    file._hash = HashString128(content);
                    file._needsReload = false;
                });
            }
            else
            {
                e.Delete();
            }
        });

        return entry.Value() is null ? null : entry.Value();
    }

    internal FileHandle? ReloadEntryIfNeeded(SyncMapEntry<Path, CachedFile> entry)
    {
        var fileName = "";
        entry.Locked(e =>
        {
            if (e.Value() is { } value && !value.MatchesDiskText())
            {
                fileName = value._fileName;
            }
        });

        if (fileName != "")
        {
            // Read file outside the lock to avoid blocking other goroutines.
            var ok = _fs.TryReadFile(fileName, out var content);
            entry.Locked(e =>
            {
                if (e.Value() is null || e.Value().MatchesDiskText())
                {
                    return; // another goroutine already reloaded it
                }

                if (ok)
                {
                    e.Change(file =>
                    {
                        file._content = content;
                        file._hash = HashString128(content);
                        file._needsReload = false;
                    });
                }
                else
                {
                    e.Delete();
                }
            });
        }

        return entry.Value() is null ? null : entry.Value();
    }

    internal bool WatchChangesOverlapCache(
        FileChangeSummary change,
        Dictionary<Path, FileHandle>? previousOpenFiles,
        Dictionary<Path, FileHandle>? openFiles)
    {
        foreach (var uri in change.Changed.Keys())
        {
            var path = _toPath(uri.FileName());
            if (previousOpenFiles?.ContainsKey(path) == true || openFiles?.ContainsKey(path) == true)
            {
                return true;
            }

            if (_cacheFiles.Load(path).Ok)
            {
                return true;
            }

            if (_nodeModulesRealpathAliases.Load(path).Ok)
            {
                return true;
            }
        }

        foreach (var uri in change.Deleted.Keys())
        {
            var path = _toPath(uri.FileName());
            if (previousOpenFiles?.ContainsKey(path) == true || openFiles?.ContainsKey(path) == true)
            {
                return true;
            }

            if (_cacheFiles.Load(path).Ok)
            {
                return true;
            }

            if (_nodeModulesRealpathAliases.Load(path).Ok)
            {
                return true;
            }
        }

        return false;
    }

    internal void InvalidateCache()
    {
        _cacheFiles.Range(entry =>
        {
            entry.Change(file => file._needsReload = true);
            return true;
        });
    }

    internal void InvalidateNodeModulesCache()
    {
        _cacheFiles.Range(entry =>
        {
            if (entry._key.Value.Contains("/node_modules/", StringComparison.Ordinal))
            {
                entry.Change(file => file._needsReload = true);
            }

            return true;
        });
    }

    internal FileChangeSummary MarkDirtyFiles(FileChangeSummary change)
    {
        if (change.Changed.Len() > 0)
        {
            var filteredChanged = new SyncSet<DocumentUri>();
            var wg = NewWorkGroup(false);
            foreach (var uri in change.Changed.Keys())
            {
                var path = _toPath(uri.FileName());
                var file = _fs.GetFileByPath(uri.FileName(), path);
                if (file is not null && file.IsOverlay())
                {
                    filteredChanged.Add(uri);
                    continue;
                }

                var (entry, ok) = _cacheFiles.Load(path);
                if (!ok)
                {
                    filteredChanged.Add(uri);
                    continue;
                }

                wg.Queue(() =>
                {
                    if (ReloadEntryIfContentChanged(entry!))
                    {
                        filteredChanged.Add(uri);
                    }
                });
            }

            wg.RunAndWait();
            var newChanged = NewSetWithSizeHint<DocumentUri>(filteredChanged.Size());
            foreach (var uri in filteredChanged.Keys())
            {
                newChanged.Add(uri);
            }

            change.Changed = newChanged;
        }

        foreach (var uri in change.Deleted.Keys())
        {
            var path = _toPath(uri.FileName());
            var (entry, ok) = _cacheFiles.Load(path);
            if (ok)
            {
                DeleteCacheEntry(entry!);
            }
        }

        return change;
    }

    private bool ReloadEntryIfContentChanged(SyncMapEntry<Path, CachedFile> entry)
    {
        var file = entry.Value();
        if (file is null)
        {
            return true;
        }

        var ok = _fs.TryReadFile(file._fileName, out var content);
        var changed = true;
        entry.Locked(e =>
        {
            var cur = e.Value();
            if (cur is null)
            {
                return;
            }

            if (!ok)
            {
                e.Delete();
                return;
            }

            if (content == cur._content)
            {
                changed = false;
                if (!cur.MatchesDiskText())
                {
                    e.Change(f => f._needsReload = false);
                }

                return;
            }

            e.Change(f =>
            {
                f._content = content;
                f._hash = HashString128(content);
                f._needsReload = false;
            });
        });
        return changed;
    }

    /// <summary>Go: <c>func isNodeModulesPath(path tspath.Path) bool</c>（snapshotfs.go:706-709）。</summary>
    internal static bool IsNodeModulesPath(Path path)
    {
        return path.Value.EndsWith("/node_modules", StringComparison.Ordinal)
            || path.Value.Contains("/node_modules/", StringComparison.Ordinal);
    }

    /// <summary>Go: <c>func isRelevantExtension(ext string) bool</c>（snapshotfs.go:651-658）。</summary>
    internal static bool IsRelevantExtension(string ext)
    {
        switch (ext)
        {
            case ".js":
            case ".jsx":
            case ".mjs":
            case ".cjs":
            case ".ts":
            case ".tsx":
            case ".mts":
            case ".cts":
            case ".json":
                return true;
        }

        return false;
    }

    /// <summary>Go: <c>func hasOpenFileWithin(...) bool</c>（snapshotfs.go:711-723）。</summary>
    internal static bool HasOpenFileWithin(
        Path path,
        Dictionary<Path, FileHandle>? previousOpenFiles,
        Dictionary<Path, FileHandle>? openFiles)
    {
        if (openFiles is not null)
        {
            foreach (var openFilePath in openFiles.Keys)
            {
                if (path.ContainsPath(openFilePath))
                {
                    return true;
                }
            }
        }

        if (previousOpenFiles is not null)
        {
            foreach (var openFilePath in previousOpenFiles.Keys)
            {
                if (path.ContainsPath(openFilePath))
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>Go: <c>func (s *snapshotFSBuilder) isRelevantFileName(...) bool</c>（snapshotfs.go:624-647）。</summary>
    private bool IsRelevantFileName(
        DocumentUri uri,
        string[]? contentMapperExtensions,
        Set<Path>? contentMapperWatchedFiles,
        Dictionary<Path, FileHandle>? openFiles)
    {
        var fileName = uri.FileName();
        if (contentMapperWatchedFiles is not null && contentMapperWatchedFiles.Has(_toPath(fileName)))
        {
            return true;
        }

        if (FileExtensionIsOneOf(fileName, contentMapperExtensions ?? []))
        {
            return true;
        }

        if (IsDynamicFileName(fileName))
        {
            return true;
        }

        var path = _toPath(fileName);
        if (openFiles?.ContainsKey(path) == true)
        {
            return true;
        }

        var i = path.Value.LastIndexOf('.');
        if (i < 0)
        {
            return false;
        }

        return IsRelevantExtension(path.Value[i..]);
    }

    /// <summary>Go: <c>func (s *snapshotFSBuilder) expandAndFilterWatchEvents(...) FileChangeSummary</c>（snapshotfs.go:662-697）。</summary>
    internal FileChangeSummary ExpandAndFilterWatchEvents(
        FileChangeSummary change,
        string[]? contentMapperExtensions,
        Set<Path>? contentMapperWatchedFiles,
        Dictionary<Path, FileHandle>? previousOpenFiles,
        Dictionary<Path, FileHandle>? openFiles)
    {
        if (change.Deleted.Len() > 0)
        {
            var filteredDeleted = new Set<DocumentUri>();
            foreach (var uri in change.Deleted.Keys())
            {
                var path = _toPath(uri.FileName());
                if (_cacheDirectories.Get(path).Ok || HasOpenFileWithin(path, previousOpenFiles, openFiles))
                {
                    CollectFilesRecursive(path, filteredDeleted, previousOpenFiles, openFiles);
                }
                else if (IsRelevantFileName(uri, contentMapperExtensions, contentMapperWatchedFiles, openFiles) || IsNodeModulesPath(path))
                {
                    // node_modules deletions must always be preserved for auto-import registry change handlers.
                    // They won't be in cacheDirectories since the registry doesn't use the snapshotFSBuilder for
                    // its file system, since we don't want to retain files read there.
                    filteredDeleted.Add(uri);
                }
            }

            change.Deleted = filteredDeleted;
        }

        if (change.Changed.Len() > 0)
        {
            var filteredChanged = new Set<DocumentUri>();
            foreach (var uri in change.Changed.Keys())
            {
                if (IsRelevantFileName(uri, contentMapperExtensions, contentMapperWatchedFiles, openFiles))
                {
                    filteredChanged.Add(uri);
                }
            }

            change.Changed = filteredChanged;
        }

        // We can't filter created events because any created path could be a directory symlink
        // that includes relevant files. configFileRegistryBuilder will do check if these paths
        // are directories if they fall within a config's wildcard directories.

        return change;
    }

    /// <summary>Go: <c>func (s *snapshotFSBuilder) collectFilesRecursive(...)</c>（snapshotfs.go:728-754）。</summary>
    private void CollectFilesRecursive(
        Path dirPath,
        Set<DocumentUri> files,
        Dictionary<Path, FileHandle>? previousOpenFiles,
        Dictionary<Path, FileHandle>? openFiles)
    {
        if (openFiles is not null)
        {
            foreach (var (path, file) in openFiles)
            {
                if (dirPath.ContainsPath(path))
                {
                    files.Add(FileNameToDocumentURI(file.FileName()));
                }
            }
        }

        if (previousOpenFiles is not null)
        {
            foreach (var (path, file) in previousOpenFiles)
            {
                if (dirPath.ContainsPath(path))
                {
                    files.Add(FileNameToDocumentURI(file.FileName()));
                }
            }
        }

        var (dirEntry, ok) = _cacheDirectories.Get(dirPath);
        if (!ok)
        {
            return;
        }

        foreach (var childPath in dirEntry!.Value().Keys)
        {
            var (entry, cached) = _cacheFiles.Load(childPath);
            if (cached)
            {
                if (entry!.Value() is { } file)
                {
                    files.Add(FileNameToDocumentURI(file.FileName()));
                }
            }

            CollectFilesRecursive(childPath, files, previousOpenFiles, openFiles);
        }
    }

    /// <summary>Go: <c>func (s *snapshotFSBuilder) convertOpenAndCloseToChanges(...) FileChangeSummary</c>（snapshotfs.go:756-789）。</summary>
    internal FileChangeSummary ConvertOpenAndCloseToChanges(
        FileChangeSummary change,
        Dictionary<Path, FileHandle>? previousOpenFiles,
        Dictionary<Path, FileHandle>? openFiles)
    {
        if (change.Opened != default && !IsDynamicFileName(change.Opened.FileName()))
        {
            var path = _toPath(change.Opened.FileName());
            var (entry, ok) = _cacheFiles.Load(path);
            if (!ok || entry!.Original() is null)
            {
                change.Created.Add(change.Opened);
            }
            else if (openFiles is not null && openFiles.TryGetValue(path, out var openFile))
            {
                // The file already exists in the program, but the open-file content from
                // didOpen may differ from what was originally read from the source (e.g. the
                // editor normalizes line endings, or the source file changed since the
                // project was loaded). Mark it as Changed so the project rebuilds.
                if (entry.Original() is { } cachedFile && openFile.Hash() != cachedFile.Hash())
                {
                    change.Changed.Add(change.Opened);
                }

                DeleteCacheEntry(entry);
            }
        }

        foreach (var uri in change.Closed.Keys())
        {
            var fileName = uri.FileName();
            if (IsDynamicFileName(fileName))
            {
                continue;
            }

            var path = _toPath(fileName);
            // We may have ignored watcher events while the file was open, so force a reload.
            var fh = GetCachedFile(fileName, path, true /*forceReload*/);
            if (fh is not null)
            {
                if (previousOpenFiles is not null
                    && previousOpenFiles.TryGetValue(path, out var previousOpenFile)
                    && previousOpenFile is not null
                    && fh.Hash() != previousOpenFile.Hash())
                {
                    change.Changed.Add(uri);
                }

                continue;
            }

            change.Deleted.Add(uri);
        }

        return change;
    }
}

/// <summary>Go: <c>type sourceFS struct</c>（snapshotfs.go:791-797）：从 FileSource 取文件并记录已读文件的 vfs.FS。</summary>
internal sealed class SourceFS : FS
{
    internal bool _tracking;

    internal Func<string, Path> _toPath;

    internal SyncSet<Path>? _missingDirectories;

    internal SyncSet<Path>? _seenFiles;

    internal FileSource _source;

    private SourceFS(bool tracking, Func<string, Path> toPath, FileSource source)
    {
        _tracking = tracking;
        _toPath = toPath;
        _source = source;
    }

    /// <summary>Go: <c>func newSourceFS(tracking bool, source FileSource, toPath func(fileName string) tspath.Path) *sourceFS</c>（snapshotfs.go:800-813）。</summary>
    internal static SourceFS NewSourceFS(bool tracking, FileSource source, Func<string, Path> toPath)
    {
        var fs = new SourceFS(tracking, toPath, source);
        if (tracking)
        {
            fs._seenFiles = new SyncSet<Path>();
            fs._missingDirectories = new SyncSet<Path>();
        }

        return fs;
    }

    internal void DisableTracking()
    {
        _tracking = false;
    }

    internal void Track(string fileName)
    {
        if (!_tracking)
        {
            return;
        }

        _seenFiles!.Add(_toPath(fileName));
    }

    internal bool SeenFile(Path path)
    {
        return _seenFiles is not null && _seenFiles.Has(path);
    }

    internal bool SeenFileOrMissingParentDirectory(Path path)
    {
        if (_seenFiles is not null && _seenFiles.Has(path))
        {
            return true;
        }

        if (_missingDirectories is not null && !_missingDirectories.IsEmpty())
        {
            while (true)
            {
                if (_missingDirectories.Has(path))
                {
                    return true;
                }

                var parent = path.GetDirectoryPath();
                if (parent == path)
                {
                    break;
                }

                path = parent;
            }
        }

        return false;
    }

    public FileHandle? GetFile(string fileName)
    {
        Track(fileName);
        return _source.GetFile(fileName);
    }

    public FileHandle? GetFileByPath(string fileName, Path path)
    {
        Track(fileName);
        return _source.GetFileByPath(fileName, path);
    }

    /// <summary>Go: <c>DirectoryExists</c>（snapshotfs.go:830-837）。</summary>
    public bool DirectoryExists(string path)
    {
        var exists = _source.FS().DirectoryExists(path);
        if (!exists && _tracking)
        {
            _missingDirectories!.Add(_toPath(path));
        }

        return exists;
    }

    public bool FileExists(string path)
    {
        Track(path);
        return _source.FileExists(path, _toPath(path));
    }

    public Entries GetAccessibleEntries(string path)
    {
        return _source.GetAccessibleEntries(path);
    }

    public bool TryReadFile(string path, out string contents)
    {
        if (GetFile(path) is { } fh)
        {
            contents = fh.Content();
            return true;
        }

        contents = "";
        return false;
    }

    public string Realpath(string path)
    {
        return _source.FS().Realpath(path);
    }

    public VfsFileInfo? Stat(string path)
    {
        return _source.FS().Stat(path);
    }

    public bool UseCaseSensitiveFileNames()
    {
        return _source.FS().UseCaseSensitiveFileNames();
    }

    public void WriteFile(string path, string data)
    {
        Debug.Fail("unimplemented");
    }

    public void AppendFile(string path, string data)
    {
        Debug.Fail("unimplemented");
    }

    public void Remove(string path)
    {
        Debug.Fail("unimplemented");
    }

    public void Chtimes(string path, DateTime accessTime, DateTime modificationTime)
    {
        Debug.Fail("unimplemented");
    }
}
