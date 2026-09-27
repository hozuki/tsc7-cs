// ported from: internal/execute/incremental/programtosnapshot.go
//
// 与 Go 的偏离（docs/architecture.md §2/§3）：
// 1) Go 的 `type toProgramSnapshot struct`（未导出）→ `internal sealed class ToProgramSnapshot`。
// 2) `core.NewWorkGroup(program.SingleThreaded())` → `NewWorkGroup(bool)`（Core/Workgroup.cs）。
// 3) `!newReferences.Equals(oldReferences)`：Go 在 nil 接收者上调用 `Set.Equals`（两者都 nil 时为 true）→
//    C# 用 `SetEqualsOrBothNull` 复刻（实例方法不能在 null 上分派）。
// 4) `go f()` 的并发写 + `wg.RunAndWait()` → `wg.Queue(...)` + `RunAndWait()`。
// 5) `checker, done := program.GetTypeCheckerForFileExclusive(...); defer done()` →
//    `var (checker, done) = ...; try { ... } finally { done(); }`（Go 的 defer 语义）。
// 6) `map[tspath.Path]...` 取不存在的键得到零值 → C# `GetValueOrDefault(...)` / `TryGetValue`。
namespace TypeScript.Execute.Incremental;

using TypeScript.Ast;
using TypeScript.Collections;
using TypeScript.Compiler;
using TypeScript.Core;
using TypeScript.TsOptions;
using static TypeScript.Ast.Ast;
using static TypeScript.Core.Core;
using static TypeScript.TSPath.TSPath;
using static TypeScript.TsOptions.TsOptions;
using CompilerProgram = TypeScript.Compiler.Program;
using Path = TypeScript.TSPath.Path;

public static partial class Incremental
{
    /// <summary>Go: <c>func programToSnapshot(program *compiler.Program, oldProgram *Program, hashWithText bool) *snapshot</c>。</summary>
    internal static Snapshot ProgramToSnapshot(CompilerProgram program, Program? oldProgram, bool hashWithText)
    {
        if (oldProgram is not null && ReferenceEquals(oldProgram._program, program))
        {
            return oldProgram._snapshot;
        }
        var snapshot = new Snapshot
        {
            _options = program.Options(),
            _hashWithText = hashWithText,
            _checkPending = program.Options().NoCheck.IsTrue(),
        };
        var to = new ToProgramSnapshot
        {
            _program = program,
            _oldProgram = oldProgram,
            _snapshot = snapshot,
        };

        if (to._snapshot.CanUseIncrementalState())
        {
            to.ReuseFromOldProgram();
            to.ComputeProgramFileChanges();
            to.HandleFileDelete();
            to.HandleGlobalScopeChange();
            to.HandlePendingEmit();
            to.HandlePendingCheck();
        }
        return snapshot;
    }

    /// <summary>Go: <c>type toProgramSnapshot struct</c>（programtosnapshot.go:42-47）。</summary>
    internal sealed class ToProgramSnapshot
    {
        internal CompilerProgram _program = null!;
        internal Program? _oldProgram;
        internal Snapshot _snapshot = null!;
        internal bool _globalFileRemoved;

        internal void ReuseFromOldProgram()
        {
            if (_oldProgram is not null)
            {
                if (_snapshot._options.Composite.IsTrue())
                {
                    _snapshot._latestChangedDtsFile = _oldProgram._snapshot._latestChangedDtsFile;
                }
                // Copy old snapshot's changed files set
                _oldProgram._snapshot._changedFilesSet.Range(key =>
                {
                    _snapshot._changedFilesSet.Add(key);
                    return true;
                });
                _oldProgram._snapshot._affectedFilesPendingEmit.Range((key, emitKind) =>
                {
                    _snapshot._affectedFilesPendingEmit.Store(key, emitKind);
                    return true;
                });
                _snapshot._buildInfoEmitPending = _oldProgram._snapshot._buildInfoEmitPending;
                _snapshot._hasErrorsFromOldState = _oldProgram._snapshot._hasErrors;
                _snapshot._hasSemanticErrorsFromOldState = _oldProgram._snapshot._hasSemanticErrors;
                _snapshot._packageJsonsFromOldState = _oldProgram._snapshot._packageJsons;
                _snapshot._missingPackageJsonsFromOldState = _oldProgram._snapshot._missingPackageJsons;
            }
            else
            {
                _snapshot._buildInfoEmitPending = _snapshot._options.IsIncremental();
            }
        }

        internal void ComputeProgramFileChanges()
        {
            var canCopySemanticDiagnostics = _oldProgram is not null &&
                !CompilerOptionsAffectSemanticDiagnostics(_oldProgram._snapshot._options, _program.Options());
            // We can only reuse emit signatures (i.e. .d.ts signatures) if the .d.ts file is unchanged,
            // which will eg be depedent on change in options like declarationDir and outDir options are unchanged.
            // We need to look in oldState.compilerOptions, rather than oldCompilerOptions (i.e.we need to disregard useOldState) because
            // oldCompilerOptions can be undefined if there was change in say module from None to some other option
            // which would make useOldState as false since we can now use reference maps that are needed to track what to emit, what to check etc
            // but that option change does not affect d.ts file name so emitSignatures should still be reused.
            var canCopyEmitSignatures = _snapshot._options.Composite.IsTrue() &&
                _oldProgram is not null &&
                !CompilerOptionsAffectDeclarationPath(_oldProgram._snapshot._options, _program.Options());
            var copyDeclarationFileDiagnostics = canCopySemanticDiagnostics &&
                _snapshot._options.SkipLibCheck.IsTrue() == _oldProgram!._snapshot._options.SkipLibCheck.IsTrue();
            var copyLibFileDiagnostics = copyDeclarationFileDiagnostics &&
                _snapshot._options.SkipDefaultLibCheck.IsTrue() == _oldProgram!._snapshot._options.SkipDefaultLibCheck.IsTrue();

            var files = _program.GetSourceFiles();
            var wg = NewWorkGroup(_program.SingleThreaded());
            foreach (var file in files)
            {
                var currentFile = file;
                wg.Queue(() =>
                {
                    var versionText = currentFile.Text();
                    if (currentFile.ContentMapper() != "")
                    {
                        versionText = currentFile.OriginalText() + "\x00" + currentFile.ContentMapperTransformIdentity();
                    }
                    var version = _snapshot.ComputeHash(versionText);
                    var impliedNodeFormat = _program.GetSourceFileMetaData(currentFile.Path()).ImpliedNodeFormat;
                    var affectsGlobalScope = FileAffectsGlobalScope(currentFile);
                    var signature = "";
                    var newReferences = GetReferencedFiles(_program, currentFile);
                    if (newReferences is not null)
                    {
                        _snapshot._referencedMap.StoreReferences(currentFile.Path(), newReferences);
                    }
                    if (_oldProgram is not null)
                    {
                        var (oldFileInfo, ok) = _oldProgram._snapshot._fileInfos.Load(currentFile.Path());
                        if (ok)
                        {
                            signature = oldFileInfo!._signature;
                            if (oldFileInfo._version != version || oldFileInfo._affectsGlobalScope != affectsGlobalScope || oldFileInfo._impliedNodeFormat != impliedNodeFormat)
                            {
                                _snapshot.AddFileToChangeSet(currentFile.Path());
                            }
                            else
                            {
                                var (oldReferences, _) = _oldProgram._snapshot._referencedMap.GetReferences(currentFile.Path());
                                if (!SetEqualsOrBothNull(newReferences, oldReferences))
                                {
                                    // Referenced files changed
                                    _snapshot.AddFileToChangeSet(currentFile.Path());
                                }
                                else if (newReferences is not null)
                                {
                                    foreach (var refPath in newReferences.Keys())
                                    {
                                        if (_program.GetSourceFileByPath(refPath) is null)
                                        {
                                            if (_oldProgram._snapshot._fileInfos.Load(refPath).Ok)
                                            {
                                                // Referenced file was deleted in the new program
                                                _snapshot.AddFileToChangeSet(currentFile.Path());
                                                break;
                                            }
                                        }
                                    }
                                }
                            }
                        }
                        else
                        {
                            _snapshot.AddFileToChangeSet(currentFile.Path());
                        }
                        if (!_snapshot._changedFilesSet.Has(currentFile.Path()))
                        {
                            var (emitDiagnostics, emitOk) = _oldProgram._snapshot._emitDiagnosticsPerFile.Load(currentFile.Path());
                            if (emitOk)
                            {
                                _snapshot._emitDiagnosticsPerFile.Store(currentFile.Path(), RepopulateDiagnosticsOfFile(emitDiagnostics!, _program, currentFile));
                            }
                            if (canCopySemanticDiagnostics)
                            {
                                if ((!currentFile.IsDeclarationFile || copyDeclarationFileDiagnostics) &&
                                    (!_program.IsSourceFileDefaultLibrary(currentFile.Path()) || copyLibFileDiagnostics))
                                {
                                    // Unchanged file copy diagnostics
                                    var (diagnostics, diagOk) = _oldProgram._snapshot._semanticDiagnosticsPerFile.Load(currentFile.Path());
                                    if (diagOk)
                                    {
                                        _snapshot._semanticDiagnosticsPerFile.Store(currentFile.Path(), RepopulateDiagnosticsOfFile(diagnostics!, _program, currentFile));
                                    }
                                }
                            }
                        }
                        if (canCopyEmitSignatures)
                        {
                            var (oldEmitSignature, sigOk) = _oldProgram._snapshot._emitSignatures.Load(currentFile.Path());
                            if (sigOk)
                            {
                                _snapshot._emitSignatures.Store(currentFile.Path(), oldEmitSignature!.GetNewEmitSignature(_oldProgram._snapshot._options, _snapshot._options));
                            }
                        }
                    }
                    else
                    {
                        _snapshot.AddFileToAffectedFilesPendingEmit(currentFile.Path(), GetFileEmitKind(_snapshot._options));
                        signature = version;
                    }
                    _snapshot._fileInfos.Store(currentFile.Path(), new FileInfo
                    {
                        _version = version,
                        _signature = signature,
                        _affectsGlobalScope = affectsGlobalScope,
                        _impliedNodeFormat = impliedNodeFormat,
                    });
                });
            }
            wg.RunAndWait();
        }

        internal void HandleFileDelete()
        {
            if (_oldProgram is not null)
            {
                // If the global file is removed, add all files as changed
                _oldProgram._snapshot._fileInfos.Range((filePath, oldInfo) =>
                {
                    if (!_snapshot._fileInfos.Load(filePath).Ok)
                    {
                        if (oldInfo!._affectsGlobalScope)
                        {
                            foreach (var file in _snapshot.GetAllFilesExcludingDefaultLibraryFile(_program, null))
                            {
                                _snapshot.AddFileToChangeSet(file.Path());
                            }
                            _globalFileRemoved = true;
                        }
                        else
                        {
                            _snapshot._buildInfoEmitPending = true;
                        }
                        return false;
                    }
                    return true;
                });
            }
        }

        internal void HandleGlobalScopeChange()
        {
            if (_oldProgram is null || _globalFileRemoved)
            {
                return;
            }
            var globalScopeLost = false;
            _oldProgram._snapshot._fileInfos.Range((filePath, oldInfo) =>
            {
                if (!oldInfo!._affectsGlobalScope)
                {
                    return true;
                }
                var (newInfo, ok) = _snapshot._fileInfos.Load(filePath);
                if (ok && !newInfo!._affectsGlobalScope)
                {
                    globalScopeLost = true;
                    return false;
                }
                return true;
            });
            if (globalScopeLost)
            {
                foreach (var file in _snapshot.GetAllFilesExcludingDefaultLibraryFile(_program, null))
                {
                    _snapshot.AddFileToChangeSet(file.Path());
                }
            }
        }

        internal void HandlePendingEmit()
        {
            if (_oldProgram is not null && !_globalFileRemoved)
            {
                // If options affect emit, then we need to do complete emit per compiler options
                // otherwise only the js or dts that needs to emitted because its different from previously emitted options
                FileEmitKind pendingEmitKind;
                if (CompilerOptionsAffectEmit(_oldProgram._snapshot._options, _snapshot._options))
                {
                    pendingEmitKind = GetFileEmitKind(_snapshot._options);
                }
                else
                {
                    pendingEmitKind = GetPendingEmitKindWithOptions(_snapshot._options, _oldProgram._snapshot._options);
                }
                if (pendingEmitKind != FileEmitKind.None)
                {
                    // Add all files to affectedFilesPendingEmit since emit changed
                    foreach (var file in _program.GetSourceFiles())
                    {
                        // Add to affectedFilesPending emit only if not changed since any changed file will do full emit
                        if (!_snapshot._changedFilesSet.Has(file.Path()))
                        {
                            _snapshot.AddFileToAffectedFilesPendingEmit(file.Path(), pendingEmitKind);
                        }
                    }
                    _snapshot._buildInfoEmitPending = true;
                }
            }
        }

        internal void HandlePendingCheck()
        {
            if (_oldProgram is not null &&
                _snapshot._semanticDiagnosticsPerFile.Size() != _program.GetSourceFiles().Length &&
                _oldProgram._snapshot._checkPending != _snapshot._checkPending)
            {
                _snapshot._buildInfoEmitPending = true;
            }
        }
    }

    /// <summary>Go 的 <c>!a.Equals(b)</c>（nil 接收者语义照 Go 的 Set.Equals：两者都 nil → true）。</summary>
    internal static bool SetEqualsOrBothNull<T>(Set<T>? a, Set<T>? b) => a is null ? b is null : a.Equals(b);

    /// <summary>Go: <c>func fileAffectsGlobalScope(file *ast.SourceFile) bool</c>（programtosnapshot.go:235）。</summary>
    internal static bool FileAffectsGlobalScope(SourceFile file)
    {
        TypeScript.Binder.Binder.BindSourceFile(file);
        // if file contains anything that augments to global scope we need to build them as if
        // they are global files as well as module
        if (Some(file.ModuleAugmentations, augmentation => IsGlobalScopeAugmentation(augmentation.Parent)))
        {
            return true;
        }

        if (IsExternalOrCommonJSModule(file) || IsJsonSourceFile(file))
        {
            return false;
        }

        // For script files that contains only ambient external modules, although they are not actually external module files,
        // they can only be consumed via importing elements from them. Regular script files cannot consume them. Therefore,
        // there are no point to rebuild all script files if these special files have changed. However, if any statement
        // in the file is not ambient external module, we treat it as a regular script file.
        return file.Statements is not null &&
            file.Statements.Nodes is not null &&
            Some(file.Statements.Nodes, stmt => !IsModuleWithStringLiteralName(stmt));
    }

    /// <summary>Go: <c>func addReferencedFilesFromSymbol(file, referencedFiles, symbol *ast.Symbol)</c>。</summary>
    internal static void AddReferencedFilesFromSymbol(SourceFile file, Set<Path> referencedFiles, Symbol? symbol)
    {
        if (symbol is null)
        {
            return;
        }
        foreach (var declaration in symbol.Declarations ?? [])
        {
            var fileOfDecl = GetSourceFileOfNode(declaration);
            if (fileOfDecl is null)
            {
                continue;
            }
            if (!ReferenceEquals(file, fileOfDecl))
            {
                referencedFiles.Add(fileOfDecl.Path());
            }
        }
    }

    /// <summary>Get the module source file and all augmenting files from the import name node from file。</summary>
    internal static void AddReferencedFilesFromImportLiteral(SourceFile file, Set<Path> referencedFiles, TypeScript.Checker.Checker checker, Node importName)
    {
        var symbol = checker.GetSymbolAtLocation(importName);
        AddReferencedFilesFromSymbol(file, referencedFiles, symbol);
    }

    /// <summary>Gets the path to reference file from file name, it could be resolvedPath if present otherwise path。</summary>
    internal static void AddReferencedFileFromFileName(CompilerProgram program, string fileName, Set<Path> referencedFiles, string sourceFileDirectory)
    {
        var redirect = program.GetParseFileRedirect(fileName);
        if (redirect != "")
        {
            referencedFiles.Add(ToPath(redirect, program.GetCurrentDirectory(), program.UseCaseSensitiveFileNames()));
        }
        else
        {
            referencedFiles.Add(ToPath(fileName, sourceFileDirectory, program.UseCaseSensitiveFileNames()));
        }
    }

    /// <summary>Gets the referenced files for a file from the program with values for the keys as referenced file's path to be true。</summary>
    internal static Set<Path>? GetReferencedFiles(CompilerProgram program, SourceFile file)
    {
        var referencedFiles = new Set<Path>();

        // We need to use a set here since the code can contain the same import twice,
        // but that will only be one dependency.
        // To avoid invernal conversion, the key of the referencedFiles map must be of type Path
        var (checker, done) = program.GetTypeCheckerForFileExclusive(Context.Background, file);
        try
        {
            foreach (var importName in file.Imports())
            {
                AddReferencedFilesFromImportLiteral(file, referencedFiles, checker, importName);
            }

            var sourceFileDirectory = GetDirectoryPath(file.FileName());
            // Handle triple slash references
            if (file.ReferencedFiles is not null)
            {
                foreach (var referencedFile in file.ReferencedFiles)
                {
                    AddReferencedFileFromFileName(program, referencedFile.FileName, referencedFiles, sourceFileDirectory);
                }
            }

            // Handle type reference directives
            if (program.GetResolvedTypeReferenceDirectives().TryGetValue(file.Path(), out var typeRefsInFile))
            {
                foreach (var typeRef in typeRefsInFile.Values)
                {
                    if (typeRef.ResolvedFileName != "")
                    {
                        AddReferencedFileFromFileName(program, typeRef.ResolvedFileName, referencedFiles, sourceFileDirectory);
                    }
                }
            }

            // Add module augmentation as references
            if (file.ModuleAugmentations is not null)
            {
                foreach (var moduleName in file.ModuleAugmentations)
                {
                    if (!IsStringLiteral(moduleName))
                    {
                        continue;
                    }
                    AddReferencedFilesFromImportLiteral(file, referencedFiles, checker, moduleName);
                }
            }

            // From ambient modules
            foreach (var ambientModule in checker.GetAmbientModules())
            {
                AddReferencedFilesFromSymbol(file, referencedFiles, ambientModule);
            }
            return IfElse(referencedFiles.Len() > 0, referencedFiles, null);
        }
        finally
        {
            done();
        }
    }

    /// <summary>
    /// repopulateDiagnosticsOfFile repopulates diagnostic chains that depend on program state.
    /// When diagnostics are copied from a previous build, their message chains may reference
    /// stale program state (e.g., resolved module alternate results, package.json scope).
    /// This function recomputes those chains using the current program's state.
    /// </summary>
    internal static DiagnosticsOrBuildInfoDiagnosticsWithFileName RepopulateDiagnosticsOfFile(DiagnosticsOrBuildInfoDiagnosticsWithFileName diags, CompilerProgram p, SourceFile file)
    {
        if (diags._diagnostics is not null)
        {
            var repopulated = RepopulateDiagnosticsList(diags._diagnostics, p, file);
            if (repopulated is null)
            {
                return diags;
            }
            return new DiagnosticsOrBuildInfoDiagnosticsWithFileName { _diagnostics = repopulated };
        }
        // buildInfoDiagnostics will be repopulated via toDiagnostic's repopulateInfo handling
        return diags;
    }

    /// <summary>
    /// repopulateDiagnosticsList repopulates diagnostic chains in a list of diagnostics.
    /// Returns nil if no diagnostics needed repopulation (i.e., no changes were made).
    /// </summary>
    internal static Diagnostic[]? RepopulateDiagnosticsList(Diagnostic[] diags, CompilerProgram p, SourceFile file)
    {
        var changed = false;
        var result = new Diagnostic[diags.Length];
        for (var i = 0; i < diags.Length; i++)
        {
            var d = diags[i];
            var repopulated = RepopulateDiagnosticMessageChain(d.MessageChain(), p, file);
            if (repopulated is not null)
            {
                var clone = d.Clone();
                clone.SetMessageChain(repopulated);
                result[i] = clone;
                changed = true;
            }
            else
            {
                result[i] = d;
            }
        }
        if (!changed)
        {
            return null;
        }
        return result;
    }

    /// <summary>
    /// repopulateDiagnosticMessageChain repopulates chains that have repopulate info.
    /// Returns nil if no changes were made.
    /// </summary>
    internal static Diagnostic[]? RepopulateDiagnosticMessageChain(Diagnostic[]? chain, CompilerProgram p, SourceFile file)
    {
        if (chain is null || chain.Length == 0)
        {
            return null;
        }
        var changed = false;
        var result = new Diagnostic[chain.Length];
        for (var i = 0; i < chain.Length; i++)
        {
            var c = chain[i];
            if (c.RepopulateInfo() is not null)
            {
                // Convert to buildInfoDiagnosticWithFileName and repopulate
                var b = new BuildInfoDiagnosticWithFileName
                {
                    _pos = c.Pos(),
                    _end = c.End(),
                    _code = c.Code(),
                    _category = c.Category(),
                    _source = c.Source(),
                    _messageText = c.MessageText(),
                    _messageKey = c.MessageKey(),
                    _messageArgs = c.MessageArgs(),
                    _repopulateInfo = c.RepopulateInfo(),
                };
                // Recursively handle nested chains
                if (c.MessageChain() is not null)
                {
                    foreach (var nested in c.MessageChain()!)
                    {
                        b._messageChain.Add(AstDiagToBuildInfoDiag(nested));
                    }
                }
                result[i] = RepopulateDiagnosticChain(b, p, file);
                changed = true;
            }
            else
            {
                // Check nested chains
                var nested = RepopulateDiagnosticMessageChain(c.MessageChain(), p, file);
                if (nested is not null)
                {
                    var clone = c.Clone();
                    clone.SetMessageChain(nested);
                    result[i] = clone;
                    changed = true;
                }
                else
                {
                    result[i] = c;
                }
            }
        }
        if (!changed)
        {
            return null;
        }
        return result;
    }

    internal static BuildInfoDiagnosticWithFileName AstDiagToBuildInfoDiag(Diagnostic d)
    {
        var b = new BuildInfoDiagnosticWithFileName
        {
            _pos = d.Pos(),
            _end = d.End(),
            _code = d.Code(),
            _category = d.Category(),
            _source = d.Source(),
            _messageText = d.MessageText(),
            _messageKey = d.MessageKey(),
            _messageArgs = d.MessageArgs(),
            _repopulateInfo = d.RepopulateInfo(),
        };
        if (d.MessageChain() is not null)
        {
            foreach (var nested in d.MessageChain()!)
            {
                b._messageChain.Add(AstDiagToBuildInfoDiag(nested));
            }
        }
        return b;
    }
}
