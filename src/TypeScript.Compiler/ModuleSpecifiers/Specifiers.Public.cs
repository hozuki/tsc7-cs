// ported from: internal/modulespecifiers/specifiers.go:1333-1402（GetModuleSpecifier / UpdateModuleSpecifier / getModuleSpecifierWithPreferences）
//
// 与 Go 的偏离：只有 Go 的 `UserPreferences{}`（零值）→ C# 的 `new UserPreferences()`（Types.cs 文件头第 3 条）。
using TypeScript.Ast;
using TypeScript.Core;
using static TypeScript.Core.Core;
using static TypeScript.TSPath.TSPath;

using TypeScript.TSPath;

namespace TypeScript.ModuleSpecifiers;

public static partial class ModuleSpecifiers
{
    /// <summary>Go: <c>func GetModuleSpecifier(compilerOptions *core.CompilerOptions, host ModuleSpecifierGenerationHost, importingSourceFile *ast.SourceFile, importingSourceFileName string, oldImportSpecifier string, toFileName string, options ModuleSpecifierOptions) string</c>。</summary>
    public static string GetModuleSpecifier(
        CompilerOptions compilerOptions,
        ModuleSpecifierGenerationHost host,
        SourceFile importingSourceFile, // !!! | FutureSourceFile
        string importingSourceFileName,
        string oldImportSpecifier, // used only in updatingModuleSpecifier
        string toFileName,
        ModuleSpecifierOptions options)
    {
        return GetModuleSpecifierWithPreferences(
            compilerOptions,
            host,
            importingSourceFile,
            importingSourceFileName,
            oldImportSpecifier,
            toFileName,
            new UserPreferences(),
            options);
    }

    /// <summary>Go: <c>func UpdateModuleSpecifier(compilerOptions *core.CompilerOptions, host ModuleSpecifierGenerationHost, importingSourceFile *ast.SourceFile, importingSourceFileName string, oldImportSpecifier string, toFileName string, userPreferences UserPreferences, options ModuleSpecifierOptions) string</c>。</summary>
    public static string UpdateModuleSpecifier(
        CompilerOptions compilerOptions,
        ModuleSpecifierGenerationHost host,
        SourceFile importingSourceFile,
        string importingSourceFileName,
        string oldImportSpecifier,
        string toFileName,
        UserPreferences userPreferences,
        ModuleSpecifierOptions options)
    {
        return GetModuleSpecifierWithPreferences(
            compilerOptions,
            host,
            importingSourceFile,
            importingSourceFileName,
            oldImportSpecifier,
            toFileName,
            userPreferences,
            options);
    }

    // `importingSourceFile` and `importingSourceFileName`? Why not just use `importingSourceFile.path`?
    // Because when this is called by the declaration emitter, `importingSourceFile` is the implementation
    // file, but `importingSourceFileName` and `toFileName` refer to declaration files (the former to the
    // one currently being produced; the latter to the one being imported). We need an implementation file
    // just to get its `impliedNodeFormat` and to detect certain preferences from existing import module
    // specifiers.
    /// <summary>Go: <c>func getModuleSpecifierWithPreferences(...) string</c>。</summary>
    private static string GetModuleSpecifierWithPreferences(
        CompilerOptions compilerOptions,
        ModuleSpecifierGenerationHost host,
        SourceFile importingSourceFile, // !!! | FutureSourceFile
        string importingSourceFileName,
        string oldImportSpecifier, // used only in updatingModuleSpecifier
        string toFileName,
        UserPreferences userPreferences,
        ModuleSpecifierOptions options)
    {
        var info = GetInfo(importingSourceFileName, host);
        var modulePaths = GetAllModulePaths(info, toFileName, host, compilerOptions, userPreferences, options);
        var preferences = GetModuleSpecifierPreferences(userPreferences, host, compilerOptions, importingSourceFile, oldImportSpecifier);

        var resolutionMode = options.OverrideImportMode;
        if (resolutionMode == ResolutionModeNone)
        {
            resolutionMode = host.GetDefaultResolutionModeForFile(importingSourceFile);
        }

        foreach (var modulePath in modulePaths)
        {
            // Go: `if firstDefined := tryGetModuleNameAsNodeModule(...); len(firstDefined) > 0 { return firstDefined }`
            var firstDefined = TryGetModuleNameAsNodeModule(
                modulePath,
                info,
                importingSourceFile,
                host,
                compilerOptions,
                userPreferences,
                packageNameOnly: false,
                options.OverrideImportMode);
            if (firstDefined.Length > 0)
            {
                return firstDefined;
            }
        }

        return GetLocalModuleSpecifier(toFileName, info, compilerOptions, host, resolutionMode, preferences, pathsOnly: false);
    }
}
