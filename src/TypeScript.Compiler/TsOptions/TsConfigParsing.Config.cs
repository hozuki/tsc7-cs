// ported from: internal/tsoptions/tsconfigparsing.go:781-1010（默认值、convertToObject、readJsonConfigFile、
// getExtendedConfig / ParseExtendedConfig / parseConfig（extends 合并））
//
// 与 Go 的差异：
// - Go 的 `extendedConfigPath any` 实际只装 `[]string`（两条产出路径都返回 []string）与理论上的 string
//   （Go 里用 `reflect.TypeOf(...).Kind() == reflect.String` 判断）；C# 用模式匹配，两个分支都保留。
// - Go 的 nil slice 与空 slice 在 `ownConfig.extendedConfigPath != nil` 处语义不同，所以
//   `GetExtendsConfigPathOrArray` 在无结果时返回 null（见 TsConfigParsing.Json.cs）。
// - `sync.Once`-free：Go 的这些函数都是纯函数。
using TypeScript.Ast;
using TypeScript.Collections;
using TypeScript.ContentMapper;
using TypeScript.Core;
using TypeScript.Diagnostics;
using TypeScript.TSPath;
using TypeScript.Vfs;
using static TypeScript.Ast.Ast;
using static TypeScript.Collections.Collections;
using static TypeScript.Core.Core;
using static TypeScript.Diagnostics.Diagnostics;
using static TypeScript.TSPath.TSPath;
using Diagnostic = TypeScript.Ast.Diagnostic;
using TPath = TypeScript.TSPath.Path;

namespace TypeScript.TsOptions;

public static partial class TsOptions
{
    // Go: func convertToObject(sourceFile *ast.SourceFile) (any, []*ast.Diagnostic)
    internal static (object? Json, Diagnostic[] Errors) ConvertToObject(SourceFile sourceFile)
    {
        Node? rootExpression = null;
        if (sourceFile.Statements is { Nodes.Length: not 0 })
        {
            rootExpression = sourceFile.Statements.Nodes[0].Expression();
        }

        return ConvertToJson(sourceFile, rootExpression, true /*returnValue*/, null /*jsonConversionNotifier*/);
    }

    // Go: func getDefaultCompilerOptions(configFileName string) *core.CompilerOptions
    internal static CompilerOptions GetDefaultCompilerOptions(string configFileName)
    {
        var options = new CompilerOptions();
        if (configFileName != "" && GetBaseFileName(configFileName) == "jsconfig.json")
        {
            options = new CompilerOptions
            {
                AllowJs = Tristate.TSTrue,
                MaxNodeModuleJsDepth = 2,
                SkipLibCheck = Tristate.TSTrue,
                NoEmit = Tristate.TSTrue,
            };
        }

        return options;
    }

    // Go: func getDefaultTypeAcquisition(configFileName string) *core.TypeAcquisition
    internal static TypeAcquisition GetDefaultTypeAcquisition(string configFileName)
    {
        var options = new TypeAcquisition();
        if (configFileName != "" && GetBaseFileName(configFileName) == "jsconfig.json")
        {
            options.Enable = Tristate.TSTrue;
        }

        return options;
    }

    // Go: func convertCompilerOptionsFromJsonWorker(jsonOptions any, basePath string, configFileName string) (*core.CompilerOptions, []*ast.Diagnostic)
    internal static (CompilerOptions Options, Diagnostic[] Errors) ConvertCompilerOptionsFromJsonWorker(
        object? jsonOptions,
        string basePath,
        string configFileName)
    {
        var options = GetDefaultCompilerOptions(configFileName);
        var (_, errors) = ConvertOptionsFromJson(CommandLineCompilerOptionsMap, jsonOptions, basePath, new CompilerOptionsParser(options));
        if (configFileName != "")
        {
            options.ConfigFilePath = NormalizeSlashes(configFileName);
        }

        return (options, errors);
    }

    // Go: func convertTypeAcquisitionFromJsonWorker(jsonOptions any, basePath string, configFileName string) (*core.TypeAcquisition, []*ast.Diagnostic)
    internal static (TypeAcquisition Options, Diagnostic[] Errors) ConvertTypeAcquisitionFromJsonWorker(
        object? jsonOptions,
        string basePath,
        string configFileName)
    {
        var options = GetDefaultTypeAcquisition(configFileName);
        var (_, errors) = ConvertOptionsFromJson(TypeAcquisitionDeclaration.ElementOptions!, jsonOptions, basePath, new TypeAcquisitionParser(options));
        return (options, errors);
    }

    // Go: func parseOwnConfigOfJson(...)
    private static (ParsedTsconfig Config, Diagnostic[] Errors) ParseOwnConfigOfJson(
        OrderedMap<string, object?> json,
        ParseConfigHost host,
        string basePath,
        string configFileName)
    {
        var errors = new List<Diagnostic>();
        if (json.Has("excludes"))
        {
            errors.Add(NewCompilerDiagnostic(Unknown_option_excludes_Did_you_mean_exclude));
        }

        var (options, err) = ConvertCompilerOptionsFromJsonWorker(json.GetOrZero("compilerOptions"), basePath, configFileName);
        var (typeAcquisition, err2) = ConvertTypeAcquisitionFromJsonWorker(json.GetOrZero("typeAcquisition"), basePath, configFileName);
        errors.AddRange(err);
        errors.AddRange(err2);
        var compileOnSaveEntry = json.Get("compileOnSave");
        if (compileOnSaveEntry.Ok)
        {
            var (converted, compileOnSaveErrors) = ConvertJsonOption(CompileOnSaveCommandLineOption, compileOnSaveEntry.Value, basePath, null, null, null);
            errors.AddRange(compileOnSaveErrors ?? []);
            json.Set("compileOnSave", converted);
        }

        string[]? extendedConfigPath = null;
        var extends = json.GetOrZero("extends");
        if (extends is not null && extends is not "")
        {
            (extendedConfigPath, err) = GetExtendsConfigPathOrArray(extends, host, basePath, configFileName, null, null, null);
            errors.AddRange(err);
        }

        var parsedConfig = new ParsedTsconfig
        {
            Raw = json,
            Options = options,
            TypeAcquisition = typeAcquisition,
            ExtendedConfigPath = extendedConfigPath,
        };
        return (parsedConfig, [.. errors]);
    }

    // Go: func readJsonConfigFile(fileName string, path tspath.Path, readFile func(fileName string) (string, bool)) (*TsConfigSourceFile, []*ast.Diagnostic)
    internal static (TsConfigSourceFile File, Diagnostic[] Errors) ReadJsonConfigFile(
        string fileName,
        TPath path,
        Func<string, (string Text, bool Ok)> readFile)
    {
        var (text, diagnostic) = TryReadFile(fileName, readFile, []);
        if (text != "")
        {
            return (new TsConfigSourceFile(TypeScript.Parser.Parser.ParseSourceFile(
                new SourceFileParseOptions
                {
                    FileName = fileName,
                    Path = path,
                },
                text,
                ScriptKind.JSON)), diagnostic);
        }

        var factory = new NodeFactory();
        var file = new TsConfigSourceFile(factory.NewSourceFile(
            new SourceFileParseOptions { FileName = fileName, Path = path },
            "",
            factory.NewNodeList([]),
            factory.NewToken(Kind.EndOfFile)).AsSourceFile()!);
        file.SourceFile.SetDiagnostics(diagnostic);
        return (file, diagnostic);
    }

    // Go: func getExtendedConfig(...)
    private static (ParsedTsconfig? Config, Diagnostic[] Errors) GetExtendedConfig(
        TsConfigSourceFile? sourceFile,
        string extendedConfigFileName,
        ParseConfigHost host,
        TPath[]? resolutionStack,
        ExtendedConfigCache? extendedConfigCache,
        ExtendsResult result)
    {
        var errors = new List<Diagnostic>();
        var extendedConfigPath = ToPath(extendedConfigFileName, host.GetCurrentDirectory(), host.FS().UseCaseSensitiveFileNames());

        ExtendedConfigCacheEntry cacheEntry;
        // Bypass the cache when we detect a cycle in the resolution stack.
        // The cache locks entries during parsing, and a cycle would cause the same goroutine
        // to re-lock the same entry, resulting in a deadlock. Let parseConfig handle the
        // circularity error via its own resolution stack check.
        if (extendedConfigCache is not null && !ContainsPathInStack(resolutionStack, extendedConfigPath))
        {
            cacheEntry = extendedConfigCache.GetExtendedConfig(extendedConfigFileName, extendedConfigPath, resolutionStack, host)!;
        }
        else
        {
            cacheEntry = ParseExtendedConfig(extendedConfigFileName, extendedConfigPath, resolutionStack, host, extendedConfigCache);
        }

        if (cacheEntry.Errors.Length > 0)
        {
            errors.AddRange(cacheEntry.Errors);
        }

        if (cacheEntry.ExtendedResult is { } extendedResult && sourceFile is not null)
        {
            result.ExtendedSourceFiles.Add(extendedResult.SourceFile.FileName());
            foreach (var extendedSourceFile in extendedResult.ExtendedSourceFiles ?? [])
            {
                result.ExtendedSourceFiles.Add(extendedSourceFile);
            }
        }

        return (cacheEntry.ExtendedConfig, [.. errors]);
    }

    // Go 的 `slices.Contains(resolutionStack, path)`。
    private static bool ContainsPathInStack(TPath[]? resolutionStack, TPath path) =>
        resolutionStack is not null && Array.IndexOf(resolutionStack, path) >= 0;

    // Go: func ParseExtendedConfig(...)
    public static ExtendedConfigCacheEntry ParseExtendedConfig(
        string fileName,
        TPath path,
        TPath[]? resolutionStack,
        ParseConfigHost host,
        ExtendedConfigCache? extendedConfigCache)
    {
        var (extendedResult, readErrors) = ReadJsonConfigFile(fileName, path, ReadFileFunc(host.FS()));
        var entry = new ExtendedConfigCacheEntry
        {
            ExtendedResult = extendedResult,
        };

        if (readErrors.Length > 0)
        {
            entry.Errors = readErrors;
            return entry;
        }

        if (extendedResult.SourceFile.Diagnostics() is { Length: > 0 } parseDiagnostics)
        {
            entry.Errors = parseDiagnostics;
            return entry;
        }

        var (config, parseErrors) = ParseConfig(
            null,
            extendedResult,
            host,
            GetDirectoryPath(fileName),
            GetBaseFileName(fileName),
            resolutionStack,
            extendedConfigCache);
        entry.ExtendedConfig = config;
        entry.Errors = parseErrors;
        return entry;
    }

    // parseConfig just extracts options/include/exclude/files out of a config file.
    // It does not resolve the included files.
    // Go: func parseConfig(...)
    internal static (ParsedTsconfig Config, Diagnostic[] Errors) ParseConfig(
        OrderedMap<string, object?>? json,
        TsConfigSourceFile? sourceFile,
        ParseConfigHost host,
        string basePath,
        string configFileName,
        TPath[]? resolutionStack,
        ExtendedConfigCache? extendedConfigCache)
    {
        basePath = NormalizeSlashes(basePath);
        var resolvedPath = ToPath(configFileName, basePath, host.FS().UseCaseSensitiveFileNames());
        var errors = new List<Diagnostic>();
        if (ContainsPathInStack(resolutionStack, resolvedPath))
        {
            ParsedTsconfig result;
            errors.Add(NewCompilerDiagnostic(Circularity_detected_while_resolving_configuration_Colon_0));
            if (json is null || json.Size() == 0)
            {
                result = new ParsedTsconfig { Raw = json };
            }
            else
            {
                var (rawResult, err) = ConvertToObject(sourceFile!.SourceFile);
                errors.AddRange(err);
                result = new ParsedTsconfig { Raw = rawResult };
            }

            return (result, [.. errors]);
        }

        ParsedTsconfig ownConfig;
        Diagnostic[] ownErrors;
        if (json is not null)
        {
            (ownConfig, ownErrors) = ParseOwnConfigOfJson(json, host, basePath, configFileName);
        }
        else
        {
            // Go 侧只在 json == nil 时走这里，此时 tsconfigToSourceFile(sourceFile).SourceFile 必定非 nil
            // （调用方二选一传 json 或 sourceFile，见 ParseJsonConfigFileContent/ParseJsonSourceFileConfigFileContent）。
            (ownConfig, ownErrors) = ParseOwnConfigOfJsonSourceFile(TsconfigToSourceFile(sourceFile)!, host, basePath, configFileName);
        }

        errors.AddRange(ownErrors);
        if (ownConfig.Options is { Paths: not null } ownOptions)
        {
            // If we end up needing to resolve relative paths from 'paths' relative to
            // the config file location, we'll need to know where that config file was.
            // Since 'paths' can be inherited from an extended config in another directory,
            // we wouldn't know which directory to use unless we store it here.
            ownOptions.PathsBasePath = basePath;
        }

        void ApplyExtendedConfig(ExtendsResult result, string extendedConfigPath)
        {
            var (extendedConfig, extendedErrors) = GetExtendedConfig(sourceFile, extendedConfigPath, host, resolutionStack, extendedConfigCache, result);
            errors.AddRange(extendedErrors);
            if (extendedConfig is { Options: not null })
            {
                var extendsRaw = extendedConfig.Raw;
                var relativeDifference = "";
                void SetPropertyValue(string propertyName)
                {
                    if (ownConfig.Raw is OrderedMap<string, object?> ownRawMap && ownRawMap.Has(propertyName))
                    {
                        return;
                    }

                    if (propertyName is "include" or "exclude" or "files")
                    {
                        if (extendsRaw is OrderedMap<string, object?> extendsRawMap && extendsRawMap.Has(propertyName))
                        {
                            if (extendsRawMap.GetOrZero(propertyName) is object?[] slice)
                            {
                                var value = Map(slice, path =>
                                {
                                    if (path is not string pathStr)
                                    {
                                        return path;
                                    }

                                    if (StartsWithConfigDirTemplate(path) || IsRootedDiskPath(pathStr))
                                    {
                                        return pathStr;
                                    }

                                    if (relativeDifference == "")
                                    {
                                        var t = new ComparePathsOptions(
                                            useCaseSensitiveFileNames: host.FS().UseCaseSensitiveFileNames(),
                                            currentDirectory: basePath);
                                        relativeDifference = ConvertToRelativePath(GetDirectoryPath(extendedConfigPath), t);
                                    }

                                    return CombinePaths(relativeDifference, pathStr);
                                });
                                if (propertyName == "include")
                                {
                                    result.Include = value;
                                }
                                else if (propertyName == "exclude")
                                {
                                    result.Exclude = value;
                                }
                                else if (propertyName == "files")
                                {
                                    result.Files = value;
                                }
                            }
                        }
                    }
                }

                SetPropertyValue("include");
                SetPropertyValue("exclude");
                SetPropertyValue("files");
                if (extendsRaw is OrderedMap<string, object?> extendsRawMap2 && extendsRawMap2.Has("contentMappers"))
                {
                    result.ContentMappers = extendsRawMap2.GetOrZero("contentMappers") as object?[];
                }

                if (extendsRaw is OrderedMap<string, object?> extendsRawMap3 && extendsRawMap3.Has("compileOnSave"))
                {
                    if (extendsRawMap3.GetOrZero("compileOnSave") is bool compileOnSave)
                    {
                        result.CompileOnSave = compileOnSave;
                    }
                }

                MergeCompilerOptions(result.Options!, extendedConfig.Options, extendsRaw);
            }
        }

        if (ownConfig.ExtendedConfigPath is not null)
        {
            // copy the resolution stack so it is never reused between branches in potential diamond-problem scenarios.
            resolutionStack = [.. resolutionStack ?? [], resolvedPath];
            var result = new ExtendsResult
            {
                Options = new CompilerOptions(),
            };
            if (ownConfig.ExtendedConfigPath is string extendedConfigPathString)
            {
                ApplyExtendedConfig(result, extendedConfigPathString);
            }
            else if (ownConfig.ExtendedConfigPath is string[] extendedConfigPaths)
            {
                foreach (var extendedConfigPath in extendedConfigPaths)
                {
                    ApplyExtendedConfig(result, extendedConfigPath);
                }
            }

            var ownRaw = (OrderedMap<string, object?>)ownConfig.Raw!;
            if (result.Include is not null)
            {
                ownRaw.Set("include", result.Include);
            }

            if (result.Exclude is not null)
            {
                ownRaw.Set("exclude", result.Exclude);
            }

            if (result.Files is not null)
            {
                ownRaw.Set("files", result.Files);
            }

            if (result.ContentMappers is not null && !ownRaw.Has("contentMappers"))
            {
                ownRaw.Set("contentMappers", result.ContentMappers);
            }

            if (result.CompileOnSave && !ownRaw.Has("compileOnSave"))
            {
                ownRaw.Set("compileOnSave", result.CompileOnSave);
            }

            if (sourceFile is not null)
            {
                foreach (var extendedSourceFile in result.ExtendedSourceFiles.Keys())
                {
                    sourceFile.ExtendedSourceFiles = InsertSorted(
                        sourceFile.ExtendedSourceFiles ?? [],
                        extendedSourceFile,
                        string.CompareOrdinal);
                }
            }

            ownConfig.Options = MergeCompilerOptions(result.Options!, ownConfig.Options, ownConfig.Raw);
        }

        return (ownConfig, [.. errors]);
    }
}
