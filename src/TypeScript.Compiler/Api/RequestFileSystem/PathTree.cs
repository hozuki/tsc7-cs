// ported from: internal/api/requestfilesystem/pathtree.go:1-268
//
// 与 Go 的偏离（逐条说明）：
//
// 1) `requestEntry` 是只有标记方法的空接口（pathtree.go:22）→ C# 的空标记接口 `IRequestEntry`。
//    Go 里 `requestFile`/`requestDirectory` **自己**实现 `vfs.FileInfo`（`Info()` 返回自身指针），但 C# 的
//    `TypeScript.Vfs.FileInfo` 是 **sealed 数据类**（Vfs/Vfs.cs:70），不能被继承 ⇒ 改成"组合 + 按需构造"：
//    两个条目类各自暴露 `Info()` 造数据类（**首次访问时构造并缓存** —— Go 里 entry 本身就是 FileInfo，
//    `node.entry.(vfs.FileInfo)` 的身份稳定，pathtree_test.go 有 `any(info) == node.entry` 的断言，
//    缓存才能保持"同一个 entry 的 Stat 返回同一实例"；先例 OverlayFs.cs:590-615 的 `overlayFS.Stat`
//    每次新建，是因为 Go 侧那里本来就没有身份要求）。于是 `node.entry.(vfs.FileInfo)`（requestfilesystem.go:365）
//    落成扩展方法 `FileInfo()`（requestSymlink 不实现 → null），`info.IsDir()` 落成扩展方法 `IsDir()`
//    （只有 `RequestDirectoryEntry` 为 true）。
//    注意：`requestFile` 的 `FileName`/`Content` 是 **internal 字段**（Go 的未导出字段），因为
//    requestfilesystem.go 与 filechanges.go 两个分片都要读它们。
// 2) Go 的 `Mode()`/`Type()`/`Sys()` 在 `Vfs.FileInfo` 里没有对应字段（见 Vfs/Vfs.cs 文件头）：0o444 与
//    `ModeDir|0o555` 的权限位、`Type()` 的 symlink 位在本包不建模（requestSymlink 本来也不实现 FileInfo），
//    `Sys()` 恒 nil 同样丢弃。`var _ vfs.DirEntry = (*requestFile)(nil)`（pathtree.go:66-71）不移植：
//    本包没有任何调用点把这两个条目当 DirEntry 用（GetAccessibleEntries 只回名字）。
//    `Info() (fs.FileInfo, error)` 的 error 恒为 nil ⇒ C# 直接返回 FileInfo，不抛。
// 3) Go 的 `Size()` 是 `int64(len(file.content))`（UTF-8 字节数，pathtree.go:47）；C# 的 `string.Length` 是
//    UTF-16 码元数 —— 本仓虚拟 FS 统一按后者（先例 VfsTest.MapFS.cs:291 `Size = entry.Data.Length`）。
// 4) `ModTime()` 的零值 `time.Time{}` → `default(DateTime)`（Vfs/Vfs.cs 已把时间统一钉死 UTC）。
// 5) `requestFallbackInherit/Allowed/Missing` → `enum RequestFallback { Inherit, Allowed, Missing }`（去前缀）。
// 6) Go 的 requestPathNode 方法允许 nil 接收者（`if node == nil`），C# 不能对 null 调实例方法 ⇒
//    `lookup`/`walkSymlinks`/`entries`/`firstSymlink`/`containsFileAncestor` 落成 **static**（首参可空），
//    分支逐条对应 Go 的 nil 检查；`ensure`/`replacesSubtree` 在 Go 里从不用 nil 接收者调用 ⇒ 保持实例方法。
// 7) `composeRequestPaths` 的 `overlay` 在 C# 声明为不可空：只有两个调用点，传入的 `paths` 恒非 null
//    （`newRequestFileSystemWorker` 里新建、`applyTo` 里接收 compose 的结果）⇒ Go 的
//    `if overlay == nil { return base }`（pathtree.go:172-174）不可能触发，故省略该分支；`base` 仍可空。
// 8) `mergeEntries` 的 `equal func(string, string) bool` 参数换成 `bool caseSensitive`：本文件与
//    requestfilesystem.go 的调用点传的都是"按 `GetCanonicalFileName` 比较"的同一形状，传 bool 既等价
//    又不产生闭包分配（Go 侧每次调用都要分配一个闭包）。
// 9) Go 允许 `for name := range result.Symlinks { delete(...) }`（map 边遍历边删）；C# 的 HashSet 不允许
//    遍历中修改 ⇒ 先 `ToArray()` 快照再删（被删的名字不会再被访问，结果集合相同）。
// 10) `strings.HasPrefix(s, p)` → `s.StartsWith(p, StringComparison.Ordinal)`（Go 的字符串比较是逐字节序）。
// 11) `tspath.Path` 是 `readonly record struct`：Go 用 `matchPath == ""` 表示"没找到"（`Path` 的零值），
//     C# 的 `default(Path).Value` 是 null，故 `firstSymlink` 的"没找到"一律判第二个返回值 `Symlink is null`
//     （Go 里两者同时有效/无效，语义等价）。
using TypeScript.TSPath;
using TypeScript.Vfs;
using static TypeScript.TSPath.TSPath;
using Path = TypeScript.TSPath.Path;
using VfsFileInfo = TypeScript.Vfs.FileInfo;

namespace TypeScript.Api.RequestFileSystem;

/// <summary>Go 的 <c>requestFallback</c>（pathtree.go:14-20）。</summary>
internal enum RequestFallback
{
    /// <summary>Go: <c>requestFallbackInherit</c>（零值：继承上层）。</summary>
    Inherit,

    /// <summary>Go: <c>requestFallbackAllowed</c>。</summary>
    Allowed,

    /// <summary>Go: <c>requestFallbackMissing</c>。</summary>
    Missing,
}

/// <summary>Go 的 <c>type requestEntry interface { isRequestEntry() }</c>（pathtree.go:22-24）。</summary>
internal interface IRequestEntry
{
}

/// <summary>Go 的 <c>type requestFile struct</c>（pathtree.go:26-29，见文件头第 1、2 条）。</summary>
internal sealed class RequestFileEntry : IRequestEntry
{
    /// <summary>Go: <c>requestFile.fileName</c>（绝对路径）。</summary>
    internal string FileName = "";

    /// <summary>Go: <c>requestFile.content</c>。</summary>
    internal string Content = "";

    /// <summary><see cref="Info"/> 的缓存（Go 里 entry 自身就是 FileInfo，恒返回同一实例）。</summary>
    private VfsFileInfo? _info;

    /// <summary>Go 的结构体字面量 <c>&amp;requestFile{fileName: …}</c> 用（statFileSystem，requestfilesystem.go:706）。</summary>
    internal RequestFileEntry()
    {
    }

    /// <summary>Go 的结构体字面量 <c>&amp;requestFile{fileName: …, content: …}</c> 用（requestfilesystem.go:147）。</summary>
    internal RequestFileEntry(string fileName, string content)
    {
        FileName = fileName;
        Content = content;
    }

    /// <summary>Go: <c>func (file *requestFile) Info() (fs.FileInfo, error)</c>（pathtree.go:53）。
    /// 首次访问时构造并**缓存**：Go 里 entry 本身就是 FileInfo（`node.entry.(vfs.FileInfo)` 的指针身份稳定，
    /// 见 pathtree_test.go 的 `assert.Assert(t, any(info) == node.entry)`），因此同一个 entry 的每次 Stat 必须
    /// 返回同一个实例；entry 构造后不再改字段（见文件头第 1 条），缓存不会过期。</summary>
    internal VfsFileInfo Info()
    {
        return _info ??= new VfsFileInfo
        {
            Name = GetBaseFileName(FileName),
            FullName = FileName,
            IsDirectory = false,
            IsSymlink = false,
            Size = Content.Length,
            ModifiedTimeUtc = default,
        };
    }
}

/// <summary>Go 的 <c>type requestSymlink struct</c>（pathtree.go:31-35）。</summary>
internal sealed class RequestSymlinkEntry : IRequestEntry
{
    /// <summary>Go: <c>requestSymlink.linkName</c>（绝对路径）。</summary>
    internal string LinkName = "";

    /// <summary>Go: <c>requestSymlink.target</c>（绝对路径）。</summary>
    internal string Target = "";

    /// <summary>Go: <c>requestSymlink.host</c>：目标是否走宿主文件系统。</summary>
    internal bool Host;
}

/// <summary>Go 的 <c>type requestDirectory struct</c>（pathtree.go:37-40）。</summary>
internal sealed class RequestDirectoryEntry : IRequestEntry
{
    /// <summary>Go: <c>requestDirectory.directoryName</c>（绝对路径）。</summary>
    internal string DirectoryName = "";

    /// <summary>Go: <c>requestDirectory.listing *vfs.Entries</c>：显式列举结果；<c>null</c> 表示由子节点推导。</summary>
    internal Entries? Listing;

    /// <summary>Go 的结构体字面量 <c>&amp;requestDirectory{directoryName: …}</c> 用。</summary>
    internal RequestDirectoryEntry()
    {
    }

    /// <summary>Go 的结构体字面量 <c>&amp;requestDirectory{directoryName: …}</c> 用。</summary>
    internal RequestDirectoryEntry(string directoryName)
    {
        DirectoryName = directoryName;
    }

    /// <summary>Go 的结构体字面量 <c>&amp;requestDirectory{directoryName: …, listing: …}</c> 用。</summary>
    internal RequestDirectoryEntry(string directoryName, Entries? listing)
    {
        DirectoryName = directoryName;
        Listing = listing;
    }

    /// <summary>Go: <c>func (directory *requestDirectory) Info() (fs.FileInfo, error)</c>（pathtree.go:64）。
    /// 同 <see cref="RequestFileEntry.Info"/>：实例缓存，保持 Go 的指针身份语义。</summary>
    internal VfsFileInfo Info()
    {
        return _info ??= new VfsFileInfo
        {
            Name = GetBaseFileName(DirectoryName),
            FullName = DirectoryName,
            IsDirectory = true,
            IsSymlink = false,
            Size = 0,
            ModifiedTimeUtc = default,
        };
    }

    /// <summary><see cref="Info"/> 的缓存（Go 里 entry 自身就是 FileInfo，恒返回同一实例）。</summary>
    private VfsFileInfo? _info;
}

/// <summary>Go 里 `node.entry.(vfs.FileInfo)`（requestfilesystem.go:365）与 `info.IsDir()` 的落点：
/// `requestFile`/`requestDirectory` 在 Go 里自己实现 `vfs.FileInfo`，C# 换成按需构造（见文件头第 1 条）。</summary>
internal static class RequestEntryExtensions
{
    /// <summary>Go: <c>info.IsDir()</c>（pathtree.go:50/61，只有 requestDirectory 为 true）。</summary>
    internal static bool IsDir(this IRequestEntry? entry)
    {
        return entry is RequestDirectoryEntry;
    }

    /// <summary>Go: <c>info, _ := node.entry.(vfs.FileInfo)</c>（requestfilesystem.go:365）：
    /// requestSymlink（以及无 entry）断言失败 → <c>null</c>。</summary>
    internal static VfsFileInfo? FileInfo(this IRequestEntry? entry)
    {
        return entry switch
        {
            RequestFileEntry file => file.Info(),
            RequestDirectoryEntry directory => directory.Info(),
            _ => null,
        };
    }
}

/// <summary>Go 的 <c>type requestPathNode struct</c>（pathtree.go:73-78）。</summary>
internal sealed class RequestPathNode
{
    /// <summary>Go: <c>entry requestEntry</c>。</summary>
    internal IRequestEntry? Entry;

    /// <summary>Go: <c>fallback requestFallback</c>。</summary>
    internal RequestFallback Fallback;

    /// <summary>Go: <c>children map[tspath.Path]*requestPathNode</c>；<c>null</c> 即 Go 的 nil map（读空、写时建）。
    /// 值不会是 null（`ensure`/`composeRequestPaths` 只写入新建的节点），所以类型不可空。</summary>
    internal Dictionary<Path, RequestPathNode>? Children;

    /// <summary>Go: <c>hasSymlinks bool</c>（只在 composeRequestPaths 里重算）。</summary>
    internal bool HasSymlinks;

    /// <summary>Go: <c>func (node *requestPathNode) replacesSubtree() bool</c>（pathtree.go:80-86）。</summary>
    internal bool ReplacesSubtree()
    {
        return Entry is RequestFileEntry or RequestSymlinkEntry;
    }

    /// <summary>Go: <c>func (node *requestPathNode) ensure(path tspath.Path) *requestPathNode</c>（pathtree.go:102-115）。</summary>
    internal RequestPathNode Ensure(Path path)
    {
        var node = this;
        foreach (var ancestor in PathTree.RequestPathAncestors(path))
        {
            node.Children ??= [];
            if (!node.Children.TryGetValue(ancestor, out var child))
            {
                child = new RequestPathNode();
                node.Children[ancestor] = child;
            }

            node = child;
        }

        return node;
    }

    /// <summary>Go: <c>func (node *requestPathNode) lookup(path tspath.Path) (*requestPathNode, requestFallback)</c>
    /// （pathtree.go:117-132，见文件头第 6 条：接收者可能为 nil ⇒ 静态方法）。</summary>
    internal static (RequestPathNode? Node, RequestFallback Fallback) Lookup(RequestPathNode? node, Path path)
    {
        var fallback = RequestFallback.Inherit;
        foreach (var ancestor in PathTree.RequestPathAncestors(path))
        {
            if (node is null)
            {
                break;
            }

            if (node.Fallback != RequestFallback.Inherit)
            {
                fallback = node.Fallback;
            }

            node = LookupChild(node, ancestor);
        }

        if (node is not null && node.Fallback != RequestFallback.Inherit)
        {
            fallback = node.Fallback;
        }

        return (node, fallback);
    }

    /// <summary>Go: <c>func (node *requestPathNode) walkSymlinks(visit func(tspath.Path, *requestSymlink))</c>
    /// （pathtree.go:134-144）。</summary>
    internal static void WalkSymlinks(RequestPathNode? node, Action<Path, RequestSymlinkEntry> visit)
    {
        if (node is null || !node.HasSymlinks || node.Children is null)
        {
            return;
        }

        foreach (var (path, child) in node.Children)
        {
            if (child.Entry is RequestSymlinkEntry symlink)
            {
                visit(path, symlink);
            }

            WalkSymlinks(child, visit);
        }
    }

    /// <summary>Go: <c>func (node *requestPathNode) entries() (vfs.Entries, bool)</c>（pathtree.go:146-169）。</summary>
    internal static (Vfs.Entries Entries, bool Ok) Entries(RequestPathNode? node)
    {
        if (node is null || node.Entry is not RequestDirectoryEntry directory)
        {
            return (new Vfs.Entries([], []), false);
        }

        if (directory.Listing is { } listing)
        {
            return (RequestFileSystemImpl.CloneEntries(listing), true);
        }

        var files = new List<string>();
        var directories = new List<string>();
        if (node.Children is not null)
        {
            foreach (var child in node.Children.Values)
            {
                switch (child.Entry)
                {
                    case RequestFileEntry file:
                        files.Add(GetBaseFileName(file.FileName));
                        break;
                    case RequestDirectoryEntry childDirectory:
                        directories.Add(GetBaseFileName(childDirectory.DirectoryName));
                        break;
                }
            }
        }

        files.Sort(StringComparer.Ordinal);
        directories.Sort(StringComparer.Ordinal);
        return (new Vfs.Entries([.. files], [.. directories]), true);
    }

    /// <summary>Go: <c>func (node *requestPathNode) firstSymlink(path tspath.Path) (tspath.Path, *requestSymlink)</c>
    /// （pathtree.go:236-249，见文件头第 6、11 条）。</summary>
    internal static (Path SymlinkPath, RequestSymlinkEntry? Symlink) FirstSymlink(RequestPathNode? node, Path path)
    {
        foreach (var ancestor in PathTree.RequestPathAncestors(path))
        {
            if (node is null)
            {
                break;
            }

            node = LookupChild(node, ancestor);
            if (node?.Entry is RequestSymlinkEntry symlink)
            {
                return (ancestor, symlink);
            }
        }

        return (default, null);
    }

    /// <summary>Go: <c>func (node *requestPathNode) containsFileAncestor(path tspath.Path) bool</c>（pathtree.go:251-264）。</summary>
    internal static bool ContainsFileAncestor(RequestPathNode? node, Path path)
    {
        foreach (var ancestor in PathTree.RequestPathAncestors(path))
        {
            if (node is null)
            {
                return false;
            }

            node = LookupChild(node, ancestor);
            if (ancestor != path && node?.Entry is RequestFileEntry)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Go 的 <c>node.children[ancestor]</c>：nil map 或缺键都得到 nil。</summary>
    private static RequestPathNode? LookupChild(RequestPathNode node, Path ancestor)
    {
        if (node.Children is null || !node.Children.TryGetValue(ancestor, out var child))
        {
            return null;
        }

        return child;
    }
}

/// <summary>Go 的 <c>pathtree.go</c> 包级函数（见文件头第 6、7、8 条）。</summary>
internal static class PathTree
{
    /// <summary>Go: <c>func requestPathAncestors(path tspath.Path) []tspath.Path</c>（pathtree.go:88-100）：
    /// 从根到 path 自身（含），倒序输出前先追加后 reverse。</summary>
    internal static Path[] RequestPathAncestors(Path path)
    {
        var paths = new List<Path>();
        while (true)
        {
            paths.Add(path);
            var parent = new Path(GetDirectoryPath(path.Value));
            if (parent == path)
            {
                break;
            }

            path = parent;
        }

        paths.Reverse();
        return [.. paths];
    }

    /// <summary>Go: <c>func composeRequestPaths(base *requestPathNode, overlay *requestPathNode, fallback requestFallback,
    /// caseSensitive bool) *requestPathNode</c>（pathtree.go:171-234）。</summary>
    internal static RequestPathNode ComposeRequestPaths(
        RequestPathNode? baseNode,
        RequestPathNode overlay,
        RequestFallback fallback,
        bool caseSensitive)
    {
        if (overlay.Fallback != RequestFallback.Inherit)
        {
            fallback = overlay.Fallback;
            baseNode = null;
        }

        if (overlay.ReplacesSubtree())
        {
            baseNode = null;
        }

        // Go 的 `result := requestPathNode{}; if base != nil { result = *base }`（浅拷贝，children 由 maps.Clone 复制）。
        var result = new RequestPathNode();
        if (baseNode is not null)
        {
            result.Entry = baseNode.Entry;
            result.Fallback = baseNode.Fallback;
            result.HasSymlinks = baseNode.HasSymlinks;
            result.Children = baseNode.Children is null ? null : new Dictionary<Path, RequestPathNode>(baseNode.Children);
        }

        if (overlay.Fallback != RequestFallback.Inherit || overlay.ReplacesSubtree())
        {
            result.Fallback = fallback;
        }

        var previousDirectory = result.Entry as RequestDirectoryEntry;
        var overlayDirectory = overlay.Entry as RequestDirectoryEntry;
        if (overlay.Entry is not null)
        {
            result.Entry = overlay.Entry;
            if (overlayDirectory is not null && overlayDirectory.Listing is null && previousDirectory is not null)
            {
                result.Entry = new RequestDirectoryEntry(overlayDirectory.DirectoryName, previousDirectory.Listing);
            }
        }

        if (overlay.Children is not null)
        {
            foreach (var (path, child) in overlay.Children)
            {
                result.Children ??= [];
                var baseChild = result.Children.TryGetValue(path, out var existing) ? existing : null;
                result.Children[path] = ComposeRequestPaths(baseChild, child, fallback, caseSensitive);
            }
        }

        if (result.Entry is RequestDirectoryEntry directory
            && directory.Listing is { } listing
            && (overlayDirectory is null || overlayDirectory.Listing is null))
        {
            var entries = RequestFileSystemImpl.CloneEntries(listing);
            if (overlay.Children is not null)
            {
                foreach (var (path, child) in overlay.Children)
                {
                    var name = GetBaseFileName(path.Value);
                    if (child.Fallback == RequestFallback.Missing || child.ReplacesSubtree())
                    {
                        entries.Files = DeleteEqualNames(entries.Files, name, caseSensitive);
                        entries.Directories = DeleteEqualNames(entries.Directories, name, caseSensitive);
                        var symlinks = entries.Symlinks;
                        if (symlinks is not null)
                        {
                            foreach (var entry in symlinks.ToArray())
                            {
                                if (EqualNames(entry, name, caseSensitive))
                                {
                                    symlinks.Remove(entry);
                                }
                            }
                        }
                    }

                    switch (child.Entry)
                    {
                        case RequestFileEntry file:
                            entries = RequestFileSystemImpl.MergeEntries(
                                entries,
                                new Entries([GetBaseFileName(file.FileName)], []),
                                caseSensitive);
                            break;
                        case RequestDirectoryEntry childDirectory:
                            entries = RequestFileSystemImpl.MergeEntries(
                                entries,
                                new Entries([], [GetBaseFileName(childDirectory.DirectoryName)]),
                                caseSensitive);
                            break;
                    }
                }
            }

            result.Entry = new RequestDirectoryEntry(directory.DirectoryName, entries);
        }

        result.HasSymlinks = result.Entry is RequestSymlinkEntry;
        if (result.Children is not null)
        {
            foreach (var child in result.Children.Values)
            {
                result.HasSymlinks = result.HasSymlinks || child.HasSymlinks;
            }
        }

        return result;
    }

    /// <summary>Go: <c>func requestPathContains(parent tspath.Path, path tspath.Path) bool</c>（pathtree.go:266-268）。</summary>
    internal static bool RequestPathContains(Path parent, Path path)
    {
        return path == parent || path.Value.StartsWith(EnsureTrailingDirectorySeparator(parent.Value), StringComparison.Ordinal);
    }

    /// <summary>Go 的 <c>equal</c> 闭包：`tspath.GetCanonicalFileName(left, caseSensitive) == tspath.GetCanonicalFileName(right, caseSensitive)`
    /// （pathtree.go:190-192，见文件头第 8 条）。</summary>
    internal static bool EqualNames(string left, string right, bool caseSensitive)
    {
        return GetCanonicalFileName(left, caseSensitive) == GetCanonicalFileName(right, caseSensitive);
    }

    /// <summary>Go 的 <c>slices.DeleteFunc(values, func(entry string) bool { return equal(entry, name) })</c>
    /// （pathtree.go:195-201）：原地压缩后返回新长度的切片。</summary>
    internal static string[] DeleteEqualNames(string[] values, string name, bool caseSensitive)
    {
        var write = 0;
        for (var read = 0; read < values.Length; read++)
        {
            if (!EqualNames(values[read], name, caseSensitive))
            {
                values[write++] = values[read];
            }
        }

        if (write == values.Length)
        {
            return values;
        }

        var result = new string[write];
        Array.Copy(values, result, write);
        return result;
    }
}
