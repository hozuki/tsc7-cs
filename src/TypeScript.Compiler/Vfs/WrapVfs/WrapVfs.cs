// ported from: internal/vfs/wrapvfs/wrapvfs.go
//
// 偏离：
//   - Go 的每个字段是"可空函数"（nil = 透传）→ C# 用可空委托；<c>ReadFile</c> 的
//     <c>func(string) (string, bool)</c> 没有对应的 <see cref="Func{T, TResult}"/>（不能带 out 参数），
//     用 <see cref="TryReadFileFunc"/> 委托。
//   - 类型名 <c>FS</c>（wrapvfs 没有导出 FS 类型，只有未导出的 wrappedFS）在本命名空间不存在，接口直接用裸 <c>FS</c>。
namespace TypeScript.Vfs.WrapVfs;

using TypeScript.Vfs;

/// <summary>Go 的 <c>wrapvfs.Replacements</c>：<c>null</c> 的成员表示透传给被包装的 FS。</summary>
public sealed class Replacements
{
    /// <summary>Go: <c>UseCaseSensitiveFileNames func() bool</c>。</summary>
    public Func<bool>? UseCaseSensitiveFileNames;

    /// <summary>Go: <c>FileExists func(string) bool</c>。</summary>
    public Func<string, bool>? FileExists;

    /// <summary>Go: <c>ReadFile func(string) (string, bool)</c>。</summary>
    public TryReadFileFunc? ReadFile;

    /// <summary>Go: <c>WriteFile func(string, string) error</c>。</summary>
    public Action<string, string>? WriteFile;

    /// <summary>Go: <c>AppendFile func(string, string) error</c>。</summary>
    public Action<string, string>? AppendFile;

    /// <summary>Go: <c>Remove func(string) error</c>。</summary>
    public Action<string>? Remove;

    /// <summary>Go: <c>Chtimes func(string, time.Time, time.Time) error</c>。</summary>
    public Action<string, DateTime, DateTime>? Chtimes;

    /// <summary>Go: <c>DirectoryExists func(string) bool</c>。</summary>
    public Func<string, bool>? DirectoryExists;

    /// <summary>Go: <c>GetAccessibleEntries func(string) Entries</c>。</summary>
    public Func<string, Entries>? GetAccessibleEntries;

    /// <summary>Go: <c>Stat func(string) FileInfo</c>。</summary>
    public Func<string, FileInfo?>? Stat;

    /// <summary>Go: <c>Realpath func(string) string</c>。</summary>
    public Func<string, string>? Realpath;
}

/// <summary>Go 的 <c>wrapvfs</c> 包级函数。</summary>
public static partial class WrapVfs
{
    /// <summary>Go: <c>Wrap(fs vfs.FS, replacements Replacements) vfs.FS</c>。</summary>
    public static FS Wrap(FS fs, Replacements replacements)
    {
        return new WrappedFS(fs, replacements);
    }

    /// <summary>Go 的 <c>wrappedFS</c>（未导出）。</summary>
    private sealed class WrappedFS(FS fs, Replacements replacements) : FS
    {
        private readonly FS _fs = fs;
        private readonly Replacements _replacements = replacements;

        public bool UseCaseSensitiveFileNames()
        {
            return _replacements.UseCaseSensitiveFileNames?.Invoke() ?? _fs.UseCaseSensitiveFileNames();
        }

        public bool FileExists(string path)
        {
            return _replacements.FileExists?.Invoke(path) ?? _fs.FileExists(path);
        }

        public bool TryReadFile(string path, out string contents)
        {
            return _replacements.ReadFile != null
                ? _replacements.ReadFile(path, out contents)
                : _fs.TryReadFile(path, out contents);
        }

        public void WriteFile(string path, string data)
        {
            if (_replacements.WriteFile != null)
            {
                _replacements.WriteFile(path, data);
                return;
            }

            _fs.WriteFile(path, data);
        }

        public void AppendFile(string path, string data)
        {
            if (_replacements.AppendFile != null)
            {
                _replacements.AppendFile(path, data);
                return;
            }

            _fs.AppendFile(path, data);
        }

        public void Remove(string path)
        {
            if (_replacements.Remove != null)
            {
                _replacements.Remove(path);
                return;
            }

            _fs.Remove(path);
        }

        public void Chtimes(string path, DateTime accessTime, DateTime modificationTime)
        {
            if (_replacements.Chtimes != null)
            {
                _replacements.Chtimes(path, accessTime, modificationTime);
                return;
            }

            _fs.Chtimes(path, accessTime, modificationTime);
        }

        public bool DirectoryExists(string path)
        {
            return _replacements.DirectoryExists?.Invoke(path) ?? _fs.DirectoryExists(path);
        }

        public Entries GetAccessibleEntries(string path)
        {
            return _replacements.GetAccessibleEntries != null
                ? _replacements.GetAccessibleEntries(path)
                : _fs.GetAccessibleEntries(path);
        }

        public FileInfo? Stat(string path)
        {
            return _replacements.Stat != null ? _replacements.Stat(path) : _fs.Stat(path);
        }

        public string Realpath(string path)
        {
            return _replacements.Realpath?.Invoke(path) ?? _fs.Realpath(path);
        }
    }
}
