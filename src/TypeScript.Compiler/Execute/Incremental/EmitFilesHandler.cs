// ported from: internal/execute/incremental/emitfileshandler.go
//
// 与 Go 的偏离（docs/architecture.md §2/§3）：
// 1) Go 的 `type emitUpdate struct` / `type emitFilesHandler struct`（未导出）→ `internal sealed class EmitUpdate` /
//    `EmitFilesHandler`；包级函数 `emitFiles` → `Incremental.EmitFiles`。
// 2) `atomic.Bool` → `volatile bool`；`func(...) error` 的写盘回调 → 无返回值 + 抛异常（§3.2）；
//    Go 的 `*bool` 出参（`differsOnlyInMap`）→ `ref bool`；`time.Time` → `DateTime`（零值 `default`）。
// 3) Go 的 `emitUpdate{...}` 局部结构体 → C# 对象；`getEmitOptions` 的"结构体拷贝"语义（返回值是副本）→
//    未开 declaration 时直接返回同一个 `EmitOptions` 对象（调用点不修改它，可观察行为相同）。
// 4) `h.program.program.Emit(...)` → `h.program._program!.Emit(...)`；`compiler.CombineEmitResults` →
//    `Compiler.CombineEmitResults`（program.go 的包级函数）。
// 5) `data.SourceFile.Path()` 在 Go 里是 `*ast.SourceFile`（可能 nil）→ C# `data.SourceFile!`（同 Go 的 nil 解引用）。
namespace TypeScript.Execute.Incremental;

using TypeScript.Ast;
using TypeScript.Collections;
using TypeScript.Compiler;
using TypeScript.Core;
using static TypeScript.Core.Core;
using static TypeScript.Execute.Incremental.Incremental;
using static TypeScript.TSPath.TSPath;
using CompilerProgram = TypeScript.Compiler.Program;
using Path = TypeScript.TSPath.Path;

/// <summary>Go: <c>type emitUpdate struct</c>（emitfileshandler.go:15-19）。</summary>
internal sealed class EmitUpdate
{
    internal FileEmitKind _pendingKind;
    internal EmitResult? _result;
    internal bool _dtsErrorsFromCache;
}

/// <summary>Go: <c>type emitFilesHandler struct</c>（emitfileshandler.go:21-31）。</summary>
internal sealed class EmitFilesHandler
{
    internal Context _ctx = null!;
    internal Program _program = null!;
    internal bool _isForDtsErrors;
    internal SyncMap<Path, string> _signatures = new();
    internal SyncMap<Path, EmitSignature> _emitSignatures = new();
    internal SyncMap<Path, string> _latestChangedDtsFiles = new();
    internal Set<Path> _deletedPendingKinds = new();
    internal SyncMap<Path, EmitUpdate> _emitUpdates = new();
    internal volatile bool _hasEmitDiagnostics;

    /// <summary>Determining what all is pending to be emitted based on previous options or previous file emit flags。</summary>
    internal FileEmitKind GetPendingEmitKindForEmitOptions(FileEmitKind emitKind, EmitOptions options)
    {
        var pendingKind = GetPendingEmitKind(emitKind, 0);
        if (options.EmitOnly == EmitOnly.EmitOnlyDts)
        {
            pendingKind &= FileEmitKind.AllDts;
        }
        if (_isForDtsErrors)
        {
            pendingKind &= FileEmitKind.DtsErrors;
        }
        return pendingKind;
    }

    /// <summary>
    /// Emits the next affected file's emit result (EmitResult and sourceFiles emitted) or returns undefined if iteration is complete.
    /// The first of writeFile if provided, writeFile of BuilderProgramHost if provided, writeFile of compiler host
    /// in that order would be used to write the files.
    /// </summary>
    internal EmitResult? EmitAllAffectedFiles(EmitOptions options)
    {
        // Emit all affected files
        if (_program._snapshot.CanUseIncrementalState())
        {
            var results = EmitFilesIncremental(options);
            if (_isForDtsErrors)
            {
                if (options.TargetSourceFiles is not null)
                {
                    // Result from cache
                    var result = new EmitResult
                    {
                        EmitSkipped = true,
                        Diagnostics = FlatMap(options.TargetSourceFiles, targetFile =>
                        {
                            var (diagnostics, _) = _program._snapshot._emitDiagnosticsPerFile.Load(targetFile.Path());
                            return diagnostics!.GetDiagnostics(_program._program!, targetFile);
                        }),
                    };
                    UpdateHasEmitDiagnostics(result);
                    return result;
                }
                foreach (var result in results)
                {
                    UpdateHasEmitDiagnostics(result);
                }
                return TypeScript.Compiler.Compiler.CombineEmitResults(results);
            }
            else
            {
                // Combine results and update buildInfo
                var result = TypeScript.Compiler.Compiler.CombineEmitResults(results);
                UpdateHasEmitDiagnostics(result);
                EmitBuildInfo(options, result);
                return result;
            }
        }
        else if (!_isForDtsErrors)
        {
            var result = _program._program!.Emit(_ctx, GetEmitOptions(options));
            UpdateHasEmitDiagnostics(result);
            UpdateSnapshot();
            EmitBuildInfo(options, result!);
            return result;
        }
        else
        {
            Diagnostic[] diagnostics;
            if (options.TargetSourceFiles is null)
            {
                diagnostics = _program._program!.GetDeclarationDiagnostics(_ctx, null);
            }
            else
            {
                diagnostics = FlatMap(options.TargetSourceFiles, targetSourceFile => _program._program!.GetDeclarationDiagnostics(_ctx, targetSourceFile));
            }
            var result = new EmitResult
            {
                EmitSkipped = true,
                Diagnostics = diagnostics,
            };
            if (result.Diagnostics.Length != 0)
            {
                UpdateHasEmitDiagnostics(result);
                _program._snapshot._hasEmitDiagnostics = true;
            }
            return result;
        }
    }

    internal void UpdateHasEmitDiagnostics(EmitResult? result)
    {
        if (result is not null && result.Diagnostics.Length != 0)
        {
            _hasEmitDiagnostics = true;
        }
    }

    internal void EmitBuildInfo(EmitOptions options, EmitResult result)
    {
        var buildInfoResult = _program.EmitBuildInfo(_ctx, options);
        if (buildInfoResult is not null)
        {
            result.Diagnostics = [.. result.Diagnostics, .. buildInfoResult.Diagnostics];
            result.EmittedFiles = [.. result.EmittedFiles, .. buildInfoResult.EmittedFiles];
        }
    }

    internal EmitResult?[] EmitFilesIncremental(EmitOptions options)
    {
        // Get all affected files
        CollectAllAffectedFiles(_ctx, _program);
        if (_ctx.CancellationToken.IsCancellationRequested)
        {
            return [];
        }

        var wg = NewWorkGroup(_program._program!.SingleThreaded());
        _program._snapshot._affectedFilesPendingEmit.Range((path, emitKind) =>
        {
            var affectedFile = _program._program.GetSourceFileByPath(path);
            if (affectedFile is null || !_program._program.SourceFileMayBeEmitted(affectedFile, false))
            {
                _deletedPendingKinds.Add(path);
                return true;
            }
            var pendingKind = GetPendingEmitKindForEmitOptions(emitKind, options);
            if (pendingKind != 0)
            {
                wg.Queue(() =>
                {
                    // Determine if we can do partial emit
                    var emitOnly = EmitOnly.EmitAll;
                    if ((pendingKind & FileEmitKind.AllJs) != 0)
                    {
                        emitOnly = EmitOnly.EmitOnlyJs;
                    }
                    if ((pendingKind & FileEmitKind.AllDts) != 0)
                    {
                        if (emitOnly == EmitOnly.EmitOnlyJs)
                        {
                            emitOnly = EmitOnly.EmitAll;
                        }
                        else
                        {
                            emitOnly = EmitOnly.EmitOnlyDts;
                        }
                    }
                    EmitResult? result;
                    if (!_isForDtsErrors)
                    {
                        result = _program._program.Emit(_ctx, GetEmitOptions(new EmitOptions
                        {
                            TargetSourceFiles = SingleElementSlice(affectedFile),
                            EmitOnly = emitOnly,
                            WriteFile = options.WriteFile,
                        }));
                    }
                    else
                    {
                        result = new EmitResult
                        {
                            EmitSkipped = true,
                            Diagnostics = _program._program.GetDeclarationDiagnostics(_ctx, affectedFile),
                        };
                    }
                    UpdateHasEmitDiagnostics(result);

                    // Update the pendingEmit for the file
                    _emitUpdates.Store(path, new EmitUpdate { _pendingKind = GetPendingEmitKind(emitKind, pendingKind), _result = result });
                });
            }
            return true;
        });
        wg.RunAndWait();
        if (_ctx.CancellationToken.IsCancellationRequested)
        {
            return [];
        }

        // Get updated errors that were not included in affected files emit
        _program._snapshot._emitDiagnosticsPerFile.Range((path, diagnostics) =>
        {
            if (!_emitUpdates.Load(path).Ok)
            {
                var affectedFile = _program._program.GetSourceFileByPath(path);
                if (affectedFile is null || !_program._program.SourceFileMayBeEmitted(affectedFile, false))
                {
                    _deletedPendingKinds.Add(path);
                    return true;
                }
                var (pendingKind, _) = _program._snapshot._affectedFilesPendingEmit.Load(path);
                _emitUpdates.Store(path, new EmitUpdate
                {
                    _pendingKind = pendingKind,
                    _result = new EmitResult
                    {
                        EmitSkipped = true,
                        Diagnostics = diagnostics!.GetDiagnostics(_program._program, affectedFile),
                    },
                    _dtsErrorsFromCache = true,
                });
            }
            return true;
        });

        return UpdateSnapshot();
    }

    internal EmitOptions GetEmitOptions(EmitOptions options)
    {
        if (!_program._snapshot._options.GetEmitDeclarations())
        {
            return options;
        }
        var canUseIncrementalState = _program._snapshot.CanUseIncrementalState();
        return new EmitOptions
        {
            TargetSourceFiles = options.TargetSourceFiles,
            EmitOnly = options.EmitOnly,
            ForceEmit = options.ForceEmit,
            WriteFile = (fileName, text, data) =>
            {
                var differsOnlyInMap = false;
                if (IsDeclarationFileName(fileName))
                {
                    if (canUseIncrementalState)
                    {
                        var sourceFile = data.SourceFile!;
                        var emitSignature = "";
                        var (info, _) = _program._snapshot._fileInfos.Load(sourceFile.Path());
                        if (info!._signature == info._version)
                        {
                            var signature = _program._snapshot.ComputeSignatureWithDiagnostics(sourceFile, text, data);
                            // With d.ts diagnostics they are also part of the signature so emitSignature will be different from it since its just hash of d.ts
                            if (data.Diagnostics.Length == 0)
                            {
                                emitSignature = signature;
                            }
                            if (signature != info._version)
                            { // Update it
                                _signatures.Store(sourceFile.Path(), signature);
                            }
                        }

                        // Store d.ts emit hash so later can be compared to check if d.ts has changed.
                        // Currently we do this only for composite projects since these are the only projects that can be referenced by other projects
                        // and would need their d.ts change time in --build mode
                        if (SkipDtsOutputOfComposite(sourceFile, fileName, text, data, emitSignature, ref differsOnlyInMap))
                        {
                            return;
                        }
                    }
                }

                var aTime = default(DateTime);
                if (differsOnlyInMap)
                {
                    aTime = _program._host.GetMTime(fileName);
                }
                if (options.WriteFile is not null)
                {
                    options.WriteFile(fileName, text, data);
                }
                else
                {
                    _program._program!.Host().FS().WriteFile(fileName, text);
                }
                if (differsOnlyInMap)
                {
                    // Revert the time to original one
                    _program._host.SetMTime(fileName, aTime);
                }
            },
        };
    }

    /// <summary>
    /// Compare to existing computed signature and store it or handle the changes in d.ts map option from before.
    /// Returning true means that, we dont need to emit this d.ts file since its contents didnt change.
    /// </summary>
    internal bool SkipDtsOutputOfComposite(SourceFile file, string outputFileName, string text, WriteFileData data, string newSignature, ref bool differsOnlyInMap)
    {
        if (!_program._snapshot._options.Composite.IsTrue())
        {
            return false;
        }
        var oldSignature = "";
        var (oldSignatureFormat, ok) = _program._snapshot._emitSignatures.Load(file.Path());
        if (ok)
        {
            if (oldSignatureFormat!._signature != "")
            {
                oldSignature = oldSignatureFormat._signature;
            }
            else
            {
                oldSignature = oldSignatureFormat._signatureWithDifferentOptions![0];
            }
        }
        if (newSignature == "")
        {
            newSignature = _program._snapshot.ComputeHash(GetTextHandlingSourceMapForSignature(text, data));
        }
        // Dont write dts files if they didn't change
        if (newSignature == oldSignature)
        {
            // If the signature was encoded as string the dts map options match so nothing to do
            if (oldSignatureFormat is not null && oldSignatureFormat._signature == oldSignature)
            {
                data.SkippedDtsWrite = true;
                return true;
            }
            else
            {
                // Mark as differsOnlyInMap so that we can reverse the timestamp with --build so that
                // the downstream projects dont detect this as change in d.ts file
                differsOnlyInMap = _program.Options().Build.IsTrue();
            }
        }
        else
        {
            _latestChangedDtsFiles.Store(file.Path(), outputFileName);
        }
        _emitSignatures.Store(file.Path(), new EmitSignature { _signature = newSignature });
        return false;
    }

    internal EmitResult?[] UpdateSnapshot()
    {
        if (_program._snapshot.CanUseIncrementalState())
        {
            _signatures.Range((file, signature) =>
            {
                var (info, _) = _program._snapshot._fileInfos.Load(file);
                info!._signature = signature!;
                if (_program._testingData is not null)
                {
                    _program._testingData.UpdatedSignatureKinds[file] = SignatureUpdateKind.StoredAtEmit;
                }
                _program._snapshot._buildInfoEmitPending = true;
                return true;
            });
            _emitSignatures.Range((file, signature) =>
            {
                _program._snapshot._emitSignatures.Store(file, signature!);
                _program._snapshot._buildInfoEmitPending = true;
                return true;
            });
            foreach (var file in _deletedPendingKinds.Keys())
            {
                _program._snapshot._affectedFilesPendingEmit.Delete(file);
                _program._snapshot._buildInfoEmitPending = true;
            }
            // Always use correct order when to collect the result
            var results = new List<EmitResult>();
            foreach (var file in _program.GetSourceFiles())
            {
                var (latestChangedDtsFile, hasLatestChangedDtsFile) = _latestChangedDtsFiles.Load(file.Path());
                if (hasLatestChangedDtsFile)
                {
                    _program._snapshot._latestChangedDtsFile = latestChangedDtsFile!;
                    _program._snapshot._buildInfoEmitPending = true;
                    _program._snapshot._hasChangedDtsFile = true;
                }
                var (update, hasUpdate) = _emitUpdates.Load(file.Path());
                if (hasUpdate)
                {
                    if (!update!._dtsErrorsFromCache)
                    {
                        if (update._pendingKind == 0)
                        {
                            _program._snapshot._affectedFilesPendingEmit.Delete(file.Path());
                        }
                        else
                        {
                            _program._snapshot._affectedFilesPendingEmit.Store(file.Path(), update._pendingKind);
                        }
                        _program._snapshot._buildInfoEmitPending = true;
                    }
                    if (update._result is not null)
                    {
                        results.Add(update._result);
                        if (update._result.Diagnostics.Length != 0)
                        {
                            _program._snapshot._emitDiagnosticsPerFile.Store(file.Path(), new DiagnosticsOrBuildInfoDiagnosticsWithFileName { _diagnostics = update._result.Diagnostics });
                        }
                    }
                }
            }
            return [.. results];
        }
        else if (_hasEmitDiagnostics)
        {
            _program._snapshot._hasEmitDiagnostics = true;
        }
        return [];
    }
}

public static partial class Incremental
{
    /// <summary>Go: <c>func emitFiles(ctx context.Context, program *Program, options compiler.EmitOptions, isForDtsErrors bool) *compiler.EmitResult</c>（emitfileshandler.go:338）。</summary>
    internal static EmitResult? EmitFiles(Context ctx, Program program, EmitOptions options, bool isForDtsErrors)
    {
        var emitHandler = new EmitFilesHandler { _ctx = ctx, _program = program, _isForDtsErrors = isForDtsErrors };

        // Single file emit - do direct from program
        if (!isForDtsErrors && options.TargetSourceFiles is not null)
        {
            var result = program._program!.Emit(ctx, emitHandler.GetEmitOptions(options));
            emitHandler.UpdateHasEmitDiagnostics(result);
            if (ctx.CancellationToken.IsCancellationRequested)
            {
                return null;
            }
            emitHandler.UpdateSnapshot();
            return result;
        }

        // Emit only affected files if using builder for emit
        return emitHandler.EmitAllAffectedFiles(options);
    }
}
