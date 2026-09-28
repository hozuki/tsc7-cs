// ported from: internal/compiler/includeprocessor.go
//
// Go→C# 改写（逐条，都不改变语义）：
// 1) `map[tspath.Path][]*FileIncludeReason` → `Dictionary<Path, List<FileIncludeReason>>`：Go 侧是
//    "逐个 append" 的写法，C# 数组没有 len<cap 视图（§3.2），所以用 List（append 摊还 O(1)）。
// 2) `sync.Once` + 缓存字段 → `Lazy<T>`，首次使用时用 `Interlocked.CompareExchange` 竞争赋值（见 FileInclude.cs）。
// 3) `[]*ast.Diagnostic` 的 nil / 非 nil 区分（`explainRedirectAndImpliedFormat` 返回 nil 表示"没有额外说明"，
//    与"空切片"在调用点行为不同）→ C# 保留 `Diagnostic[]?`：无文件时返回 null、有文件但无诊断时返回 **空数组**
//    （Go 返回 nil，两者在 processingDiagnostic 侧的 `chain` 拼接里等价，那里已按 `Length > 0` 判断）。
// 4) `slices.ContainsFunc(...)` → `List.Exists(...)`（同一语义）。
// 5) Program 的 ProcessedFiles 成员直接访问：`program._resolvedModules`、`program._typeResolutionsInFile`。
using System.Diagnostics;
using TypeScript.Ast;
using TypeScript.Collections;
using TypeScript.Core;
using TypeScript.Diagnostics;
using TypeScript.TsOptions;
using static TypeScript.Ast.Ast;
using static TypeScript.Diagnostics.Diagnostics;
using static TypeScript.TsOptions.TsOptions;
using Diagnostic = TypeScript.Ast.Diagnostic;
using Path = TypeScript.TSPath.Path;

namespace TypeScript.Compiler;

/// <summary>Go: <c>type includeProcessor struct</c>。</summary>
internal sealed class IncludeProcessor
{
    internal Dictionary<Path, List<FileIncludeReason>> _fileIncludeReasons = new();
    internal List<ProcessingDiagnostic> _processingDiagnostics = [];

    internal readonly SyncMap<FileIncludeReason, ReferenceFileLocation> _reasonToReferenceLocation = new();
    internal readonly SyncMap<FileIncludeReason, Diagnostic?> _includeReasonToRelatedInfo = new();
    internal readonly SyncMap<Path, Diagnostic[]?> _redirectAndFileFormat = new();

    private Lazy<DiagnosticsCollection>? _computedDiagnosticsOnce;
    private Lazy<ObjectLiteralExpression?>? _compilerOptionsSyntaxOnce;

    internal DiagnosticsCollection GetDiagnostics(Program p)
    {
        var once = Volatile.Read(ref _computedDiagnosticsOnce);
        if (once is null)
        {
            var created = new Lazy<DiagnosticsCollection>(() => ComputeDiagnostics(p));
            once = Interlocked.CompareExchange(ref _computedDiagnosticsOnce, created, null) ?? created;
        }

        return once.Value;
    }

    private DiagnosticsCollection ComputeDiagnostics(Program p)
    {
        var diagnostics = new DiagnosticsCollection();
        foreach (var d in _processingDiagnostics)
        {
            diagnostics.Add(d.ToDiagnostic(p));
        }

        foreach (var resolutions in p._resolvedModules.Values)
        {
            foreach (var resolvedModule in resolutions.Values)
            {
                foreach (var diag in resolvedModule.ResolutionDiagnostics)
                {
                    diagnostics.Add(diag);
                }
            }
        }

        foreach (var typeResolutions in p._typeResolutionsInFile.Values)
        {
            foreach (var resolvedTypeRef in typeResolutions.Values)
            {
                foreach (var diag in resolvedTypeRef.ResolutionDiagnostics)
                {
                    diagnostics.Add(diag);
                }
            }
        }

        return diagnostics;
    }

    internal void AddProcessingDiagnostic(params ReadOnlySpan<ProcessingDiagnostic> d)
    {
        foreach (var diagnostic in d)
        {
            _processingDiagnostics.Add(diagnostic);
        }
    }

    internal void AddProcessingDiagnosticsForFileCasing(Path file, string existingCasing, string currentCasing, FileIncludeReason reason)
    {
        // Go 的 `i.fileIncludeReasons[file]` 对缺失键得到 nil 切片 → ContainsFunc 为 false。
        var reasons = _fileIncludeReasons.GetValueOrDefault(file);
        if (!reason.IsReferencedFile() && reasons is not null && reasons.Exists(r => r.IsReferencedFile()))
        {
            AddProcessingDiagnostic(new ProcessingDiagnostic
            {
                Kind = ProcessingDiagnosticKind.ExplainingFileInclude,
                Data = new IncludeExplainingDiagnostic
                {
                    File = file,
                    DiagnosticReason = reason,
                    Message = Already_included_file_name_0_differs_from_file_name_1_only_in_casing,
                    Args = [existingCasing, currentCasing],
                },
            });
        }
        else
        {
            AddProcessingDiagnostic(new ProcessingDiagnostic
            {
                Kind = ProcessingDiagnosticKind.ExplainingFileInclude,
                Data = new IncludeExplainingDiagnostic
                {
                    File = file,
                    DiagnosticReason = reason,
                    Message = File_name_0_differs_from_already_included_file_name_1_only_in_casing,
                    Args = [currentCasing, existingCasing],
                },
            });
        }
    }

    internal ReferenceFileLocation GetReferenceLocation(FileIncludeReason r, Program program)
    {
        var (existing, ok) = _reasonToReferenceLocation.Load(r);
        if (ok)
        {
            // 本 map 只写入 GetReferencedLocation 的结果（Go 里也永不为 nil）。
            return existing!;
        }

        var (loc, _) = _reasonToReferenceLocation.LoadOrStore(r, r.GetReferencedLocation(program));
        return loc!;
    }

    internal ObjectLiteralExpression? GetCompilerOptionsObjectLiteralSyntax(Program program)
    {
        var once = Volatile.Read(ref _compilerOptionsSyntaxOnce);
        if (once is null)
        {
            var created = new Lazy<ObjectLiteralExpression?>(() =>
            {
                var configFile = program._opts.Config.ConfigFile;
                if (configFile is not null)
                {
                    var compilerOptionsProperty = ForEachTsConfigPropArray(configFile.SourceFile, "compilerOptions", property => property);
                    if (compilerOptionsProperty is not null
                        && compilerOptionsProperty.Initializer is not null
                        && IsObjectLiteralExpression(compilerOptionsProperty.Initializer))
                    {
                        // IsObjectLiteralExpression 成立后断言必定成功（Go 同样直接断言）。
                        return compilerOptionsProperty.Initializer.AsObjectLiteralExpression();
                    }
                }

                return null;
            });
            once = Interlocked.CompareExchange(ref _compilerOptionsSyntaxOnce, created, null) ?? created;
        }

        return once.Value;
    }

    internal Diagnostic? GetRelatedInfo(FileIncludeReason r, Program program)
    {
        var (existing, ok) = _includeReasonToRelatedInfo.Load(r);
        if (ok)
        {
            return existing;
        }

        var (relatedInfo, _) = _includeReasonToRelatedInfo.LoadOrStore(r, r.ToRelatedInfo(program));
        return relatedInfo;
    }

    internal Diagnostic[]? ExplainRedirectAndImpliedFormat(
        Program program,
        Path filePath,
        Func<string, string> toFileName)
    {
        var (existing, ok) = _redirectAndFileFormat.Load(filePath);
        if (ok)
        {
            return existing;
        }

        HasFileName file;
        SourceFile? sourceFile = null;
        var redirectsFile = program._redirectFilesByPath?.GetValueOrDefault(filePath);
        if (redirectsFile is not null)
        {
            file = redirectsFile;
        }
        else
        {
            sourceFile = program.GetSourceFileByPath(filePath);
            if (sourceFile is null)
            {
                return null;
            }

            file = sourceFile;
        }

        var result = new List<Diagnostic>();
        var source = program.GetSourceOfProjectReferenceIfOutputIncluded(file);
        if (source != file.FileName())
        {
            result.Add(NewCompilerDiagnostic(
                File_is_output_of_project_reference_source_0,
                toFileName(source)));
        }

        if (redirectsFile is not null)
        {
            // Go 直接解引用 GetSourceFileByPath 的结果。
            var targetFile = program.GetSourceFileByPath(redirectsFile._target)!;
            result.Add(NewCompilerDiagnostic(
                File_redirects_to_file_0,
                toFileName(targetFile.FileName())));
        }

        if (sourceFile is not null && IsExternalOrCommonJSModule(sourceFile))
        {
            var metaData = program.GetSourceFileMetaData(file.Path());
            switch (program.GetImpliedNodeFormatForEmit(file))
            {
                case ModuleKind.ESNext:
                    if (metaData.PackageJsonType == "module")
                    {
                        result.Add(NewCompilerDiagnostic(
                            File_is_ECMAScript_module_because_0_has_field_type_with_value_module,
                            toFileName(metaData.PackageJsonDirectory + "/package.json")));
                    }

                    break;
                case ModuleKind.CommonJS:
                    if (metaData.PackageJsonType != "")
                    {
                        result.Add(NewCompilerDiagnostic(File_is_CommonJS_module_because_0_has_field_type_whose_value_is_not_module, toFileName(metaData.PackageJsonDirectory + "/package.json")));
                    }
                    else if (metaData.PackageJsonDirectory != "")
                    {
                        if (metaData.PackageJsonType == "")
                        {
                            result.Add(NewCompilerDiagnostic(File_is_CommonJS_module_because_0_does_not_have_field_type, toFileName(metaData.PackageJsonDirectory + "/package.json")));
                        }
                    }
                    else
                    {
                        result.Add(NewCompilerDiagnostic(File_is_CommonJS_module_because_package_json_was_not_found));
                    }

                    break;
            }
        }

        var stored = result.ToArray();
        var (actual, _) = _redirectAndFileFormat.LoadOrStore(filePath, stored);
        Debug.Assert(actual is not null);
        return actual;
    }
}

public static partial class Compiler
{
    /// <summary>Go: <c>func updateFileIncludeProcessor(p *Program)</c>。Program 更新时只换处理器、保留已收集的原因与诊断。</summary>
    internal static void UpdateFileIncludeProcessor(Program p)
    {
        p._includeProcessor = new IncludeProcessor
        {
            _fileIncludeReasons = p._includeProcessor._fileIncludeReasons,
            _processingDiagnostics = p._includeProcessor._processingDiagnostics,
        };
    }
}
