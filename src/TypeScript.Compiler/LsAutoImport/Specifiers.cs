// ported from: internal/ls/autoimport/specifiers.go
//
// 与 Go 的偏离（逐条）：
// 1) `View` 在 C# 里跨文件 → `partial class View`（C# 的类型不能像 Go 那样在多个文件里分别写方法）。
// 2) `modulespecifiers.*` 的类型/函数：类型全限定 `TypeScript.ModuleSpecifiers.X`（裸 `ModuleSpecifiers.X`
//    会命中命名空间），函数用 `using static` 的裸名（§2）。
// 3) `(string, ResultKind)` 多返回值 → C# 具名元组。
// 4) Go 的 `cache := v.registry.specifierCache[v.importingFilePath]`（缺键时零值 = nil map，随后 Load
//    会 panic）→ C# 的 `TryGetValue` + `Debug.Fail`（同样的"不应发生"语义）。
using TypeScript.Module;
using static TypeScript.ModuleSpecifiers.ModuleSpecifiers;

namespace TypeScript.LsAutoImport;

public sealed partial class View
{
    /// <summary>Go: <c>func (v *View) GetModuleSpecifier(export *Export, userPreferences modulespecifiers.UserPreferences) (string, modulespecifiers.ResultKind)</c>（specifiers.go:9-72）。</summary>
    internal (string Specifier, TypeScript.ModuleSpecifiers.ResultKind Kind) GetModuleSpecifier(
        Export export,
        TypeScript.ModuleSpecifiers.UserPreferences userPreferences)
    {
        // Ambient module
        if (PathIsBareSpecifier((string)export.ModuleID))
        {
            var specifier = (string)export.ModuleID;
            if (IsExcludedByRegex(specifier, userPreferences.AutoImportSpecifierExcludeRegexes))
            {
                return ("", TypeScript.ModuleSpecifiers.ResultKind.None);
            }
            return ((string)export.ModuleID, TypeScript.ModuleSpecifiers.ResultKind.Ambient);
        }

        if (export.PackageName != "")
        {
            if (_registry._entrypoints.TryGetValue(export.Path, out var entrypoints))
            {
                foreach (var entrypoint in entrypoints)
                {
                    var includeConditions = entrypoint.IncludeConditions ?? new TypeScript.Collections.Set<string>();
                    var excludeConditions = entrypoint.ExcludeConditions ?? new TypeScript.Collections.Set<string>();
                    if (includeConditions.IsSubsetOf(_conditions) && !_conditions.Intersects(excludeConditions))
                    {
                        var specifier = ProcessEntrypointEnding(
                            entrypoint,
                            userPreferences,
                            _program,
                            _program.Options(),
                            _importingFile,
                            GetAllowedEndings());

                        if (!IsExcludedByRegex(specifier, userPreferences.AutoImportSpecifierExcludeRegexes))
                        {
                            return (specifier, TypeScript.ModuleSpecifiers.ResultKind.NodeModules);
                        }
                    }
                }
                return ("", TypeScript.ModuleSpecifiers.ResultKind.None);
            }
        }

        if (!_registry._specifierCache.TryGetValue(_importingFilePath, out var cache))
        {
            Debug.Fail("no specifier cache for the importing file");
            throw new System.Diagnostics.UnreachableException();
        }
        if (export.PackageName == "")
        {
            var (cachedSpecifier, ok) = cache.Load(export.Path);
            if (ok)
            {
                if (cachedSpecifier == "")
                {
                    return ("", TypeScript.ModuleSpecifiers.ResultKind.None);
                }
                return (cachedSpecifier!, TypeScript.ModuleSpecifiers.ResultKind.Relative);
            }
        }

        var (specifiers, kind) = GetModuleSpecifiersForFileWithInfo(
            _importingFile,
            export.ModuleFileName,
            _program.Options(),
            _program,
            userPreferences,
            new TypeScript.ModuleSpecifiers.ModuleSpecifierOptions(),
            true);
        // !!! unsure when this could return multiple specifiers combined with the
        //     new node_modules code. Possibly with local symlinks, which should be
        //     very rare.
        foreach (var specifier in specifiers)
        {
            if (specifier.Contains("/node_modules/", StringComparison.Ordinal))
            {
                continue;
            }
            cache.Store(export.Path, specifier);
            return (specifier, kind);
        }
        cache.Store(export.Path, "");
        return ("", TypeScript.ModuleSpecifiers.ResultKind.None);
    }
}
