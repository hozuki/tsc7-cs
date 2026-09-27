// ported from: internal/project/api.go:1-62
//
// 与 Go 的偏离（逐条说明）：
//
// 1) `APIUpdate` 在 Go 里返回 `(*Snapshot, error)`；按仓规 §3.2（`(T, error)` → 正常路径返回 T、
//    失败抛异常）落成 **`Snapshot APIUpdate(...)` + 失败抛异常**：正常路径返回一个**已加引用**的快照
//    （调用方用完必须 `Deref()`），失败时先 `Deref()` 被拒的快照（Go 的 `newSnapshot.Deref()`），
//    再抛出 `newSnapshot._apiError`（**错误对象就是 `Snapshot._apiError`**，与 Go 逐条对应）。
//    Go 里"用新的快照继续传递 error 值"的语义在 C# 由异常的 `InnerException`/`Data` 之外的信息承载：
//    抛出的就是那个 error 对象本身，调用方 `catch (Exception e)` 拿到的即 Go 的 `err`。
//
// 2) `mergeFileChangeSummary(&fileChanges, apiFileChanges)` → `FileChangeSummary.MergeFileChangeSummary(...)`
//    （本仓既有落点，见 FileChange.cs 文件头第 5 条）。
//
// 3) `Session` 是 `partial`（主实现见 `Project/Session.cs`），本文件只放 api.go 的两个方法。
using TypeScript.Core;
using Path = TypeScript.TSPath.Path;

namespace TypeScript.Project;

public sealed partial class Session
{
    /// <summary>APIUpdate creates a new snapshot incorporating the given file changes and the
    /// supplied API open/close request. The apiRequest may open or close projects and
    /// files; opens are tracked in the snapshot (ref-counted) so they persist across
    /// future updates, and closes release a previously taken ref. Programs are updated
    /// only when explicitly requested by an open or ensure operation.
    /// On success, returns a ref'd snapshot which the caller must Deref when done.
    /// On failure, releases the rejected snapshot and throws the error（api.go:16-48，
    /// 返回形状见文件头第 1 条）。</summary>
    public Snapshot APIUpdate(Context ctx, FileChangeSummary apiFileChanges, APISnapshotRequest? apiRequest)
    {
        lock (_snapshotUpdateMu)
        {
            CancelScheduledSnapshotUpdate();

            var (hostFileChanges, overlays, ataChanges, _) = FlushChanges(ctx);
            var fileChanges = hostFileChanges.Clone();
            FileChangeSummary.MergeFileChangeSummary(fileChanges, apiFileChanges);
            Vfs.FS? fs = null;
            var replaceFileSystem = false;
            if (apiRequest is not null)
            {
                fs = apiRequest.FileSystem;
                replaceFileSystem = apiRequest.ReplaceFileSystem;
            }

            var newSnapshot = UpdateSnapshotRef(ctx, overlays, new SnapshotChange
            {
                _apiRequest = apiRequest,
                _fs = fs,
                _fileSystemOverride = fs is not null,
                _replaceFileSystem = replaceFileSystem,
                _fileChanges = fileChanges,
                _ataChanges = ataChanges,
            });
            if (newSnapshot._apiError is not null)
            {
                var apiError = newSnapshot._apiError;
                newSnapshot.Deref();
                if (!hostFileChanges.IsEmpty() || ataChanges.Count != 0)
                {
                    // The API request is rejected as a unit, but host changes were already
                    // flushed and must still advance the canonical session snapshot.
                    UpdateSnapshot(ctx, overlays, new SnapshotChange
                    {
                        _fileChanges = hostFileChanges,
                        _ataChanges = ataChanges,
                    });
                }

                throw apiError;
            }

            return newSnapshot;
        }
    }

    /// <summary>TryAdoptSnapshotInBackground retains a derived snapshot and attempts to adopt it
    /// as the session's current snapshot without blocking the caller（api.go:50-55）。</summary>
    public void TryAdoptSnapshotInBackground(Snapshot baseSnapshot, Snapshot newSnapshot)
    {
        RetainSnapshot(newSnapshot);
        TryAdoptSnapshotChangeInBackground(baseSnapshot, newSnapshot);
    }
}
