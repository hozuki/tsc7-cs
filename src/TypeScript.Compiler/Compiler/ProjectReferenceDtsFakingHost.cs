// ported from: internal/compiler/projectreferencedtsfakinghost.go（全文 227 行）
//
// 与 Go 的偏离（docs/architecture.md §2/§3）：
// 1) `panic("should not be called by resolver")` → `Debug.Fail(...)`（抛 DebugFailureException；§3.2）。
// 2) Go 的 `(contents string, ok bool)` → `bool TryReadFile(path, out contents)`（Vfs.FS 的既有形状）。
// 3) Go 的 `symlinks.KnownSymlinks{}`（零值 struct）→ `new KnownSymlinks("", false)`（C# 的类没有零值构造，
//    零值语义就是 cwd="" + useCaseSensitiveFileNames=false；该实例只用来存 Files/Directories 两张表）。
// 4) `core.IfElse(isFile, a, b)`（两个方法值）→ 显式 `Func<string, Tristate>` 三元（目标类型已知，方法组可直接转换）。
// 5) `strings.CutPrefix` → `StartsWith(..., StringComparison.Ordinal)` + 切片（Go 的 CutPrefix 是字节级比较）；
//    `len(directoryPath)`（字节）→ `directoryPath.Value.Length`（路径都是 ASCII，两者一致）。
// 6) 未导出类型 → C# `internal` 且按 C# 习惯命名（`ProjectReferenceDtsFakingVfs`、方法 PascalCase）。
namespace TypeScript.Compiler;

using TypeScript.Collections;
using TypeScript.Core;
using TypeScript.Module;
using TypeScript.Symlinks;
using TypeScript.TSPath;
using TypeScript.Vfs;
using TypeScript.Vfs.CachedVfs;
using static TypeScript.Core.Core;
using static TypeScript.TSPath.TSPath;
using FS = TypeScript.Vfs.FS;
using Path = TypeScript.TSPath.Path;

/// <summary>Go: <c>type projectReferenceDtsFakingHost struct</c>（projectreferencedtsfakinghost.go:15）。</summary>
public sealed class ProjectReferenceDtsFakingHost : ResolutionHost
{
    /// <summary>Go: <c>host CompilerHost</c>。</summary>
    internal CompilerHost _host;

    /// <summary>Go: <c>fs *cachedvfs.FS</c>。</summary>
    internal FS _fs;

    internal ProjectReferenceDtsFakingHost(CompilerHost host, FS fs)
    {
        _host = host;
        _fs = fs;
    }

    /// <summary>Go: <c>func (h *projectReferenceDtsFakingHost) FS() vfs.FS</c>——<c>module.ResolutionHost</c>。</summary>
    public FS FS() => _fs;

    /// <summary>Go: <c>func (h *projectReferenceDtsFakingHost) GetCurrentDirectory() string</c>——<c>module.ResolutionHost</c>。</summary>
    public string GetCurrentDirectory() => _host.GetCurrentDirectory();
}

/// <summary>Go: <c>type projectReferenceDtsFakingVfs struct</c>（projectreferencedtsfakinghost.go:54）。</summary>
internal sealed class ProjectReferenceDtsFakingVfs : FS
{
    internal ProjectReferenceFileMapper _projectReferenceFileMapper;
    internal Set<Path> _dtsDirectories;
    internal KnownSymlinks _knownSymlinks;

    internal ProjectReferenceDtsFakingVfs(ProjectReferenceFileMapper projectReferenceFileMapper, Set<Path> dtsDirectories, KnownSymlinks knownSymlinks)
    {
        _projectReferenceFileMapper = projectReferenceFileMapper;
        _dtsDirectories = dtsDirectories;
        _knownSymlinks = knownSymlinks;
    }

    /// <summary>Go: <c>func (fs *projectReferenceDtsFakingVfs) UseCaseSensitiveFileNames() bool</c>——<c>vfs.FS</c>。</summary>
    public bool UseCaseSensitiveFileNames() => _projectReferenceFileMapper._opts.Host.FS().UseCaseSensitiveFileNames();

    /// <summary>Go: <c>func (fs *projectReferenceDtsFakingVfs) FileExists(path string) bool</c>——<c>vfs.FS</c>。</summary>
    public bool FileExists(string path)
    {
        if (_projectReferenceFileMapper._opts.Host.FS().FileExists(path))
        {
            return true;
        }
        if (!IsDeclarationFileName(path))
        {
            return false;
        }
        // 项目引用要指向源文件而不是 .d.ts 文件
        return FileOrDirectoryExistsUsingSource(path /*isFile*/, true);
    }

    /// <summary>Go: <c>func (fs *projectReferenceDtsFakingVfs) ReadFile(path string) (contents string, ok bool)</c>——<c>vfs.FS</c>。</summary>
    public bool TryReadFile(string path, out string contents)
    {
        // 不用覆盖，因为无法伪造读文件
        return _projectReferenceFileMapper._opts.Host.FS().TryReadFile(path, out contents);
    }

    /// <summary>Go: <c>func (fs *projectReferenceDtsFakingVfs) WriteFile(path string, data string) error</c>——<c>vfs.FS</c>。</summary>
    public void WriteFile(string path, string data)
    {
        Debug.Fail("should not be called by resolver");
    }

    /// <summary>Go: <c>func (fs *projectReferenceDtsFakingVfs) AppendFile(path string, data string) error</c>——<c>vfs.FS</c>。</summary>
    public void AppendFile(string path, string data)
    {
        Debug.Fail("should not be called by resolver");
    }

    /// <summary>Go: <c>func (fs *projectReferenceDtsFakingVfs) Remove(path string) error</c>——<c>vfs.FS</c>。</summary>
    public void Remove(string path)
    {
        Debug.Fail("should not be called by resolver");
    }

    /// <summary>Go: <c>func (fs *projectReferenceDtsFakingVfs) Chtimes(path string, aTime, mTime time.Time) error</c>——<c>vfs.FS</c>。</summary>
    public void Chtimes(string path, DateTime accessTime, DateTime modificationTime)
    {
        Debug.Fail("should not be called by resolver");
    }

    /// <summary>Go: <c>func (fs *projectReferenceDtsFakingVfs) DirectoryExists(path string) bool</c>——<c>vfs.FS</c>。</summary>
    public bool DirectoryExists(string path)
    {
        if (_projectReferenceFileMapper._opts.Host.FS().DirectoryExists(path))
        {
            HandleDirectoryCouldBeSymlink(path);
            return true;
        }
        return FileOrDirectoryExistsUsingSource(path /*isFile*/, false);
    }

    /// <summary>Go: <c>func (fs *projectReferenceDtsFakingVfs) GetAccessibleEntries(path string) vfs.Entries</c>——<c>vfs.FS</c>。</summary>
    public Entries GetAccessibleEntries(string path)
    {
        Debug.Fail("should not be called by resolver");
        return default;
    }

    /// <summary>Go: <c>func (fs *projectReferenceDtsFakingVfs) Stat(path string) vfs.FileInfo</c>——<c>vfs.FS</c>。</summary>
    public FileInfo? Stat(string path)
    {
        Debug.Fail("should not be called by resolver");
        return null;
    }

    /// <summary>Go: <c>func (fs *projectReferenceDtsFakingVfs) Realpath(path string) string</c>——<c>vfs.FS</c>。</summary>
    public string Realpath(string path)
    {
        var (result, ok) = _knownSymlinks.Files().Load(ToPath(path));
        if (ok)
        {
            return result!;
        }
        return _projectReferenceFileMapper._opts.Host.FS().Realpath(path);
    }

    private Path ToPath(string path) =>
        TypeScript.TSPath.TSPath.ToPath(path, _projectReferenceFileMapper._opts.Host.GetCurrentDirectory(), UseCaseSensitiveFileNames());

    private void HandleDirectoryCouldBeSymlink(string directory)
    {
        if (ContainsIgnoredPath(directory))
        {
            return;
        }

        // 既然已经 watch 了 node_modules，就处理那里的软链
        if (!directory.Contains("/node_modules/", StringComparison.Ordinal))
        {
            return;
        }

        var directoryPath = (Path)EnsureTrailingDirectorySeparator(ToPath(directory).Value);
        if (_knownSymlinks.Directories().Load(directoryPath).Ok)
        {
            return;
        }

        var realDirectory = Realpath(directory);
        if (realDirectory == directory)
        {
            // 不是软链
            return;
        }
        var realPath = (Path)EnsureTrailingDirectorySeparator(ToPath(realDirectory).Value);
        if (realPath == directoryPath)
        {
            // 不是软链
            return;
        }
        _knownSymlinks.SetDirectory(directory, directoryPath, new KnownDirectoryLink
        {
            Real = EnsureTrailingDirectorySeparator(realDirectory),
            RealPath = realPath,
        });
    }

    private bool FileOrDirectoryExistsUsingSource(string fileOrDirectory, bool isFile)
    {
        Func<string, Tristate> fileOrDirectoryExistsUsingSource = isFile
            ? FileExistsIfProjectReferenceDts
            : DirectoryExistsIfProjectReferenceDeclDir;
        // 先查当前目录或文件
        var result = fileOrDirectoryExistsUsingSource(fileOrDirectory);
        if (result != Tristate.TSUnknown)
        {
            return result == Tristate.TSTrue;
        }

        var fileOrDirectoryPath = ToPath(fileOrDirectory);
        if (!fileOrDirectoryPath.Value.Contains("/node_modules/", StringComparison.Ordinal))
        {
            return false;
        }
        // 检查该目录或文件是否是软链包
        var packageRoot = TypeScript.Module.Module.ParseNodeModuleFromPath(fileOrDirectory, true /*isFolder*/);
        if (packageRoot != "")
        {
            HandleDirectoryCouldBeSymlink(packageRoot);
        }
        var knownDirectoryLinks = _knownSymlinks.Directories();
        if (knownDirectoryLinks.Size() == 0)
        {
            return false;
        }
        if (isFile && _knownSymlinks.Files().Load(fileOrDirectoryPath).Ok)
        {
            return true;
        }

        // 若路径含 node_modules，检查它是不是我们已知的软链路径之一
        var exists = false;
        knownDirectoryLinks.Range((directoryPath, knownDirectoryLink) =>
        {
            var s = fileOrDirectoryPath.Value;
            var prefix = directoryPath.Value;
            if (!s.StartsWith(prefix, StringComparison.Ordinal))
            {
                return true;
            }
            var relative = s[prefix.Length..];
            exists = fileOrDirectoryExistsUsingSource(knownDirectoryLink!.RealPath.Value + relative).IsTrue();
            if (exists)
            {
                if (isFile)
                {
                    // 记住该文件的真实路径
                    var absolutePath = GetNormalizedAbsolutePath(fileOrDirectory, _projectReferenceFileMapper._opts.Host.GetCurrentDirectory());
                    _knownSymlinks.SetFile(
                        absolutePath,
                        fileOrDirectoryPath,
                        knownDirectoryLink.Real + absolutePath[prefix.Length..]);
                }
                return false;
            }
            return true;
        });
        return exists;
    }

    private Tristate FileExistsIfProjectReferenceDts(string file)
    {
        var source = _projectReferenceFileMapper.GetProjectReferenceFromOutputDts(ToPath(file));
        if (source is not null)
        {
            return IfElse(_projectReferenceFileMapper._opts.Host.FS().FileExists(source.Source), Tristate.TSTrue, Tristate.TSFalse);
        }
        return Tristate.TSUnknown;
    }

    private Tristate DirectoryExistsIfProjectReferenceDeclDir(string dir)
    {
        var dirPath = ToPath(dir);
        foreach (var declDirPath in _dtsDirectories.Keys())
        {
            if (dirPath.ContainsPath(declDirPath) || declDirPath.ContainsPath(dirPath))
            {
                return Tristate.TSTrue;
            }
        }
        return Tristate.TSUnknown;
    }
}

/// <summary>projectreferencedtsfakinghost.go 的包级构造函数（`Compiler` 静态类的 partial，见 Pkg.cs）。</summary>
public static partial class Compiler
{
    /// <summary>
    /// Go: <c>func newProjectReferenceDtsFakingHost(loader *fileLoader) module.ResolutionHost</c>（projectreferencedtsfakinghost.go:20）——
    /// 创建一个"伪造 dts 文件"的 host。
    /// </summary>
    internal static ResolutionHost NewProjectReferenceDtsFakingHost(FileLoader loader)
    {
        var host = new ProjectReferenceDtsFakingHost(
            loader._opts.Host,
            CachedVfs.From(new ProjectReferenceDtsFakingVfs(
                loader._projectReferenceFileMapper!,
                loader._dtsDirectories,
                // Go: symlinks.KnownSymlinks{}（零值）→ C# 用等价的空构造
                new KnownSymlinks("", false))));
        return host;
    }
}
