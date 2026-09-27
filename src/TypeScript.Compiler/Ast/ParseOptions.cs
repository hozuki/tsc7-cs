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
// - 推迟移植（依赖尚未移植的 package/函数，见报告）：无。`SetExternalModuleIndicator` 系列（Go:63-149）本轮已落地，
//   `GetImpliedNodeFormatForEmitWorker`（Go 里在 utilities.go）由 Utilities.cs 提供，此处只调用。
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
    public static ExternalModuleIndicatorOptions GetExternalModuleIndicatorOptions(string fileName, CompilerOptions? options, SourceFileMetaData metadata)
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

    // ── SetExternalModuleIndicator 系列（parseoptions.go:63-149）──

    /// <summary>Go: <c>func SetExternalModuleIndicator(file *SourceFile, opts ExternalModuleIndicatorOptions)</c>。</summary>
    public static void SetExternalModuleIndicator(SourceFile file, ExternalModuleIndicatorOptions opts) =>
        file.ExternalModuleIndicator = GetExternalModuleIndicator(file, opts);

    /// <summary>Go: <c>func getExternalModuleIndicator(file *SourceFile, opts ExternalModuleIndicatorOptions) *Node</c>。</summary>
    private static Node? GetExternalModuleIndicator(SourceFile file, ExternalModuleIndicatorOptions opts)
    {
        if (file.ScriptKind == ScriptKind.JSON)
        {
            return null;
        }

        var node = IsFileProbablyExternalModule(file);
        if (node is not null)
        {
            return node;
        }

        if (file.IsDeclarationFile)
        {
            return null;
        }

        if (opts.JSX)
        {
            node = IsFileModuleFromUsingJSXTag(file);
            if (node is not null)
            {
                return node;
            }
        }

        if (opts.Force)
        {
            return file.AsNode();
        }

        return null;
    }

    /// <summary>Go: <c>func isFileProbablyExternalModule(sourceFile *SourceFile) *Node</c>。</summary>
    private static Node? IsFileProbablyExternalModule(SourceFile sourceFile)
    {
        // Go: sourceFile.Statements.Nodes（Statements 为 nil 即 panic；Nodes 为 nil 时 Go 的 range 是空循环）。
        var statements = sourceFile.Statements!.Nodes;
        if (statements is not null)
        {
            foreach (var statement in statements)
            {
                if (IsAnExternalModuleIndicatorNode(statement))
                {
                    return statement;
                }
            }
        }

        return GetImportMetaIfNecessary(sourceFile);
    }

    /// <summary>Go: <c>func isAnExternalModuleIndicatorNode(node *Node) bool</c>。</summary>
    private static bool IsAnExternalModuleIndicatorNode(Node node) =>
        HasSyntacticModifier(node, ModifierFlags.Export) ||
        (IsImportEqualsDeclaration(node) && IsExternalModuleReference(node.AsImportEqualsDeclaration()!.ModuleReference!)) ||
        IsImportDeclaration(node) ||
        IsExportAssignment(node) ||
        IsExportDeclaration(node);

    /// <summary>Go: <c>func getImportMetaIfNecessary(sourceFile *SourceFile) *Node</c>。</summary>
    private static Node? GetImportMetaIfNecessary(SourceFile sourceFile)
    {
        if ((sourceFile.Flags & NodeFlags.PossiblyContainsImportMeta) != 0)
        {
            // Go 直接传函数名；C# 传 `IsImportMeta` 方法组会给 Func&lt;Node,bool&gt; 带可空性告警，用 lambda。
            return FindChildNode(sourceFile.AsNode(), node => IsImportMeta(node));
        }

        return null;
    }

    /// <summary>Go: <c>func findChildNode(root *Node, check func(*Node) bool) *Node</c>。</summary>
    private static Node? FindChildNode(Node root, Func<Node, bool> check)
    {
        Node? result = null;
        bool Visit(Node node)
        {
            if (check(node))
            {
                result = node;
                return true;
            }

            return node.ForEachChild(Visit);
        }

        Visit(root);
        return result;
    }

    /// <summary>Go: <c>func isFileModuleFromUsingJSXTag(file *SourceFile) *Node</c>。</summary>
    private static Node? IsFileModuleFromUsingJSXTag(SourceFile file) => WalkTreeForJSXTags(file.AsNode());

    /// <summary>
    /// Go: <c>func walkTreeForJSXTags(node *Node) *Node</c>。为了定位 JSX 标签，这基本无法避免整树遍历
    /// （`import.meta` 也一样，但那靠 `PossiblyContainsImportMeta` 标记避免了大部分遍历；JSX 没有可用的 NodeFlag 空间）。
    /// </summary>
    private static Node? WalkTreeForJSXTags(Node node)
    {
        Node? found = null;

        bool Visitor(Node candidate)
        {
            if (found is not null)
            {
                return true;
            }

            if ((candidate.SubtreeFacts & SubtreeFacts.ContainsJsx) == 0)
            {
                return false;
            }

            if (IsJsxOpeningLikeElement(candidate) || IsJsxFragment(candidate))
            {
                found = candidate;
                return true;
            }

            return candidate.ForEachChild(Visitor);
        }

        Visitor(node);
        return found;
    }
}
