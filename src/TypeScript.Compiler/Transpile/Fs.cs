// ported from: internal/transpile/fs.go
//
// 与 Go 的差异（Go 头注释：transpileFS embeds unsupported operations so unexpected filesystem access panics）：
// Go 的 struct 嵌入 `vfs.FS`（nil）让未覆写的方法在调用时 nil 解引用 panic；C# 没有嵌入，所以显式列出
// `FS` 的全部成员，未覆写的一律 `Debug.Fail`（语义同为"不该发生 → 立刻失败"）。
namespace TypeScript.Transpile;

using TypeScript.Vfs;

/// <summary>Go: <c>type transpileFS struct</c>（fs.go:11-14）。</summary>
internal sealed class TranspileFS : FS
{
    private readonly Dictionary<string, string> _files;

    internal TranspileFS(Dictionary<string, string> files)
    {
        _files = files;
    }

    /// <summary>Go: <c>func (fs *transpileFS) UseCaseSensitiveFileNames() bool</c>（fs.go:18-20）。</summary>
    public bool UseCaseSensitiveFileNames() => true;

    /// <summary>Go: <c>func (fs *transpileFS) FileExists(path string) bool</c>（fs.go:22-28）。</summary>
    public bool FileExists(string path)
    {
        if (!_files.ContainsKey(path))
        {
            Debug.Fail($"unexpected file existence check for \"{path}\"");
        }
        // Go: return ok（走到这里必然为 true）
        return true;
    }

    /// <summary>Go: <c>func (fs *transpileFS) ReadFile(path string) (string, bool)</c>（fs.go:30-36）。</summary>
    public bool TryReadFile(string path, out string contents)
    {
        if (!_files.TryGetValue(path, out var content))
        {
            Debug.Fail($"unexpected file read for \"{path}\"");
            contents = "";
            return false;
        }
        contents = content;
        return true;
    }

    /// <summary>Go: <c>func (fs *transpileFS) DirectoryExists(path string) bool</c>（fs.go:38-40）。</summary>
    public bool DirectoryExists(string path)
    {
        Debug.Fail($"unexpected directory existence check for \"{path}\"");
        return false;
    }

    /// <summary>Go: <c>func (fs *transpileFS) Realpath(path string) string</c>（fs.go:42-44）。</summary>
    public string Realpath(string path)
    {
        Debug.Fail($"unexpected realpath request for \"{path}\"");
        return path;
    }

    // 以下成员在 Go 里走嵌入的 nil `vfs.FS`（= 调用即 panic）。

    public void WriteFile(string path, string data) => Debug.Fail($"unexpected file write for \"{path}\"");

    public void AppendFile(string path, string data) => Debug.Fail($"unexpected file append for \"{path}\"");

    public void Remove(string path) => Debug.Fail($"unexpected remove for \"{path}\"");

    public void Chtimes(string path, DateTime accessTime, DateTime modificationTime) => Debug.Fail($"unexpected chtimes for \"{path}\"");

    public Entries GetAccessibleEntries(string path)
    {
        Debug.Fail($"unexpected directory listing for \"{path}\"");
        return default;
    }

    public TypeScript.Vfs.FileInfo? Stat(string path)
    {
        Debug.Fail($"unexpected stat for \"{path}\"");
        return null;
    }
}
