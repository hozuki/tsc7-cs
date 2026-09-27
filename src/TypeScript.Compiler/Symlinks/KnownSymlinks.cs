// ported from: internal/symlinks/knownsymlinks.go（207 行，整文件；`knownsymlinks_bench_test.go` 不在移植范围）
//
// 本文件与 Go 逐成员对应：`KnownDirectoryLink`、`KnownSymlinks` 两个类型 + 12 个方法。
// 每个成员上方的 `/// <summary>Go: <c>func …</c>。</summary>` 给出 Go 原文签名，可与 Go 源并排阅读。
//
// 与 Go 的偏离（都是 C# 语言特性，逐条依据 docs/architecture.md §2/§3）：
// 1) Go 的 `KnownSymlinks` 是结构体、内部 map 是**值字段**（调用方拿到的一直是同一个 map，因为 Go 的
//    sync.Map 内部是指针）→ C# 的 `SyncMap` 是引用类型，字段仍按 Go 的 `_camelCase` 命名。
//    Go 的 `Directories()` 返回 `*collections.SyncMap`（内部字段的地址，调用方 Store 会改到缓存本体）
//    → C# 直接返回字段引用（同一实例，语义相同）。
// 2) Go 的 `*KnownDirectoryLink` 允许 nil，且 `SetDirectory` 会把 nil 存进表 → C# 的
//    `KnownDirectoryLink?`。`Collections.SyncMap` 的值槽用 Slot 包装（不是 ConcurrentDictionary 的原始值），
//    因此能存 null：`Load` 返回 (null, true) 与 Go 存"带类型的 nil 指针"可观察结果相同
//    （见 Collections/SyncMap.cs 文件头第 2 条）。
// 3) Go 的切片 `aParts[:len(aParts)-1]` 是零拷贝视图 → C# 用长度变量推进，最后做一次 `aParts[..len]`
//    切片（只发生在 `guessDirectorySymlink`，非热路径）。`len(aParts)` → 长度变量。
// 4) Go 的匿名函数类型 → C# 的 `Action<>`：
//    `func(callback func(resolution *module.ResolvedModule, moduleName string, mode core.ResolutionMode, filePath tspath.Path), file *ast.SourceFile)`
//    → `Action<Action<ResolvedModule, string, ModuleKind, Path>, SourceFile?>`。
//    Go 调用点把 file 传成 nil（`}, nil)`）→ 第二个参数类型是 `SourceFile?`。
// 5) `ResolutionMode` 是 `ModuleKind` 的别名（docs §3.2 / Core/CompilerOptions.cs:526-531）→ 一律写 `ModuleKind`。
using TypeScript.Ast;
using TypeScript.Collections;
using TypeScript.Core;
using TypeScript.Module;
using static TypeScript.TSPath.TSPath;
using Path = TypeScript.TSPath.Path;

namespace TypeScript.Symlinks;

/// <summary>Go: <c>type KnownDirectoryLink struct</c>（一个已知的目录软链）。</summary>
public sealed class KnownDirectoryLink
{
    /// <summary>Matches the casing returned by `realpath`. Used to compute the `realpath` of children. Always has trailing directory separator.</summary>
    public string Real = "";

    /// <summary>toPath(real). Stored to avoid repeated recomputation. Always has trailing directory separator.</summary>
    public Path RealPath;
}

/// <summary>Go: <c>type KnownSymlinks struct</c>（解析过程中收集到的软链集合）。</summary>
public sealed class KnownSymlinks
{
    private readonly SyncMap<Path, KnownDirectoryLink?> _directories = new();
    private readonly SyncMap<Path, SyncSet<string>> _directoriesByRealpath = new();
    private readonly SyncMap<Path, string> _files = new();
    private readonly SyncMap<Path, SyncSet<string>> _filesByRealpath = new();
    // Go 的测试（同一个 package）直接读这两个未导出字段，C# 靠 Properties/AssemblyInfo.cs 的 InternalsVisibleTo。
    internal readonly string _cwd;
    internal readonly bool _useCaseSensitiveFileNames;

    /// <summary>Go: <c>func (cache *KnownSymlinks) HasDirectory(symlinkPath tspath.Path) bool</c>。</summary>
    public bool HasDirectory(Path symlinkPath)
    {
        var (_, ok) = _directories.Load(symlinkPath.EnsureTrailingDirectorySeparator());
        return ok;
    }

    /// <summary>Go: <c>func (cache *KnownSymlinks) Directories() *collections.SyncMap[tspath.Path, *KnownDirectoryLink]</c>（symlink → realpath；键带尾分隔符）。</summary>
    public SyncMap<Path, KnownDirectoryLink?> Directories() => _directories;

    /// <summary>Go: <c>func (cache *KnownSymlinks) DirectoriesByRealpath() *collections.SyncMap[tspath.Path, *collections.SyncSet[string]]</c>。</summary>
    public SyncMap<Path, SyncSet<string>> DirectoriesByRealpath() => _directoriesByRealpath;

    /// <summary>Go: <c>func (cache *KnownSymlinks) Files() *collections.SyncMap[tspath.Path, string]</c>（symlink → realpath）。</summary>
    public SyncMap<Path, string> Files() => _files;

    /// <summary>Go: <c>func (cache *KnownSymlinks) FilesByRealpath() *collections.SyncMap[tspath.Path, *collections.SyncSet[string]]</c>（realpath → symlinks）。</summary>
    public SyncMap<Path, SyncSet<string>> FilesByRealpath() => _filesByRealpath;

    /// <summary>Go: <c>func (cache *KnownSymlinks) SetDirectory(symlink string, symlinkPath tspath.Path, realDirectory *KnownDirectoryLink)</c>。</summary>
    public void SetDirectory(string symlink, Path symlinkPath, KnownDirectoryLink? realDirectory)
    {
        if (realDirectory is not null)
        {
            var (_, ok) = _directories.Load(symlinkPath);
            if (!ok)
            {
                var (set, _) = _directoriesByRealpath.LoadOrStore(realDirectory.RealPath, new SyncSet<string>());
                set!.Add(symlink);
            }
        }

        _directories.Store(symlinkPath, realDirectory);
    }

    /// <summary>Go: <c>func (cache *KnownSymlinks) SetFile(symlink string, symlinkPath tspath.Path, realpath string)</c>。</summary>
    public void SetFile(string symlink, Path symlinkPath, string realpath)
    {
        var (_, ok) = _files.Load(symlinkPath);
        if (!ok)
        {
            var realpathPath = ToPath(realpath, _cwd, _useCaseSensitiveFileNames);
            var (set, _) = _filesByRealpath.LoadOrStore(realpathPath, new SyncSet<string>());
            set!.Add(symlink);
        }

        _files.Store(symlinkPath, realpath);
    }

    /// <summary>Go 的 `NewKnownSymlink` 用的结构体字面量；C# 里构造函数不是 Go 的导出面，所以是 internal。</summary>
    internal KnownSymlinks(string currentDirectory, bool useCaseSensitiveFileNames)
    {
        _cwd = currentDirectory;
        _useCaseSensitiveFileNames = useCaseSensitiveFileNames;
    }

    /// <summary>Go: <c>func (cache *KnownSymlinks) SetSymlinksFromResolutions(forEachResolvedModule, forEachResolvedTypeReferenceDirective)</c>。</summary>
    public void SetSymlinksFromResolutions(
        Action<Action<ResolvedModule, string, ModuleKind, Path>, SourceFile?> forEachResolvedModule,
        Action<Action<ResolvedTypeReferenceDirective, string, ModuleKind, Path>, SourceFile?> forEachResolvedTypeReferenceDirective)
    {
        forEachResolvedModule(
            (resolution, _, _, _) =>
            {
                ProcessResolution(resolution.OriginalPath, resolution.ResolvedFileName);
            },
            null);
        forEachResolvedTypeReferenceDirective(
            (resolution, _, _, _) =>
            {
                ProcessResolution(resolution.OriginalPath, resolution.ResolvedFileName);
            },
            null);
    }

    /// <summary>Go: <c>func (cache *KnownSymlinks) ProcessResolution(originalPath string, resolvedFileName string)</c>。</summary>
    public void ProcessResolution(string originalPath, string resolvedFileName)
    {
        if (originalPath.Length == 0 || resolvedFileName.Length == 0)
        {
            return;
        }

        SetFile(originalPath, ToPath(originalPath, _cwd, _useCaseSensitiveFileNames), resolvedFileName);
        var (commonResolved, commonOriginal) = GuessDirectorySymlink(resolvedFileName, originalPath, _cwd);
        if (commonResolved.Length != 0 && commonOriginal.Length != 0)
        {
            var symlinkPath = ToPath(commonOriginal, _cwd, _useCaseSensitiveFileNames);
            if (!ContainsIgnoredPath(symlinkPath.Value))
            {
                SetDirectory(
                    commonOriginal,
                    symlinkPath.EnsureTrailingDirectorySeparator(),
                    new KnownDirectoryLink
                    {
                        Real = EnsureTrailingDirectorySeparator(commonResolved),
                        RealPath = ToPath(commonResolved, _cwd, _useCaseSensitiveFileNames).EnsureTrailingDirectorySeparator(),
                    });
            }
        }
    }

    /// <summary>Go: <c>func (cache *KnownSymlinks) guessDirectorySymlink(a string, b string, cwd string) (string, string)</c>。</summary>
    internal (string CommonResolved, string CommonOriginal) GuessDirectorySymlink(string a, string b, string cwd)
    {
        var aParts = GetPathComponents(GetNormalizedAbsolutePath(a, cwd), "");
        var bParts = GetPathComponents(GetNormalizedAbsolutePath(b, cwd), "");
        // Go 用 `aParts = aParts[:len(aParts)-1]` 逐步缩尾；C# 用长度变量（见文件头第 3 条）。
        var aLength = aParts.Length;
        var bLength = bParts.Length;
        var isDirectory = false;
        while (aLength >= 2 && bLength >= 2
            && !IsNodeModulesOrScopedPackageDirectory(aParts[aLength - 2])
            && !IsNodeModulesOrScopedPackageDirectory(bParts[bLength - 2])
            && GetCanonicalFileName(aParts[aLength - 1], _useCaseSensitiveFileNames) == GetCanonicalFileName(bParts[bLength - 1], _useCaseSensitiveFileNames))
        {
            aLength--;
            bLength--;
            isDirectory = true;
        }

        if (isDirectory)
        {
            return (GetPathFromPathComponents(aParts[..aLength]), GetPathFromPathComponents(bParts[..bLength]));
        }

        return ("", "");
    }

    /// <summary>Go: <c>func (cache *KnownSymlinks) isNodeModulesOrScopedPackageDirectory(s string) bool</c>。</summary>
    internal bool IsNodeModulesOrScopedPackageDirectory(string s)
    {
        return s.Length != 0 && (GetCanonicalFileName(s, _useCaseSensitiveFileNames) == "node_modules" || s[0] == '@');
    }
}

/// <summary>Go 的 `symlinks` 包（包级函数放这里，见 docs/architecture.md §2）。</summary>
public static partial class Symlinks
{
    /// <summary>Go: <c>func NewKnownSymlink(currentDirectory string, useCaseSensitiveFileNames bool) *KnownSymlinks</c>。</summary>
    public static KnownSymlinks NewKnownSymlink(string currentDirectory, bool useCaseSensitiveFileNames) =>
        new(currentDirectory, useCaseSensitiveFileNames);
}
