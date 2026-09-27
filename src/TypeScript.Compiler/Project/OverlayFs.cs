// ported from: tsc/internal/project/overlayfs.go:1-589
//
// 与 Go 的偏离（逐条说明）：
//
// 1) **Go 的内嵌 `fileBase` → C# 继承**：Go 的 `cachedFile`/`Overlay` 内嵌 `fileBase`（值），
//    `fileName`/`content`/`hash` 与方法被提升。C# 没有内嵌，最贴近的是继承 `FileBase`。
//    ⚠ 代价：`overlayfs.go` 的 `fileBase` 是**未导出**的，而 `Overlay` 是导出的；C# 规定基类可
//    见性不能低于派生类（CS0060），所以 `FileBase` 只能是 `public`（凭空多一个公开类型）。
//    另一种做法（dirty 包用的）是把字段内联进两个派生类 —— 但这里 `fileBase` 承载
//    `sync.Once` 的惰性 line map 逻辑，内联要复制两份，故取继承。`cachedFile` 仍是 `internal`
//    （Go 未导出），公开面只有 `FileBase` + `Overlay` + `NewCachedFileHandle`。
//
// 2) `sync.Once` → **标志 + Lock 双重检查**（本仓约定，见 context 的映射表）；`lineMapOnce`/
//    `lineInfoOnce` 各一个 `System.Threading.Lock`。读路径先用 `Volatile.Read` 快速返回。
//
// 3) Go 的 `func (f *fileBase) LSPLineMap() *lsconv.LSPLineMap` 返回指针 → `LSPLineMap?`
//    （`*T`→`T?`）。实现永不返回 null（惰性初始化后才返回），但签名保持 Go 的可空形状。
//
// 4) `xxh3.Uint128` → `System.UInt128`；`xxh3.HashString128(string)` → `HashString128(string)`（已落地）。
//
// 5) Go 的 `lsconv`/`sourcemap` 包级函数在 C# 落在**与本命名空间同名的静态类**上（本仓落地物的实际形状）：
//    `lsconv.ComputeLSPLineStarts`/`lsconv.LanguageKindToScriptKind`/`lsconv.NewConverters` →
//    `TypeScript.LsConv.LsConv` 的静态成员；`sourcemap.CreateECMALineInfo` →
//    `TypeScript.SourceMap.SourceMap.CreateECMALineInfo`；`core.ComputeECMALineStarts` →
//    `TypeScript.Core.Core.ComputeECMALineStarts`；`xxh3.HashString128` → `TypeScript.Xxh3.Xxh3.HashString128`。
//    ⚠ 这些静态类都与同名**命名空间**同名，在 `TypeScript.*` 子命名空间里裸写 `LsConv.X`/`SourceMap.X`/`Xxh3.X`
//    会解析到命名空间（CS0234）——所以本文件用 `using static ...;` 后**只写裸成员名**。
//
// 6) `vfs.FileInfo` 在 C# 是**数据类**（`TypeScript.Vfs.FileInfo`：Name/FullName/IsDirectory/IsSymlink/
//    Size/ModifiedTimeUtc），不是接口 ⇒ Go 的 `overlayFileInfo`/`overlayDirectoryInfo` 两个类型
//    不需要移植，`Stat` 直接构造 `Vfs.FileInfo`。相应字段的映射：
//      - `Mode() 0o444`（Go 的只读普通文件位）在 C# 侧无对应字段（Vfs.FileInfo 只拆出
//        IsDirectory/IsSymlink 两个布尔），**丢弃**；
//      - `Mode() iofs.ModeDir|0o555` → `IsDirectory = true`；
//      - `ModTime() time.Time{}` → `ModifiedTimeUtc = default(DateTime)`（Go 的零值时间）；
//      - `Sys() any` → 无对应物（Vfs.FileInfo 没有 Sys），丢弃；
//      - `FullName` 是 `os.FileInfo` 才有的字段（`fs.FileInfo` 没有，Go 侧这两个 info 类型也没有），
//        这里按本仓 MapFS 的惯例填**虚拟路径**（overlay 文件填 `overlay.FileName()`，虚拟目录填
//        `Stat` 收到的 path），仅作占位、无 Go 语义。
//
// 7) `vfs.FS.ReadFile(path) (string, bool)` → `TryReadFile(path, out string contents)`（已落地接口）。
//
// 8) Go 的 `overlayFS.mu sync.RWMutex` → `System.Threading.Lock`（本仓 sync.Mutex 的统一替身）。
//    只是并发度差异（读不再并行），可观察行为一致；`ReaderWriterLockSlim` 需要 Dispose、且本仓
//    已有先例偏重 `Lock`，故不引入。
//
// 9) `layeredFS.Overlays()` 在 Go 里是在 `defer fs.mu.RUnlock()` 下**把内部 map 直接交出去**，
//    调用方在锁外遍历；C# 逐句照搬（返回内部 `Dictionary` 引用）。行为（含潜在的并发遍历风险）
//    与 Go 一致。
//
// 10) `processChanges` 里函数内声明的局部类型 `fileEvents` → C# 不允许局部类型，落成
//     `OverlayFS` 的 **private 嵌套类 `FileEvents`**（字段按本仓规则 `_camelCase`）。Go 的
//     `[]*FileChange`/`*FileChange` → `List<FileChange>`/`FileChange`（C# 的 FileChange 是类，引用语义）。
//
// 11) `panic(...)` → `Debug.Fail(...)`（`TypeScript.Debug`，抛 DebugFailureException）。
//
// 12) `vfs.Entries` 是 struct，`Files`/`Directories` 在 C# 是 `string[]`（非空数组，Go 的 nil 切片
//     → 空数组）；`Symlinks` 是 `HashSet<string>?`（null 表示"该 FS 不提供符号链接信息"，与 Go 的
//     nil map 同义）。Go 的 `slices.Clone`/`maps.Clone` 分别落成 `List<string>` 拷贝/`HashSet` 拷贝。
//
// 13) Go 的 `maps.Clone(directory)`（`overlayDirectories` 的 value 是 `map[tspath.Path]string`）→
//     `new Dictionary<Path, string>(directory)`。
//
// 14) 空值检查：Go 用 `x != nil`/`uri == ""`，C# 用 `is not null`/`== default`（DocumentUri 是
//     readonly record struct，`default` 即 Go 的空串）。
using TypeScript.Core;
using TypeScript.LsConv;
using TypeScript.LsProto;
using TypeScript.SourceMap;
using TypeScript.SpanMap;
using TypeScript.TSPath;
using TypeScript.Vfs;
using static TypeScript.Core.Core;
using static TypeScript.LsConv.LsConv;
using static TypeScript.SourceMap.SourceMap;
using static TypeScript.TSPath.TSPath;
using static TypeScript.Xxh3.Xxh3;
using Path = TypeScript.TSPath.Path;
using SpanMapType = TypeScript.SpanMap.SpanMap;
using VfsFileInfo = TypeScript.Vfs.FileInfo;

namespace TypeScript.Project;

/// <summary>Go: <c>type FileContent interface</c>（overlayfs.go:22-25）。</summary>
public interface FileContent
{
    /// <summary>Go: <c>Content()</c>。</summary>
    string Content();

    /// <summary>Go: <c>Hash() xxh3.Uint128</c> → <see cref="UInt128"/>。</summary>
    UInt128 Hash();
}

/// <summary>Go: <c>type FileHandle interface</c>（overlayfs.go:27-37）。</summary>
public interface FileHandle : FileContent
{
    string FileName();

    int Version();

    bool MatchesDiskText();

    bool IsOverlay();

    LSPLineMap? LSPLineMap();

    ECMALineInfo? ECMALineInfo();

    ScriptKind Kind();
}

/// <summary>Go: <c>type fileBase struct</c>（overlayfs.go:39-49，见文件头第 1 条）。</summary>
public class FileBase
{
    internal string _fileName = "";

    internal string _content = "";

    internal UInt128 _hash;

    // Go: lineMapOnce sync.Once + lineMap *lsconv.LSPLineMap（见文件头第 2 条）。
    private readonly Lock _lineMapMu = new();

    private bool _lineMapDone;

    private LSPLineMap? _lineMap;

    // Go: lineInfoOnce sync.Once + lineInfo *sourcemap.ECMALineInfo。
    private readonly Lock _lineInfoMu = new();

    private bool _lineInfoDone;

    private ECMALineInfo? _lineInfo;

    public string FileName()
    {
        return _fileName;
    }

    public UInt128 Hash()
    {
        return _hash;
    }

    public string Content()
    {
        return _content;
    }

    public LSPLineMap? LSPLineMap()
    {
        if (Volatile.Read(ref _lineMapDone))
        {
            return _lineMap;
        }

        lock (_lineMapMu)
        {
            if (!_lineMapDone)
            {
                _lineMap = ComputeLSPLineStarts(_content);
                Volatile.Write(ref _lineMapDone, true);
            }

            return _lineMap;
        }
    }

    public ECMALineInfo? ECMALineInfo()
    {
        if (Volatile.Read(ref _lineInfoDone))
        {
            return _lineInfo;
        }

        lock (_lineInfoMu)
        {
            if (!_lineInfoDone)
            {
                var lineStarts = ComputeECMALineStarts(_content);
                _lineInfo = CreateECMALineInfo(_content, lineStarts);
                Volatile.Write(ref _lineInfoDone, true);
            }

            return _lineInfo;
        }
    }
}

/// <summary>Go: <c>type cachedFile struct</c>（overlayfs.go:79-83）。</summary>
internal sealed class CachedFile : FileBase, FileHandle, Dirty.Cloneable<CachedFile>
{
    internal bool _needsReload;

    internal Path _realpathPath;

    /// <summary>Go 的结构体字面量 <c>&amp;cachedFile{fileName: fileName, needsReload: true}</c> 用（不设 hash）。</summary>
    internal CachedFile()
    {
    }

    internal CachedFile(string fileName, string content)
    {
        _fileName = fileName;
        _content = content;
        _hash = HashString128(content);
    }

    /// <summary>Go: <c>func newCachedFile(fileName string, content string) *cachedFile</c>（同 package 调用点用）。</summary>
    internal static CachedFile NewCachedFile(string fileName, string content)
    {
        return new CachedFile(fileName, content);
    }

    /// <summary>Go: <c>func NewCachedFileHandle(fileName string, content string) FileHandle</c>（overlayfs.go:95-97）。</summary>
    public static FileHandle NewCachedFileHandle(string fileName, string content)
    {
        return new CachedFile(fileName, content);
    }

    public int Version()
    {
        return 0;
    }

    public bool MatchesDiskText()
    {
        return !_needsReload;
    }

    public bool IsOverlay()
    {
        return false;
    }

    public ScriptKind Kind()
    {
        return GetScriptKindFromFileName(_fileName);
    }

    public CachedFile Clone()
    {
        // Go 只复制 realpathPath/fileName/content/hash——**不**复制 needsReload（新值是零值 false），
        // 也不复制惰性 line map（新对象重新计算）。
        return new CachedFile
        {
            _realpathPath = _realpathPath,
            _fileName = _fileName,
            _content = _content,
            _hash = _hash,
        };
    }
}

/// <summary>Go: <c>type Overlay struct</c>（overlayfs.go:106-111）。</summary>
public sealed class Overlay : FileBase, FileHandle, Script
{
    internal int _version;

    internal ScriptKind _kind;

    internal bool _matchesDiskText;

    /// <summary>Go 的结构体字面量构造（测试里 <c>&amp;Overlay{fileBase: fileBase{...}}</c>）用。</summary>
    internal Overlay()
    {
    }

    /// <summary>Go: <c>func newOverlay(fileName string, content string, version int32, kind core.ScriptKind) *Overlay</c>。</summary>
    internal static Overlay NewOverlay(string fileName, string content, int version, ScriptKind kind)
    {
        return new Overlay
        {
            _fileName = fileName,
            _content = content,
            _hash = HashString128(content),
            _version = version,
            _kind = kind,
        };
    }

    public int Version()
    {
        return _version;
    }

    /// <summary>Go: <c>Text()</c>（同时满足 <see cref="Script"/>）。</summary>
    public string Text()
    {
        return _content;
    }

    public string OriginalFileName()
    {
        return FileName();
    }

    /// <summary>Go: <c>SpanMap() *spanmap.SpanMap</c>：overlay 永远没有 span map（注释见 Go 源码）。</summary>
    public SpanMapType? SpanMap()
    {
        return null;
    }

    /// <summary>Go: <c>OriginalText()</c>。</summary>
    public string OriginalText()
    {
        return _content;
    }

    /// <summary>Go: <c>MatchesDiskText()</c>（可能假阴性，但绝不假阳性）。</summary>
    public bool MatchesDiskText()
    {
        return _matchesDiskText;
    }

    /// <summary>Go: <c>func (o *Overlay) computeMatchesDiskText(fs vfs.FS) (matchesDiskText bool, exists bool)</c>
    /// （overlayfs.go:151-161）。Go 的 <c>(T, bool)</c> → 本仓的具名元组。</summary>
    internal (bool MatchesDiskText, bool Exists) ComputeMatchesDiskText(FS fs)
    {
        if (IsDynamicFileName(_fileName))
        {
            return (false, false);
        }

        if (!fs.TryReadFile(_fileName, out var diskContent))
        {
            return (false, false);
        }

        return (HashString128(diskContent) == _hash, true);
    }

    public bool IsOverlay()
    {
        return true;
    }

    public ScriptKind Kind()
    {
        return _kind;
    }
}

/// <summary>Go: <c>type overlayFS struct</c>（overlayfs.go:163-171）。</summary>
internal sealed class OverlayFS : LayeredFileSystem
{
    internal Func<string, Path> _toPath;

    internal FS _host;

    internal PositionEncodingKind _positionEncoding;

    private readonly Lock _mu = new();

    internal Dictionary<Path, Overlay> _overlays;

    internal Dictionary<Path, Dictionary<Path, string>> _overlayDirectories;

    private OverlayFS(
        Func<string, Path> toPath,
        FS host,
        PositionEncodingKind positionEncoding,
        Dictionary<Path, Overlay> overlays,
        Dictionary<Path, Dictionary<Path, string>> overlayDirectories)
    {
        _toPath = toPath;
        _host = host;
        _positionEncoding = positionEncoding;
        _overlays = overlays;
        _overlayDirectories = overlayDirectories;
    }

    /// <summary>Go: <c>func newOverlayFS(fs vfs.FS, overlays map[tspath.Path]*Overlay, positionEncoding lsproto.PositionEncodingKind, toPath func(string) tspath.Path) *overlayFS</c>（overlayfs.go:204-212）。</summary>
    internal static OverlayFS NewOverlayFS(
        FS fs,
        Dictionary<Path, Overlay> overlays,
        PositionEncodingKind positionEncoding,
        Func<string, Path> toPath)
    {
        return new OverlayFS(toPath, fs, positionEncoding, overlays, CreateOverlayDirectories(overlays));
    }

    public Dictionary<Path, Overlay> Overlays()
    {
        lock (_mu)
        {
            return _overlays;
        }
    }

    /// <summary>Go: <c>func layerOverlayFileSystem(...) LayeredFileSystem</c>（overlayfs.go:226-238）。</summary>
    internal static LayeredFileSystem LayerOverlayFileSystem(
        FS fileSystem,
        Dictionary<Path, Overlay> overlays,
        PositionEncodingKind positionEncoding,
        Func<string, Path> toPath)
    {
        var baseFs = fileSystem;
        RebasableFileSystem? layer = null;
        if (fileSystem is RebasableFileSystem candidate)
        {
            layer = candidate;
            baseFs = candidate.BaseFileSystem();
        }

        if (baseFs is OverlayFS previous)
        {
            baseFs = previous._host;
        }

        var overlay = NewOverlayFS(baseFs, overlays, positionEncoding, toPath);
        if (layer is null)
        {
            return overlay;
        }

        return layer.WithBaseFileSystem(overlay);
    }

    public FileHandle? GetFile(string fileName)
    {
        return GetFileByPath(fileName, _toPath(fileName));
    }

    public FileHandle? GetFileByPath(string fileName, Path path)
    {
        Overlay? overlay;
        bool directory;
        lock (_mu)
        {
            _overlays.TryGetValue(path, out overlay);
            directory = _overlayDirectories.ContainsKey(path);
        }

        if (overlay is not null)
        {
            return overlay;
        }

        if (directory)
        {
            return null;
        }

        if (_host is FileHandleSource source)
        {
            return source.GetFileByPath(fileName, path);
        }

        if (!_host.TryReadFile(fileName, out var content))
        {
            return null;
        }

        return CachedFile.NewCachedFile(fileName, content);
    }

    public bool UseCaseSensitiveFileNames()
    {
        return _host.UseCaseSensitiveFileNames();
    }

    public bool FileExists(string fileName)
    {
        bool file;
        bool directory;
        lock (_mu)
        {
            var path = _toPath(fileName);
            file = _overlays.ContainsKey(path);
            directory = _overlayDirectories.ContainsKey(path);
        }

        return file || (!directory && _host.FileExists(fileName));
    }

    public bool TryReadFile(string fileName, out string contents)
    {
        if (GetFile(fileName) is { } file)
        {
            contents = file.Content();
            return true;
        }

        contents = "";
        return false;
    }

    public void WriteFile(string path, string data)
    {
        _host.WriteFile(path, data);
    }

    public void AppendFile(string path, string data)
    {
        _host.AppendFile(path, data);
    }

    public void Remove(string path)
    {
        _host.Remove(path);
    }

    public void Chtimes(string path, DateTime accessTime, DateTime modificationTime)
    {
        _host.Chtimes(path, accessTime, modificationTime);
    }

    public bool DirectoryExists(string directoryName)
    {
        bool file;
        bool directory;
        lock (_mu)
        {
            var path = _toPath(directoryName);
            file = _overlays.ContainsKey(path);
            directory = _overlayDirectories.ContainsKey(path);
        }

        return directory || (!file && _host.DirectoryExists(directoryName));
    }

    public Entries GetAccessibleEntries(string directoryName)
    {
        Dictionary<Path, string>? directory;
        bool file;
        Dictionary<Path, Overlay> overlays;
        lock (_mu)
        {
            var path = _toPath(directoryName);
            file = _overlays.ContainsKey(path);
            directory = _overlayDirectories.TryGetValue(path, out var found) ? found : null;
            if (directory is not null)
            {
                directory = new Dictionary<Path, string>(directory);
            }

            overlays = _overlays;
        }

        if (file)
        {
            return new Entries([], [], null);
        }

        var hostEntries = _host.GetAccessibleEntries(directoryName);
        var files = new List<string>(hostEntries.Files);
        var directories = new List<string>(hostEntries.Directories);
        HashSet<string>? symlinks = hostEntries.Symlinks is null
            ? null
            : new HashSet<string>(hostEntries.Symlinks, hostEntries.Symlinks.Comparer);
        bool EqualName(string left, string right) =>
            GetCanonicalFileName(left, UseCaseSensitiveFileNames()) == GetCanonicalFileName(right, UseCaseSensitiveFileNames());

        if (directory is not null)
        {
            foreach (var (childPath, childName) in directory)
            {
                files.RemoveAll(name => EqualName(name, childName));
                directories.RemoveAll(name => EqualName(name, childName));
                if (symlinks is not null)
                {
                    symlinks.RemoveWhere(name => EqualName(name, childName));
                }

                if (overlays.ContainsKey(childPath))
                {
                    files.Add(childName);
                }
                else
                {
                    directories.Add(childName);
                }
            }
        }

        return new Entries([.. files], [.. directories], symlinks);
    }

    public VfsFileInfo? Stat(string path)
    {
        Overlay? overlay;
        bool directory;
        lock (_mu)
        {
            var canonicalPath = _toPath(path);
            _overlays.TryGetValue(canonicalPath, out overlay);
            directory = _overlayDirectories.ContainsKey(canonicalPath);
        }

        if (overlay is not null)
        {
            return new VfsFileInfo
            {
                Name = GetBaseFileName(overlay.FileName()),
                FullName = overlay.FileName(),
                IsDirectory = false,
                IsSymlink = false,
                Size = overlay.Content().Length,
                ModifiedTimeUtc = default,
            };
        }

        if (directory)
        {
            return new VfsFileInfo
            {
                Name = GetBaseFileName(path),
                FullName = path,
                IsDirectory = true,
                IsSymlink = false,
                Size = 0,
                ModifiedTimeUtc = default,
            };
        }

        return _host.Stat(path);
    }

    public string Realpath(string path)
    {
        return _host.Realpath(path);
    }

    /// <summary>Go: <c>func createOverlayDirectories(overlays map[tspath.Path]*Overlay) map[tspath.Path]map[tspath.Path]string</c>（overlayfs.go:398-420）。</summary>
    internal static Dictionary<Path, Dictionary<Path, string>> CreateOverlayDirectories(Dictionary<Path, Overlay> overlays)
    {
        var overlayDirectories = new Dictionary<Path, Dictionary<Path, string>>();
        foreach (var (path, overlay) in overlays)
        {
            var childPath = path;
            var child = overlay.FileName();
            while (true)
            {
                var parentPath = childPath.GetDirectoryPath();
                var parent = GetDirectoryPath(child);
                if (childPath == parentPath)
                {
                    break;
                }

                if (overlayDirectories.TryGetValue(parentPath, out var directory))
                {
                    directory[childPath] = GetBaseFileName(child);
                }
                else
                {
                    overlayDirectories[parentPath] = new Dictionary<Path, string> { [childPath] = GetBaseFileName(child) };
                }

                childPath = parentPath;
                child = parent;
            }
        }

        return overlayDirectories;
    }

    /// <summary>Go 的 <c>processChanges</c> 里函数内声明的 <c>fileEvents</c>（见文件头第 10 条）。</summary>
    private sealed class FileEvents
    {
        internal FileChange? _openChange;

        internal FileChange? _closeChange;

        internal bool _watchChanged;

        internal List<FileChange> _changes = [];

        internal bool _saved;

        internal bool _created;

        internal bool _deleted;
    }

    /// <summary>Go: <c>func (fs *overlayFS) processChanges(changes []FileChange) (FileChangeSummary, map[tspath.Path]*Overlay)</c>（overlayfs.go:422-589）。</summary>
    internal (FileChangeSummary Result, Dictionary<Path, Overlay> Overlays) ProcessChanges(FileChange[] changes)
    {
        lock (_mu)
        {
            var result = new FileChangeSummary();
            var newOverlays = new Dictionary<Path, Overlay>(_overlays);
            var fileEventMap = new Dictionary<DocumentUri, FileEvents>();

            foreach (var change in changes)
            {
                var uri = change.URI;
                if (fileEventMap.TryGetValue(uri, out var existing))
                {
                    if (existing._openChange is not null)
                    {
                        Debug.Fail("should see no changes after open");
                    }
                }
                else
                {
                    existing = new FileEvents();
                    fileEventMap[uri] = existing;
                }

                var events = existing;

                if (!result.IncludesWatchChangeOutsideNodeModules
                    && change.Kind.IsWatchKind()
                    && !uri.Value.Contains("/node_modules/", StringComparison.Ordinal))
                {
                    result.IncludesWatchChangeOutsideNodeModules = true;
                }

                switch (change.Kind)
                {
                    case FileChangeKind.Open:
                        events._closeChange = null;
                        events._openChange = change;
                        events._watchChanged = false;
                        events._changes = [];
                        events._saved = false;
                        events._created = false;
                        events._deleted = false;
                        break;
                    case FileChangeKind.Close:
                        events._closeChange = change;
                        events._changes = [];
                        events._saved = false;
                        events._watchChanged = false;
                        break;
                    case FileChangeKind.Change:
                        if (events._closeChange is not null)
                        {
                            Debug.Fail("should see no changes after close");
                        }

                        events._changes.Add(change);
                        events._saved = false;
                        events._watchChanged = false;
                        break;
                    case FileChangeKind.Save:
                        events._saved = true;
                        break;
                    case FileChangeKind.WatchCreate:
                        if (events._deleted)
                        {
                            // Delete followed by create becomes a change
                            events._deleted = false;
                            events._watchChanged = true;
                        }
                        else
                        {
                            events._created = true;
                        }

                        break;
                    case FileChangeKind.WatchChange:
                        if (!events._created)
                        {
                            events._watchChanged = true;
                            events._saved = false;
                        }

                        break;
                    case FileChangeKind.WatchDelete:
                        events._watchChanged = false;
                        events._saved = false;
                        // Delete after create cancels out
                        if (events._created)
                        {
                            events._created = false;
                        }
                        else
                        {
                            events._deleted = true;
                        }

                        break;
                }
            }

            // Process deduplicated events per file
            foreach (var (uri, events) in fileEventMap)
            {
                var path = uri.Path(_host.UseCaseSensitiveFileNames());
                _overlays.TryGetValue(path, out var o);

                if (events._openChange is { } openChange)
                {
                    if (result.Opened != default || result.Reopened != default)
                    {
                        Debug.Fail("can only process one file open event at a time");
                    }

                    if (o is not null && o.Content() != openChange.Content)
                    {
                        result.Changed.Add(uri);
                    }
                    else if (o is null)
                    {
                        result.Opened = uri;
                    }
                    else
                    {
                        result.Reopened = uri;
                    }

                    var scriptKind = LanguageKindToScriptKind(openChange.LanguageKind);
                    if (scriptKind == ScriptKind.Unknown)
                    {
                        scriptKind = GetScriptKindFromFileName(uri.FileName());
                    }

                    o = Overlay.NewOverlay(uri.FileName(), openChange.Content, openChange.Version, scriptKind);
                    newOverlays[path] = o;
                    continue;
                }

                if (events._closeChange is not null && o is not null)
                {
                    result.Closed.Add(uri);
                    newOverlays.Remove(path);
                    o = null;
                }

                if (events._watchChanged)
                {
                    if (o is null)
                    {
                        result.Changed.Add(uri);
                    }
                    else if (!events._saved)
                    {
                        var (matchesDiskText, _) = o.ComputeMatchesDiskText(_host);
                        if (matchesDiskText != o.MatchesDiskText())
                        {
                            o = Overlay.NewOverlay(o.FileName(), o.Content(), o.Version(), o._kind);
                            o._matchesDiskText = matchesDiskText;
                            newOverlays[path] = o;
                        }
                    }
                }

                if (events._changes.Count > 0 && o is not null)
                {
                    result.Changed.Add(uri);
                    foreach (var change in events._changes)
                    {
                        var converters = NewConverters(_positionEncoding, _ => o!.LSPLineMap());
                        foreach (var textChange in change.Changes ?? [])
                        {
                            if (textChange.Partial is { } partialChange)
                            {
                                var ranges = converters.FromLSPRange(o, partialChange.Range, Feature.All);
                                Debug.Assert(ranges.Length == 1, "expected exactly one range for partial change");
                                var coreChange = new TextChange(ranges[0].Span, partialChange.Text);
                                var newContent = coreChange.ApplyTo(o._content);
                                o = Overlay.NewOverlay(o._fileName, newContent, change.Version, o._kind);
                            }
                            else if (textChange.WholeDocument is { } wholeChange)
                            {
                                o = Overlay.NewOverlay(o._fileName, wholeChange.Text, change.Version, o._kind);
                            }
                        }

                        if (change.Changes is { Length: > 0 })
                        {
                            o._version = change.Version;
                            o._hash = HashString128(o._content);
                            o._matchesDiskText = false;
                            newOverlays[path] = o;
                        }
                    }
                }

                if (events._saved)
                {
                    if (o is not null)
                    {
                        o = Overlay.NewOverlay(o.FileName(), o.Content(), o.Version(), o._kind);
                        o._matchesDiskText = true;
                        newOverlays[path] = o;
                    }
                    else if (!events._watchChanged)
                    {
                        // File was saved but never opened via didOpen; treat as a disk change.
                        result.Changed.Add(uri);
                    }
                }

                if (events._created && o is null)
                {
                    result.Created.Add(uri);
                }

                if (events._deleted && o is null)
                {
                    result.Deleted.Add(uri);
                }
            }

            _overlays = newOverlays;
            _overlayDirectories = CreateOverlayDirectories(newOverlays);
            return (result, newOverlays);
        }
    }
}

/// <summary>Go: <c>type LayeredFileSystem interface</c>（overlayfs.go:173-177）。</summary>
public interface LayeredFileSystem : FS, FileHandleSource
{
    Dictionary<Path, Overlay> Overlays();
}

/// <summary>Go: <c>type RebasableFileSystem interface</c>（overlayfs.go:179-183）。</summary>
public interface RebasableFileSystem : FS
{
    FS BaseFileSystem();

    LayeredFileSystem WithBaseFileSystem(FS @base);
}
