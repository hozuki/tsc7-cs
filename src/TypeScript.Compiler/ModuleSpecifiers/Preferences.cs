// ported from: internal/modulespecifiers/preferences.go（256 行，整文件）
//
// 与 Go 的偏离：
// 1) Go 的 `ModuleSpecifierPreferences.getAllowedEndingsInPreferredOrder` 是**函数字段**；C# 里字段按
//    .editorconfig 必须是 `_camelCase`，读起来就不再与 Go 的调用点对应，所以落成 PascalCase 的**属性**
//    （属性持委托是 C# 的惯用法），调用点 `preferences.GetAllowedEndingsInPreferredOrder(importMode)`
//    与 Go 逐字相同。`relativePreference`/`excludeRegexes` 仍是字段（`RelativePreference`/`ExcludeRegexes`
//    —— 这两个类是 private 的，命名只对本文件可见；按 §2 的"C# 习惯"用 PascalCase 属性/字段更可读，
//    这里选字段以保留 Go 的可变语义）。
// 2) `ImportModuleSpecifierEndingPreference` 是 string 的包装结构体（见 Types.cs 文件头第 1 条），
//    与常量比较写成 `pref == ImportModuleSpecifierEndingPreferenceJs`（结构体提供了与 string 的 ==），
//    switch 则写成 `switch (pref.Value)`（C# 的 case 标签必须是编译期常量）。
// 3) `debug.AssertNever` 是 void（Go 的它也是 void，之后 Go 直接 fallthrough 到函数末尾的 return）
//    → C# 的 default 分支用 `break;`，函数末尾保留 Go 那条 `return []ModuleSpecifierEnding{Minimal}`。
// 4) `strings.HasSuffix(s, "/index")` → `s.EndsWith("/index", StringComparison.Ordinal)`（Go 是纯字节比较）。
// 5) `sourceFile` 在 Go 里可能为 nil（接口的零值）→ C# 的接口参数写 `SourceFileForSpecifierGeneration?`。
using TypeScript.Ast;
using TypeScript.Core;
using static TypeScript.Core.Core;
using static TypeScript.TSPath.TSPath;

using TypeScript.TSPath;

namespace TypeScript.ModuleSpecifiers;

public static partial class ModuleSpecifiers
{
    // Program errors validate that `noEmit` or `emitDeclarationOnly` is also set,
    // so this function doesn't check them to avoid propagating errors.
    /// <summary>Go: <c>func shouldAllowImportingTsExtension(compilerOptions *core.CompilerOptions, fromFileName string) bool</c>。</summary>
    private static bool ShouldAllowImportingTsExtension(CompilerOptions compilerOptions, string fromFileName)
    {
        return compilerOptions.GetAllowImportingTsExtensions()
            || (fromFileName.Length > 0 && IsDeclarationFileName(fromFileName));
    }

    /// <summary>Go: <c>func usesExtensionsOnImports(file SourceFileForSpecifierGeneration) bool</c>。</summary>
    private static bool UsesExtensionsOnImports(SourceFileForSpecifierGeneration file)
    {
        foreach (var reference in file.Imports())
        {
            var text = reference.Text();
            if (PathIsRelative(text) && !FileExtensionIsOneOf(text, ExtensionsNotSupportingExtensionlessResolution))
            {
                return HasTSFileExtension(text) || HasJSFileExtension(text);
            }
        }

        return false;
    }

    /// <summary>Go: <c>func inferPreference(resolutionMode core.ResolutionMode, sourceFile SourceFileForSpecifierGeneration, moduleResolutionIsNodeNext bool) ModuleSpecifierEnding</c>。</summary>
    private static ModuleSpecifierEnding InferPreference(
        ModuleKind resolutionMode,
        SourceFileForSpecifierGeneration? sourceFile,
        bool moduleResolutionIsNodeNext)
    {
        var usesJsExtensions = false;
        Node[] specifiers = [];
        if (sourceFile is not null && sourceFile.Imports().Length > 0)
        {
            specifiers = sourceFile.Imports();
        }
        else if (sourceFile is not null && sourceFile.IsJS())
        {
            // Go 这里是 `// !!! TODO: JS support` + 一行被注释掉的 getRequiresAtTopOfFile（preferences.go:38-40）；
            // 不移植，见交付报告的"跳过的 Go 符号"清单。
        }

        foreach (var specifier in specifiers)
        {
            var path = specifier.Text();
            if (PathIsRelative(path))
            {
                // Go: `// !!! TODO: proper resolutionMode support`（preferences.go:48）原样对应；
                // 条件里被注释掉的 `getModeForUsageLocation(...)` 不移植。
                if (moduleResolutionIsNodeNext && resolutionMode == ResolutionModeCommonJS)
                {
                    // We're trying to decide a preference for a CommonJS module specifier, but looking at an ESM import.
                    continue;
                }

                if (FileExtensionIsOneOf(path, ExtensionsNotSupportingExtensionlessResolution))
                {
                    // These extensions are not optional, so do not indicate a preference.
                    continue;
                }

                if (HasTSFileExtension(path))
                {
                    return ModuleSpecifierEnding.TsExtension;
                }

                if (HasJSFileExtension(path))
                {
                    usesJsExtensions = true;
                }
            }
        }

        if (usesJsExtensions)
        {
            return ModuleSpecifierEnding.JsExtension;
        }

        return ModuleSpecifierEnding.Minimal;
    }

    /// <summary>Go: <c>func getModuleSpecifierEndingPreference(pref ImportModuleSpecifierEndingPreference, resolutionMode core.ResolutionMode, compilerOptions *core.CompilerOptions, sourceFile SourceFileForSpecifierGeneration) ModuleSpecifierEnding</c>。</summary>
    private static ModuleSpecifierEnding GetModuleSpecifierEndingPreference(
        ImportModuleSpecifierEndingPreference pref,
        ModuleKind resolutionMode,
        CompilerOptions compilerOptions,
        SourceFileForSpecifierGeneration? sourceFile)
    {
        var moduleResolution = compilerOptions.GetModuleResolutionKind();
        var moduleResolutionIsNodeNext = ModuleResolutionKind.Node16 <= moduleResolution && moduleResolution <= ModuleResolutionKind.NodeNext;

        if (pref == ImportModuleSpecifierEndingPreferenceJs || (resolutionMode == ResolutionModeESM && moduleResolutionIsNodeNext))
        {
            // Extensions are explicitly requested or required. Now choose between .js and .ts.
            if (!ShouldAllowImportingTsExtension(compilerOptions, ""))
            {
                return ModuleSpecifierEnding.JsExtension;
            }

            // `allowImportingTsExtensions` is a strong signal, so use .ts unless the file
            // already uses .js extensions and no .ts extensions.
            if (InferPreference(resolutionMode, sourceFile, moduleResolutionIsNodeNext) != ModuleSpecifierEnding.JsExtension)
            {
                return ModuleSpecifierEnding.TsExtension;
            }

            return ModuleSpecifierEnding.JsExtension;
        }

        if (pref == ImportModuleSpecifierEndingPreferenceMinimal)
        {
            return ModuleSpecifierEnding.Minimal;
        }

        if (pref == ImportModuleSpecifierEndingPreferenceIndex)
        {
            return ModuleSpecifierEnding.Index;
        }

        // No preference was specified.
        // Look at imports and/or requires to guess whether .js, .ts, or extensionless imports are preferred.
        // N.B. that `Index` detection is not supported since it would require file system probing to do
        // accurately, and more importantly, literally nobody wants `Index` and its existence is a mystery.
        if (!ShouldAllowImportingTsExtension(compilerOptions, ""))
        {
            // If .ts imports are not valid, we only need to see one .js import to go with that.
            if (sourceFile is not null && UsesExtensionsOnImports(sourceFile))
            {
                return ModuleSpecifierEnding.JsExtension;
            }

            return ModuleSpecifierEnding.Minimal;
        }

        return InferPreference(resolutionMode, sourceFile, moduleResolutionIsNodeNext);
    }

    /// <summary>Go: <c>func getPreferredEnding(prefs UserPreferences, host ModuleSpecifierGenerationHost, compilerOptions *core.CompilerOptions, importingSourceFile SourceFileForSpecifierGeneration, oldImportSpecifier string, resolutionMode core.ResolutionMode) ModuleSpecifierEnding</c>。</summary>
    private static ModuleSpecifierEnding GetPreferredEnding(
        UserPreferences prefs,
        ModuleSpecifierGenerationHost host,
        CompilerOptions compilerOptions,
        SourceFileForSpecifierGeneration importingSourceFile,
        string oldImportSpecifier,
        ModuleKind resolutionMode)
    {
        if (oldImportSpecifier.Length > 0)
        {
            if (HasJSFileExtension(oldImportSpecifier))
            {
                return ModuleSpecifierEnding.JsExtension;
            }

            if (oldImportSpecifier.EndsWith("/index", StringComparison.Ordinal))
            {
                return ModuleSpecifierEnding.Index;
            }
        }

        if (resolutionMode == ResolutionModeNone)
        {
            resolutionMode = host.GetDefaultResolutionModeForFile(importingSourceFile);
        }

        return GetModuleSpecifierEndingPreference(
            prefs.ImportModuleSpecifierEnding,
            resolutionMode,
            compilerOptions,
            importingSourceFile);
    }

    /// <summary>Go: <c>func GetAllowedEndingsInPreferredOrder(prefs UserPreferences, host ModuleSpecifierGenerationHost, compilerOptions *core.CompilerOptions, importingSourceFile SourceFileForSpecifierGeneration, oldImportSpecifier string, syntaxImpliedNodeFormat core.ResolutionMode) []ModuleSpecifierEnding</c>。</summary>
    public static ModuleSpecifierEnding[] GetAllowedEndingsInPreferredOrder(
        UserPreferences prefs,
        ModuleSpecifierGenerationHost host,
        CompilerOptions compilerOptions,
        SourceFileForSpecifierGeneration importingSourceFile,
        string oldImportSpecifier,
        ModuleKind syntaxImpliedNodeFormat)
    {
        var preferredEnding = GetPreferredEnding(
            prefs,
            host,
            compilerOptions,
            importingSourceFile,
            oldImportSpecifier,
            ResolutionModeNone);
        var resolutionMode = host.GetDefaultResolutionModeForFile(importingSourceFile);
        if (resolutionMode != syntaxImpliedNodeFormat)
        {
            preferredEnding = GetPreferredEnding(
                prefs,
                host,
                compilerOptions,
                importingSourceFile,
                oldImportSpecifier,
                syntaxImpliedNodeFormat);
        }

        var moduleResolution = compilerOptions.GetModuleResolutionKind();
        var moduleResolutionIsNodeNext = ModuleResolutionKind.Node16 <= moduleResolution && moduleResolution <= ModuleResolutionKind.NodeNext;
        var allowImportingTsExtension = ShouldAllowImportingTsExtension(compilerOptions, importingSourceFile.FileName());
        // TypeScript uses `(syntaxImpliedNodeFormat ?? impliedNodeFormat)` here - fall back to the
        // file's default resolution mode when no syntax-implied mode is given.
        var effectiveSyntaxMode = syntaxImpliedNodeFormat;
        if (effectiveSyntaxMode == ResolutionModeNone)
        {
            effectiveSyntaxMode = resolutionMode;
        }

        if (effectiveSyntaxMode == ResolutionModeESM && moduleResolutionIsNodeNext)
        {
            if (allowImportingTsExtension)
            {
                return [ModuleSpecifierEnding.TsExtension, ModuleSpecifierEnding.JsExtension];
            }

            return [ModuleSpecifierEnding.JsExtension];
        }

        switch (preferredEnding)
        {
            case ModuleSpecifierEnding.JsExtension:
                if (allowImportingTsExtension)
                {
                    return [ModuleSpecifierEnding.JsExtension, ModuleSpecifierEnding.TsExtension, ModuleSpecifierEnding.Minimal, ModuleSpecifierEnding.Index];
                }

                return [ModuleSpecifierEnding.JsExtension, ModuleSpecifierEnding.Minimal, ModuleSpecifierEnding.Index];
            case ModuleSpecifierEnding.TsExtension:
                return [ModuleSpecifierEnding.TsExtension, ModuleSpecifierEnding.Minimal, ModuleSpecifierEnding.JsExtension, ModuleSpecifierEnding.Index];
            case ModuleSpecifierEnding.Index:
                if (allowImportingTsExtension)
                {
                    return [ModuleSpecifierEnding.Index, ModuleSpecifierEnding.Minimal, ModuleSpecifierEnding.TsExtension, ModuleSpecifierEnding.JsExtension];
                }

                return [ModuleSpecifierEnding.Index, ModuleSpecifierEnding.Minimal, ModuleSpecifierEnding.JsExtension];
            case ModuleSpecifierEnding.Minimal:
                if (allowImportingTsExtension)
                {
                    return [ModuleSpecifierEnding.Minimal, ModuleSpecifierEnding.Index, ModuleSpecifierEnding.TsExtension, ModuleSpecifierEnding.JsExtension];
                }

                return [ModuleSpecifierEnding.Minimal, ModuleSpecifierEnding.Index, ModuleSpecifierEnding.JsExtension];
            default:
                Debug.AssertNever(preferredEnding);
                break;
        }

        return [ModuleSpecifierEnding.Minimal];
    }

    /// <summary>Go: <c>func getModuleSpecifierPreferences(prefs UserPreferences, host ModuleSpecifierGenerationHost, compilerOptions *core.CompilerOptions, importingSourceFile SourceFileForSpecifierGeneration, oldImportSpecifier string) ModuleSpecifierPreferences</c>。</summary>
    private static ModuleSpecifierPreferences GetModuleSpecifierPreferences(
        UserPreferences prefs,
        ModuleSpecifierGenerationHost host,
        CompilerOptions compilerOptions,
        SourceFileForSpecifierGeneration importingSourceFile,
        string oldImportSpecifier)
    {
        var excludes = prefs.AutoImportSpecifierExcludeRegexes;
        var relativePreference = RelativePreferenceKind.Shortest;
        if (oldImportSpecifier.Length > 0)
        {
            if (IsExternalModuleNameRelative(oldImportSpecifier))
            {
                relativePreference = RelativePreferenceKind.Relative;
            }
            else
            {
                relativePreference = RelativePreferenceKind.NonRelative;
            }
        }
        else
        {
            switch (prefs.ImportModuleSpecifierPreference.Value)
            {
                case ImportModuleSpecifierPreferenceRelative:
                    relativePreference = RelativePreferenceKind.Relative;
                    break;
                case ImportModuleSpecifierPreferenceNonRelative:
                    relativePreference = RelativePreferenceKind.NonRelative;
                    break;
                case ImportModuleSpecifierPreferenceProjectRelative:
                    relativePreference = RelativePreferenceKind.ExternalNonRelative;

                    // all others are shortest
                    break;
            }
        }

        // Go 的闭包：捕获上面几个参数，供每处调用点按 syntaxImpliedNodeFormat 现场求值。
        ModuleSpecifierEnding[] GetAllowedEndingsInPreferredOrder(ModuleKind syntaxImpliedNodeFormat) =>
            ModuleSpecifiers.GetAllowedEndingsInPreferredOrder(
                prefs,
                host,
                compilerOptions,
                importingSourceFile,
                oldImportSpecifier,
                syntaxImpliedNodeFormat);

        return new ModuleSpecifierPreferences(relativePreference, excludes, GetAllowedEndingsInPreferredOrder);
    }
}

/// <summary>Go: <c>type ModuleSpecifierPreferences struct</c>（Go 里是**包级导出**类型 → C# 命名空间级 public，docs/architecture.md §2）。</summary>
public sealed class ModuleSpecifierPreferences
{
    public RelativePreferenceKind RelativePreference;
    public Func<ModuleKind, ModuleSpecifierEnding[]> GetAllowedEndingsInPreferredOrder;
    public string[]? ExcludeRegexes;

    public ModuleSpecifierPreferences(
        RelativePreferenceKind relativePreference,
        string[]? excludeRegexes,
        Func<ModuleKind, ModuleSpecifierEnding[]> getAllowedEndingsInPreferredOrder)
    {
        RelativePreference = relativePreference;
        ExcludeRegexes = excludeRegexes;
        GetAllowedEndingsInPreferredOrder = getAllowedEndingsInPreferredOrder;
    }
}
