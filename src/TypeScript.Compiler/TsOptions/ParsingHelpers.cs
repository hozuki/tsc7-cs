// ported from: internal/tsoptions/parsinghelpers.go
//
// 映射约定（docs/architecture.md §2/§3）：
// 1) Go 的 `any` → `object?`；`value == nil`/`value == true` 这类断言 → C# 的模式匹配（`value is Tristate`、
//    `value is true`）。Go 里"从 JSON 解出来的对象"统一是 `*collections.OrderedMap[string, any]` → C#
//    `OrderedMap<string, object?>`。
// 2) Go 里 `if arr, ok := value.([]any); ok { if arr == nil { return nil } … }` 的 `arr == nil` 分支在 C# 是死代码
//    （模式匹配成功即非 null）→ 已按 §3.2 删除。
// 3) `optionParser`（未导出接口）→ `IOptionParser`（C# 习惯加 I 前缀；`convertOptionsFromJson`/
//    `convertMapToOptions` 在 tsconfigparsing.go 分片里约束到这个接口）。四个 parser 结构体照 Go 内嵌字段的名字
//    （`CompilerOptions`/`WatchOptions`/`TypeAcquisition`/`BuildOptions`），并用显式构造器（C# 无字段提升）。
// 4) **改名**（C# 不能只按返回值重载）：Go 有导出的 `ParseCompilerOptions(key, value, allOptions) []*Diagnostic`
//    与未导出的 `parseCompilerOptions(key, value, allOptions) (foundKey bool)`；后者在 C# 叫 `ParseCompilerOptionsWorker`。
// 5) `floatOrInt32ToFlag[T ~int32](value any)` → `FloatOrInt32ToFlag<T>(object? value) where T : struct, Enum`：
//    值的来源只有两种——枚举字面量表里装箱好的枚举，或 JSON/命令行数字（float64）经 `(T)(object)(int)double` 转换。
// 6) `mergeCompilerOptions` 用 `reflect` 按 struct 字段顺序 + `json` tag 做"显式 null 覆盖 + 非零值合并"。C# 无反射
//    （§3.6 AOT），所以这里**逐字段展开**（顺序与 Go 的 struct 声明一致，json 名逐条照 Go 的 tag），
//    零值判定照 Go 的 `reflect.Value.IsZero`：Tristate → TSUnknown、string → ""、切片/指针 → nil、
//    枚举 → 0。生成脚本：artifacts/tsoptions/gen-merge.mjs（一次性，gitignore 内）。
// 7) `parseNumber` 的 `int` 分支保留（Go 里 JSON 数字是 float64，但命令行 `--checkers` 之类可能已经是 int）。
using System;
using System.Collections.Generic;
using TypeScript.Ast;
using TypeScript.Collections;
using TypeScript.ContentMapper;
using Diagnostic = TypeScript.Ast.Diagnostic; // Ast 与 ContentMapper 各有一个 Diagnostic（Go 里也是两个包）→ 别名消歧
using TypeScript.Core;
using TypeScript.Diagnostics;
using TypeScript.Json;
using static TypeScript.Collections.Collections;
using static TypeScript.Core.Core;
using static TypeScript.Diagnostics.Diagnostics;
using static TypeScript.StringUtil.StringUtil;
using static TypeScript.TSPath.TSPath;

namespace TypeScript.TsOptions;

public static partial class TsOptions
{
    public static Tristate ParseTristate(object? value)
    {
        if (value is null)
        {
            return Tristate.TSUnknown;
        }
        if (value is Tristate tristate)
        {
            return tristate;
        }
        if (value is true)
        {
            return Tristate.TSTrue;
        }
        else
        {
            return Tristate.TSFalse;
        }
    }

    public static string[]? ParseStringArray(object? value)
    {
        if (value is object?[] arr)
        {
            var result = new List<string>(arr.Length);
            foreach (var v in arr)
            {
                if (v is string str)
                {
                    result.Add(str);
                }
            }
            return [.. result];
        }
        return null;
    }

    internal static OrderedMap<string, string[]>? ParseStringMap(object? value)
    {
        if (value is OrderedMap<string, object?> m)
        {
            var result = NewOrderedMapWithSizeHint<string, string[]>(m.Size());
            foreach (var (key, entryValue) in m.Entries())
            {
                // Go 的 map 值类型 `[]string` 允许 nil（`ParseStringArray` 对非数组返回 nil），
                // C# 的 `OrderedMap<string, string[]>` 在注解层是非空，`Get` 读出的是 `string[]?`。
                result.Set(key, ParseStringArray(entryValue)!);
            }
            return result;
        }
        return null;
    }

    public static string ParseString(object? value) => value is string str ? str : "";

    internal static int? ParseNumber(object? value)
    {
        if (value is int num)
        {
            return num;
        }
        if (value is double d)
        {
            return (int)d;
        }
        return null;
    }

    internal sealed class ProjectReferenceParseResult
    {
        // 非公开字段必须是 _camelCase（IDE1006），但这些名字要与 tsconfigparsing.go 分片的读取点一致，
        // 且 Go 侧是可变的普通字段 → 落成 internal 属性（属性受 PascalCase 规则约束，名字与 Go 一致）。
        internal ProjectReference Reference { get; } = new();
        internal bool HasPath { get; set; }
        internal bool PathValid { get; set; }
        internal bool HasCircular { get; set; }
        internal bool CircularValid { get; set; }
    }

    internal static ProjectReferenceParseResult? ParseProjectReference(object? json)
    {
        if (json is OrderedMap<string, object?> v)
        {
            var result = new ProjectReferenceParseResult();
            var (value, hasPath) = v.Get("path");
            if (hasPath)
            {
                result.HasPath = true;
                if (value is string path)
                {
                    result.Reference.Path = path;
                    result.PathValid = true;
                }
            }
            var (circularValue, hasCircular) = v.Get("circular");
            if (hasCircular)
            {
                result.HasCircular = true;
                if (circularValue is bool circular)
                {
                    result.Reference.Circular = circular;
                    result.CircularValid = true;
                }
            }
            return result;
        }
        return null;
    }

    internal static (Mapper? Mapper, Diagnostic[] Errors) ParseContentMapper(object? value)
    {
        if (value is not OrderedMap<string, object?> v)
        {
            return (null, []);
        }
        var errors = new List<Diagnostic>();
        var mapper = new Mapper();
        var (pkg, hasPackage) = v.Get("package");
        if (hasPackage)
        {
            if (pkg is string str && str != "")
            {
                mapper.Package = str; // Go: mapper.Package（内嵌 Definition 的提升字段）
            }
            else
            {
                errors.Add(Ast.Ast.NewCompilerDiagnostic(Compiler_option_0_requires_a_value_of_type_1, "contentMapper.package", "string"));
            }
        }
        else
        {
            errors.Add(Ast.Ast.NewCompilerDiagnostic(Compiler_option_0_requires_a_value_of_type_1, "contentMapper.package", "string"));
        }
        var (extensions, hasExtensions) = v.Get("extensions");
        if (hasExtensions)
        {
            if (TryParseStringArrayStrict(extensions, out var strs))
            {
                mapper.Extensions = strs;
            }
            else
            {
                errors.Add(Ast.Ast.NewCompilerDiagnostic(Compiler_option_0_requires_a_value_of_type_1, "contentMapper.extensions", "string[]"));
            }
        }
        else
        {
            errors.Add(Ast.Ast.NewCompilerDiagnostic(Compiler_option_0_requires_a_value_of_type_1, "contentMapper.extensions", "string[]"));
        }
        var (options, hasOptions) = v.Get("options");
        if (hasOptions)
        {
            if (options is not OrderedMap<string, object?>)
            {
                errors.Add(Ast.Ast.NewCompilerDiagnostic(Compiler_option_0_requires_a_value_of_type_1, "contentMapper.options", "object"));
            }
            else
            {
                // Go: mapper.Options, _ = json.Marshal(options)（C# 的 Json.Marshal 不返回 error）
                mapper.Options = Json.Json.Marshal(options);
            }
        }
        if (errors.Count != 0)
        {
            return (null, [.. errors]);
        }
        return (mapper, [.. errors]);
    }

    // parseStringArrayStrict returns the string slice and true only if value is an array whose
    // elements are all strings. A missing element or wrong element type yields false.
    internal static bool TryParseStringArrayStrict(object? value, out string[] result)
    {
        if (value is not object?[] arr)
        {
            result = [];
            return false;
        }
        var list = new List<string>(arr.Length);
        foreach (var v in arr)
        {
            if (v is not string str)
            {
                result = [];
                return false;
            }
            list.Add(str);
        }
        result = [.. list];
        return true;
    }

    internal static OrderedMap<string, object?> ParseJsonToStringKey(object? json)
    {
        var result = NewOrderedMapWithSizeHint<string, object?>(6);
        if (json is OrderedMap<string, object?> m)
        {
            // Go 的写法是把"取到的值"原样搬过来（含 nil）；C# 的 `Get` 返回 (Value, Ok) 但值本身可以是 null，
            // 所以这里必须用 Ok 判断而不是值是否为 null。
            foreach (var key in new[] { "include", "exclude", "files", "references", "contentMappers", "extends", "compilerOptions", "excludes", "typeAcquisition" })
            {
                var (value, ok) = m.Get(key);
                if (!ok)
                {
                    continue;
                }
                if (key == "extends" && value is string str)
                {
                    result.Set("extends", new object?[] { str });
                }
                result.Set(key, value);
            }
        }
        return result;
    }

    internal static string TrimSpace(string value)
    {
        // Go: strings.TrimSpace（unicode.IsSpace；注意 .NET 的 char.IsWhiteSpace 还包含 U+001C–U+001F，
        // Go 的 unicode.IsSpace 不含）→ 这里按 Go 的集合逐个判断。
        var start = 0;
        var end = value.Length;
        while (start < end && IsGoSpace(value[start]))
        {
            start++;
        }
        while (end > start && IsGoSpace(value[end - 1]))
        {
            end--;
        }
        return value.AsSpan(start, end - start).ToString();
    }

    internal static string TrimWhiteSpaceLike(string value)
    {
        // Go: strings.TrimFunc(value, stringutil.IsWhiteSpaceLike)
        var start = 0;
        var end = value.Length;
        while (start < end && IsWhiteSpaceLike(value[start]))
        {
            start++;
        }
        while (end > start && IsWhiteSpaceLike(value[end - 1]))
        {
            end--;
        }
        return value.AsSpan(start, end - start).ToString();
    }

    // Go: unicode.IsSpace
    private static bool IsGoSpace(int ch) =>
        ch is '\t' or '\n' or '\v' or '\f' or '\r' or ' ' or 0x85 or 0xA0 or 0x1680 or 0x2028 or 0x2029 or 0x202F or 0x205F or 0x3000
        || (ch >= 0x2000 && ch <= 0x200A);
}

internal interface IOptionParser
{
    Diagnostic[] ParseOption(string key, object? value);
    Message? UnknownOptionDiagnostic();
    Message? UnknownDidYouMeanDiagnostic();
}

internal sealed class CompilerOptionsParser : IOptionParser
{
    internal CompilerOptions CompilerOptions { get; }

    internal CompilerOptionsParser(CompilerOptions compilerOptions) => CompilerOptions = compilerOptions;

    public Diagnostic[] ParseOption(string key, object? value) => TsOptions.ParseCompilerOptions(key, value, CompilerOptions);

    public Message? UnknownOptionDiagnostic() => TsOptions.ExtraKeyDiagnostics("compilerOptions");

    public Message? UnknownDidYouMeanDiagnostic() => TsOptions.ExtraKeyDidYouMeanDiagnostics("compilerOptions");
}

internal sealed class WatchOptionsParser : IOptionParser
{
    internal WatchOptions WatchOptions { get; }

    internal WatchOptionsParser(WatchOptions watchOptions) => WatchOptions = watchOptions;

    public Diagnostic[] ParseOption(string key, object? value) => TsOptions.ParseWatchOptions(key, value, WatchOptions);

    public Message? UnknownOptionDiagnostic() => TsOptions.ExtraKeyDiagnostics("watchOptions");

    public Message? UnknownDidYouMeanDiagnostic() => TsOptions.ExtraKeyDidYouMeanDiagnostics("watchOptions");
}

internal sealed class TypeAcquisitionParser : IOptionParser
{
    internal TypeAcquisition TypeAcquisition { get; }

    internal TypeAcquisitionParser(TypeAcquisition typeAcquisition) => TypeAcquisition = typeAcquisition;

    public Diagnostic[] ParseOption(string key, object? value) => TsOptions.ParseTypeAcquisition(key, value, TypeAcquisition);

    public Message? UnknownOptionDiagnostic() => TsOptions.ExtraKeyDiagnostics("typeAcquisition");

    public Message? UnknownDidYouMeanDiagnostic() => TsOptions.ExtraKeyDidYouMeanDiagnostics("typeAcquisition");
}

internal sealed class BuildOptionsParser : IOptionParser
{
    internal BuildOptions BuildOptions { get; }

    internal BuildOptionsParser(BuildOptions buildOptions) => BuildOptions = buildOptions;

    public Diagnostic[] ParseOption(string key, object? value) => TsOptions.ParseBuildOptions(key, value, BuildOptions);

    public Message? UnknownOptionDiagnostic() => TsOptions.ExtraKeyDiagnostics("buildOptions");

    public Message? UnknownDidYouMeanDiagnostic() => TsOptions.ExtraKeyDidYouMeanDiagnostics("buildOptions");
}

public static partial class TsOptions
{
    public static Diagnostic[] ParseCompilerOptions(string key, object? value, CompilerOptions? allOptions)
    {
        if (value is null)
        {
            return [];
        }
        if (allOptions is null)
        {
            return [];
        }
        ParseCompilerOptionsWorker(key, value, allOptions);
        return [];
    }

    internal static bool ParseCompilerOptionsWorker(string key, object? value, CompilerOptions allOptions)
    {
        var option = CommandLineCompilerOptionsMap.Get(key);
        if (option is not null)
        {
            key = option.Name;
        }
        switch (key)
        {
            case "allowJs":
                allOptions.AllowJs = ParseTristate(value);
                break;
            case "allowImportingTsExtensions":
                allOptions.AllowImportingTsExtensions = ParseTristate(value);
                break;
            case "allowSyntheticDefaultImports":
                allOptions.AllowSyntheticDefaultImports = ParseTristate(value);
                break;
            case "allowNonTsExtensions":
                allOptions.AllowNonTsExtensions = ParseTristate(value);
                break;
            case "allowUmdGlobalAccess":
                allOptions.AllowUmdGlobalAccess = ParseTristate(value);
                break;
            case "allowUnreachableCode":
                allOptions.AllowUnreachableCode = ParseTristate(value);
                break;
            case "allowUnusedLabels":
                allOptions.AllowUnusedLabels = ParseTristate(value);
                break;
            case "allowArbitraryExtensions":
                allOptions.AllowArbitraryExtensions = ParseTristate(value);
                break;
            case "alwaysStrict":
                allOptions.AlwaysStrict = ParseTristate(value);
                break;
            case "assumeChangesOnlyAffectDirectDependencies":
                allOptions.AssumeChangesOnlyAffectDirectDependencies = ParseTristate(value);
                break;
            case "baseUrl":
                allOptions.BaseUrl = ParseString(value);
                break;
            case "build":
                allOptions.Build = ParseTristate(value);
                break;
            case "checkJs":
                allOptions.CheckJs = ParseTristate(value);
                break;
            case "customConditions":
                allOptions.CustomConditions = ParseStringArray(value);
                break;
            case "composite":
                allOptions.Composite = ParseTristate(value);
                break;
            case "declarationDir":
                allOptions.DeclarationDir = ParseString(value);
                break;
            case "deduplicatePackages":
                allOptions.DeduplicatePackages = ParseTristate(value);
                break;
            case "diagnostics":
                allOptions.Diagnostics = ParseTristate(value);
                break;
            case "disableSizeLimit":
                allOptions.DisableSizeLimit = ParseTristate(value);
                break;
            case "disableSourceOfProjectReferenceRedirect":
                allOptions.DisableSourceOfProjectReferenceRedirect = ParseTristate(value);
                break;
            case "disableSolutionSearching":
                allOptions.DisableSolutionSearching = ParseTristate(value);
                break;
            case "disableReferencedProjectLoad":
                allOptions.DisableReferencedProjectLoad = ParseTristate(value);
                break;
            case "declarationMap":
                allOptions.DeclarationMap = ParseTristate(value);
                break;
            case "declaration":
                allOptions.Declaration = ParseTristate(value);
                break;
            case "downlevelIteration":
                allOptions.DownlevelIteration = ParseTristate(value);
                break;
            case "erasableSyntaxOnly":
                allOptions.ErasableSyntaxOnly = ParseTristate(value);
                break;
            case "emitDeclarationOnly":
                allOptions.EmitDeclarationOnly = ParseTristate(value);
                break;
            case "extendedDiagnostics":
                allOptions.ExtendedDiagnostics = ParseTristate(value);
                break;
            case "emitDecoratorMetadata":
                allOptions.EmitDecoratorMetadata = ParseTristate(value);
                break;
            case "emitBOM":
                allOptions.EmitBOM = ParseTristate(value);
                break;
            case "esModuleInterop":
                allOptions.ESModuleInterop = ParseTristate(value);
                break;
            case "exactOptionalPropertyTypes":
                allOptions.ExactOptionalPropertyTypes = ParseTristate(value);
                break;
            case "explainFiles":
                allOptions.ExplainFiles = ParseTristate(value);
                break;
            case "experimentalDecorators":
                allOptions.ExperimentalDecorators = ParseTristate(value);
                break;
            case "forceConsistentCasingInFileNames":
                allOptions.ForceConsistentCasingInFileNames = ParseTristate(value);
                break;
            case "generateCpuProfile":
                allOptions.GenerateCpuProfile = ParseString(value);
                break;
            case "generateTrace":
                allOptions.GenerateTrace = ParseString(value);
                break;
            case "isolatedModules":
                allOptions.IsolatedModules = ParseTristate(value);
                break;
            case "ignoreConfig":
                allOptions.IgnoreConfig = ParseTristate(value);
                break;
            case "ignoreDeprecations":
                allOptions.IgnoreDeprecations = ParseString(value);
                break;
            case "importHelpers":
                allOptions.ImportHelpers = ParseTristate(value);
                break;
            case "incremental":
                allOptions.Incremental = ParseTristate(value);
                break;
            case "init":
                allOptions.Init = ParseTristate(value);
                break;
            case "inlineSourceMap":
                allOptions.InlineSourceMap = ParseTristate(value);
                break;
            case "inlineSources":
                allOptions.InlineSources = ParseTristate(value);
                break;
            case "isolatedDeclarations":
                allOptions.IsolatedDeclarations = ParseTristate(value);
                break;
            case "jsx":
                allOptions.Jsx = FloatOrInt32ToFlag<JsxEmit>(value);
                break;
            case "jsxFactory":
                allOptions.JsxFactory = ParseString(value);
                break;
            case "jsxFragmentFactory":
                allOptions.JsxFragmentFactory = ParseString(value);
                break;
            case "jsxImportSource":
                allOptions.JsxImportSource = ParseString(value);
                break;
            case "lib":
                if (value is string[] lib)
                {
                    allOptions.Lib = lib;
                }
                else
                {
                    allOptions.Lib = ParseStringArray(value);
                }
                break;
            case "libReplacement":
                allOptions.LibReplacement = ParseTristate(value);
                break;
            case "listEmittedFiles":
                allOptions.ListEmittedFiles = ParseTristate(value);
                break;
            case "listFiles":
                allOptions.ListFiles = ParseTristate(value);
                break;
            case "listFilesOnly":
                allOptions.ListFilesOnly = ParseTristate(value);
                break;
            case "locale":
                allOptions.Locale = ParseString(value);
                break;
            case "mapRoot":
                allOptions.MapRoot = ParseString(value);
                break;
            case "module":
                allOptions.Module = FloatOrInt32ToFlag<ModuleKind>(value);
                break;
            case "moduleDetectionKind":
                allOptions.ModuleDetection = FloatOrInt32ToFlag<ModuleDetectionKind>(value);
                break;
            case "moduleResolution":
                allOptions.ModuleResolution = FloatOrInt32ToFlag<ModuleResolutionKind>(value);
                break;
            case "moduleSuffixes":
                allOptions.ModuleSuffixes = ParseStringArray(value);
                break;
            case "moduleDetection":
                allOptions.ModuleDetection = FloatOrInt32ToFlag<ModuleDetectionKind>(value);
                break;
            case "noCheck":
                allOptions.NoCheck = ParseTristate(value);
                break;
            case "noFallthroughCasesInSwitch":
                allOptions.NoFallthroughCasesInSwitch = ParseTristate(value);
                break;
            case "noEmitForJsFiles":
                allOptions.NoEmitForJsFiles = ParseTristate(value);
                break;
            case "noErrorTruncation":
                allOptions.NoErrorTruncation = ParseTristate(value);
                break;
            case "noImplicitAny":
                allOptions.NoImplicitAny = ParseTristate(value);
                break;
            case "noImplicitThis":
                allOptions.NoImplicitThis = ParseTristate(value);
                break;
            case "noLib":
                allOptions.NoLib = ParseTristate(value);
                break;
            case "noPropertyAccessFromIndexSignature":
                allOptions.NoPropertyAccessFromIndexSignature = ParseTristate(value);
                break;
            case "noUncheckedIndexedAccess":
                allOptions.NoUncheckedIndexedAccess = ParseTristate(value);
                break;
            case "noEmitHelpers":
                allOptions.NoEmitHelpers = ParseTristate(value);
                break;
            case "noEmitOnError":
                allOptions.NoEmitOnError = ParseTristate(value);
                break;
            case "noImplicitReturns":
                allOptions.NoImplicitReturns = ParseTristate(value);
                break;
            case "noUnusedLocals":
                allOptions.NoUnusedLocals = ParseTristate(value);
                break;
            case "noUnusedParameters":
                allOptions.NoUnusedParameters = ParseTristate(value);
                break;
            case "noImplicitOverride":
                allOptions.NoImplicitOverride = ParseTristate(value);
                break;
            case "noUncheckedSideEffectImports":
                allOptions.NoUncheckedSideEffectImports = ParseTristate(value);
                break;
            case "outFile":
                allOptions.OutFile = ParseString(value);
                break;
            case "noResolve":
                allOptions.NoResolve = ParseTristate(value);
                break;
            case "paths":
                allOptions.Paths = ParseStringMap(value);
                break;
            case "plugins":
                // Native TypeScript does not load plugins; retain them only so tools can report the incompatibility.
                if (value is object?[] plugins)
                {
                    allOptions.Plugins = Map(plugins, plugin =>
                    {
                        if (plugin is OrderedMap<string, object?> pluginMap)
                        {
                            return new PluginImport { Name = ParseString(pluginMap.GetOrZero("name")) };
                        }
                        return new PluginImport();
                    });
                }
                break;
            case "preserveWatchOutput":
                allOptions.PreserveWatchOutput = ParseTristate(value);
                break;
            case "preserveConstEnums":
                allOptions.PreserveConstEnums = ParseTristate(value);
                break;
            case "preserveSymlinks":
                allOptions.PreserveSymlinks = ParseTristate(value);
                break;
            case "project":
                allOptions.Project = ParseString(value);
                break;
            case "pretty":
                allOptions.Pretty = ParseTristate(value);
                break;
            case "resolveJsonModule":
                allOptions.ResolveJsonModule = ParseTristate(value);
                break;
            case "resolvePackageJsonExports":
                allOptions.ResolvePackageJsonExports = ParseTristate(value);
                break;
            case "resolvePackageJsonImports":
                allOptions.ResolvePackageJsonImports = ParseTristate(value);
                break;
            case "reactNamespace":
                allOptions.ReactNamespace = ParseString(value);
                break;
            case "rewriteRelativeImportExtensions":
                allOptions.RewriteRelativeImportExtensions = ParseTristate(value);
                break;
            case "rootDir":
                allOptions.RootDir = ParseString(value);
                break;
            case "rootDirs":
                allOptions.RootDirs = ParseStringArray(value);
                break;
            case "removeComments":
                allOptions.RemoveComments = ParseTristate(value);
                break;
            case "stableTypeOrdering":
                allOptions.StableTypeOrdering = ParseTristate(value);
                break;
            case "strict":
                allOptions.Strict = ParseTristate(value);
                break;
            case "strictBindCallApply":
                allOptions.StrictBindCallApply = ParseTristate(value);
                break;
            case "strictBuiltinIteratorReturn":
                allOptions.StrictBuiltinIteratorReturn = ParseTristate(value);
                break;
            case "strictFunctionTypes":
                allOptions.StrictFunctionTypes = ParseTristate(value);
                break;
            case "strictNullChecks":
                allOptions.StrictNullChecks = ParseTristate(value);
                break;
            case "strictPropertyInitialization":
                allOptions.StrictPropertyInitialization = ParseTristate(value);
                break;
            case "skipDefaultLibCheck":
                allOptions.SkipDefaultLibCheck = ParseTristate(value);
                break;
            case "sourceMap":
                allOptions.SourceMap = ParseTristate(value);
                break;
            case "sourceRoot":
                allOptions.SourceRoot = ParseString(value);
                break;
            case "stripInternal":
                allOptions.StripInternal = ParseTristate(value);
                break;
            case "suppressOutputPathCheck":
                allOptions.SuppressOutputPathCheck = ParseTristate(value);
                break;
            case "target":
                allOptions.Target = FloatOrInt32ToFlag<ScriptTarget>(value);
                break;
            case "traceResolution":
                allOptions.TraceResolution = ParseTristate(value);
                break;
            case "tsBuildInfoFile":
                allOptions.TsBuildInfoFile = ParseString(value);
                break;
            case "typeRoots":
                allOptions.TypeRoots = ParseStringArray(value);
                break;
            case "types":
                allOptions.Types = ParseStringArray(value);
                break;
            case "useDefineForClassFields":
                allOptions.UseDefineForClassFields = ParseTristate(value);
                break;
            case "useUnknownInCatchVariables":
                allOptions.UseUnknownInCatchVariables = ParseTristate(value);
                break;
            case "verbatimModuleSyntax":
                allOptions.VerbatimModuleSyntax = ParseTristate(value);
                break;
            case "version":
                allOptions.Version = ParseTristate(value);
                break;
            case "help":
                allOptions.Help = ParseTristate(value);
                break;
            case "all":
                allOptions.All = ParseTristate(value);
                break;
            case "maxNodeModuleJsDepth":
                allOptions.MaxNodeModuleJsDepth = ParseNumber(value);
                break;
            case "skipLibCheck":
                allOptions.SkipLibCheck = ParseTristate(value);
                break;
            case "noEmit":
                allOptions.NoEmit = ParseTristate(value);
                break;
            case "showConfig":
                allOptions.ShowConfig = ParseTristate(value);
                break;
            case "configFilePath":
                allOptions.ConfigFilePath = ParseString(value);
                break;
            case "noDtsResolution":
                allOptions.NoDtsResolution = ParseTristate(value);
                break;
            case "pathsBasePath":
                allOptions.PathsBasePath = ParseString(value);
                break;
            case "outDir":
                allOptions.OutDir = ParseString(value);
                break;
            case "newLine":
                allOptions.NewLine = FloatOrInt32ToFlag<NewLineKind>(value);
                break;
            case "watch":
                allOptions.Watch = ParseTristate(value);
                break;
            case "pprofDir":
                allOptions.PprofDir = ParseString(value);
                break;
            case "singleThreaded":
                allOptions.SingleThreaded = ParseTristate(value);
                break;
            case "quiet":
                allOptions.Quiet = ParseTristate(value);
                break;
            case "checkers":
                allOptions.Checkers = ParseNumber(value);
                break;
            case "runExternalCode":
                allOptions.RunExternalCode = ParseTristate(value);
                break;
            default:
                // different than any key above
                return false;
        }
        return true;
    }

    private static T FloatOrInt32ToFlag<T>(object? value) where T : struct, Enum
    {
        if (value is T t)
        {
            return t;
        }
        return (T)(object)(int)(double)value!;
    }

    public static Diagnostic[] ParseWatchOptions(string key, object? value, WatchOptions? allOptions)
    {
        if (allOptions is null)
        {
            return [];
        }
        switch (key)
        {
            case "watchInterval":
                allOptions.Interval = ParseNumber(value);
                break;
            case "watchFile":
                if (value is not null)
                {
                    allOptions.FileKind = (WatchFileKind)value;
                }
                break;
            case "watchDirectory":
                if (value is not null)
                {
                    allOptions.DirectoryKind = (WatchDirectoryKind)value;
                }
                break;
            case "fallbackPolling":
                if (value is not null)
                {
                    allOptions.FallbackPolling = (PollingKind)value;
                }
                break;
            case "synchronousWatchDirectory":
                allOptions.SyncWatchDir = ParseTristate(value);
                break;
            case "excludeDirectories":
                allOptions.ExcludeDir = ParseStringArray(value);
                break;
            case "excludeFiles":
                allOptions.ExcludeFiles = ParseStringArray(value);
                break;
        }
        return [];
    }

    public static Diagnostic[] ParseTypeAcquisition(string key, object? value, TypeAcquisition? allOptions)
    {
        if (value is null)
        {
            return [];
        }
        if (allOptions is null)
        {
            return [];
        }
        switch (key)
        {
            case "enable":
                allOptions.Enable = ParseTristate(value);
                break;
            case "include":
                allOptions.Include = ParseStringArray(value);
                break;
            case "exclude":
                allOptions.Exclude = ParseStringArray(value);
                break;
            case "disableFilenameBasedTypeAcquisition":
                allOptions.DisableFilenameBasedTypeAcquisition = ParseTristate(value);
                break;
        }
        return [];
    }

    public static Diagnostic[] ParseBuildOptions(string key, object? value, BuildOptions? allOptions)
    {
        if (value is null)
        {
            return [];
        }
        if (allOptions is null)
        {
            return [];
        }
        var option = BuildNameMap.Get(key);
        if (option is not null)
        {
            key = option.Name;
        }
        switch (key)
        {
            case "clean":
                allOptions.Clean = ParseTristate(value);
                break;
            case "dry":
                allOptions.Dry = ParseTristate(value);
                break;
            case "force":
                allOptions.Force = ParseTristate(value);
                break;
            case "builders":
                allOptions.Builders = ParseNumber(value);
                break;
            case "stopBuildOnErrors":
                allOptions.StopBuildOnErrors = ParseTristate(value);
                break;
            case "verbose":
                allOptions.Verbose = ParseTristate(value);
                break;
        }
        return [];
    }

    // mergeCompilerOptions merges the source compiler options into the target compiler options
    // with optional awareness of explicitly set null values in the raw JSON.
    // Fields in the source options will overwrite the corresponding fields in the target options,
    // including when they are explicitly set to null in the raw configuration (if rawSource is provided).
    internal static CompilerOptions MergeCompilerOptions(CompilerOptions targetOptions, CompilerOptions? sourceOptions, object? rawSource)
    {
        if (sourceOptions is null)
        {
            return targetOptions;
        }

        // Collect explicitly null field names from raw JSON
        var explicitNullFields = new Set<string>();
        if (rawSource is OrderedMap<string, object?> rawMap)
        {
            // Options are nested under "compilerOptions" in both tsconfig.json and wrapped command line options
            var (compilerOptionsRaw, exists) = rawMap.Get("compilerOptions");
            if (exists && compilerOptionsRaw is OrderedMap<string, object?> compilerOptionsMap)
            {
                foreach (var (key, value) in compilerOptionsMap.Entries())
                {
                    if (value is null)
                    {
                        explicitNullFields.Add(key);
                    }
                }
            }
        }

        // Do the merge, handling explicit nulls during the normal merge
    if (explicitNullFields.Has("allowJs"))
    {
        targetOptions.AllowJs = Tristate.TSUnknown;
    }
    else if (sourceOptions.AllowJs != Tristate.TSUnknown)
    {
        targetOptions.AllowJs = sourceOptions.AllowJs;
    }

    if (explicitNullFields.Has("allowArbitraryExtensions"))
    {
        targetOptions.AllowArbitraryExtensions = Tristate.TSUnknown;
    }
    else if (sourceOptions.AllowArbitraryExtensions != Tristate.TSUnknown)
    {
        targetOptions.AllowArbitraryExtensions = sourceOptions.AllowArbitraryExtensions;
    }

    if (explicitNullFields.Has("allowImportingTsExtensions"))
    {
        targetOptions.AllowImportingTsExtensions = Tristate.TSUnknown;
    }
    else if (sourceOptions.AllowImportingTsExtensions != Tristate.TSUnknown)
    {
        targetOptions.AllowImportingTsExtensions = sourceOptions.AllowImportingTsExtensions;
    }

    if (explicitNullFields.Has("allowNonTsExtensions"))
    {
        targetOptions.AllowNonTsExtensions = Tristate.TSUnknown;
    }
    else if (sourceOptions.AllowNonTsExtensions != Tristate.TSUnknown)
    {
        targetOptions.AllowNonTsExtensions = sourceOptions.AllowNonTsExtensions;
    }

    if (explicitNullFields.Has("allowUmdGlobalAccess"))
    {
        targetOptions.AllowUmdGlobalAccess = Tristate.TSUnknown;
    }
    else if (sourceOptions.AllowUmdGlobalAccess != Tristate.TSUnknown)
    {
        targetOptions.AllowUmdGlobalAccess = sourceOptions.AllowUmdGlobalAccess;
    }

    if (explicitNullFields.Has("allowUnreachableCode"))
    {
        targetOptions.AllowUnreachableCode = Tristate.TSUnknown;
    }
    else if (sourceOptions.AllowUnreachableCode != Tristate.TSUnknown)
    {
        targetOptions.AllowUnreachableCode = sourceOptions.AllowUnreachableCode;
    }

    if (explicitNullFields.Has("allowUnusedLabels"))
    {
        targetOptions.AllowUnusedLabels = Tristate.TSUnknown;
    }
    else if (sourceOptions.AllowUnusedLabels != Tristate.TSUnknown)
    {
        targetOptions.AllowUnusedLabels = sourceOptions.AllowUnusedLabels;
    }

    if (explicitNullFields.Has("assumeChangesOnlyAffectDirectDependencies"))
    {
        targetOptions.AssumeChangesOnlyAffectDirectDependencies = Tristate.TSUnknown;
    }
    else if (sourceOptions.AssumeChangesOnlyAffectDirectDependencies != Tristate.TSUnknown)
    {
        targetOptions.AssumeChangesOnlyAffectDirectDependencies = sourceOptions.AssumeChangesOnlyAffectDirectDependencies;
    }

    if (explicitNullFields.Has("checkJs"))
    {
        targetOptions.CheckJs = Tristate.TSUnknown;
    }
    else if (sourceOptions.CheckJs != Tristate.TSUnknown)
    {
        targetOptions.CheckJs = sourceOptions.CheckJs;
    }

    if (explicitNullFields.Has("customConditions"))
    {
        targetOptions.CustomConditions = null;
    }
    else if (sourceOptions.CustomConditions is not null)
    {
        targetOptions.CustomConditions = sourceOptions.CustomConditions;
    }

    if (explicitNullFields.Has("composite"))
    {
        targetOptions.Composite = Tristate.TSUnknown;
    }
    else if (sourceOptions.Composite != Tristate.TSUnknown)
    {
        targetOptions.Composite = sourceOptions.Composite;
    }

    if (explicitNullFields.Has("emitDeclarationOnly"))
    {
        targetOptions.EmitDeclarationOnly = Tristate.TSUnknown;
    }
    else if (sourceOptions.EmitDeclarationOnly != Tristate.TSUnknown)
    {
        targetOptions.EmitDeclarationOnly = sourceOptions.EmitDeclarationOnly;
    }

    if (explicitNullFields.Has("emitBOM"))
    {
        targetOptions.EmitBOM = Tristate.TSUnknown;
    }
    else if (sourceOptions.EmitBOM != Tristate.TSUnknown)
    {
        targetOptions.EmitBOM = sourceOptions.EmitBOM;
    }

    if (explicitNullFields.Has("emitDecoratorMetadata"))
    {
        targetOptions.EmitDecoratorMetadata = Tristate.TSUnknown;
    }
    else if (sourceOptions.EmitDecoratorMetadata != Tristate.TSUnknown)
    {
        targetOptions.EmitDecoratorMetadata = sourceOptions.EmitDecoratorMetadata;
    }

    if (explicitNullFields.Has("declaration"))
    {
        targetOptions.Declaration = Tristate.TSUnknown;
    }
    else if (sourceOptions.Declaration != Tristate.TSUnknown)
    {
        targetOptions.Declaration = sourceOptions.Declaration;
    }

    if (explicitNullFields.Has("declarationDir"))
    {
        targetOptions.DeclarationDir = "";
    }
    else if (sourceOptions.DeclarationDir != "")
    {
        targetOptions.DeclarationDir = sourceOptions.DeclarationDir;
    }

    if (explicitNullFields.Has("declarationMap"))
    {
        targetOptions.DeclarationMap = Tristate.TSUnknown;
    }
    else if (sourceOptions.DeclarationMap != Tristate.TSUnknown)
    {
        targetOptions.DeclarationMap = sourceOptions.DeclarationMap;
    }

    if (explicitNullFields.Has("deduplicatePackages"))
    {
        targetOptions.DeduplicatePackages = Tristate.TSUnknown;
    }
    else if (sourceOptions.DeduplicatePackages != Tristate.TSUnknown)
    {
        targetOptions.DeduplicatePackages = sourceOptions.DeduplicatePackages;
    }

    if (explicitNullFields.Has("disableSizeLimit"))
    {
        targetOptions.DisableSizeLimit = Tristate.TSUnknown;
    }
    else if (sourceOptions.DisableSizeLimit != Tristate.TSUnknown)
    {
        targetOptions.DisableSizeLimit = sourceOptions.DisableSizeLimit;
    }

    if (explicitNullFields.Has("disableSourceOfProjectReferenceRedirect"))
    {
        targetOptions.DisableSourceOfProjectReferenceRedirect = Tristate.TSUnknown;
    }
    else if (sourceOptions.DisableSourceOfProjectReferenceRedirect != Tristate.TSUnknown)
    {
        targetOptions.DisableSourceOfProjectReferenceRedirect = sourceOptions.DisableSourceOfProjectReferenceRedirect;
    }

    if (explicitNullFields.Has("disableSolutionSearching"))
    {
        targetOptions.DisableSolutionSearching = Tristate.TSUnknown;
    }
    else if (sourceOptions.DisableSolutionSearching != Tristate.TSUnknown)
    {
        targetOptions.DisableSolutionSearching = sourceOptions.DisableSolutionSearching;
    }

    if (explicitNullFields.Has("disableReferencedProjectLoad"))
    {
        targetOptions.DisableReferencedProjectLoad = Tristate.TSUnknown;
    }
    else if (sourceOptions.DisableReferencedProjectLoad != Tristate.TSUnknown)
    {
        targetOptions.DisableReferencedProjectLoad = sourceOptions.DisableReferencedProjectLoad;
    }

    if (explicitNullFields.Has("erasableSyntaxOnly"))
    {
        targetOptions.ErasableSyntaxOnly = Tristate.TSUnknown;
    }
    else if (sourceOptions.ErasableSyntaxOnly != Tristate.TSUnknown)
    {
        targetOptions.ErasableSyntaxOnly = sourceOptions.ErasableSyntaxOnly;
    }

    if (explicitNullFields.Has("exactOptionalPropertyTypes"))
    {
        targetOptions.ExactOptionalPropertyTypes = Tristate.TSUnknown;
    }
    else if (sourceOptions.ExactOptionalPropertyTypes != Tristate.TSUnknown)
    {
        targetOptions.ExactOptionalPropertyTypes = sourceOptions.ExactOptionalPropertyTypes;
    }

    if (explicitNullFields.Has("experimentalDecorators"))
    {
        targetOptions.ExperimentalDecorators = Tristate.TSUnknown;
    }
    else if (sourceOptions.ExperimentalDecorators != Tristate.TSUnknown)
    {
        targetOptions.ExperimentalDecorators = sourceOptions.ExperimentalDecorators;
    }

    if (explicitNullFields.Has("forceConsistentCasingInFileNames"))
    {
        targetOptions.ForceConsistentCasingInFileNames = Tristate.TSUnknown;
    }
    else if (sourceOptions.ForceConsistentCasingInFileNames != Tristate.TSUnknown)
    {
        targetOptions.ForceConsistentCasingInFileNames = sourceOptions.ForceConsistentCasingInFileNames;
    }

    if (explicitNullFields.Has("isolatedModules"))
    {
        targetOptions.IsolatedModules = Tristate.TSUnknown;
    }
    else if (sourceOptions.IsolatedModules != Tristate.TSUnknown)
    {
        targetOptions.IsolatedModules = sourceOptions.IsolatedModules;
    }

    if (explicitNullFields.Has("isolatedDeclarations"))
    {
        targetOptions.IsolatedDeclarations = Tristate.TSUnknown;
    }
    else if (sourceOptions.IsolatedDeclarations != Tristate.TSUnknown)
    {
        targetOptions.IsolatedDeclarations = sourceOptions.IsolatedDeclarations;
    }

    if (explicitNullFields.Has("ignoreConfig"))
    {
        targetOptions.IgnoreConfig = Tristate.TSUnknown;
    }
    else if (sourceOptions.IgnoreConfig != Tristate.TSUnknown)
    {
        targetOptions.IgnoreConfig = sourceOptions.IgnoreConfig;
    }

    if (explicitNullFields.Has("ignoreDeprecations"))
    {
        targetOptions.IgnoreDeprecations = "";
    }
    else if (sourceOptions.IgnoreDeprecations != "")
    {
        targetOptions.IgnoreDeprecations = sourceOptions.IgnoreDeprecations;
    }

    if (explicitNullFields.Has("importHelpers"))
    {
        targetOptions.ImportHelpers = Tristate.TSUnknown;
    }
    else if (sourceOptions.ImportHelpers != Tristate.TSUnknown)
    {
        targetOptions.ImportHelpers = sourceOptions.ImportHelpers;
    }

    if (explicitNullFields.Has("inlineSourceMap"))
    {
        targetOptions.InlineSourceMap = Tristate.TSUnknown;
    }
    else if (sourceOptions.InlineSourceMap != Tristate.TSUnknown)
    {
        targetOptions.InlineSourceMap = sourceOptions.InlineSourceMap;
    }

    if (explicitNullFields.Has("inlineSources"))
    {
        targetOptions.InlineSources = Tristate.TSUnknown;
    }
    else if (sourceOptions.InlineSources != Tristate.TSUnknown)
    {
        targetOptions.InlineSources = sourceOptions.InlineSources;
    }

    if (explicitNullFields.Has("init"))
    {
        targetOptions.Init = Tristate.TSUnknown;
    }
    else if (sourceOptions.Init != Tristate.TSUnknown)
    {
        targetOptions.Init = sourceOptions.Init;
    }

    if (explicitNullFields.Has("incremental"))
    {
        targetOptions.Incremental = Tristate.TSUnknown;
    }
    else if (sourceOptions.Incremental != Tristate.TSUnknown)
    {
        targetOptions.Incremental = sourceOptions.Incremental;
    }

    if (explicitNullFields.Has("jsx"))
    {
        targetOptions.Jsx = default;
    }
    else if (sourceOptions.Jsx != default)
    {
        targetOptions.Jsx = sourceOptions.Jsx;
    }

    if (explicitNullFields.Has("jsxFactory"))
    {
        targetOptions.JsxFactory = "";
    }
    else if (sourceOptions.JsxFactory != "")
    {
        targetOptions.JsxFactory = sourceOptions.JsxFactory;
    }

    if (explicitNullFields.Has("jsxFragmentFactory"))
    {
        targetOptions.JsxFragmentFactory = "";
    }
    else if (sourceOptions.JsxFragmentFactory != "")
    {
        targetOptions.JsxFragmentFactory = sourceOptions.JsxFragmentFactory;
    }

    if (explicitNullFields.Has("jsxImportSource"))
    {
        targetOptions.JsxImportSource = "";
    }
    else if (sourceOptions.JsxImportSource != "")
    {
        targetOptions.JsxImportSource = sourceOptions.JsxImportSource;
    }

    if (explicitNullFields.Has("lib"))
    {
        targetOptions.Lib = null;
    }
    else if (sourceOptions.Lib is not null)
    {
        targetOptions.Lib = sourceOptions.Lib;
    }

    if (explicitNullFields.Has("libReplacement"))
    {
        targetOptions.LibReplacement = Tristate.TSUnknown;
    }
    else if (sourceOptions.LibReplacement != Tristate.TSUnknown)
    {
        targetOptions.LibReplacement = sourceOptions.LibReplacement;
    }

    if (explicitNullFields.Has("locale"))
    {
        targetOptions.Locale = "";
    }
    else if (sourceOptions.Locale != "")
    {
        targetOptions.Locale = sourceOptions.Locale;
    }

    if (explicitNullFields.Has("mapRoot"))
    {
        targetOptions.MapRoot = "";
    }
    else if (sourceOptions.MapRoot != "")
    {
        targetOptions.MapRoot = sourceOptions.MapRoot;
    }

    if (explicitNullFields.Has("module"))
    {
        targetOptions.Module = default;
    }
    else if (sourceOptions.Module != default)
    {
        targetOptions.Module = sourceOptions.Module;
    }

    if (explicitNullFields.Has("moduleResolution"))
    {
        targetOptions.ModuleResolution = default;
    }
    else if (sourceOptions.ModuleResolution != default)
    {
        targetOptions.ModuleResolution = sourceOptions.ModuleResolution;
    }

    if (explicitNullFields.Has("moduleSuffixes"))
    {
        targetOptions.ModuleSuffixes = null;
    }
    else if (sourceOptions.ModuleSuffixes is not null)
    {
        targetOptions.ModuleSuffixes = sourceOptions.ModuleSuffixes;
    }

    if (explicitNullFields.Has("moduleDetection"))
    {
        targetOptions.ModuleDetection = default;
    }
    else if (sourceOptions.ModuleDetection != default)
    {
        targetOptions.ModuleDetection = sourceOptions.ModuleDetection;
    }

    if (explicitNullFields.Has("newLine"))
    {
        targetOptions.NewLine = default;
    }
    else if (sourceOptions.NewLine != default)
    {
        targetOptions.NewLine = sourceOptions.NewLine;
    }

    if (explicitNullFields.Has("noEmit"))
    {
        targetOptions.NoEmit = Tristate.TSUnknown;
    }
    else if (sourceOptions.NoEmit != Tristate.TSUnknown)
    {
        targetOptions.NoEmit = sourceOptions.NoEmit;
    }

    if (explicitNullFields.Has("noCheck"))
    {
        targetOptions.NoCheck = Tristate.TSUnknown;
    }
    else if (sourceOptions.NoCheck != Tristate.TSUnknown)
    {
        targetOptions.NoCheck = sourceOptions.NoCheck;
    }

    if (explicitNullFields.Has("noErrorTruncation"))
    {
        targetOptions.NoErrorTruncation = Tristate.TSUnknown;
    }
    else if (sourceOptions.NoErrorTruncation != Tristate.TSUnknown)
    {
        targetOptions.NoErrorTruncation = sourceOptions.NoErrorTruncation;
    }

    if (explicitNullFields.Has("noFallthroughCasesInSwitch"))
    {
        targetOptions.NoFallthroughCasesInSwitch = Tristate.TSUnknown;
    }
    else if (sourceOptions.NoFallthroughCasesInSwitch != Tristate.TSUnknown)
    {
        targetOptions.NoFallthroughCasesInSwitch = sourceOptions.NoFallthroughCasesInSwitch;
    }

    if (explicitNullFields.Has("noImplicitAny"))
    {
        targetOptions.NoImplicitAny = Tristate.TSUnknown;
    }
    else if (sourceOptions.NoImplicitAny != Tristate.TSUnknown)
    {
        targetOptions.NoImplicitAny = sourceOptions.NoImplicitAny;
    }

    if (explicitNullFields.Has("noImplicitThis"))
    {
        targetOptions.NoImplicitThis = Tristate.TSUnknown;
    }
    else if (sourceOptions.NoImplicitThis != Tristate.TSUnknown)
    {
        targetOptions.NoImplicitThis = sourceOptions.NoImplicitThis;
    }

    if (explicitNullFields.Has("noImplicitReturns"))
    {
        targetOptions.NoImplicitReturns = Tristate.TSUnknown;
    }
    else if (sourceOptions.NoImplicitReturns != Tristate.TSUnknown)
    {
        targetOptions.NoImplicitReturns = sourceOptions.NoImplicitReturns;
    }

    if (explicitNullFields.Has("noEmitHelpers"))
    {
        targetOptions.NoEmitHelpers = Tristate.TSUnknown;
    }
    else if (sourceOptions.NoEmitHelpers != Tristate.TSUnknown)
    {
        targetOptions.NoEmitHelpers = sourceOptions.NoEmitHelpers;
    }

    if (explicitNullFields.Has("noLib"))
    {
        targetOptions.NoLib = Tristate.TSUnknown;
    }
    else if (sourceOptions.NoLib != Tristate.TSUnknown)
    {
        targetOptions.NoLib = sourceOptions.NoLib;
    }

    if (explicitNullFields.Has("noPropertyAccessFromIndexSignature"))
    {
        targetOptions.NoPropertyAccessFromIndexSignature = Tristate.TSUnknown;
    }
    else if (sourceOptions.NoPropertyAccessFromIndexSignature != Tristate.TSUnknown)
    {
        targetOptions.NoPropertyAccessFromIndexSignature = sourceOptions.NoPropertyAccessFromIndexSignature;
    }

    if (explicitNullFields.Has("noUncheckedIndexedAccess"))
    {
        targetOptions.NoUncheckedIndexedAccess = Tristate.TSUnknown;
    }
    else if (sourceOptions.NoUncheckedIndexedAccess != Tristate.TSUnknown)
    {
        targetOptions.NoUncheckedIndexedAccess = sourceOptions.NoUncheckedIndexedAccess;
    }

    if (explicitNullFields.Has("noEmitOnError"))
    {
        targetOptions.NoEmitOnError = Tristate.TSUnknown;
    }
    else if (sourceOptions.NoEmitOnError != Tristate.TSUnknown)
    {
        targetOptions.NoEmitOnError = sourceOptions.NoEmitOnError;
    }

    if (explicitNullFields.Has("noUnusedLocals"))
    {
        targetOptions.NoUnusedLocals = Tristate.TSUnknown;
    }
    else if (sourceOptions.NoUnusedLocals != Tristate.TSUnknown)
    {
        targetOptions.NoUnusedLocals = sourceOptions.NoUnusedLocals;
    }

    if (explicitNullFields.Has("noUnusedParameters"))
    {
        targetOptions.NoUnusedParameters = Tristate.TSUnknown;
    }
    else if (sourceOptions.NoUnusedParameters != Tristate.TSUnknown)
    {
        targetOptions.NoUnusedParameters = sourceOptions.NoUnusedParameters;
    }

    if (explicitNullFields.Has("noResolve"))
    {
        targetOptions.NoResolve = Tristate.TSUnknown;
    }
    else if (sourceOptions.NoResolve != Tristate.TSUnknown)
    {
        targetOptions.NoResolve = sourceOptions.NoResolve;
    }

    if (explicitNullFields.Has("noImplicitOverride"))
    {
        targetOptions.NoImplicitOverride = Tristate.TSUnknown;
    }
    else if (sourceOptions.NoImplicitOverride != Tristate.TSUnknown)
    {
        targetOptions.NoImplicitOverride = sourceOptions.NoImplicitOverride;
    }

    if (explicitNullFields.Has("noUncheckedSideEffectImports"))
    {
        targetOptions.NoUncheckedSideEffectImports = Tristate.TSUnknown;
    }
    else if (sourceOptions.NoUncheckedSideEffectImports != Tristate.TSUnknown)
    {
        targetOptions.NoUncheckedSideEffectImports = sourceOptions.NoUncheckedSideEffectImports;
    }

    if (explicitNullFields.Has("outDir"))
    {
        targetOptions.OutDir = "";
    }
    else if (sourceOptions.OutDir != "")
    {
        targetOptions.OutDir = sourceOptions.OutDir;
    }

    if (explicitNullFields.Has("paths"))
    {
        targetOptions.Paths = null;
    }
    else if (sourceOptions.Paths is not null)
    {
        targetOptions.Paths = sourceOptions.Paths;
    }

    if (explicitNullFields.Has("plugins"))
    {
        targetOptions.Plugins = null;
    }
    else if (sourceOptions.Plugins is not null)
    {
        targetOptions.Plugins = sourceOptions.Plugins;
    }

    if (explicitNullFields.Has("preserveConstEnums"))
    {
        targetOptions.PreserveConstEnums = Tristate.TSUnknown;
    }
    else if (sourceOptions.PreserveConstEnums != Tristate.TSUnknown)
    {
        targetOptions.PreserveConstEnums = sourceOptions.PreserveConstEnums;
    }

    if (explicitNullFields.Has("preserveSymlinks"))
    {
        targetOptions.PreserveSymlinks = Tristate.TSUnknown;
    }
    else if (sourceOptions.PreserveSymlinks != Tristate.TSUnknown)
    {
        targetOptions.PreserveSymlinks = sourceOptions.PreserveSymlinks;
    }

    if (explicitNullFields.Has("project"))
    {
        targetOptions.Project = "";
    }
    else if (sourceOptions.Project != "")
    {
        targetOptions.Project = sourceOptions.Project;
    }

    if (explicitNullFields.Has("resolveJsonModule"))
    {
        targetOptions.ResolveJsonModule = Tristate.TSUnknown;
    }
    else if (sourceOptions.ResolveJsonModule != Tristate.TSUnknown)
    {
        targetOptions.ResolveJsonModule = sourceOptions.ResolveJsonModule;
    }

    if (explicitNullFields.Has("resolvePackageJsonExports"))
    {
        targetOptions.ResolvePackageJsonExports = Tristate.TSUnknown;
    }
    else if (sourceOptions.ResolvePackageJsonExports != Tristate.TSUnknown)
    {
        targetOptions.ResolvePackageJsonExports = sourceOptions.ResolvePackageJsonExports;
    }

    if (explicitNullFields.Has("resolvePackageJsonImports"))
    {
        targetOptions.ResolvePackageJsonImports = Tristate.TSUnknown;
    }
    else if (sourceOptions.ResolvePackageJsonImports != Tristate.TSUnknown)
    {
        targetOptions.ResolvePackageJsonImports = sourceOptions.ResolvePackageJsonImports;
    }

    if (explicitNullFields.Has("removeComments"))
    {
        targetOptions.RemoveComments = Tristate.TSUnknown;
    }
    else if (sourceOptions.RemoveComments != Tristate.TSUnknown)
    {
        targetOptions.RemoveComments = sourceOptions.RemoveComments;
    }

    if (explicitNullFields.Has("rewriteRelativeImportExtensions"))
    {
        targetOptions.RewriteRelativeImportExtensions = Tristate.TSUnknown;
    }
    else if (sourceOptions.RewriteRelativeImportExtensions != Tristate.TSUnknown)
    {
        targetOptions.RewriteRelativeImportExtensions = sourceOptions.RewriteRelativeImportExtensions;
    }

    if (explicitNullFields.Has("reactNamespace"))
    {
        targetOptions.ReactNamespace = "";
    }
    else if (sourceOptions.ReactNamespace != "")
    {
        targetOptions.ReactNamespace = sourceOptions.ReactNamespace;
    }

    if (explicitNullFields.Has("rootDir"))
    {
        targetOptions.RootDir = "";
    }
    else if (sourceOptions.RootDir != "")
    {
        targetOptions.RootDir = sourceOptions.RootDir;
    }

    if (explicitNullFields.Has("rootDirs"))
    {
        targetOptions.RootDirs = null;
    }
    else if (sourceOptions.RootDirs is not null)
    {
        targetOptions.RootDirs = sourceOptions.RootDirs;
    }

    if (explicitNullFields.Has("skipLibCheck"))
    {
        targetOptions.SkipLibCheck = Tristate.TSUnknown;
    }
    else if (sourceOptions.SkipLibCheck != Tristate.TSUnknown)
    {
        targetOptions.SkipLibCheck = sourceOptions.SkipLibCheck;
    }

    if (explicitNullFields.Has("stableTypeOrdering"))
    {
        targetOptions.StableTypeOrdering = Tristate.TSUnknown;
    }
    else if (sourceOptions.StableTypeOrdering != Tristate.TSUnknown)
    {
        targetOptions.StableTypeOrdering = sourceOptions.StableTypeOrdering;
    }

    if (explicitNullFields.Has("strict"))
    {
        targetOptions.Strict = Tristate.TSUnknown;
    }
    else if (sourceOptions.Strict != Tristate.TSUnknown)
    {
        targetOptions.Strict = sourceOptions.Strict;
    }

    if (explicitNullFields.Has("strictBindCallApply"))
    {
        targetOptions.StrictBindCallApply = Tristate.TSUnknown;
    }
    else if (sourceOptions.StrictBindCallApply != Tristate.TSUnknown)
    {
        targetOptions.StrictBindCallApply = sourceOptions.StrictBindCallApply;
    }

    if (explicitNullFields.Has("strictBuiltinIteratorReturn"))
    {
        targetOptions.StrictBuiltinIteratorReturn = Tristate.TSUnknown;
    }
    else if (sourceOptions.StrictBuiltinIteratorReturn != Tristate.TSUnknown)
    {
        targetOptions.StrictBuiltinIteratorReturn = sourceOptions.StrictBuiltinIteratorReturn;
    }

    if (explicitNullFields.Has("strictFunctionTypes"))
    {
        targetOptions.StrictFunctionTypes = Tristate.TSUnknown;
    }
    else if (sourceOptions.StrictFunctionTypes != Tristate.TSUnknown)
    {
        targetOptions.StrictFunctionTypes = sourceOptions.StrictFunctionTypes;
    }

    if (explicitNullFields.Has("strictNullChecks"))
    {
        targetOptions.StrictNullChecks = Tristate.TSUnknown;
    }
    else if (sourceOptions.StrictNullChecks != Tristate.TSUnknown)
    {
        targetOptions.StrictNullChecks = sourceOptions.StrictNullChecks;
    }

    if (explicitNullFields.Has("strictPropertyInitialization"))
    {
        targetOptions.StrictPropertyInitialization = Tristate.TSUnknown;
    }
    else if (sourceOptions.StrictPropertyInitialization != Tristate.TSUnknown)
    {
        targetOptions.StrictPropertyInitialization = sourceOptions.StrictPropertyInitialization;
    }

    if (explicitNullFields.Has("stripInternal"))
    {
        targetOptions.StripInternal = Tristate.TSUnknown;
    }
    else if (sourceOptions.StripInternal != Tristate.TSUnknown)
    {
        targetOptions.StripInternal = sourceOptions.StripInternal;
    }

    if (explicitNullFields.Has("skipDefaultLibCheck"))
    {
        targetOptions.SkipDefaultLibCheck = Tristate.TSUnknown;
    }
    else if (sourceOptions.SkipDefaultLibCheck != Tristate.TSUnknown)
    {
        targetOptions.SkipDefaultLibCheck = sourceOptions.SkipDefaultLibCheck;
    }

    if (explicitNullFields.Has("sourceMap"))
    {
        targetOptions.SourceMap = Tristate.TSUnknown;
    }
    else if (sourceOptions.SourceMap != Tristate.TSUnknown)
    {
        targetOptions.SourceMap = sourceOptions.SourceMap;
    }

    if (explicitNullFields.Has("sourceRoot"))
    {
        targetOptions.SourceRoot = "";
    }
    else if (sourceOptions.SourceRoot != "")
    {
        targetOptions.SourceRoot = sourceOptions.SourceRoot;
    }

    if (explicitNullFields.Has("suppressOutputPathCheck"))
    {
        targetOptions.SuppressOutputPathCheck = Tristate.TSUnknown;
    }
    else if (sourceOptions.SuppressOutputPathCheck != Tristate.TSUnknown)
    {
        targetOptions.SuppressOutputPathCheck = sourceOptions.SuppressOutputPathCheck;
    }

    if (explicitNullFields.Has("target"))
    {
        targetOptions.Target = default;
    }
    else if (sourceOptions.Target != default)
    {
        targetOptions.Target = sourceOptions.Target;
    }

    if (explicitNullFields.Has("traceResolution"))
    {
        targetOptions.TraceResolution = Tristate.TSUnknown;
    }
    else if (sourceOptions.TraceResolution != Tristate.TSUnknown)
    {
        targetOptions.TraceResolution = sourceOptions.TraceResolution;
    }

    if (explicitNullFields.Has("tsBuildInfoFile"))
    {
        targetOptions.TsBuildInfoFile = "";
    }
    else if (sourceOptions.TsBuildInfoFile != "")
    {
        targetOptions.TsBuildInfoFile = sourceOptions.TsBuildInfoFile;
    }

    if (explicitNullFields.Has("typeRoots"))
    {
        targetOptions.TypeRoots = null;
    }
    else if (sourceOptions.TypeRoots is not null)
    {
        targetOptions.TypeRoots = sourceOptions.TypeRoots;
    }

    if (explicitNullFields.Has("types"))
    {
        targetOptions.Types = null;
    }
    else if (sourceOptions.Types is not null)
    {
        targetOptions.Types = sourceOptions.Types;
    }

    if (explicitNullFields.Has("useDefineForClassFields"))
    {
        targetOptions.UseDefineForClassFields = Tristate.TSUnknown;
    }
    else if (sourceOptions.UseDefineForClassFields != Tristate.TSUnknown)
    {
        targetOptions.UseDefineForClassFields = sourceOptions.UseDefineForClassFields;
    }

    if (explicitNullFields.Has("useUnknownInCatchVariables"))
    {
        targetOptions.UseUnknownInCatchVariables = Tristate.TSUnknown;
    }
    else if (sourceOptions.UseUnknownInCatchVariables != Tristate.TSUnknown)
    {
        targetOptions.UseUnknownInCatchVariables = sourceOptions.UseUnknownInCatchVariables;
    }

    if (explicitNullFields.Has("verbatimModuleSyntax"))
    {
        targetOptions.VerbatimModuleSyntax = Tristate.TSUnknown;
    }
    else if (sourceOptions.VerbatimModuleSyntax != Tristate.TSUnknown)
    {
        targetOptions.VerbatimModuleSyntax = sourceOptions.VerbatimModuleSyntax;
    }

    if (explicitNullFields.Has("maxNodeModuleJsDepth"))
    {
        targetOptions.MaxNodeModuleJsDepth = null;
    }
    else if (sourceOptions.MaxNodeModuleJsDepth is not null)
    {
        targetOptions.MaxNodeModuleJsDepth = sourceOptions.MaxNodeModuleJsDepth;
    }

    if (explicitNullFields.Has("allowSyntheticDefaultImports"))
    {
        targetOptions.AllowSyntheticDefaultImports = Tristate.TSUnknown;
    }
    else if (sourceOptions.AllowSyntheticDefaultImports != Tristate.TSUnknown)
    {
        targetOptions.AllowSyntheticDefaultImports = sourceOptions.AllowSyntheticDefaultImports;
    }

    if (explicitNullFields.Has("alwaysStrict"))
    {
        targetOptions.AlwaysStrict = Tristate.TSUnknown;
    }
    else if (sourceOptions.AlwaysStrict != Tristate.TSUnknown)
    {
        targetOptions.AlwaysStrict = sourceOptions.AlwaysStrict;
    }

    if (explicitNullFields.Has("baseUrl"))
    {
        targetOptions.BaseUrl = "";
    }
    else if (sourceOptions.BaseUrl != "")
    {
        targetOptions.BaseUrl = sourceOptions.BaseUrl;
    }

    if (explicitNullFields.Has("downlevelIteration"))
    {
        targetOptions.DownlevelIteration = Tristate.TSUnknown;
    }
    else if (sourceOptions.DownlevelIteration != Tristate.TSUnknown)
    {
        targetOptions.DownlevelIteration = sourceOptions.DownlevelIteration;
    }

    if (explicitNullFields.Has("esModuleInterop"))
    {
        targetOptions.ESModuleInterop = Tristate.TSUnknown;
    }
    else if (sourceOptions.ESModuleInterop != Tristate.TSUnknown)
    {
        targetOptions.ESModuleInterop = sourceOptions.ESModuleInterop;
    }

    if (explicitNullFields.Has("outFile"))
    {
        targetOptions.OutFile = "";
    }
    else if (sourceOptions.OutFile != "")
    {
        targetOptions.OutFile = sourceOptions.OutFile;
    }

    if (explicitNullFields.Has("configFilePath"))
    {
        targetOptions.ConfigFilePath = "";
    }
    else if (sourceOptions.ConfigFilePath != "")
    {
        targetOptions.ConfigFilePath = sourceOptions.ConfigFilePath;
    }

    if (explicitNullFields.Has("noDtsResolution"))
    {
        targetOptions.NoDtsResolution = Tristate.TSUnknown;
    }
    else if (sourceOptions.NoDtsResolution != Tristate.TSUnknown)
    {
        targetOptions.NoDtsResolution = sourceOptions.NoDtsResolution;
    }

    if (explicitNullFields.Has("pathsBasePath"))
    {
        targetOptions.PathsBasePath = "";
    }
    else if (sourceOptions.PathsBasePath != "")
    {
        targetOptions.PathsBasePath = sourceOptions.PathsBasePath;
    }

    if (explicitNullFields.Has("diagnostics"))
    {
        targetOptions.Diagnostics = Tristate.TSUnknown;
    }
    else if (sourceOptions.Diagnostics != Tristate.TSUnknown)
    {
        targetOptions.Diagnostics = sourceOptions.Diagnostics;
    }

    if (explicitNullFields.Has("extendedDiagnostics"))
    {
        targetOptions.ExtendedDiagnostics = Tristate.TSUnknown;
    }
    else if (sourceOptions.ExtendedDiagnostics != Tristate.TSUnknown)
    {
        targetOptions.ExtendedDiagnostics = sourceOptions.ExtendedDiagnostics;
    }

    if (explicitNullFields.Has("generateCpuProfile"))
    {
        targetOptions.GenerateCpuProfile = "";
    }
    else if (sourceOptions.GenerateCpuProfile != "")
    {
        targetOptions.GenerateCpuProfile = sourceOptions.GenerateCpuProfile;
    }

    if (explicitNullFields.Has("generateTrace"))
    {
        targetOptions.GenerateTrace = "";
    }
    else if (sourceOptions.GenerateTrace != "")
    {
        targetOptions.GenerateTrace = sourceOptions.GenerateTrace;
    }

    if (explicitNullFields.Has("listEmittedFiles"))
    {
        targetOptions.ListEmittedFiles = Tristate.TSUnknown;
    }
    else if (sourceOptions.ListEmittedFiles != Tristate.TSUnknown)
    {
        targetOptions.ListEmittedFiles = sourceOptions.ListEmittedFiles;
    }

    if (explicitNullFields.Has("listFiles"))
    {
        targetOptions.ListFiles = Tristate.TSUnknown;
    }
    else if (sourceOptions.ListFiles != Tristate.TSUnknown)
    {
        targetOptions.ListFiles = sourceOptions.ListFiles;
    }

    if (explicitNullFields.Has("explainFiles"))
    {
        targetOptions.ExplainFiles = Tristate.TSUnknown;
    }
    else if (sourceOptions.ExplainFiles != Tristate.TSUnknown)
    {
        targetOptions.ExplainFiles = sourceOptions.ExplainFiles;
    }

    if (explicitNullFields.Has("listFilesOnly"))
    {
        targetOptions.ListFilesOnly = Tristate.TSUnknown;
    }
    else if (sourceOptions.ListFilesOnly != Tristate.TSUnknown)
    {
        targetOptions.ListFilesOnly = sourceOptions.ListFilesOnly;
    }

    if (explicitNullFields.Has("noEmitForJsFiles"))
    {
        targetOptions.NoEmitForJsFiles = Tristate.TSUnknown;
    }
    else if (sourceOptions.NoEmitForJsFiles != Tristate.TSUnknown)
    {
        targetOptions.NoEmitForJsFiles = sourceOptions.NoEmitForJsFiles;
    }

    if (explicitNullFields.Has("preserveWatchOutput"))
    {
        targetOptions.PreserveWatchOutput = Tristate.TSUnknown;
    }
    else if (sourceOptions.PreserveWatchOutput != Tristate.TSUnknown)
    {
        targetOptions.PreserveWatchOutput = sourceOptions.PreserveWatchOutput;
    }

    if (explicitNullFields.Has("pretty"))
    {
        targetOptions.Pretty = Tristate.TSUnknown;
    }
    else if (sourceOptions.Pretty != Tristate.TSUnknown)
    {
        targetOptions.Pretty = sourceOptions.Pretty;
    }

    if (explicitNullFields.Has("version"))
    {
        targetOptions.Version = Tristate.TSUnknown;
    }
    else if (sourceOptions.Version != Tristate.TSUnknown)
    {
        targetOptions.Version = sourceOptions.Version;
    }

    if (explicitNullFields.Has("watch"))
    {
        targetOptions.Watch = Tristate.TSUnknown;
    }
    else if (sourceOptions.Watch != Tristate.TSUnknown)
    {
        targetOptions.Watch = sourceOptions.Watch;
    }

    if (explicitNullFields.Has("showConfig"))
    {
        targetOptions.ShowConfig = Tristate.TSUnknown;
    }
    else if (sourceOptions.ShowConfig != Tristate.TSUnknown)
    {
        targetOptions.ShowConfig = sourceOptions.ShowConfig;
    }

    if (explicitNullFields.Has("build"))
    {
        targetOptions.Build = Tristate.TSUnknown;
    }
    else if (sourceOptions.Build != Tristate.TSUnknown)
    {
        targetOptions.Build = sourceOptions.Build;
    }

    if (explicitNullFields.Has("help"))
    {
        targetOptions.Help = Tristate.TSUnknown;
    }
    else if (sourceOptions.Help != Tristate.TSUnknown)
    {
        targetOptions.Help = sourceOptions.Help;
    }

    if (explicitNullFields.Has("all"))
    {
        targetOptions.All = Tristate.TSUnknown;
    }
    else if (sourceOptions.All != Tristate.TSUnknown)
    {
        targetOptions.All = sourceOptions.All;
    }

    if (explicitNullFields.Has("runExternalCode"))
    {
        targetOptions.RunExternalCode = Tristate.TSUnknown;
    }
    else if (sourceOptions.RunExternalCode != Tristate.TSUnknown)
    {
        targetOptions.RunExternalCode = sourceOptions.RunExternalCode;
    }

    if (explicitNullFields.Has("pprofDir"))
    {
        targetOptions.PprofDir = "";
    }
    else if (sourceOptions.PprofDir != "")
    {
        targetOptions.PprofDir = sourceOptions.PprofDir;
    }

    if (explicitNullFields.Has("singleThreaded"))
    {
        targetOptions.SingleThreaded = Tristate.TSUnknown;
    }
    else if (sourceOptions.SingleThreaded != Tristate.TSUnknown)
    {
        targetOptions.SingleThreaded = sourceOptions.SingleThreaded;
    }

    if (explicitNullFields.Has("quiet"))
    {
        targetOptions.Quiet = Tristate.TSUnknown;
    }
    else if (sourceOptions.Quiet != Tristate.TSUnknown)
    {
        targetOptions.Quiet = sourceOptions.Quiet;
    }

    if (explicitNullFields.Has("checkers"))
    {
        targetOptions.Checkers = null;
    }
    else if (sourceOptions.Checkers is not null)
    {
        targetOptions.Checkers = sourceOptions.Checkers;
    }

        return targetOptions;
    }

    internal static OrderedMap<string, object?>? ConvertToOptionsWithAbsolutePaths(OrderedMap<string, object?>? optionsBase, TsOptions.CommandLineOptionNameMap optionMap, string cwd)
    {
        // !!! convert to options with absolute paths was previously done with `CompilerOptions` object, but for ease of implementation, we do it pre-conversion.
        // !!! Revisit this choice if/when refactoring when conversion is done in tsconfig parsing
        if (optionsBase is null)
        {
            return null;
        }
        foreach (var (o, v) in optionsBase.Entries())
        {
            var (result, ok) = ConvertOptionToAbsolutePath(o, v, optionMap, cwd);
            if (ok)
            {
                optionsBase.Set(o, result);
            }
        }
        return optionsBase;
    }

    internal static (object? Value, bool Ok) ConvertOptionToAbsolutePath(string o, object? v, TsOptions.CommandLineOptionNameMap optionMap, string cwd)
    {
        var option = optionMap.Get(o);
        if (option is null)
        {
            return (null, false);
        }
        if (option.Kind == CommandLineOptionKind.List)
        {
            if (option.Elements()!.IsFilePath)
            {
                if (v is string[] arr)
                {
                    return (Map(arr, item => GetNormalizedAbsolutePath(item, cwd)), true);
                }
                if (v is object?[] anyArr)
                {
                    return (Map(anyArr, item =>
                    {
                        if (item is string s)
                        {
                            return (object?)GetNormalizedAbsolutePath(s, cwd);
                        }
                        return item;
                    }), true);
                }
            }
        }
        else if (option.IsFilePath)
        {
            if (v is string @value)
            {
                return (GetNormalizedAbsolutePath(@value, cwd), true);
            }
        }
        return (null, false);
    }
}
