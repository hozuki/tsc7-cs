// ported from: internal/execute/incremental/snapshottobuildinfo.go
//
// 与 Go 的偏离（docs/architecture.md §2/§3）：
// 1) `snapshotToBuildInfo(...) (*BuildInfo, error)` → 返回 `BuildInfo`，错误抛异常（§3.2；唯一错误源是
//    `ContentMapperIdentities`）。
// 2) Go 的 `type toBuildInfo struct`（未导出）→ `internal sealed class ToBuildInfo`；`_roots` 的键是
//    `*ast.SourceFile`（C# 的 `SourceFile` 是引用类型，Dictionary 用引用相等 → Go 的 map 键语义一致）。
// 3) `core.Map(slice, f)`（返回 nil 语义）→ 需要写进 JSON 的字段用 `MapListOrNull`（nil 保持 nil），
//    `FileInfos`/`ChangeFileSet`/`ReferencedMap` 这类"只要 nil 就该省略"的也用它；`EmitDiagnosticsPerFile`
//    的元素可能为 nil（Go 的 `[]*T` 可以含 nil）→ 元素类型是 `BuildInfoDiagnosticsOfFile?`。
//    **空集合 vs nil 是本文件最容易写错的地方**：Go 侧 `slices.Collect(空 iterator)` 返回 nil、`append(nil, …)`
//    一个元素都没 append 时也保持 nil，而 `core.Map(非 nil 空切片)` 返回**非 nil** 空切片。因此
//    `ChangeFileSet`/`EmitDiagnosticsPerFile` 必须写成 `files.Length == 0 ? null : …`，
//    `SemanticDiagnosticsPerFile`/`AffectedFilesPendingEmit`/`EmitSignatures`/`ResolvedRoot` 必须在**真正 append 时**
//    才 `??= []`（**不能**在方法开头预初始化）。写错成 `[]` 的后果不是"格式不漂亮"而是**行为偏离**：
//    build 模式的 `getUpToDateStatus` 判的是 `buildInfo.X != nil`（buildtask.go:399-419，读回来 `[]` 也是非 nil），
//    于是 `tsc -b` 第二次会对每个工程打印 "indicates that program needs to report errors." 并**永远重建**
//    （与 Go/tsc 的 up-to-date 行为不符）。
// 4) `t.snapshot.options` 的 reflect 遍历 → `TsOptions.ForEachCompilerOptionValue` + `CompilerOptionFields[i].IsZero`
//    （C# 侧零值判定在字段表里，见 TsOptions/CompilerOptionFields.cs）。
// 5) `panic(fmt.Sprintf(...))` → `Debug.Fail(...)`；`slices.Collect(map.Keys)` → `Dictionary.Keys`；
//    `slices.Sort([]tspath.Path)` → `Array.Sort(..., CompareOrdinal)`。
namespace TypeScript.Execute.Incremental;

using System.Globalization;
using TypeScript.Ast;
using TypeScript.Collections;
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
    /// <summary>Go: <c>func snapshotToBuildInfo(snapshot *snapshot, program *compiler.Program, buildInfoFileName string) (*BuildInfo, error)</c>。</summary>
    internal static BuildInfo SnapshotToBuildInfo(Snapshot snapshot, CompilerProgram program, string buildInfoFileName)
    {
        var contentMapperIdentities = ContentMapperIdentities(program.ContentMapperProject());
        var buildInfo = new BuildInfo
        {
            Version = TypeScript.Core.Core.Version(),
            ContentMapperIdentities = contentMapperIdentities,
        };
        var to = new ToBuildInfo
        {
            _snapshot = snapshot,
            _program = program,
            _buildInfo = buildInfo,
            _buildInfoDirectory = GetDirectoryPath(buildInfoFileName),
            _comparePathsOptions = new TypeScript.TSPath.ComparePathsOptions(
                program.UseCaseSensitiveFileNames(),
                program.GetCurrentDirectory()),
        };

        if (snapshot._options.IsIncremental())
        {
            to.CollectRootFiles();
            to.SetFileInfoAndEmitSignatures();
            to.SetRootOfIncrementalProgram();
            to.SetCompilerOptions();
            to.SetReferencedMap();
            to.SetChangeFileSet();
            to.SetSemanticDiagnostics();
            to.SetEmitDiagnostics();
            to.SetAffectedFilesPendingEmit();
            if (snapshot._latestChangedDtsFile != "")
            {
                buildInfo.LatestChangedDtsFile = to.RelativeToBuildInfo(snapshot._latestChangedDtsFile);
            }
        }
        else
        {
            to.SetRootOfNonIncrementalProgram();
        }
        buildInfo.Errors = snapshot._hasErrors.IsTrue();
        buildInfo.SemanticErrors = snapshot._hasSemanticErrors;
        buildInfo.CheckPending = snapshot._checkPending;
        to.SetPackageJsons();
        return buildInfo;
    }

    /// <summary>Go: <c>type toBuildInfo struct</c>（snapshottobuildinfo.go:64-73）。</summary>
    internal sealed class ToBuildInfo
    {
        internal Snapshot _snapshot = null!;
        internal CompilerProgram _program = null!;
        internal BuildInfo _buildInfo = null!;
        internal string _buildInfoDirectory = "";
        internal TypeScript.TSPath.ComparePathsOptions _comparePathsOptions;
        internal Dictionary<string, BuildInfoFileId> _fileNameToFileId = new();
        internal Dictionary<string, BuildInfoFileIdListId> _fileNamesToFileIdListId = new();
        internal Dictionary<SourceFile, Path> _roots = new();

        internal string RelativeToBuildInfo(string path) =>
            EnsurePathIsNonModuleName(GetRelativePathFromDirectory(_buildInfoDirectory, path, _comparePathsOptions));

        internal BuildInfoFileId ToFileId(Path path)
        {
            var fileId = _fileNameToFileId.GetValueOrDefault(path.Value);
            if (fileId == 0)
            {
                var libFile = _program.GetDefaultLibFile(path);
                if (libFile is not null && !libFile.Replaced)
                {
                    _buildInfo.FileNames ??= [];
                    _buildInfo.FileNames.Add(libFile.Name);
                }
                else
                {
                    _buildInfo.FileNames ??= [];
                    _buildInfo.FileNames.Add(RelativeToBuildInfo(path.Value));
                }
                fileId = _buildInfo.FileNames.Count;
                _fileNameToFileId[path.Value] = fileId;
            }
            return fileId;
        }

        internal BuildInfoFileIdListId ToFileIdListId(Set<Path> set)
        {
            var fileIds = Map(set.Keys().ToArray(), ToFileId);
            Array.Sort(fileIds);
            var key = string.Join(",", Map(fileIds, static id => id.Value.ToString(CultureInfo.InvariantCulture)));

            var fileIdListId = _fileNamesToFileIdListId.GetValueOrDefault(key);
            if (fileIdListId == 0)
            {
                _buildInfo.FileIdsList ??= [];
                _buildInfo.FileIdsList.Add([.. fileIds]);
                fileIdListId = _buildInfo.FileIdsList.Count;
                _fileNamesToFileIdListId[key] = fileIdListId;
            }
            return fileIdListId;
        }

        internal object? ToRelativeToBuildInfoCompilerOptionValue(CommandLineOption option, object? v)
        {
            if (option.Kind == CommandLineOptionKind.List)
            {
                if (option.Elements()?.IsFilePath == true)
                {
                    if (v is string[] arr)
                    {
                        return Map(arr, RelativeToBuildInfo);
                    }
                }
            }
            else if (option.IsFilePath)
            {
                if (v is string str && str != "")
                {
                    return RelativeToBuildInfo(str);
                }
            }
            // Go 的 `core.Tristate` 自己实现 MarshalJSON（TSTrue → true、TSFalse → false、TSUnknown → null），
            // 而 C# 的 enum 走不通这个钩子 → 在这里就地折成 bool?（帧内可观察字节与 Go 一致）。
            if (v is Tristate tristate)
            {
                return tristate switch
                {
                    Tristate.TSTrue => true,
                    Tristate.TSFalse => false,
                    _ => null,
                };
            }
            return v;
        }

        internal List<BuildInfoDiagnostic>? ToBuildInfoDiagnosticsFromFileNameDiagnostics(List<BuildInfoDiagnosticWithFileName>? diagnostics) =>
            MapListOrNull(diagnostics, d => new BuildInfoDiagnostic
            {
                File = IsEmptyPath(d._file) ? default : ToFileId(d._file),
                NoFile = d._noFile,
                Pos = d._pos,
                End = d._end,
                Code = d._code,
                Category = d._category,
                Source = d._source,
                MessageText = d._messageText,
                MessageKey = d._messageKey,
                MessageArgs = d._messageArgs is null ? null : [.. d._messageArgs],
                MessageChain = ToBuildInfoDiagnosticsFromFileNameDiagnostics(d._messageChain.Count == 0 ? null : d._messageChain),
                RelatedInformation = ToBuildInfoDiagnosticsFromFileNameDiagnostics(d._relatedInformation.Count == 0 ? null : d._relatedInformation),
                ReportsUnnecessary = d._reportsUnnecessary,
                ReportsDeprecated = d._reportsDeprecated,
                SkippedOnNoEmit = d._skippedOnNoEmit,
                RepopulateInfo = ToBuildInfoRepopulateInfo(d._repopulateInfo),
            });

        internal List<BuildInfoDiagnostic>? ToBuildInfoDiagnosticsFromDiagnostics(Path filePath, Diagnostic[]? diagnostics) =>
            MapListOrNull(ToListOrNull(diagnostics), d => new BuildInfoDiagnostic
            {
                File = d.File() is null || d.File()!.Path() == filePath ? default : ToFileId(d.File()!.Path()),
                NoFile = d.File() is null,
                Pos = d.Loc().Pos(),
                End = d.Loc().End(),
                Code = d.Code(),
                Category = d.Category(),
                Source = d.Source(),
                MessageText = d.MessageText(),
                MessageKey = d.MessageKey(),
                MessageArgs = ToListOrNull(d.MessageArgs()),
                MessageChain = ToBuildInfoDiagnosticsFromDiagnostics(filePath, d.MessageChain()),
                RelatedInformation = ToBuildInfoDiagnosticsFromDiagnostics(filePath, d.RelatedInformation()),
                ReportsUnnecessary = d.ReportsUnnecessary(),
                ReportsDeprecated = d.ReportsDeprecated(),
                SkippedOnNoEmit = d.SkippedOnNoEmit(),
                RepopulateInfo = ToBuildInfoRepopulateInfo(d.RepopulateInfo()),
            });

        internal BuildInfoDiagnosticsOfFile? ToBuildInfoDiagnosticsOfFile(Path filePath, DiagnosticsOrBuildInfoDiagnosticsWithFileName? diags)
        {
            if (diags!._diagnostics is { Length: > 0 })
            {
                return new BuildInfoDiagnosticsOfFile
                {
                    FileId = ToFileId(filePath),
                    Diagnostics = ToBuildInfoDiagnosticsFromDiagnostics(filePath, diags._diagnostics),
                };
            }
            if (diags._buildInfoDiagnostics is { Length: > 0 })
            {
                return new BuildInfoDiagnosticsOfFile
                {
                    FileId = ToFileId(filePath),
                    Diagnostics = ToBuildInfoDiagnosticsFromFileNameDiagnostics([.. diags._buildInfoDiagnostics]),
                };
            }
            return null;
        }

        internal void CollectRootFiles()
        {
            foreach (var fileName in _program.CommandLine().FileNames())
            {
                SourceFile? file;
                var redirect = _program.GetParseFileRedirect(fileName);
                if (redirect != "")
                {
                    file = _program.GetSourceFile(redirect);
                }
                else
                {
                    file = _program.GetSourceFile(fileName);
                }
                if (file is not null)
                {
                    _roots[file] = ToPath(fileName, _comparePathsOptions.CurrentDirectory, _comparePathsOptions.UseCaseSensitiveFileNames);
                }
            }
        }

        internal void SetFileInfoAndEmitSignatures()
        {
            _buildInfo.FileInfos ??= [];
            foreach (var file in _program.GetSourceFiles())
            {
                var (info, _) = _snapshot._fileInfos.Load(file.Path());
                var fileId = ToFileId(file.Path());
                //  tryAddRoot(key, fileId);
                if (_buildInfo.FileNames![(int)fileId - 1] != RelativeToBuildInfo(file.Path().Value))
                {
                    var libFile = _program.GetDefaultLibFile(file.Path());
                    if (libFile is null || libFile.Replaced || _buildInfo.FileNames[(int)fileId - 1] != libFile.Name)
                    {
                        Debug.Fail($"File name at index {(int)fileId - 1} does not match expected relative path or libName: {_buildInfo.FileNames[(int)fileId - 1]} != {RelativeToBuildInfo(file.Path().Value)}");
                    }
                }
                if (_snapshot._options.Composite.IsTrue())
                {
                    if (!IsJsonSourceFile(file) && _program.SourceFileMayBeEmitted(file, false))
                    {
                        var (emitSignature, loaded) = _snapshot._emitSignatures.Load(file.Path());
                        if (!loaded)
                        {
                            _buildInfo.EmitSignatures ??= [];
                            _buildInfo.EmitSignatures.Add(new BuildInfoEmitSignature { FileId = fileId });
                        }
                        else if (emitSignature!._signature != info!.Signature())
                        {
                            var incrementalEmitSignature = new BuildInfoEmitSignature { FileId = fileId };
                            if (emitSignature._signature != "")
                            {
                                incrementalEmitSignature.Signature = emitSignature._signature;
                            }
                            else if (emitSignature._signatureWithDifferentOptions![0] == info.Signature())
                            {
                                incrementalEmitSignature.DiffersOnlyInDtsMap = true;
                            }
                            else
                            {
                                incrementalEmitSignature.Signature = emitSignature._signatureWithDifferentOptions[0];
                                incrementalEmitSignature.DiffersInOptions = true;
                            }
                            _buildInfo.EmitSignatures ??= [];
                            _buildInfo.EmitSignatures.Add(incrementalEmitSignature);
                        }
                    }
                }
                _buildInfo.FileInfos.Add(NewBuildInfoFileInfo(info!));
            }
        }

        internal void SetRootOfIncrementalProgram()
        {
            var keys = _roots.Keys.ToArray();
            Array.Sort(keys, (a, b) => (int)ToFileId(a.Path()) - (int)ToFileId(b.Path()));
            foreach (var file in keys)
            {
                var root = ToFileId(_roots[file]);
                var resolved = ToFileId(file.Path());
                if (_buildInfo.Root is null || _buildInfo.Root.Count == 0)
                {
                    // First fileId as is
                    _buildInfo.Root ??= [];
                    _buildInfo.Root.Add(new BuildInfoRoot { Start = resolved });
                }
                else
                {
                    var last = _buildInfo.Root[^1];
                    if (last.End == resolved - 1)
                    {
                        // If its [..., last = [start, end = fileId - 1]], update last to [start, fileId]
                        last.End = resolved;
                    }
                    else if (last.End == 0 && last.Start == resolved - 1)
                    {
                        // If its [..., last = start = fileId - 1 ], update last to [start, fileId]
                        last.End = resolved;
                    }
                    else
                    {
                        _buildInfo.Root.Add(new BuildInfoRoot { Start = resolved });
                    }
                }
                if (root != resolved)
                {
                    _buildInfo.ResolvedRoot ??= [];
                    _buildInfo.ResolvedRoot.Add(new BuildInfoResolvedRoot
                    {
                        Resolved = resolved,
                        Root = root,
                    });
                }
            }
        }

        internal void SetCompilerOptions()
        {
            ForEachCompilerOptionValue(
                _snapshot._options,
                static option => option.AffectsBuildInfo,
                (option, value, i) =>
                {
                    if (CompilerOptionFields[i].IsZero(_snapshot._options))
                    {
                        return false;
                    }

                    // Make it relative to buildInfo directory if file path
                    _buildInfo.Options ??= new OrderedMap<string, object?>();
                    _buildInfo.Options.Set(option.Name, ToRelativeToBuildInfoCompilerOptionValue(option, value));
                    return false;
                });
        }

        internal void SetReferencedMap()
        {
            var keys = _snapshot._referencedMap.GetPathsWithReferences();
            Array.Sort(keys, static (a, b) => string.CompareOrdinal(a.Value, b.Value));
            _buildInfo.ReferencedMap = MapListOrNull(ToListOrNull(keys), filePath =>
            {
                var (references, _) = _snapshot._referencedMap.GetReferences(filePath);
                return new BuildInfoReferenceMapEntry
                {
                    FileId = ToFileId(filePath),
                    FileIdListId = ToFileIdListId(references!),
                };
            });
        }

        internal void SetChangeFileSet()
        {
            var files = _snapshot._changedFilesSet.Keys().ToArray();
            Array.Sort(files, static (a, b) => string.CompareOrdinal(a.Value, b.Value));
            // Go: `toBuildInfo.setChangeFileSet()`（snapshottobuildinfo.go:323-327）：`slices.Collect(...)` 在**空 iterator**
            // 时返回 **nil**（不是 `[]`）→ `core.Map(nil, …)` = nil（core/core.go:80-89）→ `omitzero` 省略该字段，
            // 读回来也是 nil。空数组会让 build 模式的 `buildInfo.ChangeFileSet != nil`（buildtask.go:411/417）判成
            // "有改动" → 第二次 `-b` 永远重建。
            _buildInfo.ChangeFileSet = files.Length == 0 ? null : MapListOrNull(ToListOrNull(files), ToFileId);
        }

        internal void SetSemanticDiagnostics()
        {
            // Go: `toBuildInfo.setSemanticDiagnostics()`（snapshottobuildinfo.go:329-347）：`SemanticDiagnosticsPerFile` 由
            // `append(t.buildInfo.SemanticDiagnosticsPerFile, …)` 构造，**一条都没 append 时字段保持 nil**
            // （JSON 省略，读回来也是 nil）。这里**不能**用 `??= []` 预初始化 —— 那会让空列表变成 `[]`（非 nil），
            // 于是 build 模式第二次运行会把 `buildInfo.SemanticDiagnosticsPerFile != nil`（buildtask.go:411）判成
            // "buildinfo 说需要报错"，永远重建；有诊断时则照常 append（`??= []` 只在真正 append 的分支里）。
            foreach (var file in _program.GetSourceFiles())
            {
                var (value, ok) = _snapshot._semanticDiagnosticsPerFile.Load(file.Path());
                if (!ok)
                {
                    if (!_snapshot._changedFilesSet.Has(file.Path()))
                    {
                        (_buildInfo.SemanticDiagnosticsPerFile ??= []).Add(new BuildInfoSemanticDiagnostic
                        {
                            FileId = ToFileId(file.Path()),
                        });
                    }
                }
                else
                {
                    var diagnostics = ToBuildInfoDiagnosticsOfFile(file.Path(), value);
                    if (diagnostics is not null)
                    {
                        (_buildInfo.SemanticDiagnosticsPerFile ??= []).Add(new BuildInfoSemanticDiagnostic
                        {
                            Diagnostics = diagnostics,
                        });
                    }
                }
            }
        }

        internal void SetEmitDiagnostics()
        {
            var files = _snapshot._emitDiagnosticsPerFile.Keys().ToArray();
            Array.Sort(files, static (a, b) => string.CompareOrdinal(a.Value, b.Value));
            // 同 SetChangeFileSet：Go 的 `setEmitDiagnostics()`（snapshottobuildinfo.go:349-356）用
            // `slices.Collect(...)`（空 → nil）+ `core.Map`，空集合在 Go 里是 nil（JSON 省略），不能写成 `[]`
            // （会触发 build 模式的 `GetEmitDeclarations() && buildInfo.EmitDiagnosticsPerFile != nil` → 永远重建，
            // buildtask.go:409-412）。
            _buildInfo.EmitDiagnosticsPerFile = files.Length == 0 ? null : MapListOrNull(ToListOrNull(files), filePath =>
            {
                var (value, _) = _snapshot._emitDiagnosticsPerFile.Load(filePath);
                return ToBuildInfoDiagnosticsOfFile(filePath, value);
            });
        }

        internal void SetAffectedFilesPendingEmit()
        {
            var files = _snapshot._affectedFilesPendingEmit.Keys().ToArray();
            Array.Sort(files, static (a, b) => string.CompareOrdinal(a.Value, b.Value));
            var fullEmitKind = GetFileEmitKind(_snapshot._options);
            foreach (var filePath in files)
            {
                var file = _program.GetSourceFileByPath(filePath);
                if (file is null || !_program.SourceFileMayBeEmitted(file, false))
                {
                    continue;
                }
                var (pendingEmit, _) = _snapshot._affectedFilesPendingEmit.Load(filePath);
                _buildInfo.AffectedFilesPendingEmit ??= [];
                _buildInfo.AffectedFilesPendingEmit.Add(new BuildInfoFilePendingEmit
                {
                    FileId = ToFileId(filePath),
                    EmitKind = IfElse(pendingEmit == fullEmitKind, FileEmitKind.None, pendingEmit),
                });
            }
        }

        internal void SetRootOfNonIncrementalProgram()
        {
            _buildInfo.Root = MapListOrNull(
                [.. _program.CommandLine().FileNames()],
                fileName => new BuildInfoRoot
                {
                    NonIncremental = RelativeToBuildInfo(ToPath(fileName, _comparePathsOptions.CurrentDirectory, _comparePathsOptions.UseCaseSensitiveFileNames).Value),
                });
        }

        internal void SetPackageJsons()
        {
            if (_snapshot._packageJsons is { Length: > 0 })
            {
                _buildInfo.PackageJsons = MapListOrNull(ToListOrNull(_snapshot._packageJsons), RelativeToBuildInfo);
            }
            if (_snapshot._missingPackageJsons is { Length: > 0 })
            {
                _buildInfo.MissingPackageJsons = MapListOrNull(ToListOrNull(_snapshot._missingPackageJsons), RelativeToBuildInfo);
            }
        }
    }

    /// <summary>Go: <c>func toBuildInfoRepopulateInfo(info *ast.RepopulateDiagnosticInfo) *BuildInfoRepopulateInfo</c>。</summary>
    internal static BuildInfoRepopulateInfo? ToBuildInfoRepopulateInfo(RepopulateDiagnosticInfo? info)
    {
        if (info is null)
        {
            return null;
        }
        return new BuildInfoRepopulateInfo
        {
            Kind = info.Kind,
            ModuleReference = info.ModuleReference,
            Mode = info.Mode,
            PackageName = info.PackageName,
        };
    }
}
