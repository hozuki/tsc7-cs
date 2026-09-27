// ported from: internal/vfs/walkdir.go
//
// WalkDir 的回调在 Go 里返回 error，其中 fs.SkipDir/fs.SkipAll 是**控制流**而不是错误。C# 侧用
// WalkDirResult 承接（Ok/SkipDir/SkipAll/FromError），最终"真正的错误"以异常抛出（Go 的返回值 → throw，
// 与 docs/architecture.md §3.2 的 (T,error) 约定一致）。
//
// 偏离：Go 的 walkDirEntry（未导出，实现 fs.DirEntry）+ walkDirFileInfo（符号链接的"只有 name+mode"的
// FileInfo）折进 TypeScript.Vfs.DirEntry —— 未导出类型不单独建类；符号链接条目的 Info() 由
// Vfs.SymlinkInfo 造出等价的 FileInfo（Name + IsSymlink，Size 0、ModTime 零值），非符号链接条目则惰性
// 调 FS.Stat（= Go 的 walkDirEntry.Info）。
namespace TypeScript.Vfs;

using System.Runtime.ExceptionServices;
using TypeScript.TSPath;

using static TypeScript.TSPath.TSPath;

/// <summary>Go: <c>fs.WalkDirFunc</c> 的返回值（Go 的 <c>error</c>）。</summary>
public readonly struct WalkDirResult
{
    private readonly byte _kind;
    private readonly Exception? _error;

    private WalkDirResult(byte kind, Exception? error)
    {
        _kind = kind;
        _error = error;
    }

    /// <summary>Go: <c>return nil</c>。</summary>
    public static WalkDirResult Ok => default;

    /// <summary>Go: <c>return fs.SkipDir</c>：跳过当前目录的内容（在文件上返回时会向上传播）。</summary>
    public static WalkDirResult SkipDir => new(1, null);

    /// <summary>Go: <c>return fs.SkipAll</c>：停止整个遍历（WalkDir 视作成功）。</summary>
    public static WalkDirResult SkipAll => new(2, null);

    /// <summary>Go: <c>return err</c>：真正的错误，会原样抛出。</summary>
    public static WalkDirResult FromError(Exception error) => new(3, error);

    /// <summary>Go 的 <c>err != nil</c>。</summary>
    public bool IsError => _kind != 0;

    /// <summary>Go 的 <c>errors.Is(err, fs.SkipDir)</c>。</summary>
    public bool IsSkipDir => _kind == 1;

    /// <summary>Go 的 <c>errors.Is(err, fs.SkipAll)</c>。</summary>
    public bool IsSkipAll => _kind == 2;

    /// <summary>Go: 非哨兵错误时的 <c>err</c>。</summary>
    public Exception? Error => _error;
}

/// <summary>Go: <c>fs.WalkDirFunc</c>。</summary>
public delegate WalkDirResult WalkDirFunc(string path, DirEntry? entry, Exception? err);

public static partial class Vfs
{
    /// <summary>
    /// Go: <c>WalkDir(fileSystem, root, walkFn) error</c>：对 root 及其所有可达后代按字典序调用 walkFn；
    /// 符号链接会被报告但不会被跟随。目录读取失败与空目录无法区分（<see cref="FS.GetAccessibleEntries"/>
    /// 不返回错误）。错误的 FileInfo（符号链接）只带名字，因为 FS 不提供 lstat。
    /// </summary>
    public static void WalkDir(FS fileSystem, string root, WalkDirFunc walkFn)
    {
        var rootInfo = fileSystem.Stat(root);
        if (rootInfo == null)
        {
            ThrowIfError(NormalizeWalkDirError(walkFn(root, null, new FileNotFoundException("file does not exist"))));
            return;
        }

        var useCaseSensitiveFileNames = fileSystem.UseCaseSensitiveFileNames();
        var rootPrefix = root[..GetRootLength(root)];
        bool sameRoot(string path)
        {
            var pathRootLength = GetRootLength(path);
            return pathRootLength == rootPrefix.Length
                && ComparePaths(path[..pathRootLength], rootPrefix, new ComparePathsOptions(useCaseSensitiveFileNames)) == 0;
        }

        bool equivalent(string left, string right)
            => ComparePaths(left, right, new ComparePathsOptions(useCaseSensitiveFileNames)) == 0;

        string canonicalize(string path) => GetCanonicalFileName(NormalizePath(path), useCaseSensitiveFileNames);

        var visited = new HashSet<string>();

        WalkDirResult visit(string path, DirEntry entry, string realpath)
        {
            if (entry.IsDirectory)
            {
                var canonicalRealpath = canonicalize(realpath);
                if (!visited.Add(canonicalRealpath))
                {
                    return WalkDirResult.Ok;
                }
            }

            var result = walkFn(path, entry, null);
            if (result.IsError)
            {
                if (result.IsSkipDir && entry.IsDirectory)
                {
                    return WalkDirResult.Ok;
                }

                return result;
            }

            if (!entry.IsDirectory)
            {
                return WalkDirResult.Ok;
            }

            var entries = fileSystem.GetAccessibleEntries(path);
            var directories = new HashSet<string>(entries.Directories);
            // Go: names := append(slices.Clone(entries.Directories), entries.Files...); slices.Sort(names)
            var names = new string[entries.Directories.Length + entries.Files.Length];
            entries.Directories.CopyTo(names, 0);
            entries.Files.CopyTo(names, entries.Directories.Length);
            Array.Sort(names, StringComparer.Ordinal);

            foreach (var name in names)
            {
                var childPath = CombinePaths(path, name);
                if (!sameRoot(childPath))
                {
                    continue;
                }

                var isDir = directories.Contains(name);
                var childRealpath = "";
                var isSymlink = false;
                if (entries.Symlinks != null)
                {
                    isSymlink = entries.Symlinks.Contains(name);
                    if (!isSymlink && isDir)
                    {
                        childRealpath = CombinePaths(realpath, name);
                    }
                }
                else
                {
                    childRealpath = fileSystem.Realpath(childPath);
                    isSymlink = !equivalent(childRealpath, CombinePaths(realpath, name));
                }

                if (isSymlink)
                {
                    // Go: mode = fs.ModeSymlink，于是 Mode().IsDir() 为 false。
                    isDir = false;
                }

                var childEntry = isSymlink
                    ? new DirEntry(name, false, true, SymlinkInfo(name, childPath))
                    : new DirEntry(name, isDir, false, () => fileSystem.Stat(childPath) ?? throw new FileNotFoundException("file does not exist"));
                if (!isDir)
                {
                    var childResult = visit(childPath, childEntry, "");
                    if (childResult.IsError)
                    {
                        return childResult.IsSkipDir ? WalkDirResult.Ok : childResult;
                    }

                    continue;
                }

                if (childRealpath.Length == 0)
                {
                    childRealpath = fileSystem.Realpath(childPath);
                }

                var dirResult = visit(childPath, childEntry, childRealpath);
                if (dirResult.IsError)
                {
                    return dirResult.IsSkipDir ? WalkDirResult.Ok : dirResult;
                }
            }

            return WalkDirResult.Ok;
        }

        var rootEntry = DirEntry.FromFileInfo(rootInfo);
        var rootRealpath = fileSystem.Realpath(root);
        if (GetRootLength(root) != root.Length)
        {
            var parent = GetDirectoryPath(root);
            var expectedRealpath = CombinePaths(fileSystem.Realpath(parent), GetBaseFileName(root));
            if (!equivalent(rootRealpath, expectedRealpath))
            {
                var baseName = GetBaseFileName(root);
                rootEntry = new DirEntry(baseName, false, true, SymlinkInfo(baseName, root));
            }
        }

        ThrowIfError(NormalizeWalkDirError(visit(root, rootEntry, rootRealpath)));
    }

    /// <summary>Go 的 <c>walkDirFileInfo</c>：符号链接条目的 FileInfo 只带名字与 mode（Size 0、ModTime 零值），
    /// 因为 FS 不提供 lstat（Go 的文件头注释）。C# 的 FileInfo 是我们自己的类，所以可以逐字复刻。</summary>
    private static FileInfo SymlinkInfo(string name, string path)
    {
        return new FileInfo { Name = name, FullName = path, IsSymlink = true };
    }

    /// <summary>Go: <c>normalizeWalkDirError</c>（SkipDir/SkipAll 视作成功）。</summary>
    private static WalkDirResult NormalizeWalkDirError(WalkDirResult result)
    {
        return result.IsSkipDir || result.IsSkipAll ? WalkDirResult.Ok : result;
    }

    private static void ThrowIfError(WalkDirResult result)
    {
        if (result.Error != null)
        {
            ExceptionDispatchInfo.Capture(result.Error).Throw();
        }
    }
}
