// ported from: internal/bundled/embed.go（//go:build !noembed 分支；noembed.go 不移植，理由见 Bundled.cs 头部）
//
// 偏离（都是语言/存储差异）：
//   * Go 的 embeddedContents（embed_generated.go）是 map[string]string，值是文件内容；C# 的表值改成资源名，
//     按需从 EmbeddedResource 读（AOT 安全、无反射枚举，也不生成 3.8 MB 字符串字面量）。表见 Embed.Generated.cs。
//   * Go 的 Stat 用 len(map 值) 取大小；C# 取资源流的 Length（不读内容）。
//   * 本移植新增读取 API：TryLoadLib/LoadLib/TryLoadLibBytes/LoadLibBytes（Go 的内容本来就编译进变量，
//     没有等价的导出成员），见"读取嵌入的 lib"一节。
namespace TypeScript.Bundled;

using TypeScript.Vfs;
using TypeScript.Vfs.Internal;

using VfsFileInfo = TypeScript.Vfs.FileInfo;

public static partial class Bundled
{
    /// <summary>Go: <c>scheme</c>。</summary>
    private const string _scheme = "bundled:///";

    /// <summary>Go: <c>splitPath</c>（strings.CutPrefix）：<paramref name="rest"/> 是去掉 scheme 后的路径。</summary>
    internal static bool SplitPath(string path, out string rest)
    {
        if (path.StartsWith(_scheme, StringComparison.Ordinal))
        {
            rest = path[_scheme.Length..];
            return true;
        }

        rest = "";
        return false;
    }

    /// <summary>Go: <c>libPath()</c>（embed.go）。</summary>
    private static string LibPathImpl() => _scheme + "libs";

    /// <summary>Go: <c>IsBundled</c>：path 是否落在嵌入文件系统里。</summary>
    public static bool IsBundled(string path) => SplitPath(path, out _);

    /// <summary>Go: <c>wrapFS</c>（embed.go）。</summary>
    private static FS WrapFSImpl(FS fs) => new WrappedFS(fs);

    // ── 读取嵌入的 lib（本移植新增的公开 API） ───────────────────────────────────────────────────────

    /// <summary>按名读取嵌入的 lib 内容（名字是 <see cref="LibNames"/> 里的条目，如 <c>"lib.es5.d.ts"</c>）。
    /// 本移植新增：Go 侧的内容编译进 embeddedContents 变量，没有等价的读取函数。</summary>
    public static bool TryLoadLib(string name, out string contents)
    {
        if (TryReadEmbeddedBytes("libs/" + name, out var bytes))
        {
            // 与 osvfs 的 ReadFile 一致：FS 层交出解码后的文本（BOM/UTF-16 由 Internal.DecodeBytes 处理）。
            contents = Internal.DecodeBytes(bytes);
            return true;
        }

        contents = "";
        return false;
    }

    /// <summary>按名读取嵌入的 lib 内容；不存在时抛 <see cref="DebugFailureException"/>
    /// （存在性判断用 <see cref="TryLoadLib"/>）。</summary>
    public static string LoadLib(string name) =>
        TryLoadLib(name, out var contents)
            ? contents
            : throw new DebugFailureException("no such bundled lib: " + name);

    /// <summary>同 <see cref="TryLoadLib"/>，但返回文件的原始字节（lib 是 UTF-8 文本，字节级比对用这个）。</summary>
    public static bool TryLoadLibBytes(string name, out byte[] bytes) => TryReadEmbeddedBytes("libs/" + name, out bytes);

    /// <summary>同 <see cref="LoadLib"/>，但返回原始字节。</summary>
    public static byte[] LoadLibBytes(string name) =>
        TryReadEmbeddedBytes("libs/" + name, out var bytes)
            ? bytes
            : throw new DebugFailureException("no such bundled lib: " + name);

    // ── 嵌入资源的读取与元信息 ───────────────────────────────────────────────────────────────────────

    /// <summary>Go 的 <c>embeddedContents[rest]</c>：<paramref name="key"/> 是 FS 相对路径
    /// （<c>"libs/&lt;name&gt;"</c>）。</summary>
    internal static bool TryReadEmbeddedBytes(string key, out byte[] bytes)
    {
        if (EmbeddedContents.TryGetValue(key, out var resourceName))
        {
            bytes = ReadResourceBytes(resourceName);
            return true;
        }

        bytes = [];
        return false;
    }

    /// <summary>Go 的 <c>len(embeddedContents[rest])</c>（Stat 用）；资源不存在时返回 <c>null</c>。</summary>
    internal static long? GetEmbeddedSize(string key)
    {
        if (EmbeddedContents.TryGetValue(key, out var resourceName))
        {
            using var stream = typeof(Bundled).Assembly.GetManifestResourceStream(resourceName);
            if (stream != null)
            {
                return stream.Length;
            }
        }

        return null;
    }

    private static byte[] ReadResourceBytes(string resourceName)
    {
        // 资源名来自生成表（不由反射枚举），AOT 安全。
        using var stream = typeof(Bundled).Assembly.GetManifestResourceStream(resourceName)
            ?? throw new DebugFailureException("failed to find embedded lib resource: " + resourceName);
        var bytes = new byte[stream.Length];
        stream.ReadExactly(bytes);
        return bytes;
    }
}

/// <summary>Go 的 <c>wrappedFS</c>（未导出类型按 C# 习惯命名）：把 <c>bundled:///</c> 路径导向嵌入资源，
/// 其余路径透传给被包装的 FS。
/// Go 的注释：直接实现 vfs.FS 而不经 io/fs.FS —— vfs.FS 以 string 传内容（go:embed 的底层就是 string），
/// 走 io/fs 会多一次 []byte ↔ string 拷贝；C# 侧同理（查生成表 + 读 EmbeddedResource）。</summary>
internal sealed class WrappedFS(FS fs) : FS
{
    private readonly FS _fs = fs;

    public bool UseCaseSensitiveFileNames() => _fs.UseCaseSensitiveFileNames();

    public bool FileExists(string path)
    {
        if (Bundled.SplitPath(path, out var rest))
        {
            return Bundled.EmbeddedContents.ContainsKey(rest);
        }

        return _fs.FileExists(path);
    }

    public bool TryReadFile(string path, out string contents)
    {
        if (Bundled.SplitPath(path, out var rest))
        {
            if (Bundled.TryReadEmbeddedBytes(rest, out var bytes))
            {
                // Go 把嵌入的原始字节直接当 string 交出；C# 的 FS 契约是"交出解码后的文本"（与 osvfs 一致）。
                contents = Internal.DecodeBytes(bytes);
                return true;
            }

            contents = "";
            return false;
        }

        return _fs.TryReadFile(path, out contents);
    }

    public bool DirectoryExists(string path)
    {
        if (Bundled.SplitPath(path, out var rest))
        {
            return rest == "libs";
        }

        return _fs.DirectoryExists(path);
    }

    public Entries GetAccessibleEntries(string path)
    {
        if (Bundled.SplitPath(path, out var rest))
        {
            if (rest.Length == 0)
            {
                return new Entries([], ["libs"]);
            }

            if (rest == "libs")
            {
                // Go 直接把 LibNames 交出去（调用方原地排序会改到它）；这里给一份副本，
                // 内容与顺序完全相同，但不会被调用方改坏。
                return new Entries((string[])Bundled.LibNames.Clone(), []);
            }

            // Go 这里返回零值（nil 切片）；§3.2：语义等同时用空数组。
            return new Entries([], []);
        }

        return _fs.GetAccessibleEntries(path);
    }

    public VfsFileInfo? Stat(string path)
    {
        if (Bundled.SplitPath(path, out var rest))
        {
            if (rest.Length == 0 || rest == "libs")
            {
                return new VfsFileInfo { Name = rest, FullName = path, IsDirectory = true };
            }

            if (Bundled.GetEmbeddedSize(rest) is { } size)
            {
                return new VfsFileInfo { Name = SplitLibName(rest), FullName = path, Size = size };
            }

            return null;
        }

        return _fs.Stat(path);
    }

    public string Realpath(string path) => Bundled.SplitPath(path, out _) ? path : _fs.Realpath(path);

    public void WriteFile(string path, string data)
    {
        if (Bundled.SplitPath(path, out _))
        {
            // Go: panic("cannot write to embedded file system")
            Debug.Fail("cannot write to embedded file system");
        }

        _fs.WriteFile(path, data);
    }

    public void AppendFile(string path, string data)
    {
        if (Bundled.SplitPath(path, out _))
        {
            // Go: panic("cannot write to embedded file system")
            Debug.Fail("cannot write to embedded file system");
        }

        _fs.AppendFile(path, data);
    }

    public void Remove(string path)
    {
        if (Bundled.SplitPath(path, out _))
        {
            // Go: panic("cannot remove from embedded file system")
            Debug.Fail("cannot remove from embedded file system");
        }

        _fs.Remove(path);
    }

    public void Chtimes(string path, DateTime accessTime, DateTime modificationTime)
    {
        if (Bundled.SplitPath(path, out _))
        {
            // Go: panic("cannot change times on embedded file system")
            Debug.Fail("cannot change times on embedded file system");
        }

        _fs.Chtimes(path, accessTime, modificationTime);
    }

    /// <summary>Go: <c>strings.CutPrefix(rest, "libs/")</c>（嵌入路径的 rest 已是 lib 文件名）。</summary>
    private static string SplitLibName(string rest)
    {
        const string prefix = "libs/";
        return rest.StartsWith(prefix, StringComparison.Ordinal) ? rest[prefix.Length..] : rest;
    }
}
