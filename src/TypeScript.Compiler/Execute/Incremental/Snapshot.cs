// ported from: internal/execute/incremental/snapshot.go
//
// 与 Go 的偏离（docs/architecture.md §2/§3）：
// 1) Go 的 `type snapshot struct`（未导出）→ C# `internal sealed class Snapshot`（Go 里传 `*snapshot` 就是传引用，
//    C# 直接用对象引用；`&to.snapshot` 那种"把局部结构体的地址交出去"在 C# 里就是把这个对象交出去）。
// 2) Go 未导出字段 → `internal _camelCase`；跨类（Program/各 handler）访问，故全部 `internal`。
// 3) `atomic.Bool` → `volatile bool`（仓库先例：Core/Workgroup.cs）。
// 4) `sync.Once`（`allFilesExcludingDefaultLibraryFileOnce`）→ 一把锁 + 标志位；Go 的 sync.Once 用**首次调用**的参数，
//    这里保持同样的语义（后续调用的参数被忽略）。
// 5) `strings.Builder` → `System.Text.StringBuilder`（Go 的 `WriteString` → `Append`）。
// 6) `time`/`encoding/hex` 无关；`ComputeHash` 用 `Xxh3.HashString128` + `Convert.ToHexStringLower`（小写十六进制，
//    与 Go 的 `hex.EncodeToString` 一致，见 Compiler/Compiler.cs 的同一处理）。
// 7) `diag.toDiagnostic(p, file)` 的 `file` 在 Go 里可为 nil（`*ast.SourceFile`）→ C# `SourceFile?`。
// 8) `slices.Sort([]tspath.Path)` → `Array.Sort(keys, (a, b) => string.CompareOrdinal(a.Value, b.Value))`（Go 的 string `<`）。
namespace TypeScript.Execute.Incremental;

using System.Globalization;
using System.Text;
using TypeScript.Ast;
using TypeScript.Collections;
using TypeScript.Compiler;
using TypeScript.Core;
using TypeScript.Diagnostics;
using static TypeScript.Ast.Ast;
using static TypeScript.Collections.Collections;
using static TypeScript.Core.Core;
using static TypeScript.Diagnostics.Diagnostics;
using static TypeScript.Execute.Incremental.Incremental;
using static TypeScript.TSPath.TSPath;
using CompilerProgram = TypeScript.Compiler.Program;
using Path = TypeScript.TSPath.Path;

/// <summary>Go: <c>type FileInfo struct</c>（snapshot.go:20-25）。</summary>
public sealed class FileInfo
{
    internal string _version = "";
    internal string _signature = "";
    internal bool _affectsGlobalScope;
    internal ModuleKind _impliedNodeFormat;

    public string Version() => _version;

    public string Signature() => _signature;

    public bool AffectsGlobalScope() => _affectsGlobalScope;

    /// <summary>Go 的 <c>core.ResolutionMode</c>（= <c>ModuleKind</c>，C# 无类型别名）。</summary>
    public ModuleKind ImpliedNodeFormat() => _impliedNodeFormat;
}

/// <summary>Go: <c>type FileEmitKind uint32</c>（snapshot.go:41-56）。成员名照 §3.3 去掉 Go 的 <c>FileEmitKind</c> 前缀。</summary>
[Flags]
public enum FileEmitKind : uint
{
    /// <summary>Go: <c>FileEmitKindNone FileEmitKind = 0</c>。</summary>
    None = 0,

    /// <summary>Go: <c>FileEmitKindJs</c>：emit js file。</summary>
    Js = 1 << 0,

    /// <summary>Go: <c>FileEmitKindJsMap</c>：emit js.map file。</summary>
    JsMap = 1 << 1,

    /// <summary>Go: <c>FileEmitKindJsInlineMap</c>：emit inline source map in js file。</summary>
    JsInlineMap = 1 << 2,

    /// <summary>Go: <c>FileEmitKindDtsErrors</c>：emit dts errors。</summary>
    DtsErrors = 1 << 3,

    /// <summary>Go: <c>FileEmitKindDtsEmit</c>：emit d.ts file。</summary>
    DtsEmit = 1 << 4,

    /// <summary>Go: <c>FileEmitKindDtsMap</c>：emit d.ts.map file。</summary>
    DtsMap = 1 << 5,

    /// <summary>Go: <c>FileEmitKindDts = FileEmitKindDtsErrors | FileEmitKindDtsEmit</c>。</summary>
    Dts = DtsErrors | DtsEmit,

    /// <summary>Go: <c>FileEmitKindAllJs = FileEmitKindJs | FileEmitKindJsMap | FileEmitKindJsInlineMap</c>。</summary>
    AllJs = Js | JsMap | JsInlineMap,

    /// <summary>Go: <c>FileEmitKindAllDtsEmit = FileEmitKindDtsEmit | FileEmitKindDtsMap</c>。</summary>
    AllDtsEmit = DtsEmit | DtsMap,

    /// <summary>Go: <c>FileEmitKindAllDts = FileEmitKindDts | FileEmitKindDtsMap</c>。</summary>
    AllDts = Dts | DtsMap,

    /// <summary>Go: <c>FileEmitKindAll = FileEmitKindAllJs | FileEmitKindAllDts</c>。</summary>
    All = AllJs | AllDts,
}

/// <summary>Signature (Hash of d.ts emitted) 的 C# 对应物。Go: <c>type emitSignature struct</c>（snapshot.go:111-116）。</summary>
internal sealed class EmitSignature
{
    internal string _signature = "";

    /// <summary>Go 是 nil 切片（nil 与空切片有区别：`GetNewEmitSignature` 判 nil）。</summary>
    internal string[]? _signatureWithDifferentOptions;

    /// <summary>Go: <c>func (e *emitSignature) getNewEmitSignature(oldOptions, newOptions *core.CompilerOptions) *emitSignature</c>。</summary>
    internal EmitSignature GetNewEmitSignature(CompilerOptions oldOptions, CompilerOptions newOptions)
    {
        if (oldOptions.DeclarationMap.IsTrue() == newOptions.DeclarationMap.IsTrue())
        {
            return this;
        }
        if (_signatureWithDifferentOptions is null)
        {
            return new EmitSignature
            {
                _signatureWithDifferentOptions = [_signature],
            };
        }
        return new EmitSignature
        {
            _signature = _signatureWithDifferentOptions[0],
        };
    }
}

/// <summary>Go: <c>type buildInfoDiagnosticWithFileName struct</c>（snapshot.go:133-151）。</summary>
internal sealed class BuildInfoDiagnosticWithFileName
{
    /// <summary>filename if it is for a File thats other than its stored for。</summary>
    internal Path _file;

    internal bool _noFile;
    internal int _pos;
    internal int _end;
    internal int _code;
    internal Category _category;
    internal string _source = "";
    internal string _messageText = "";
    internal Key _messageKey;
    internal string[]? _messageArgs;
    internal List<BuildInfoDiagnosticWithFileName> _messageChain = [];
    internal List<BuildInfoDiagnosticWithFileName> _relatedInformation = [];
    internal bool _reportsUnnecessary;
    internal bool _reportsDeprecated;
    internal bool _skippedOnNoEmit;
    internal RepopulateDiagnosticInfo? _repopulateInfo;

    /// <summary>Go: <c>func (b *buildInfoDiagnosticWithFileName) toDiagnostic(p *compiler.Program, file *ast.SourceFile) *ast.Diagnostic</c>。</summary>
    internal Diagnostic ToDiagnostic(CompilerProgram p, SourceFile? file)
    {
        SourceFile? fileForDiagnostic;
        if (!IsEmptyPath(_file))
        {
            fileForDiagnostic = p.GetSourceFileByPath(_file);
        }
        else if (!_noFile)
        {
            fileForDiagnostic = file;
        }
        else
        {
            fileForDiagnostic = null;
        }

        if (_repopulateInfo is not null)
        {
            return RepopulateDiagnosticChain(this, p, fileForDiagnostic);
        }

        var messageChain = new List<Diagnostic>();
        foreach (var msg in _messageChain)
        {
            messageChain.Add(msg.ToDiagnostic(p, fileForDiagnostic));
        }
        var relatedInformation = new List<Diagnostic>();
        foreach (var info in _relatedInformation)
        {
            relatedInformation.Add(info.ToDiagnostic(p, fileForDiagnostic));
        }
        var diagnostic = NewDiagnosticFromSerialized(
            fileForDiagnostic,
            NewTextRange(_pos, _end),
            _code,
            _category,
            _messageKey,
            _messageArgs,
            [.. messageChain],
            [.. relatedInformation],
            _reportsUnnecessary,
            _reportsDeprecated,
            _skippedOnNoEmit);
        if (_source != "" || _messageText != "")
        {
            diagnostic.SetExternalData(_source, _messageText);
        }
        return diagnostic;
    }

    /// <summary>Go: <c>func (b *buildInfoDiagnosticWithFileName) toDiagnosticWithoutRepopulate(p, file)</c>。</summary>
    internal Diagnostic ToDiagnosticWithoutRepopulate(CompilerProgram p, SourceFile? file)
    {
        var messageChain = new List<Diagnostic>();
        foreach (var msg in _messageChain)
        {
            messageChain.Add(msg.ToDiagnostic(p, file));
        }
        var relatedInformation = new List<Diagnostic>();
        foreach (var info in _relatedInformation)
        {
            relatedInformation.Add(info.ToDiagnostic(p, file));
        }
        return NewDiagnosticFromSerialized(
            file,
            NewTextRange(_pos, _end),
            _code,
            _category,
            _messageKey,
            _messageArgs,
            [.. messageChain],
            [.. relatedInformation],
            _reportsUnnecessary,
            _reportsDeprecated,
            _skippedOnNoEmit);
    }
}

/// <summary>Go: <c>type DiagnosticsOrBuildInfoDiagnosticsWithFileName struct</c>（snapshot.go:153-156）。</summary>
public sealed class DiagnosticsOrBuildInfoDiagnosticsWithFileName
{
    internal Diagnostic[]? _diagnostics;
    internal BuildInfoDiagnosticWithFileName[]? _buildInfoDiagnostics;

    /// <summary>Go: <c>func (d *DiagnosticsOrBuildInfoDiagnosticsWithFileName) getDiagnostics(p, file)</c>（带缓存）。</summary>
    internal Diagnostic[] GetDiagnostics(CompilerProgram p, SourceFile? file)
    {
        if (_diagnostics is not null)
        {
            return _diagnostics;
        }
        // Convert and cache the diagnostics
        // （Go 对 nil 的 buildInfoDiagnostics 每次都会重算并返回 nil；C# 侧缓存空数组，`len()` 判断处可观察行为相同。）
        _diagnostics = _buildInfoDiagnostics is null
            ? []
            : Map(_buildInfoDiagnostics, diag => diag.ToDiagnostic(p, file));
        return _diagnostics;
    }
}

/// <summary>Go: <c>type snapshot struct</c>（snapshot.go:306-350）。</summary>
internal sealed class Snapshot
{
    // These are the fields that get serialized

    /// <summary>Information of the file eg. its version, signature etc。</summary>
    internal SyncMap<Path, FileInfo> _fileInfos = new();

    internal CompilerOptions _options = null!;

    /// <summary>Contains the map of ReferencedSet=Referenced files of the file if module emit is enabled。</summary>
    internal ReferenceMap _referencedMap = new();

    /// <summary>Cache of semantic diagnostics for files with their Path being the key。</summary>
    internal SyncMap<Path, DiagnosticsOrBuildInfoDiagnosticsWithFileName> _semanticDiagnosticsPerFile = new();

    /// <summary>Cache of dts emit diagnostics for files with their Path being the key。</summary>
    internal SyncMap<Path, DiagnosticsOrBuildInfoDiagnosticsWithFileName> _emitDiagnosticsPerFile = new();

    /// <summary>The map has key by source file's path that has been changed。</summary>
    internal SyncSet<Path> _changedFilesSet = new();

    /// <summary>Files pending to be emitted。</summary>
    internal SyncMap<Path, FileEmitKind> _affectedFilesPendingEmit = new();

    /// <summary>Name of the file whose dts was the latest to change。</summary>
    internal string _latestChangedDtsFile = "";

    /// <summary>Hash of d.ts emitted for the file, use to track when emit of d.ts changes。</summary>
    internal SyncMap<Path, EmitSignature> _emitSignatures = new();

    /// <summary>Recorded if program had errors that need to be reported even with --noCheck。</summary>
    internal Tristate _hasErrors = Tristate.TSUnknown;

    /// <summary>Recorded if program had semantic errors only for non incremental build。</summary>
    internal bool _hasSemanticErrors;

    /// <summary>If semantic diagnostic check is pending。</summary>
    internal bool _checkPending;

    /// <summary>Looked up package.json files from。</summary>
    internal string[]? _packageJsons;

    internal string[]? _missingPackageJsons;

    // Additional fields that are not serialized but needed to track state

    /// <summary>true if build info emit is pending（Go 的 <c>atomic.Bool</c> → <c>volatile bool</c>）。</summary>
    internal volatile bool _buildInfoEmitPending;

    internal Tristate _hasErrorsFromOldState = Tristate.TSUnknown;
    internal bool _hasSemanticErrorsFromOldState;
    internal string[]? _packageJsonsFromOldState;
    internal string[]? _missingPackageJsonsFromOldState;

    /// <summary>Cache of all files excluding default library file for the current program（Go 的 sync.Once）。</summary>
    internal SourceFile[]? _allFilesExcludingDefaultLibraryFile;

    internal bool _hasChangedDtsFile;
    internal bool _hasEmitDiagnostics;

    /// <summary>Used with testing to add text of hash for better comparison。</summary>
    internal bool _hashWithText;

    // Go: allFilesExcludingDefaultLibraryFileOnce sync.Once + allFilesExcludingDefaultLibraryFile 的缓存。
    private readonly object _allFilesExcludingDefaultLibraryFileLock = new();
    private bool _gotAllFilesExcludingDefaultLibraryFile;

    internal void AddFileToChangeSet(Path filePath)
    {
        _changedFilesSet.Add(filePath);
        _buildInfoEmitPending = true;
    }

    internal void AddFileToAffectedFilesPendingEmit(Path filePath, FileEmitKind emitKind)
    {
        var (existingKind, _) = _affectedFilesPendingEmit.Load(filePath);
        _affectedFilesPendingEmit.Store(filePath, existingKind | emitKind);
        if ((emitKind & FileEmitKind.DtsErrors) != 0)
        {
            _emitDiagnosticsPerFile.Delete(filePath);
        }
        _buildInfoEmitPending = true;
    }

    internal SourceFile[] GetAllFilesExcludingDefaultLibraryFile(CompilerProgram program, SourceFile? firstSourceFile)
    {
        lock (_allFilesExcludingDefaultLibraryFileLock)
        {
            if (_gotAllFilesExcludingDefaultLibraryFile)
            {
                return _allFilesExcludingDefaultLibraryFile!;
            }

            var files = program.GetSourceFiles();
            var result = new List<SourceFile>(files.Length);
            void AddSourceFile(SourceFile file)
            {
                if (!program.IsSourceFileDefaultLibrary(file.Path()))
                {
                    result.Add(file);
                }
            }

            if (firstSourceFile is not null)
            {
                AddSourceFile(firstSourceFile);
            }
            foreach (var file in files)
            {
                if (file != firstSourceFile)
                {
                    AddSourceFile(file);
                }
            }

            _allFilesExcludingDefaultLibraryFile = [.. result];
            _gotAllFilesExcludingDefaultLibraryFile = true;
            return _allFilesExcludingDefaultLibraryFile;
        }
    }

    internal string ComputeSignatureWithDiagnostics(SourceFile file, string text, WriteFileData data)
    {
        var builder = new StringBuilder();
        builder.Append(GetTextHandlingSourceMapForSignature(text, data));
        foreach (var diag in data.Diagnostics)
        {
            DiagnosticToStringBuilder(diag, file, builder);
        }
        return ComputeHash(builder.ToString());
    }

    internal string ComputeHash(string text) => Incremental.ComputeHash(text, _hashWithText);

    internal bool CanUseIncrementalState()
    {
        if (!_options.IsIncremental() && _options.Build.IsTrue())
        {
            // If not incremental build (with tsc -b), we don't need to track state except diagnostics per file so we can use it
            return false;
        }
        return true;
    }
}

public static partial class Incremental
{
    /// <summary>Go: <c>func ComputeHash(text string, hashWithText bool) string</c>（snapshot.go:32）。</summary>
    public static string ComputeHash(string text, bool hashWithText)
    {
        var hash = Convert.ToHexStringLower(TypeScript.Xxh3.Xxh3.Bytes(TypeScript.Xxh3.Xxh3.HashString128(text)));
        if (hashWithText)
        {
            hash += "-" + text;
        }
        return hash;
    }

    /// <summary>Go: <c>func GetFileEmitKind(options *core.CompilerOptions) FileEmitKind</c>（snapshot.go:59）。</summary>
    public static FileEmitKind GetFileEmitKind(CompilerOptions options)
    {
        var result = FileEmitKind.Js;
        if (options.SourceMap.IsTrue())
        {
            result |= FileEmitKind.JsMap;
        }
        if (options.InlineSourceMap.IsTrue())
        {
            result |= FileEmitKind.JsInlineMap;
        }
        if (options.GetEmitDeclarations())
        {
            result |= FileEmitKind.Dts;
        }
        if (options.DeclarationMap.IsTrue())
        {
            result |= FileEmitKind.DtsMap;
        }
        if (options.EmitDeclarationOnly.IsTrue())
        {
            result &= FileEmitKind.AllDts;
        }
        return result;
    }

    /// <summary>Go: <c>func getPendingEmitKindWithOptions(options, oldOptions *core.CompilerOptions) FileEmitKind</c>。</summary>
    internal static FileEmitKind GetPendingEmitKindWithOptions(CompilerOptions options, CompilerOptions oldOptions)
    {
        var oldEmitKind = GetFileEmitKind(oldOptions);
        var newEmitKind = GetFileEmitKind(options);
        return GetPendingEmitKind(newEmitKind, oldEmitKind);
    }

    /// <summary>Go: <c>func getPendingEmitKind(emitKind, oldEmitKind FileEmitKind) FileEmitKind</c>。</summary>
    internal static FileEmitKind GetPendingEmitKind(FileEmitKind emitKind, FileEmitKind oldEmitKind)
    {
        if (oldEmitKind == emitKind)
        {
            return FileEmitKind.None;
        }
        if (oldEmitKind == 0 || emitKind == 0)
        {
            return emitKind;
        }
        var diff = oldEmitKind ^ emitKind;
        var result = FileEmitKind.None;
        // If there is diff in Js emit, pending emit is js emit flags
        if ((diff & FileEmitKind.AllJs) != 0)
        {
            result |= emitKind & FileEmitKind.AllJs;
        }
        // If dts errors pending, add dts errors flag
        if ((diff & FileEmitKind.DtsErrors) != 0)
        {
            result |= emitKind & FileEmitKind.AllDts;
        }
        // If there is diff in Dts emit, pending emit is dts emit flags
        if ((diff & FileEmitKind.AllDtsEmit) != 0)
        {
            result |= emitKind & FileEmitKind.AllDtsEmit;
        }
        return result;
    }

    /// <summary>Go 的 <c>b.file != ""</c> 判定（<c>tspath.Path</c> 的零值在 C# 里是 <c>default(Path)</c>，其 Value 为 null）。</summary>
    internal static bool IsEmptyPath(Path path) => string.IsNullOrEmpty(path.Value);

    /// <summary>Go: <c>func getTextHandlingSourceMapForSignature(text string, data *compiler.WriteFileData) string</c>。</summary>
    internal static string GetTextHandlingSourceMapForSignature(string text, WriteFileData data)
    {
        if (data.SourceMapUrlPos != -1)
        {
            return text[..data.SourceMapUrlPos];
        }
        return text;
    }

    /// <summary>Go: <c>func diagnosticToStringBuilder(diagnostic *ast.Diagnostic, file *ast.SourceFile, builder *strings.Builder)</c>。</summary>
    internal static void DiagnosticToStringBuilder(Diagnostic? diagnostic, SourceFile? file, StringBuilder builder)
    {
        if (diagnostic is null)
        {
            return;
        }
        builder.Append('\n');
        if (diagnostic.File() != file)
        {
            builder.Append(EnsurePathIsNonModuleName(GetRelativePathFromDirectory(
                GetDirectoryPath(file!.Path().Value),
                diagnostic.File()!.Path().Value,
                default(TypeScript.TSPath.ComparePathsOptions))));
        }
        if (diagnostic.File() is not null)
        {
            // Go: fmt.Sprintf("(%d,%d): ", diagnostic.Pos(), diagnostic.Len())
            builder.Append('(').Append(diagnostic.Pos().ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(diagnostic.Len().ToString(CultureInfo.InvariantCulture)).Append("): ");
        }
        builder.Append(diagnostic.Category().Name());
        builder.Append(diagnostic.Code().ToString(CultureInfo.InvariantCulture)).Append(": ");
        builder.Append((string)diagnostic.MessageKey());
        builder.Append('\n');
        if (diagnostic.MessageArgs() is { } args)
        {
            foreach (var arg in args)
            {
                builder.Append(arg);
                builder.Append('\n');
            }
        }
        if (diagnostic.MessageChain() is { } messageChain)
        {
            foreach (var chain in messageChain)
            {
                DiagnosticToStringBuilder(chain, file, builder);
            }
        }
        if (diagnostic.RelatedInformation() is { } relatedInformation)
        {
            foreach (var info in relatedInformation)
            {
                DiagnosticToStringBuilder(info, file, builder);
            }
        }
    }

    /// <summary>Go: <c>func repopulateDiagnosticChain(b *buildInfoDiagnosticWithFileName, p, file) *ast.Diagnostic</c>。</summary>
    internal static Diagnostic RepopulateDiagnosticChain(BuildInfoDiagnosticWithFileName b, CompilerProgram p, SourceFile? file)
    {
        var info = b._repopulateInfo!;
        switch (info.Kind)
        {
            case RepopulateDiagnosticKind.ModeMismatch:
                return RepopulateModeMismatchChain(b, p, file);
            case RepopulateDiagnosticKind.ModuleNotFound:
                return RepopulateModuleNotFoundChain(b, p, file, info);
            default:
                // Fall back to using the stored (possibly stale) data
                return b.ToDiagnosticWithoutRepopulate(p, file);
        }
    }

    internal static Diagnostic RepopulateModeMismatchChain(BuildInfoDiagnosticWithFileName b, CompilerProgram p, SourceFile? file)
    {
        if (file is null)
        {
            return b.ToDiagnosticWithoutRepopulate(p, file);
        }

        var details = TypeScript.Checker.Checker.CreateModeMismatchDetails(p, file);

        var nextChain = new List<Diagnostic>();
        foreach (var msg in b._messageChain)
        {
            nextChain.Add(msg.ToDiagnostic(p, file));
        }

        return NewDiagnosticFromSerialized(
            file,
            NewTextRange(b._pos, b._end),
            details.Message.Code(),
            details.Message.Category(),
            details.Message.Key(),
            StringifyArgs(details.Args),
            [.. nextChain],
            null,
            false,
            false,
            false);
    }

    internal static Diagnostic RepopulateModuleNotFoundChain(BuildInfoDiagnosticWithFileName b, CompilerProgram p, SourceFile? file, RepopulateDiagnosticInfo info)
    {
        if (file is null)
        {
            return b.ToDiagnosticWithoutRepopulate(p, file);
        }

        var packageName = info.PackageName;
        if (packageName == "")
        {
            packageName = info.ModuleReference;
        }

        var details = TypeScript.Checker.Checker.CreateModuleNotFoundChain(p, file, info.ModuleReference, info.Mode, packageName);

        var nextChain = new List<Diagnostic>();
        foreach (var msg in b._messageChain)
        {
            nextChain.Add(msg.ToDiagnostic(p, file));
        }

        return NewDiagnosticFromSerialized(
            file,
            NewTextRange(b._pos, b._end),
            details.Message.Code(),
            details.Message.Category(),
            details.Message.Key(),
            StringifyArgs(details.Args),
            [.. nextChain],
            null,
            false,
            false,
            false);
    }
}
