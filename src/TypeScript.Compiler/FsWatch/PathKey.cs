// ported from: internal/fswatch/pathkey.go
//
// C# 映射与偏离（逐条）：
// 1) Go 的 `PathComparer` 里嵌了未导出的 `comparer pathComparer`（同 package 其它文件直接读 `c.comparer`）→
//    C# 的私有字段 `_comparer` + internal 属性 `Comparer`（同程序集可见，见 properties/AssemblyInfo.cs 的
//    InternalsVisibleTo）。零值 = `VolumePathComparer` 的零值 = 逐字节精确比较，与 Go 的零值 `PathComparer{}` 一致。
// 2) `NativePathComparisonAvailable` 是 Go 的导出常量 → C# `public static readonly bool`（= NativePathFolding）。
// 3) Go 的 `(string, bool)` → C# 的 `bool Rebase(..., out string result)`（§3.2）。`Key` 的语义里
//    "非法 UTF-8 与含 NUL 的名字是不透明的，不是 Unicode 别名" 在 C# 侧对应"含孤立代理项或含 NUL"
//    （见 FsWatch.IsValidUtf8Equivalent 的说明）。
namespace TypeScript.FsWatch;

public static partial class FsWatch
{
    /// <summary>Go: <c>const NativePathComparisonAvailable = nativePathFolding</c>。</summary>
    public static readonly bool NativePathComparisonAvailable = NativePathFolding;
}

/// <summary>
/// Go: <c>type PathComparer struct</c>。描述被监听卷的文件名等价关系；零值逐字节精确比较。不可变、可共享。
/// </summary>
public readonly struct PathComparer
{
    private readonly VolumePathComparer _comparer;

    internal PathComparer(VolumePathComparer comparer)
    {
        _comparer = comparer;
    }

    /// <summary>Go: <c>c.comparer</c>（未导出字段）。</summary>
    internal VolumePathComparer Comparer => _comparer;

    /// <summary>
    /// Go: <c>func (c PathComparer) Key(path string) string</c>。返回**只用于 watch 比较**的 key，不是文件系统路径、
    /// 也不是编译器的身份标识。darwin 的原生比较器用与 watcher 相同的 CoreFoundation 折叠；其它比较器保留字节
    /// （含畸形 UTF-8）。
    /// </summary>
    public string Key(string path)
    {
        if (!_comparer.IgnoreCase || !FsWatch.NativePathFolding)
        {
            return path;
        }

        if (path.Contains('\0'))
        {
            return path;
        }

        var folded = FsWatch.FoldNativePath(path);
        if (folded.Length != 0)
        {
            return folded;
        }

        return path;
    }

    /// <summary>
    /// Go: <c>func (c PathComparer) Rebase(path, from, to string) (string, bool)</c>。替换匹配的目录前缀，同时保留
    /// 剩余事件路径的拼写与字节边界。
    /// </summary>
    public bool Rebase(string path, string from, string to, out string result)
    {
        if (!FsWatch.IsValidUtf8Equivalent(path) || !FsWatch.IsValidUtf8Equivalent(from)
            || path.Contains('\0') || from.Contains('\0'))
        {
            return default(VolumePathComparer).Rebase(path, from, to, out result);
        }

        return _comparer.Rebase(path, from, to, out result);
    }
}
