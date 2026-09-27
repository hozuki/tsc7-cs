// ported from: internal/core/compileroptions.go（字段表对应 tsoptions/contentmapper 里的 reflect 用法：
//              internal/tsoptions/showconfig.go serializeCompilerOptions、
//              internal/tsoptions/declscompiler.go optionsHaveChanges/ForEachCompilerOptionValue、
//              internal/contentmapper/contentmapper.go compilerOptionFields）
//
// Go 用 reflect 遍历 CompilerOptions 的导出字段；C# 禁反射（docs/architecture.md §3.6），
// 所以这里把「字段名 + json 名 + 取值 + 是否零值 + 是否变化」显式列成表，逐字段对应 Go 的 reflect 语义：
//   Name        = field.Name（= CommandLineCompilerOptionsMap 的查找键）
//   JsonName    = struct tag 的 json 名（contentmapper 的 manifest.compilerOptions 用的就是它）
//   Get         = fieldValue.Interface()（装箱值；--showConfig 序列化用）
//   IsZero      = fieldValue.IsZero()（注意 Go 的 *int 为 nil 才是零值，指向 0 的指针不是）
//   Changed     = !reflect.DeepEqual(newValue, oldValue)（nil slice 与非 nil 空 slice 不相等；
//                 OrderedMap 逐键值比较）
//   GetTristate 只有 Tristate 字段非 null，供 declscompiler.go 的 strictFlag 分支使用。
//
// **表的顺序 = Go struct 的字段顺序**（= Core/CompilerOptions.cs 的顺序）：showconfig 的输出键顺序
// 依赖它（--showConfig 逐字节对齐）。Go 用字段下标 i 取 oldOptions 的对应字段，C# 里下标就是本表下标。
//
// 重新生成：bun artifacts/TsOptionsConfig/gen_option_fields.js
using TypeScript.Collections;
using TypeScript.Core;

namespace TypeScript.TsOptions;

/// <summary>Go 的 <c>reflect.StructField</c> 对应物：CompilerOptions 的一个导出字段。</summary>
internal sealed class CompilerOptionField
{
    /// <summary>Go 的字段名。</summary>
    public readonly string Name;

    /// <summary>Go 的 struct tag 里 <c>json</c> 的名字部分（首字母小写的字段名）。</summary>
    public readonly string JsonName;

    /// <summary>Go 的 <c>fieldValue.Interface()</c>。</summary>
    public readonly Func<CompilerOptions, object?> Get;

    /// <summary>Go 的 <c>fieldValue.IsZero()</c>。</summary>
    public readonly Func<CompilerOptions, bool> IsZero;

    /// <summary>Go 的 <c>!reflect.DeepEqual(newValue, oldValue)</c>。</summary>
    public readonly Func<CompilerOptions, CompilerOptions, bool> Changed;

    /// <summary>Tristate 字段的强类型取值（非 Tristate 字段为 null）。</summary>
    public readonly Func<CompilerOptions, Tristate>? GetTristate;

    public CompilerOptionField(
        string name,
        string jsonName,
        Func<CompilerOptions, object?> get,
        Func<CompilerOptions, bool> isZero,
        Func<CompilerOptions, CompilerOptions, bool> changed,
        Func<CompilerOptions, Tristate>? getTristate = null)
    {
        Name = name;
        JsonName = jsonName;
        Get = get;
        IsZero = isZero;
        Changed = changed;
        GetTristate = getTristate;
    }
}

public static partial class TsOptions
{
    // Go: core.CompilerOptions 的导出字段（reflect 版的 field.Name / json tag / Interface() / IsZero() / DeepEqual）。
    // 名字保持 Go 的导出面（PascalCase），落成只读属性以符合 IDE1006（非公开字段须 `_camelCase`）。
    internal static CompilerOptionField[] CompilerOptionFields => _compilerOptionFields;

    private static readonly CompilerOptionField[] _compilerOptionFields =
    [
        new("AllowJs", "allowJs", static o => o.AllowJs, static o => o.AllowJs == Tristate.TSUnknown, static (a, b) => a.AllowJs != b.AllowJs, static o => o.AllowJs),
        new("AllowArbitraryExtensions", "allowArbitraryExtensions", static o => o.AllowArbitraryExtensions, static o => o.AllowArbitraryExtensions == Tristate.TSUnknown, static (a, b) => a.AllowArbitraryExtensions != b.AllowArbitraryExtensions, static o => o.AllowArbitraryExtensions),
        new("AllowImportingTsExtensions", "allowImportingTsExtensions", static o => o.AllowImportingTsExtensions, static o => o.AllowImportingTsExtensions == Tristate.TSUnknown, static (a, b) => a.AllowImportingTsExtensions != b.AllowImportingTsExtensions, static o => o.AllowImportingTsExtensions),
        new("AllowNonTsExtensions", "allowNonTsExtensions", static o => o.AllowNonTsExtensions, static o => o.AllowNonTsExtensions == Tristate.TSUnknown, static (a, b) => a.AllowNonTsExtensions != b.AllowNonTsExtensions, static o => o.AllowNonTsExtensions),
        new("AllowUmdGlobalAccess", "allowUmdGlobalAccess", static o => o.AllowUmdGlobalAccess, static o => o.AllowUmdGlobalAccess == Tristate.TSUnknown, static (a, b) => a.AllowUmdGlobalAccess != b.AllowUmdGlobalAccess, static o => o.AllowUmdGlobalAccess),
        new("AllowUnreachableCode", "allowUnreachableCode", static o => o.AllowUnreachableCode, static o => o.AllowUnreachableCode == Tristate.TSUnknown, static (a, b) => a.AllowUnreachableCode != b.AllowUnreachableCode, static o => o.AllowUnreachableCode),
        new("AllowUnusedLabels", "allowUnusedLabels", static o => o.AllowUnusedLabels, static o => o.AllowUnusedLabels == Tristate.TSUnknown, static (a, b) => a.AllowUnusedLabels != b.AllowUnusedLabels, static o => o.AllowUnusedLabels),
        new("AssumeChangesOnlyAffectDirectDependencies", "assumeChangesOnlyAffectDirectDependencies", static o => o.AssumeChangesOnlyAffectDirectDependencies, static o => o.AssumeChangesOnlyAffectDirectDependencies == Tristate.TSUnknown, static (a, b) => a.AssumeChangesOnlyAffectDirectDependencies != b.AssumeChangesOnlyAffectDirectDependencies, static o => o.AssumeChangesOnlyAffectDirectDependencies),
        new("CheckJs", "checkJs", static o => o.CheckJs, static o => o.CheckJs == Tristate.TSUnknown, static (a, b) => a.CheckJs != b.CheckJs, static o => o.CheckJs),
        new("CustomConditions", "customConditions", static o => o.CustomConditions, static o => o.CustomConditions is null, static (a, b) => !SameStringArray(a.CustomConditions, b.CustomConditions), null),
        new("Composite", "composite", static o => o.Composite, static o => o.Composite == Tristate.TSUnknown, static (a, b) => a.Composite != b.Composite, static o => o.Composite),
        new("EmitDeclarationOnly", "emitDeclarationOnly", static o => o.EmitDeclarationOnly, static o => o.EmitDeclarationOnly == Tristate.TSUnknown, static (a, b) => a.EmitDeclarationOnly != b.EmitDeclarationOnly, static o => o.EmitDeclarationOnly),
        new("EmitBOM", "emitBOM", static o => o.EmitBOM, static o => o.EmitBOM == Tristate.TSUnknown, static (a, b) => a.EmitBOM != b.EmitBOM, static o => o.EmitBOM),
        new("EmitDecoratorMetadata", "emitDecoratorMetadata", static o => o.EmitDecoratorMetadata, static o => o.EmitDecoratorMetadata == Tristate.TSUnknown, static (a, b) => a.EmitDecoratorMetadata != b.EmitDecoratorMetadata, static o => o.EmitDecoratorMetadata),
        new("Declaration", "declaration", static o => o.Declaration, static o => o.Declaration == Tristate.TSUnknown, static (a, b) => a.Declaration != b.Declaration, static o => o.Declaration),
        new("DeclarationDir", "declarationDir", static o => o.DeclarationDir, static o => o.DeclarationDir.Length == 0, static (a, b) => a.DeclarationDir != b.DeclarationDir, null),
        new("DeclarationMap", "declarationMap", static o => o.DeclarationMap, static o => o.DeclarationMap == Tristate.TSUnknown, static (a, b) => a.DeclarationMap != b.DeclarationMap, static o => o.DeclarationMap),
        new("DeduplicatePackages", "deduplicatePackages", static o => o.DeduplicatePackages, static o => o.DeduplicatePackages == Tristate.TSUnknown, static (a, b) => a.DeduplicatePackages != b.DeduplicatePackages, static o => o.DeduplicatePackages),
        new("DisableSizeLimit", "disableSizeLimit", static o => o.DisableSizeLimit, static o => o.DisableSizeLimit == Tristate.TSUnknown, static (a, b) => a.DisableSizeLimit != b.DisableSizeLimit, static o => o.DisableSizeLimit),
        new("DisableSourceOfProjectReferenceRedirect", "disableSourceOfProjectReferenceRedirect", static o => o.DisableSourceOfProjectReferenceRedirect, static o => o.DisableSourceOfProjectReferenceRedirect == Tristate.TSUnknown, static (a, b) => a.DisableSourceOfProjectReferenceRedirect != b.DisableSourceOfProjectReferenceRedirect, static o => o.DisableSourceOfProjectReferenceRedirect),
        new("DisableSolutionSearching", "disableSolutionSearching", static o => o.DisableSolutionSearching, static o => o.DisableSolutionSearching == Tristate.TSUnknown, static (a, b) => a.DisableSolutionSearching != b.DisableSolutionSearching, static o => o.DisableSolutionSearching),
        new("DisableReferencedProjectLoad", "disableReferencedProjectLoad", static o => o.DisableReferencedProjectLoad, static o => o.DisableReferencedProjectLoad == Tristate.TSUnknown, static (a, b) => a.DisableReferencedProjectLoad != b.DisableReferencedProjectLoad, static o => o.DisableReferencedProjectLoad),
        new("ErasableSyntaxOnly", "erasableSyntaxOnly", static o => o.ErasableSyntaxOnly, static o => o.ErasableSyntaxOnly == Tristate.TSUnknown, static (a, b) => a.ErasableSyntaxOnly != b.ErasableSyntaxOnly, static o => o.ErasableSyntaxOnly),
        new("ExactOptionalPropertyTypes", "exactOptionalPropertyTypes", static o => o.ExactOptionalPropertyTypes, static o => o.ExactOptionalPropertyTypes == Tristate.TSUnknown, static (a, b) => a.ExactOptionalPropertyTypes != b.ExactOptionalPropertyTypes, static o => o.ExactOptionalPropertyTypes),
        new("ExperimentalDecorators", "experimentalDecorators", static o => o.ExperimentalDecorators, static o => o.ExperimentalDecorators == Tristate.TSUnknown, static (a, b) => a.ExperimentalDecorators != b.ExperimentalDecorators, static o => o.ExperimentalDecorators),
        new("ForceConsistentCasingInFileNames", "forceConsistentCasingInFileNames", static o => o.ForceConsistentCasingInFileNames, static o => o.ForceConsistentCasingInFileNames == Tristate.TSUnknown, static (a, b) => a.ForceConsistentCasingInFileNames != b.ForceConsistentCasingInFileNames, static o => o.ForceConsistentCasingInFileNames),
        new("IsolatedModules", "isolatedModules", static o => o.IsolatedModules, static o => o.IsolatedModules == Tristate.TSUnknown, static (a, b) => a.IsolatedModules != b.IsolatedModules, static o => o.IsolatedModules),
        new("IsolatedDeclarations", "isolatedDeclarations", static o => o.IsolatedDeclarations, static o => o.IsolatedDeclarations == Tristate.TSUnknown, static (a, b) => a.IsolatedDeclarations != b.IsolatedDeclarations, static o => o.IsolatedDeclarations),
        new("IgnoreConfig", "ignoreConfig", static o => o.IgnoreConfig, static o => o.IgnoreConfig == Tristate.TSUnknown, static (a, b) => a.IgnoreConfig != b.IgnoreConfig, static o => o.IgnoreConfig),
        new("IgnoreDeprecations", "ignoreDeprecations", static o => o.IgnoreDeprecations, static o => o.IgnoreDeprecations.Length == 0, static (a, b) => a.IgnoreDeprecations != b.IgnoreDeprecations, null),
        new("ImportHelpers", "importHelpers", static o => o.ImportHelpers, static o => o.ImportHelpers == Tristate.TSUnknown, static (a, b) => a.ImportHelpers != b.ImportHelpers, static o => o.ImportHelpers),
        new("InlineSourceMap", "inlineSourceMap", static o => o.InlineSourceMap, static o => o.InlineSourceMap == Tristate.TSUnknown, static (a, b) => a.InlineSourceMap != b.InlineSourceMap, static o => o.InlineSourceMap),
        new("InlineSources", "inlineSources", static o => o.InlineSources, static o => o.InlineSources == Tristate.TSUnknown, static (a, b) => a.InlineSources != b.InlineSources, static o => o.InlineSources),
        new("Init", "init", static o => o.Init, static o => o.Init == Tristate.TSUnknown, static (a, b) => a.Init != b.Init, static o => o.Init),
        new("Incremental", "incremental", static o => o.Incremental, static o => o.Incremental == Tristate.TSUnknown, static (a, b) => a.Incremental != b.Incremental, static o => o.Incremental),
        new("Jsx", "jsx", static o => o.Jsx, static o => o.Jsx == 0, static (a, b) => a.Jsx != b.Jsx, null),
        new("JsxFactory", "jsxFactory", static o => o.JsxFactory, static o => o.JsxFactory.Length == 0, static (a, b) => a.JsxFactory != b.JsxFactory, null),
        new("JsxFragmentFactory", "jsxFragmentFactory", static o => o.JsxFragmentFactory, static o => o.JsxFragmentFactory.Length == 0, static (a, b) => a.JsxFragmentFactory != b.JsxFragmentFactory, null),
        new("JsxImportSource", "jsxImportSource", static o => o.JsxImportSource, static o => o.JsxImportSource.Length == 0, static (a, b) => a.JsxImportSource != b.JsxImportSource, null),
        new("Lib", "lib", static o => o.Lib, static o => o.Lib is null, static (a, b) => !SameStringArray(a.Lib, b.Lib), null),
        new("LibReplacement", "libReplacement", static o => o.LibReplacement, static o => o.LibReplacement == Tristate.TSUnknown, static (a, b) => a.LibReplacement != b.LibReplacement, static o => o.LibReplacement),
        new("Locale", "locale", static o => o.Locale, static o => o.Locale.Length == 0, static (a, b) => a.Locale != b.Locale, null),
        new("MapRoot", "mapRoot", static o => o.MapRoot, static o => o.MapRoot.Length == 0, static (a, b) => a.MapRoot != b.MapRoot, null),
        new("Module", "module", static o => o.Module, static o => o.Module == 0, static (a, b) => a.Module != b.Module, null),
        new("ModuleResolution", "moduleResolution", static o => o.ModuleResolution, static o => o.ModuleResolution == 0, static (a, b) => a.ModuleResolution != b.ModuleResolution, null),
        new("ModuleSuffixes", "moduleSuffixes", static o => o.ModuleSuffixes, static o => o.ModuleSuffixes is null, static (a, b) => !SameStringArray(a.ModuleSuffixes, b.ModuleSuffixes), null),
        new("ModuleDetection", "moduleDetection", static o => o.ModuleDetection, static o => o.ModuleDetection == 0, static (a, b) => a.ModuleDetection != b.ModuleDetection, null),
        new("NewLine", "newLine", static o => o.NewLine, static o => o.NewLine == 0, static (a, b) => a.NewLine != b.NewLine, null),
        new("NoEmit", "noEmit", static o => o.NoEmit, static o => o.NoEmit == Tristate.TSUnknown, static (a, b) => a.NoEmit != b.NoEmit, static o => o.NoEmit),
        new("NoCheck", "noCheck", static o => o.NoCheck, static o => o.NoCheck == Tristate.TSUnknown, static (a, b) => a.NoCheck != b.NoCheck, static o => o.NoCheck),
        new("NoErrorTruncation", "noErrorTruncation", static o => o.NoErrorTruncation, static o => o.NoErrorTruncation == Tristate.TSUnknown, static (a, b) => a.NoErrorTruncation != b.NoErrorTruncation, static o => o.NoErrorTruncation),
        new("NoFallthroughCasesInSwitch", "noFallthroughCasesInSwitch", static o => o.NoFallthroughCasesInSwitch, static o => o.NoFallthroughCasesInSwitch == Tristate.TSUnknown, static (a, b) => a.NoFallthroughCasesInSwitch != b.NoFallthroughCasesInSwitch, static o => o.NoFallthroughCasesInSwitch),
        new("NoImplicitAny", "noImplicitAny", static o => o.NoImplicitAny, static o => o.NoImplicitAny == Tristate.TSUnknown, static (a, b) => a.NoImplicitAny != b.NoImplicitAny, static o => o.NoImplicitAny),
        new("NoImplicitThis", "noImplicitThis", static o => o.NoImplicitThis, static o => o.NoImplicitThis == Tristate.TSUnknown, static (a, b) => a.NoImplicitThis != b.NoImplicitThis, static o => o.NoImplicitThis),
        new("NoImplicitReturns", "noImplicitReturns", static o => o.NoImplicitReturns, static o => o.NoImplicitReturns == Tristate.TSUnknown, static (a, b) => a.NoImplicitReturns != b.NoImplicitReturns, static o => o.NoImplicitReturns),
        new("NoEmitHelpers", "noEmitHelpers", static o => o.NoEmitHelpers, static o => o.NoEmitHelpers == Tristate.TSUnknown, static (a, b) => a.NoEmitHelpers != b.NoEmitHelpers, static o => o.NoEmitHelpers),
        new("NoLib", "noLib", static o => o.NoLib, static o => o.NoLib == Tristate.TSUnknown, static (a, b) => a.NoLib != b.NoLib, static o => o.NoLib),
        new("NoPropertyAccessFromIndexSignature", "noPropertyAccessFromIndexSignature", static o => o.NoPropertyAccessFromIndexSignature, static o => o.NoPropertyAccessFromIndexSignature == Tristate.TSUnknown, static (a, b) => a.NoPropertyAccessFromIndexSignature != b.NoPropertyAccessFromIndexSignature, static o => o.NoPropertyAccessFromIndexSignature),
        new("NoUncheckedIndexedAccess", "noUncheckedIndexedAccess", static o => o.NoUncheckedIndexedAccess, static o => o.NoUncheckedIndexedAccess == Tristate.TSUnknown, static (a, b) => a.NoUncheckedIndexedAccess != b.NoUncheckedIndexedAccess, static o => o.NoUncheckedIndexedAccess),
        new("NoEmitOnError", "noEmitOnError", static o => o.NoEmitOnError, static o => o.NoEmitOnError == Tristate.TSUnknown, static (a, b) => a.NoEmitOnError != b.NoEmitOnError, static o => o.NoEmitOnError),
        new("NoUnusedLocals", "noUnusedLocals", static o => o.NoUnusedLocals, static o => o.NoUnusedLocals == Tristate.TSUnknown, static (a, b) => a.NoUnusedLocals != b.NoUnusedLocals, static o => o.NoUnusedLocals),
        new("NoUnusedParameters", "noUnusedParameters", static o => o.NoUnusedParameters, static o => o.NoUnusedParameters == Tristate.TSUnknown, static (a, b) => a.NoUnusedParameters != b.NoUnusedParameters, static o => o.NoUnusedParameters),
        new("NoResolve", "noResolve", static o => o.NoResolve, static o => o.NoResolve == Tristate.TSUnknown, static (a, b) => a.NoResolve != b.NoResolve, static o => o.NoResolve),
        new("NoImplicitOverride", "noImplicitOverride", static o => o.NoImplicitOverride, static o => o.NoImplicitOverride == Tristate.TSUnknown, static (a, b) => a.NoImplicitOverride != b.NoImplicitOverride, static o => o.NoImplicitOverride),
        new("NoUncheckedSideEffectImports", "noUncheckedSideEffectImports", static o => o.NoUncheckedSideEffectImports, static o => o.NoUncheckedSideEffectImports == Tristate.TSUnknown, static (a, b) => a.NoUncheckedSideEffectImports != b.NoUncheckedSideEffectImports, static o => o.NoUncheckedSideEffectImports),
        new("OutDir", "outDir", static o => o.OutDir, static o => o.OutDir.Length == 0, static (a, b) => a.OutDir != b.OutDir, null),
        new("Paths", "paths", static o => o.Paths, static o => o.Paths is null, static (a, b) => !SameStringArrayMap(a.Paths, b.Paths), null),
        new("Plugins", "plugins", static o => o.Plugins, static o => o.Plugins is null, static (a, b) => !SameObjectArray(a.Plugins, b.Plugins), null),
        new("PreserveConstEnums", "preserveConstEnums", static o => o.PreserveConstEnums, static o => o.PreserveConstEnums == Tristate.TSUnknown, static (a, b) => a.PreserveConstEnums != b.PreserveConstEnums, static o => o.PreserveConstEnums),
        new("PreserveSymlinks", "preserveSymlinks", static o => o.PreserveSymlinks, static o => o.PreserveSymlinks == Tristate.TSUnknown, static (a, b) => a.PreserveSymlinks != b.PreserveSymlinks, static o => o.PreserveSymlinks),
        new("Project", "project", static o => o.Project, static o => o.Project.Length == 0, static (a, b) => a.Project != b.Project, null),
        new("ResolveJsonModule", "resolveJsonModule", static o => o.ResolveJsonModule, static o => o.ResolveJsonModule == Tristate.TSUnknown, static (a, b) => a.ResolveJsonModule != b.ResolveJsonModule, static o => o.ResolveJsonModule),
        new("ResolvePackageJsonExports", "resolvePackageJsonExports", static o => o.ResolvePackageJsonExports, static o => o.ResolvePackageJsonExports == Tristate.TSUnknown, static (a, b) => a.ResolvePackageJsonExports != b.ResolvePackageJsonExports, static o => o.ResolvePackageJsonExports),
        new("ResolvePackageJsonImports", "resolvePackageJsonImports", static o => o.ResolvePackageJsonImports, static o => o.ResolvePackageJsonImports == Tristate.TSUnknown, static (a, b) => a.ResolvePackageJsonImports != b.ResolvePackageJsonImports, static o => o.ResolvePackageJsonImports),
        new("RemoveComments", "removeComments", static o => o.RemoveComments, static o => o.RemoveComments == Tristate.TSUnknown, static (a, b) => a.RemoveComments != b.RemoveComments, static o => o.RemoveComments),
        new("RewriteRelativeImportExtensions", "rewriteRelativeImportExtensions", static o => o.RewriteRelativeImportExtensions, static o => o.RewriteRelativeImportExtensions == Tristate.TSUnknown, static (a, b) => a.RewriteRelativeImportExtensions != b.RewriteRelativeImportExtensions, static o => o.RewriteRelativeImportExtensions),
        new("ReactNamespace", "reactNamespace", static o => o.ReactNamespace, static o => o.ReactNamespace.Length == 0, static (a, b) => a.ReactNamespace != b.ReactNamespace, null),
        new("RootDir", "rootDir", static o => o.RootDir, static o => o.RootDir.Length == 0, static (a, b) => a.RootDir != b.RootDir, null),
        new("RootDirs", "rootDirs", static o => o.RootDirs, static o => o.RootDirs is null, static (a, b) => !SameStringArray(a.RootDirs, b.RootDirs), null),
        new("SkipLibCheck", "skipLibCheck", static o => o.SkipLibCheck, static o => o.SkipLibCheck == Tristate.TSUnknown, static (a, b) => a.SkipLibCheck != b.SkipLibCheck, static o => o.SkipLibCheck),
        new("StableTypeOrdering", "stableTypeOrdering", static o => o.StableTypeOrdering, static o => o.StableTypeOrdering == Tristate.TSUnknown, static (a, b) => a.StableTypeOrdering != b.StableTypeOrdering, static o => o.StableTypeOrdering),
        new("Strict", "strict", static o => o.Strict, static o => o.Strict == Tristate.TSUnknown, static (a, b) => a.Strict != b.Strict, static o => o.Strict),
        new("StrictBindCallApply", "strictBindCallApply", static o => o.StrictBindCallApply, static o => o.StrictBindCallApply == Tristate.TSUnknown, static (a, b) => a.StrictBindCallApply != b.StrictBindCallApply, static o => o.StrictBindCallApply),
        new("StrictBuiltinIteratorReturn", "strictBuiltinIteratorReturn", static o => o.StrictBuiltinIteratorReturn, static o => o.StrictBuiltinIteratorReturn == Tristate.TSUnknown, static (a, b) => a.StrictBuiltinIteratorReturn != b.StrictBuiltinIteratorReturn, static o => o.StrictBuiltinIteratorReturn),
        new("StrictFunctionTypes", "strictFunctionTypes", static o => o.StrictFunctionTypes, static o => o.StrictFunctionTypes == Tristate.TSUnknown, static (a, b) => a.StrictFunctionTypes != b.StrictFunctionTypes, static o => o.StrictFunctionTypes),
        new("StrictNullChecks", "strictNullChecks", static o => o.StrictNullChecks, static o => o.StrictNullChecks == Tristate.TSUnknown, static (a, b) => a.StrictNullChecks != b.StrictNullChecks, static o => o.StrictNullChecks),
        new("StrictPropertyInitialization", "strictPropertyInitialization", static o => o.StrictPropertyInitialization, static o => o.StrictPropertyInitialization == Tristate.TSUnknown, static (a, b) => a.StrictPropertyInitialization != b.StrictPropertyInitialization, static o => o.StrictPropertyInitialization),
        new("StripInternal", "stripInternal", static o => o.StripInternal, static o => o.StripInternal == Tristate.TSUnknown, static (a, b) => a.StripInternal != b.StripInternal, static o => o.StripInternal),
        new("SkipDefaultLibCheck", "skipDefaultLibCheck", static o => o.SkipDefaultLibCheck, static o => o.SkipDefaultLibCheck == Tristate.TSUnknown, static (a, b) => a.SkipDefaultLibCheck != b.SkipDefaultLibCheck, static o => o.SkipDefaultLibCheck),
        new("SourceMap", "sourceMap", static o => o.SourceMap, static o => o.SourceMap == Tristate.TSUnknown, static (a, b) => a.SourceMap != b.SourceMap, static o => o.SourceMap),
        new("SourceRoot", "sourceRoot", static o => o.SourceRoot, static o => o.SourceRoot.Length == 0, static (a, b) => a.SourceRoot != b.SourceRoot, null),
        new("SuppressOutputPathCheck", "suppressOutputPathCheck", static o => o.SuppressOutputPathCheck, static o => o.SuppressOutputPathCheck == Tristate.TSUnknown, static (a, b) => a.SuppressOutputPathCheck != b.SuppressOutputPathCheck, static o => o.SuppressOutputPathCheck),
        new("Target", "target", static o => o.Target, static o => o.Target == 0, static (a, b) => a.Target != b.Target, null),
        new("TraceResolution", "traceResolution", static o => o.TraceResolution, static o => o.TraceResolution == Tristate.TSUnknown, static (a, b) => a.TraceResolution != b.TraceResolution, static o => o.TraceResolution),
        new("TsBuildInfoFile", "tsBuildInfoFile", static o => o.TsBuildInfoFile, static o => o.TsBuildInfoFile.Length == 0, static (a, b) => a.TsBuildInfoFile != b.TsBuildInfoFile, null),
        new("TypeRoots", "typeRoots", static o => o.TypeRoots, static o => o.TypeRoots is null, static (a, b) => !SameStringArray(a.TypeRoots, b.TypeRoots), null),
        new("Types", "types", static o => o.Types, static o => o.Types is null, static (a, b) => !SameStringArray(a.Types, b.Types), null),
        new("UseDefineForClassFields", "useDefineForClassFields", static o => o.UseDefineForClassFields, static o => o.UseDefineForClassFields == Tristate.TSUnknown, static (a, b) => a.UseDefineForClassFields != b.UseDefineForClassFields, static o => o.UseDefineForClassFields),
        new("UseUnknownInCatchVariables", "useUnknownInCatchVariables", static o => o.UseUnknownInCatchVariables, static o => o.UseUnknownInCatchVariables == Tristate.TSUnknown, static (a, b) => a.UseUnknownInCatchVariables != b.UseUnknownInCatchVariables, static o => o.UseUnknownInCatchVariables),
        new("VerbatimModuleSyntax", "verbatimModuleSyntax", static o => o.VerbatimModuleSyntax, static o => o.VerbatimModuleSyntax == Tristate.TSUnknown, static (a, b) => a.VerbatimModuleSyntax != b.VerbatimModuleSyntax, static o => o.VerbatimModuleSyntax),
        new("MaxNodeModuleJsDepth", "maxNodeModuleJsDepth", static o => o.MaxNodeModuleJsDepth, static o => o.MaxNodeModuleJsDepth is null, static (a, b) => a.MaxNodeModuleJsDepth != b.MaxNodeModuleJsDepth, null),
        new("AllowSyntheticDefaultImports", "allowSyntheticDefaultImports", static o => o.AllowSyntheticDefaultImports, static o => o.AllowSyntheticDefaultImports == Tristate.TSUnknown, static (a, b) => a.AllowSyntheticDefaultImports != b.AllowSyntheticDefaultImports, static o => o.AllowSyntheticDefaultImports),
        new("AlwaysStrict", "alwaysStrict", static o => o.AlwaysStrict, static o => o.AlwaysStrict == Tristate.TSUnknown, static (a, b) => a.AlwaysStrict != b.AlwaysStrict, static o => o.AlwaysStrict),
        new("BaseUrl", "baseUrl", static o => o.BaseUrl, static o => o.BaseUrl.Length == 0, static (a, b) => a.BaseUrl != b.BaseUrl, null),
        new("DownlevelIteration", "downlevelIteration", static o => o.DownlevelIteration, static o => o.DownlevelIteration == Tristate.TSUnknown, static (a, b) => a.DownlevelIteration != b.DownlevelIteration, static o => o.DownlevelIteration),
        new("ESModuleInterop", "esModuleInterop", static o => o.ESModuleInterop, static o => o.ESModuleInterop == Tristate.TSUnknown, static (a, b) => a.ESModuleInterop != b.ESModuleInterop, static o => o.ESModuleInterop),
        new("OutFile", "outFile", static o => o.OutFile, static o => o.OutFile.Length == 0, static (a, b) => a.OutFile != b.OutFile, null),
        new("ConfigFilePath", "configFilePath", static o => o.ConfigFilePath, static o => o.ConfigFilePath.Length == 0, static (a, b) => a.ConfigFilePath != b.ConfigFilePath, null),
        new("NoDtsResolution", "noDtsResolution", static o => o.NoDtsResolution, static o => o.NoDtsResolution == Tristate.TSUnknown, static (a, b) => a.NoDtsResolution != b.NoDtsResolution, static o => o.NoDtsResolution),
        new("PathsBasePath", "pathsBasePath", static o => o.PathsBasePath, static o => o.PathsBasePath.Length == 0, static (a, b) => a.PathsBasePath != b.PathsBasePath, null),
        new("Diagnostics", "diagnostics", static o => o.Diagnostics, static o => o.Diagnostics == Tristate.TSUnknown, static (a, b) => a.Diagnostics != b.Diagnostics, static o => o.Diagnostics),
        new("ExtendedDiagnostics", "extendedDiagnostics", static o => o.ExtendedDiagnostics, static o => o.ExtendedDiagnostics == Tristate.TSUnknown, static (a, b) => a.ExtendedDiagnostics != b.ExtendedDiagnostics, static o => o.ExtendedDiagnostics),
        new("GenerateCpuProfile", "generateCpuProfile", static o => o.GenerateCpuProfile, static o => o.GenerateCpuProfile.Length == 0, static (a, b) => a.GenerateCpuProfile != b.GenerateCpuProfile, null),
        new("GenerateTrace", "generateTrace", static o => o.GenerateTrace, static o => o.GenerateTrace.Length == 0, static (a, b) => a.GenerateTrace != b.GenerateTrace, null),
        new("ListEmittedFiles", "listEmittedFiles", static o => o.ListEmittedFiles, static o => o.ListEmittedFiles == Tristate.TSUnknown, static (a, b) => a.ListEmittedFiles != b.ListEmittedFiles, static o => o.ListEmittedFiles),
        new("ListFiles", "listFiles", static o => o.ListFiles, static o => o.ListFiles == Tristate.TSUnknown, static (a, b) => a.ListFiles != b.ListFiles, static o => o.ListFiles),
        new("ExplainFiles", "explainFiles", static o => o.ExplainFiles, static o => o.ExplainFiles == Tristate.TSUnknown, static (a, b) => a.ExplainFiles != b.ExplainFiles, static o => o.ExplainFiles),
        new("ListFilesOnly", "listFilesOnly", static o => o.ListFilesOnly, static o => o.ListFilesOnly == Tristate.TSUnknown, static (a, b) => a.ListFilesOnly != b.ListFilesOnly, static o => o.ListFilesOnly),
        new("NoEmitForJsFiles", "noEmitForJsFiles", static o => o.NoEmitForJsFiles, static o => o.NoEmitForJsFiles == Tristate.TSUnknown, static (a, b) => a.NoEmitForJsFiles != b.NoEmitForJsFiles, static o => o.NoEmitForJsFiles),
        new("PreserveWatchOutput", "preserveWatchOutput", static o => o.PreserveWatchOutput, static o => o.PreserveWatchOutput == Tristate.TSUnknown, static (a, b) => a.PreserveWatchOutput != b.PreserveWatchOutput, static o => o.PreserveWatchOutput),
        new("Pretty", "pretty", static o => o.Pretty, static o => o.Pretty == Tristate.TSUnknown, static (a, b) => a.Pretty != b.Pretty, static o => o.Pretty),
        new("Version", "version", static o => o.Version, static o => o.Version == Tristate.TSUnknown, static (a, b) => a.Version != b.Version, static o => o.Version),
        new("Watch", "watch", static o => o.Watch, static o => o.Watch == Tristate.TSUnknown, static (a, b) => a.Watch != b.Watch, static o => o.Watch),
        new("ShowConfig", "showConfig", static o => o.ShowConfig, static o => o.ShowConfig == Tristate.TSUnknown, static (a, b) => a.ShowConfig != b.ShowConfig, static o => o.ShowConfig),
        new("Build", "build", static o => o.Build, static o => o.Build == Tristate.TSUnknown, static (a, b) => a.Build != b.Build, static o => o.Build),
        new("Help", "help", static o => o.Help, static o => o.Help == Tristate.TSUnknown, static (a, b) => a.Help != b.Help, static o => o.Help),
        new("All", "all", static o => o.All, static o => o.All == Tristate.TSUnknown, static (a, b) => a.All != b.All, static o => o.All),
        new("RunExternalCode", "runExternalCode", static o => o.RunExternalCode, static o => o.RunExternalCode == Tristate.TSUnknown, static (a, b) => a.RunExternalCode != b.RunExternalCode, static o => o.RunExternalCode),
        new("PprofDir", "pprofDir", static o => o.PprofDir, static o => o.PprofDir.Length == 0, static (a, b) => a.PprofDir != b.PprofDir, null),
        new("SingleThreaded", "singleThreaded", static o => o.SingleThreaded, static o => o.SingleThreaded == Tristate.TSUnknown, static (a, b) => a.SingleThreaded != b.SingleThreaded, static o => o.SingleThreaded),
        new("Quiet", "quiet", static o => o.Quiet, static o => o.Quiet == Tristate.TSUnknown, static (a, b) => a.Quiet != b.Quiet, static o => o.Quiet),
        new("Checkers", "checkers", static o => o.Checkers, static o => o.Checkers is null, static (a, b) => a.Checkers != b.Checkers, null),
    ];

    // Go 的 reflect.DeepEqual 对 slice 逐元素比较（nil 与非 nil 空 slice 不相等）。
    private static bool SameStringArray(string[]? a, string[]? b)
    {
        if (a is null || b is null)
        {
            return a is null && b is null;
        }
        if (a.Length != b.Length)
        {
            return false;
        }
        for (var i = 0; i < a.Length; i++)
        {
            if (a[i] != b[i])
            {
                return false;
            }
        }
        return true;
    }

    // 同上；元素是引用类型，按引用比较（Go 的 PluginImport 是结构体，DeepEqual 会逐字段比较）。
    private static bool SameObjectArray<T>(T[]? a, T[]? b)
        where T : class
    {
        if (a is null || b is null)
        {
            return a is null && b is null;
        }
        if (a.Length != b.Length)
        {
            return false;
        }
        for (var i = 0; i < a.Length; i++)
        {
            if (!ReferenceEquals(a[i], b[i]))
            {
                return false;
            }
        }
        return true;
    }

    private static bool SameStringArrayMap(OrderedMap<string, string[]>? a, OrderedMap<string, string[]>? b)
    {
        if (a is null || b is null)
        {
            return a is null && b is null;
        }
        if (a.Size() != b.Size())
        {
            return false;
        }
        foreach (var (key, value) in a.Entries())
        {
            var (otherValue, ok) = b.Get(key);
            if (!ok || !SameStringArray(value, otherValue))
            {
                return false;
            }
        }
        return true;
    }
}
