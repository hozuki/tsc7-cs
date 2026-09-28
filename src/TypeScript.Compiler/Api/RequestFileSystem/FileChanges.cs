// ported from: internal/api/requestfilesystem/filechanges.go:1-112
//
// 与 Go 的偏离（逐条说明）：
//
// 1) `func (s *requestFileSystem) ExpandFileChanges(...)` 是 `requestFileSystem` 的方法，而它定义在另一个 Go 文件里
//    ⇒ C# 用 **partial** 续写 `RequestFileSystemImpl`（两部分同修饰符 `internal sealed partial class`），
//    保持"一个 Go 文件 = 一个 .cs 文件"的对应关系。
// 2) Go 的 `var additional collections.Set[...]`（零值即可写）→ C# 必须 `new Collections.Set<DocumentUri>()`：
//    本仓的 `Set<T>` 是 class（Collections/Set.cs），"零值可写"由构造函数保证。
// 3) Go 的 `expand(&summary.Changed)` 传指针 + 闭包 → C# 直接传 `Set<T>` 实例（引用类型，`Add` 就地生效）；
//    `addChange`/`addChangeAndAliases`/`addReplacement` 相互调用，按 C# 的局部函数落（Go 的闭包变量等价），
//    无委托分配。
// 4) Go 的 `map[tspath.Path]struct{}`（overlayFiles）→ `HashSet<Path>`。
// 5) `project.FileChangeSummary` 的四个集合在 C# 是 `Collections.Set<DocumentUri>`（`Has`/`Add`/`Delete`/`Len`），
//    与 Go 的 `collections.Set` 一一对应。
// 6) `baseFS.(project.LayeredFileSystem)` → C# 的 `baseFS is LayeredFileSystem`；`overlays[path]` 的
//    "存在性"判定 → `ContainsKey`。`fileSystem.Overlays()` 在 C# 恒非 null（见 RequestFileSystem.cs 文件头第 7 条）。
// 7) `request.Directories`/`Symlinks`/`RemovedPaths` 在 C# 都可空（Go 的 `omitempty` → `T?`），遍历前判空
//    （Go 遍历 nil map/slice 天然是空集，语义相同）。
// 8) `lsconv.FileNameToDocumentURI` → 全限定 `TypeScript.LsConv.LsConv.FileNameToDocumentURI`
//    （虽然本文件有 `using TypeScript.LsConv;`，但全限定能避免与局部名/命名空间段混淆；先例 Proto.cs:162）。
using TypeScript.Collections;
using TypeScript.LsConv;
using TypeScript.LsProto;
using TypeScript.Project;
using TypeScript.TSPath;
using TypeScript.Vfs;
using static TypeScript.TSPath.TSPath;
using Path = TypeScript.TSPath.Path;

namespace TypeScript.Api.RequestFileSystem;

internal sealed partial class RequestFileSystemImpl
{
    /// <summary>Go: <c>func (s *requestFileSystem) ExpandFileChanges(summary project.FileChangeSummary) project.FileChangeSummary</c>
    /// （filechanges.go:12-28）：把 Changed/Created/Deleted 里的每个 URI 展开成"经符号链接可达的别名"。</summary>
    public FileChangeSummary ExpandFileChanges(FileChangeSummary summary)
    {
        Expand(summary.Changed);
        Expand(summary.Created);
        Expand(summary.Deleted);
        return summary;

        void Expand(Set<DocumentUri> uris)
        {
            var additional = new Set<DocumentUri>();
            foreach (var uri in uris.Keys())
            {
                foreach (var alias in AliasesForPath(uri.FileName()))
                {
                    additional.Add(TypeScript.LsConv.LsConv.FileNameToDocumentURI(alias));
                }
            }

            foreach (var uri in additional.Keys())
            {
                uris.Add(uri);
            }
        }
    }
}

/// <summary>Go 的 <c>filechanges.go</c> 包级函数（见文件头第 1 条）。</summary>
internal static class FileChanges
{
    /// <summary>Go: <c>func addFileChanges(summary *project.FileChangeSummary, request *RequestFileSystem, baseFS vfs.FS,
    /// fileSystem *requestFileSystem, currentDirectory string)</c>（filechanges.go:30-112）：把请求文件系统相对
    /// baseFS 的增删改写进 summary（layer 文件系统才需要，`NewForUpdate` 调用）。</summary>
    internal static void AddFileChanges(
        FileChangeSummary summary,
        RequestFileSystem request,
        FS baseFS,
        RequestFileSystemImpl fileSystem,
        string currentDirectory)
    {
        Path ToPathOf(string fileName)
        {
            return ToPath(fileName, currentDirectory, baseFS.UseCaseSensitiveFileNames());
        }

        var baseRequestFS = RequestFileSystemImpl.GetRequestFileSystem(baseFS);

        void AddChange(string fileName, bool deleted)
        {
            var uri = TypeScript.LsConv.LsConv.FileNameToDocumentURI(fileName);
            if (deleted)
            {
                if (baseFS.FileExists(fileName) || baseFS.DirectoryExists(fileName))
                {
                    summary.Deleted.Add(uri);
                }

                return;
            }

            if (baseFS.FileExists(fileName))
            {
                summary.Changed.Add(uri);
            }
            else
            {
                summary.Created.Add(uri);
            }
        }

        void AddChangeAndAliases(string fileName, bool deleted)
        {
            AddChange(fileName, deleted);
            if (baseRequestFS is not null)
            {
                foreach (var alias in baseRequestFS.AliasesForPath(fileName))
                {
                    AddChange(alias, deleted);
                }
            }
        }

        var overlayFiles = new HashSet<Path>();
        foreach (var fileName in request.Files.Keys)
        {
            var absoluteFileName = GetNormalizedAbsolutePath(fileName, currentDirectory);
            overlayFiles.Add(ToPathOf(absoluteFileName));
            AddChangeAndAliases(absoluteFileName, false);
        }

        if (request.RemovedPaths is not null)
        {
            foreach (var removedPath in request.RemovedPaths)
            {
                var absoluteFileName = GetNormalizedAbsolutePath(removedPath, currentDirectory);
                if (overlayFiles.Contains(ToPathOf(absoluteFileName)))
                {
                    continue;
                }

                AddChangeAndAliases(absoluteFileName, true);
            }
        }

        // 替换一个目录列举或一个符号链接会改变它下面所有缓存的后代：
        // 删除事件沿快照的缓存目录树展开，创建事件用于刷新通配符根与此前缺失的模块解析。
        void AddReplacement(string path)
        {
            var absolutePath = GetNormalizedAbsolutePath(path, currentDirectory);
            AddChangeAndAliases(absolutePath, true);
            summary.Created.Add(TypeScript.LsConv.LsConv.FileNameToDocumentURI(absolutePath));
            if (baseRequestFS is not null)
            {
                foreach (var alias in baseRequestFS.AliasesForPath(absolutePath))
                {
                    summary.Created.Add(TypeScript.LsConv.LsConv.FileNameToDocumentURI(alias));
                }
            }
        }

        if (request.Directories is not null)
        {
            foreach (var directoryName in request.Directories.Keys)
            {
                AddReplacement(directoryName);
            }
        }

        if (request.Symlinks is not null)
        {
            foreach (var linkName in request.Symlinks.Keys)
            {
                AddReplacement(linkName);
            }
        }

        if (baseFS is LayeredFileSystem layeredBase)
        {
            var overlays = fileSystem.Overlays();
            foreach (var (path, overlay) in layeredBase.Overlays())
            {
                if (overlays.ContainsKey(path))
                {
                    continue;
                }

                var uri = TypeScript.LsConv.LsConv.FileNameToDocumentURI(overlay.FileName());
                if (summary.Closed.Has(uri))
                {
                    continue;
                }

                summary.Created.Delete(uri);
                if (fileSystem.FileExists(overlay.FileName()))
                {
                    summary.Deleted.Delete(uri);
                    summary.Changed.Add(uri);
                }
                else
                {
                    summary.Changed.Delete(uri);
                    summary.Deleted.Add(uri);
                }
            }
        }

        if (summary.Changed.Len() + summary.Created.Len() + summary.Deleted.Len() > 0)
        {
            summary.IncludesWatchChangeOutsideNodeModules = true;
        }
    }
}
