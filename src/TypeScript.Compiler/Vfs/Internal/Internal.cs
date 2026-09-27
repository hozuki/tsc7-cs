// ported from: internal/vfs/internal/internal.go
//
// 偏离：
//   - Go 的 <c>Common.RootFor func(root string) fs.FS</c> 里 fs.FS 没有 C# 等价物 → 换成 <see cref="IIoFs"/>
//     （只保留 internal 用到的 Stat/ReadDir/ReadFile）。
//   - Go 的 <c>Common.IsReparsePoint func(path string) bool</c>（Windows 上识别 junction 的 OS 专用兜底）
//     不再需要：.NET 的 FileSystemInfo.Attributes 已经把重解析点暴露成 ReparsePoint，osvfs 在
//     <c>IIoFs.ReadDir</c> 里直接算出 DirEntry.IsSymlink（Go 的 DirEntry.Type() 在 Windows 上返回
//     ModeIrregular 才需要这个兜底）。
//   - Go 的 <c>RootAndPath</c> 返回三个值 → C# 元组 <c>(Fsys, RootName, Remainder)</c>（Go 的 rest 不能叫 Rest：C# 把 Rest 保留给 ValueTuple 的第 8 个字段，CS8126）（与 Collections 的约定一致）。
namespace TypeScript.Vfs.Internal;

using System.Text;
using TypeScript.TSPath;
using TypeScript.Vfs;

using static TypeScript.TSPath.TSPath;

/// <summary>Go 的 <c>internal</c> 包级函数。</summary>
public static partial class Internal
{
    /// <summary>Go: <c>RootLength</c>：路径根的长度（URL 根编码成 ^l）。非绝对路径 → <see cref="Debug.Fail"/>。</summary>
    public static int RootLength(string p)
    {
        var l = GetEncodedRootLength(p);
        if (l == 0)
        {
            Debug.Fail($"vfs: path \"{p}\" is not absolute");
        }
        else if (l < 0)
        {
            return ~l;
        }

        return l;
    }

    /// <summary>Go: <c>SplitPath(p) (rootName, rest string)</c>。</summary>
    public static (string RootName, string Remainder) SplitPath(string p)
    {
        p = NormalizePath(p);
        var l = RootLength(p);
        var rootName = p[..l];
        var rest = p[l..];
        rest = RemoveTrailingDirectorySeparator(rest);
        return (rootName, rest);
    }

    /// <summary>Go: <c>decodeBytes(s) (contents string, ok bool)</c>（UTF-16 LE/BE BOM 与 UTF-8 BOM 的处理）。
    /// C# 侧返回 string（Go 的非 BOM 分支是把原始字节重新解释成 string；C# 用 UTF-8 解码，非法序列替换为 U+FFFD）。</summary>
    internal static string DecodeBytes(ReadOnlySpan<byte> s)
    {
        if (s.Length >= 2)
        {
            if (s[0] == 0xFF && s[1] == 0xFE)
            {
                return DecodeUtf16(s[2..], bigEndian: false);
            }

            if (s[0] == 0xFE && s[1] == 0xFF)
            {
                return DecodeUtf16(s[2..], bigEndian: true);
            }
        }

        if (s.Length >= 3 && s[0] == 0xEF && s[1] == 0xBB && s[2] == 0xBF)
        {
            s = s[3..];
        }

        return Encoding.UTF8.GetString(s);
    }

    /// <summary>Go: <c>decodeUtf16(s string, order binary.ByteOrder) string</c>。奇数长度与 Go 一样返回空串
    /// （Go 的 binary.Read 会报错）；孤立代理项与 Go 的 utf16.Decode 一样替换成 U+FFFD。</summary>
    internal static string DecodeUtf16(ReadOnlySpan<byte> s, bool bigEndian)
    {
        if (s.Length % 2 != 0)
        {
            return "";
        }

        return (bigEndian ? Encoding.BigEndianUnicode : Encoding.Unicode).GetString(s);
    }
}

/// <summary>Go 的 <c>internal.Common</c>：把 vfs.FS 的路径语义架在"根 -> fs.FS"之上。</summary>
public sealed class Common
{
    /// <summary>Go: <c>RootFor func(root string) fs.FS</c>。</summary>
    public Func<string, IIoFs?> RootFor;

    public Common(Func<string, IIoFs?> rootFor)
    {
        RootFor = rootFor;
    }

    /// <summary>Go: <c>Common.RootAndPath(path) (fsys fs.FS, rootName, rest string)</c>。</summary>
    public (IIoFs? Fsys, string RootName, string Remainder) RootAndPath(string path)
    {
        var (rootName, rest) = Internal.SplitPath(path);
        if (rest.Length == 0)
        {
            rest = ".";
        }

        return (RootFor(rootName), rootName, rest);
    }

    /// <summary>Go: <c>Common.Stat</c>。</summary>
    public FileInfo? Stat(string path)
    {
        var (fsys, _, rest) = RootAndPath(path);
        return fsys?.Stat(rest);
    }

    /// <summary>Go: <c>Common.FileExists</c>。</summary>
    public bool FileExists(string path)
    {
        var stat = Stat(path);
        return stat != null && !stat.IsDirectory;
    }

    /// <summary>Go: <c>Common.DirectoryExists</c>。</summary>
    public bool DirectoryExists(string path)
    {
        var stat = Stat(path);
        return stat != null && stat.IsDirectory;
    }

    /// <summary>Go: <c>Common.GetAccessibleEntries</c>。</summary>
    public Entries GetAccessibleEntries(string path)
    {
        var files = new List<string>();
        var directories = new List<string>();
        var symlinks = new HashSet<string>();

        bool addToResult(string name, bool isDir, bool isFile, bool isLink)
        {
            if (isDir)
            {
                directories.Add(name);
            }
            else if (isFile)
            {
                files.Add(name);
            }
            else
            {
                return false;
            }

            if (isLink)
            {
                symlinks.Add(name);
            }

            return true;
        }

        var entries = GetEntries(path);
        if (entries != null)
        {
            foreach (var entry in entries)
            {
                var name = entry.Name;
                if (!entry.IsSymlink)
                {
                    if (addToResult(name, entry.IsDirectory, !entry.IsDirectory, false))
                    {
                        continue;
                    }

                    // Go: 既不是目录也不是普通文件（命名管道等）→ 丢弃。
                    continue;
                }

                // Go: UNIX-like 系统会明确标记符号链接；跟随链接后按目标类型归类。
                var stat = Stat(path + "/" + name);
                if (stat != null)
                {
                    addToResult(name, stat.IsDirectory, !stat.IsDirectory, true);
                }
            }
        }

        return new Entries(files.ToArray(), directories.ToArray(), symlinks);
    }

    /// <summary>Go: <c>Common.getEntries(path) []fs.DirEntry</c>。</summary>
    private DirEntry[]? GetEntries(string path)
    {
        var (fsys, _, rest) = RootAndPath(path);
        return fsys?.ReadDir(rest);
    }

    /// <summary>Go: <c>Common.ReadFile(path) (contents string, ok bool)</c>。</summary>
    public bool TryReadFile(string path, out string contents)
    {
        var (fsys, _, rest) = RootAndPath(path);
        if (fsys == null)
        {
            contents = "";
            return false;
        }

        return fsys.TryReadFile(rest, out contents);
    }
}
