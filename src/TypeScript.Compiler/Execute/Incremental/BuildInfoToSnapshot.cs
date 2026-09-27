// ported from: internal/execute/incremental/buildinfotosnapshot.go
//
// 与 Go 的偏离（docs/architecture.md §2/§3）：
// 1) Go 的 `type toSnapshot struct` 是局部结构体（`buildInfoToSnapshot` 返回 `&to.snapshot`）→ C# `internal sealed
//    class ToSnapshot`，`_snapshot` 字段直接持有被返回的 `Snapshot` 对象。
// 2) Go 的 `core.Map([]T, f)` → `Incremental.MapList(List<T>?, f)`（BuildInfo 的集合字段是 `List<T>?`，见 BuildInfo.cs）。
// 3) `t.buildInfo.PackageJsons != nil` 分支：Go 两个分支都得到**非 nil** 切片 → C# 直接 `MapList(...)`（null → 空数组）。
// 4) `ast.RepopulateDiagnosticInfo` 的 Kind 是 `ast.RepopulateDiagnosticKind`。
namespace TypeScript.Execute.Incremental;

using TypeScript.Ast;
using TypeScript.Collections;
using TypeScript.Compiler;
using TypeScript.Core;
using TypeScript.TsOptions;
using static TypeScript.Collections.Collections;
using static TypeScript.Core.Core;
using static TypeScript.TSPath.TSPath;
using Path = TypeScript.TSPath.Path;

public static partial class Incremental
{
    /// <summary>Go: <c>func buildInfoToSnapshot(buildInfo *BuildInfo, config *tsoptions.ParsedCommandLine, host compiler.CompilerHost) *snapshot</c>。</summary>
    internal static Snapshot BuildInfoToSnapshot(BuildInfo buildInfo, ParsedCommandLine config, CompilerHost host)
    {
        var to = new ToSnapshot
        {
            _buildInfo = buildInfo,
            _buildInfoDirectory = GetDirectoryPath(GetNormalizedAbsolutePath(config.GetBuildInfoFileName(), config.GetCurrentDirectory())),
        };
        to._filePaths = MapList(buildInfo.FileNames, fileName =>
        {
            if (IsBuildInfoFileNameDefaultLibrary(fileName))
            {
                return ToPath(CombinePaths(host.DefaultLibraryPath(), fileName), host.GetCurrentDirectory(), host.FS().UseCaseSensitiveFileNames());
            }
            return ToPath(fileName, to._buildInfoDirectory, config.UseCaseSensitiveFileNames());
        });
        to._filePathSet = MapList(buildInfo.FileIdsList, fileIdList =>
        {
            var fileSet = NewSetWithSizeHint<Path>(fileIdList.Count);
            foreach (var fileId in fileIdList)
            {
                fileSet.Add(to.ToFilePath(fileId));
            }
            return fileSet;
        });
        to.SetCompilerOptions();
        to.SetFileInfoAndEmitSignatures();
        to.SetReferencedMap();
        to.SetChangeFileSet();
        to.SetSemanticDiagnostics();
        to.SetEmitDiagnostics();
        to.SetAffectedFilesPendingEmit();
        if (buildInfo.LatestChangedDtsFile != "")
        {
            to._snapshot._latestChangedDtsFile = to.ToAbsolutePath(buildInfo.LatestChangedDtsFile);
        }
        to._snapshot._hasErrors = IfElse(buildInfo.Errors, Tristate.TSTrue, Tristate.TSFalse);
        to._snapshot._hasSemanticErrors = buildInfo.SemanticErrors;
        to._snapshot._checkPending = buildInfo.CheckPending;
        to.SetPackageJsons();
        return to._snapshot;
    }

    /// <summary>Go: <c>type toSnapshot struct</c>（buildinfotosnapshot.go:49-56）。</summary>
    internal sealed class ToSnapshot
    {
        internal BuildInfo _buildInfo = null!;
        internal string _buildInfoDirectory = "";
        internal Snapshot _snapshot = new();
        internal Path[] _filePaths = [];
        internal Set<Path>[] _filePathSet = [];

        internal string ToAbsolutePath(string path) => GetNormalizedAbsolutePath(path, _buildInfoDirectory);

        internal Path ToFilePath(BuildInfoFileId fileId) => _filePaths[(int)fileId - 1];

        internal Set<Path> ToFilePathSet(BuildInfoFileIdListId fileIdListId) => _filePathSet[(int)fileIdListId - 1];

        internal BuildInfoDiagnosticWithFileName[] MapDiagnostics(List<BuildInfoDiagnostic>? diagnostics) =>
            MapList(diagnostics, d =>
            {
                var file = default(Path);
                if (d.File != 0)
                {
                    file = ToFilePath(d.File);
                }
                return new BuildInfoDiagnosticWithFileName
                {
                    _file = file,
                    _noFile = d.NoFile,
                    _pos = d.Pos,
                    _end = d.End,
                    _code = d.Code,
                    _category = d.Category,
                    _source = d.Source,
                    _messageText = d.MessageText,
                    _messageKey = d.MessageKey,
                    _messageArgs = d.MessageArgs?.ToArray(),
                    _messageChain = [.. MapDiagnostics(d.MessageChain)],
                    _relatedInformation = [.. MapDiagnostics(d.RelatedInformation)],
                    _reportsUnnecessary = d.ReportsUnnecessary,
                    _reportsDeprecated = d.ReportsDeprecated,
                    _skippedOnNoEmit = d.SkippedOnNoEmit,
                    _repopulateInfo = FromBuildInfoRepopulateInfo(d.RepopulateInfo),
                };
            });

        internal DiagnosticsOrBuildInfoDiagnosticsWithFileName ToDiagnosticsOrBuildInfoDiagnosticsWithFileName(BuildInfoDiagnosticsOfFile dig) =>
            new()
            {
                _buildInfoDiagnostics = MapDiagnostics(dig.Diagnostics),
            };

        internal void SetCompilerOptions()
        {
            _snapshot._options = _buildInfo.GetCompilerOptions(_buildInfoDirectory);
        }

        internal void SetFileInfoAndEmitSignatures()
        {
            var isComposite = _snapshot._options.Composite.IsTrue();
            for (var index = 0; index < (_buildInfo.FileInfos?.Count ?? 0); index++)
            {
                var path = ToFilePath(index + 1);
                var info = _buildInfo.FileInfos![index].GetFileInfo()!;
                _snapshot._fileInfos.Store(path, info);
                // Add default emit signature as file's signature
                if (info._signature != "" && isComposite)
                {
                    _snapshot._emitSignatures.Store(path, new EmitSignature { _signature = info._signature });
                }
            }
            // Fix up emit signatures
            if (_buildInfo.EmitSignatures is not null)
            {
                foreach (var value in _buildInfo.EmitSignatures)
                {
                    if (value.NoEmitSignature())
                    {
                        _snapshot._emitSignatures.Delete(ToFilePath(value.FileId));
                    }
                    else
                    {
                        var path = ToFilePath(value.FileId);
                        _snapshot._emitSignatures.Store(path, value.ToEmitSignature(path, _snapshot._emitSignatures));
                    }
                }
            }
        }

        internal void SetReferencedMap()
        {
            if (_buildInfo.ReferencedMap is null)
            {
                return;
            }
            foreach (var entry in _buildInfo.ReferencedMap)
            {
                _snapshot._referencedMap.StoreReferences(ToFilePath(entry.FileId), ToFilePathSet(entry.FileIdListId));
            }
        }

        internal void SetChangeFileSet()
        {
            if (_buildInfo.ChangeFileSet is null)
            {
                return;
            }
            foreach (var fileId in _buildInfo.ChangeFileSet)
            {
                var filePath = ToFilePath(fileId);
                _snapshot._changedFilesSet.Add(filePath);
            }
        }

        internal void SetSemanticDiagnostics()
        {
            _snapshot._fileInfos.Range((path, info) =>
            {
                // Initialize to have no diagnostics if its not changed file
                if (!_snapshot._changedFilesSet.Has(path))
                {
                    _snapshot._semanticDiagnosticsPerFile.Store(path, new DiagnosticsOrBuildInfoDiagnosticsWithFileName());
                }
                return true;
            });
            if (_buildInfo.SemanticDiagnosticsPerFile is null)
            {
                return;
            }
            foreach (var diagnostic in _buildInfo.SemanticDiagnosticsPerFile)
            {
                if (diagnostic.FileId != 0)
                {
                    var filePath = ToFilePath(diagnostic.FileId);
                    _snapshot._semanticDiagnosticsPerFile.Delete(filePath); // does not have cached diagnostics
                }
                else
                {
                    var filePath = ToFilePath(diagnostic.Diagnostics!.FileId);
                    _snapshot._semanticDiagnosticsPerFile.Store(filePath, ToDiagnosticsOrBuildInfoDiagnosticsWithFileName(diagnostic.Diagnostics));
                }
            }
        }

        internal void SetEmitDiagnostics()
        {
            if (_buildInfo.EmitDiagnosticsPerFile is null)
            {
                return;
            }
            foreach (var diagnostic in _buildInfo.EmitDiagnosticsPerFile)
            {
                var filePath = ToFilePath(diagnostic!.FileId);
                _snapshot._emitDiagnosticsPerFile.Store(filePath, ToDiagnosticsOrBuildInfoDiagnosticsWithFileName(diagnostic));
            }
        }

        internal void SetAffectedFilesPendingEmit()
        {
            if (_buildInfo.AffectedFilesPendingEmit is null || _buildInfo.AffectedFilesPendingEmit.Count == 0)
            {
                return;
            }
            var ownOptionsEmitKind = GetFileEmitKind(_snapshot._options);
            foreach (var pendingEmit in _buildInfo.AffectedFilesPendingEmit)
            {
                _snapshot._affectedFilesPendingEmit.Store(ToFilePath(pendingEmit.FileId), IfElse(pendingEmit.EmitKind == 0, ownOptionsEmitKind, pendingEmit.EmitKind));
            }
        }

        internal void SetPackageJsons()
        {
            _snapshot._packageJsons = MapList(_buildInfo.PackageJsons, ToAbsolutePath);
            _snapshot._missingPackageJsons = MapList(_buildInfo.MissingPackageJsons, ToAbsolutePath);
        }
    }

    /// <summary>Go: <c>func fromBuildInfoRepopulateInfo(info *BuildInfoRepopulateInfo) *ast.RepopulateDiagnosticInfo</c>。</summary>
    internal static RepopulateDiagnosticInfo? FromBuildInfoRepopulateInfo(BuildInfoRepopulateInfo? info)
    {
        if (info is null)
        {
            return null;
        }
        return new RepopulateDiagnosticInfo
        {
            Kind = info.Kind,
            ModuleReference = info.ModuleReference,
            Mode = info.Mode,
            PackageName = info.PackageName,
        };
    }
}
