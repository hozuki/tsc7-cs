// ported from: internal/transformers/moduletransforms/utilities.go
//
// Go → C# 形状说明（docs/architecture.md §2、docs/ast.md §2）：
// 1) Go 的包级函数（isDeclarationNameOfEnumOrNamespace…）→ 与包同名的静态类 `ModuleTransforms` 的 static 成员。
// 2) Go 的类型别名 `*ast.Expression`/`*ast.Statement`/`*ast.IdentifierNode`/`*ast.StringLiteralNode`/`*ast.ModuleExportName`
//    全部是 `*ast.Node` 的别名 → C# 一律用 `Node`（C# 不生成别名，docs/ast.md §2）。
// 3) Go 的 `host any /*EmitHost*/` → `EmitHost?`（TypeScript.Printer.EmitHost）；Go 的 `resolver printer.EmitResolver`
//    是接口，可为 nil → `EmitResolver?`。
// 4) Go 的 `*core.TextRange`（可空指针）→ `TextRange?`（值类型可空）。

using TypeScript.Ast;
using TypeScript.Core;
using TypeScript.Printer;
using TypeScript.Transformers;
using NodeFactory = TypeScript.Printer.NodeFactory; // 消歧：Ast 与 Printer 各有一个 NodeFactory；Go 原文此处写 printer.NodeFactory
using static TypeScript.Ast.Ast;
using static TypeScript.Core.Core;
using static TypeScript.OutputPaths.OutputPaths;
using static TypeScript.TSPath.TSPath;
using static TypeScript.Transformers.Transformers;

namespace TypeScript.Transformers.ModuleTransforms;

/// <summary>Go: moduletransforms 包的包级函数（internal/transformers/moduletransforms/utilities.go）。</summary>
public static partial class ModuleTransforms
{
    /// <summary>Go: <c>func isDeclarationNameOfEnumOrNamespace(emitContext *printer.EmitContext, node *ast.IdentifierNode) bool</c>。</summary>
    internal static bool IsDeclarationNameOfEnumOrNamespace(EmitContext emitContext, Node node)
    {
        var original = emitContext.MostOriginal(node);
        if (original is not null && original.Parent is not null)
        {
            switch (original.Parent.Kind)
            {
                case Kind.EnumDeclaration:
                case Kind.ModuleDeclaration:
                    return ReferenceEquals(original, original.Parent.Name());
            }
        }
        return false;
    }

    /// <summary>Go: <c>func rewriteModuleSpecifier(emitContext *printer.EmitContext, node *ast.Expression, compilerOptions *core.CompilerOptions) *ast.Expression</c>。</summary>
    internal static Node? RewriteModuleSpecifier(EmitContext emitContext, Node? node, CompilerOptions compilerOptions)
    {
        if (node is null || !IsStringLiteral(node) || !ShouldRewriteModuleSpecifier(node.Text(), compilerOptions))
        {
            return node;
        }
        var updatedText = ChangeExtension(node.Text(), GetOutputExtension(node.Text(), compilerOptions.Jsx));
        if (updatedText != node.Text())
        {
            var updated = emitContext.Factory.NewStringLiteral(updatedText, node.AsStringLiteral()!.TokenFlags);
            emitContext.SetOriginal(updated, node);
            emitContext.AssignCommentAndSourceMapRanges(updated, node);
            return updated;
        }
        return node;
    }

    /// <summary>Go: <c>func createEmptyImports(factory *printer.NodeFactory) *ast.Statement</c>。</summary>
    internal static Node CreateEmptyImports(NodeFactory factory)
    {
        return factory.NewExportDeclaration(
            null,  /*modifiers*/
            false, /*isTypeOnly*/
            factory.NewNamedExports(factory.NewNodeList(null)),
            null, /*moduleSpecifier*/
            null  /*attributes*/
        );
    }

    // Get the name of a target module from an import/export declaration as should be written in the emitted output.
    // The emitted output name can be different from the input if:
    //  1. The module has a /// <amd-module name="<new name>" />
    //  2. --out or --outFile is used, making the name relative to the rootDir
    //     3- The containing SourceFile has an entry in renamedDependencies for the import as requested by some module loaders (e.g. System).
    //
    // Otherwise, a new StringLiteral node representing the module name will be returned.
    /// <summary>
    /// Go: <c>func getExternalModuleNameLiteral(factory *printer.NodeFactory, importNode *ast.Node, sourceFile *ast.SourceFile, host any, resolver printer.EmitResolver, compilerOptions *core.CompilerOptions) *ast.StringLiteralNode</c>。
    /// </summary>
    internal static Node? GetExternalModuleNameLiteral(
        NodeFactory factory,
        Node importNode, /*ImportDeclaration | ExportDeclaration | ImportEqualsDeclaration | ImportCall*/
        SourceFile sourceFile,
        EmitHost? host,
        EmitResolver? resolver,
        CompilerOptions compilerOptions)
    {
        var moduleName = GetExternalModuleName(importNode);
        if (moduleName is not null && IsStringLiteral(moduleName))
        {
            var name = TryGetModuleNameFromDeclaration(importNode, host, factory, resolver, compilerOptions);
            name ??= TryRenameExternalModule(factory, moduleName, sourceFile);
            if (name is null) // !!! propagate token flags (will produce new diffs)
            {
                name = factory.NewStringLiteral(moduleName.Text(), TokenFlags.None);
            }
            return name;
        }
        return null;
    }

    // Get the name of a module as should be written in the emitted output.
    // The emitted output name can be different from the input if:
    //  1. The module has a /// <amd-module name="<new name>" />
    //  2. --out or --outFile is used, making the name relative to the rootDir
    //
    // Otherwise, a new StringLiteral node representing the module name will be returned.
    /// <summary>
    /// Go: <c>func tryGetModuleNameFromFile(factory *printer.NodeFactory, file *ast.SourceFile, host any, options *core.CompilerOptions) *ast.StringLiteralNode</c>。
    /// </summary>
    internal static Node? TryGetModuleNameFromFile(NodeFactory factory, SourceFile? file, EmitHost? host, CompilerOptions options)
    {
        if (file is null)
        {
            return null;
        }
        // !!!
        // if file.moduleName {
        // 	return factory.createStringLiteral(file.moduleName)
        // }
        return null;
    }

    /// <summary>
    /// Go: <c>func tryGetModuleNameFromDeclaration(declaration *ast.Node, host any, factory *printer.NodeFactory, resolver printer.EmitResolver, compilerOptions *core.CompilerOptions) *ast.StringLiteralNode</c>。
    /// </summary>
    internal static Node? TryGetModuleNameFromDeclaration(
        Node declaration, /*ImportEqualsDeclaration | ImportDeclaration | ExportDeclaration | ImportCall*/
        EmitHost? host,
        NodeFactory factory,
        EmitResolver? resolver,
        CompilerOptions compilerOptions)
    {
        if (resolver is null)
        {
            return null;
        }
        return TryGetModuleNameFromFile(factory, resolver.GetExternalModuleFileFromDeclaration(declaration), host, compilerOptions);
    }

    // Resolves a local path to a path which is absolute to the base of the emit
    /// <summary>Go: <c>func getExternalModuleNameFromPath(host any, fileName string, referencePath string) string</c>。</summary>
    internal static string GetExternalModuleNameFromPath(object? host, string fileName, string referencePath)
    {
        // !!!
        return "";
    }

    // Some bundlers (SystemJS builder) sometimes want to rename dependencies.
    // Here we check if alternative name was provided for a given moduleName and return it if possible.
    /// <summary>Go: <c>func tryRenameExternalModule(factory *printer.NodeFactory, moduleName *ast.LiteralExpression, sourceFile *ast.SourceFile) *ast.StringLiteralNode</c>。</summary>
    internal static Node? TryRenameExternalModule(NodeFactory factory, Node moduleName, SourceFile sourceFile)
    {
        // !!!
        return null;
    }

    /// <summary>Go: <c>func isFileLevelReservedGeneratedIdentifier(emitContext *printer.EmitContext, name *ast.IdentifierNode) bool</c>。</summary>
    internal static bool IsFileLevelReservedGeneratedIdentifier(EmitContext emitContext, Node name)
    {
        var info = emitContext.GetAutoGenerateInfo(name);
        return info is not null &&
            info.Flags.IsFileLevel() &&
            info.Flags.IsOptimistic() &&
            info.Flags.IsReservedInNestedScopes();
    }

    // A simple inlinable expression is an expression which can be copied into multiple locations
    // without risk of repeating any sideeffects and whose value could not possibly change between
    // any such locations
    /// <summary>Go: <c>func isSimpleInlineableExpression(expression *ast.Expression) bool</c>。</summary>
    internal static bool IsSimpleInlineableExpression(Node expression) =>
        !IsIdentifier(expression) && IsSimpleCopiableExpression(expression);
}
