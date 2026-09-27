// ported from: internal/vfs/vfstest/vfstest.go（Clock/MapFile/FromMap/convertMapFS 部分；MapFS 见 VfsTest.MapFS.cs）
//
// 偏离：
//   - Go 的 <c>testing/fstest.MapFile</c> → <see cref="MapFile"/>（C# 没有 stdlib 等价物）。Go 的
//     <c>Data []byte</c> 在这里是 <c>string</c>：C# 的 string 是 UTF-16，MapFS 必须逐字符无损保存
//     （§3.1 的孤立代理项），而 <c>File.ReadAllBytes</c> 那条路才需要字节。[]byte 输入在
//     <see cref="FromMap(Dictionary{string, object}, bool)"/> 里用 <c>Internal.DecodeBytes</c> 解码（BOM/UTF-16 处理）。
//     <c>Mode fs.FileMode</c> 拆成 <c>IsDir</c>/<c>IsSymlink</c>；<c>Sys any</c> 不移植（MapFS 内部用 MapEntry）。
//   - Go 的 <c>FromMap[File any]</c> 是泛型 + type switch → C# 三个重载（string/MapFile/object，object 版对应
//     <c>map[string]any</c>，值是 string | byte[] | MapFile）。非法类型的 panic 文本用 .NET 类型名
//     （Go: "invalid file type int"，C#: "invalid file type Int32"）。
//   - <c>FromMapWithClock</c> 在 C# 里只保留 object 重载（Go 的泛型实例化在 C# 侧没有对应调用点）。
//   - Go 的 panic → <c>Debug.Fail</c>（§3.2）；<c>time.Time</c> → <see cref="DateTime"/>（统一 UTC）。
namespace TypeScript.Vfs.VfsTest;

// 注意：这里**不能** `using System.Diagnostics;` —— 文件级 using 会先于外层 `TypeScript` 命名空间的成员解析，
// `Debug.Fail(...)` 就会绑到 System.Diagnostics.Debug.Fail（VSTest 下抛 DebugAssertException、普通进程里 FailFast），
// 而不是 `TypeScript.Debug.Fail`。`UnreachableException` 用别名引入。
using UnreachableException = System.Diagnostics.UnreachableException;
using TypeScript.TSPath;
using TypeScript.Vfs;
using TypeScript.Vfs.Internal;
using TypeScript.Vfs.Iovfs;

using static TypeScript.TSPath.TSPath;

/// <summary>Go 的 <c>vfstest.Clock</c>：让测试控制 MapFS 里的时间。</summary>
public interface Clock
{
    /// <summary>Go: <c>Now() time.Time</c>。</summary>
    DateTime Now();

    /// <summary>Go: <c>SinceStart() time.Duration</c>。</summary>
    TimeSpan SinceStart();
}

/// <summary>Go 的 <c>vfstest.clockImpl</c>（未导出 → C# 习惯命名；用 UTC 时钟，见文件头）。</summary>
internal sealed class SystemClock(DateTime start) : Clock
{
    public DateTime Now()
    {
        return DateTime.UtcNow;
    }

    public TimeSpan SinceStart()
    {
        return DateTime.UtcNow - start;
    }
}

/// <summary>Go 的 <c>testing/fstest.MapFile</c>（见文件头）。</summary>
public sealed class MapFile
{
    /// <summary>Go: <c>Data</c>：文件内容，或符号链接的目标路径。</summary>
    public string Data = "";

    /// <summary>Go: <c>Mode&amp;fs.ModeDir != 0</c>。</summary>
    public bool IsDir;

    /// <summary>Go: <c>Mode&amp;fs.ModeSymlink != 0</c>。</summary>
    public bool IsSymlink;

    /// <summary>Go: <c>ModTime</c>。</summary>
    public DateTime ModTime;
}

/// <summary>Go 的 <c>vfstest</c> 包级函数。</summary>
public static partial class VfsTest
{
    /// <summary>Go: <c>Symlink(target) *fstest.MapFile</c>。</summary>
    public static MapFile Symlink(string target)
    {
        return new MapFile { Data = target, IsSymlink = true };
    }

    /// <summary>Go: <c>FromMap(map[string]string, useCaseSensitiveFileNames)</c>。</summary>
    public static FS FromMap(Dictionary<string, string> m, bool useCaseSensitiveFileNames)
    {
        return FromMapWithClock(m, useCaseSensitiveFileNames, new SystemClock(DateTime.UtcNow));
    }

    /// <summary>Go: <c>FromMap(map[string]*fstest.MapFile, …)</c>。</summary>
    public static FS FromMap(Dictionary<string, MapFile> m, bool useCaseSensitiveFileNames)
    {
        return FromMapWithClock(m, useCaseSensitiveFileNames, new SystemClock(DateTime.UtcNow));
    }

    /// <summary>Go: <c>FromMap(map[string]any, …)</c>（值可以是 string / byte[] / <see cref="MapFile"/>）。</summary>
    public static FS FromMap(Dictionary<string, object> m, bool useCaseSensitiveFileNames)
    {
        return FromMapWithClock(m, useCaseSensitiveFileNames, new SystemClock(DateTime.UtcNow));
    }

    public static FS FromMapWithClock(Dictionary<string, string> m, bool useCaseSensitiveFileNames, Clock clock)
    {
        return FromMapCore(m, useCaseSensitiveFileNames, clock, value => new MapFile { Data = value, ModTime = clock.Now() });
    }

    public static FS FromMapWithClock(Dictionary<string, MapFile> m, bool useCaseSensitiveFileNames, Clock clock)
    {
        // Go: fCopy := *f; fCopy.ModTime = clock.Now()
        return FromMapCore(m, useCaseSensitiveFileNames, clock, value => new MapFile
        {
            Data = value.Data,
            IsDir = value.IsDir,
            IsSymlink = value.IsSymlink,
            ModTime = clock.Now(),
        });
    }

    /// <summary>Go: <c>FromMapWithClock[File any](m map[string]File, useCaseSensitiveFileNames, clock)</c>。</summary>
    public static FS FromMapWithClock(Dictionary<string, object> m, bool useCaseSensitiveFileNames, Clock clock)
    {
        return FromMapCore(m, useCaseSensitiveFileNames, clock, value => ToMapFile(value, clock));
    }

    /// <summary>
    /// Go: <c>FromMap</c> 的主干：校验路径、按路径部件序构造、再交给 <see cref="Iovfs.From"/>。
    /// 路径必须是 tspath 规范化后的绝对路径（无尾分隔符），且必须全是 POSIX 风格或全是 Windows 风格。
    /// </summary>
    private static FS FromMapCore<TFile>(
        Dictionary<string, TFile> m,
        bool useCaseSensitiveFileNames,
        Clock clock,
        Func<TFile, MapFile> toMapFile)
    {
        var posix = false;
        var windows = false;

        void CheckPath(string p)
        {
            if (!IsRootedDiskPath(p))
            {
                Fail<object>($"non-rooted path \"{p}\"");
            }

            if (RemoveTrailingDirectorySeparator(NormalizePath(p)) != p)
            {
                Fail<object>($"non-normalized path \"{p}\"");
            }

            if (p.StartsWith('/'))
            {
                posix = true;
            }
            else
            {
                windows = true;
            }
        }

        var mfs = new Dictionary<string, MapFile>(m.Count);
        // Go: 按序构造，保证时间是单调的。
        var keys = m.Keys.ToArray();
        Array.Sort(keys, ComparePathsByParts);
        foreach (var p in keys)
        {
            var file = toMapFile(m[p]);
            CheckPath(p);

            if (file.IsSymlink)
            {
                var target = file.Data;
                CheckPath(target);
                if (target.StartsWith('/'))
                {
                    target = target[1..];
                }

                file = new MapFile { Data = target, IsDir = file.IsDir, IsSymlink = true, ModTime = file.ModTime };
            }

            var key = p.StartsWith('/') ? p[1..] : p;
            mfs[key] = file;
        }

        if (posix && windows)
        {
            Fail<object>("mixed posix and windows paths");
        }

        return Iovfs.From(ConvertMapFS(mfs, useCaseSensitiveFileNames, clock), useCaseSensitiveFileNames);
    }

    /// <summary>Go: <c>convertMapFS(input, useCaseSensitiveFileNames, clock) *MapFS</c>。</summary>
    internal static MapFS ConvertMapFS(Dictionary<string, MapFile> input, bool useCaseSensitiveFileNames, Clock? clock)
    {
        clock ??= new SystemClock(DateTime.UtcNow);
        var m = new MapFS(useCaseSensitiveFileNames, clock);

        // 校验输入是否良构：canonical path 不能重复。
        var canonicalPaths = new Dictionary<string, string>(input.Count);
        foreach (var path in input.Keys)
        {
            var canonical = m.GetCanonicalPath(path);
            if (canonicalPaths.TryGetValue(canonical, out var other))
            {
                var (min, max) = string.CompareOrdinal(path, other) <= 0 ? (path, other) : (other, path);
                Fail<object>($"duplicate path: \"{min}\" and \"{max}\" have the same canonical path");
            }

            canonicalPaths[canonical] = path;
        }

        // 按深度 + 路径排序，保证父目录先于子项被显式创建（若输入里写了）。
        var inputKeys = input.Keys.ToArray();
        Array.Sort(inputKeys, ComparePathsByParts);
        foreach (var p in inputKeys)
        {
            var file = input[p];

            // 显式建出所有中间目录，好把 realpath 挂到每一个上（fstest.MapFS 是现合成的，但那样很难在
            // 深层 FileInfo 方法里补回 realpath）。
            var dir = DirName(p);
            if (dir.Length != 0)
            {
                try
                {
                    m.MkdirAll(dir);
                }
                catch (IOException e)
                {
                    Fail<object>($"failed to create intermediate directories for \"{p}\": {e.Message}");
                }
            }

            m.SetEntry(p, m.GetCanonicalPath(p), file);
        }

        return m;
    }

    /// <summary>Go: <c>comparePathsByParts(a, b)</c>：先按路径部件、再按整串的序数比较。</summary>
    internal static int ComparePathsByParts(string a, string b)
    {
        while (true)
        {
            var aIdx = a.IndexOf('/');
            var bIdx = b.IndexOf('/');
            if (aIdx < 0 || bIdx < 0)
            {
                return string.CompareOrdinal(a, b);
            }

            var r = string.CompareOrdinal(a[..aIdx], b[..bIdx]);
            if (r != 0)
            {
                return r;
            }

            a = a[(aIdx + 1)..];
            b = b[(bIdx + 1)..];
        }
    }

    /// <summary>Go: <c>dirName(p)</c>（path.Split 的目录部分，去掉尾部分隔符）。</summary>
    internal static string DirName(string p)
    {
        var idx = p.LastIndexOf('/');
        return idx < 0 ? "" : p[..idx];
    }

    /// <summary>Go: <c>baseName(p)</c>（path.Split 的文件名部分）。</summary>
    internal static string BaseName(string p)
    {
        var idx = p.LastIndexOf('/');
        return idx < 0 ? p : p[(idx + 1)..];
    }

    private static MapFile ToMapFile(object? value, Clock clock)
    {
        switch (value)
        {
            case string s:
                return new MapFile { Data = s, ModTime = clock.Now() };
            case byte[] bytes:
                return new MapFile { Data = Internal.DecodeBytes(bytes), ModTime = clock.Now() };
            case MapFile f:
                return new MapFile { Data = f.Data, IsDir = f.IsDir, IsSymlink = f.IsSymlink, ModTime = clock.Now() };
            default:
                return Fail<MapFile>($"invalid file type {value?.GetType().Name ?? "<nil>"}");
        }
    }

    /// <summary>Go 的 <c>panic</c>：<c>Debug.Fail</c> 必抛，后面的 throw 只为满足返回值分析。</summary>
    private static T Fail<T>(string reason)
    {
        Debug.Fail(reason);
        throw new UnreachableException();
    }
}
