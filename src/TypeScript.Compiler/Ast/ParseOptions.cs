// ported from: internal/ast/parseoptions.go
//
// 与 Go 的差异（C# 语言特性，依据 docs/architecture.md §3.2）：
// - Go 的 `SourceFileParseOptions`/`ExternalModuleIndicatorOptions` 结构体 → C# class（字段名照抄 Go 的导出
//   字段）。Go 的零值构造 `ExternalModuleIndicatorOptions{}` → `new ExternalModuleIndicatorOptions()`；
//   唯一的就地写入点是 contentmapper（`parseOptions.ExternalModuleIndicatorOptions.Force = true`），
//   那是对同一个对象写字段，class 语义与 Go 的"结构体字段就地赋值"一致。
// - Go 的 `tspath.Path` → `TypeScript.Ast.SourceFileParseOptions.Path` 字段类型 `TSPath.Path`；用
//   `using Path = TypeScript.TSPath.Path;` 消歧（ImplicitUsings 会带进 System.IO.Path，裸写 Path 是 CS0104）。
// - Go 的包级 var `isFileForcedToBeModuleByFormatExtensions` → private static readonly 字段
//   `_isFileForcedToBeModuleByFormatExtensions`（非公开字段 → `_camelCase`）。
// - Go 的 `*core.CompilerOptions` nil 指针在 C# 里是 `CompilerOptions?`，解引用即 NRE（Go 是 panic）；
//   此处用局部变量 `opts = options!` 表达"与 Go 一样不做 nil 检查"。
// - 推迟移植（依赖 SourceFile 的完整实现与其它 utilities.go 函数，见 docs/ast.md 的轮次划分）：
//   SetExternalModuleIndicator、getExternalModuleIndicator、isFileProbablyExternalModule、
//   isAnExternalModuleIndicatorNode、getImportMetaIfNecessary、findChildNode、isFileModuleFromUsingJSXTag、
//   walkTreeForJSXTags。
// - `GetImpliedNodeFormatForEmitWorker`（Go 里在 utilities.go）由 Utilities.cs 提供，本轮只调用。
using TypeScript.Core;
using static TypeScript.TSPath.TSPath;
using Path = TypeScript.TSPath.Path;

namespace TypeScript.Ast;

public sealed class SourceFileParseOptions
{
    public string FileName = "";
    public Path Path;
    public ExternalModuleIndicatorOptions ExternalModuleIndicatorOptions = new();
}

public sealed class ExternalModuleIndicatorOptions
{
    public bool JSX;
    public bool Force;
}

public static partial class Ast
{
    internal static ExternalModuleIndicatorOptions GetExternalModuleIndicatorOptions(string fileName, CompilerOptions? options, SourceFileMetaData metadata)
    {
        if (IsDeclarationFileName(fileName))
        {
            return new ExternalModuleIndicatorOptions();
        }

        // Go 直接解引用 options（nil 会 panic）；这里同样不检查。
        var opts = options!;

        switch (opts.GetEmitModuleDetectionKind())
        {
            case ModuleDetectionKind.Force:
                // All non-declaration files are modules, declaration files still do the usual isFileProbablyExternalModule
                return new ExternalModuleIndicatorOptions { Force = true };
            case ModuleDetectionKind.Legacy:
                // Files are modules if they have imports, exports, or import.meta
                return new ExternalModuleIndicatorOptions();
            case ModuleDetectionKind.Auto:
                // If module is nodenext or node16, all esm format files are modules
                // If jsx is react-jsx or react-jsxdev then jsx tags force module-ness
                // otherwise, the presence of import or export statments (or import.meta) implies module-ness
                return new ExternalModuleIndicatorOptions
                {
                    JSX = opts.Jsx == JsxEmit.ReactJSX || opts.Jsx == JsxEmit.ReactJSXDev,
                    Force = IsFileForcedToBeModuleByFormat(fileName, opts, metadata),
                };
            default:
                return new ExternalModuleIndicatorOptions();
        }
    }

    private static readonly string[] _isFileForcedToBeModuleByFormatExtensions = [ExtensionCjs, ExtensionCts, ExtensionMjs, ExtensionMts];

    private static bool IsFileForcedToBeModuleByFormat(string fileName, CompilerOptions? options, SourceFileMetaData metadata)
    {
        // Go: options 是 `*core.CompilerOptions`，这里不做 nil 检查（解引用即 NRE，对应 Go 的 panic）。
        var opts = options!;

        // Excludes declaration files - they still require an explicit `export {}` or the like
        // for back compat purposes. The only non-declaration files _not_ forced to be a module are `.js` files
        // that aren't esm-mode (meaning not in a `type: module` scope).
        if (GetImpliedNodeFormatForEmitWorker(fileName, opts.GetEmitModuleKind(), metadata) == ModuleKind.ESNext || FileExtensionIsOneOf(fileName, _isFileForcedToBeModuleByFormatExtensions))
        {
            return true;
        }

        return false;
    }
}
