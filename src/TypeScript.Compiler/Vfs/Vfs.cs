// ported from: internal/vfs/vfs.go
//
// Go 的 io/fs 类型没有 C# 等价物（实测：.NET 的 FileInfo/DirectoryInfo 是 sealed，FileSystemInfo 的
// Attributes/LastWriteTime 也不是 virtual、Name/Exists/Delete 是 abstract —— 虚拟 FS 造不出带虚拟属性的
// FileSystemInfo，而 vfstest/harness 的内存 FS 是测试的核心），所以用我们自己的两个数据类：
//
//   fs.FileInfo           → FileInfo（Name/FullName/IsDirectory/IsSymlink/Size/ModifiedTimeUtc）
//                           Go 的 Name()/IsDir()/Size()/ModTime() 是方法，这里按 Main 定的数据类形状落成字段；
//                           FullName 是 os.FileInfo 才有的（fs.FileInfo 没有），虚拟 FS 用 realpath。
//                           Sys() 无对应物；Mode() 拆成 IsDirectory/IsSymlink 两个布尔（Go 的
//                           Mode()&fs.ModeSymlink != 0 → IsSymlink）。
//   fs.DirEntry           → DirEntry（Name/IsDirectory/IsSymlink + Info()）。
//                           Go 的 Type() fs.FileMode 不存在：`Type().IsRegular()` → `!IsDirectory && !IsSymlink`，
//                           `Type()&fs.ModeSymlink != 0` → `IsSymlink`。
//   io/fs.FS              → IIoFs（只保留 internal.Common 用到的 Stat/ReadDir/ReadFile；Go 的 Open 在 vfs 层从未调用）。
//   time.Time             → DateTime（统一 UTC：Go 的 os.Stat 给本地时区，这里钉死 UTC，MapFS 的 Clock 也按 UTC 存）。
//   fs.WalkDirFunc/fs.SkipDir → WalkDirFunc/WalkDirResult（见 WalkDir.cs）。
//
// Go 的哨兵错误没有 C# 等价物，映射成异常（调用点用 catch 对应类型代替 errors.Is）：
//
//	fs.ErrInvalid    → ArgumentException
//	fs.ErrPermission → UnauthorizedAccessException
//	fs.ErrExist      → IOException
//	fs.ErrNotExist   → FileNotFoundException（目录场景 DirectoryNotFoundException；错误文本 "file does not exist"）
//	fs.ErrClosed     → ObjectDisposedException
//	fs.SkipDir/SkipAll → WalkDirResult.SkipDir / WalkDirResult.SkipAll（控制流，不是异常）
//
// 偏离：Go 的 `type DirEntry = fs.DirEntry`/`FileInfo = fs.FileInfo` 别名与 Err* 包级变量不移植（别名无对象；
// Err* 见上表）。ReadFile 的 (contents string, ok bool) 按 §3.2 落成 bool TryReadFile(path, out contents)。
namespace TypeScript.Vfs;

/// <summary>Go 的 <c>vfs.FS</c>：文件系统抽象。</summary>
public interface FS
{
    /// <summary>Go: <c>UseCaseSensitiveFileNames</c>。</summary>
    bool UseCaseSensitiveFileNames();

    /// <summary>Go: <c>FileExists</c>。</summary>
    bool FileExists(string path);

    /// <summary>Go: <c>ReadFile(path) (contents string, ok bool)</c>（§3.2 的 (T,bool) → TryX）。</summary>
    bool TryReadFile(string path, out string contents);

    /// <summary>Go: <c>WriteFile(path, data) error</c>（失败抛 IOException 家族）。</summary>
    void WriteFile(string path, string data);

    /// <summary>Go: <c>AppendFile(path, data) error</c>。</summary>
    void AppendFile(string path, string data);

    /// <summary>Go: <c>Remove(path) error</c>：删除 path 及其全部内容，遇到第一个错误即返回。</summary>
    void Remove(string path);

    /// <summary>Go: <c>Chtimes(path, aTime, mTime) error</c>。</summary>
    void Chtimes(string path, DateTime accessTime, DateTime modificationTime);

    /// <summary>Go: <c>DirectoryExists</c>。</summary>
    bool DirectoryExists(string path);

    /// <summary>Go: <c>GetAccessibleEntries(path) Entries</c>（符号链接会被跟随）。</summary>
    Entries GetAccessibleEntries(string path);

    /// <summary>Go: <c>Stat(path) FileInfo</c>；不存在时返回 <c>null</c>（Go 的 nil）。</summary>
    FileInfo? Stat(string path);

    /// <summary>Go: <c>Realpath(path) string</c>：跟随符号链接并纠正大小写。</summary>
    string Realpath(string path);
}

/// <summary>Go 的 <c>fs.FileInfo</c>（+<c>os.FileInfo</c> 的 FullName）。</summary>
public sealed class FileInfo
{
    /// <summary>Go: <c>FileInfo.Name()</c>（不含路径）。</summary>
    public string Name = "";

    /// <summary>Go: <c>os.FileInfo</c> 的完整路径（<c>fs.FileInfo</c> 没有这个方法）；虚拟 FS 用 realpath。</summary>
    public string FullName = "";

    /// <summary>Go: <c>FileInfo.IsDir()</c>。</summary>
    public bool IsDirectory;

    /// <summary>Go: <c>Mode()&amp;fs.ModeSymlink != 0</c>。</summary>
    public bool IsSymlink;

    /// <summary>Go: <c>FileInfo.Size()</c>（目录为 0）。</summary>
    public long Size;

    /// <summary>Go: <c>FileInfo.ModTime()</c>（统一 UTC）。</summary>
    public DateTime ModifiedTimeUtc;

    /// <summary>真实 <see cref="System.IO.FileSystemInfo"/> → <see cref="FileInfo"/>（osvfs 的 IIoFs 用它）。</summary>
    public static FileInfo FromFileSystemInfo(System.IO.FileSystemInfo info)
    {
        var isDirectory = info is System.IO.DirectoryInfo;
        var size = 0L;
        if (info is System.IO.FileInfo file)
        {
            try
            {
                size = file.Length;
            }
            catch (IOException)
            {
                size = 0;
            }
            catch (UnauthorizedAccessException)
            {
                size = 0;
            }
        }

        var isSymlink = false;
        var modified = default(DateTime);
        try
        {
            isSymlink = (info.Attributes & FileAttributes.ReparsePoint) != 0;
            modified = info.LastWriteTimeUtc;
        }
        catch (IOException)
        {
            // 断链符号链接等：按 Go 的"尽力而为"处理。
        }
        catch (UnauthorizedAccessException)
        {
        }

        return new FileInfo
        {
            Name = info.Name,
            FullName = info.FullName,
            IsDirectory = isDirectory,
            IsSymlink = isSymlink,
            Size = size,
            ModifiedTimeUtc = modified,
        };
    }
}

/// <summary>Go 的 <c>vfs.Entries</c>。字段名与 Go 一致（导出字段）。</summary>
public struct Entries
{
    /// <summary>Go: <c>Files</c>（普通文件的名字，不含路径）。</summary>
    public string[] Files;

    /// <summary>Go: <c>Directories</c>（目录的名字，不含路径）。</summary>
    public string[] Directories;

    /// <summary>
    /// Go: <c>Symlinks map[string]struct{}</c>：Files/Directories 中原本是符号链接（或 Windows 重解析点）的
    /// 条目名。<c>null</c> 表示该 FS 不提供符号链接信息（调用方需自行检查）。
    /// </summary>
    public HashSet<string>? Symlinks;

    /// <summary>Go 的结构体字面量 <c>Entries{Files: …, Directories: …}</c>。</summary>
    public Entries(string[] files, string[] directories, HashSet<string>? symlinks = null)
    {
        Files = files;
        Directories = directories;
        Symlinks = symlinks;
    }
}

/// <summary>Go 的 <c>io/fs.FS</c>（+<c>Stat</c>/<c>ReadDir</c>/<c>ReadFile</c> 快路径）在 C# 侧的最小替身。
/// 路径是相对根的、以 '/' 分隔的 fs 风格路径（根自身是 "."），失败一律返回 null/false。</summary>
public interface IIoFs
{
    /// <summary>Go: <c>fs.Stat(fsys, name)</c>。</summary>
    FileInfo? Stat(string name);

    /// <summary>Go: <c>fs.ReadDir(fsys, name)</c>，按名字序数序（Go 的 fs.ReadDir 会排序）。</summary>
    DirEntry[]? ReadDir(string name);

    /// <summary>Go: <c>fs.ReadFile(fsys, name)</c> 的结果再经编码解码（UTF-16/UTF-8 BOM）后的文本。
    /// 偏离：Go 的 io/fs 交出原始字节、由 <c>vfs.internal</c> 统一解码；C# 的 string 是 UTF-16，
    /// 内存 FS 必须逐字符无损保存（§3.1 的孤立代理项），所以解码放在各 IIoFs 实现里
    /// （osvfs 用 <c>Internal.DecodeBytes</c>，vfstest 在构造时解码 []byte 输入）。</summary>
    bool TryReadFile(string name, out string contents);
}

/// <summary>Go 的 <c>io/fs.DirEntry</c>（vfs 只用到 Name/IsDir/Type 的 symlink 位/Info）。</summary>
public sealed class DirEntry
{
    private FileInfo? _info;
    private Func<FileInfo>? _stat;

    /// <summary>Go: <c>DirEntry.Name()</c>。</summary>
    public string Name = "";

    /// <summary>Go: <c>DirEntry.IsDir()</c>（符号链接目录的条目这里是 <c>false</c>，与 Go 的 ModeSymlink 一致）。</summary>
    public bool IsDirectory;

    /// <summary>Go 的 <c>Type()&amp;fs.ModeSymlink != 0</c>。</summary>
    public bool IsSymlink;

    public DirEntry()
    {
    }

    /// <summary>数据条目（虚拟 FS 的目录列举结果、walkdir 的符号链接条目）。</summary>
    public DirEntry(string name, bool isDirectory, bool isSymlink, FileInfo? info = null)
    {
        Name = name;
        IsDirectory = isDirectory;
        IsSymlink = isSymlink;
        _info = info;
    }

    /// <summary>Go 的 <c>walkDirEntry.Info()</c>：惰性取元信息。</summary>
    internal DirEntry(string name, bool isDirectory, bool isSymlink, Func<FileInfo> stat)
    {
        Name = name;
        IsDirectory = isDirectory;
        IsSymlink = isSymlink;
        _stat = stat;
    }

    /// <summary>Go: <c>fs.FileInfoToDirEntry</c>。</summary>
    public static DirEntry FromFileInfo(FileInfo info)
        => new(info.Name, info.IsDirectory, info.IsSymlink, info);

    /// <summary>Go: <c>DirEntry.Info() (fs.FileInfo, error)</c>；取不到时抛 <see cref="FileNotFoundException"/>。</summary>
    public FileInfo Info()
    {
        if (_stat != null)
        {
            _info = _stat();
            _stat = null;
        }

        return _info ?? throw new FileNotFoundException("file does not exist", Name);
    }
}

/// <summary>Go 的 <c>func(path string) (contents string, ok bool)</c>（FS.ReadFile 的委托形状；
/// <see cref="Func{T, TResult}"/> 不能带 out 参数）。</summary>
public delegate bool TryReadFileFunc(string path, out string contents);

/// <summary>Go 的 <c>vfstest.brokenSymlinkError</c>（C# 侧公开：调用方要能把它和"不存在"区分开——
/// Go 里 <c>errors.Is(err, fs.ErrNotExist)</c> 对它是 false，见 MapFS 的 mkdirAll/WriteFile）。
/// 错误文本与 Go 一致：<c>broken symlink "from" -&gt; "to"</c>。</summary>
public sealed class BrokenSymlinkException(string from, string to) : IOException($"broken symlink \"{from}\" -> \"{to}\"")
{
    /// <summary>Go: <c>brokenSymlinkError.from</c>。</summary>
    public string From = from;

    /// <summary>Go: <c>brokenSymlinkError.to</c>。</summary>
    public string To = to;
}

/// <summary>Go 的 <c>vfs</c> 包级函数所在的静态类。</summary>
public static partial class Vfs
{
}
