// ported from: internal/module/util.go
//
// 与 Go 的偏离（docs/architecture.md §2/§3）：
// - Go 的 `typeScriptVersion = semver.MustParse(core.Version())` 是包级 var → C# `private static readonly Version`。
// - Go 的 `(packageName, rest string)` 双返回值 → C# `string ParsePackageName(string moduleName, out string rest)`
//   （§3.2 的 out 约定；主值 + out 次值）。
// - Go 的 `strings.HasPrefix`/`strings.Cut`/`strings.TrimPrefix` → `StartsWith`/`IndexOf` + 切片/`RemovePrefix`。
// - Go 的 `core.IfElse`/`tspath.*`/`diagnostics.*` 包级成员 → `using static` 后的裸名（§2 的同名陷阱：
//   `Core.X`/`TSPath.X` 这种"命名空间 + 静态类成员"限定在 C# 里不成立）。
using TypeScript.Ast;
using TypeScript.Core;
using TypeScript.Diagnostics;
using TypeScript.SemVer;
using TypeScript.TSPath;
using static TypeScript.Core.Core;
using static TypeScript.Diagnostics.Diagnostics;
using static TypeScript.SemVer.SemVer;
using static TypeScript.TSPath.TSPath;

namespace TypeScript.Module;

public static partial class Module
{
    private static readonly SemVer.Version _typeScriptVersion = MustParse(Version());

    public const string InferredTypesContainingFile = "__inferred type names__.ts";

    public static bool IsApplicableVersionedTypesKey(string key)
    {
        const string typesPrefix = "types@";
        if (!key.StartsWith(typesPrefix, StringComparison.Ordinal))
        {
            return false;
        }

        if (!TryParseVersionRange(key[typesPrefix.Length..], out var range))
        {
            return false;
        }

        return range.Test(_typeScriptVersion);
    }

    public static string ParseNodeModuleFromPath(string resolved, bool isFolder)
    {
        var path = NormalizePath(resolved);
        var idx = path.LastIndexOf("/node_modules/", StringComparison.Ordinal);
        if (idx == -1)
        {
            return "";
        }

        var indexAfterNodeModules = idx + "/node_modules/".Length;
        var indexAfterPackageName = MoveToNextDirectorySeparatorIfAvailable(path, indexAfterNodeModules, isFolder);
        if (path[indexAfterNodeModules] == '@')
        {
            indexAfterPackageName = MoveToNextDirectorySeparatorIfAvailable(path, indexAfterPackageName, isFolder);
        }

        return path[..indexAfterPackageName];
    }

    public static string ParsePackageName(string moduleName, out string rest)
    {
        var idx = moduleName.IndexOf('/');
        if (moduleName.Length > 0 && moduleName[0] == '@')
        {
            var offset = idx + 1;
            idx = moduleName.IndexOf('/', offset);
        }

        if (idx == -1)
        {
            rest = "";
            return moduleName;
        }

        rest = moduleName[(idx + 1)..];
        return moduleName[..idx];
    }

    public static string MangleScopedPackageName(string packageName)
    {
        if (packageName.Length > 0 && packageName[0] == '@')
        {
            var idx = packageName.IndexOf('/');
            if (idx == -1)
            {
                return packageName;
            }

            return packageName[1..idx] + "__" + packageName[(idx + 1)..];
        }

        return packageName;
    }

    public static string UnmangleScopedPackageName(string packageName)
    {
        const string separator = "__";
        var idx = packageName.IndexOf(separator, StringComparison.Ordinal);
        if (idx != -1)
        {
            return "@" + packageName[..idx] + "/" + packageName[(idx + separator.Length)..];
        }

        return packageName;
    }

    public static string GetTypesPackageName(string packageName)
    {
        return "@types/" + MangleScopedPackageName(packageName);
    }

    public static string GetPackageNameFromTypesPackageName(string mangledName)
    {
        const string atTypesPrefix = "@types/";
        if (mangledName.StartsWith(atTypesPrefix, StringComparison.Ordinal))
        {
            return UnmangleScopedPackageName(mangledName[atTypesPrefix.Length..]);
        }

        return mangledName;
    }

    public static int ComparePatternKeys(string a, string b)
    {
        var aPatternIndex = a.IndexOf('*');
        var bPatternIndex = b.IndexOf('*');
        var baseLenA = a.Length;
        if (aPatternIndex != -1)
        {
            baseLenA = aPatternIndex + 1;
        }

        var baseLenB = b.Length;
        if (bPatternIndex != -1)
        {
            baseLenB = bPatternIndex + 1;
        }

        if (baseLenA > baseLenB)
        {
            return -1;
        }

        if (baseLenB > baseLenA)
        {
            return 1;
        }

        if (aPatternIndex == -1)
        {
            return 1;
        }

        if (bPatternIndex == -1)
        {
            return -1;
        }

        if (a.Length > b.Length)
        {
            return -1;
        }

        if (b.Length > a.Length)
        {
            return 1;
        }

        return 0;
    }

    /// <summary>
    /// Returns a DiagnosticMessage if we won't include a resolved module due to its extension.
    /// The DiagnosticMessage's parameters are the imported module name, and the filename it resolved to.
    /// This returns a diagnostic even if the module will be an untyped module.
    /// </summary>
    public static Message? GetResolutionDiagnostic(CompilerOptions options, ResolvedModule resolvedModule, SourceFile? file)
    {
        Message? NeedJsx()
        {
            if (options.Jsx != JsxEmit.None)
            {
                return null;
            }

            return Module_0_was_resolved_to_1_but_jsx_is_not_set;
        }

        Message? NeedAllowJs()
        {
            if (options.GetAllowJS() || !options.NoImplicitAny.DefaultIfUnknown(options.Strict).IsTrue())
            {
                return null;
            }

            return Could_not_find_a_declaration_file_for_module_0_1_implicitly_has_an_any_type;
        }

        Message? NeedResolveJsonModule()
        {
            if (options.GetResolveJsonModule())
            {
                return null;
            }

            return Module_0_was_resolved_to_1_but_resolveJsonModule_is_not_used;
        }

        Message? NeedAllowArbitraryExtensions()
        {
            if ((file?.IsDeclarationFile ?? false) || options.AllowArbitraryExtensions.IsTrue())
            {
                return null;
            }

            return Module_0_was_resolved_to_1_but_allowArbitraryExtensions_is_not_set;
        }

        if (resolvedModule.ResolvedUsingExtraExtensions)
        {
            return null;
        }

        switch (resolvedModule.Extension)
        {
            case ExtensionTs:
            case ExtensionDts:
            case ExtensionMts:
            case ExtensionDmts:
            case ExtensionCts:
            case ExtensionDcts:
                // These are always allowed.
                return null;
            case ExtensionTsx:
                return NeedJsx();
            case ExtensionJsx:
                if (NeedJsx() is { } jsxMessage)
                {
                    return jsxMessage;
                }

                return NeedAllowJs();
            case ExtensionJs:
            case ExtensionMjs:
            case ExtensionCjs:
                return NeedAllowJs();
            case ExtensionJson:
                return NeedResolveJsonModule();
            default:
                return NeedAllowArbitraryExtensions();
        }
    }

    /// <summary>
    /// TryGetJSExtensionForFile maps TS/JS/DTS extensions to the output JS-side extension.
    /// Returns an empty string if the extension is unsupported.
    /// </summary>
    public static string TryGetJSExtensionForFile(string fileName, CompilerOptions options)
    {
        var ext = TryGetExtensionFromPath(fileName);
        switch (ext)
        {
            case ExtensionTs:
            case ExtensionDts:
                return ExtensionJs;
            case ExtensionTsx:
                if (options.Jsx == JsxEmit.Preserve)
                {
                    return ExtensionJsx;
                }

                return ExtensionJs;
            case ExtensionJs:
            case ExtensionJsx:
            case ExtensionJson:
                return ext;
            case ExtensionDmts:
            case ExtensionMts:
            case ExtensionMjs:
                return ExtensionMjs;
            case ExtensionDcts:
            case ExtensionCts:
            case ExtensionCjs:
                return ExtensionCjs;
            default:
                return "";
        }
    }
}
