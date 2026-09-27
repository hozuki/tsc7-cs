// ported from: internal/project/autoimport.go:1-178
//
// 与 Go 的偏离（逐条说明）：
//
// 1) **包级函数的落点**：Go 的 `project` 包名与类型 `Project` 同名，C# 不能再声明 `static class Project`
//    （见 Project.cs 文件头第 1 条），所以未导出的包级函数挂到最相关的类型上：
//    `newAutoImportRegistryCloneHost(...)` → `AutoImport.NewAutoImportRegistryCloneHost(...)`
//    （本文件 `public static class AutoImport`，命名空间 `TypeScript.Project`；与 `TypeScript.LsAutoImport.AutoImport`
//    是**两个不同命名空间**里的同名静态类，调用点按命名空间区分）。
//
// 2) Go 未导出类型 → C# `internal` + PascalCase：`autoImportBuilderFS` → `AutoImportBuilderFS`、
//    `autoImportRegistryCloneHost` → `AutoImportRegistryCloneHost`；未导出字段 → `internal _camelCase`。
//
// 3) `collections.SyncMap[tspath.Path, FileHandle]` → `Collections.SyncMap<Path, FileHandle>`；
//    `Load`/`LoadOrStore` 的 `(V, bool)` 元组按本仓形状解构。
//
// 4) `sync.Mutex`（`filesMu`）→ `System.Threading.Lock`。
//
// 5) `packagejson.Parse([]byte)` 在 C# 是"正常返回 `Fields`、失败抛 `JsonException`"；Go 的 `err != nil`
//    分支在这里用 `PackageJson.TryParse(byte[], out Fields)` 表达同一判断（避免用异常做正常控制流，
//    行为与 Go 逐条一致：解析失败 → `Parseable: false` 的空内容）。
//    Go 的结构体字面量 `&packagejson.PackageJson{Fields: fields, Parseable: true}` 在 C# 里
//    （`PackageJson : Fields`）逐个字段复制（四个引用字段），语义相同。
//
// 6) `projectID.(ID)` 的 Go 类型断言 → C# 的 `projectID is not ID`（装箱/拆箱等价）；失败分支的
//    `panic(fmt.Sprintf("unexpected project ID type %T", projectID))` → `Debug.Fail(...)` + 返回 null
//    （`Debug.Fail` 没有 [DoesNotReturn]）。
using System.Text;
using TypeScript.Ast;
using TypeScript.Collections;
using TypeScript.PackageJson;
using TypeScript.TSPath;
using PJ = TypeScript.PackageJson.PackageJson;
using Path = TypeScript.TSPath.Path;
using static TypeScript.TSPath.TSPath;

namespace TypeScript.Project;

/// <summary>Go: <c>type autoImportBuilderFS struct</c>（autoimport.go:16-19）。给 auto-import 注册表用的
/// `FileSource`：命中 snapshot 缓存的文件走缓存（惰性重载），未见过的文件进本地的 untracked 表，
/// **不**污染 snapshotFSBuilder 的 cacheFiles（否则只被 auto-import 引用的文件会被长期缓存）。</summary>
internal sealed class AutoImportBuilderFS : FileSource
{
    internal SnapshotFSBuilder _snapshotFSBuilder = null!;

    internal SyncMap<Path, FileHandle> _untrackedFiles = new();

    /// <summary>FS implements FileSource.</summary>
    public Vfs.FS FS()
    {
        return _snapshotFSBuilder._fs;
    }

    /// <summary>GetFile implements FileSource.</summary>
    public FileHandle? GetFile(string fileName)
    {
        var path = _snapshotFSBuilder._toPath(fileName);
        return GetFileByPath(fileName, path);
    }

    /// <summary>GetFileByPath implements FileSource.</summary>
    public FileHandle? GetFileByPath(string fileName, Path path)
    {
        // We want to avoid long-term caching of files referenced only by auto-imports, so we
        // override GetFileByPath to avoid collecting more files into the snapshotFSBuilder's
        // cacheFiles. (Note the reason we can't just use the finalized SnapshotFS is that changed
        // files not read during other parts of the snapshot clone will be marked as dirty, but
        // not yet refreshed from the source filesystem.)
        var (cachedFile, hasCachedFile) = _snapshotFSBuilder._cacheFiles.Load(path);
        if (hasCachedFile)
        {
            return _snapshotFSBuilder.ReloadEntryIfNeeded(cachedFile!);
        }

        var (existing, ok) = _untrackedFiles.Load(path);
        if (ok)
        {
            return existing;
        }

        var fileHandle = _snapshotFSBuilder._fs.GetFileByPath(fileName, path);
        (fileHandle, _) = _untrackedFiles.LoadOrStore(path, fileHandle!);
        return fileHandle;
    }

    public Vfs.Entries GetAccessibleEntries(string path)
    {
        return _snapshotFSBuilder.GetAccessibleEntries(path);
    }

    /// <summary>FileExists implements FileSource.</summary>
    public bool FileExists(string fileName, Path path)
    {
        return _snapshotFSBuilder.FileExists(fileName, path);
    }
}

/// <summary>Go: <c>type autoImportRegistryCloneHost struct</c>（autoimport.go:78-86）。</summary>
internal sealed class AutoImportRegistryCloneHost : LsAutoImport.RegistryCloneHost
{
    internal ProjectCollection _projectCollection = null!;

    internal ParseCache _parseCache = null!;

    internal SourceFS _fs = null!;

    internal string _currentDirectory = "";

    private readonly Lock _filesMu = new();

    internal List<ParseCacheKey> _files = [];

    /// <summary>FS implements autoimport.RegistryCloneHost.</summary>
    public Vfs.FS FS()
    {
        return _fs;
    }

    /// <summary>GetCurrentDirectory implements autoimport.RegistryCloneHost.</summary>
    public string GetCurrentDirectory()
    {
        return _currentDirectory;
    }

    /// <summary>GetDefaultProject implements autoimport.RegistryCloneHost.</summary>
    public (LsAutoImport.ProjectID Id, Compiler.Program? Program) GetDefaultProject(Path path)
    {
        var project = _projectCollection.GetDefaultProject(path);
        if (project is null)
        {
            return (null!, null);
        }

        return (project.ID(), project.GetProgram());
    }

    /// <summary>GetPackageJson implements autoimport.RegistryCloneHost.</summary>
    public InfoCacheEntry? GetPackageJson(string fileName)
    {
        // TODO: ref-counted shared cache
        var fileHandle = _fs.GetFile(fileName);
        var packageDirectory = GetDirectoryPath(fileName);
        if (fileHandle is null)
        {
            return new InfoCacheEntry
            {
                DirectoryExists = _fs.DirectoryExists(packageDirectory),
                PackageDirectory = packageDirectory,
            };
        }

        if (!PJ.TryParse(Encoding.UTF8.GetBytes(fileHandle.Content()), out var fields))
        {
            return new InfoCacheEntry
            {
                DirectoryExists = true,
                PackageDirectory = packageDirectory,
                Contents = new PJ
                {
                    Parseable = false,
                },
            };
        }

        return new InfoCacheEntry
        {
            DirectoryExists = true,
            PackageDirectory = packageDirectory,
            Contents = new PJ
            {
                // Go: `Fields: fields`（`PackageJson` 内嵌 `Fields`，见文件头第 5 条）
                HeaderFields = fields.HeaderFields,
                PathFields = fields.PathFields,
                DependencyFields = fields.DependencyFields,
                ContentMapper = fields.ContentMapper,
                Parseable = true,
            },
        };
    }

    /// <summary>GetProgramForProject implements autoimport.RegistryCloneHost.</summary>
    public Compiler.Program? GetProgramForProject(LsAutoImport.ProjectID projectID)
    {
        if (projectID is not ID id)
        {
            // Go: panic(fmt.Sprintf("unexpected project ID type %T", projectID))
            Debug.Fail($"unexpected project ID type {projectID.GetType()}");
            return null;
        }

        var project = _projectCollection.GetProject(id);
        if (project is null)
        {
            return null;
        }

        return project.GetProgram();
    }

    /// <summary>GetSourceFile implements autoimport.RegistryCloneHost.</summary>
    public SourceFile? GetSourceFile(string fileName, Path path)
    {
        var fileHandle = _fs.GetFile(fileName);
        if (fileHandle is null)
        {
            return null;
        }

        var opts = new SourceFileParseOptions
        {
            FileName = fileName,
            Path = path,
        };
        var key = ParseCacheKey.NewParseCacheKey(opts, fileHandle.Hash(), fileHandle.Kind());
        var result = _parseCache.Acquire(key, fileHandle);

        lock (_filesMu)
        {
            _files.Add(key);
        }

        return result;
    }

    /// <summary>Dispose implements autoimport.RegistryCloneHost.</summary>
    public void Dispose()
    {
        lock (_filesMu)
        {
            foreach (var key in _files)
            {
                _parseCache.Deref(key);
            }
        }
    }
}

/// <summary>Go 的 `project` 包里未导出的包级函数（本文件只放 autoimport.go 的那个）。</summary>
public static class AutoImport
{
    /// <summary>Go: <c>func newAutoImportRegistryCloneHost(projectCollection *ProjectCollection, parseCache *ParseCache,
    /// snapshotFSBuilder *snapshotFSBuilder, currentDirectory string, toPath func(fileName string) tspath.Path) *autoImportRegistryCloneHost</c>
    /// （autoimport.go:88-101）。</summary>
    internal static AutoImportRegistryCloneHost NewAutoImportRegistryCloneHost(
        ProjectCollection projectCollection,
        ParseCache parseCache,
        SnapshotFSBuilder snapshotFSBuilder,
        string currentDirectory,
        Func<string, Path> toPath)
    {
        return new AutoImportRegistryCloneHost
        {
            _projectCollection = projectCollection,
            _parseCache = parseCache,
            _fs = SourceFS.NewSourceFS(false, new AutoImportBuilderFS { _snapshotFSBuilder = snapshotFSBuilder }, toPath),
            _currentDirectory = currentDirectory,
        };
    }
}
