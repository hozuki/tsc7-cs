// ported from: internal/tsoptions/tsconfigparsing.go:1011-2104（configFileSpecs 校验、通配枚举、
// parseJsonConfigFileContentWorker、GetParsedCommandLineOfConfigFile 入口）
//
// 与 Go 的差异：
// - Go 的 `propOfRaw{wrongValue: "no-prop"/"not-array"}` 用字符串哨兵 → C# 保留同样的字符串常量
//   （`PropOfRaw` 的 WrongValue），便于逐行对照。
// - Go 的 `reflect.TypeOf(element) == orderedMapType` → C# 模式匹配 `is OrderedMap<string, object?>`。
// - `func(value any) bool` 的校验回调 → `Func<object?, bool>`。
// - `[]T` 的 Go nil/空 slice 在 These 函数里只影响 `len == 0` 判断，C# 用 `[]`（注释标出 Go 为 nil 的位置）。
// - `tspath.Path` → `TypeScript.TSPath.Path`（record struct，相等语义与 Go 一致）。
using TypeScript.Ast;
using TypeScript.Collections;
using TypeScript.ContentMapper;
using TypeScript.Core;
using TypeScript.Diagnostics;
using TypeScript.TSPath;
using TypeScript.Vfs;
using TypeScript.Vfs.VfsMatch;
using static TypeScript.Ast.Ast;
using static TypeScript.Collections.Collections;
using static TypeScript.Core.Core;
using static TypeScript.Diagnostics.Diagnostics;
using static TypeScript.TSPath.TSPath;
using static TypeScript.Vfs.VfsMatch.VfsMatch;
using Diagnostic = TypeScript.Ast.Diagnostic;
using TPath = TypeScript.TSPath.Path;

namespace TypeScript.TsOptions;

public static partial class TsOptions
{
    // Go: const defaultIncludeSpec = "**/*"（落成只读属性以符合 IDE1006）
    internal static string DefaultIncludeSpec => "**/*";

    // Go: type propOfRaw struct
    internal sealed class PropOfRaw
    {
        public object?[]? SliceValue;
        public string WrongValue = "";
    }

    // Go: func isStringValue(value any) bool
    private static bool IsStringValue(object? value) => value is string;

    // parseJsonConfigFileContentWorker parses the contents of a config file from json or json source file (tsconfig.json).
    // json: The contents of the config file to parse
    // sourceFile: sourceFile corresponding to the Json
    // host: Instance of ParseConfigHost used to enumerate files in folder.
    // basePath: A root directory to resolve relative path entries in the config file to. e.g. outDir
    // resolutionStack: Only present for backwards-compatibility. Should be empty.
    // Go: func parseJsonConfigFileContentWorker(...)
    internal static ParsedCommandLine ParseJsonConfigFileContentWorker(
        OrderedMap<string, object?>? json,
        TsConfigSourceFile? sourceFile,
        ParseConfigHost host,
        string basePath,
        CompilerOptions? existingOptions,
        OrderedMap<string, object?>? existingOptionsRaw,
        string configFileName,
        TPath[]? resolutionStack,
        ExtendedConfigCache? extendedConfigCache)
    {
        Debug.Assert(json is null && sourceFile is not null || json is not null && sourceFile is null);

        string basePathForFileNames;
        if (configFileName != "")
        {
            basePathForFileNames = NormalizePath(DirectoryOfCombinedPath(configFileName, basePath));
        }
        else
        {
            basePathForFileNames = NormalizePath(basePath);
        }

        var errors = new List<Diagnostic>();
        var (parsedConfig, parseErrors) = ParseConfig(json, sourceFile, host, basePath, configFileName, resolutionStack, extendedConfigCache);
        errors.AddRange(parseErrors);
        // Go 在这里直接解引用 parsedConfig.options；仅在"extends 成环 + existingOptions 非空"时才会为 nil
        // （Go 会 panic，C# 是 NRE，行为一致）。
        MergeCompilerOptions(parsedConfig.Options!, existingOptions, existingOptionsRaw);
        HandleOptionConfigDirTemplateSubstitution(parsedConfig.Options, basePathForFileNames);
        var rawConfig = ParseJsonToStringKey(parsedConfig.Raw);
        if (configFileName != "" && parsedConfig.Options is not null)
        {
            parsedConfig.Options.ConfigFilePath = NormalizeSlashes(configFileName);
        }

        PropOfRaw GetPropFromRaw(string prop, Func<object?, bool> validateElement, string elementTypeName)
        {
            var (value, exists) = rawConfig.Get(prop);
            if (exists && value is not null)
            {
                if (value is Array)
                {
                    var result = rawConfig.GetOrZero(prop);
                    if (result is object?[] resultArray)
                    {
                        if (sourceFile is null && !Every(resultArray, validateElement))
                        {
                            errors.Add(NewCompilerDiagnostic(Compiler_option_0_requires_a_value_of_type_1, prop, elementTypeName));
                        }
                    }

                    return new PropOfRaw { SliceValue = (object?[]?)result };
                }

                if (sourceFile is null)
                {
                    errors.Add(NewCompilerDiagnostic(Compiler_option_0_requires_a_value_of_type_1, prop, "Array"));
                    return new PropOfRaw { SliceValue = null, WrongValue = "not-array" };
                }
            }

            return new PropOfRaw { SliceValue = null, WrongValue = "no-prop" };
        }

        var referencesOfRaw = GetPropFromRaw("references", element => element is OrderedMap<string, object?>, "object");
        var fileSpecs = GetPropFromRaw("files", IsStringValue, "string");
        if (fileSpecs.SliceValue is not null || fileSpecs.WrongValue == "")
        {
            var hasZeroOrNoReferences = false;
            if (referencesOfRaw.WrongValue is "no-prop" or "not-array" || referencesOfRaw.SliceValue is { Length: 0 })
            {
                hasZeroOrNoReferences = true;
            }

            var hasExtends = rawConfig.GetOrZero("extends");
            if (fileSpecs.SliceValue is { Length: 0 } && hasZeroOrNoReferences && hasExtends is null)
            {
                if (sourceFile is not null)
                {
                    var fileName = configFileName != "" ? configFileName : "tsconfig.json";
                    var diagnosticMessage = The_files_list_in_config_file_0_is_empty;
                    // "files" 属性存在（上面的分支条件），所以 nodeValue 非空（Go 里为 nil 会 panic）。
                    var nodeValue = ForEachTsConfigPropArray(sourceFile.SourceFile, "files", property => property.Initializer)!;
                    errors.Add(CreateDiagnosticForNodeInSourceFile(sourceFile.SourceFile, nodeValue, diagnosticMessage, fileName));
                }
                else
                {
                    errors.Add(NewCompilerDiagnostic(The_files_list_in_config_file_0_is_empty, configFileName));
                }
            }
        }

        var includeSpecs = GetPropFromRaw("include", IsStringValue, "string");
        var excludeSpecs = GetPropFromRaw("exclude", IsStringValue, "string");
        var isDefaultIncludeSpec = false;
        if (excludeSpecs.WrongValue == "no-prop" && parsedConfig.Options is not null)
        {
            var outDir = parsedConfig.Options.OutDir;
            var declarationDir = parsedConfig.Options.DeclarationDir;
            if (outDir != "" || declarationDir != "")
            {
                var values = new List<object?>();
                if (outDir != "")
                {
                    values.Add(outDir);
                }

                if (declarationDir != "")
                {
                    values.Add(declarationDir);
                }

                excludeSpecs = new PropOfRaw { SliceValue = [.. values] };
            }
        }

        if (fileSpecs.SliceValue is null && includeSpecs.SliceValue is null)
        {
            includeSpecs = new PropOfRaw { SliceValue = [DefaultIncludeSpec] };
            isDefaultIncludeSpec = true;
        }

        // Go 这里用 nil slice 表示"没有"；C# 用 []，语义只体现在后续的 `!= nil` 判断上，这里是本函数的局部变量。
        string[] validatedIncludeSpecs = [];
        string[] validatedIncludeSpecsBeforeSubstitution = [];
        string[] validatedExcludeSpecs = [];
        string[] validatedFilesSpec = [];
        string[] validatedFilesSpecBeforeSubstitution = [];

        // The exclude spec list is converted into a regular expression, which allows us to quickly
        // test whether a file or directory should be excluded before recursively traversing the
        // file system.
        if (includeSpecs.SliceValue is not null)
        {
            var (specs, err) = ValidateSpecs(includeSpecs.SliceValue, true /*disallowTrailingRecursion*/, TsconfigToSourceFile(sourceFile), "include");
            validatedIncludeSpecsBeforeSubstitution = specs;
            errors.AddRange(err);
            validatedIncludeSpecs = GetSubstitutedStringArrayWithConfigDirTemplate(validatedIncludeSpecsBeforeSubstitution, basePathForFileNames)
                ?? validatedIncludeSpecsBeforeSubstitution;
        }

        if (excludeSpecs.SliceValue is not null)
        {
            var (specs, err) = ValidateSpecs(excludeSpecs.SliceValue, false /*disallowTrailingRecursion*/, TsconfigToSourceFile(sourceFile), "exclude");
            validatedExcludeSpecs = specs;
            errors.AddRange(err);
            if (GetSubstitutedStringArrayWithConfigDirTemplate(validatedExcludeSpecs, basePathForFileNames) is { } validatedExcludeSpecsWithSubstitution)
            {
                validatedExcludeSpecs = validatedExcludeSpecsWithSubstitution;
            }
        }

        if (fileSpecs.SliceValue is not null)
        {
            var files = Filter(fileSpecs.SliceValue, IsStringValue);
            foreach (var spec in files)
            {
                validatedFilesSpecBeforeSubstitution = [.. validatedFilesSpecBeforeSubstitution, (string)spec!];
            }

            validatedFilesSpec = GetSubstitutedStringArrayWithConfigDirTemplate(validatedFilesSpecBeforeSubstitution, basePathForFileNames)
                ?? validatedFilesSpecBeforeSubstitution;
        }

        var configFileSpecs = new TsConfigFileSpecs
        {
            FilesSpecs = fileSpecs.SliceValue,
            IncludeSpecs = includeSpecs.SliceValue,
            ExcludeSpecs = excludeSpecs.SliceValue,
            ValidatedFilesSpec = validatedFilesSpec,
            ValidatedIncludeSpecs = validatedIncludeSpecs,
            ValidatedExcludeSpecs = validatedExcludeSpecs,
            ValidatedFilesSpecBeforeSubstitution = validatedFilesSpecBeforeSubstitution,
            ValidatedIncludeSpecsBeforeSubstitution = validatedIncludeSpecsBeforeSubstitution,
            IsDefaultIncludeSpec = isDefaultIncludeSpec,
        };

        if (sourceFile is not null)
        {
            sourceFile._configFileSpecs = configFileSpecs;
        }

        var contentMapperSourceFile = sourceFile?.SourceFile;
        var contentMappers = new List<Mapper>();
        var contentMapperIndices = new List<int>();
        var contentMappersOfRaw = GetPropFromRaw("contentMappers", element => element is OrderedMap<string, object?>, "object");
        for (var i = 0; i < (contentMappersOfRaw.SliceValue?.Length ?? 0); i++)
        {
            var element = contentMappersOfRaw.SliceValue![i];
            var (mapper, mapperErrors) = ParseContentMapper(element);
            foreach (var mapperError in mapperErrors)
            {
                errors.Add(SetContentMapperDiagnosticLocation(mapperError, contentMapperSourceFile, GetContentMapperSyntax(contentMapperSourceFile, i, "")));
            }

            if (mapper is not null)
            {
                contentMappers.Add(mapper);
                contentMapperIndices.Add(i);
            }
        }

        var totalContentMapperExtensions = 0;
        foreach (var mapper in contentMappers)
        {
            totalContentMapperExtensions += mapper.Extensions.Length;
        }

        var seenContentMapperExtensions = new HashSet<string>(totalContentMapperExtensions);
        var contentMapperExtensions = new List<string>(totalContentMapperExtensions);
        var nativeExtensions = Flatten(AllSupportedExtensionsWithJson);
        string CanonicalExtension(string extension) => GetCanonicalFileName(extension, host.FS().UseCaseSensitiveFileNames());
        for (var j = 0; j < contentMappers.Count; j++)
        {
            var mapper = contentMappers[j];
            var validExtensions = new List<string>(mapper.Extensions.Length);
            foreach (var ext in mapper.Extensions)
            {
                var extNode = GetContentMapperExtensionSyntax(contentMapperSourceFile, contentMapperIndices[j], ext);
                var canonicalExt = CanonicalExtension(ext);
                if (!ext.StartsWith('.'))
                {
                    errors.Add(SetContentMapperDiagnosticLocation(NewCompilerDiagnostic(Content_mapper_file_extension_0_must_begin_with_a, ext), contentMapperSourceFile, extNode));
                }
                else if (Some(nativeExtensions, nativeExtension => TypeScript.StringUtil.StringUtil.EquateStringCaseInsensitive(nativeExtension, ext)))
                {
                    errors.Add(SetContentMapperDiagnosticLocation(NewCompilerDiagnostic(Content_mapper_file_extension_0_is_a_built_in_extension_and_cannot_be_registered_by_a_content_mapper, ext), contentMapperSourceFile, extNode));
                }
                else
                {
                    if (!seenContentMapperExtensions.Add(canonicalExt))
                    {
                        errors.Add(SetContentMapperDiagnosticLocation(NewCompilerDiagnostic(Content_mapper_file_extension_0_is_registered_by_more_than_one_content_mapper, ext), contentMapperSourceFile, extNode));
                    }
                    else
                    {
                        contentMapperExtensions.Add(ext);
                        validExtensions.Add(ext);
                    }
                }
            }

            mapper.Extensions = [.. validExtensions];
        }

        if (contentMappers.Count > 0 && !(parsedConfig.Options is not null && parsedConfig.Options.RunExternalCode.IsTrue()))
        {
            errors.Add(SetContentMapperDiagnosticLocation(NewCompilerDiagnostic(Content_mappers_require_the_runExternalCode_command_line_flag_to_be_enabled), contentMapperSourceFile, GetContentMappersKeySyntax(contentMapperSourceFile)));

            // Without the flag the mappers are not trusted to run, so drop them entirely: their extensions are
            // not registered and their files are not intercepted (they are treated as unknown foreign files).
            contentMappers = [];
            contentMapperExtensions = [];
        }
        else if (contentMappers.Count > 0)
        {
            // Resolve each mapper's package.json now so its name, version, and run command are available to
            // everything downstream (diagnostics, build-info staleness) without executing anything.
            var containingFile = configFileName;
            if (containingFile == "")
            {
                containingFile = CombinePaths(basePathForFileNames, "tsconfig.json");
            }

            var resolvedContentMappers = new List<Mapper>(contentMappers.Count);
            for (var j = 0; j < contentMappers.Count; j++)
            {
                var mapper = contentMappers[j];
                var (manifest, packageDirectory, diagnostic) = ResolveContentMapperManifest(host, containingFile, mapper.Package);
                mapper.PackageDirectory = packageDirectory;
                if (diagnostic is not null)
                {
                    errors.Add(SetContentMapperDiagnosticLocation(diagnostic, contentMapperSourceFile, GetContentMapperSyntax(contentMapperSourceFile, contentMapperIndices[j], "package")));
                    continue;
                }

                mapper.SetManifest(manifest);
                resolvedContentMappers.Add(mapper);
            }

            contentMappers = resolvedContentMappers;
            contentMapperExtensions = [.. FlatMap([.. contentMappers], mapper => mapper.Extensions)];
        }

        (string[] FileNames, int LiteralFileNamesLen) GetFileNames(string basePathForNames)
        {
            var parsedConfigOptions = parsedConfig.Options;
            var (fileNames, literalFileNamesLen) = GetFileNamesFromConfigSpecs(configFileSpecs, basePathForNames, parsedConfigOptions, host.FS(), [.. contentMapperExtensions]);
            if (ShouldReportNoInputFiles(fileNames, CanJsonReportNoInputFiles(rawConfig), resolutionStack))
            {
                var includeSpecsForMessage = configFileSpecs.IncludeSpecs;
                var excludeSpecsForMessage = configFileSpecs.ExcludeSpecs;
                if (includeSpecsForMessage is null)
                {
                    includeSpecsForMessage = new object?[0];
                }

                if (excludeSpecsForMessage is null)
                {
                    excludeSpecsForMessage = new object?[0];
                }

                errors.Add(NewCompilerDiagnostic(No_inputs_were_found_in_config_file_0_Specified_include_paths_were_1_and_exclude_paths_were_2, configFileName, StringifyJson(includeSpecsForMessage, "", ""), StringifyJson(excludeSpecsForMessage, "", "")));
            }

            return (fileNames, literalFileNamesLen);
        }

        ProjectReference[] GetProjectReferences(string basePathForReferences)
        {
            ProjectReference[]? projectReferences = null;
            var newReferencesOfRaw = GetPropFromRaw("references", element => element is OrderedMap<string, object?>, "object");
            if (newReferencesOfRaw.SliceValue is not null)
            {
                var references = new List<ProjectReference>();
                for (var index = 0; index < newReferencesOfRaw.SliceValue.Length; index++)
                {
                    var reference = newReferencesOfRaw.SliceValue[index];
                    var reference0 = ParseProjectReference(reference);
                    if (reference0 is null)
                    {
                        continue;
                    }

                    if (!reference0.HasPath || !reference0.PathValid)
                    {
                        errors.Add(CreateDiagnosticAtProjectReferenceProperty(sourceFile, index, "path", Compiler_option_0_requires_a_value_of_type_1, "reference.path", "string"));
                        continue;
                    }

                    if (reference0.Reference.Path == "")
                    {
                        errors.Add(CreateDiagnosticAtProjectReferenceProperty(sourceFile, index, "path", Compiler_option_0_cannot_be_given_an_empty_string, "reference.path"));
                        continue;
                    }

                    if (reference0.HasCircular && !reference0.CircularValid)
                    {
                        errors.Add(CreateDiagnosticAtProjectReferenceProperty(sourceFile, index, "circular", Compiler_option_0_requires_a_value_of_type_1, "reference.circular", "boolean"));
                    }

                    references.Add(new ProjectReference
                    {
                        Path = GetNormalizedAbsolutePath(reference0.Reference.Path, basePathForReferences),
                        OriginalPath = reference0.Reference.Path,
                        Circular = reference0.Reference.Circular,
                    });
                }

                projectReferences = [.. references];
            }

            return projectReferences ?? [];
        }

        var (fileNames0, literalFileNamesLen0) = GetFileNames(basePathForFileNames);
        // Go: `compileOnSave := new(false)` —— 恒为非 nil 指针（值为 false），配置里写了才覆盖。
        bool? compileOnSave = false;
        if (parsedConfig.Raw is OrderedMap<string, object?> raw0 && raw0.GetOrZero("compileOnSave") is bool compileOnSaveValue)
        {
            compileOnSave = compileOnSaveValue;
        }

        return new ParsedCommandLine
        {
            ParsedConfig = new ParsedOptions
            {
                CompilerOptions = parsedConfig.Options,
                TypeAcquisition = parsedConfig.TypeAcquisition,
                FileNames = fileNames0,
                ProjectReferences = GetProjectReferences(basePathForFileNames),
                ContentMappers = [.. contentMappers],
            },
            ConfigFile = sourceFile,
            Raw = parsedConfig.Raw,
            Errors = [.. errors],
            CompileOnSave = compileOnSave,
            _comparePathsOptions = new ComparePathsOptions(
                useCaseSensitiveFileNames: host.FS().UseCaseSensitiveFileNames(),
                currentDirectory: basePathForFileNames),
            _literalFileNamesLen = literalFileNamesLen0,
        };
    }

    // Go: func canJsonReportNoInputFiles(rawConfig *collections.OrderedMap[string, any]) bool
    internal static bool CanJsonReportNoInputFiles(OrderedMap<string, object?> rawConfig)
    {
        var filesExists = rawConfig.Has("files");
        var referencesExists = rawConfig.Has("references");
        return !filesExists && !referencesExists;
    }

    // Go: func shouldReportNoInputFiles(fileNames []string, canJsonReportNoInputFiles bool, resolutionStack []tspath.Path) bool
    internal static bool ShouldReportNoInputFiles(string[] fileNames, bool canJsonReportNoInputFiles, TPath[]? resolutionStack) =>
        fileNames.Length == 0 && canJsonReportNoInputFiles && (resolutionStack?.Length ?? 0) == 0;

    // Go: func validateSpecs(specs any, disallowTrailingRecursion bool, jsonSourceFile *ast.SourceFile, specKey string) ([]string, []*ast.Diagnostic)
    internal static (string[] Specs, Diagnostic[] Errors) ValidateSpecs(
        object?[] specs,
        bool disallowTrailingRecursion,
        SourceFile? jsonSourceFile,
        string specKey)
    {
        Diagnostic CreateDiagnostic(Message message, string spec)
        {
            var element = GetTsConfigPropArrayElementValue(jsonSourceFile, specKey, spec);
            Node? node = null;
            if (element is not null)
            {
                node = element;
            }

            return CreateDiagnosticForNodeInSourceFileOrCompilerDiagnostic(jsonSourceFile, node, message, spec);
        }

        var errors = new List<Diagnostic>();
        var finalSpecs = new List<string>();
        foreach (var value in specs)
        {
            if (value is not string spec)
            {
                continue;
            }

            var diag = SpecToDiagnostic(spec, disallowTrailingRecursion);
            if (diag is not null)
            {
                errors.Add(CreateDiagnostic(diag, spec));
            }
            else
            {
                finalSpecs.Add(spec);
            }
        }

        return ([.. finalSpecs], [.. errors]);
    }

    // Go: func specToDiagnostic(spec string, disallowTrailingRecursion bool) *diagnostics.Message
    internal static Message? SpecToDiagnostic(string spec, bool disallowTrailingRecursion)
    {
        if (disallowTrailingRecursion && InvalidTrailingRecursion(spec))
        {
            return File_specification_cannot_end_in_a_recursive_directory_wildcard_Asterisk_Asterisk_Colon_0;
        }

        if (InvalidDotDotAfterRecursiveWildcard(spec))
        {
            return File_specification_cannot_contain_a_parent_directory_that_appears_after_a_recursive_directory_wildcard_Asterisk_Asterisk_Colon_0;
        }

        return null;
    }

    // Go: func invalidTrailingRecursion(spec string) bool
    internal static bool InvalidTrailingRecursion(string spec)
    {
        // Matches **, /**, **/, and /**/, but not a**b.
        // Strip optional trailing slash, then check if it ends with /** or is just **
        var s = spec.EndsWith('/') ? spec[..^1] : spec;
        return s == "**" || s.EndsWith("/**", StringComparison.Ordinal);
    }

    // Go: func invalidDotDotAfterRecursiveWildcard(s string) bool
    internal static bool InvalidDotDotAfterRecursiveWildcard(string s)
    {
        // We used to use the regex /(^|\/)\*\*\/(.*\/)?\.\.($|\/)/ to check for this case, but
        // in v8, that has polynomial performance because the recursive wildcard match - **/ -
        // can be matched in many arbitrary positions when multiple are present, resulting
        // in bad backtracking (and we don't care which is matched - just that some /.. segment
        // comes after some **/ segment).
        int wildcardIndex;
        if (s.StartsWith("**/", StringComparison.Ordinal))
        {
            wildcardIndex = 0;
        }
        else
        {
            wildcardIndex = s.IndexOf("/**/", StringComparison.Ordinal);
        }

        if (wildcardIndex == -1)
        {
            return false;
        }

        int lastDotIndex;
        if (s.EndsWith("/..", StringComparison.Ordinal))
        {
            lastDotIndex = s.Length;
        }
        else
        {
            lastDotIndex = s.LastIndexOf("/../", StringComparison.Ordinal);
        }

        return lastDotIndex > wildcardIndex;
    }

    // Go: func GetTsConfigPropArrayElementValue(tsConfigSourceFile *ast.SourceFile, propKey string, elementValue string) *ast.StringLiteral
    public static StringLiteral? GetTsConfigPropArrayElementValue(SourceFile? tsConfigSourceFile, string propKey, string elementValue)
    {
        var callback = GetCallbackForFindingPropertyAssignmentByValue(elementValue);
        return ForEachTsConfigPropArray(tsConfigSourceFile, propKey, property =>
        {
            var value = callback(property);
            return value?.AsStringLiteral();
        });
    }

    // Go: func ForEachTsConfigPropArray[T any](tsConfigSourceFile *ast.SourceFile, propKey string, callback func(property *ast.PropertyAssignment) *T) *T
    public static T? ForEachTsConfigPropArray<T>(SourceFile? tsConfigSourceFile, string propKey, Func<PropertyAssignment, T?> callback)
        where T : class
    {
        if (tsConfigSourceFile is not null)
        {
            return ForEachPropertyAssignment(GetTsConfigObjectLiteralExpression(tsConfigSourceFile), propKey, callback);
        }

        return null;
    }

    // Go: func CreateDiagnosticAtReferenceSyntax(config *ParsedCommandLine, index int, message *diagnostics.Message, args ...any) *ast.Diagnostic
    public static Diagnostic CreateDiagnosticAtReferenceSyntax(ParsedCommandLine config, int index, Message message, params object?[] args) =>
        ForEachTsConfigPropArray(config.ConfigFile!.SourceFile, "references", property =>
        {
            if (property.Initializer is { } initializer && IsArrayLiteralExpression(initializer))
            {
                // IsArrayLiteralExpression 成立后 ElementList() 必定非 nil（Go 同样直接解引用）。
                var value = initializer.Elements()!;
                if (value.Length > index)
                {
                    return CreateDiagnosticForNodeInSourceFile(config.ConfigFile.SourceFile, value[index], message, args);
                }
            }

            return (Diagnostic?)null;
        })!;

    // Go: func createDiagnosticAtProjectReferenceProperty(sourceFile *TsConfigSourceFile, index int, propertyName string, message *diagnostics.Message, args ...any) *ast.Diagnostic
    internal static Diagnostic CreateDiagnosticAtProjectReferenceProperty(
        TsConfigSourceFile? sourceFile,
        int index,
        string propertyName,
        Message message,
        params object?[] args)
    {
        Node? node = null;
        if (sourceFile is not null)
        {
            node = ForEachTsConfigPropArray(sourceFile.SourceFile, "references", property =>
            {
                if (property.Initializer is { } initializer && IsArrayLiteralExpression(initializer))
                {
                    // IsArrayLiteralExpression 成立后 ElementList() 必定非 nil（Go 同样直接解引用）。
                    var elements = initializer.Elements()!;
                    if (elements.Length > index && IsObjectLiteralExpression(elements[index]))
                    {
                        if (ForEachPropertyAssignment(elements[index].AsObjectLiteralExpression()!, propertyName, innerProperty => innerProperty.Initializer) is { } propertyNode)
                        {
                            return propertyNode;
                        }

                        return elements[index];
                    }
                }

                return (Node?)null;
            });
        }

        return CreateDiagnosticForNodeInSourceFileOrCompilerDiagnostic(TsconfigToSourceFile(sourceFile), node, message, args);
    }

    // Go: func GetCallbackForFindingPropertyAssignmentByValue(value string) func(property *ast.PropertyAssignment) *ast.Node
    public static Func<PropertyAssignment, Node?> GetCallbackForFindingPropertyAssignmentByValue(string value) =>
        property =>
        {
            if (property.Initializer is { } initializer && IsArrayLiteralExpression(initializer))
            {
                // Find 对 null slice 返回 default（与 Go 的 core.Find 一致），故无需 `!`。
                return Find(initializer.Elements(), element => IsStringLiteral(element) && element.Text() == value);
            }

            return null;
        };

    // Go: func GetOptionsSyntaxByArrayElementValue(objectLiteral *ast.ObjectLiteralExpression, propKey string, elementValue string) *ast.Node
    public static Node? GetOptionsSyntaxByArrayElementValue(ObjectLiteralExpression objectLiteral, string propKey, string elementValue) =>
        ForEachPropertyAssignment(objectLiteral, propKey, GetCallbackForFindingPropertyAssignmentByValue(elementValue));

    // getContentMapperSyntax returns the tsconfig JSON node to attribute a diagnostic about the content
    // mapper at index to: the value of subKey within that mapper's object (when subKey is non-empty),
    // falling back to the mapper element, then to the "contentMappers" array. An index outside the array
    // (e.g. -1) yields the array itself. Returns nil when there is no source file (JSON API).
    // Go: func getContentMapperSyntax(sourceFile *ast.SourceFile, index int, subKey string) *ast.Node
    internal static Node? GetContentMapperSyntax(SourceFile? sourceFile, int index, string subKey)
    {
        if (sourceFile is null)
        {
            return null;
        }

        return ForEachTsConfigPropArray(sourceFile, "contentMappers", property =>
        {
            if (property.Initializer is not { } initializer || !IsArrayLiteralExpression(initializer))
            {
                return property.Initializer;
            }

            // IsArrayLiteralExpression 成立后 ElementList() 必定非 nil（Go 同样直接解引用）。
            var elements = initializer.Elements()!;
            if (index < 0 || index >= elements.Length)
            {
                return property.Initializer;
            }

            var element = elements[index];
            if (subKey != "" && IsObjectLiteralExpression(element))
            {
                if (ForEachPropertyAssignment(element.AsObjectLiteralExpression()!, subKey, innerProperty => innerProperty.Initializer) is { } node)
                {
                    return node;
                }
            }

            return element;
        });
    }

    // Go: func GetContentMapperOptionDiagnosticLocation(config *ParsedCommandLine, mapper *contentmapper.Mapper, path []contentmapper.OptionPathSegment) (*ast.SourceFile, core.TextRange)
    public static (SourceFile? File, TextRange Range) GetContentMapperOptionDiagnosticLocation(
        ParsedCommandLine? config,
        Mapper mapper,
        OptionPathSegment[] path)
    {
        if (config?.ConfigFile is null)
        {
            return (null, UndefinedTextRange());
        }

        // Go: slices.Index(config.ContentMappers(), mapper)——Go 对 nil slice 返回 -1，C# 的 Array.IndexOf
        // 对 null 会抛，故先补空数组（ContentMappers() 在 JSON API 路径下为 nil）。
        var index = Array.IndexOf(config.ContentMappers() ?? [], mapper);
        var mapperNode = GetContentMapperSyntax(config.ConfigFile.SourceFile, index, "");
        var node = GetContentMapperSyntax(config.ConfigFile.SourceFile, index, "options");
        if (node is null)
        {
            node = mapperNode;
        }

        foreach (var segment in path)
        {
            Node? next = null;
            if (segment.IsIndex && node is not null && IsArrayLiteralExpression(node))
            {
                // IsArrayLiteralExpression 成立后 ElementList() 必定非 nil（Go 同样直接解引用）。
                var elements = node.Elements()!;
                if (segment.Index < elements.Length)
                {
                    next = elements[segment.Index];
                }
            }
            else if (!segment.IsIndex && node is not null && IsObjectLiteralExpression(node))
            {
                next = ForEachPropertyAssignment(node.AsObjectLiteralExpression()!, segment.Property, property => property.Initializer);
            }

            if (next is null)
            {
                break;
            }

            node = next;
        }

        if (node is null)
        {
            return (null, UndefinedTextRange());
        }

        var file = config.ConfigFile.SourceFile;
        return (file, NewTextRange(TypeScript.Scanner.Scanner.SkipTrivia(file.Text(), node.Pos()), node.End()));
    }

    // getContentMappersKeySyntax returns the "contentMappers" property key node, used to attribute a
    // diagnostic about the setting as a whole rather than a specific mapper.
    // Go: func getContentMappersKeySyntax(sourceFile *ast.SourceFile) *ast.Node
    private static Node? GetContentMappersKeySyntax(SourceFile? sourceFile)
    {
        if (sourceFile is null)
        {
            return null;
        }

        return ForEachTsConfigPropArray(sourceFile, "contentMappers", property => property.Name());
    }

    // getContentMapperExtensionSyntax returns the node for a specific extension string within the content
    // mapper at index, falling back to the "extensions" array or the mapper element.
    // Go: func getContentMapperExtensionSyntax(sourceFile *ast.SourceFile, index int, ext string) *ast.Node
    private static Node? GetContentMapperExtensionSyntax(SourceFile? sourceFile, int index, string ext)
    {
        var node = GetContentMapperSyntax(sourceFile, index, "extensions");
        if (node is not null && IsArrayLiteralExpression(node))
        {
            if (Find(node.Elements(), element => IsStringLiteral(element) && element.Text() == ext) is { } element)
            {
                return element;
            }
        }

        return node;
    }

    // setContentMapperDiagnosticLocation attaches a source location to a content mapper diagnostic when a
    // tsconfig source file and node are available (the jsonSourceFile API), leaving it as a location-less
    // compiler diagnostic otherwise (the JSON API).
    // Go: func setContentMapperDiagnosticLocation(diagnostic *ast.Diagnostic, sourceFile *ast.SourceFile, node *ast.Node) *ast.Diagnostic
    private static Diagnostic SetContentMapperDiagnosticLocation(Diagnostic diagnostic, SourceFile? sourceFile, Node? node)
    {
        if (sourceFile is not null && node is not null)
        {
            diagnostic.SetFile(sourceFile);
            diagnostic.SetLocation(NewTextRange(TypeScript.Scanner.Scanner.SkipTrivia(sourceFile.Text(), node.Pos()), node.End()));
        }

        return diagnostic;
    }

    // Go: func ForEachPropertyAssignment[T any](objectLiteral *ast.ObjectLiteralExpression, key string, callback func(property *ast.PropertyAssignment) *T, key2 ...string) *T
    public static T? ForEachPropertyAssignment<T>(
        ObjectLiteralExpression? objectLiteral,
        string key,
        Func<PropertyAssignment, T?> callback,
        params string[] key2)
        where T : class
    {
        if (objectLiteral is not null)
        {
            // Go 的 Properties 是值字段、Nodes 是 nil 时 range 零次迭代 → `Properties!` + `?? []`。
            foreach (var property in objectLiteral.Properties!.Nodes ?? [])
            {
                if (!IsPropertyAssignment(property))
                {
                    continue;
                }

                if (TryGetTextOfPropertyName(property.Name(), out var propName))
                {
                    if (propName == key || (key2.Length > 0 && key2[0] == propName))
                    {
                        return callback(property.AsPropertyAssignment()!);
                    }
                }
            }
        }

        return null;
    }

    // Go: func getTsConfigObjectLiteralExpression(tsConfigSourceFile *ast.SourceFile) *ast.ObjectLiteralExpression
    private static ObjectLiteralExpression? GetTsConfigObjectLiteralExpression(SourceFile? tsConfigSourceFile)
    {
        if (tsConfigSourceFile is { } sourceFile && sourceFile.Statements is { Nodes.Length: > 0 })
        {
            if (sourceFile.Statements.Nodes[0].Expression() is { } expression && IsObjectLiteralExpression(expression))
            {
                return expression.AsObjectLiteralExpression();
            }
        }

        return null;
    }

    // Go: func getSubstitutedPathWithConfigDirTemplate(value string, basePath string) string
    private static string GetSubstitutedPathWithConfigDirTemplate(string value, string basePath)
    {
        var index = value.IndexOf(ConfigDirTemplate, StringComparison.Ordinal);
        var substituted = index >= 0
            ? string.Concat(value.AsSpan(0, index), "./", value.AsSpan(index + ConfigDirTemplate.Length))
            : value;
        return GetNormalizedAbsolutePath(substituted, basePath);
    }

    // Go: func getSubstitutedStringArrayWithConfigDirTemplate(list []string, basePath string) []string
    internal static string[]? GetSubstitutedStringArrayWithConfigDirTemplate(string[]? list, string basePath)
    {
        // Go 的 `range` 遍历 nil 切片合法（零次迭代）→ nil 进、nil 出。
        // 实参可能是 `compilerOptions.Paths` 里值为 nil 的条目：parseStringMap/ParseStringArray 对
        // `"paths": { "*": "*" }` 这种非数组值返回 nil（Go: parsinghelpers.go:47-56 的 parseStringMap）。
        if (list is null)
        {
            return null;
        }

        string[]? result = null;
        for (var i = 0; i < list.Length; i++)
        {
            var element = list[i];
            if (StartsWithConfigDirTemplate(element))
            {
                result ??= (string[])list.Clone();
                result[i] = GetSubstitutedPathWithConfigDirTemplate(element, basePath);
            }
        }

        return result;
    }

    // Go: func handleOptionConfigDirTemplateSubstitution(compilerOptions *core.CompilerOptions, basePath string)
    internal static void HandleOptionConfigDirTemplateSubstitution(CompilerOptions? compilerOptions, string basePath)
    {
        if (compilerOptions is null)
        {
            return;
        }

        // !!! don't hardcode this; use options declarations?

        OrderedMap<string, string[]>? paths = null;
        if (compilerOptions.Paths is not null)
        {
            foreach (var (k, v) in compilerOptions.Paths.Entries())
            {
                // Go: `for k, v := range compilerOptions.Paths.Entries()` —— v 可为 nil 切片（见 parseStringMap）。
                if (GetSubstitutedStringArrayWithConfigDirTemplate(v, basePath) is { } substitution)
                {
                    if (paths is null)
                    {
                        paths = compilerOptions.Paths.Clone();
                        compilerOptions.Paths = paths;
                    }

                    paths.Set(k, substitution);
                }
            }
        }

        if (GetSubstitutedStringArrayWithConfigDirTemplate(compilerOptions.RootDirs ?? [], basePath) is { } rootDirs)
        {
            compilerOptions.RootDirs = rootDirs;
        }

        if (GetSubstitutedStringArrayWithConfigDirTemplate(compilerOptions.TypeRoots ?? [], basePath) is { } typeRoots)
        {
            compilerOptions.TypeRoots = typeRoots;
        }

        if (StartsWithConfigDirTemplate(compilerOptions.GenerateCpuProfile))
        {
            compilerOptions.GenerateCpuProfile = GetSubstitutedPathWithConfigDirTemplate(compilerOptions.GenerateCpuProfile, basePath);
        }

        if (StartsWithConfigDirTemplate(compilerOptions.GenerateTrace))
        {
            compilerOptions.GenerateTrace = GetSubstitutedPathWithConfigDirTemplate(compilerOptions.GenerateTrace, basePath);
        }

        if (StartsWithConfigDirTemplate(compilerOptions.OutFile))
        {
            compilerOptions.OutFile = GetSubstitutedPathWithConfigDirTemplate(compilerOptions.OutFile, basePath);
        }

        if (StartsWithConfigDirTemplate(compilerOptions.OutDir))
        {
            compilerOptions.OutDir = GetSubstitutedPathWithConfigDirTemplate(compilerOptions.OutDir, basePath);
        }

        if (StartsWithConfigDirTemplate(compilerOptions.RootDir))
        {
            compilerOptions.RootDir = GetSubstitutedPathWithConfigDirTemplate(compilerOptions.RootDir, basePath);
        }

        if (StartsWithConfigDirTemplate(compilerOptions.TsBuildInfoFile))
        {
            compilerOptions.TsBuildInfoFile = GetSubstitutedPathWithConfigDirTemplate(compilerOptions.TsBuildInfoFile, basePath);
        }

        if (StartsWithConfigDirTemplate(compilerOptions.BaseUrl))
        {
            compilerOptions.BaseUrl = GetSubstitutedPathWithConfigDirTemplate(compilerOptions.BaseUrl, basePath);
        }

        if (StartsWithConfigDirTemplate(compilerOptions.DeclarationDir))
        {
            compilerOptions.DeclarationDir = GetSubstitutedPathWithConfigDirTemplate(compilerOptions.DeclarationDir, basePath);
        }
    }

    // hasFileWithHigherPriorityExtension determines whether a literal or wildcard file has already been included that has a higher extension priority.
    // file is the path to the file.
    // Go: func hasFileWithHigherPriorityExtension(file string, extensions [][]string, hasFile func(fileName string) bool) bool
    private static bool HasFileWithHigherPriorityExtension(string file, string[][] extensions, Func<string, bool> hasFile)
    {
        List<string>? extensionGroup = null;
        foreach (var group in extensions)
        {
            if (FileExtensionIsOneOf(file, group))
            {
                extensionGroup ??= [];
                extensionGroup.AddRange(group);
            }
        }

        if (extensionGroup is null /* Go 里是 len(extensionGroup) == 0 */)
        {
            return false;
        }

        foreach (var ext in extensionGroup)
        {
            // d.ts files match with .ts extension and with case sensitive sorting the file order for same files with ts tsx and dts extension is
            // d.ts, .ts, .tsx in that order so we need to handle tsx and dts of same same name case here and in remove files with same extensions
            // So dont match .d.ts files with .ts extension
            if (FileExtensionIs(file, ext) && (ext != ExtensionTs || !FileExtensionIs(file, ExtensionDts)))
            {
                return false;
            }

            if (hasFile(ChangeExtension(file, ext)))
            {
                if (ext == ExtensionDts && (FileExtensionIs(file, ExtensionJs) || FileExtensionIs(file, ExtensionJsx)))
                {
                    // LEGACY BEHAVIOR: An off-by-one bug somewhere in the extension priority system for wildcard module loading allowed declaration
                    // files to be loaded alongside their js(x) counterparts. We regard this as generally undesirable, but retain the behavior to
                    // prevent breakage.
                    continue;
                }

                return true;
            }
        }

        return false;
    }

    // Removes files included via wildcard expansion with a lower extension priority that have already been included.
    // file is the path to the file.
    // Go: func removeWildcardFilesWithLowerPriorityExtension(file string, wildcardFiles *collections.OrderedMap[string, string], extensions [][]string, keyMapper func(value string) string)
    private static void RemoveWildcardFilesWithLowerPriorityExtension(
        string file,
        OrderedMap<string, string> wildcardFiles,
        string[][] extensions,
        Func<string, string> keyMapper)
    {
        List<string>? extensionGroup = null;
        foreach (var group in extensions)
        {
            if (FileExtensionIsOneOf(file, group))
            {
                extensionGroup ??= [];
                extensionGroup.AddRange(group);
            }
        }

        if (extensionGroup is null /* Go: extensionGroup == nil */)
        {
            return;
        }

        for (var i = extensionGroup.Count - 1; i >= 0; i--) // Go: slices.Backward
        {
            var ext = extensionGroup[i];
            if (FileExtensionIs(file, ext))
            {
                return;
            }

            var lowerPriorityPath = keyMapper(ChangeExtension(file, ext));
            wildcardFiles.Delete(lowerPriorityPath);
        }
    }

    // getFileNamesFromConfigSpecs gets the file names from the provided config file specs that contain, files, include, exclude and
    // other properties needed to resolve the file names
    // configFileSpecs is the config file specs extracted with file names to include, wildcards to include/exclude and other details
    // basePath is the base path for any relative file specifications.
    // options is the Compiler options.
    // host is the host used to resolve files and directories.
    // extraExtensions are additional file extensions (e.g. from content mappers) to treat as supported.
    // Go: func getFileNamesFromConfigSpecs(configFileSpecs configFileSpecs, basePath string, options *core.CompilerOptions, host vfs.FS, extraExtensions []string) ([]string, int)
    internal static (string[] FileNames, int LiteralFileNamesLen) GetFileNamesFromConfigSpecs(
        TsConfigFileSpecs configFileSpecs,
        string basePath, // considering this is the current directory
        CompilerOptions? options,
        FS host,
        string[]? extraExtensions = null)
    {
        basePath = NormalizePath(basePath);
        string KeyMapper(string value) => GetCanonicalFileName(value, host.UseCaseSensitiveFileNames());

        // Literal file names (provided via the "files" array in tsconfig.json) are stored in a
        // file map with a possibly case insensitive key. We use this map later when when including
        // wildcard paths.
        var literalFileMap = new OrderedMap<string, string>();

        // Wildcard paths (provided via the "includes" array in tsconfig.json) are stored in a
        // file map with a possibly case insensitive key. We use this map to store paths matched
        // via wildcard, and to handle extension priority.
        var wildcardFileMap = new OrderedMap<string, string>();

        // Wildcard paths of json files (provided via the "includes" array in tsconfig.json) are stored in a
        // file map with a possibly case insensitive key. We use this map to store paths matched
        // via wildcard of *.json kind
        var wildCardJsonFileMap = new OrderedMap<string, string>();
        var validatedFilesSpec = configFileSpecs.ValidatedFilesSpec;
        var validatedIncludeSpecs = configFileSpecs.ValidatedIncludeSpecs;
        var validatedExcludeSpecs = configFileSpecs.ValidatedExcludeSpecs;

        // Rather than re-query this for each file and filespec, we query the supported extensions
        // once and store it on the expansion context.
        var supportedExtensions = GetSupportedExtensions(options, extraExtensions ?? []);
        var supportedExtensionsWithJsonIfResolveJsonModule = GetSupportedExtensionsWithJsonIfResolveJsonModule(options, supportedExtensions);

        // Literal files are always included verbatim. An "include" or "exclude" specification cannot
        // remove a literal file.
        foreach (var fileName in validatedFilesSpec)
        {
            var file = GetNormalizedAbsolutePath(fileName, basePath);
            literalFileMap.Set(KeyMapper(fileName), file);
        }

        SpecMatcher? jsonOnlyIncludeMatchers = null;
        if (validatedIncludeSpecs.Length > 0)
        {
            var files = ReadDirectory(host, basePath, basePath, Flatten(supportedExtensionsWithJsonIfResolveJsonModule), validatedExcludeSpecs, validatedIncludeSpecs, UnlimitedDepth);
            foreach (var file in files)
            {
                if (FileExtensionIs(file, ExtensionJson))
                {
                    if (jsonOnlyIncludeMatchers is null)
                    {
                        var includes = Filter(validatedIncludeSpecs, include => include.EndsWith(ExtensionJson, StringComparison.Ordinal));
                        jsonOnlyIncludeMatchers = NewSpecMatcher(includes, basePath, Usage.Files, host.UseCaseSensitiveFileNames());
                    }

                    var includeIndex = -1;
                    if (jsonOnlyIncludeMatchers is not null)
                    {
                        includeIndex = jsonOnlyIncludeMatchers.MatchIndex(file);
                    }

                    if (includeIndex != -1)
                    {
                        var key = KeyMapper(file);
                        if (!literalFileMap.Has(key) && !wildCardJsonFileMap.Has(key))
                        {
                            wildCardJsonFileMap.Set(key, file);
                        }
                    }

                    continue;
                }

                // If we have already included a literal or wildcard path with a
                // higher priority extension, we should skip this file.
                //
                // This handles cases where we may encounter both <file>.ts and
                // <file>.d.ts (or <file>.js if "allowJs" is enabled) in the same
                // directory when they are compilation outputs.
                if (HasFileWithHigherPriorityExtension(file, supportedExtensions, fileName =>
                {
                    var canonicalFileName = KeyMapper(fileName);
                    return literalFileMap.Has(canonicalFileName) || wildcardFileMap.Has(canonicalFileName);
                }))
                {
                    continue;
                }

                // We may have included a wildcard path with a lower priority
                // extension due to the user-defined order of entries in the
                // "include" array. If there is a lower priority extension in the
                // same directory, we should remove it.
                RemoveWildcardFilesWithLowerPriorityExtension(file, wildcardFileMap, supportedExtensions, KeyMapper);
                var key0 = KeyMapper(file);
                if (!literalFileMap.Has(key0) && !wildcardFileMap.Has(key0))
                {
                    wildcardFileMap.Set(key0, file);
                }
            }
        }

        var result = new List<string>(literalFileMap.Size() + wildcardFileMap.Size() + wildCardJsonFileMap.Size());
        foreach (var file in literalFileMap.Values())
        {
            result.Add(file!);
        }

        foreach (var file in wildcardFileMap.Values())
        {
            result.Add(file!);
        }

        foreach (var file in wildCardJsonFileMap.Values())
        {
            result.Add(file!);
        }

        return ([.. result], literalFileMap.Size());
    }

    // Go: func GetSupportedExtensions(compilerOptions *core.CompilerOptions, extraExtensions []string) [][]string
    public static string[][] GetSupportedExtensions(CompilerOptions? compilerOptions, string[] extraExtensions)
    {
        var needJSExtensions = compilerOptions?.GetAllowJS() ?? false;
        string[][] builtins;
        if (needJSExtensions)
        {
            builtins = AllSupportedExtensions;
        }
        else
        {
            builtins = SupportedTSExtensions;
        }

        if (extraExtensions.Length == 0)
        {
            return builtins;
        }

        var flatBuiltins = Flatten(builtins);
        List<string[]>? result = null;
        foreach (var ext in extraExtensions)
        {
            if (!Some(flatBuiltins, builtin => builtin == ext))
            {
                result ??= [];
                result.Add([ext]);
            }
        }

        if (result is null)
        {
            return builtins;
        }

        return [.. builtins, .. result];
    }

    // Go: func GetSupportedExtensionsWithJsonIfResolveJsonModule(compilerOptions *core.CompilerOptions, supportedExtensions [][]string) [][]string
    public static string[][] GetSupportedExtensionsWithJsonIfResolveJsonModule(CompilerOptions? compilerOptions, string[][] supportedExtensions)
    {
        if (compilerOptions is null || !compilerOptions.GetResolveJsonModule())
        {
            return supportedExtensions;
        }

        // TypeScript.Core.Same：`Core.` 前缀会被解析成命名空间 TypeScript.Core（docs/architecture.md §2），
        // 已有 `using static TypeScript.Core.Core;`，故用裸名。
        if (Same(supportedExtensions, AllSupportedExtensions))
        {
            return AllSupportedExtensionsWithJson;
        }

        if (Same(supportedExtensions, SupportedTSExtensions))
        {
            return SupportedTSExtensionsWithJson;
        }

        return [.. supportedExtensions, [ExtensionJson]];
    }

    // Reads the config file and reports errors.
    // Go: func GetParsedCommandLineOfConfigFile(configFileName string, options *core.CompilerOptions, optionsRaw *collections.OrderedMap[string, any], sys ParseConfigHost, extendedConfigCache ExtendedConfigCache) (*ParsedCommandLine, []*ast.Diagnostic)
    public static (ParsedCommandLine? CommandLine, Diagnostic[] Errors) GetParsedCommandLineOfConfigFile(
        string configFileName,
        CompilerOptions? options,
        OrderedMap<string, object?>? optionsRaw,
        ParseConfigHost sys,
        ExtendedConfigCache? extendedConfigCache)
    {
        configFileName = GetNormalizedAbsolutePath(configFileName, sys.GetCurrentDirectory());
        return GetParsedCommandLineOfConfigFilePath(
            configFileName,
            ToPath(configFileName, sys.GetCurrentDirectory(), sys.FS().UseCaseSensitiveFileNames()),
            options,
            optionsRaw,
            sys,
            extendedConfigCache);
    }

    // Go: func GetParsedCommandLineOfConfigFilePath(configFileName string, path tspath.Path, options *core.CompilerOptions, optionsRaw *collections.OrderedMap[string, any], sys ParseConfigHost, extendedConfigCache ExtendedConfigCache) (*ParsedCommandLine, []*ast.Diagnostic)
    public static (ParsedCommandLine? CommandLine, Diagnostic[] Errors) GetParsedCommandLineOfConfigFilePath(
        string configFileName,
        TPath path,
        CompilerOptions? options,
        OrderedMap<string, object?>? optionsRaw,
        ParseConfigHost sys,
        ExtendedConfigCache? extendedConfigCache)
    {
        Diagnostic[] errors = [];
        var (configFileText, readErrors) = TryReadFile(configFileName, ReadFileFunc(sys.FS()), errors);
        errors = readErrors;
        if (errors.Length > 0)
        {
            // these are unrecoverable errors--exit to report them as diagnostics
            return (null, errors);
        }

        var tsConfigSourceFile = NewTsconfigSourceFileFromFilePath(configFileName, path, configFileText);

        // tsConfigSourceFile.resolvedPath = tsConfigSourceFile.FileName()
        // tsConfigSourceFile.originalFileName = tsConfigSourceFile.FileName()
        return (ParseJsonSourceFileConfigFileContent(
            tsConfigSourceFile,
            sys,
            GetDirectoryPath(configFileName),
            options,
            optionsRaw,
            configFileName,
            null,
            extendedConfigCache), []);
    }

    // Go 的 `func(fileName string) (string, bool)` 读取回调：包装 FS.TryReadFile（docs §3.2 的 (T,bool) → out）。
    internal static Func<string, (string Text, bool Ok)> ReadFileFunc(FS fs) =>
        fileName =>
        {
            var ok = fs.TryReadFile(fileName, out var text);
            return (text, ok);
        };
}
