// ported from: internal/tsoptions/tsconfigparsing.go:1-465（tsconfig 的 JSON→对象转换：类型、声明表入口、
// parseOwnConfigOfJsonSourceFile、convertJsonOption 系列、extends 路径解析）
//
// 大文件按逻辑段拆成 4 个 partial（同一个静态类 TsOptions）：
//   TsConfigParsing.Json.cs        1-465    JSON 值 → 选项值（含诊断）
//   TsConfigParsing.JsonObject.cs  466-780  OptionNameMap、对象/数组字面量 → JSON、ParseConfigFileTextToJson 等入口
//   TsConfigParsing.Config.cs      780-1010 默认值、readJsonConfigFile、parseConfig/extends 合并
//   TsConfigParsing.FileNames.cs   1010-2104 file specs 校验、通配枚举、GetParsedCommandLineOfConfigFile
//
// 与 Go 的差异（均在 docs/architecture.md 允许范围内）：
// - Go 的 `any` JSON 值 → `object?`（bool/string/double/null/OrderedMap<string,object?>/object?[]）。
// - Go 用 reflect 判断 JSON 值的种类（`reflect.TypeOf(v).Kind()`）→ C# 的模式匹配（docs §3.6 禁反射）。
// - Go 的包级 var 之间有初始化顺序依赖（compilerOptionsDeclaration → CommandLineCompilerOptionsMap →
//   OptionsDeclarations），C# partial 的静态字段初始化顺序不保证，所以这些互相依赖的 var 落成
//   `Lazy<T>` + 只读属性（与 TsOptionsModel 的 NameMap 系列同一约定）；名字保持 Go 的导出面。
// - `CommandLineOptionNameMap` 在 Go 里是 `map[string]*CommandLineOption` 的命名类型 → C# class。
using TypeScript.Ast;
using TypeScript.Collections;
using TypeScript.ContentMapper;
using TypeScript.Core;
using TypeScript.Diagnostics;
using TypeScript.Module;
using TypeScript.TSPath;
using TypeScript.Vfs;
using TypeScript.Vfs.VfsMatch;
using static TypeScript.Ast.Ast;
using static TypeScript.Collections.Collections;
using static TypeScript.Core.Core;
using static TypeScript.Diagnostics.Diagnostics;
using static TypeScript.Module.Module;
using static TypeScript.StringUtil.StringUtil;
using static TypeScript.TSPath.TSPath;
using static TypeScript.Vfs.VfsMatch.VfsMatch;
using Diagnostic = TypeScript.Ast.Diagnostic;
using TPath = TypeScript.TSPath.Path;

namespace TypeScript.TsOptions;

public static partial class TsOptions
{
    // Go: type extendsResult struct
    internal sealed class ExtendsResult
    {
        public CompilerOptions? Options;
        public object?[]? Include;
        public object?[]? Exclude;
        public object?[]? Files;
        public object?[]? ContentMappers;
        public bool CompileOnSave;
        public Set<string> ExtendedSourceFiles = new(); // Go 的零值 collections.Set[string]
    }

    // Go: var compilerOptionsDeclaration = &CommandLineOption{…}
    private static readonly Lazy<CommandLineOption> _compilerOptionsDeclaration = new(() => new CommandLineOption
    {
        Name = "compilerOptions",
        Kind = CommandLineOptionKind.Object,
        ElementOptions = CommandLineCompilerOptionsMap,
    });

    internal static CommandLineOption CompilerOptionsDeclaration => _compilerOptionsDeclaration.Value;

    // 名字保持 Go 的导出面（PascalCase），落成只读属性以符合 IDE1006（非公开字段须 `_camelCase`）。
    // Go: var compileOnSaveCommandLineOption = &CommandLineOption{…}
    internal static CommandLineOption CompileOnSaveCommandLineOption => _compileOnSaveCommandLineOption;

    private static readonly CommandLineOption _compileOnSaveCommandLineOption = new()
    {
        Name = "compileOnSave",
        Kind = CommandLineOptionKind.Boolean,
        DefaultValueDescription = false,
    };

    // Go: var extendsOptionDeclaration = &CommandLineOption{…}
    internal static CommandLineOption ExtendsOptionDeclaration => _extendsOptionDeclaration;

    private static readonly CommandLineOption _extendsOptionDeclaration = new()
    {
        Name = "extends",
        Kind = CommandLineOptionKind.ListOrElement,
        Category = File_Management,
        ElementOptions = CommandLineOptionsToMap([new CommandLineOption { Name = "extends", Kind = CommandLineOptionKind.String }]),
    };

    // Go: var tsconfigRootOptionsMap = &CommandLineOption{…}
    private static readonly Lazy<CommandLineOption> _tsconfigRootOptionsMap = new(() => new CommandLineOption
    {
        Name = "undefined", // should never be needed since this is root
        Kind = CommandLineOptionKind.Object,
        ElementOptions = CommandLineOptionsToMap(
        [
            CompilerOptionsDeclaration,
            TypeAcquisitionDeclaration,
            ExtendsOptionDeclaration,
            new CommandLineOption
            {
                Name = "references",
                Kind = CommandLineOptionKind.List, // should be a list of projectReference
            },
            new CommandLineOption
            {
                Name = "contentMappers",
                Kind = CommandLineOptionKind.List, // list of content mapper objects
            },
            new CommandLineOption
            {
                Name = "files",
                Kind = CommandLineOptionKind.List,
            },
            new CommandLineOption
            {
                Name = "include",
                Kind = CommandLineOptionKind.List,
            },
            new CommandLineOption
            {
                Name = "exclude",
                Kind = CommandLineOptionKind.List,
            },
            CompileOnSaveCommandLineOption,
        ]),
    });

    internal static CommandLineOption TsconfigRootOptionsMap => _tsconfigRootOptionsMap.Value;

    // Go: type configFileSpecs struct（字段名照 Go 导出的那几个，C# 侧全部 public 便于跨文件读）
    internal sealed class TsConfigFileSpecs
    {
        public object? FilesSpecs;

        // Present to report errors (user specified specs), validatedIncludeSpecs are used for file name matching
        public object? IncludeSpecs;

        // Present to report errors (user specified specs), validatedExcludeSpecs are used for file name matching
        public object? ExcludeSpecs;
        public string[] ValidatedFilesSpec = [];
        public string[] ValidatedIncludeSpecs = [];
        public string[] ValidatedExcludeSpecs = [];
        public string[] ValidatedFilesSpecBeforeSubstitution = [];
        public string[] ValidatedIncludeSpecsBeforeSubstitution = [];
        public bool IsDefaultIncludeSpec;

        // Go: func (c *configFileSpecs) matchesExclude(fileName string, comparePathsOptions tspath.ComparePathsOptions) bool
        public bool MatchesExclude(string fileName, ComparePathsOptions comparePathsOptions)
        {
            if (ValidatedExcludeSpecs.Length == 0)
            {
                return false;
            }

            var excludeMatcher = NewSpecMatcher(ValidatedExcludeSpecs, comparePathsOptions.CurrentDirectory, Usage.Exclude, comparePathsOptions.UseCaseSensitiveFileNames);
            if (excludeMatcher is null)
            {
                return false;
            }

            if (excludeMatcher.MatchString(fileName))
            {
                return true;
            }

            if (!HasExtension(fileName))
            {
                if (excludeMatcher.MatchString(EnsureTrailingDirectorySeparator(fileName)))
                {
                    return true;
                }
            }

            return false;
        }

        // Go: func (c *configFileSpecs) getMatchedIncludeSpec(fileName string, comparePathsOptions tspath.ComparePathsOptions) string
        public string GetMatchedIncludeSpec(string fileName, ComparePathsOptions comparePathsOptions)
        {
            if (ValidatedIncludeSpecs.Length == 0)
            {
                return "";
            }

            for (var index = 0; index < ValidatedIncludeSpecs.Length; index++)
            {
                var spec = ValidatedIncludeSpecs[index];
                var includeMatcher = NewSpecMatcher([spec], comparePathsOptions.CurrentDirectory, Usage.Files, comparePathsOptions.UseCaseSensitiveFileNames);
                if (includeMatcher is not null && includeMatcher.MatchString(fileName))
                {
                    return ValidatedIncludeSpecsBeforeSubstitution[index];
                }
            }

            return "";
        }

        // Go: func (c *configFileSpecs) getMatchedFileSpec(fileName string, comparePathsOptions tspath.ComparePathsOptions) string
        public string GetMatchedFileSpec(string fileName, ComparePathsOptions comparePathsOptions)
        {
            if (ValidatedFilesSpec.Length == 0)
            {
                return "";
            }

            var filePath = ToPath(fileName, comparePathsOptions.CurrentDirectory, comparePathsOptions.UseCaseSensitiveFileNames);
            for (var index = 0; index < ValidatedFilesSpec.Length; index++)
            {
                if (ToPath(ValidatedFilesSpec[index], comparePathsOptions.CurrentDirectory, comparePathsOptions.UseCaseSensitiveFileNames) == filePath)
                {
                    return ValidatedFilesSpecBeforeSubstitution[index];
                }
            }

            return "";
        }
    }

    // Go: type ExtendedConfigCache interface
    public interface ExtendedConfigCache
    {
        ExtendedConfigCacheEntry? GetExtendedConfig(string fileName, TPath path, TPath[]? resolutionStack, ParseConfigHost host);
    }

    // Go: type ExtendedConfigCacheEntry struct
    public sealed class ExtendedConfigCacheEntry
    {
        internal TsConfigSourceFile? _extendedResult;
        internal ParsedTsconfig? _extendedConfig;
        internal Diagnostic[] _errors = [];

        /// <summary>Go 的导出字段（`extendedResult` 等未导出的部分由本包内部读写）。</summary>
        public TsConfigSourceFile? ExtendedResult
        {
            get => _extendedResult;
            internal set => _extendedResult = value;
        }

        // Go: 未导出字段 extendedConfig（ParsedTsconfig 是 internal 类型，所以属性也必须是 internal）。
        internal ParsedTsconfig? ExtendedConfig
        {
            get => _extendedConfig;
            set => _extendedConfig = value;
        }

        public Diagnostic[] Errors
        {
            get => _errors;
            internal set => _errors = value;
        }

        // Go: func (e *ExtendedConfigCacheEntry) ExtendedFileNames() []string
        public string[] ExtendedFileNames() => _extendedResult?.ExtendedSourceFiles ?? [];
    }

    // Go: type parsedTsconfig struct
    internal sealed class ParsedTsconfig
    {
        public object? Raw;
        public CompilerOptions? Options;
        public TypeAcquisition? TypeAcquisition;

        // Note that the case of the config path has not yet been normalized, as no files have been imported into the project yet
        public object? ExtendedConfigPath;
    }

    // Go: func parseOwnConfigOfJsonSourceFile(...)
    internal static (ParsedTsconfig Config, Diagnostic[] Errors) ParseOwnConfigOfJsonSourceFile(
        SourceFile sourceFile,
        ParseConfigHost host,
        string basePath,
        string configFileName)
    {
        var compilerOptions = GetDefaultCompilerOptions(configFileName);
        var typeAcquisition = GetDefaultTypeAcquisition(configFileName);
        object? extendedConfigPath = null;
        var rootCompilerOptions = new List<Node>();
        var errors = new List<Diagnostic>();
        (object? Value, Diagnostic[] Errors) OnPropertySet(
            string keyText,
            object? value,
            PropertyAssignment propertyAssignment,
            CommandLineOption? parentOption,
            CommandLineOption? option)
        {
            // Ensure value is verified except for extends which is handled in its own way for error reporting
            // ConvertJsonOption 返回 `Diagnostic[]?`（null 表示无错误）；这里承接后归一成非空数组，
            // 避免下面的展开式把它当成可能为 null（Go 的 nil slice 展开等价于空）。
            Diagnostic[] propertySetErrors = [];
            if (option is not null && !ReferenceEquals(option, ExtendsOptionDeclaration))
            {
                var (convertedValue, convertedErrors) = ConvertJsonOption(option, value, basePath, propertyAssignment, propertyAssignment.Initializer, sourceFile);
                value = convertedValue;
                propertySetErrors = convertedErrors ?? [];
            }

            if (parentOption is not null && parentOption.Name != "undefined" && value is not null)
            {
                if (option is not null && option.Name != "")
                {
                    Diagnostic[] parseDiagnostics = [];
                    switch (parentOption.Name)
                    {
                        case "compilerOptions":
                            parseDiagnostics = ParseCompilerOptions(option.Name, value, compilerOptions);
                            break;
                        case "typeAcquisition":
                            parseDiagnostics = ParseTypeAcquisition(option.Name, value, typeAcquisition);
                            break;
                    }

                    propertySetErrors = [.. propertySetErrors, .. parseDiagnostics];
                }
                else if (keyText != "" && ExtraKeyDiagnostics(parentOption.Name) is { } unknownNameDiag)
                {
                    if (parentOption.ElementOptions is not null)
                    {
                        var possibleOption = parentOption.ElementOptions.Get(keyText);
                        possibleOption ??= parentOption.ElementOptions.GetSpellingSuggestion(keyText);
                        if (possibleOption is not null && possibleOption.Name != keyText)
                        {
                            propertySetErrors =
                            [
                                .. propertySetErrors,
                                CreateDiagnosticForNodeInSourceFileOrCompilerDiagnostic(
                                    sourceFile,
                                    propertyAssignment.Name(),
                                    ExtraKeyDidYouMeanDiagnostics(parentOption.Name)!,
                                    keyText,
                                    possibleOption.Name),
                            ];
                        }
                        else
                        {
                            propertySetErrors =
                            [
                                .. propertySetErrors,
                                CreateUnknownOptionError(
                                    keyText,
                                    unknownNameDiag,
                                    "" /*unknownOptionErrorText*/,
                                    propertyAssignment.Name(),
                                    sourceFile,
                                    null /*alternateMode*/,
                                    null /*unknownDidYouMeanDiagnostic*/,
                                    null /*optionsNameMap*/),
                            ];
                        }
                    }
                }
            }
            else if (ReferenceEquals(parentOption, TsconfigRootOptionsMap))
            {
                if (ReferenceEquals(option, ExtendsOptionDeclaration))
                {
                    var (configPath, err) = GetExtendsConfigPathOrArray(value, host, basePath, configFileName, propertyAssignment, propertyAssignment.Initializer, sourceFile);
                    extendedConfigPath = configPath;
                    propertySetErrors = [.. propertySetErrors, .. err];
                }
                else if (option is null)
                {
                    // PropertyAssignment 的 name 由语法保证存在（Go 侧同样直接解引用 propertyAssignment.Name()）。
                    var propertyNameNode = propertyAssignment.Name()!;
                    if (keyText == "excludes")
                    {
                        propertySetErrors =
                        [
                            .. propertySetErrors,
                            CreateDiagnosticForNodeInSourceFile(sourceFile, propertyNameNode, Unknown_option_excludes_Did_you_mean_exclude),
                        ];
                    }

                    if (Find(OptionsForCompiler, option => option.Name == keyText) is not null)
                    {
                        rootCompilerOptions.Add(propertyNameNode);
                    }
                }
            }

            return (value, propertySetErrors);
        }

        var (json, err) = ConvertConfigFileToObject(
            sourceFile,
            new JsonConversionNotifier
            {
                RootOptions = TsconfigRootOptionsMap,
                OnPropertySet = OnPropertySet,
            });
        errors.AddRange(err);
        if (json is OrderedMap<string, object?> jsonObject && rootCompilerOptions.Count != 0 && !jsonObject.Has("compilerOptions"))
        {
            errors.Add(CreateDiagnosticForNodeInSourceFile(
                sourceFile,
                rootCompilerOptions[0],
                X_0_should_be_set_inside_the_compilerOptions_object_of_the_config_json_file,
                GetTextOfPropertyName(rootCompilerOptions[0])));
        }

        return (new ParsedTsconfig
        {
            Raw = json,
            Options = compilerOptions,
            TypeAcquisition = typeAcquisition,
            ExtendedConfigPath = extendedConfigPath,
        }, [.. errors]);
    }

    // Go: type TsConfigSourceFile struct
    public sealed class TsConfigSourceFile
    {
        public string[]? ExtendedSourceFiles;
        internal TsConfigFileSpecs? _configFileSpecs;
        public SourceFile SourceFile;

        public TsConfigSourceFile(SourceFile sourceFile)
        {
            SourceFile = sourceFile;
        }
    }

    // Go: func tsconfigToSourceFile(tsconfigSourceFile *TsConfigSourceFile) *ast.SourceFile
    internal static SourceFile? TsconfigToSourceFile(TsConfigSourceFile? tsconfigSourceFile) => tsconfigSourceFile?.SourceFile;

    // Go: func NewTsconfigSourceFileFromFilePath(configFileName string, configPath tspath.Path, configSourceText string) *TsConfigSourceFile
    public static TsConfigSourceFile NewTsconfigSourceFileFromFilePath(string configFileName, TPath configPath, string configSourceText)
    {
        var sourceFile = TypeScript.Parser.Parser.ParseSourceFile(
            new SourceFileParseOptions
            {
                FileName = configFileName,
                Path = configPath,
            },
            configSourceText,
            ScriptKind.JSON);
        return new TsConfigSourceFile(sourceFile);
    }

    // Go: type jsonConversionNotifier struct
    internal sealed class JsonConversionNotifier
    {
        public CommandLineOption? RootOptions;
        public Func<string, object?, PropertyAssignment, CommandLineOption?, CommandLineOption?, (object?, Diagnostic[])> OnPropertySet = (_, _, _, _, _) => (null, []);
    }

    // Go: func convertConfigFileToObject(...)
    internal static (object? Json, Diagnostic[] Errors) ConvertConfigFileToObject(
        SourceFile sourceFile,
        JsonConversionNotifier? jsonConversionNotifier)
    {
        Node? rootExpression = null;
        if (sourceFile.Statements is { Nodes.Length: > 0 })
        {
            rootExpression = sourceFile.Statements.Nodes[0].Expression();
        }

        if (rootExpression is not null && rootExpression.Kind != Kind.ObjectLiteralExpression)
        {
            var baseFileName = "tsconfig.json";
            if (GetBaseFileName(sourceFile.FileName()) == "jsconfig.json")
            {
                baseFileName = "jsconfig.json";
            }

            Diagnostic[] errors =
            [
                CreateDiagnosticForNodeInSourceFile(sourceFile, rootExpression, The_root_value_of_a_0_file_must_be_an_object, baseFileName),
            ];

            // Last-ditch error recovery. Somewhat useful because the JSON parser will recover from some parse errors by
            // synthesizing a top-level array literal expression. There's a reasonable chance the first element of that
            // array is a well-formed configuration object, made into an array element by stray characters.
            if (IsArrayLiteralExpression(rootExpression))
            {
                var firstObject = Find(rootExpression.Elements(), node => IsObjectLiteralExpression(node));
                if (firstObject is not null)
                {
                    return ConvertToJson(sourceFile, firstObject, true /*returnValue*/, jsonConversionNotifier);
                }
            }

            return (new OrderedMap<string, object?>(), errors);
        }

        return ConvertToJson(sourceFile, rootExpression, true, jsonConversionNotifier);
    }

    // Go: func isCompilerOptionsValue(option *CommandLineOption, value any) bool
    internal static bool IsCompilerOptionsValue(CommandLineOption? option, object? value)
    {
        if (option is not null)
        {
            if (value is null)
            {
                return !option.DisallowNullOrUndefined();
            }

            switch (option.Kind)
            {
                case CommandLineOptionKind.List:
                    return value is object?[];
                case CommandLineOptionKind.ListOrElement:
                    return value is object?[] || IsCompilerOptionsValue(option.Elements(), value);
                case CommandLineOptionKind.String:
                    return value is string;
                case CommandLineOptionKind.Boolean:
                    return value is bool;
                case CommandLineOptionKind.Number:
                    return value is double;
                case CommandLineOptionKind.Object:
                    return value is OrderedMap<string, object?>;
                case CommandLineOptionKind.Enum:
                    return value is string;
            }
        }

        return false;
    }

    // Go: func validateJsonOptionValue(...)
    // Errors 用空数组而不是 null：Go 的调用点全是 len(errors)==0 / append，空切片与 nil 在那边无从区分；
    // C# 侧返回非空数组可以让 nullable 流分析正常工作（调用点不必到处判空）。
    internal static (object? Value, Diagnostic[] Errors) ValidateJsonOptionValue(
        CommandLineOption opt,
        object? val,
        Node? valueExpression,
        SourceFile? sourceFile)
    {
        if (val is null)
        {
            return (null, []);
        }

        Diagnostic[] errors = [];

        switch (opt._extraValidation)
        {
            case ExtraValidation.Spec:
                if (SpecToDiagnostic((string)val, false) is { } diag)
                {
                    errors = [CreateDiagnosticForNodeInSourceFileOrCompilerDiagnostic(sourceFile, valueExpression, diag)];
                }

                break;
            case ExtraValidation.Locale:
                if (!TypeScript.Locale.Locale.TryParse((string)val, out _))
                {
                    errors =
                    [
                        CreateDiagnosticForNodeInSourceFileOrCompilerDiagnostic(sourceFile, valueExpression, Locale_must_be_an_IETF_BCP_47_language_tag_Examples_Colon_0_1, "en", "ja-jp"),
                    ];
                }

                break;
        }

        if (errors is { Length: > 0 })
        {
            return (null, errors);
        }

        return (val, []);
    }

    // Go: func convertJsonOptionOfListType(...)
    internal static (object?[]? Values, Diagnostic[]? Errors) ConvertJsonOptionOfListType(
        CommandLineOption option,
        object? values,
        string basePath,
        PropertyAssignment? propertyAssignment,
        Node? valueExpression,
        SourceFile? sourceFile)
    {
        Node? expression = null;
        Diagnostic[]? errors = null;
        if (values is object?[] valuesArray)
        {
            var mappedValues = MapIndex(valuesArray, (v, index) =>
            {
                if (valueExpression is not null)
                {
                    // valueExpression 在 values 为数组时必定是数组字面量（Go 同样直接解引用 Elements()）。
                    expression = valueExpression.Elements()![index];
                }

                var (result, err) = ConvertJsonOption(option.Elements()!, v, basePath, propertyAssignment, expression, sourceFile);
                errors = errors is null ? err : [.. errors, .. err ?? []];
                return result;
            });
            var filteredValues = mappedValues;
            if (!option._listPreserveFalsyValues)
            {
                filteredValues = Filter(mappedValues, v => !(v is null || v is false || (v is double d && d == 0) || v is ""));
            }

            return (filteredValues, errors);
        }

        return (null, errors);
    }

    // Go: const configDirTemplate = "${configDir}"（落成只读属性以符合 IDE1006）
    internal static string ConfigDirTemplate => "${configDir}";

    // Go: func startsWithConfigDirTemplate(value any) bool
    private static bool StartsWithConfigDirTemplate(object? value)
    {
        if (value is not string str)
        {
            return false;
        }

        return HasPrefix(TypeScript.StringUtil.StringUtil.ToLowerJS(str), TypeScript.StringUtil.StringUtil.ToLowerJS(ConfigDirTemplate), false);
    }

    // Go: func normalizeNonListOptionValue(option *CommandLineOption, basePath string, value any) any
    private static object? NormalizeNonListOptionValue(CommandLineOption option, string basePath, object? value)
    {
        if (option.IsFilePath)
        {
            value = NormalizeSlashes((string)value!);
            if (!StartsWithConfigDirTemplate(value))
            {
                value = GetNormalizedAbsolutePath((string)value, basePath);
            }

            if (value is "")
            {
                value = ".";
            }
        }

        return value;
    }

    // Go: func convertJsonOption(...)
    internal static (object? Value, Diagnostic[]? Errors) ConvertJsonOption(
        CommandLineOption opt,
        object? value,
        string basePath,
        PropertyAssignment? propertyAssignment,
        Node? valueExpression,
        SourceFile? sourceFile)
    {
        if (opt.IsCommandLineOnly)
        {
            Node? nodeValue = null;
            if (propertyAssignment is not null)
            {
                nodeValue = propertyAssignment.Name();
            }

            if (sourceFile is null && nodeValue is null)
            {
                return (null, [NewCompilerDiagnostic(Option_0_can_only_be_specified_on_command_line, opt.Name)]);
            }

            return (null, [CreateDiagnosticForNodeInSourceFileOrCompilerDiagnostic(sourceFile, nodeValue, Option_0_can_only_be_specified_on_command_line, opt.Name)]);
        }

        if (IsCompilerOptionsValue(opt, value))
        {
            switch (opt.Kind)
            {
                case CommandLineOptionKind.List:
                    return ConvertJsonOptionOfListType(opt, value, basePath, propertyAssignment, valueExpression, sourceFile); // as ArrayLiteralExpression | undefined
                case CommandLineOptionKind.ListOrElement:
                    if (value is object?[])
                    {
                        return ConvertJsonOptionOfListType(opt, value, basePath, propertyAssignment, valueExpression, sourceFile);
                    }

                    return ConvertJsonOption(opt.Elements()!, value, basePath, propertyAssignment, valueExpression, sourceFile);
                case CommandLineOptionKind.Enum:
                    if (value is null)
                    {
                        return (null, null);
                    }

                    return ConvertJsonOptionOfEnumType(opt, (string)value, valueExpression, sourceFile);
            }

            var (validatedValue, errors) = ValidateJsonOptionValue(opt, value, valueExpression, sourceFile);
            if (errors is { Length: > 0 } || validatedValue is null)
            {
                return (validatedValue, errors);
            }

            return (NormalizeNonListOptionValue(opt, basePath, validatedValue), errors);
        }

        return (null, [CreateDiagnosticForNodeInSourceFileOrCompilerDiagnostic(sourceFile, valueExpression, Compiler_option_0_requires_a_value_of_type_1, opt.Name, GetCompilerOptionValueTypeString(opt))]);
    }

    // Go: func getExtendsConfigPathOrArray(...)
    internal static (string[]? ExtendedConfigPaths, Diagnostic[] Errors) GetExtendsConfigPathOrArray(
        object? value,
        ParseConfigHost host,
        string basePath,
        string configFileName,
        PropertyAssignment? propertyAssignment,
        Node? valueExpression,
        SourceFile? sourceFile)
    {
        var extendedConfigPathArray = new List<string>();
        var newBase = basePath;
        if (configFileName != "")
        {
            newBase = DirectoryOfCombinedPath(configFileName, basePath);
        }

        if (value is null)
        {
            var (_, errors) = ConvertJsonOption(ExtendsOptionDeclaration, value, basePath, propertyAssignment, valueExpression, sourceFile);
            return (NullIfEmpty(extendedConfigPathArray), errors ?? []);
        }

        if (value is string valueString)
        {
            var (val, err) = GetExtendsConfigPath(valueString, host, newBase, valueExpression, sourceFile);
            if (val != "")
            {
                extendedConfigPathArray.Add(val);
            }

            return (NullIfEmpty(extendedConfigPathArray), err);
        }

        Diagnostic[]? allErrors = null;
        if (value is object?[] valueArray)
        {
            for (var index = 0; index < valueArray.Length; index++)
            {
                var fileName = valueArray[index];
                Node? expression = null;
                if (valueExpression is not null)
                {
                    // valueExpression 在 values 为数组时必定是数组字面量（Go 同样直接解引用 Elements()）。
                    expression = valueExpression.Elements()![index];
                }

                if (fileName is string fileNameString)
                {
                    var (val, err) = GetExtendsConfigPath(fileNameString, host, newBase, expression, sourceFile);
                    if (val != "")
                    {
                        extendedConfigPathArray.Add(val);
                    }

                    allErrors = allErrors is null ? err : [.. allErrors, .. err];
                }
                else
                {
                    var (_, err) = ConvertJsonOption(ExtendsOptionDeclaration.Elements()!, value, basePath, propertyAssignment, expression, sourceFile);
                    allErrors = allErrors is null ? err ?? [] : [.. allErrors, .. err ?? []];
                }
            }
        }
        else
        {
            (_, allErrors) = ConvertJsonOption(ExtendsOptionDeclaration, value, basePath, propertyAssignment, valueExpression, sourceFile);
        }

        return (NullIfEmpty(extendedConfigPathArray), allErrors ?? []);
    }

    // Go 的 `var extendedConfigPathArray []string` 在没 append 过时是 nil；
    // parseConfig 用 `extendedConfigPath != nil` 判断是否走 extends 合并分支，所以这里必须区分 nil 与空。
    private static string[]? NullIfEmpty(List<string> paths) => paths.Count == 0 ? null : [.. paths];

    // Go: func getExtendsConfigPath(...)
    internal static (string Path, Diagnostic[] Errors) GetExtendsConfigPath(
        string extendedConfig,
        ParseConfigHost host,
        string basePath,
        Node? valueExpression,
        SourceFile? sourceFile)
    {
        extendedConfig = NormalizeSlashes(extendedConfig);
        var errors = new List<Diagnostic>();
        var errorFile = sourceFile;
        if (IsRootedDiskPath(extendedConfig) || extendedConfig.StartsWith("./", StringComparison.Ordinal) || extendedConfig.StartsWith("../", StringComparison.Ordinal))
        {
            var extendedConfigPath = GetNormalizedAbsolutePath(extendedConfig, basePath);
            if (!host.FS().FileExists(extendedConfigPath) && !extendedConfigPath.EndsWith(ExtensionJson, StringComparison.Ordinal))
            {
                extendedConfigPath += ExtensionJson;
                if (!host.FS().FileExists(extendedConfigPath))
                {
                    errors.Add(CreateDiagnosticForNodeInSourceFileOrCompilerDiagnostic(errorFile, valueExpression, File_0_not_found, extendedConfig));
                    return ("", [.. errors]);
                }
            }

            return (extendedConfigPath, [.. errors]);
        }

        // If the path isn't a rooted or relative path, resolve like a module
        var resolverHost = new ResolverHost(host);
        var resolved = ResolveConfig(extendedConfig, CombinePaths(basePath, "tsconfig.json"), resolverHost);
        if (resolved.IsResolved())
        {
            return (resolved.ResolvedFileName, [.. errors]);
        }

        if (extendedConfig == "")
        {
            errors.Add(CreateDiagnosticForNodeInSourceFileOrCompilerDiagnostic(errorFile, valueExpression, Compiler_option_0_cannot_be_given_an_empty_string, "extends"));
        }
        else
        {
            errors.Add(CreateDiagnosticForNodeInSourceFileOrCompilerDiagnostic(errorFile, valueExpression, File_0_not_found, extendedConfig));
        }

        return ("", [.. errors]);
    }
}
