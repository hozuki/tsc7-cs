// ported from: internal/compiler/fileInclude.go
//
// Go→C# 改写（逐条，都不改变语义）：
// 1) `data any` + 按 kind 断言 → `object? _data` + 显式 `(T)_data!`。Go 的断言失败/解引用 nil 会 panic，
//    C# 对应 InvalidCastException/NullReferenceException。
// 2) `sync.Once` + 缓存字段 → `Lazy<Diagnostic?>`，首次使用时用 `Interlocked.CompareExchange` 竞争赋值
//    （与 sync.Once 一样：只算一次、并发下可能多算但只保留一个）。C# 不可能在构造器里建 Lazy（闭包要 program），
//    所以这是最接近的写法。
// 3) `panic(fmt.Sprintf("unknown reason: %v", r.kind))` → `Debug.Fail(...)` + `throw new
//    System.Diagnostics.UnreachableException()`（Debug.Fail 没有 [DoesNotReturn]，见 Binder.cs 文件头第 7 条）。
// 4) `fmt.Sprintf(`"%s"`, node.Text())` → `"\"" + node.Text() + "\""`（Go 的 %s 不转义，等价）。
// 5) `scanner.SkipTrivia(...)` → `using static TypeScript.Scanner.Scanner;` 后的裸 `SkipTrivia`。
// 6) `r.ref` 在非 Import 分支上必定非 nil（Go 直接解引用，nil 会 panic）→ C# 写 `_ref!`。
// 7) `config.GetMatchedIncludeSpec(fileName) (string, bool)`：本仓已按 §3.2 落成
//    `TryGetMatchedIncludeSpec(fileName, out string)`，**返回值就是 Go 的 isDefaultIncludeSpec**
//    （见 TsOptions/ParsedCommandLine.cs），命中与否看 out 参数是否非空。
// 8) 跨片可见的成员名：Program（T10Program）只用到 `ToDiagnostic(program, relativeInclude)`；
//    ProcessingDiagnostic（T12EmitterHost）会读 `FileIncludeReason._kind`、`IncludeProcessor.GetReferenceLocation(...)`。
//    本文件里 Go 未导出字段一律 `_camelCase`（internal），与 architecture.md §2 一致。
using TypeScript.Ast;
using TypeScript.Diagnostics;
using TypeScript.Module;
using TypeScript.TSPath;
using static TypeScript.Ast.Ast;
using static TypeScript.Core.Core;
using static TypeScript.Diagnostics.Diagnostics;
using static TypeScript.Scanner.Scanner;
using static TypeScript.TSPath.TSPath;
using static TypeScript.TsOptions.TsOptions;
using Diagnostic = TypeScript.Ast.Diagnostic;
using Path = TypeScript.TSPath.Path;

namespace TypeScript.Compiler;

/// <summary>Go: <c>type fileIncludeKind int</c>（数值照 Go 的 iota，因为 <c>IsReferencedFile</c> 依赖顺序）。</summary>
internal enum FileIncludeKind
{
    // References from file
    Import = 0,
    ReferenceFile = 1,
    TypeReferenceDirective = 2,
    LibReferenceDirective = 3,

    RootFile = 4,
    LibFile = 5,
    AutomaticTypeDirectiveFile = 6,
    ContentMapperSupplemental = 7,
}

/// <summary>Go: <c>type FileIncludeReason struct</c>（Go 里是**导出**类型，故 C# 也 public；它的字段与方法在 Go 里
/// 全是未导出，因此这里全部是 <c>internal</c>）。</summary>
public sealed class FileIncludeReason
{
    internal FileIncludeKind _kind;

    /// <summary>Go: <c>data any</c>——四种可能：<c>int</c>、<see cref="ReferencedFileData"/>、
    /// <see cref="AutomaticTypeDirectiveFileData"/>、<see cref="Path"/>（content mapper supplemental）。</summary>
    internal object? _data;

    // Uses relative file name
    private Lazy<Diagnostic?>? _relativeFileNameDiagOnce;

    // Uses file name as is
    private Lazy<Diagnostic?>? _diagOnce;

    internal int AsIndex() => (int)_data!;

    internal bool TryAsLibFileIndex(out int index)
    {
        if (_data is int value)
        {
            index = value;
            return true;
        }

        index = 0;
        return false;
    }

    internal bool IsReferencedFile() => _kind <= FileIncludeKind.LibReferenceDirective;

    internal ReferencedFileData AsReferencedFileData() => (ReferencedFileData)_data!;

    internal AutomaticTypeDirectiveFileData AsAutomaticTypeDirectiveFileData() => (AutomaticTypeDirectiveFileData)_data!;

    internal ReferenceFileLocation GetReferencedLocation(Program program)
    {
        var reference = AsReferencedFileData();
        var file = program.GetSourceFileByPath(reference.File)!;
        switch (_kind)
        {
            case FileIncludeKind.Import:
                Node? specifier = null;
                var isSynthetic = false;
                if (reference.Synthetic is not null)
                {
                    specifier = reference.Synthetic;
                    isSynthetic = true;
                }
                else if (reference.Index < file.Imports().Length)
                {
                    specifier = file.Imports()[reference.Index];
                }
                else
                {
                    var augIndex = file.Imports().Length;
                    foreach (var imp in file.ModuleAugmentations ?? [])
                    {
                        if (imp.Kind == Kind.StringLiteral)
                        {
                            if (augIndex == reference.Index)
                            {
                                specifier = imp;
                                break;
                            }

                            augIndex++;
                        }
                    }
                }

                // Go 直接把 specifier（可能为 nil）传进去，GetResolvedModuleFromModuleSpecifier 内部会 Debug.Fail。
                var resolution = program.GetResolvedModuleFromModuleSpecifier(file, specifier!)!;
                return new ReferenceFileLocation
                {
                    _file = file,
                    _node = specifier,
                    _packageId = resolution.PackageId,
                    _isSynthetic = isSynthetic,
                };
            case FileIncludeKind.ReferenceFile:
                return new ReferenceFileLocation
                {
                    _file = file,
                    _ref = file.ReferencedFiles![reference.Index],
                };
            case FileIncludeKind.TypeReferenceDirective:
                return new ReferenceFileLocation
                {
                    _file = file,
                    _ref = file.TypeReferenceDirectives![reference.Index],
                };
            case FileIncludeKind.LibReferenceDirective:
                return new ReferenceFileLocation
                {
                    _file = file,
                    _ref = file.LibReferenceDirectives![reference.Index],
                };
            default:
                Debug.Fail($"unknown reason: {_kind}");
                throw new System.Diagnostics.UnreachableException();
        }
    }

    internal Diagnostic ToDiagnostic(Program program, bool relativeFileName)
    {
        if (relativeFileName)
        {
            return EnsureRelativeFileNameDiagOnce(program).Value!;
        }

        return EnsureDiagOnce(program).Value!;
    }

    private Lazy<Diagnostic?> EnsureRelativeFileNameDiagOnce(Program program)
    {
        var once = Volatile.Read(ref _relativeFileNameDiagOnce);
        if (once is null)
        {
            var created = new Lazy<Diagnostic?>(() => ComputeDiagnostic(
                program,
                fileName => GetRelativePathFromDirectory(program.GetCurrentDirectory(), fileName, program._comparePathsOptions)));
            once = Interlocked.CompareExchange(ref _relativeFileNameDiagOnce, created, null) ?? created;
        }

        return once;
    }

    private Lazy<Diagnostic?> EnsureDiagOnce(Program program)
    {
        var once = Volatile.Read(ref _diagOnce);
        if (once is null)
        {
            var created = new Lazy<Diagnostic?>(() => ComputeDiagnostic(program, fileName => fileName));
            once = Interlocked.CompareExchange(ref _diagOnce, created, null) ?? created;
        }

        return once;
    }

    private Diagnostic ComputeDiagnostic(Program program, Func<string, string> toFileName)
    {
        if (IsReferencedFile())
        {
            return ComputeReferenceFileDiagnostic(program, toFileName);
        }

        switch (_kind)
        {
            case FileIncludeKind.RootFile:
                if (program._opts.Config.ConfigFile is not null)
                {
                    var config = program._opts.Config;
                    var fileName = GetNormalizedAbsolutePath(config.FileNames()[AsIndex()], program.GetCurrentDirectory());
                    var matchedFileSpec = config.GetMatchedFileSpec(fileName);
                    if (matchedFileSpec != "")
                    {
                        return NewCompilerDiagnostic(Part_of_files_list_in_tsconfig_json, matchedFileSpec, toFileName(fileName));
                    }

                    var isDefaultIncludeSpec = config.TryGetMatchedIncludeSpec(fileName, out var matchedIncludeSpec);
                    if (matchedIncludeSpec != "")
                    {
                        if (isDefaultIncludeSpec)
                        {
                            return NewCompilerDiagnostic(Matched_by_default_include_pattern_Asterisk_Asterisk_Slash_Asterisk);
                        }

                        return NewCompilerDiagnostic(Matched_by_include_pattern_0_in_1, matchedIncludeSpec, toFileName(config.ConfigName()));
                    }

                    return NewCompilerDiagnostic(Root_file_specified_for_compilation);
                }
                else
                {
                    return NewCompilerDiagnostic(Root_file_specified_for_compilation);
                }
            case FileIncludeKind.AutomaticTypeDirectiveFile:
                var data = AsAutomaticTypeDirectiveFileData();
                if (!program.Options().UsesWildcardTypes())
                {
                    if (data.PackageId.Name != "")
                    {
                        return NewCompilerDiagnostic(Entry_point_of_type_library_0_specified_in_compilerOptions_with_packageId_1, data.TypeReference, data.PackageId.ToString());
                    }

                    return NewCompilerDiagnostic(Entry_point_of_type_library_0_specified_in_compilerOptions, data.TypeReference);
                }
                else
                {
                    if (data.PackageId.Name != "")
                    {
                        return NewCompilerDiagnostic(Entry_point_for_implicit_type_library_0_with_packageId_1, data.TypeReference, data.PackageId.ToString());
                    }

                    return NewCompilerDiagnostic(Entry_point_for_implicit_type_library_0, data.TypeReference);
                }
            case FileIncludeKind.LibFile:
                if (TryAsLibFileIndex(out var libIndex))
                {
                    return NewCompilerDiagnostic(Library_0_specified_in_compilerOptions, program.Options().Lib![libIndex]);
                }
                else if (program.Options().GetEmitScriptTarget().String() is { Length: > 0 } target)
                {
                    return NewCompilerDiagnostic(Default_library_for_target_0, target);
                }
                else
                {
                    return NewCompilerDiagnostic(Default_library);
                }
            case FileIncludeKind.ContentMapperSupplemental:
                var canonical = program.GetSourceFileByPath((Path)_data!)!;
                return NewCompilerDiagnostic(Supplemental_virtual_file_produced_by_the_content_mapper_for_file_0, toFileName(canonical.FileName()));
            default:
                Debug.Fail($"unknown reason: {_kind}");
                throw new System.Diagnostics.UnreachableException();
        }
    }

    private Diagnostic ComputeReferenceFileDiagnostic(Program program, Func<string, string> toFileName)
    {
        var referenceLocation = program._includeProcessor.GetReferenceLocation(this, program);
        var referenceText = referenceLocation.Text();
        switch (_kind)
        {
            case FileIncludeKind.Import:
                if (!referenceLocation.IsSynthetic)
                {
                    if (referenceLocation.PackageId.Name != "")
                    {
                        return NewCompilerDiagnostic(Imported_via_0_from_file_1_with_packageId_2, referenceText, toFileName(referenceLocation.File.FileName()), referenceLocation.PackageId.ToString());
                    }

                    return NewCompilerDiagnostic(Imported_via_0_from_file_1, referenceText, toFileName(referenceLocation.File.FileName()));
                }
                else if (program._importHelpersImportSpecifiers is { } importHelpersImportSpecifiers
                    && importHelpersImportSpecifiers.TryGetValue(referenceLocation.File.Path(), out var specifier)
                    && specifier == referenceLocation.Node)
                {
                    if (referenceLocation.PackageId.Name != "")
                    {
                        return NewCompilerDiagnostic(Imported_via_0_from_file_1_with_packageId_2_to_import_importHelpers_as_specified_in_compilerOptions, referenceText, toFileName(referenceLocation.File.FileName()), referenceLocation.PackageId.ToString());
                    }

                    return NewCompilerDiagnostic(Imported_via_0_from_file_1_to_import_importHelpers_as_specified_in_compilerOptions, referenceText, toFileName(referenceLocation.File.FileName()));
                }
                else
                {
                    if (referenceLocation.PackageId.Name != "")
                    {
                        return NewCompilerDiagnostic(Imported_via_0_from_file_1_with_packageId_2_to_import_jsx_and_jsxs_factory_functions, referenceText, toFileName(referenceLocation.File.FileName()), referenceLocation.PackageId.ToString());
                    }

                    return NewCompilerDiagnostic(Imported_via_0_from_file_1_to_import_jsx_and_jsxs_factory_functions, referenceText, toFileName(referenceLocation.File.FileName()));
                }
            case FileIncludeKind.ReferenceFile:
                return NewCompilerDiagnostic(Referenced_via_0_from_file_1, referenceText, toFileName(referenceLocation.File.FileName()));
            case FileIncludeKind.TypeReferenceDirective:
                if (referenceLocation.PackageId.Name != "")
                {
                    return NewCompilerDiagnostic(Type_library_referenced_via_0_from_file_1_with_packageId_2, referenceText, toFileName(referenceLocation.File.FileName()), referenceLocation.PackageId.ToString());
                }

                return NewCompilerDiagnostic(Type_library_referenced_via_0_from_file_1, referenceText, toFileName(referenceLocation.File.FileName()));
            case FileIncludeKind.LibReferenceDirective:
                return NewCompilerDiagnostic(Library_referenced_via_0_from_file_1, referenceText, toFileName(referenceLocation.File.FileName()));
            default:
                Debug.Fail($"unknown reason: {_kind}");
                throw new System.Diagnostics.UnreachableException();
        }
    }

    internal Diagnostic? ToRelatedInfo(Program program)
    {
        if (IsReferencedFile())
        {
            return ComputeReferenceFileRelatedInfo(program);
        }

        if (program._opts.Config.ConfigFile is null)
        {
            return null;
        }

        var config = program._opts.Config;
        switch (_kind)
        {
            case FileIncludeKind.RootFile:
                var fileName = GetNormalizedAbsolutePath(config.FileNames()[AsIndex()], program.GetCurrentDirectory());
                var matchedFileSpec = config.GetMatchedFileSpec(fileName);
                if (matchedFileSpec != "")
                {
                    if (GetTsConfigPropArrayElementValue(config.ConfigFile.SourceFile, "files", matchedFileSpec) is { } filesNode)
                    {
                        return CreateDiagnosticForNodeInSourceFile(config.ConfigFile.SourceFile, filesNode.AsNode(), File_is_matched_by_files_list_specified_here);
                    }
                }
                else
                {
                    var isDefaultIncludeSpec = config.TryGetMatchedIncludeSpec(fileName, out var matchedIncludeSpec);
                    if (matchedIncludeSpec != "" && !isDefaultIncludeSpec)
                    {
                        if (GetTsConfigPropArrayElementValue(config.ConfigFile.SourceFile, "include", matchedIncludeSpec) is { } includeNode)
                        {
                            return CreateDiagnosticForNodeInSourceFile(config.ConfigFile.SourceFile, includeNode.AsNode(), File_is_matched_by_include_pattern_specified_here);
                        }
                    }
                }

                break;
            case FileIncludeKind.AutomaticTypeDirectiveFile:
                if (!program.Options().UsesWildcardTypes())
                {
                    var data = AsAutomaticTypeDirectiveFileData();
                    var optionsSyntax = program._includeProcessor.GetCompilerOptionsObjectLiteralSyntax(program);
                    if (optionsSyntax is not null
                        && GetOptionsSyntaxByArrayElementValue(optionsSyntax, "types", data.TypeReference) is { } typesSyntax)
                    {
                        return CreateDiagnosticForNodeInSourceFile(config.ConfigFile.SourceFile, typesSyntax.AsNode(), File_is_entry_point_of_type_library_specified_here);
                    }
                }

                break;
            case FileIncludeKind.LibFile:
                if (TryAsLibFileIndex(out var libIndex))
                {
                    var optionsSyntax = program._includeProcessor.GetCompilerOptionsObjectLiteralSyntax(program);
                    if (optionsSyntax is not null
                        && GetOptionsSyntaxByArrayElementValue(optionsSyntax, "lib", program.Options().Lib![libIndex]) is { } libSyntax)
                    {
                        return CreateDiagnosticForNodeInSourceFile(config.ConfigFile.SourceFile, libSyntax.AsNode(), File_is_library_specified_here);
                    }
                }
                else if (program.Options().GetEmitScriptTarget().String() is { Length: > 0 } target)
                {
                    if (ForEachPropertyAssignment(
                        program._includeProcessor.GetCompilerOptionsObjectLiteralSyntax(program),
                        "target",
                        GetCallbackForFindingPropertyAssignmentByValue(target)) is { } targetValueSyntax)
                    {
                        return CreateDiagnosticForNodeInSourceFile(config.ConfigFile.SourceFile, targetValueSyntax.AsNode(), File_is_default_library_for_target_specified_here);
                    }
                }

                break;
            case FileIncludeKind.ContentMapperSupplemental:
                return null;
            default:
                Debug.Fail($"unknown reason: {_kind}");
                throw new System.Diagnostics.UnreachableException();
        }

        return null;
    }

    private Diagnostic? ComputeReferenceFileRelatedInfo(Program program)
    {
        var referenceLocation = program._includeProcessor.GetReferenceLocation(this, program);
        if (referenceLocation.IsSynthetic)
        {
            return null;
        }

        switch (_kind)
        {
            case FileIncludeKind.Import:
                return referenceLocation.DiagnosticAt(File_is_included_via_import_here);
            case FileIncludeKind.ReferenceFile:
                return referenceLocation.DiagnosticAt(File_is_included_via_reference_here);
            case FileIncludeKind.TypeReferenceDirective:
                return referenceLocation.DiagnosticAt(File_is_included_via_type_library_reference_here);
            case FileIncludeKind.LibReferenceDirective:
                return referenceLocation.DiagnosticAt(File_is_included_via_library_reference_here);
            default:
                Debug.Fail($"unknown reason: {_kind}");
                throw new System.Diagnostics.UnreachableException();
        }
    }
}

/// <summary>Go: <c>type referencedFileData struct</c>。</summary>
internal sealed class ReferencedFileData
{
    internal Path File; // Go: file tspath.Path
    internal int Index;
    internal Node? Synthetic;
}

/// <summary>Go: <c>type referenceFileLocation struct</c>。</summary>
internal sealed class ReferenceFileLocation
{
    internal SourceFile _file = null!;
    internal Node? _node;
    internal FileReference? _ref;
    internal PackageId _packageId;
    internal bool _isSynthetic;

    internal SourceFile File => _file;

    internal Node? Node => _node;

    internal PackageId PackageId => _packageId;

    internal bool IsSynthetic => _isSynthetic;

    internal string Text()
    {
        if (_node is not null)
        {
            if (!NodeIsSynthesized(_node))
            {
                var text = _file.Text();
                return text[SkipTrivia(text, _node.Loc.Pos()).._node.End()];
            }
            else
            {
                return "\"" + _node.Text() + "\"";
            }
        }
        else
        {
            // Go 在这里直接解引用 r.ref（Import 分支之外 ref 必定非 nil）。
            var text = _file.Text();
            return text[_ref!.Range.Pos().._ref!.Range.End()];
        }
    }

    internal Diagnostic DiagnosticAt(Message message, params object?[] args)
    {
        if (_node is not null)
        {
            return CreateDiagnosticForNodeInSourceFile(_file, _node, message, args);
        }
        else
        {
            return NewDiagnostic(_file, _ref!.Range, message, args);
        }
    }
}

/// <summary>Go: <c>type automaticTypeDirectiveFileData struct</c>。</summary>
internal sealed class AutomaticTypeDirectiveFileData
{
    internal string TypeReference = ""; // Go: typeReference string
    internal PackageId PackageId;
}
