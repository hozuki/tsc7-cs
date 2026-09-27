// ported from: internal/vfs/trackingvfs/trackingvfs.go
//
// 偏离：类型名 <c>FS</c> 与 <c>TypeScript.Vfs.FS</c> 同名（Go 里是 <c>trackingvfs.FS</c> vs <c>vfs.FS</c>）：
// 本命名空间内的裸 <c>FS</c> 指本类，接口写 <c>TypeScript.Vfs.FS</c>。Go 的结构体字面量缺省值 → C# 构造函数。
namespace TypeScript.Vfs.TrackingVfs;

using TypeScript.Collections;
using TypeScript.Vfs;

/// <summary>
/// Go 的 <c>trackingvfs.FS</c>：记录编译期间访问过的每个文件路径，好让 watch 模式知道编译器依赖了哪些文件
/// （包括模块解析失败时访问过的不存在路径）。写操作（WriteFile/Remove/Chtimes）是产物而非依赖，不记录。
/// </summary>
public sealed class FS : TypeScript.Vfs.FS
{
    /// <summary>Go: <c>Inner</c>。</summary>
    public TypeScript.Vfs.FS Inner;

    /// <summary>Go: <c>SeenFiles</c>。</summary>
    public SyncSet<string> SeenFiles;

    public FS(TypeScript.Vfs.FS inner, SyncSet<string>? seenFiles = null)
    {
        Inner = inner;
        SeenFiles = seenFiles ?? new SyncSet<string>();
    }

    public bool TryReadFile(string path, out string contents)
    {
        SeenFiles.Add(path);
        return Inner.TryReadFile(path, out contents);
    }

    public bool FileExists(string path)
    {
        SeenFiles.Add(path);
        return Inner.FileExists(path);
    }

    public bool UseCaseSensitiveFileNames()
    {
        return Inner.UseCaseSensitiveFileNames();
    }

    public void WriteFile(string path, string data)
    {
        Inner.WriteFile(path, data);
    }

    public void AppendFile(string path, string data)
    {
        Inner.AppendFile(path, data);
    }

    public void Remove(string path)
    {
        Inner.Remove(path);
    }

    public void Chtimes(string path, DateTime accessTime, DateTime modificationTime)
    {
        Inner.Chtimes(path, accessTime, modificationTime);
    }

    public bool DirectoryExists(string path)
    {
        SeenFiles.Add(path);
        return Inner.DirectoryExists(path);
    }

    public Entries GetAccessibleEntries(string path)
    {
        SeenFiles.Add(path);
        return Inner.GetAccessibleEntries(path);
    }

    public FileInfo? Stat(string path)
    {
        SeenFiles.Add(path);
        return Inner.Stat(path);
    }

    public string Realpath(string path)
    {
        SeenFiles.Add(path);
        return Inner.Realpath(path);
    }
}
