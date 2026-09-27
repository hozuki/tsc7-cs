// ported from: internal/vfs/iovfs/iofs.go
//
// 偏离：
//   - Go 的 <c>fs.FS</c> → <see cref="IIoFs"/>（见 Vfs.cs 文件头）。
//   - Go 的 <c>fs.FileMode</c> 权限参数（WriteFile/AppendFile 的 0o666、MkdirAll 的 0o777）不移植：
//     C# 的权限模型是 UnixFileMode/ACL，内存 FS 不需要它（Go 侧权限只用于 MapFS 的 Mode）。
//   - Go 的 <c>(string, error)</c> → <c>string?</c>（null = 失败）；<c>panic</c> → <c>Debug.Fail</c>。
//   - Go 的 <c>fs.Sub</c>（stdlib）→ 本文件的私有 <see cref="SubFs"/>（只做前缀映射，IIoFs 只有三个方法）。
namespace TypeScript.Vfs.Iovfs;

using TypeScript.TSPath;
using TypeScript.Vfs;
using TypeScript.Vfs.Internal;

using static TypeScript.TSPath.TSPath;

/// <summary>Go 的 <c>iovfs.RealpathFS</c>。</summary>
public interface RealpathFS : IIoFs
{
    /// <summary>Go: <c>Realpath(path) (string, error)</c>；失败返回 <c>null</c>。</summary>
    string? Realpath(string path);
}

/// <summary>Go 的 <c>iovfs.WritableFS</c>（权限参数见文件头）。</summary>
public interface WritableFS : IIoFs
{
    /// <summary>Go: <c>WriteFile(path, data, perm) error</c>。</summary>
    void WriteFile(string path, string data);

    /// <summary>Go: <c>AppendFile(path, data, perm) error</c>。</summary>
    void AppendFile(string path, string data);

    /// <summary>Go: <c>MkdirAll(path, perm) error</c>。</summary>
    void MkdirAll(string path);

    /// <summary>Go: <c>Remove(path) error</c>：删除 path 及其全部内容。</summary>
    void Remove(string path);

    /// <summary>Go: <c>Chtimes(path, aTime, mTime) error</c>。</summary>
    void Chtimes(string path, DateTime accessTime, DateTime modificationTime);
}

/// <summary>Go 的 <c>iovfs.FsWithSys</c>。</summary>
public interface FsWithSys : FS
{
    /// <summary>Go: <c>FSys() fs.FS</c>（拿回底层的原始 FS，如 <c>VfsTest.MapFS</c>）。</summary>
    IIoFs FSys();
}

/// <summary>Go 的 <c>iovfs</c> 包级函数。</summary>
public static partial class Iovfs
{
    /// <summary>
    /// Go: <c>From(fsys fs.FS, useCaseSensitiveFileNames bool) FsWithSys</c>。
    /// 对 <c>c:/foo/bar</c> 这样的路径，fsys 被当作以 <c>/</c> 为根、路径是 <c>/c:/foo/bar</c>。
    /// fsys 实现 <see cref="RealpathFS"/> 时用它实现 Realpath，实现 <see cref="WritableFS"/> 时用它实现写入。
    /// 本函数不处理大小写不敏感：需要时由传入的 fsys 自己处理（测试用 <c>VfsTest.FromMap</c>）。
    /// </summary>
    public static FsWithSys From(IIoFs fsys, bool useCaseSensitiveFileNames)
    {
        Func<string, string?> realpath;
        if (fsys is RealpathFS realpathFs)
        {
            realpath = path =>
            {
                var hadSlash = path.StartsWith(TSPath.DirectorySeparator);
                var rest = hadSlash ? path[1..] : path;
                var rp = realpathFs.Realpath(rest);
                return rp == null ? null : hadSlash ? "/" + rp : rp;
            };
        }
        else
        {
            realpath = static path => path;
        }

        Action<string, string> writeFile;
        Action<string, string> appendFile;
        Action<string> mkdirAll;
        Action<string> remove;
        Action<string, DateTime, DateTime> chtimes;
        if (fsys is WritableFS writableFs)
        {
            writeFile = (path, content) => writableFs.WriteFile(CutPrefixSlash(path), content);
            appendFile = (path, content) => writableFs.AppendFile(CutPrefixSlash(path), content);
            mkdirAll = path => writableFs.MkdirAll(CutPrefixSlash(path));
            remove = path => writableFs.Remove(CutPrefixSlash(path));
            chtimes = (path, aTime, mTime) => writableFs.Chtimes(CutPrefixSlash(path), aTime, mTime);
        }
        else
        {
            writeFile = static (_, _) => Debug.Fail("writeFile not supported");
            appendFile = static (_, _) => Debug.Fail("appendFile not supported");
            mkdirAll = static _ => Debug.Fail("mkdirAll not supported");
            remove = static _ => Debug.Fail("remove not supported");
            chtimes = static (_, _, _) => Debug.Fail("chtimes not supported");
        }

        return new IoFS(
            new Common(root =>
            {
                if (root == "/")
                {
                    return fsys;
                }

                var p = RemoveTrailingDirectorySeparator(root);
                var sub = SubFs.Create(fsys, p);
                if (sub == null)
                {
                    if (IsUrl(root))
                    {
                        return null;
                    }

                    Debug.Fail($"vfs: failed to create sub file system for \"{p}\"");
                }

                return sub;
            }),
            useCaseSensitiveFileNames,
            realpath,
            writeFile,
            appendFile,
            mkdirAll,
            remove,
            chtimes,
            fsys);
    }

    /// <summary>Go: <c>strings.CutPrefix(path, "/")</c>（只去掉一个前导 '/'）。</summary>
    private static string CutPrefixSlash(string path)
    {
        return path.StartsWith(TSPath.DirectorySeparator) ? path[1..] : path;
    }

    /// <summary>Go 的 <c>ioFS</c>（未导出）。</summary>
    private sealed class IoFS : FsWithSys
    {
        private readonly Common _common;
        private readonly bool _useCaseSensitiveFileNames;
        private readonly Func<string, string?> _realpath;
        private readonly Action<string, string> _writeFile;
        private readonly Action<string, string> _appendFile;
        private readonly Action<string> _mkdirAll;
        private readonly Action<string> _remove;
        private readonly Action<string, DateTime, DateTime> _chtimes;
        private readonly IIoFs _fsys;

        internal IoFS(
            Common common,
            bool useCaseSensitiveFileNames,
            Func<string, string?> realpath,
            Action<string, string> writeFile,
            Action<string, string> appendFile,
            Action<string> mkdirAll,
            Action<string> remove,
            Action<string, DateTime, DateTime> chtimes,
            IIoFs fsys)
        {
            _common = common;
            _useCaseSensitiveFileNames = useCaseSensitiveFileNames;
            _realpath = realpath;
            _writeFile = writeFile;
            _appendFile = appendFile;
            _mkdirAll = mkdirAll;
            _remove = remove;
            _chtimes = chtimes;
            _fsys = fsys;
        }

        public bool UseCaseSensitiveFileNames()
        {
            return _useCaseSensitiveFileNames;
        }

        public bool DirectoryExists(string path)
        {
            return _common.DirectoryExists(path);
        }

        public bool FileExists(string path)
        {
            return _common.FileExists(path);
        }

        public Entries GetAccessibleEntries(string path)
        {
            return _common.GetAccessibleEntries(path);
        }

        public FileInfo? Stat(string path)
        {
            _ = Internal.RootLength(path); // 断言 path 是绝对路径
            return _common.Stat(path);
        }

        public bool TryReadFile(string path, out string contents)
        {
            return _common.TryReadFile(path, out contents);
        }

        public void Remove(string path)
        {
            _ = Internal.RootLength(path); // 断言 path 是绝对路径
            _remove(path);
        }

        public void Chtimes(string path, DateTime accessTime, DateTime modificationTime)
        {
            _ = Internal.RootLength(path); // 断言 path 是绝对路径
            _chtimes(path, accessTime, modificationTime);
        }

        public string Realpath(string path)
        {
            var (root, rest) = Internal.SplitPath(path);
            // SplitPath 把路径规范化成部件（"c:/foo/bar" → "c:/", "foo/bar"），拼回去再调 realpath。
            var realpath = _realpath(root + rest);
            return realpath ?? path;
        }

        public void WriteFile(string path, string content)
        {
            WriteFileEnsuringDir(path, content, _writeFile);
        }

        public void AppendFile(string path, string content)
        {
            WriteFileEnsuringDir(path, content, _appendFile);
        }

        public IIoFs FSys()
        {
            return _fsys;
        }

        private void WriteFileEnsuringDir(string path, string content, Action<string, string> write)
        {
            _ = Internal.RootLength(path); // 断言 path 是绝对路径
            try
            {
                write(path, content);
                return;
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }

            _mkdirAll(GetDirectoryPath(NormalizePath(path)));
            write(path, content);
        }
    }

    /// <summary>Go 的 <c>fs.Sub(fsys, dir)</c>：把 dir 当作新的根。</summary>
    private sealed class SubFs : IIoFs
    {
        private readonly IIoFs _inner;
        private readonly string _prefix;

        private SubFs(IIoFs inner, string prefix)
        {
            _inner = inner;
            _prefix = prefix;
        }

        /// <summary>Go 的 fs.Sub 只在路径非法时报错；这里等价成"空前缀 → null"。</summary>
        internal static SubFs? Create(IIoFs fsys, string prefix)
        {
            return prefix.Length == 0 ? null : new SubFs(fsys, prefix);
        }

        public FileInfo? Stat(string name)
        {
            return _inner.Stat(Join(name));
        }

        public DirEntry[]? ReadDir(string name)
        {
            return _inner.ReadDir(Join(name));
        }

        public bool TryReadFile(string name, out string contents)
        {
            return _inner.TryReadFile(Join(name), out contents);
        }

        private string Join(string name)
        {
            return name == "." ? _prefix : _prefix + "/" + name;
        }
    }
}
