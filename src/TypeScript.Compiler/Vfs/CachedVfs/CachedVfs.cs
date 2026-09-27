// ported from: internal/vfs/cachedvfs/cachedvfs.go
//
// 偏离：
//   - 类型名 <c>FS</c> 与 <c>TypeScript.Vfs.FS</c>（接口）同名（Go 里是 <c>cachedvfs.FS</c> vs <c>vfs.FS</c>）：
//     本命名空间内的裸 <c>FS</c> 指本类，接口一律写 <c>TypeScript.Vfs.FS</c>（读起来与 Go 的 <c>vfs.FS</c> 一致）。
//   - Go 的 <c>atomic.Bool</c> → <c>Interlocked</c> 操作的 int 标志（<c>volatile</c> 无法做 CompareAndSwap）。
//   - Go 的包级函数 <c>From</c> 落在同名静态类 <c>CachedVfs</c> 上（docs/architecture.md §2）。
namespace TypeScript.Vfs.CachedVfs;

using TypeScript.Collections;
using TypeScript.Vfs;

/// <summary>Go 的 <c>cachedvfs</c> 包级函数。</summary>
public static partial class CachedVfs
{
    /// <summary>Go: <c>From(fs vfs.FS) *FS</c>。</summary>
    public static FS From(TypeScript.Vfs.FS fs)
    {
        var fsys = new FS(fs);
        fsys.Enable();
        return fsys;
    }
}

/// <summary>Go 的 <c>cachedvfs.FS</c>：给 FS 的读操作加缓存。</summary>
public sealed class FS : TypeScript.Vfs.FS
{
    private readonly TypeScript.Vfs.FS _fs;
    private int _enabled; // Go: atomic.Bool

    private readonly SyncMap<string, bool> _directoryExistsCache = new();
    private readonly SyncMap<string, bool> _fileExistsCache = new();
    private readonly SyncMap<string, Entries> _getAccessibleEntriesCache = new();
    private readonly SyncMap<string, string> _realpathCache = new();
    private readonly SyncMap<string, TypeScript.Vfs.FileInfo?> _statCache = new();

    internal FS(TypeScript.Vfs.FS fs)
    {
        _fs = fs;
    }

    private bool Enabled => Volatile.Read(ref _enabled) != 0;

    /// <summary>Go: <c>DisableAndClearCache</c>。</summary>
    public void DisableAndClearCache()
    {
        if (Interlocked.CompareExchange(ref _enabled, 0, 1) == 1)
        {
            ClearCache();
        }
    }

    /// <summary>Go: <c>Enable</c>。</summary>
    public void Enable()
    {
        Volatile.Write(ref _enabled, 1);
    }

    /// <summary>Go: <c>ClearCache</c>。</summary>
    public void ClearCache()
    {
        _directoryExistsCache.Clear();
        _fileExistsCache.Clear();
        _getAccessibleEntriesCache.Clear();
        _realpathCache.Clear();
        _statCache.Clear();
    }

    public bool DirectoryExists(string path)
    {
        if (Enabled)
        {
            var (ret, ok) = _directoryExistsCache.Load(path);
            if (ok)
            {
                return ret;
            }
        }

        var result = _fs.DirectoryExists(path);

        if (Enabled)
        {
            _directoryExistsCache.Store(path, result);
        }

        return result;
    }

    public bool FileExists(string path)
    {
        if (Enabled)
        {
            var (ret, ok) = _fileExistsCache.Load(path);
            if (ok)
            {
                return ret;
            }
        }

        var result = _fs.FileExists(path);

        if (Enabled)
        {
            _fileExistsCache.Store(path, result);
        }

        return result;
    }

    public Entries GetAccessibleEntries(string path)
    {
        if (Enabled)
        {
            var (ret, ok) = _getAccessibleEntriesCache.Load(path);
            if (ok)
            {
                return ret;
            }
        }

        var result = _fs.GetAccessibleEntries(path);

        if (Enabled)
        {
            _getAccessibleEntriesCache.Store(path, result);
        }

        return result;
    }

    public bool TryReadFile(string path, out string contents)
    {
        return _fs.TryReadFile(path, out contents);
    }

    public string Realpath(string path)
    {
        if (Enabled)
        {
            var (ret, ok) = _realpathCache.Load(path);
            if (ok)
            {
                // 缓存里存的只会是 Realpath 的返回值（非 null）。
                return ret!;
            }
        }

        var result = _fs.Realpath(path);

        if (Enabled)
        {
            _realpathCache.Store(path, result);
        }

        return result;
    }

    public void Remove(string path)
    {
        _fs.Remove(path);
    }

    public void Chtimes(string path, DateTime accessTime, DateTime modificationTime)
    {
        _fs.Chtimes(path, accessTime, modificationTime);
    }

    public FileInfo? Stat(string path)
    {
        if (Enabled)
        {
            var (ret, ok) = _statCache.Load(path);
            if (ok)
            {
                return ret;
            }
        }

        var result = _fs.Stat(path);

        if (Enabled)
        {
            _statCache.Store(path, result);
        }

        return result;
    }

    public bool UseCaseSensitiveFileNames()
    {
        return _fs.UseCaseSensitiveFileNames();
    }

    public void WriteFile(string path, string data)
    {
        _fs.WriteFile(path, data);
    }

    public void AppendFile(string path, string data)
    {
        _fs.AppendFile(path, data);
    }
}
