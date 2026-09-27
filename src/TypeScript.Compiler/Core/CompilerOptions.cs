// ported from: internal/core/compileroptions.go
//
// JSON tag 来源（本轮不实现映射，见 docs/json.md §4）：
// Go 的每个字段都带 `json:"<name>,omitzero"`，<name> 是字段名首字母小写（AllowJs → "allowJs"、
// ESModuleInterop → "esModuleInterop"、CheckJs → "checkJs"），"omitzero" 表示零值省略；
// 另有 `deprecated:"true"`（只在 options 解析/校验内部使用）和 `internal:"true"`（内部字段）两类标记。
// 下方按 Go 的分组保留了这些标记的注释。C# 侧 JSON 无反射（docs §3.6），需要序列化时由显式
// IJsonMarshaler/IJsonUnmarshaler mapper 按这些名字读写，字段本身不挂任何 attribute。
//
// 其它偏离 Go 的地方：
// 1) Go 是可变的 `*CompilerOptions` 结构体；C# 用 class（引用语义一致、字段可变）。
// 2) `Clone()`：Go 用 reflect 逐字段浅拷贝（源码自己标了 "could be generated code instead of
//    reflection"）→ C# 直接 `(CompilerOptions)MemberwiseClone()`（docs §3.2）。
// 3) `_ noCopy` 字段及其 Lock/Unlock（go vet 的 -copylocks 检查用）跳过：C# 引用类型没有对应物。
// 4) `optionsType`（reflect.TypeFor[CompilerOptions]）只服务 Clone 的反射，跳过。
// 5) string 字段默认 `""`（Go 的零值；`ConfigFilePath != ""`、`PathsBasePath != ""` 这类判断依赖它）、
//    slice 字段 `T[]?`（null = Go 的 nil；`GetEffectiveTypeRoots` 的 `TypeRoots != nil` 依赖它）、
//    `*int` → `int?`、`*collections.OrderedMap[...]` → `OrderedMap<...>?`。
// 6) 枚举成员名 = Go 常量名去掉类型前缀（见各枚举）；Go 的 `String()` stringer 见
//    ModuleKindStringer.Generated.cs / ScriptTargetStringer.Generated.cs，手写的 String() 在本文件里落成
//    Core 的扩展方法（调用点保留 Go 的 `x.String()`）。别名常量（ScriptTargetLatest/LatestStandard、
//    ResolutionMode 系列）是 `Core` 的 const，因为 C# enum 的别名成员会污染 ToString；`type ResolutionMode
//    = ModuleKind` 是类型别名，C# 没有别名，所有出现 `ResolutionMode` 的地方一律写 `ModuleKind`。
using TypeScript.Collections;
using static TypeScript.TSPath.TSPath;

namespace TypeScript.Core;

// Go: type PluginImport struct { Name string `json:"name"` }
public sealed class PluginImport
{
    public string Name = "";
}

// CompilerOptions contains the compiler options exposed by the API.
public sealed class CompilerOptions
{
    public Tristate AllowJs;
    public Tristate AllowArbitraryExtensions;
    public Tristate AllowImportingTsExtensions;
    public Tristate AllowNonTsExtensions;
    public Tristate AllowUmdGlobalAccess;
    public Tristate AllowUnreachableCode;
    public Tristate AllowUnusedLabels;
    public Tristate AssumeChangesOnlyAffectDirectDependencies;
    public Tristate CheckJs;
    public string[]? CustomConditions;
    public Tristate Composite;
    public Tristate EmitDeclarationOnly;
    public Tristate EmitBOM;
    public Tristate EmitDecoratorMetadata;
    public Tristate Declaration;
    public string DeclarationDir = "";
    public Tristate DeclarationMap;
    public Tristate DeduplicatePackages;
    public Tristate DisableSizeLimit;
    public Tristate DisableSourceOfProjectReferenceRedirect;
    public Tristate DisableSolutionSearching;
    public Tristate DisableReferencedProjectLoad;
    public Tristate ErasableSyntaxOnly;
    public Tristate ExactOptionalPropertyTypes;
    public Tristate ExperimentalDecorators;
    public Tristate ForceConsistentCasingInFileNames;
    public Tristate IsolatedModules;
    public Tristate IsolatedDeclarations;
    public Tristate IgnoreConfig;
    public string IgnoreDeprecations = "";
    public Tristate ImportHelpers;
    public Tristate InlineSourceMap;
    public Tristate InlineSources;
    public Tristate Init;
    public Tristate Incremental;
    public JsxEmit Jsx;
    public string JsxFactory = "";
    public string JsxFragmentFactory = "";
    public string JsxImportSource = "";
    public string[]? Lib;
    public Tristate LibReplacement;
    public string Locale = "";
    public string MapRoot = "";
    public ModuleKind Module;
    public ModuleResolutionKind ModuleResolution;
    public string[]? ModuleSuffixes;
    public ModuleDetectionKind ModuleDetection;
    public NewLineKind NewLine;
    public Tristate NoEmit;
    public Tristate NoCheck;
    public Tristate NoErrorTruncation;
    public Tristate NoFallthroughCasesInSwitch;
    public Tristate NoImplicitAny;
    public Tristate NoImplicitThis;
    public Tristate NoImplicitReturns;
    public Tristate NoEmitHelpers;
    public Tristate NoLib;
    public Tristate NoPropertyAccessFromIndexSignature;
    public Tristate NoUncheckedIndexedAccess;
    public Tristate NoEmitOnError;
    public Tristate NoUnusedLocals;
    public Tristate NoUnusedParameters;
    public Tristate NoResolve;
    public Tristate NoImplicitOverride;
    public Tristate NoUncheckedSideEffectImports;
    public string OutDir = "";
    public OrderedMap<string, string[]>? Paths;

    // Plugins are parsed only so tools can report that native TypeScript does not support them.
    public PluginImport[]? Plugins;
    public Tristate PreserveConstEnums;
    public Tristate PreserveSymlinks;
    public string Project = "";
    public Tristate ResolveJsonModule;
    public Tristate ResolvePackageJsonExports;
    public Tristate ResolvePackageJsonImports;
    public Tristate RemoveComments;
    public Tristate RewriteRelativeImportExtensions;
    public string ReactNamespace = "";
    public string RootDir = "";
    public string[]? RootDirs;
    public Tristate SkipLibCheck;
    public Tristate StableTypeOrdering;
    public Tristate Strict;
    public Tristate StrictBindCallApply;
    public Tristate StrictBuiltinIteratorReturn;
    public Tristate StrictFunctionTypes;
    public Tristate StrictNullChecks;
    public Tristate StrictPropertyInitialization;
    public Tristate StripInternal;
    public Tristate SkipDefaultLibCheck;
    public Tristate SourceMap;
    public string SourceRoot = "";
    public Tristate SuppressOutputPathCheck;
    public ScriptTarget Target;
    public Tristate TraceResolution;
    public string TsBuildInfoFile = "";
    public string[]? TypeRoots;
    public string[]? Types;
    public Tristate UseDefineForClassFields;
    public Tristate UseUnknownInCatchVariables;
    public Tristate VerbatimModuleSyntax;
    public int? MaxNodeModuleJsDepth;

    // Deprecated: Do not use outside of options parsing and validation.
    public Tristate AllowSyntheticDefaultImports;
    // Deprecated: Do not use outside of options parsing and validation.
    public Tristate AlwaysStrict;
    // Deprecated: Do not use outside of options parsing and validation.
    public string BaseUrl = "";
    // Deprecated: Do not use outside of options parsing and validation.
    public Tristate DownlevelIteration;
    // Deprecated: Do not use outside of options parsing and validation.
    public Tristate ESModuleInterop;
    // Deprecated: Do not use outside of options parsing and validation.
    public string OutFile = "";

    // Internal fields
    public string ConfigFilePath = ""; // internal, but intentionally exposed via API
    public Tristate NoDtsResolution;
    public string PathsBasePath = "";
    public Tristate Diagnostics;
    public Tristate ExtendedDiagnostics;
    public string GenerateCpuProfile = "";
    public string GenerateTrace = "";
    public Tristate ListEmittedFiles;
    public Tristate ListFiles;
    public Tristate ExplainFiles;
    public Tristate ListFilesOnly;
    public Tristate NoEmitForJsFiles;
    public Tristate PreserveWatchOutput;
    public Tristate Pretty;
    public Tristate Version;
    public Tristate Watch;
    public Tristate ShowConfig;
    public Tristate Build;
    public Tristate Help;
    public Tristate All;
    public Tristate RunExternalCode;

    public string PprofDir = "";
    public Tristate SingleThreaded;
    public Tristate Quiet;
    public int? Checkers;

    // Clone creates a shallow copy of the CompilerOptions.
    public CompilerOptions Clone()
    {
        return (CompilerOptions)MemberwiseClone();
    }

    public ScriptTarget GetEmitScriptTarget()
    {
        if (Target != ScriptTarget.None)
        {
            return Target;
        }
        return Core.ScriptTargetLatestStandard;
    }

    public ModuleKind GetEmitModuleKind()
    {
        if (Module != ModuleKind.None)
        {
            return Module;
        }

        var target = GetEmitScriptTarget();
        if (target == ScriptTarget.ESNext)
        {
            return ModuleKind.ESNext;
        }
        if (target >= ScriptTarget.ES2022)
        {
            return ModuleKind.ES2022;
        }
        if (target >= ScriptTarget.ES2020)
        {
            return ModuleKind.ES2020;
        }
        if (target >= ScriptTarget.ES2015)
        {
            return ModuleKind.ES2015;
        }
        return ModuleKind.CommonJS;
    }

    public ModuleResolutionKind GetModuleResolutionKind()
    {
        switch (ModuleResolution)
        {
            case ModuleResolutionKind.Unknown:
            case ModuleResolutionKind.Classic:
            case ModuleResolutionKind.Node10:
                switch (GetEmitModuleKind())
                {
                    case ModuleKind.Node16:
                    case ModuleKind.Node18:
                    case ModuleKind.Node20:
                        return ModuleResolutionKind.Node16;
                    case ModuleKind.NodeNext:
                        return ModuleResolutionKind.NodeNext;
                    default:
                        return ModuleResolutionKind.Bundler;
                }
            default:
                return ModuleResolution;
        }
    }

    public ModuleDetectionKind GetEmitModuleDetectionKind()
    {
        if (ModuleDetection != ModuleDetectionKind.None)
        {
            return ModuleDetection;
        }
        var moduleKind = GetEmitModuleKind();
        if (ModuleKind.Node16 <= moduleKind && moduleKind <= ModuleKind.NodeNext)
        {
            return ModuleDetectionKind.Force;
        }
        return ModuleDetectionKind.Auto;
    }

    public bool GetResolvePackageJsonExports()
    {
        return ResolvePackageJsonExports.IsTrueOrUnknown();
    }

    public bool GetResolvePackageJsonImports()
    {
        return ResolvePackageJsonImports.IsTrueOrUnknown();
    }

    public bool GetAllowImportingTsExtensions()
    {
        return AllowImportingTsExtensions.IsTrue() || RewriteRelativeImportExtensions.IsTrue();
    }

    public bool AllowImportingTsExtensionsFrom(string fileName)
    {
        return GetAllowImportingTsExtensions() || IsDeclarationFileName(fileName);
    }

    public bool GetResolveJsonModule()
    {
        if (ResolveJsonModule != Tristate.TSUnknown)
        {
            return ResolveJsonModule == Tristate.TSTrue;
        }
        switch (GetEmitModuleKind())
        {
            // TODO in 6.0: add Node16/Node18
            case ModuleKind.Node20:
            case ModuleKind.NodeNext:
                return true;
        }
        return GetModuleResolutionKind() == ModuleResolutionKind.Bundler;
    }

    public bool ShouldPreserveConstEnums()
    {
        return PreserveConstEnums == Tristate.TSTrue || GetIsolatedModules();
    }

    public bool GetAllowJS()
    {
        if (AllowJs != Tristate.TSUnknown)
        {
            return AllowJs == Tristate.TSTrue;
        }
        return CheckJs == Tristate.TSTrue;
    }

    public bool GetJSXTransformEnabled()
    {
        var jsx = Jsx;
        return jsx == JsxEmit.React || jsx == JsxEmit.ReactJSX || jsx == JsxEmit.ReactJSXDev;
    }

    public bool GetStrictOptionValue(Tristate value)
    {
        if (value != Tristate.TSUnknown)
        {
            return value == Tristate.TSTrue;
        }
        return Strict != Tristate.TSFalse;
    }

    public (string[] Result, bool FromConfig) GetEffectiveTypeRoots(string currentDirectory)
    {
        var configuredTypeRoots = TypeRoots;
        if (configuredTypeRoots != null)
        {
            return (configuredTypeRoots, true);
        }
        string baseDir;
        if (ConfigFilePath != "")
        {
            baseDir = GetDirectoryPath(ConfigFilePath);
        }
        else
        {
            baseDir = currentDirectory;
            if (baseDir == "")
            {
                // This was accounted for in the TS codebase, but only for third-party API usage
                // where the module resolution host does not provide a getCurrentDirectory().
                Debug.Fail("cannot get effective type roots without a config file path or current directory");
            }
        }

        // Go: make([]string, 0, strings.Count(baseDir, "/")) —— 容量提示不是语义，用无提示的 List。
        var typeRoots = new List<string>();
        ForEachAncestorDirectory(baseDir, (string dir) =>
        {
            typeRoots.Add(CombinePaths(dir, "node_modules", "@types"));
            return (0, false);
        }, out int _);
        return (typeRoots.ToArray(), false);
    }

    // UsesWildcardTypes returns true if this option's types array includes "*"
    public bool UsesWildcardTypes()
    {
        // Go: slices.Contains(options.Types, "*")（nil slice → false）
        return Types != null && Array.IndexOf(Types, "*") >= 0;
    }

    public bool GetIsolatedModules()
    {
        return IsolatedModules == Tristate.TSTrue || VerbatimModuleSyntax == Tristate.TSTrue;
    }

    public bool IsIncremental()
    {
        return Incremental.IsTrue() || Composite.IsTrue();
    }

    public bool GetEmitStandardClassFields()
    {
        return UseDefineForClassFields != Tristate.TSFalse && GetEmitScriptTarget() >= ScriptTarget.ES2022;
    }

    public bool GetUseDefineForClassFields()
    {
        if (UseDefineForClassFields == Tristate.TSUnknown)
        {
            return GetEmitScriptTarget() >= ScriptTarget.ES2022;
        }
        return UseDefineForClassFields == Tristate.TSTrue;
    }

    public bool GetEmitDeclarations()
    {
        return Declaration.IsTrue() || Composite.IsTrue();
    }

    public bool GetAreDeclarationMapsEnabled()
    {
        return DeclarationMap == Tristate.TSTrue && GetEmitDeclarations();
    }

    public bool HasJsonModuleEmitEnabled()
    {
        switch (GetEmitModuleKind())
        {
            case ModuleKind.System:
            case ModuleKind.UMD:
                return false;
        }
        return true;
    }

    public string GetPathsBasePath(string currentDirectory)
    {
        if ((Paths?.Size() ?? 0) == 0)
        {
            return "";
        }
        if (PathsBasePath != "")
        {
            return PathsBasePath;
        }
        return currentDirectory;
    }
}

public enum ModuleDetectionKind : int
{
    None = 0,
    Auto = 1,
    Legacy = 2,
    Force = 3,
}

public enum ModuleKind : int
{
    None = 0,
    CommonJS = 1,
    // Deprecated: Do not use outside of options parsing and validation.
    AMD = 2,
    // Deprecated: Do not use outside of options parsing and validation.
    UMD = 3,
    // Deprecated: Do not use outside of options parsing and validation.
    System = 4,
    // NOTE: ES module kinds should be contiguous to more easily check whether a module kind is *any* ES module kind.
    //       Non-ES module kinds should not come between ES2015 (the earliest ES module kind) and ESNext (the last ES
    //       module kind).
    ES2015 = 5,
    ES2020 = 6,
    ES2022 = 7,
    ESNext = 99,
    // Node16+ is an amalgam of commonjs (albeit updated) and es2022+, and represents a distinct module system from es2020/esnext
    Node16 = 100,
    Node18 = 101,
    Node20 = 102,
    NodeNext = 199,
    // Emit as written
    Preserve = 200,
}

public enum ModuleResolutionKind : int
{
    Unknown = 0,
    // Deprecated: Do not use outside of options parsing and validation.
    Classic = 1,
    // Deprecated: Do not use outside of options parsing and validation.
    Node10 = 2,
    // Starting with node16, node's module resolver has significant departures from traditional cjs resolution
    // to better support ECMAScript modules and their use within node - however more features are still being added.
    // TypeScript's Node ESM support was introduced after Node 12 went end-of-life, and Node 14 is the earliest stable
    // version that supports both pattern trailers - *but*, Node 16 is the first version that also supports ECMAScript 2022.
    // In turn, we offer both a `NodeNext` moving resolution target, and a `Node16` version-anchored resolution target
    Node16 = 3,
    NodeNext = 99, // Not simply `Node16` so that compiled code linked against TS can use the `Next` value reliably (same as with `ModuleKind`)
    Bundler = 100,
}

public enum NewLineKind : int
{
    None = 0,
    CRLF = 1,
    LF = 2,
}

public enum ScriptTarget : int
{
    None = 0,
    // Deprecated: Do not use outside of options parsing and validation.
    ES5 = 1,
    ES2015 = 2,
    ES2016 = 3,
    ES2017 = 4,
    ES2018 = 5,
    ES2019 = 6,
    ES2020 = 7,
    ES2021 = 8,
    ES2022 = 9,
    ES2023 = 10,
    ES2024 = 11,
    ES2025 = 12,
    ESNext = 99,
    JSON = 100,
}

public enum JsxEmit : int
{
    None = 0,
    Preserve = 1,
    React = 2,
    ReactNative = 3,
    ReactJSX = 4,
    ReactJSXDev = 5,
}

public static partial class Core
{
    /// <summary>Go 的 <c>var EmptyCompilerOptions = &amp;CompilerOptions{}</c>：共享的可变空选项实例。</summary>
    public static readonly CompilerOptions EmptyCompilerOptions = new();

    // Go: var ModuleKindToModuleResolutionKind = map[ModuleKind]ModuleResolutionKind{…}
    public static readonly Dictionary<ModuleKind, ModuleResolutionKind> ModuleKindToModuleResolutionKind = new()
    {
        { ModuleKind.Node16, ModuleResolutionKind.Node16 },
        { ModuleKind.NodeNext, ModuleResolutionKind.NodeNext },
    };

    // Go: type ResolutionMode = ModuleKind（ModuleKindNone | ModuleKindCommonJS | ModuleKindESNext）
    //     + const ResolutionModeNone/ResolutionModeCommonJS/ResolutionModeESM。
    // C# 没有类型别名，`ResolutionMode` 出现处一律写 `ModuleKind`。
    public const ModuleKind ResolutionModeNone = ModuleKind.None;
    public const ModuleKind ResolutionModeCommonJS = ModuleKind.CommonJS;
    public const ModuleKind ResolutionModeESM = ModuleKind.ESNext;

    // Go: ScriptTargetLatest / ScriptTargetLatestStandard（= ESNext / ES2025 的别名常量）。
    // 不能做 enum 成员：C# 的重复值成员会让 ToString() 可能返回 "Latest"/"LatestStandard"，
    // 而 Go 的 stringer 固定返回 "ESNext"/"ES2025"。
    public const ScriptTarget ScriptTargetLatest = ScriptTarget.ESNext;
    public const ScriptTarget ScriptTargetLatestStandard = ScriptTarget.ES2025;

    public static NewLineKind GetNewLineKind(string s)
    {
        switch (s)
        {
            case "\r\n":
                return NewLineKind.CRLF;
            case "\n":
                return NewLineKind.LF;
            default:
                return NewLineKind.None;
        }
    }

    // Go: func (newLine NewLineKind) GetNewLineCharacter() string
    public static string GetNewLineCharacter(this NewLineKind newLine)
    {
        switch (newLine)
        {
            case NewLineKind.CRLF:
                return "\r\n";
            default:
                return "\n";
        }
    }

    // Go: func (moduleKind ModuleKind) IsNonNodeESM() bool
    public static bool IsNonNodeESM(this ModuleKind moduleKind)
    {
        return moduleKind >= ModuleKind.ES2015 && moduleKind <= ModuleKind.ESNext;
    }

    // Go: func (moduleKind ModuleKind) SupportsImportAttributes() bool
    public static bool SupportsImportAttributes(this ModuleKind moduleKind)
    {
        return ModuleKind.Node18 <= moduleKind && moduleKind <= ModuleKind.NodeNext ||
            moduleKind == ModuleKind.Preserve ||
            moduleKind == ModuleKind.ESNext;
    }

    // We don't use stringer on this for now, because these values
    // are user-facing in --traceResolution, and stringer currently
    // lacks the ability to remove the "ModuleResolutionKind" prefix
    // when generating code for multiple types into the same output
    // file. Additionally, since there's no TS equivalent of
    // `ModuleResolutionKindUnknown`, we want to panic on that case,
    // as it probably represents a mistake when porting TS to Go.
    //
    // Go: func (m ModuleResolutionKind) String() string（panic → Debug.Fail）
    public static string String(this ModuleResolutionKind m)
    {
        switch (m)
        {
            case ModuleResolutionKind.Unknown:
                Debug.Fail("should not use zero value of ModuleResolutionKind");
                throw new System.Diagnostics.UnreachableException();
            case ModuleResolutionKind.Classic:
                return "Classic";
            case ModuleResolutionKind.Node10:
                return "Node10";
            case ModuleResolutionKind.Node16:
                return "Node16";
            case ModuleResolutionKind.NodeNext:
                return "NodeNext";
            case ModuleResolutionKind.Bundler:
                return "Bundler";
            default:
                Debug.Fail("unhandled case in ModuleResolutionKind.String");
                throw new System.Diagnostics.UnreachableException();
        }
    }

    // Go: func (j JsxEmit) String() string（panic → Debug.Fail；注意返回值是小写的 kebab-case，
    // 与 C# 的枚举成员名不同，所以必须走这个扩展方法而不是 ToString()）
    public static string String(this JsxEmit j)
    {
        switch (j)
        {
            case JsxEmit.None:
                Debug.Fail("should not use zero value of JsxEmit");
                throw new System.Diagnostics.UnreachableException();
            case JsxEmit.Preserve:
                return "preserve";
            case JsxEmit.ReactNative:
                return "react-native";
            case JsxEmit.React:
                return "react";
            case JsxEmit.ReactJSX:
                return "react-jsx";
            case JsxEmit.ReactJSXDev:
                return "react-jsxdev";
            default:
                Debug.Fail("unhandled case in JsxEmit.String");
                throw new System.Diagnostics.UnreachableException();
        }
    }
}
