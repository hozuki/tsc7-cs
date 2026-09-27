// ported from: internal/vfs/vfstest/vfstest.go（MapFS 部分）
//
// Go 的 MapFS 是架在 <c>testing/fstest.MapFS</c>（stdlib）之上的：只有 Open 被重写，Stat/ReadDir 由 fstest
// 合成、realpath 靠 <c>MapFile.Sys</c> 携带。C# 没有 fstest.MapFS，所以这里直接实现 IIoFs
// （Stat/ReadDir/TryReadFile）+ RealpathFS/WritableFS，语义逐条对齐 Go：
//   - 键是 canonical path（<see cref="TSPath.GetCanonicalFileName"/> 之后），不含前导 '/'；
//   - 中间目录在构造时被显式建出来（Go 的 convertMapFS 调 mkdirAll），所以列目录就是前缀扫描；
//     Go 里由 fstest 合成的根目录 "." 在这里等价成"FS 非空 ⇒ 根存在"；
//   - 符号链接信息放在 <see cref="_symlinks"/>（Go 的 m.symlinks）；每个条目的 realpath（Go 的 sys.realpath）
//     是"请求写入时用的那个路径"，于是 Stat().Name 与 Realpath() 与 Go 一致（不含前导 '/'）。
// 偏离：
//   - <c>MapFile.Sys</c>（Go 用来挂 realpath 的任意值）不移植：C# 侧由 MapEntry 承载同一信息，
//     于是 <c>FileInfo</c> 里也没有 Sys（见 Vfs.cs 文件头）。
//   - fs.FileMode 权限位不移植（<see cref="WritableFS"/> 已经没有 perm 参数）；目录/普通文件/符号链接由
//     IsDir/IsSymlink 两个布尔表达。
//   - 错误消息里的 %q 等价成 C# 的双引号；异常类型详见 Vfs.cs 文件头的哨兵错误映射表。
namespace TypeScript.Vfs.VfsTest;

using TypeScript.TSPath;
using TypeScript.Vfs;
using TypeScript.Vfs.Iovfs;

using static TypeScript.TSPath.TSPath;

/// <summary>Go 的 <c>vfstest.MapFS</c>（内存文件系统）。</summary>
public sealed class MapFS : RealpathFS, WritableFS
{
    // Go 是 sync.RWMutex + 私有助手不重复加锁；C# 侧公共方法用 Monitor，私有助手假定已持锁
    // （ReaderWriterLockSlim 非重入，而这些方法之间会互相调用）。
    private readonly object _mu = new();

    // Go: m fstest.MapFS（key = canonicalPath）。
    private readonly Dictionary<string, MapEntry> _m = [];

    // Go: symlinks map[canonicalPath]canonicalPath。
    private readonly Dictionary<string, string> _symlinks = [];

    private readonly bool _useCaseSensitiveFileNames;
    private readonly Clock _clock;

    internal MapFS(bool useCaseSensitiveFileNames, Clock clock)
    {
        _useCaseSensitiveFileNames = useCaseSensitiveFileNames;
        _clock = clock;
    }

    internal string GetCanonicalPath(string p)
    {
        return GetCanonicalFileName(p, _useCaseSensitiveFileNames);
    }

    /// <summary>Go: <c>setEntry(realpath, canonical, file)</c>。</summary>
    internal void SetEntry(string realpath, string canonical, MapFile file)
    {
        if (realpath.Length == 0 || canonical.Length == 0)
        {
            Debug.Fail("empty path");
        }

        var entry = new MapEntry
        {
            Data = file.Data,
            IsDir = file.IsDir,
            IsSymlink = file.IsSymlink,
            ModTime = file.ModTime,
            Realpath = realpath,
        };
        _m[canonical] = entry;

        if (file.IsSymlink)
        {
            _symlinks[canonical] = GetCanonicalPath(file.Data);
        }
    }

    private DirEntry[]? ReadDirCore(string name)
    {
        string prefix;
        if (name == ".")
        {
            prefix = "";
        }
        else
        {
            var (dirEntry, canonical, err) = GetFollowingSymlinks(GetCanonicalPath(name));
            if (dirEntry == null || err != null || !dirEntry.IsDir)
            {
                return null;
            }

            prefix = canonical + "/";
        }

        var entries = new List<DirEntry>();
        foreach (var (key, entry) in _m)
        {
            if (!key.StartsWith(prefix, StringComparison.Ordinal))
            {
                continue;
            }

            var rest = key[prefix.Length..];
            if (rest.Length == 0 || rest.Contains('/'))
            {
                continue;
            }

            entries.Add(new DirEntry(VfsTest.BaseName(entry.Realpath), entry.IsDir, entry.IsSymlink, ToFileInfo(entry)));
        }

        entries.Sort(static (a, b) => string.CompareOrdinal(a.Name, b.Name));
        return entries.ToArray();
    }

    public DirEntry[]? ReadDir(string name)
    {
        lock (_mu)
        {
            return ReadDirCore(name);
        }
    }

    public FileInfo? Stat(string name)
    {
        lock (_mu)
        {
            if (name == ".")
            {
                // Go 的 fstest.MapFS 会合成根目录；这里等价成"FS 非空 ⇒ 根存在"。
                return _m.Count == 0
                    ? null
                    : new FileInfo { Name = ".", FullName = ".", IsDirectory = true };
            }

            var (entry, _, err) = GetFollowingSymlinks(GetCanonicalPath(name));
            return entry == null || err != null ? null : ToFileInfo(entry);
        }
    }

    public bool TryReadFile(string name, out string contents)
    {
        lock (_mu)
        {
            var (entry, _, err) = GetFollowingSymlinks(GetCanonicalPath(name));
            if (entry == null || err != null || entry.IsDir)
            {
                contents = "";
                return false;
            }

            contents = entry.Data;
            return true;
        }
    }

    /// <summary>Go: <c>MapFS.Realpath(name) (string, error)</c>；失败返回 <c>null</c>。</summary>
    public string? Realpath(string name)
    {
        lock (_mu)
        {
            var (entry, _, err) = GetFollowingSymlinks(GetCanonicalPath(name));
            return entry == null || err != null ? null : entry.Realpath;
        }
    }

    /// <summary>Go: <c>MapFS.AddSymlink(path, target)</c>。</summary>
    public void AddSymlink(string path, string target)
    {
        lock (_mu)
        {
            var canonical = GetCanonicalPath(path);
            SetEntry(path, canonical, new MapFile { Data = target, IsSymlink = true, ModTime = _clock.Now() });
        }
    }

    /// <summary>Go: <c>MapFS.MkdirAll(path, perm) error</c>（perm 见文件头）。</summary>
    public void MkdirAll(string path)
    {
        lock (_mu)
        {
            MkdirAllCore(path);
        }
    }

    public void WriteFile(string path, string data)
    {
        lock (_mu)
        {
            WriteFileCore(path, data);
        }
    }

    public void AppendFile(string path, string data)
    {
        lock (_mu)
        {
            AppendFileCore(path, data);
        }
    }

    public void Remove(string path)
    {
        lock (_mu)
        {
            RemoveCore(path);
        }
    }

    public void Chtimes(string path, DateTime accessTime, DateTime modificationTime)
    {
        lock (_mu)
        {
            var canonical = GetCanonicalPath(path);
            if (!_m.TryGetValue(canonical, out var entry))
            {
                throw new FileNotFoundException("file does not exist");
            }

            entry.ModTime = modificationTime.Kind == DateTimeKind.Utc
                ? modificationTime
                : modificationTime.ToUniversalTime();
        }
    }

    /// <summary>Go: <c>MapFS.GetTargetOfSymlink(path) (string, bool)</c>（§3.2 的 (T,bool) → TryX）。</summary>
    public bool TryGetTargetOfSymlink(string path, out string target)
    {
        var rest = path.StartsWith('/') ? path[1..] : path;
        lock (_mu)
        {
            var canonical = GetCanonicalPath(rest);
            if (_m.TryGetValue(canonical, out var entry) && entry.IsSymlink)
            {
                target = "/" + entry.Data;
                return true;
            }
        }

        target = "";
        return false;
    }

    /// <summary>Go: <c>MapFS.GetModTime(path) time.Time</c>。</summary>
    public DateTime GetModTime(string path)
    {
        var rest = path.StartsWith('/') ? path[1..] : path;
        lock (_mu)
        {
            var canonical = GetCanonicalPath(rest);
            return _m.TryGetValue(canonical, out var entry) ? entry.ModTime : default;
        }
    }

    /// <summary>Go: <c>MapFS.Entries() iter.Seq2[string, *fstest.MapFile]</c>（按路径部件序）。</summary>
    public IEnumerable<(string Path, MapFile File)> Entries()
    {
        lock (_mu)
        {
            var keys = _m.Keys.ToArray();
            Array.Sort(keys, VfsTest.ComparePathsByParts);
            var result = new (string Path, MapFile File)[keys.Length];
            for (var i = 0; i < keys.Length; i++)
            {
                var entry = _m[keys[i]];
                var path = entry.Realpath;
                if (!PathIsAbsolute(path))
                {
                    path = "/" + path;
                }

                result[i] = (path, ToMapFile(entry));
            }

            return result;
        }
    }

    /// <summary>Go: <c>MapFS.GetFileInfo(path) *fstest.MapFile</c>（不跟随符号链接；不存在返回 null）。</summary>
    public MapFile? GetFileInfo(string path)
    {
        var rest = path.StartsWith('/') ? path[1..] : path;
        lock (_mu)
        {
            var canonical = GetCanonicalPath(rest);
            return _m.TryGetValue(canonical, out var entry) ? ToMapFile(entry) : null;
        }
    }

    internal static FileInfo ToFileInfo(MapEntry entry)
    {
        return new FileInfo
        {
            Name = VfsTest.BaseName(entry.Realpath),
            FullName = entry.Realpath,
            IsDirectory = entry.IsDir,
            IsSymlink = entry.IsSymlink,
            Size = entry.IsDir ? 0 : entry.Data.Length,
            ModifiedTimeUtc = entry.ModTime,
        };
    }

    private static MapFile ToMapFile(MapEntry entry)
    {
        return new MapFile
        {
            Data = entry.Data,
            IsDir = entry.IsDir,
            IsSymlink = entry.IsSymlink,
            ModTime = entry.ModTime,
        };
    }

    // Go: getFollowingSymlinks / getFollowingSymlinksWorker。
    private (MapEntry? Entry, string Path, Exception? Error) GetFollowingSymlinks(string p)
    {
        return GetFollowingSymlinksWorker(p, "", "");
    }

    private (MapEntry? Entry, string Path, Exception? Error) GetFollowingSymlinksWorker(string p, string symlinkFrom, string symlinkTo)
    {
        if (_m.TryGetValue(p, out var file) && !file.IsSymlink)
        {
            return (file, p, null);
        }

        if (_symlinks.TryGetValue(p, out var target))
        {
            return GetFollowingSymlinksWorker(target, p, target);
        }

        // 也可能是符号链接目录下面的路径。
        foreach (var (other, linkTarget) in _symlinks)
        {
            if (other.Length < p.Length && p.StartsWith(other, StringComparison.Ordinal) && p[other.Length] == '/')
            {
                return GetFollowingSymlinksWorker(linkTarget + p[other.Length..], other, linkTarget);
            }
        }

        Exception err = symlinkFrom.Length == 0
            ? new FileNotFoundException("file does not exist")
            : new BrokenSymlinkException(symlinkFrom, symlinkTo);
        return (null, p, err);
    }

    private void MkdirAllCore(string p)
    {
        if (p.Length == 0)
        {
            Debug.Fail("empty path");
        }

        // 快速路径：已存在。
        var (existing, _, err) = GetFollowingSymlinks(GetCanonicalPath(p));
        if (err == null && existing != null)
        {
            if (!existing.IsDir)
            {
                throw new IOException($"mkdir \"{p}\": path exists but is not a directory");
            }

            return;
        }

        var toCreate = new List<string>();
        var offset = 0;
        while (true)
        {
            var (dir, rest) = SplitPathAt(p, offset);
            var canonical = GetCanonicalPath(dir);
            var (other, otherPath, statErr) = GetFollowingSymlinks(canonical);
            if (statErr != null)
            {
                if (statErr is not FileNotFoundException)
                {
                    throw statErr;
                }

                toCreate.Add(dir);
            }
            else
            {
                if (other == null || !other.IsDir)
                {
                    throw new IOException($"mkdir \"{otherPath}\": path exists but is not a directory");
                }

                if (canonical != otherPath)
                {
                    // 有符号链接父级：重设 p 并重来。
                    p = other.Realpath + "/" + rest;
                    toCreate.Clear();
                    offset = 0;
                    continue;
                }
            }

            if (rest.Length == 0)
            {
                break;
            }

            offset = dir.Length + 1;
        }

        foreach (var dir in toCreate)
        {
            SetEntry(dir, GetCanonicalPath(dir), new MapFile { IsDir = true, ModTime = _clock.Now() });
        }
    }

    private void WriteFileCore(string path, string data)
    {
        CheckParentDirectory(path, "write");
        var (file, canonical, err) = GetFollowingSymlinks(GetCanonicalPath(path));
        if (err != null)
        {
            // Go: 只有 ErrNotExist 与 brokenSymlink 可以走到这里。
            if (err is not FileNotFoundException and not BrokenSymlinkException)
            {
                Debug.Fail(err.Message);
            }
        }
        else if (file == null || file.IsDir)
        {
            throw new IOException($"write \"{path}\": path exists but is not a regular file");
        }

        SetEntry(path, canonical, new MapFile { Data = data, ModTime = _clock.Now() });
    }

    private void AppendFileCore(string path, string data)
    {
        CheckParentDirectory(path, "append");

        var existingData = "";
        var (file, canonical, err) = GetFollowingSymlinks(GetCanonicalPath(path));
        if (err != null)
        {
            if (err is not FileNotFoundException and not BrokenSymlinkException)
            {
                Debug.Fail(err.Message);
            }
        }
        else if (file == null || file.IsDir)
        {
            throw new IOException($"append \"{path}\": path exists but is not a regular file");
        }
        else
        {
            existingData = file.Data;
        }

        SetEntry(path, canonical, new MapFile { Data = existingData + data, ModTime = _clock.Now() });
    }

    private void CheckParentDirectory(string path, string operation)
    {
        var parent = VfsTest.DirName(path);
        if (parent.Length == 0)
        {
            return;
        }

        var canonical = GetCanonicalPath(parent);
        var (parentFile, _, err) = GetFollowingSymlinks(canonical);
        if (err != null)
        {
            throw new IOException($"{operation} \"{path}\": {err.Message}");
        }

        if (parentFile == null || !parentFile.IsDir)
        {
            throw new IOException($"{operation} \"{path}\": parent path exists but is not a directory");
        }
    }

    private void RemoveCore(string path)
    {
        var canonical = GetCanonicalPath(path);
        if (!_m.TryGetValue(canonical, out var fileInfo))
        {
            // 文件不存在
            return;
        }

        _m.Remove(canonical);
        _symlinks.Remove(canonical);

        if (!fileInfo.IsDir)
        {
            return;
        }

        var prefix = canonical + "/";
        var toRemove = new List<string>();
        foreach (var key in _m.Keys)
        {
            if (key.StartsWith(prefix, StringComparison.Ordinal))
            {
                toRemove.Add(key);
            }
        }

        foreach (var key in toRemove)
        {
            _m.Remove(key);
            _symlinks.Remove(key);
        }
    }

    /// <summary>Go: <c>splitPath(s, offset) (before, after)</c>。</summary>
    private static (string Before, string After) SplitPathAt(string s, int offset)
    {
        var idx = s.AsSpan(offset).IndexOf('/');
        return idx < 0
            ? (s, "")
            : (s[..(idx + offset)], s[(idx + 1 + offset)..]);
    }

    /// <summary>Go 的 <c>sys{original, realpath}</c> 在 C# 侧的落点（MapEntry 取代 fstest.MapFile + Sys）。</summary>
    internal sealed class MapEntry
    {
        public string Data = "";
        public bool IsDir;
        public bool IsSymlink;
        public DateTime ModTime;
        public string Realpath = "";
    }
}
