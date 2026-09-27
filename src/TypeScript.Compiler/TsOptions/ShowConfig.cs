// ported from: internal/tsoptions/showconfig.go
//
// 与 Go 的差异：
// - Go 用 reflect 遍历 `core.CompilerOptions` 的字段（serializeCompilerOptions/optionsHaveChanges）→
//   C# 用显式字段表 `CompilerOptionFields`（docs §3.6 禁反射，见 CompilerOptionFields.cs）。
// - Go 的 `computeFn[T any]`（把 `func(*CompilerOptions) T` 适配成 `func(*CompilerOptions) any`）→ C# 的
//   `Func<CompilerOptions, object?>` 直接接受返回具体类型的 lambda（隐式装箱）。
// - Go 的 `reflect.DeepEqual(implied, defaultVal)` → `Equals(implied, defaultVal)`（两边都是标量/枚举/bool，
//   值语义与 Go 的 DeepEqual 一致）。
// - `TSConfig` 在 Go 里靠 `json:"…,omitzero"` tag 序列化；C# 无反射 → 实现 `IJsonMarshaler` 手写同名
//   字段与 omitzero 判定（docs/json.md §4；`--showConfig` 逐字节对齐）。
// - `serializeEnumValue` 的 `reflect.Value.CanInt()` → `value.GetType().IsEnum` + `Convert.ToInt64`。
using System.Globalization;
using TypeScript.Collections;
using TypeScript.Core;
using TypeScript.Diagnostics;
using TypeScript.Json;
using TypeScript.TSPath;
using static TypeScript.Collections.Collections;
using static TypeScript.Core.Core;
using static TypeScript.Diagnostics.Diagnostics;
using static TypeScript.Json.Json;
using static TypeScript.TSPath.TSPath;
using static TypeScript.TsOptions.TsOptions;
using Diagnostic = TypeScript.Ast.Diagnostic;

namespace TypeScript.TsOptions;

// impliedOption describes a compiler option whose effective value can be derived from
// other options. This mirrors TypeScript's computedOptions concept used in convertToTSConfig.
// Go: type impliedOption struct
internal sealed class ImpliedOption
{
    /// <summary>name is the Go struct field name of the CompilerOptions field (e.g., "Module").</summary>
    public string Name = "";

    /// <summary>dependencies lists the Go struct field names that this option depends on.</summary>
    public string[] Dependencies = [];

    /// <summary>compute returns the effective value of this option given compiler options.</summary>
    public Func<CompilerOptions, object?> Compute = _ => null;
}

// TSConfig represents the output structure for --showConfig
// Go: type TSConfig struct（json tag：compilerOptions / references,omitzero / files,omitzero /
// include,omitzero / exclude,omitzero / compileOnSave,omitzero）
public sealed class TSConfig : IJsonMarshaler
{
    public OrderedMap<string, object?>? CompilerOptions;
    public object?[]? References;
    public string[]? Files;
    public string[]? Include;
    public string[]? Exclude;
    public bool? CompileOnSave;

    // Go 的 struct tag 序列化 → 手写 mapper（docs/json.md §4）。字段顺序照 Go 的结构体声明顺序。
    public void MarshalJSONTo(Encoder encoder)
    {
        encoder.WriteToken(BeginObject);
        encoder.WriteToken(JsonToken.String("compilerOptions"));
        MarshalEncode(encoder, CompilerOptions);
        if (References is not null)
        {
            encoder.WriteToken(JsonToken.String("references"));
            MarshalEncode(encoder, References);
        }

        if (Files is not null)
        {
            encoder.WriteToken(JsonToken.String("files"));
            MarshalEncode(encoder, Files);
        }

        if (Include is not null)
        {
            encoder.WriteToken(JsonToken.String("include"));
            MarshalEncode(encoder, Include);
        }

        if (Exclude is not null)
        {
            encoder.WriteToken(JsonToken.String("exclude"));
            MarshalEncode(encoder, Exclude);
        }

        if (CompileOnSave is { } compileOnSave)
        {
            encoder.WriteToken(JsonToken.String("compileOnSave"));
            MarshalEncode(encoder, compileOnSave);
        }

        encoder.WriteToken(EndObject);
    }
}

public static partial class TsOptions
{
    // impliedOptions lists the compiler options that may be implied by other options,
    // mirroring TypeScript's computedOptions used in convertToTSConfig.
    // Each compute function delegates directly to an existing core.CompilerOptions getter.
    private static readonly ImpliedOption[] _impliedOptions =
    [
        new() { Name = "Module", Dependencies = ["Target"], Compute = options => options.GetEmitModuleKind() },
        new() { Name = "ModuleResolution", Dependencies = ["Module", "Target"], Compute = options => options.GetModuleResolutionKind() },
        new() { Name = "ModuleDetection", Dependencies = ["Module", "Target"], Compute = options => options.GetEmitModuleDetectionKind() },
        new() { Name = "IsolatedModules", Dependencies = ["VerbatimModuleSyntax"], Compute = options => options.GetIsolatedModules() },
        new() { Name = "PreserveConstEnums", Dependencies = ["IsolatedModules", "VerbatimModuleSyntax"], Compute = options => options.ShouldPreserveConstEnums() },
        new() { Name = "Declaration", Dependencies = ["Composite"], Compute = options => options.GetEmitDeclarations() },
        new() { Name = "DeclarationMap", Dependencies = ["Declaration", "Composite"], Compute = options => options.GetAreDeclarationMapsEnabled() },
        new() { Name = "Incremental", Dependencies = ["Composite"], Compute = options => options.IsIncremental() },
        new() { Name = "UseDefineForClassFields", Dependencies = ["Target", "Module"], Compute = options => options.GetUseDefineForClassFields() },
        new() { Name = "ResolvePackageJsonExports", Dependencies = ["ModuleResolution", "Module", "Target"], Compute = options => options.GetResolvePackageJsonExports() },
        new() { Name = "ResolvePackageJsonImports", Dependencies = ["ModuleResolution", "ResolvePackageJsonExports", "Module", "Target"], Compute = options => options.GetResolvePackageJsonImports() },
        new() { Name = "ResolveJsonModule", Dependencies = ["ModuleResolution", "Module", "Target"], Compute = options => options.GetResolveJsonModule() },
        new() { Name = "AllowJs", Dependencies = ["CheckJs"], Compute = options => options.GetAllowJS() },
        new() { Name = "AllowImportingTsExtensions", Dependencies = ["RewriteRelativeImportExtensions"], Compute = options => options.GetAllowImportingTsExtensions() },
    ];

    // ConvertToTSConfig generates a complete tsconfig representation for --showConfig output,
    // matching the behavior of TypeScript's convertToTSConfig function.
    // Go: func ConvertToTSConfig(configParseResult *ParsedCommandLine, configFileName string) *TSConfig
    public static TSConfig ConvertToTSConfig(ParsedCommandLine configParseResult, string configFileName)
    {
        if (configFileName == "")
        {
            configFileName = "tsconfig.json";
        }

        var normalizedConfigPath = GetNormalizedAbsolutePath(configFileName, configParseResult.GetCurrentDirectory());
        var comparePathsOptions = new ComparePathsOptions(
            useCaseSensitiveFileNames: configParseResult.UseCaseSensitiveFileNames(),
            currentDirectory: configParseResult.GetCurrentDirectory());

        // Build the list of all resolved files as relative paths from the config file.
        var files = new List<string>();
        foreach (var f in configParseResult.FileNames())
        {
            var normalizedFilePath = GetNormalizedAbsolutePath(f, configParseResult.GetCurrentDirectory());
            var relativePath = GetRelativePathFromFile(normalizedConfigPath, normalizedFilePath, comparePathsOptions);
            files.Add(relativePath);
        }

        // Go 的 configParseResult.CompilerOptions() 返回 *core.CompilerOptions；ConstructParsedCommandLine 建立
        // 的 ParsedCommandLine 必定带 options（Go 侧同样直接解引用），这里承接一次避免重复 `!`。
        var compilerOptions = configParseResult.CompilerOptions()!;

        // Serialize compiler options
        var optionMap = SerializeCompilerOptions(compilerOptions, normalizedConfigPath, comparePathsOptions);

        // Remove command-line-only options from the output
        foreach (var name in new[]
        {
            "showConfig", "configFile", "configFilePath", "help", "init",
            "listFilesOnly", "listEmittedFiles", "project", "build", "version",
        })
        {
            optionMap.Delete(name);
        }

        // Add implied compiler options (options that are derived from explicitly set options,
        // such as moduleResolution implied by module, or useDefineForClassFields implied by target).
        // This mirrors TypeScript's convertToTSConfig computedOptions logic.
        AddImpliedOptions(optionMap, compilerOptions, normalizedConfigPath, comparePathsOptions);

        var config = new TSConfig
        {
            CompilerOptions = optionMap,
        };

        // Add references
        if (configParseResult.ProjectReferences() is { Length: > 0 } refs)
        {
            var references = new List<object?>();
            foreach (var r in refs)
            {
                var reference = new OrderedMap<string, object?>();
                reference.Set("path", r.OriginalPath);
                if (r.Circular)
                {
                    reference.Set("circular", true);
                }

                references.Add(reference);
            }

            config.References = [.. references];
        }

        // Add files
        if (files.Count > 0)
        {
            config.Files = [.. files];
        }

        // Add include/exclude from configFileSpecs
        if (configParseResult.ConfigFile?._configFileSpecs is { } specs)
        {
            var include = FilterSameAsDefaultInclude(specs.ValidatedIncludeSpecs);
            if (include is { Length: > 0 })
            {
                config.Include = include;
            }

            // Go 的 validatedExcludeSpecs 在无 exclude 规格时是 nil（omitzero 省略该键）；
            // C# 侧 ValidatedExcludeSpecs 是空数组，所以只有非空时才写出（否则会多出 "exclude": []，
            // 与 tsc --showConfig 的逐字节输出不符——运行时探针实测）。
            if (specs.ValidatedExcludeSpecs.Length > 0)
            {
                config.Exclude = specs.ValidatedExcludeSpecs;
            }
        }

        // Add compileOnSave
        if (configParseResult.CompileOnSave is true)
        {
            config.CompileOnSave = true;
        }

        return config;
    }

    // filterSameAsDefaultInclude returns nil if specs is the default include spec ["**/*"]
    // Go: func filterSameAsDefaultInclude(specs []string) []string
    internal static string[]? FilterSameAsDefaultInclude(string[] specs)
    {
        if (specs.Length == 0)
        {
            return null;
        }

        if (specs.Length == 1 && specs[0] == DefaultIncludeSpec)
        {
            return null;
        }

        return specs;
    }

    // getNameOfCompilerOptionValue returns the string key for a given enum value by
    // searching the option's enum map.
    // Go: func getNameOfCompilerOptionValue(value any, enumMap *collections.OrderedMap[string, any]) string
    private static string GetNameOfCompilerOptionValue(object? value, OrderedMap<string, object?> enumMap)
    {
        foreach (var (k, v) in enumMap.Entries())
        {
            if (Equals(v, value))
            {
                return k;
            }
        }

        return "";
    }

    // serializeCompilerOptions converts CompilerOptions to an ordered map with
    // string names as keys and serialized values (enums as strings, paths as
    // relative paths, etc.) matching the output of tsc --showConfig.
    // Go: func serializeCompilerOptions(options *core.CompilerOptions, configFilePath string, comparePathsOptions tspath.ComparePathsOptions) *collections.OrderedMap[string, any]
    internal static OrderedMap<string, object?> SerializeCompilerOptions(
        CompilerOptions options,
        string configFilePath,
        ComparePathsOptions comparePathsOptions)
    {
        var result = NewOrderedMapWithSizeHint<string, object?>(32);
        var configDir = GetDirectoryPath(configFilePath);

        // Go: reflect.ValueOf(options).Elem() 的字段遍历 → 显式字段表（顺序相同）。
        foreach (var field in CompilerOptionFields)
        {
            var optionDecl = CommandLineCompilerOptionsMap.Get(field.Name);
            if (optionDecl is null)
            {
                continue;
            }

            // Skip command-line-only and output formatting options
            if (ReferenceEquals(optionDecl.Category, Command_line_Options) || ReferenceEquals(optionDecl.Category, Output_Formatting))
            {
                continue;
            }

            // Skip zero values (unset options)
            if (field.IsZero(options))
            {
                continue;
            }

            var name = optionDecl.Name;
            object? value = field.Get(options);

            var enumMap = optionDecl.EnumMap();
            if (enumMap is not null)
            {
                // Enum option - convert numeric value to string name
                var serialized = SerializeEnumValue(value, enumMap);
                if (serialized != "")
                {
                    result.Set(name, serialized);
                }

                continue;
            }

            switch (optionDecl.Kind)
            {
                case CommandLineOptionKind.ListOrElement:
                    Debug.Fail("listOrElement option should not reach serialization");
                    break;
                case CommandLineOptionKind.List:
                    var elem = optionDecl.Elements();
                    if (elem is { IsFilePath: true } && value is string[] strs)
                    {
                        // List of file paths - make relative
                        var relPaths = new string[strs.Length];
                        for (var j = 0; j < strs.Length; j++)
                        {
                            var absPath = GetNormalizedAbsolutePath(strs[j], configDir);
                            relPaths[j] = GetRelativePathFromFile(configFilePath, absPath, comparePathsOptions);
                        }

                        result.Set(name, relPaths);
                        continue;
                    }

                    if (elem is not null && elem.EnumMap() is { } elemMap && value is string[] enumStrs)
                    {
                        // List of enum values (e.g., lib)
                        var serialized = new List<string>(enumStrs.Length);
                        foreach (var s in enumStrs)
                        {
                            // lib values are already stored as the d.ts filename, need to find original key
                            var found = GetNameOfCompilerOptionValue(s, elemMap);
                            serialized.Add(found != "" ? found : s);
                        }

                        result.Set(name, serialized.ToArray());
                        continue;
                    }

                    result.Set(name, value);
                    break;

                case CommandLineOptionKind.String:
                    if (optionDecl.IsFilePath && value is string s0 && s0 != "")
                    {
                        // File path option - make relative to config
                        var absPath = GetNormalizedAbsolutePath(s0, configDir);
                        result.Set(name, GetRelativePathFromFile(configFilePath, absPath, comparePathsOptions));
                        continue;
                    }

                    result.Set(name, value);
                    break;

                case CommandLineOptionKind.Boolean:
                    if (value is Tristate t)
                    {
                        if (t.IsTrue())
                        {
                            result.Set(name, true);
                        }
                        else if (t.IsFalse())
                        {
                            result.Set(name, false);
                        }
                    }
                    else
                    {
                        result.Set(name, value);
                    }

                    break;

                case CommandLineOptionKind.Number:
                    result.Set(name, value);
                    break;

                default:
                    result.Set(name, value);
                    break;
            }
        }

        return result;
    }

    // serializeEnumValue converts an enum field value to its corresponding string key
    // using the option's enum map. It handles int32-based enum types.
    // Go: func serializeEnumValue(value any, enumMap *collections.OrderedMap[string, any]) string
    internal static string SerializeEnumValue(object? value, OrderedMap<string, object?> enumMap)
    {
        // The enum maps store values as core.ModuleKind, core.ScriptTarget, etc.
        // But those are all int32 underneath. We need to compare by the underlying int32 value.
        if (value is not null && value.GetType().IsEnum)
        {
            var intVal = Convert.ToInt64(value, CultureInfo.InvariantCulture);
            foreach (var (k, v) in enumMap.Entries())
            {
                if (v is not null && v.GetType().IsEnum && Convert.ToInt64(v, CultureInfo.InvariantCulture) == intVal)
                {
                    return k;
                }
            }
        }

        // Fallback: direct comparison
        return GetNameOfCompilerOptionValue(value, enumMap);
    }

    // addImpliedOptions adds compiler options that are implied by other explicitly-set options,
    // mirroring TypeScript's convertToTSConfig behavior for computedOptions.
    // For example, when module: nodenext is set, moduleResolution: nodenext is implied.
    // Go: func addImpliedOptions(optionMap *collections.OrderedMap[string, any], options *core.CompilerOptions, _ string, _ tspath.ComparePathsOptions)
    internal static void AddImpliedOptions(
        OrderedMap<string, object?> optionMap,
        CompilerOptions options,
        string _,
        ComparePathsOptions __)
    {
        // Build the set of explicitly provided option JSON names (e.g., "module", "target").
        var provided = new HashSet<string>();
        foreach (var k in optionMap.Keys())
        {
            provided.Add(k);
        }

        var defaultOpts = new CompilerOptions();

        foreach (var entry in _impliedOptions)
        {
            // Get the option declaration for this implied option (using case-insensitive lookup).
            var optionDecl = CommandLineCompilerOptionsMap.Get(entry.Name);
            if (optionDecl is null)
            {
                continue;
            }

            // Skip if this option is already explicitly provided.
            if (provided.Contains(optionDecl.Name))
            {
                continue;
            }

            // Check if any direct dependency is in the provided set.
            // This mirrors TypeScript's optionDependsOn check.
            if (!AnyDependencyProvided(entry.Dependencies, provided))
            {
                continue;
            }

            // Compute the effective value with current options and the default value with empty options.
            var implied = entry.Compute(options);
            var defaultVal = entry.Compute(defaultOpts);

            // If the implied value equals the default, this option doesn't add useful information.
            if (Equals(implied, defaultVal))
            {
                continue;
            }

            // Serialize the implied value and add it to the option map.
            var serialized = SerializeImpliedOptionValue(optionDecl, implied);
            if (serialized is null)
            {
                continue;
            }

            optionMap.Set(optionDecl.Name, serialized);
        }
    }

    // anyDependencyProvided returns true if any of the given dependency names
    // (using Go field names like "Target") corresponds to an option in the provided set.
    // Go: func anyDependencyProvided(dependencies []string, provided map[string]bool) bool
    internal static bool AnyDependencyProvided(string[] dependencies, HashSet<string> provided)
    {
        foreach (var dep in dependencies)
        {
            var depDecl = CommandLineCompilerOptionsMap.Get(dep);
            if (depDecl is not null && provided.Contains(depDecl.Name))
            {
                return true;
            }
        }

        return false;
    }

    // serializeImpliedOptionValue converts a computed implied option value to its serializable form.
    // For enum options, it converts numeric values to their string names.
    // For boolean options, it returns the bool directly.
    // Go: func serializeImpliedOptionValue(optionDecl *CommandLineOption, value any) any
    internal static object? SerializeImpliedOptionValue(CommandLineOption optionDecl, object? value)
    {
        if (value is null)
        {
            return null;
        }

        var enumMap = optionDecl.EnumMap();
        if (enumMap is not null)
        {
            var s = SerializeEnumValue(value, enumMap);
            return s != "" ? s : null;
        }

        switch (value)
        {
            case bool b:
                return b;
            case Tristate t:
                if (t.IsTrue())
                {
                    return true;
                }

                if (t.IsFalse())
                {
                    return false;
                }

                return null;
        }

        return value;
    }
}
