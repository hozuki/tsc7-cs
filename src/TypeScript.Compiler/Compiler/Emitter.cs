// ported from: internal/compiler/emitter.go（576 行，全部）
//
// 与 Go 的偏离（逐条；其余与 Go 逐行对应）：
// 1) Go 未导出 struct `emitter` → `internal sealed partial class Emitter`；未导出字段 → `internal _camelCase`
//    （`_host`/`_emitOnly`/`_emitterDiagnostics`/`_writer`/`_paths`/`_sourceFile`/`_emitResult`/`_forceEmit`/
//    `_writeFile`/`_tr`）。**跨类写入点**：program.go 的 `Program.Emit` 逐个字段赋值并调用 `Emit()`，所以这些
//    字段必须是 `internal`（不是 private）。
// 2) Go 未导出接口 `declarationTransformer` → `IDeclarationTransformer`（docs §2：未导出接口加 `I` 前缀，
//    先例 `IOptionParser`/`IProcessExitState`）。C# **没有结构性接口满足**，而
//    `DeclarationTransformer`/`SupplementalReferencesTransformer`（Transformers/Declarations，非本片所有权）
//    都没有声明该接口，故这里用只做转发的 `DeclarationTransformerAdapter` 在**构造点**把它们包成统一形状；
//    若那两个类型以后显式声明 `: IDeclarationTransformer`，删掉该适配器即可（见报告）。
// 3) `(T, error)` → 抛异常（docs §3.2），但 emitter 对"写文件失败"是**按错误值分支**（Go 里加
//    `Could_not_write_file_0_Colon_1` 诊断并继续），所以 `WriteText` 的调用点用 `try/catch (Exception err)` 保住
//    Go 的分支形状（`err.Error()` → `err.Message`）。写文件的 `error` 来自注入的 `writeFile` 回调/FS，任意异常都可能。
// 4) `WriteFileData`/`EmitResult`/`SourceMapEmitResult`/`WriteFile`（委托）都在 program.go（T10Program 的实现），
//    本文件按 Go 的字段集引用：`WriteFileData{SourceMapUrlPos, BuildInfo, Diagnostics, SkippedDtsWrite, SourceFile}`、
//    `EmitResult{EmitSkipped, Diagnostics, EmittedFiles, SourceMaps}`、`WriteFile(string, string, WriteFileData)`。
// 5) `append(slice, x)`（Go 的 nil slice 合法）→ `Core.Concatenate(slice, [x])`（仓库既有做法，见
//    Transformers/Declarations/SupplementalReferences.cs）。
// 6) Go 的 `defer f()` → `var pop = …; try { … } finally { pop?.Invoke(); }`；同一函数里多个 defer 按
//    **LIFO** 展开成嵌套 try/finally（内层 finally 先跑），顺序与 Go 一致。
// 7) `tracing.PhaseEmit` → `Phase.Emit`；`map[string]any{…}` → `new Dictionary<string, object?> { ["k"] = v }`。
// 8) 「命名空间遮蔽」实测坑（docs §2）：`Printer`/`SourceMap`/`OutputPaths`/`ModuleTransforms`/`EsTransforms`/
//    `TsTransforms`/`JsxTransforms`/`Inliners`/`Declarations`/`Binder` 都是"命名空间 + 同名静态类"，
//    裸写会命中命名空间段（CS0234）→ 包级函数一律全限定 `TypeScript.<Ns>.<Ns>.X(...)`；只有 `using static`
//    引入的（TSPath/Core/Ast/StringUtil/Diagnostics）才用裸名。
// 9) `printer.GetEmitContext()` → `TypeScript.Printer.Printer.GetEmitContext()` 返回 `(EmitContext, Action)`；
//    `printer.NewPrinter(...)` → `TypeScript.Printer.Printer.NewPrinter(...)`。
// 10) `sourcemap.Source`/`Generator`/`RawSourceMap` → `TypeScript.SourceMap.*`；`Generator.String()`（Go 的 stringer）
//     → C# 是 `SourceMap` 静态类上的扩展方法 `String(this Generator)`（SourceMap/Generator.cs 文件头第 10 条），
//     靠 `using static TypeScript.SourceMap.SourceMap;` 保住 Go 的 `sourceMapGenerator.String()` 写法。
// 11) `core.IfElse(a, b, c)` → `IfElse(a, b, c)`（Core 静态类，`using static`）；`core.Filter/Some/SingleElementSlice`
//     → `Filter/Some/SingleElementSlice`。
// 12) 返回可空 transformer 的工厂（module/estransforms/tstransforms 的 C# 签名是 `Transformer?`，Go 侧恒非 nil）
//     → `!`（Go 里 nil 会在下一行解引用 panic，这里与 Go 的契约一致）。
// 13) `EmitOnly`（`type EmitOnly byte` + iota）→ `enum EmitOnly : byte`，成员名照 Go、数值照 iota。
// 14) `string(e.sourceFile.Path())` → `(string)_sourceFile.Path()`（`Path` 有 explicit operator string）。
// 15) `for _, x := range …` / `core.Filter` 等照抄；`len(s) == 0` → `s.Length == 0`。
namespace TypeScript.Compiler;

using System;
using System.Collections.Generic;
using TypeScript.Ast;
using TypeScript.Binder;
using TypeScript.Core;
using TypeScript.Printer;
using TypeScript.Tracing;
using TypeScript.Transformers;
using static TypeScript.Ast.Ast;
using static TypeScript.Core.Core;
using static TypeScript.Diagnostics.Diagnostics;
using static TypeScript.SourceMap.SourceMap;
using static TypeScript.StringUtil.StringUtil;
using static TypeScript.TSPath.TSPath;
using TypeScript.TSPath;

/// <summary>Go: <c>type EmitOnly byte</c>（数值照 Go 的 iota）。</summary>
public enum EmitOnly : byte
{
    EmitAll = 0,
    EmitOnlyJs = 1,
    EmitOnlyDts = 2,
    EmitOnlyBuilderSignature = 3,
}

internal sealed partial class Emitter
{
    internal EmitHost _host = null!;
    internal EmitOnly _emitOnly;
    internal DiagnosticsCollection _emitterDiagnostics = new();
    internal EmitTextWriter _writer = null!;
    internal TypeScript.OutputPaths.OutputPaths _paths = null!;
    internal SourceFile _sourceFile = null!;
    internal EmitResult _emitResult = new();
    internal bool _forceEmit;
    internal WriteFile? _writeFile;
    internal Tracing? _tr;

    internal void Emit()
    {
        var pop = _tr?.Push(Phase.Emit, "emit", new Dictionary<string, object?> { ["path"] = (string)_sourceFile.Path() }, true);
        try
        {
            EmitJSFile(_sourceFile, _paths.JsFilePath(), _paths.SourceMapFilePath());
            EmitDeclarationFile(_sourceFile, _paths.DeclarationFilePath(), _paths.DeclarationMapPath());
            _emitResult.Diagnostics = _emitterDiagnostics.GetDiagnostics();
        }
        finally
        {
            pop?.Invoke();
        }
    }

    internal IDeclarationTransformer[] GetDeclarationTransformers(EmitContext emitContext, SourceFile sourceFile, string declarationFilePath, string declarationMapPath)
    {
        var forceDtsEmit = _emitOnly == EmitOnly.EmitOnlyBuilderSignature || _forceEmit && _emitOnly == EmitOnly.EmitOnlyDts;
        // 见文件头第 2 条：C# 无结构性接口满足 → 在构造点用转发适配器统一形状。
        var declarationTransformer = TypeScript.Transformers.Declarations.Declarations.NewDeclarationTransformer(_host, emitContext, _host.Options(), declarationFilePath, declarationMapPath);
        var supplementalReferencesTransformer = TypeScript.Transformers.Declarations.Declarations.NewSupplementalReferencesTransformer(_host, sourceFile, declarationFilePath, forceDtsEmit);
        return
        [
            new DeclarationTransformerAdapter(declarationTransformer.TransformSourceFile, declarationTransformer.GetDiagnostics),
            new DeclarationTransformerAdapter(supplementalReferencesTransformer.TransformSourceFile, supplementalReferencesTransformer.GetDiagnostics),
        ];
    }

    internal SourceFile RunScriptTransformers(EmitContext emitContext, SourceFile sourceFile)
    {
        var pop = _tr?.Push(Phase.Emit, "transformNodes", new Dictionary<string, object?> { ["path"] = (string)sourceFile.Path() }, false);
        try
        {
            foreach (var transformer in Compiler.GetScriptTransformers(emitContext, _host, sourceFile))
            {
                sourceFile = transformer.TransformSourceFile(sourceFile);
            }

            return sourceFile;
        }
        finally
        {
            pop?.Invoke();
        }
    }

    internal (SourceFile SourceFile, Diagnostic[] Diagnostics) RunDeclarationTransformers(EmitContext emitContext, SourceFile sourceFile, string declarationFilePath, string declarationMapPath)
    {
        var pop = _tr?.Push(Phase.Emit, "transformNodes", new Dictionary<string, object?> { ["path"] = (string)sourceFile.Path() }, false);
        try
        {
            var diags = new List<Diagnostic>();
            foreach (var transformer in GetDeclarationTransformers(emitContext, sourceFile, declarationFilePath, declarationMapPath))
            {
                sourceFile = transformer.TransformSourceFile(sourceFile);
                diags.AddRange(transformer.GetDiagnostics());
            }

            return (sourceFile, [.. diags]);
        }
        finally
        {
            pop?.Invoke();
        }
    }

    internal void EmitJSFile(SourceFile? sourceFile, string jsFilePath, string sourceMapFilePath)
    {
        var options = _host.Options();

        if (sourceFile is null || _emitOnly != EmitOnly.EmitAll && _emitOnly != EmitOnly.EmitOnlyJs || jsFilePath.Length == 0)
        {
            return;
        }

        if (!_forceEmit && (options.NoEmit == Tristate.TSTrue || _host.IsEmitBlocked(jsFilePath)))
        {
            _emitResult.EmitSkipped = true;
            return;
        }

        var pop = _tr?.Push(Phase.Emit, "emitJsFileOrBundle", new Dictionary<string, object?> { ["jsFilePath"] = jsFilePath }, true);
        try
        {
            var (emitContext, putEmitContext) = TypeScript.Printer.Printer.GetEmitContext();
            try
            {
                sourceFile = RunScriptTransformers(emitContext, sourceFile);

                var printerOptions = new PrinterOptions
                {
                    RemoveComments = options.RemoveComments.IsTrue(),
                    NewLine = options.NewLine,
                    NoEmitHelpers = options.NoEmitHelpers.IsTrue(),
                    SourceMap = options.SourceMap.IsTrue(),
                    InlineSourceMap = options.InlineSourceMap.IsTrue(),
                    InlineSources = options.InlineSources.IsTrue(),
                    Target = options.Target,
                    // !!!
                };

                // create a printer to print the nodes
                var printer_ = TypeScript.Printer.Printer.NewPrinter(
                    printerOptions,
                    new PrintHandlers
                    {
                        // !!!
                    },
                    emitContext);

                PrintSourceFile(jsFilePath, sourceMapFilePath, sourceFile, printer_, options, Compiler.ShouldEmitSourceMaps(options, sourceFile));
            }
            finally
            {
                putEmitContext();
            }
        }
        finally
        {
            pop?.Invoke();
        }
    }

    internal void EmitDeclarationFile(SourceFile? sourceFile, string declarationFilePath, string declarationMapPath)
    {
        var options = _host.Options();

        if (sourceFile is null || _emitOnly == EmitOnly.EmitOnlyJs || declarationFilePath.Length == 0)
        {
            return;
        }

        var emitDeclarationMap = _emitOnly != EmitOnly.EmitOnlyBuilderSignature && options.DeclarationMap.IsTrue();
        var contentMappedSource = sourceFile;

        var pop = _tr?.Push(Phase.Emit, "emitDeclarationFileOrBundle", new Dictionary<string, object?> { ["declarationFilePath"] = declarationFilePath }, true);
        try
        {
            var (emitContext, putEmitContext) = TypeScript.Printer.Printer.GetEmitContext();
            try
            {
                var (transformed, diags) = RunDeclarationTransformers(emitContext, sourceFile, declarationFilePath, declarationMapPath);
                sourceFile = transformed;

                foreach (var elem in diags)
                {
                    // Add declaration transform diagnostics to emit diagnostics
                    _emitterDiagnostics.Add(elem);
                }

                if (!_forceEmit && _emitOnly != EmitOnly.EmitOnlyBuilderSignature && (options.NoEmit == Tristate.TSTrue || _host.IsEmitBlocked(declarationFilePath)))
                {
                    _emitResult.EmitSkipped = true;
                    return;
                }

                var declBlocked = diags.Length > 0 && !_forceEmit && _emitOnly != EmitOnly.EmitOnlyBuilderSignature;
                if (declBlocked)
                {
                    _emitResult.EmitSkipped = true;
                    return;
                }

                var printerOptions = new PrinterOptions
                {
                    RemoveComments = options.RemoveComments.IsTrue(),
                    NewLine = options.NewLine,
                    NoEmitHelpers = true,
                    // Module: 			   options.Module, // NYI
                    // ModuleResolution:   options.ModuleResolution, // NYI
                    Target = options.GetEmitScriptTarget(),
                    SourceMap = emitDeclarationMap,
                    InlineSourceMap = options.InlineSourceMap.IsTrue(),
                    // InlineSources:       options.InlineSources.IsTrue(), // ignored, per strada
                    // ExtendedDiagnostics: options.ExtendedDiagnostics.IsTrue(), // NYI
                    OnlyPrintJSDocStyle = true,
                    OmitBraceSourceMapPositions = true,
                };

                // create a printer to print the nodes
                var printHandlers = new PrintHandlers();
                var spanMap = contentMappedSource.SpanMap();
                if (emitDeclarationMap && spanMap is not null)
                {
                    var originalSource = Compiler.NewDeclarationMapSource(contentMappedSource);
                    printHandlers.MapSourcePosition = (TypeScript.SourceMap.Source source, int pos, out TypeScript.SourceMap.Source mappedSource, out int mappedPos) =>
                    {
                        if (source.FileName() != contentMappedSource.FileName())
                        {
                            mappedSource = source;
                            mappedPos = pos;
                            return true;
                        }

                        if (!spanMap.TryVirtualToOriginalPositionExact(new TextPos(pos), out var mapped))
                        {
                            mappedSource = null!;
                            mappedPos = 0;
                            return false;
                        }

                        mappedSource = originalSource;
                        mappedPos = (int)mapped;
                        return true;
                    };
                }

                var printer_ = TypeScript.Printer.Printer.NewPrinter(printerOptions, printHandlers, emitContext);

                var declarationMapOptions = new CompilerOptions
                {
                    SourceMap = IfElse(emitDeclarationMap, Tristate.TSTrue, Tristate.TSFalse),
                    SourceRoot = options.SourceRoot,
                    MapRoot = options.MapRoot,
                    // Explicitly do not pass through either inline option.
                };
                PrintSourceFile(declarationFilePath, declarationMapPath, sourceFile, printer_, declarationMapOptions, Compiler.ShouldEmitSourceMaps(declarationMapOptions, sourceFile));
            }
            finally
            {
                putEmitContext();
            }
        }
        finally
        {
            pop?.Invoke();
        }
    }

    internal void PrintSourceFile(string jsFilePath, string sourceMapFilePath, SourceFile sourceFile, Printer printer_, CompilerOptions mapOptions, bool shouldEmitSourceMaps)
    {
        // !!! sourceMapGenerator
        var options = _host.Options();
        TypeScript.SourceMap.Generator? sourceMapGenerator = null;
        if (shouldEmitSourceMaps)
        {
            sourceMapGenerator = NewGenerator(
                GetBaseFileName(NormalizeSlashes(jsFilePath)),
                Compiler.GetSourceRoot(mapOptions),
                GetSourceMapDirectory(mapOptions, jsFilePath, sourceFile),
                new ComparePathsOptions(_host.UseCaseSensitiveFileNames(), _host.GetCurrentDirectory()));
        }

        printer_.Write(sourceFile.AsNode(), sourceFile, _writer, sourceMapGenerator);

        var sourceMapUrlPos = -1;
        if (sourceMapGenerator is not null)
        {
            if (mapOptions.SourceMap.IsTrue() || mapOptions.InlineSourceMap.IsTrue())
            {
                _emitResult.SourceMaps = Concatenate(_emitResult.SourceMaps, [new SourceMapEmitResult
                {
                    InputSourceFileNames = sourceMapGenerator.Sources(),
                    SourceMap = sourceMapGenerator.RawSourceMap(),
                    GeneratedFile = jsFilePath,
                }]);
            }

            var sourceMappingURL = GetSourceMappingURL(
                mapOptions,
                sourceMapGenerator,
                jsFilePath,
                sourceMapFilePath,
                sourceFile);

            if (sourceMappingURL.Length > 0)
            {
                if (!_writer.IsAtStartOfLine())
                {
                    _writer.RawWrite(IfElse(options.NewLine == NewLineKind.CRLF, "\r\n", "\n"));
                }

                sourceMapUrlPos = _writer.GetTextPos();
                _writer.WriteComment("//# sourceMappingURL=");
                _writer.WriteComment(sourceMappingURL);
            }

            // Write the source map
            if (sourceMapFilePath.Length > 0)
            {
                var sourceMap = sourceMapGenerator.String();
                // Go: err := e.writeText(…); if err != nil { … } else { … }（见文件头第 3 条）
                try
                {
                    WriteText(sourceMapFilePath, sourceMap, new WriteFileData { SourceFile = _sourceFile });
                    _emitResult.EmittedFiles = Concatenate(_emitResult.EmittedFiles, [sourceMapFilePath]);
                }
                catch (Exception err)
                {
                    _emitterDiagnostics.Add(NewCompilerDiagnostic(Could_not_write_file_0_Colon_1, jsFilePath, err.Message));
                }
            }
        }
        else
        {
            _writer.WriteLine();
        }

        // Write the output file
        var text = _writer.String();
        if (options.EmitBOM.IsTrue())
        {
            text = AddUTF8ByteOrderMark(text);
        }

        var data = new WriteFileData
        {
            SourceMapUrlPos = sourceMapUrlPos,
            Diagnostics = _emitterDiagnostics.GetDiagnostics(),
            SourceFile = _sourceFile,
        };
        try
        {
            WriteText(jsFilePath, text, data);
            var skippedDtsWrite = data.SkippedDtsWrite;
            if (!skippedDtsWrite)
            {
                _emitResult.EmittedFiles = Concatenate(_emitResult.EmittedFiles, [jsFilePath]);
            }
        }
        catch (Exception err)
        {
            _emitterDiagnostics.Add(NewCompilerDiagnostic(Could_not_write_file_0_Colon_1, jsFilePath, err.Message));
        }

        // Reset state
        _writer.Clear();
    }

    internal void WriteText(string fileName, string text, WriteFileData data)
    {
        if (_writeFile is not null)
        {
            _writeFile(fileName, text, data);
            return;
        }

        _host.WriteFile(fileName, text);
    }

    internal string GetSourceMapDirectory(CompilerOptions mapOptions, string filePath, SourceFile? sourceFile)
    {
        if (mapOptions.SourceRoot.Length > 0)
        {
            return _host.CommonSourceDirectory();
        }

        if (mapOptions.MapRoot.Length > 0)
        {
            var sourceMapDir = NormalizeSlashes(mapOptions.MapRoot);
            if (sourceFile is not null)
            {
                // For modules or multiple emit files the mapRoot will have directory structure like the sources
                // So if src\a.ts and src\lib\b.ts are compiled together user would be moving the maps into mapRoot\a.js.map and mapRoot\lib\b.js.map
                sourceMapDir = GetDirectoryPath(TypeScript.OutputPaths.OutputPaths.GetSourceFilePathInNewDir(
                    sourceFile.FileName(),
                    sourceMapDir,
                    _host.GetCurrentDirectory(),
                    _host.CommonSourceDirectory(),
                    _host.UseCaseSensitiveFileNames()));
            }

            if (GetRootLength(sourceMapDir) == 0)
            {
                // The relative paths are relative to the common directory
                sourceMapDir = CombinePaths(_host.CommonSourceDirectory(), sourceMapDir);
            }

            return sourceMapDir;
        }

        return GetDirectoryPath(NormalizePath(filePath));
    }

    internal string GetSourceMappingURL(CompilerOptions mapOptions, TypeScript.SourceMap.Generator sourceMapGenerator, string filePath, string sourceMapFilePath, SourceFile? sourceFile)
    {
        if (mapOptions.InlineSourceMap.IsTrue())
        {
            // Encode the sourceMap into the sourceMap url
            return sourceMapGenerator.Base64DataURL();
        }

        var sourceMapFile = GetBaseFileName(NormalizeSlashes(sourceMapFilePath));
        if (mapOptions.MapRoot.Length > 0)
        {
            var sourceMapDir = NormalizeSlashes(mapOptions.MapRoot);
            if (sourceFile is not null)
            {
                // For modules or multiple emit files the mapRoot will have directory structure like the sources
                // So if src\a.ts and src\lib\b.ts are compiled together user would be moving the maps into mapRoot\a.js.map and mapRoot\lib\b.js.map
                sourceMapDir = GetDirectoryPath(TypeScript.OutputPaths.OutputPaths.GetSourceFilePathInNewDir(
                    sourceFile.FileName(),
                    sourceMapDir,
                    _host.GetCurrentDirectory(),
                    _host.CommonSourceDirectory(),
                    _host.UseCaseSensitiveFileNames()));
            }

            if (GetRootLength(sourceMapDir) == 0)
            {
                // The relative paths are relative to the common directory
                sourceMapDir = CombinePaths(_host.CommonSourceDirectory(), sourceMapDir);
                return EncodeURI(
                    GetRelativePathToDirectoryOrUrl(
                        GetDirectoryPath(NormalizePath(filePath)), // get the relative sourceMapDir path based on jsFilePath
                        CombinePaths(sourceMapDir, sourceMapFile), // this is where user expects to see sourceMap
                        /*isAbsolutePathAnUrl*/ true,
                        new ComparePathsOptions(_host.UseCaseSensitiveFileNames(), _host.GetCurrentDirectory())));
            }
            else
            {
                return EncodeURI(CombinePaths(sourceMapDir, sourceMapFile));
            }
        }

        return EncodeURI(sourceMapFile);
    }
}

/// <summary>Go: <c>type declarationTransformer interface</c>（未导出接口 → `I` 前缀）。</summary>
internal interface IDeclarationTransformer
{
    SourceFile TransformSourceFile(SourceFile sourceFile);

    Diagnostic[] GetDiagnostics();
}

/// <summary>
/// 见文件头第 2 条：C# 无结构性接口满足，`DeclarationTransformer`/`SupplementalReferencesTransformer` 又不在本片
/// 所有权内，故用一个**只转发**的适配器把它们统一成 <see cref="IDeclarationTransformer"/>（方法组委托捕获，
/// 不是拷贝语义）。两个类型以后显式声明该接口后，本类型即可删除。
/// </summary>
internal sealed class DeclarationTransformerAdapter(Func<SourceFile, SourceFile> transformSourceFile, Func<Diagnostic[]> getDiagnostics) : IDeclarationTransformer
{
    public SourceFile TransformSourceFile(SourceFile sourceFile) => transformSourceFile(sourceFile);

    public Diagnostic[] GetDiagnostics() => getDiagnostics();
}

/// <summary>Go: <c>type declarationMapSource struct</c>（把未变换的原文当作 source map 的 source）。</summary>
internal sealed class DeclarationMapSource : TypeScript.SourceMap.Source
{
    internal string _fileName = "";
    internal string _text = "";
    internal TextPos[] _lineMap = [];

    public string FileName() => _fileName;

    public string Text() => _text;

    public TextPos[] ECMALineMap() => _lineMap;
}

/// <summary>Go: <c>type SourceFileMayBeEmittedHost interface</c>。</summary>
public interface SourceFileMayBeEmittedHost
{
    CompilerOptions Options();

    TypeScript.TsOptions.SourceOutputAndProjectReference? GetProjectReferenceFromSource(TypeScript.TSPath.Path path);

    bool IsSourceFileFromExternalLibrary(SourceFile file);

    string GetCurrentDirectory();

    bool UseCaseSensitiveFileNames();

    SourceFile[] SourceFiles();
}

/// <summary>Go 包级声明（package compiler）→ 与包同名的静态类（docs/architecture.md §2）。</summary>
public static partial class Compiler
{
    internal static Transformer GetModuleTransformer(TransformOptions opts)
    {
        switch (opts.CompilerOptions!.GetEmitModuleKind())
        {
            case ModuleKind.Preserve:
                // `ESModuleTransformer` contains logic for preserving CJS input syntax in `--module preserve`
                return TypeScript.Transformers.ModuleTransforms.ModuleTransforms.NewESModuleTransformer(opts)!;

            case ModuleKind.ESNext:
            case ModuleKind.ES2022:
            case ModuleKind.ES2020:
            case ModuleKind.ES2015:
            case ModuleKind.Node20:
            case ModuleKind.Node18:
            case ModuleKind.Node16:
            case ModuleKind.NodeNext:
            case ModuleKind.CommonJS:
                return TypeScript.Transformers.ModuleTransforms.ModuleTransforms.NewImpliedModuleTransformer(opts)!;

            default:
                return TypeScript.Transformers.ModuleTransforms.ModuleTransforms.NewCommonJSModuleTransformer(opts)!;
        }
    }

    internal static Transformer[] GetScriptTransformers(EmitContext emitContext, EmitHost host, SourceFile sourceFile)
    {
        var tx = new List<Transformer>();
        var options = host.Options();

        // JS files don't use reference calculations as they don't do import elision, no need to calculate it
        var importElisionEnabled = !options.VerbatimModuleSyntax.IsTrue() && !IsInJSFile(sourceFile.AsNode());
        var jsxTransformEnabled = options.GetJSXTransformEnabled() && sourceFile.LanguageVariant == LanguageVariant.JSX;

        // `EmitHost` 从 `Printer.EmitHost` 与 `declarations.DeclarationEmitHost` 两边继承到同签名的
        // `GetEmitResolver()`（Go 的接口嵌入会把同签名去重，C# 不会）→ 显式选一个基接口消歧。
        var emitResolver = ((TypeScript.Printer.EmitHost)host).GetEmitResolver();

        ReferenceResolver referenceResolver;
        if (importElisionEnabled || jsxTransformEnabled || !options.GetIsolatedModules() || options.EmitDecoratorMetadata.IsTrue())
        {
            referenceResolver = emitResolver;
        }
        else
        {
            referenceResolver = TypeScript.Binder.Binder.NewReferenceResolver(options, new ReferenceResolverHooks());
        }

        var opts = new TransformOptions
        {
            Context = emitContext,
            CompilerOptions = options,
            Resolver = referenceResolver,
            EmitResolver = emitResolver,
            GetEmitModuleFormatOfFile = host.GetEmitModuleFormatOfFile,
        };

        // transform TypeScript syntax
        {
            // use type nodes to add metadata decorators
            if (options.EmitDecoratorMetadata.IsTrue())
            {
                tx.Add(TypeScript.Transformers.TsTransforms.MetadataTransformer.NewMetadataTransformer(opts)!);
            }

            // erase types
            tx.Add(TypeScript.Transformers.TsTransforms.TypeEraserTransformer.NewTypeEraserTransformer(opts)!);

            // elide imports
            if (importElisionEnabled)
            {
                tx.Add(TypeScript.Transformers.TsTransforms.ImportElisionTransformer.NewImportElisionTransformer(opts)!);
            }

            // transform `enum`, `namespace`, and parameter properties
            tx.Add(TypeScript.Transformers.TsTransforms.RuntimeSyntaxTransformer.NewRuntimeSyntaxTransformer(opts)!);

            if (options.ExperimentalDecorators.IsTrue())
            {
                tx.Add(TypeScript.Transformers.TsTransforms.LegacyDecoratorsTransformer.NewLegacyDecoratorsTransformer(opts)!);
            }
        }

        if (jsxTransformEnabled)
        {
            tx.Add(TypeScript.Transformers.JsxTransforms.JsxTransforms.NewJSXTransformer(opts));
        }

        var downleveler = TypeScript.Transformers.EsTransforms.EsTransforms.GetESTransformer(opts);
        if (downleveler is not null)
        {
            tx.Add(downleveler);
        }

        tx.Add(TypeScript.Transformers.EsTransforms.EsTransforms.NewUseStrictTransformer(opts)!);

        // transform module syntax
        tx.Add(GetModuleTransformer(opts));

        // inlining (formerly done via substitutions)
        if (!options.GetIsolatedModules())
        {
            tx.Add(TypeScript.Transformers.Inliners.Inliners.NewConstEnumInliningTransformer(opts));
        }

        return [.. tx];
    }

    internal static bool ShouldEmitSourceMaps(CompilerOptions mapOptions, SourceFile sourceFile)
    {
        return (mapOptions.SourceMap.IsTrue() || mapOptions.InlineSourceMap.IsTrue()) &&
            !FileExtensionIs(sourceFile.FileName(), ExtensionJson);
    }

    internal static string GetSourceRoot(CompilerOptions mapOptions)
    {
        // Normalize source root and make sure it has trailing "/" so that it can be used to combine paths with the
        // relative paths of the sources list in the sourcemap
        var sourceRoot = NormalizeSlashes(mapOptions.SourceRoot);
        if (sourceRoot.Length > 0)
        {
            sourceRoot = EnsureTrailingDirectorySeparator(sourceRoot);
        }

        return sourceRoot;
    }

    internal static DeclarationMapSource NewDeclarationMapSource(SourceFile sourceFile)
    {
        var text = sourceFile.OriginalText();
        return new DeclarationMapSource
        {
            _fileName = sourceFile.OriginalFileName(),
            _text = text,
            // Go: lineMap: []core.TextPos(core.ComputeECMALineStarts(text))（ECMALineStarts → TextPos[] 有隐式转换）
            _lineMap = ComputeECMALineStarts(text),
        };
    }

    internal static bool SourceFileMayBeEmitted(SourceFile sourceFile, SourceFileMayBeEmittedHost host, bool forceDtsEmit, bool forceJsEmit)
    {
        // TODO: move this to outputpaths?
        var options = host.Options();
        // Js files are emitted only if option is enabled
        if (!forceJsEmit && options.NoEmitForJsFiles.IsTrue() && IsSourceFileJS(sourceFile))
        {
            return false;
        }

        // Declaration files are not emitted
        if (sourceFile.IsDeclarationFile)
        {
            return false;
        }

        // Runtime output for content-mapped files is owned by the external content mapper or build tool. Only
        // include them in the emit set when their transformed TypeScript can produce declarations.
        if (sourceFile.ContentMapper() != "" && !forceDtsEmit && !options.GetEmitDeclarations())
        {
            return false;
        }

        // Source file from node_modules are not emitted
        if (host.IsSourceFileFromExternalLibrary(sourceFile))
        {
            return false;
        }

        // forcing dts emit => file needs to be emitted
        if (forceDtsEmit || forceJsEmit)
        {
            return true;
        }

        // Check other conditions for file emit
        // Source files from referenced projects are not emitted
        if (host.GetProjectReferenceFromSource(sourceFile.Path()) is not null)
        {
            return false;
        }

        // Any non json file should be emitted
        if (!IsJsonSourceFile(sourceFile))
        {
            return true;
        }

        // Json file is not emitted if outDir is not specified
        if (options.OutDir == "")
        {
            return false;
        }

        // Otherwise, if rootDir is specified or a config file exists, we know the common source directory and can check if the file would be emitted in the same location
        if (options.RootDir != "" || options.ConfigFilePath != "")
        {
            var commonDir = GetNormalizedAbsolutePath(
                TypeScript.OutputPaths.OutputPaths.GetCommonSourceDirectory(
                    options,
                    static () => [],
                    host.GetCurrentDirectory(),
                    host.UseCaseSensitiveFileNames(),
                    null),
                host.GetCurrentDirectory());
            var outputPath = TypeScript.OutputPaths.OutputPaths.GetSourceFilePathInNewDirWorker(
                sourceFile.FileName(),
                options.OutDir,
                host.GetCurrentDirectory(),
                commonDir,
                host.UseCaseSensitiveFileNames());
            if (ComparePaths(sourceFile.FileName(), outputPath, new ComparePathsOptions(host.UseCaseSensitiveFileNames(), host.GetCurrentDirectory())) == 0)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Go: <c>func getSourceFilesToEmit(host SourceFileMayBeEmittedHost, targetSourceFiles []*ast.SourceFile, forceDtsEmit bool, forceJsEmit bool) []*ast.SourceFile</c>。</summary>
    internal static SourceFile[] GetSourceFilesToEmit(SourceFileMayBeEmittedHost host, SourceFile[]? targetSourceFiles, bool forceDtsEmit, bool forceJsEmit)
    {
        if (targetSourceFiles is null)
        {
            targetSourceFiles = host.SourceFiles();
        }

        return Filter(targetSourceFiles, sourceFile => SourceFileMayBeEmitted(sourceFile, host, forceDtsEmit, forceJsEmit));
    }

    internal static bool IsSourceFileNotJson(SourceFile file)
    {
        return !IsJsonSourceFile(file);
    }

    internal static Diagnostic[] GetDeclarationDiagnostics(EmitHost host, SourceFile file)
    {
        // TODO: use p.getSourceFilesToEmit cache
        var fullFiles = Filter(GetSourceFilesToEmit(host, SingleElementSlice(file), false, false), IsSourceFileNotJson);
        if (!Some(fullFiles, f => f == file))
        {
            return [];
        }

        var options = host.Options();
        // Go: NewDeclarationTransformer(host, nil, options, "", "")。C# 的参数被标成非空 `EmitContext`，
        // 但 `Transformer.NewTransformer` 内部是 `emitContext ??= NewEmitContext()`，与 Go 的 nil 分支一致 → `null!`。
        var transform = TypeScript.Transformers.Declarations.Declarations.NewDeclarationTransformer(host, null!, options, "", "");
        transform.TransformSourceFile(file);
        return transform.GetDiagnostics();
    }
}
