// ported from: internal/tsoptions/parsedcommandline.go
//
// 映射约定（docs/architecture.md §2/§3）：
// 1) Go 的 `iter.Seq2[string,string]`/`iter.Seq[string]` → C# 的迭代器方法（`IEnumerable<...>` + `yield`）；
//    Go 的 `slices.Concat` → C# 集合表达式展开（`[.. a, .. b]`）。
// 2) `sync.Once` + 缓存字段 → `Lazy<T>` 实例字段（缓存字段本身在 Go 里还有别的读取点——WithFileNames/ReloadFileNames
//    会连缓存一起复制——所以那两处仍保留 `_wildcardDirectories`/`_includeGlobs` 字段）。
// 3) Go 的 nil 接收者检查（`if p == nil { return ... }`）在 C# 不可达 → 删除（§3.2）；`if p.ParsedConfig == nil` 同理
//    （C# 的 `ParsedConfig` 非空）。
// 4) 接口实现：Go 的编译期断言 `_ module.ResolvedProjectReference = (*ParsedCommandLine)(nil)` /
//    `_ outputpaths.OutputPathsHost = (*ParsedCommandLine)(nil)` → C# 直接把这两个接口列进基类型列表。
//    `ContentMapperExtensions()` 既是本类型的公开方法，也是 `OutputPathsHost` 的成员（签名一致，隐式实现）。
// 5) `(T, bool)` 形态的 `GetMatchedIncludeSpec` → `TryGetMatchedIncludeSpec(..., out string)`（§3.2）。
// 6) `strings.EqualFold` → `EquateStringCaseInsensitive`（§3.4）；`slices.ContainsFunc` → `Core.Some`。
// 7) `GetContentMapperForFileName`/`ContentMapperExtensions`/`ContentMappers` 里 Go 的 `mapper.Definition.Extensions`
//    被内嵌提升（`mapper.Extensions`）；C# 的 `Mapper` 没有字段提升，一律写 `mapper.Definition.Extensions`
//    （与 ContentMapper 分片的落法一致）。
using System;
using System.Collections.Generic;
using TypeScript.Ast;
using TypeScript.ContentMapper;
using Diagnostic = TypeScript.Ast.Diagnostic; // Ast 与 ContentMapper 各有一个 Diagnostic（Go 里也是两个包）→ 别名消歧
using TypeScript.Core;
using TypeScript.Glob;
using TypeScript.Module;
using TypeScript.OutputPaths;
using TypeScript.TSPath;
using TypeScript.Vfs;
using static TypeScript.Ast.Ast;
using static TypeScript.Diagnostics.Diagnostics;
using static TypeScript.Vfs.VfsMatch.VfsMatch;
using static TypeScript.Core.Core;
using static TypeScript.OutputPaths.OutputPaths;
using static TypeScript.StringUtil.StringUtil;
using static TypeScript.TSPath.TSPath;
using Path = TypeScript.TSPath.Path;

namespace TypeScript.TsOptions;

public sealed class SourceOutputAndProjectReference
{
    public string Source = "";
    public string OutputDts = "";
    public ParsedCommandLine? Resolved;
}

public sealed class ParsedCommandLine : ResolvedProjectReference, OutputPathsHost
{
    private const string _fileGlobPattern = "*.{js,jsx,mjs,cjs,ts,tsx,mts,cts,json}";
    private const string _recursiveFileGlobPattern = "**/*.{js,jsx,mjs,cjs,ts,tsx,mts,cts,json}";

    public ParsedOptions ParsedConfig = new();

    public TsOptions.TsConfigSourceFile? ConfigFile; // TsConfigSourceFile, used in Program and ExecuteCommandLine
    public Diagnostic[] Errors = [];
    public object? Raw;
    public bool? CompileOnSave;

    internal ComparePathsOptions _comparePathsOptions;
    internal Dictionary<string, bool>? _wildcardDirectories;
    private Lazy<Dictionary<string, bool>?>? _wildcardDirectoriesOnce;
    internal TypeScript.Glob.Glob[]? _includeGlobs;
    private Lazy<TypeScript.Glob.Glob[]>? _includeGlobsOnce;

    private Lazy<bool>? _sourceAndOutputMapsOnce;
    internal Dictionary<Path, SourceOutputAndProjectReference>? _sourceToProjectReference;
    internal Dictionary<Path, SourceOutputAndProjectReference>? _outputDtsToProjectReference;

    internal string _commonSourceDirectory = "";
    private Lazy<string>? _commonSourceDirectoryOnce;

    internal string[]? _resolvedProjectReferencePaths;
    private Lazy<string[]>? _resolvedProjectReferencePathsOnce;

    internal int _literalFileNamesLen;
    internal Dictionary<Path, string>? _fileNamesByPath; // maps file names to their paths, used for quick lookups
    private Lazy<Dictionary<Path, string>>? _fileNamesByPathOnce;

    internal TypeScript.Locale.Locale _locale;
    private Lazy<TypeScript.Locale.Locale>? _localeOnce;

    // fileGlobPatterns returns the include file glob patterns for this command line, augmenting the
    // built-in patterns with the extensions registered by its content mappers so that created
    // content-mapped files are recognized as possible root files.
    internal (string FileGlob, string RecursiveFileGlob) FileGlobPatterns()
    {
        var mapperExtensions = ContentMapperExtensions();
        if (mapperExtensions.Length == 0)
        {
            return (_fileGlobPattern, _recursiveFileGlobPattern);
        }
        var extensions = new List<string>(9 + mapperExtensions.Length)
        {
            "js", "jsx", "mjs", "cjs", "ts", "tsx", "mts", "cts", "json",
        };
        foreach (var extension in mapperExtensions)
        {
            // Go: strings.TrimPrefix(extension, ".")
            extensions.Add(extension.StartsWith('.') ? extension[1..] : extension);
        }
        var fileGlob = "*.{" + string.Join(",", extensions) + "}";
        return (fileGlob, "**/" + fileGlob);
    }

    public static ParsedCommandLine NewParsedCommandLine(
        CompilerOptions compilerOptions,
        string[] rootFileNames,
        ProjectReference[]? projectReferences,
        ComparePathsOptions comparePathsOptions)
    {
        return new ParsedCommandLine
        {
            ParsedConfig = new ParsedOptions
            {
                CompilerOptions = compilerOptions,
                FileNames = rootFileNames,
                ProjectReferences = projectReferences,
            },
            _comparePathsOptions = comparePathsOptions,
        };
    }

    public ParsedCommandLine WithFileNames(string[] fileNames)
    {
        var parsedConfig = ParsedConfig.Clone();
        parsedConfig.FileNames = fileNames;
        return new ParsedCommandLine
        {
            ParsedConfig = parsedConfig,
            ConfigFile = ConfigFile,
            Errors = Errors,
            Raw = Raw,
            CompileOnSave = CompileOnSave,
            _comparePathsOptions = _comparePathsOptions,
            _wildcardDirectories = _wildcardDirectories,
            _includeGlobs = _includeGlobs,
            _literalFileNamesLen = _literalFileNamesLen,
        };
    }

    public string ConfigName()
    {
        if (ConfigFile is null)
        {
            return "";
        }
        return ConfigFile.SourceFile.FileName();
    }

    public Dictionary<Path, SourceOutputAndProjectReference>? SourceToProjectReference() => _sourceToProjectReference;

    public Dictionary<Path, SourceOutputAndProjectReference>? OutputDtsToProjectReference() => _outputDtsToProjectReference;

    public void ParseInputOutputNames()
    {
        _sourceAndOutputMapsOnce ??= new Lazy<bool>(() =>
        {
            var sourceToOutput = new Dictionary<Path, SourceOutputAndProjectReference>();
            var outputDtsToSource = new Dictionary<Path, SourceOutputAndProjectReference>();

            foreach (var (outputDts, source) in GetOutputDeclarationAndSourceFileNames())
            {
                var path = ToPath(source, GetCurrentDirectory(), UseCaseSensitiveFileNames());
                var projectReference = new SourceOutputAndProjectReference
                {
                    Source = source,
                    OutputDts = outputDts,
                    Resolved = this,
                };
                if (outputDts != "")
                {
                    outputDtsToSource[ToPath(outputDts, GetCurrentDirectory(), UseCaseSensitiveFileNames())] = projectReference;
                }
                sourceToOutput[path] = projectReference;
            }
            _outputDtsToProjectReference = outputDtsToSource;
            _sourceToProjectReference = sourceToOutput;
            return true;
        });
        _ = _sourceAndOutputMapsOnce.Value;
    }

    public string CommonSourceDirectory()
    {
        _commonSourceDirectoryOnce ??= new Lazy<string>(() =>
        {
            string[] Files()
            {
                return Filter(ParsedConfig.FileNames, file =>
                    !(ParsedConfig.CompilerOptions!.NoEmitForJsFiles.IsTrue() && HasJSFileExtension(file)) && !IsDeclarationFileName(file));
            }

            _commonSourceDirectory = GetCommonSourceDirectory(
                ParsedConfig.CompilerOptions!,
                Files,
                GetCurrentDirectory(),
                UseCaseSensitiveFileNames(),
                CheckSourceFilesBelongToPath);
            return _commonSourceDirectory;
        });
        return _commonSourceDirectoryOnce.Value;
    }

    internal bool CheckSourceFilesBelongToPath(string[] sourceFiles, string rootDirectory)
    {
        var allFilesBelongToPath = true;
        foreach (var file in sourceFiles)
        {
            var absoluteSourceFilePath = GetCanonicalFileName(GetNormalizedAbsolutePath(file, GetCurrentDirectory()), UseCaseSensitiveFileNames());
            if (!ContainsPath(rootDirectory, file, _comparePathsOptions))
            {
                Errors = [.. Errors, NewCompilerDiagnostic(File_0_is_not_under_rootDir_1_rootDir_is_expected_to_contain_all_source_files, absoluteSourceFilePath, rootDirectory)];
                allFilesBelongToPath = false;
            }
        }

        return allFilesBelongToPath;
    }

    public string GetCurrentDirectory() => _comparePathsOptions.CurrentDirectory;

    public bool UseCaseSensitiveFileNames() => _comparePathsOptions.UseCaseSensitiveFileNames;

    internal IEnumerable<(string OutputDts, string InputName)> GetOutputDeclarationAndSourceFileNames()
    {
        foreach (var fileName in ParsedConfig.FileNames)
        {
            var outputDts = "";
            if (!IsDeclarationFileName(fileName) && !FileExtensionIs(fileName, ExtensionJson))
            {
                outputDts = GetOutputDeclarationFileNameWorker(fileName, CompilerOptions()!, this);
            }
            yield return (outputDts, fileName);
        }
    }

    public IEnumerable<string> GetOutputFileNames()
    {
        foreach (var fileName in ParsedConfig.FileNames)
        {
            if (IsDeclarationFileName(fileName))
            {
                continue;
            }
            var jsFileName = GetOutputJSFileName(fileName, CompilerOptions()!, this);
            var isJson = FileExtensionIs(fileName, ExtensionJson);
            if (jsFileName != "")
            {
                yield return jsFileName;
                if (!isJson)
                {
                    var sourceMap = GetSourceMapFilePath(jsFileName, CompilerOptions()!);
                    if (sourceMap != "")
                    {
                        yield return sourceMap;
                    }
                }
            }
            if (isJson)
            {
                continue;
            }
            if (CompilerOptions()!.GetEmitDeclarations())
            {
                var dtsFileName = GetOutputDeclarationFileNameWorker(fileName, CompilerOptions()!, this);
                if (dtsFileName != "")
                {
                    yield return dtsFileName;
                    if (GetContentMapperForFileName(fileName) is null && CompilerOptions()!.GetAreDeclarationMapsEnabled())
                    {
                        yield return dtsFileName + ".map";
                    }
                }
            }
        }
    }

    public string GetBuildInfoFileName()
    {
        return TypeScript.OutputPaths.OutputPaths.GetBuildInfoFileName(CompilerOptions()!, _comparePathsOptions);
    }

    // WildcardDirectories returns the cached wildcard directories, initializing them if needed
    public Dictionary<string, bool>? WildcardDirectories()
    {
        _wildcardDirectoriesOnce ??= new Lazy<Dictionary<string, bool>?>(() =>
        {
            if (_wildcardDirectories is null)
            {
                // Go: p.ConfigFile.configFileSpecs（nil 会在 Go 侧 panic，保留同样的前提）
                _wildcardDirectories = TsOptions.GetWildcardDirectories(
                    ConfigFile!._configFileSpecs!.ValidatedIncludeSpecs,
                    ConfigFile!._configFileSpecs!.ValidatedExcludeSpecs,
                    _comparePathsOptions);
            }
            return _wildcardDirectories;
        });

        return _wildcardDirectoriesOnce.Value;
    }

    public TypeScript.Glob.Glob[]? WildcardDirectoryGlobs()
    {
        var wildcardDirectories = WildcardDirectories();
        if (wildcardDirectories is null)
        {
            return null;
        }

        _includeGlobsOnce ??= new Lazy<TypeScript.Glob.Glob[]>(() =>
        {
            if (_includeGlobs is null)
            {
                var (fileGlob, recursiveFileGlob) = FileGlobPatterns();
                var globs = new List<TypeScript.Glob.Glob>(wildcardDirectories.Count);
                foreach (var (dir, recursive) in wildcardDirectories)
                {
                    if (TypeScript.Glob.Glob.TryParse($"{NormalizePath(dir)}/{IfElse(recursive, recursiveFileGlob, fileGlob)}", out var parsed))
                    {
                        globs.Add(parsed!);
                    }
                }
                _includeGlobs = [.. globs];
            }
            return _includeGlobs;
        });

        return _includeGlobsOnce.Value;
    }

    // Normalized file names explicitly specified in `files`
    public string[]? LiteralFileNames()
    {
        if (ConfigFile is not null)
        {
            return FileNames()[.._literalFileNamesLen];
        }
        return null;
    }

    public void SetParsedOptions(ParsedOptions o) => ParsedConfig = o;

    public void SetCompilerOptions(CompilerOptions o) => ParsedConfig.CompilerOptions = o;

    public CompilerOptions? CompilerOptions() => ParsedConfig.CompilerOptions;

    public void SetTypeAcquisition(TypeAcquisition o) => ParsedConfig.TypeAcquisition = o;

    public TypeAcquisition? TypeAcquisition() => ParsedConfig.TypeAcquisition;

    // All file names matched by files, include, and exclude patterns
    public string[] FileNames() => ParsedConfig.FileNames;

    public Dictionary<Path, string> FileNamesByPath()
    {
        _fileNamesByPathOnce ??= new Lazy<Dictionary<Path, string>>(() =>
        {
            _fileNamesByPath = new Dictionary<Path, string>(ParsedConfig.FileNames.Length);
            foreach (var fileName in ParsedConfig.FileNames)
            {
                var path = ToPath(fileName, GetCurrentDirectory(), UseCaseSensitiveFileNames());
                _fileNamesByPath[path] = fileName;
            }
            return _fileNamesByPath;
        });
        return _fileNamesByPathOnce.Value;
    }

    public ProjectReference[]? ProjectReferences() => ParsedConfig.ProjectReferences;

    public Mapper[]? ContentMappers() => ParsedConfig.ContentMappers;

    // ContentMapperExtensions returns the flattened list of file extensions registered by the
    // config's content mappers.
    public string[] ContentMapperExtensions()
    {
        return FlatMap(ContentMappers(), m => m.Extensions);
    }

    // GetContentMapperForFileName returns the configured content mapper whose extensions include fileName,
    // or nil if no content mapper is registered for the file's extension.
    public Mapper? GetContentMapperForFileName(string fileName)
    {
        var ignoreCase = !UseCaseSensitiveFileNames();
        var extension = GetLongestExtensionFromPath(fileName, ContentMapperExtensions(), ignoreCase);
        foreach (var mapper in ContentMappers() ?? [])
        {
            if (Some(mapper.Extensions, mapperExtension =>
                extension == mapperExtension || ignoreCase && EquateStringCaseInsensitive(extension, mapperExtension)))
            {
                return mapper;
            }
        }
        return null;
    }

    public string[] ResolvedProjectReferencePaths()
    {
        _resolvedProjectReferencePathsOnce ??= new Lazy<string[]>(() =>
        {
            _resolvedProjectReferencePaths = Map(ParsedConfig.ProjectReferences, ResolveProjectReferencePath);
            return _resolvedProjectReferencePaths;
        });
        return _resolvedProjectReferencePathsOnce.Value;
    }

    public string[]? ExtendedSourceFiles()
    {
        if (ConfigFile is null)
        {
            return null;
        }
        return ConfigFile.ExtendedSourceFiles;
    }

    public Diagnostic[] GetConfigFileParsingDiagnostics()
    {
        if (ConfigFile is not null)
        {
            // todo: !!! should be ConfigFile.ParseDiagnostics, check if they are the same
            return [.. ConfigFile.SourceFile.Diagnostics(), .. Errors];
        }
        return Errors;
    }

    // PossiblyMatchesFileName is a fast check to see if a file is currently included by a config
    // or would be included if the file were to be created. It may return false positives.
    public bool PossiblyMatchesFileName(string fileName)
    {
        var path = ToPath(fileName, GetCurrentDirectory(), UseCaseSensitiveFileNames());
        if (FileNamesByPath().ContainsKey(path))
        {
            return true;
        }

        foreach (var include in ConfigFile!._configFileSpecs!.ValidatedIncludeSpecs)
        {
            if (!include.Contains('*') && !include.Contains('?') && !IsImplicitGlob(include))
            {
                var includePath = ToPath(include, GetCurrentDirectory(), UseCaseSensitiveFileNames());
                if (includePath == path)
                {
                    return true;
                }
            }
        }
        if (GetContentMapperForFileName(fileName) is not null)
        {
            var directoryPath = path.GetDirectoryPath();
            if (PossiblyMatchesDirectoryName(directoryPath))
            {
                return true;
            }
        }
        var wildcardDirectoryGlobs = WildcardDirectoryGlobs();
        if (wildcardDirectoryGlobs is not null && wildcardDirectoryGlobs.Length > 0)
        {
            foreach (var glob in wildcardDirectoryGlobs)
            {
                if (glob.Match(fileName))
                {
                    return true;
                }
            }
        }
        return false;
    }

    public bool PossiblyMatchesDirectoryName(Path directoryPath)
    {
        var wildcardDirectories = WildcardDirectories();
        if (wildcardDirectories is null)
        {
            return false;
        }
        foreach (var (wildcardDir, recursive) in wildcardDirectories)
        {
            var wildcardDirPath = ToPath(wildcardDir, GetCurrentDirectory(), UseCaseSensitiveFileNames());
            if (recursive)
            {
                if (wildcardDirPath.ContainsPath(directoryPath))
                {
                    return true;
                }
            }
            else
            {
                if (wildcardDirPath == directoryPath)
                {
                    return true;
                }
            }
        }
        return false;
    }

    public string GetMatchedFileSpec(string fileName)
    {
        return ConfigFile!._configFileSpecs!.GetMatchedFileSpec(fileName, _comparePathsOptions);
    }

    public bool TryGetMatchedIncludeSpec(string fileName, out string matchedSpec)
    {
        var specs = ConfigFile!._configFileSpecs!;
        if (specs.ValidatedIncludeSpecs.Length == 0)
        {
            matchedSpec = "";
            return false;
        }

        if (specs.IsDefaultIncludeSpec)
        {
            matchedSpec = specs.ValidatedIncludeSpecs[0];
            return true;
        }

        matchedSpec = specs.GetMatchedIncludeSpec(fileName, _comparePathsOptions);
        return false;
    }

    public ParsedCommandLine ReloadFileNamesOfParsedCommandLine(FS fs)
    {
        var parsedConfig = ParsedConfig.Clone();
        var (fileNames, literalFileNamesLen) = TsOptions.GetFileNamesFromConfigSpecs(
            ConfigFile!._configFileSpecs!,
            GetCurrentDirectory(),
            CompilerOptions(),
            fs,
            ContentMapperExtensions());
        parsedConfig.FileNames = fileNames;
        return new ParsedCommandLine
        {
            ParsedConfig = parsedConfig,
            ConfigFile = ConfigFile,
            Errors = Errors,
            Raw = Raw,
            CompileOnSave = CompileOnSave,
            _comparePathsOptions = _comparePathsOptions,
            _wildcardDirectories = _wildcardDirectories,
            _includeGlobs = _includeGlobs,
            _literalFileNamesLen = literalFileNamesLen,
        };
    }

    public TypeScript.Locale.Locale Locale
    {
        get
        {
            _localeOnce ??= new Lazy<TypeScript.Locale.Locale>(() =>
            {
                // Go: p.locale, _ = locale.Parse(p.CompilerOptions().Locale)（CompilerOptions 为 nil 时 Go 会 panic）
                TypeScript.Locale.Locale.TryParse(CompilerOptions()!.Locale, out var locale);
                _locale = locale;
                return _locale;
            });
            return _localeOnce.Value;
        }
    }
}
