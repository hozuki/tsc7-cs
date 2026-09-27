// ported from: internal/ls/crossproject.go:1-436（`Project` 接口那 4 行已在 `Ls/Project.cs`，本文件从 23 行起）
//
// ⚠ **G1 的跨项目编排层**。`HandleCrossProject` 是 G2/G6/G7 的 `Provide*` 共用的入口（泛型 + 编排），
//    调用采样见 Go 的 `ProvideReferences`/`ProvideVSReferences`/`ProvideImplementations`（G6）。
//
// 与 Go 的偏离（逐条说明）：
//
// 1) **唯一一处签名偏离（Main 裁定所需：G1 必须能独立编译并被冒烟测试）**：Go 的 `handleCrossProject` 内部直接
//    调用 `ls.provideSymbolsAndEntries(...)`（定义在 `findallreferences.go`，属 G6）。而 `FindAllReferences.cs`
//    在本切片里只有 G1 前缀（不含 `provideSymbolsAndEntries` 及其整条 `getReferencedSymbolsForNode` 闭包），
//    若直接调用会留下未解析成员、使整个 `TypeScript.Compiler` 无法编译（违反"每刀必须可编译"）。
//    因此把符号搜索作为**显式委托参数** `provideSymbolsAndEntries` 传入（Go 里它本来也是"对 ls 的一个调用"，
//    只是隐式取实例方法）。G6 写 `ProvideReferences` 等调用点时照抄：
//        l.HandleCrossProject(ctx, params, orchestrator, ..., defaultProjectData: null,
//            provideSymbolsAndEntries: (ls, c, uri, pos, rename, impl) => ls.ProvideSymbolsAndEntries(c, uri, pos, rename, impl));
//    语义与 Go 逐行一致（默认项目那一支用 `defaultLs`，其余项目用各自 `ls`）。
//
// 2) Go 的 `context.Context` → C# `TypeScript.Core.Context`；`ctx.Err() != nil` → `ctx.CancellationToken.IsCancellationRequested`
//    （`Context` 只暴露 `CancellationToken`，见 Core/Context.cs:53），取消错误用 `OperationCanceledException` 表达。
//
// 3) Go 的 `recover()` 收集 panic 再 panic → C# `catch (Exception)` 收集 `Environment.StackTrace`（对应
//    `runtime/debug.Stack()`），最后 `throw new DebugFailureException(...)`（对应 Go 的 `panic(fmt.Sprintf(...))`）。
//
// 4) `wg := core.NewWorkGroup(false)` + `wg.Queue`/`wg.RunAndWait` → 同名 C# API（Core/Workgroup.cs:92,117,264）。
//
// 5) `collections.SyncMap[string, *response[Resp]]` → `SyncMap<string, Response<Resp>>`（引用类型 ⇒ 复刻
//    Go 的"存指针、闭包里改写后所有读者可见"）；`LoadOrStore` 返回 `(V? Value, bool Loaded)`。
//
// 6) Go 的 `iter.Seq[Resp]` → `IEnumerable<Resp>`（本地迭代器方法 + `yield return`）；Go 里 `!yield(...)` 提前
//    终止的语义由调用方 `break`/枚举器释放承担。
//
// 7) Go 的 `*[]T` 出参（`combineLocationArray` 的 `locations *[]T`、`combineResponseLocations` 的返回值）
//    → C# 的 `List<T>` / `T[]`（Go 用指针是因为切片头要能被追加替换）。
//
// 8) `lsproto.ReferencesResponse` / `VSReferencesResponse` / `RenameResponse` / `ImplementationResponse` /
//    `CallHierarchyIncomingCallsResponse` 都是 Go 的**类型别名**（`= LocationsOrNull` / `= VSReferenceItemsOrNull` /
//    `= WorkspaceEditOrNull` / `= LocationOrLocationsOrDefinitionLinksOrNull` / `= CallHierarchyIncomingCallsOrNull`）；
//    C# 生成物不给别名（LsProto/Lsp.Generated.Methods.cs:668,743,403 的注释）⇒ 本文件直接用底层 union 类型。
// 9) `combineVSReferences` 里 Go 的 `newItem := *item`（结构体浅拷贝）→ C# 逐字段复制（生成物没有 `Clone`），
//    见 `ShallowCloneVSReferenceItem`；union 的可空数组 `VSReferenceItems`（`VSReferenceItem?[]?`）在遍历时判空元素。
// 10) Go 的 `[2]lsproto.DocumentUri` 作 set 键 → C# 的 `(DocumentUri OldUri, DocumentUri NewUri)` 元组（同样按值比较）。
// 11) 深递归：本文件不递归（WorkGroup 的队列是显式循环）⇒ 无需大栈线程；调用方（ls/*.go 的 Provide*）自带。
using TypeScript.Ast;
using TypeScript.Collections;
using TypeScript.Compiler;
using TypeScript.Core;
using TypeScript.LsProto;
using TypeScript.TSPath;
using static TypeScript.Core.Core;
// 同名类型消歧（`using System` 带来的 `System.Range`；先例 LsConv/Converters.cs:56）：
using Range = TypeScript.LsProto.Range;

namespace TypeScript.Ls;

/// <summary>Go: <c>type projectAndTextDocumentPosition struct</c>（crossproject.go:23-30）。</summary>
internal sealed class ProjectAndTextDocumentPosition
{
    internal Project _project = null!;
    internal LanguageService? _ls;
    public DocumentUri Uri;
    public Position Position = null!;
    internal SymbolAndEntriesData? _symbolData;
    internal bool _forOriginalLocation;
}

/// <summary>Go: <c>type response[Resp any] struct</c>（crossproject.go:32-36）。</summary>
internal sealed class Response<Resp>
{
    internal bool _complete;
    internal Resp _result = default!;
    internal bool _forOriginalLocation;
}

/// <summary>Go: <c>type CrossProjectOrchestrator interface</c>（crossproject.go:38-44）。实现方：`lsp.Server`（P4 切片）。</summary>
public interface CrossProjectOrchestrator
{
    Project GetDefaultProject();

    Project[] GetAllProjectsForInitialRequest();

    LanguageService? GetLanguageServiceForProjectWithFile(Context ctx, Project project, DocumentUri uri);

    (Project[] Projects, Exception? Error) GetProjectsForFile(Context ctx, DocumentUri uri);

    IEnumerable<Project> GetProjectsLoadingProjectTree(Context ctx, Set<TSPath.Path> requestedProjectTrees);
}

public sealed partial class LanguageService
{
    internal (Resp Response, Exception? Error) HandleCrossProject<Req, Resp>(
        Context ctx,
        Req @params,
        CrossProjectOrchestrator? orchestrator,
        Func<LanguageService, Context, Req, SymbolAndEntriesData, SymbolEntryTransformOptions, (Resp Response, Exception? Error)> symbolAndEntriesToResp,
        Func<IEnumerable<Resp>, Resp> combineResults,
        bool isRename,
        bool implementations,
        SymbolEntryTransformOptions options,
        SymbolAndEntriesData? defaultProjectData,
        Func<LanguageService, Context, DocumentUri, Position, bool, bool, (SymbolAndEntriesData Data, bool Ok)> provideSymbolsAndEntries)
        where Req : HasTextDocumentPosition
    {
        var resp = default(Resp)!;
        Exception? err = null;

        // Single project
        if (orchestrator is null)
        {
            SymbolAndEntriesData data;
            if (defaultProjectData is not null)
            {
                data = defaultProjectData;
            }
            else
            {
                (data, _) = provideSymbolsAndEntries(this, ctx, @params.TextDocumentURI(), @params.TextDocumentPosition(), isRename, implementations);
            }

            return symbolAndEntriesToResp(this, ctx, @params, data, options);
        }

        var defaultProject = orchestrator.GetDefaultProject();
        var allProjects = orchestrator.GetAllProjectsForInitialRequest();
        var results = new SyncMap<string, Response<Resp>>();
        NonLocalDefinition? defaultDefinition = null;
        bool CanSearchProject(Project project)
        {
            var (_, searched) = results.Load(project.Id());
            return !searched;
        }

        var wg = NewWorkGroup(false);
        var errMu = new Lock();
        var panicsOccurred = new List<string>();
        var panicMu = new Lock();

        void EnqueueItem(ProjectAndTextDocumentPosition item)
        {
            var response = new Response<Resp>();
            var (_, loaded) = results.LoadOrStore(item._project.Id(), response);
            if (loaded)
            {
                return;
            }

            wg.Queue(() =>
            {
                if (ctx.CancellationToken.IsCancellationRequested)
                {
                    return;
                }

                try
                {
                    // Process the item
                    var ls = item._ls;
                    if (ls is null)
                    {
                        // Get it now
                        ls = orchestrator.GetLanguageServiceForProjectWithFile(ctx, item._project, item.Uri);
                        if (ls is null)
                        {
                            return;
                        }
                    }

                    SymbolAndEntriesData data;
                    bool ok;
                    if (item._symbolData is not null)
                    {
                        data = item._symbolData;
                        ok = true;
                    }
                    else
                    {
                        (data, ok) = provideSymbolsAndEntries(ls, ctx, item.Uri, item.Position, isRename, implementations);
                    }

                    if (ctx.CancellationToken.IsCancellationRequested)
                    {
                        return;
                    }

                    if (ok)
                    {
                        foreach (var entry in data.SymbolsAndEntries ?? [])
                        {
                            // Find the default definition that can be in another project
                            // Later we will use this load ancestor tree that references this location and expand search
                            if (ReferenceEquals(item._project, defaultProject) && defaultDefinition is null)
                            {
                                defaultDefinition = ls.GetNonLocalDefinition(ctx, entry);
                            }

                            ls.ForEachOriginalDefinitionLocation(ctx, entry, (uri, position) =>
                            {
                                // Get default configured project for this file
                                var (defProjects, errProjects) = orchestrator.GetProjectsForFile(ctx, uri);
                                if (errProjects is not null)
                                {
                                    return;
                                }

                                foreach (var defProject in defProjects)
                                {
                                    // Optimization: don't enqueue if will be discarded
                                    if (CanSearchProject(defProject))
                                    {
                                        EnqueueItem(new ProjectAndTextDocumentPosition
                                        {
                                            _project = defProject,
                                            Uri = uri,
                                            Position = position,
                                            _forOriginalLocation = true,
                                        });
                                    }
                                }
                            });
                        }
                    }

                    var (result, errSearch) = symbolAndEntriesToResp(ls, ctx, @params, data, options);
                    if (errSearch is null)
                    {
                        response._complete = true;
                        response._result = result;
                        response._forOriginalLocation = item._forOriginalLocation;
                    }
                    else
                    {
                        lock (errMu)
                        {
                            err ??= errSearch;
                        }
                    }
                }
                catch (Exception ex)
                {
                    var stack = Environment.StackTrace;
                    var panicOccurred = $"panic handling request: {ex}\n{stack}";
                    lock (panicMu)
                    {
                        panicsOccurred.Add(panicOccurred);
                    }
                }
            });
        }

        // Initial set of projects and locations in the queue, starting with default project
        var initialItem = new ProjectAndTextDocumentPosition
        {
            _project = defaultProject,
            _ls = this,
            Uri = @params.TextDocumentURI(),
            Position = @params.TextDocumentPosition(),
            _symbolData = defaultProjectData,
        };
        EnqueueItem(initialItem);
        foreach (var project in allProjects)
        {
            if (!ReferenceEquals(project, defaultProject))
            {
                EnqueueItem(new ProjectAndTextDocumentPosition
                {
                    _project = project,
                    // TODO!! symlinks need to change the URI
                    Uri = @params.TextDocumentURI(),
                    Position = @params.TextDocumentPosition(),
                });
            }
        }

        IEnumerable<Resp> GetResultsIterator()
        {
            var seenProjects = new SyncSet<string>();
            var (defaultResponse, defaultLoaded) = results.Load(defaultProject.Id());
            if (defaultLoaded && defaultResponse!._complete)
            {
                yield return defaultResponse._result;
            }

            seenProjects.Add(defaultProject.Id());
            foreach (var project in allProjects)
            {
                if (seenProjects.AddIfAbsent(project.Id()))
                {
                    var (projectResponse, projectLoaded) = results.Load(project.Id());
                    if (projectLoaded && projectResponse!._complete)
                    {
                        yield return projectResponse._result;
                    }
                }
            }

            // Go 在 `results.Range(...)` 的回调里直接 `yield`（回调返回 false 即中断 Range）；C# 的迭代器不能
            // 在回调里 yield，所以此处先取一次快照（此时已单线程、无并发写入，与 Go 的活的 Range 等价），
            // 再按 Go 的两趟顺序产出。
            var snapshot = new List<(string Key, Response<Resp> Response)>();
            results.Range((key, rangedResponse) =>
            {
                snapshot.Add((key, rangedResponse!));
                return true;
            });

            // Prefer the searches from locations for default definition
            foreach (var (key, rangedResponse) in snapshot)
            {
                if (!rangedResponse._forOriginalLocation && seenProjects.AddIfAbsent(key) && rangedResponse._complete)
                {
                    yield return rangedResponse._result;
                }
            }

            // Then the searches from original locations
            foreach (var (key, rangedResponse) in snapshot)
            {
                if (rangedResponse._forOriginalLocation && seenProjects.AddIfAbsent(key) && rangedResponse._complete)
                {
                    yield return rangedResponse._result;
                }
            }
        }

        // Outer loop - to complete work if more is added after completing existing queue
        while (true)
        {
            // Process existing known projects first
            wg.RunAndWait();
            // No need to use mu here since we are not in parallel at this point
            if (panicsOccurred.Count != 0)
            {
                throw new DebugFailureException($"Panics occurred during cross-project handling: {string.Join(", ", panicsOccurred)}");
            }

            if (ctx.CancellationToken.IsCancellationRequested)
            {
                return (resp, new OperationCanceledException(ctx.CancellationToken));
            }

            if (err is not null)
            {
                return (resp, err);
            }

            wg = NewWorkGroup(false);
            var hasMoreWork = false;
            if (defaultDefinition is not null)
            {
                var requestedProjectTrees = new Set<TSPath.Path>();
                results.Range((key, rangedResponse) =>
                {
                    if (rangedResponse!._complete)
                    {
                        requestedProjectTrees.Add(new TSPath.Path(key));
                    }

                    return true;
                });

                // Load more projects based on default definition found
                foreach (var loadedProject in orchestrator.GetProjectsLoadingProjectTree(ctx, requestedProjectTrees))
                {
                    if (ctx.CancellationToken.IsCancellationRequested)
                    {
                        return (resp, new OperationCanceledException(ctx.CancellationToken));
                    }

                    // Can loop forever without this (enqueue here, dequeue above, repeat)
                    if (!CanSearchProject(loadedProject) || loadedProject.GetProgram() is null)
                    {
                        continue;
                    }

                    // Enqueue the project and location for further processing
                    var definitionUri = defaultDefinition.TextDocumentURI();
                    if (loadedProject.HasFile(definitionUri.FileName()))
                    {
                        EnqueueItem(new ProjectAndTextDocumentPosition
                        {
                            _project = loadedProject,
                            Uri = definitionUri,
                            Position = defaultDefinition.TextDocumentPosition(),
                        });
                        hasMoreWork = true;
                    }
                    else
                    {
                        var sourcePos = defaultDefinition.GetSourcePosition.Value;
                        if (sourcePos is not null && loadedProject.HasFile(sourcePos.TextDocumentURI().FileName()))
                        {
                            EnqueueItem(new ProjectAndTextDocumentPosition
                            {
                                _project = loadedProject,
                                Uri = sourcePos.TextDocumentURI(),
                                Position = sourcePos.TextDocumentPosition(),
                            });
                            hasMoreWork = true;
                        }
                        else
                        {
                            var generatedPos = defaultDefinition.GetGeneratedPosition.Value;
                            if (generatedPos is not null && loadedProject.HasFile(generatedPos.TextDocumentURI().FileName()))
                            {
                                EnqueueItem(new ProjectAndTextDocumentPosition
                                {
                                    _project = loadedProject,
                                    Uri = generatedPos.TextDocumentURI(),
                                    Position = generatedPos.TextDocumentPosition(),
                                });
                                hasMoreWork = true;
                            }
                        }
                    }
                }
            }

            if (!hasMoreWork)
            {
                break;
            }
        }

        if (results.Size() > 1)
        {
            resp = combineResults(GetResultsIterator());
        }
        else
        {
            // Single result, return that directly
            foreach (var value in GetResultsIterator())
            {
                resp = value;
                break;
            }
        }

        return (resp, null);
    }
}

public static partial class Ls
{
    internal static List<T> CombineLocationArray<T>(List<T> combined, T?[] locations, Set<Location> seen)
        where T : class, HasLocation
    {
        foreach (var loc in locations)
        {
            if (loc is not null && seen.AddIfAbsent(loc.GetLocation()))
            {
                combined.Add(loc);
            }
        }

        return combined;
    }

    internal static Location[] CombineResponseLocations<T>(IEnumerable<T> results)
        where T : HasLocations
    {
        var combined = new List<Location>();
        var seenLocations = new Set<Location>();
        foreach (var resp in results)
        {
            var locations = resp.GetLocations();
            if (locations is not null)
            {
                combined = CombineLocationArray(combined, locations, seenLocations);
            }
        }

        return combined.ToArray();
    }

    internal static LocationsOrNull CombineReferences(IEnumerable<LocationsOrNull> results)
    {
        return new LocationsOrNull { Locations = CombineResponseLocations(results) };
    }

    /// <summary>Go 的 `newItem := *item` 结构体浅拷贝（生成物无 `Clone`，逐字段复刻）。</summary>
    internal static VSReferenceItem ShallowCloneVSReferenceItem(VSReferenceItem item)
    {
        return new VSReferenceItem
        {
            VSId = item.VSId,
            VSDefinitionId = item.VSDefinitionId,
            VSKind = item.VSKind,
            VSLocation = item.VSLocation,
            VSDefinitionText = item.VSDefinitionText,
            VSProjectName = item.VSProjectName,
            VSContainingType = item.VSContainingType,
        };
    }

    internal static VSReferenceItemsOrNull CombineVSReferences(IEnumerable<VSReferenceItemsOrNull> results)
    {
        var combined = new List<VSReferenceItem>();
        // Re-number IDs across projects to maintain unique IDs and correct definition references
        var nextId = 0;
        foreach (var resp in results)
        {
            if (resp.VSReferenceItems is null)
            {
                continue;
            }

            // Map old IDs to new IDs for this batch
            var idMap = new Dictionary<int, int>();
            foreach (var item in resp.VSReferenceItems)
            {
                if (item is null)
                {
                    continue;
                }

                var oldId = item.VSId;
                var newId = nextId;
                idMap[oldId] = newId;
                nextId++;

                var newItem = ShallowCloneVSReferenceItem(item);
                newItem.VSId = newId;
                if (item.VSDefinitionId is not null)
                {
                    var newDefId = idMap[item.VSDefinitionId.Value];
                    newItem.VSDefinitionId = newDefId;
                }

                combined.Add(newItem);
            }
        }

        return new VSReferenceItemsOrNull { VSReferenceItems = combined.ToArray() };
    }

    internal static LocationOrLocationsOrDefinitionLinksOrNull CombineImplementations(IEnumerable<LocationOrLocationsOrDefinitionLinksOrNull> results)
    {
        var combined = new List<LocationLink>();
        var seenLocations = new Set<Location>();
        foreach (var resp in results)
        {
            if (resp.DefinitionLinks is not null)
            {
                combined = CombineLocationArray(combined, resp.DefinitionLinks, seenLocations);
            }
            else if (resp.Locations is not null)
            {
                return new LocationOrLocationsOrDefinitionLinksOrNull { Locations = CombineResponseLocations(results) };
            }
        }

        return new LocationOrLocationsOrDefinitionLinksOrNull { DefinitionLinks = combined.ToArray() };
    }

    internal static WorkspaceEditOrNull CombineRenameResponse(IEnumerable<WorkspaceEditOrNull> results)
    {
        var combined = new Dictionary<DocumentUri, List<TextEdit>>();
        var seenChanges = new Dictionary<DocumentUri, Set<Range>>();
        var documentChanges = new List<TextDocumentEditOrCreateFileOrRenameFileOrDeleteFile>();
        var seenRenames = new Set<(DocumentUri OldUri, DocumentUri NewUri)>();

        foreach (var resp in results)
        {
            if (resp.WorkspaceEdit is not null && resp.WorkspaceEdit.DocumentChanges is not null)
            {
                foreach (var change in resp.WorkspaceEdit.DocumentChanges)
                {
                    if (change.RenameFile is not null)
                    {
                        var key = (change.RenameFile.OldUri, change.RenameFile.NewUri);
                        if (seenRenames.AddIfAbsent(key))
                        {
                            documentChanges.Add(change);
                        }
                    }
                    else
                    {
                        documentChanges.Add(change);
                    }
                }
            }

            if (resp.WorkspaceEdit is not null && resp.WorkspaceEdit.Changes is not null)
            {
                foreach (var (doc, changes) in resp.WorkspaceEdit.Changes)
                {
                    if (!seenChanges.TryGetValue(doc, out var seenSet))
                    {
                        seenSet = new Set<Range>();
                        seenChanges[doc] = seenSet;
                    }

                    if (!combined.TryGetValue(doc, out var changesForDoc))
                    {
                        changesForDoc = [];
                        combined[doc] = changesForDoc;
                    }

                    foreach (var change in changes ?? [])
                    {
                        if (change is not null && !seenSet.Has(change.Range))
                        {
                            seenSet.Add(change.Range);
                            changesForDoc.Add(change);
                        }
                    }
                }
            }
        }

        if (documentChanges.Count > 0 || combined.Count > 0)
        {
            var workspaceEdit = new WorkspaceEdit();
            if (documentChanges.Count > 0)
            {
                workspaceEdit.DocumentChanges = documentChanges.ToArray();
            }

            if (combined.Count > 0)
            {
                var changes = new Dictionary<DocumentUri, TextEdit?[]?>();
                foreach (var (doc, edits) in combined)
                {
                    changes[doc] = edits.ToArray();
                }

                workspaceEdit.Changes = changes;
            }

            return new WorkspaceEditOrNull
            {
                WorkspaceEdit = workspaceEdit,
            };
        }

        return new WorkspaceEditOrNull();
    }

    internal static CallHierarchyIncomingCallsOrNull CombineIncomingCalls(IEnumerable<CallHierarchyIncomingCallsOrNull> results)
    {
        var combined = new List<CallHierarchyIncomingCall>();
        var seenCalls = new Set<Location>();
        foreach (var resp in results)
        {
            if (resp.CallHierarchyIncomingCalls is not null)
            {
                foreach (var call in resp.CallHierarchyIncomingCalls)
                {
                    if (call is not null && seenCalls.AddIfAbsent(call.From!.GetLocation()))
                    {
                        combined.Add(call);
                    }
                }
            }
        }

        return new CallHierarchyIncomingCallsOrNull { CallHierarchyIncomingCalls = combined.ToArray() };
    }
}
