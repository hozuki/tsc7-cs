// ported from: internal/execute/incremental/affectedfileshandler.go
//
// 与 Go 的偏离（docs/architecture.md §2/§3）：
// 1) Go 的 `type dtsMayChange map[tspath.Path]FileEmitKind` → 继承 `Dictionary<Path, FileEmitKind>` 的
//    `internal sealed class DtsMayChange`（Go 的具名 map 方法照搬）。Go 的 `[]dtsMayChange` → `List<DtsMayChange>`。
// 2) `atomic.Bool` → `volatile bool`；`sync.Once` → 锁 + 标志位；`sync.Mutex` → `lock`。
// 3) `func(currentFile *ast.SourceFile, currentPath tspath.Path) (queueForFile bool, fastReturn bool)` →
//    `Func<SourceFile?, Path, (bool QueueForFile, bool FastReturn)>`。
// 4) `core.Filter(slices.Collect(maps.Values(m)), f)` → `Filter([.. m.Values], f)`（顺序与 Go 一样不保证）。
// 5) `symbol.Declarations` / `symbol.Exports` 是 Go 的导出字段 → C# 的公开字段（可能是 null → `?? []`；
//    `Exports` 是 `SymbolTable`（`Dictionary<string, Symbol>`），Go 的 `for _, x := range map` 对应 `.Values`）。
// 6) `checker.SkipAlias` / `checker.Checker` 是包级/静态函数 → 全限定 `TypeScript.Checker.Checker.SkipAlias`。
namespace TypeScript.Execute.Incremental;

using TypeScript.Ast;
using TypeScript.Collections;
using TypeScript.Compiler;
using TypeScript.Core;
using static TypeScript.Ast.Ast;
using static TypeScript.Core.Core;
using static TypeScript.Execute.Incremental.Incremental;
using static TypeScript.TSPath.TSPath;
using CompilerProgram = TypeScript.Compiler.Program;
using Path = TypeScript.TSPath.Path;

/// <summary>Go: <c>type dtsMayChange map[tspath.Path]FileEmitKind</c>（affectedfileshandler.go:18）。</summary>
internal sealed class DtsMayChange : Dictionary<Path, FileEmitKind>
{
    internal void AddFileToAffectedFilesPendingEmit(Path filePath, FileEmitKind emitKind)
    {
        this[filePath] = emitKind;
    }
}

/// <summary>Go: <c>type updatedSignature struct</c>（affectedfileshandler.go:24-28）。</summary>
internal sealed class UpdatedSignature
{
    internal readonly object _mu = new();
    internal string _signature = "";
    internal SignatureUpdateKind _kind;
}

/// <summary>Go: <c>type affectedFilesHandler struct</c>（affectedfileshandler.go:30-39）。</summary>
internal sealed class AffectedFilesHandler
{
    internal Context _ctx = null!;
    internal Program _program = null!;
    internal volatile bool _hasAllFilesExcludingDefaultLibraryFile;
    internal SyncMap<Path, UpdatedSignature> _updatedSignatures = new();
    internal List<DtsMayChange> _dtsMayChange = [];
    internal SyncSet<Path> _filesToRemoveDiagnostics = new();
    internal SyncMap<Path, bool> _seenFileAndReferences = new();

    // Go: cleanedDiagnosticsOfLibFiles sync.Once
    private readonly object _cleanedDiagnosticsOfLibFilesLock = new();
    private bool _cleanedDiagnosticsOfLibFiles;

    internal DtsMayChange GetDtsMayChange(Path affectedFilePath, FileEmitKind affectedFileEmitKind)
    {
        var result = new DtsMayChange { [affectedFilePath] = affectedFileEmitKind };
        _dtsMayChange.Add(result);
        return result;
    }

    internal bool IsChangedSignature(Path path)
    {
        var (newSignature, _) = _updatedSignatures.Load(path);
        // This method is called after updating signatures of that path, so signature is present in updatedSignatures
        // And is already calculated, so no need to lock and unlock mutex on the entry
        var (oldInfo, _) = _program._snapshot._fileInfos.Load(path);
        return newSignature!._signature != oldInfo!._signature;
    }

    internal void RemoveSemanticDiagnosticsOf(Path path)
    {
        _filesToRemoveDiagnostics.Add(path);
    }

    internal void RemoveDiagnosticsOfLibraryFiles()
    {
        lock (_cleanedDiagnosticsOfLibFilesLock)
        {
            if (_cleanedDiagnosticsOfLibFiles)
            {
                return;
            }
            _cleanedDiagnosticsOfLibFiles = true;
        }
        foreach (var file in _program.GetSourceFiles())
        {
            if (_program._program!.IsSourceFileDefaultLibrary(file.Path()) && !_program._program.SkipTypeChecking(file, true))
            {
                RemoveSemanticDiagnosticsOf(file.Path());
            }
        }
    }

    internal string ComputeDtsSignature(SourceFile file)
    {
        var signature = "";
        var done = _program.BeginNestedEmit();
        try
        {
            _program._program!.Emit(_ctx, new EmitOptions
            {
                TargetSourceFiles = SingleElementSlice(file),
                EmitOnly = EmitOnly.EmitOnlyBuilderSignature,
                WriteFile = (fileName, text, data) =>
                {
                    if (!IsDeclarationFileName(fileName))
                    {
                        Debug.Fail("File extension for signature expected to be dts, got : " + fileName);
                    }
                    signature = _program._snapshot.ComputeSignatureWithDiagnostics(file, text, data);
                },
            });
        }
        finally
        {
            done();
        }
        return signature;
    }

    internal bool UpdateShapeSignature(SourceFile file, bool useFileVersionAsSignature)
    {
        var update = new UpdatedSignature();
        lock (update._mu)
        {
            // If we have cached the result for this file, that means hence forth we should assume file shape is uptodate
            var (existing, loaded) = _updatedSignatures.LoadOrStore(file.Path(), update);
            if (loaded)
            {
                // Ensure calculations for existing ones are complete before using the value
                lock (existing!._mu)
                {
                    return false;
                }
            }

            var (info, _) = _program._snapshot._fileInfos.Load(file.Path());
            var prevSignature = info!._signature;
            // JSON files have no declaration output from which to compute a shape
            // signature, so use the file version to conservatively invalidate dependents.
            if (!file.IsDeclarationFile && !IsJsonSourceFile(file) && !useFileVersionAsSignature)
            {
                update._signature = ComputeDtsSignature(file);
            }
            // Default is to use file version as signature
            if (update._signature == "")
            {
                update._signature = info._version;
                update._kind = SignatureUpdateKind.UsedVersion;
            }
            return update._signature != prevSignature;
        }
    }

    internal SourceFile[] GetFilesAffectedBy(Path path)
    {
        var file = _program._program!.GetSourceFileByPath(path);
        if (file is null)
        {
            return [];
        }

        if (!UpdateShapeSignature(file, false))
        {
            return [file];
        }

        var (info, _) = _program._snapshot._fileInfos.Load(file.Path());
        if (info!._affectsGlobalScope)
        {
            _hasAllFilesExcludingDefaultLibraryFile = true;
            return _program._snapshot.GetAllFilesExcludingDefaultLibraryFile(_program._program, file);
        }

        if (_program._snapshot._options.IsolatedModules.IsTrue())
        {
            return [file];
        }

        // Now we need to if each file in the referencedBy list has a shape change as well.
        // Because if so, its own referencedBy files need to be saved as well to make the
        // emitting result consistent with files on disk.
        var seenFileNamesMap = ForEachFileReferencedBy(
            file,
            (currentFile, currentPath) =>
            {
                // If the current file is not nil and has a shape change, we need to queue it for processing
                if (currentFile is not null && UpdateShapeSignature(currentFile, false))
                {
                    return (true, false);
                }
                return (false, false);
            });
        // Return array of values that needs emit
        var filesNeedingEmit = new List<SourceFile>();
        foreach (var affected in seenFileNamesMap.Values)
        {
            if (affected is not null)
            {
                filesNeedingEmit.Add(affected);
            }
        }
        return [.. filesNeedingEmit];
    }

    internal Dictionary<Path, SourceFile?> ForEachFileReferencedBy(SourceFile file, Func<SourceFile?, Path, (bool QueueForFile, bool FastReturn)> fn)
    {
        // Now we need to if each file in the referencedBy list has a shape change as well.
        // Because if so, its own referencedBy files need to be saved as well to make the
        // emitting result consistent with files on disk.
        var seenFileNamesMap = new Dictionary<Path, SourceFile?>();
        // Start with the paths this file was referenced by
        seenFileNamesMap[file.Path()] = file;
        var queue = new List<Path>(_program._snapshot._referencedMap.GetReferencedBy(file.Path()));
        while (queue.Count > 0)
        {
            var currentPath = queue[^1];
            queue.RemoveAt(queue.Count - 1);
            if (!seenFileNamesMap.ContainsKey(currentPath))
            {
                var currentFile = _program._program!.GetSourceFileByPath(currentPath);
                seenFileNamesMap[currentPath] = currentFile;
                var (queueForFile, fastReturn) = fn(currentFile, currentPath);
                if (fastReturn)
                {
                    return seenFileNamesMap;
                }
                if (queueForFile)
                {
                    foreach (var @ref in _program._snapshot._referencedMap.GetReferencedBy(currentFile!.Path()))
                    {
                        queue.Add(@ref);
                    }
                }
            }
        }
        return seenFileNamesMap;
    }

    /// <summary>
    /// Handles semantic diagnostics and dts emit for affectedFile and files, that are referencing modules that export
    /// entities from affected file. This is because even though js emit doesnt change, dts emit / type used can change
    /// resulting in need for dts emit and js change.
    /// </summary>
    internal void HandleDtsMayChangeOfAffectedFile(DtsMayChange dtsMayChange, SourceFile affectedFile)
    {
        RemoveSemanticDiagnosticsOf(affectedFile.Path());

        // If affected files is everything except default library, then nothing more to do
        if (_hasAllFilesExcludingDefaultLibraryFile)
        {
            RemoveDiagnosticsOfLibraryFiles();
            // When a change affects the global scope, all files are considered to be affected without updating their signature
            // That means when affected file is handled, its signature can be out of date
            // To avoid this, ensure that we update the signature for any affected file in this scenario.
            UpdateShapeSignature(affectedFile, false);
            return;
        }

        if (_program._snapshot._options.AssumeChangesOnlyAffectDirectDependencies.IsTrue())
        {
            return;
        }

        // Iterate on referencing modules that export entities from affected file and delete diagnostics and add pending emit
        // If there was change in signature (dts output) for the changed file,
        // then only we need to handle pending file emit
        if (!_program._snapshot._changedFilesSet.Has(affectedFile.Path()) ||
            !IsChangedSignature(affectedFile.Path()))
        {
            return;
        }

        // At this point affectedFile is actually one of the changed files
        // that has some change in its .d.ts signature.

        // Since isolated modules dont change js files, files affected by change in signature is itself
        // But we need to cleanup semantic diagnostics and queue dts emit for affected files
        if (_program._snapshot._options.IsolatedModules.IsTrue())
        {
            ForEachFileReferencedBy(
                affectedFile,
                (currentFile, currentPath) =>
                {
                    if (HandleDtsMayChangeOfGlobalScope(dtsMayChange, currentPath /*invalidateJsFiles*/, false))
                    {
                        return (false, true);
                    }
                    HandleDtsMayChangeOf(dtsMayChange, currentPath /*invalidateJsFiles*/, false);
                    if (IsChangedSignature(currentPath))
                    {
                        return (true, false);
                    }
                    return (false, false);
                });
        }

        var invalidateJsFiles = false;
        TypeScript.Checker.Checker? typeChecker = null;
        Action? done = null;
        // If exported const enum, we need to ensure that js files are emitted as well since the const enum value changed
        if (affectedFile.Symbol is not null && affectedFile.Symbol.Exports is not null)
        {
            foreach (var exported in affectedFile.Symbol.Exports.Values)
            {
                if ((exported.Flags & SymbolFlags.ConstEnum) != 0)
                {
                    invalidateJsFiles = true;
                    break;
                }
                if (typeChecker is null)
                {
                    (typeChecker, done) = _program._program!.GetTypeCheckerForFileExclusive(_ctx, affectedFile);
                }
                var aliased = TypeScript.Checker.Checker.SkipAlias(exported, typeChecker);
                if (ReferenceEquals(aliased, exported))
                {
                    continue;
                }
                if ((aliased.Flags & SymbolFlags.ConstEnum) != 0)
                {
                    if (Some(aliased.Declarations, d => ReferenceEquals(GetSourceFileOfNode(d), affectedFile)))
                    {
                        invalidateJsFiles = true;
                        break;
                    }
                }
            }
        }
        done?.Invoke();

        // Go through files that reference affected file and handle dts emit and semantic diagnostics for them and their references
        foreach (var fileReferencingChangedFile in _program._snapshot._referencedMap.GetReferencedBy(affectedFile.Path()))
        {
            if (HandleDtsMayChangeOfGlobalScope(dtsMayChange, fileReferencingChangedFile, invalidateJsFiles))
            {
                return;
            }
            // Since references of changed file = affected files - we would have already handled d.ts emit and semantic diagnostics
            // for those files. Now we need to handle files referencing those affected files to ensure correctness.
            foreach (var fileReferencingAffectedFile in _program._snapshot._referencedMap.GetReferencedBy(fileReferencingChangedFile))
            {
                if (HandleDtsMayChangeOfFileAndReferences(dtsMayChange, fileReferencingAffectedFile, invalidateJsFiles))
                {
                    return;
                }
            }
        }
    }

    internal bool HandleDtsMayChangeOfFileAndReferences(DtsMayChange dtsMayChange, Path filePath, bool invalidateJsFiles)
    {
        var (existing, loaded) = _seenFileAndReferences.LoadOrStore(filePath, invalidateJsFiles);
        if (loaded && (existing || !invalidateJsFiles))
        {
            return false;
        }
        else if (loaded && invalidateJsFiles)
        {
            _seenFileAndReferences.Store(filePath, true);
        }

        if (HandleDtsMayChangeOfGlobalScope(dtsMayChange, filePath, invalidateJsFiles))
        {
            return true;
        }
        HandleDtsMayChangeOf(dtsMayChange, filePath, invalidateJsFiles);

        // Remove the diagnostics of files that import this file and
        // any files that are referenced by it (directly or indirectly)
        foreach (var referencingFilePath in _program._snapshot._referencedMap.GetReferencedBy(filePath))
        {
            if (HandleDtsMayChangeOfFileAndReferences(dtsMayChange, referencingFilePath, invalidateJsFiles))
            {
                return true;
            }
        }
        return false;
    }

    internal bool HandleDtsMayChangeOfGlobalScope(DtsMayChange dtsMayChange, Path filePath, bool invalidateJsFiles)
    {
        var (info, ok) = _program._snapshot._fileInfos.Load(filePath);
        if (!ok || !info!._affectsGlobalScope)
        {
            return false;
        }
        // Every file needs to be handled
        foreach (var file in _program._snapshot.GetAllFilesExcludingDefaultLibraryFile(_program._program!, null))
        {
            HandleDtsMayChangeOf(dtsMayChange, file.Path(), invalidateJsFiles);
        }
        RemoveDiagnosticsOfLibraryFiles();
        return true;
    }

    /// <summary>
    /// Handle the dts may change, so they need to be added to pending emit if dts emit is enabled,
    /// Also we need to make sure signature is updated for these files.
    /// </summary>
    internal void HandleDtsMayChangeOf(DtsMayChange dtsMayChange, Path path, bool invalidateJsFiles)
    {
        if (_program._snapshot._changedFilesSet.Has(path))
        {
            return;
        }
        var file = _program._program!.GetSourceFileByPath(path);
        if (file is null)
        {
            return;
        }
        RemoveSemanticDiagnosticsOf(path);
        // Even though the js emit doesnt change and we are already handling dts emit and semantic diagnostics
        // we need to update the signature to reflect correctness of the signature(which is output d.ts emit) of this file
        // This ensures that we dont later during incremental builds considering wrong signature.
        // Eg where this also is needed to ensure that .tsbuildinfo generated by incremental build should be same as if it was first fresh build
        // But we avoid expensive full shape computation, as using file version as shape is enough for correctness.
        UpdateShapeSignature(file, true);
        // If not dts emit, nothing more to do
        if (invalidateJsFiles)
        {
            dtsMayChange.AddFileToAffectedFilesPendingEmit(path, GetFileEmitKind(_program._snapshot._options));
        }
        else if (_program._snapshot._options.GetEmitDeclarations())
        {
            dtsMayChange.AddFileToAffectedFilesPendingEmit(path, IfElse(_program._snapshot._options.DeclarationMap.IsTrue(), FileEmitKind.AllDts, FileEmitKind.Dts));
        }
    }

    internal void UpdateSnapshot()
    {
        if (_ctx.CancellationToken.IsCancellationRequested)
        {
            return;
        }
        _updatedSignatures.Range((filePath, update) =>
        {
            var (info, ok) = _program._snapshot._fileInfos.Load(filePath);
            if (ok)
            {
                info!._signature = update!._signature;
                if (_program._testingData is not null)
                {
                    _program._testingData.UpdatedSignatureKinds[filePath] = update._kind;
                }
            }
            return true;
        });
        _filesToRemoveDiagnostics.Range(file =>
        {
            _program._snapshot._semanticDiagnosticsPerFile.Delete(file);
            return true;
        });
        foreach (var change in _dtsMayChange)
        {
            foreach (var (filePath, emitKind) in change)
            {
                _program._snapshot.AddFileToAffectedFilesPendingEmit(filePath, emitKind);
            }
        }
        _program._snapshot._changedFilesSet = new TypeScript.Collections.SyncSet<Path>();
        _program._snapshot._buildInfoEmitPending = true;
    }
}

public static partial class Incremental
{
    /// <summary>Go: <c>func collectAllAffectedFiles(ctx context.Context, program *Program)</c>（affectedfileshandler.go:359）。</summary>
    internal static void CollectAllAffectedFiles(Context ctx, Program program)
    {
        if (program._snapshot._changedFilesSet.Size() == 0)
        {
            return;
        }

        var handler = new AffectedFilesHandler { _ctx = ctx, _program = program };
        var wg = NewWorkGroup(handler._program._program!.SingleThreaded());
        var result = new SyncSet<SourceFile>();
        program._snapshot._changedFilesSet.Range(file =>
        {
            wg.Queue(() =>
            {
                foreach (var affectedFile in handler.GetFilesAffectedBy(file))
                {
                    result.Add(affectedFile);
                }
            });
            return true;
        });
        wg.RunAndWait();

        if (ctx.CancellationToken.IsCancellationRequested)
        {
            return;
        }

        // For all the affected files, get all the files that would need to change their dts or js files,
        // update their diagnostics
        wg = NewWorkGroup(program._program!.SingleThreaded());
        var emitKind = GetFileEmitKind(program._snapshot._options);
        result.Range(file =>
        {
            // remove the cached semantic diagnostics and handle dts emit and js emit if needed
            var dtsMayChange = handler.GetDtsMayChange(file.Path(), emitKind);
            wg.Queue(() =>
            {
                handler.HandleDtsMayChangeOfAffectedFile(dtsMayChange, file);
            });
            return true;
        });
        wg.RunAndWait();

        // Update the snapshot with the new state
        handler.UpdateSnapshot();
    }
}
