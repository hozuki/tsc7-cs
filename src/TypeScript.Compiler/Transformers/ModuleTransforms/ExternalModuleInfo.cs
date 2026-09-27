// ported from: internal/transformers/moduletransforms/externalmoduleinfo.go
//
// Go → C# 形状说明（docs/architecture.md §2、docs/ast.md §2）：
// 1) 未导出类型 `externalModuleInfo`/`externalModuleInfoCollector` → C# 按习惯命名 PascalCase 的 class
//    （Go 的 struct 值语义在这里只被单一调用者使用，class + 引用语义等价）。
// 2) 未导出字段 → `_camelCase`（跨 partial/跨类访问的是 internal）：`_externalImports`/`_exportSpecifiers`/…
// 3) Go 的 `collections.MultiMap[K,V].Get` 返回 nil 切片表示"没有" → C# 返回 `List<V>?`（contract §3.2）。
// 4) Go 的 `slices`/`append` 用法：需要按追加语义增长时用 `List<T>`（`Count` 等价 `len`），只在边界
//    （NewNodeList/Map/SingleOrMany）`.ToArray()`。
// 5) Go 的 `core.AppendIfUnique`/`core.Map`/`core.Some` → 静态类 `Core` 的 static 成员（using static）。
// 6) Go 的 `slices.SortFunc(xs, stringutil.CompareStringsCaseSensitive)` → `Array.Sort(xs, CompareStringsCaseSensitive)`：
//    两者都是不稳定排序，且此处元素已去重（全序），结果一致。
// 7) Go 的包级函数（collectExternalModuleInfo/createExternalHelpersImportDeclarationIfNeeded/…）→ 与包同名的
//    静态类 `ModuleTransforms`（跨 partial 文件）的 static 成员。

using System;
using TypeScript.Ast;
using TypeScript.Binder;
using TypeScript.Collections;
using TypeScript.Core;
using TypeScript.Printer;
using static TypeScript.Ast.Ast;
using static TypeScript.Core.Core;
using static TypeScript.StringUtil.StringUtil;
using static TypeScript.Transformers.Transformers;

namespace TypeScript.Transformers.ModuleTransforms;

/// <summary>Go: <c>type externalModuleInfo struct</c>（moduletransforms/externalmoduleinfo.go:13-22）。</summary>
internal sealed class ExternalModuleInfo
{
    /// <summary>Go: <c>externalImports</c>（ImportDeclaration | ImportEqualsDeclaration | ExportDeclaration）。</summary>
    internal List<Node> _externalImports = [];

    /// <summary>Go: <c>exportSpecifiers</c>：Maps local names to their associated export specifiers (excludes reexports)。</summary>
    internal MultiMap<string, Node> _exportSpecifiers = new();

    /// <summary>Go: <c>exportedBindings</c>：Maps local declarations to their associated export aliases。</summary>
    internal MultiMap<Node, Node> _exportedBindings = new();

    /// <summary>Go: <c>exportedNames</c>：all exported names in the module。</summary>
    internal List<Node> _exportedNames = [];

    /// <summary>Go: <c>exportedFunctions</c>：all of the top-level exported function declarations。</summary>
    internal OrderedSet<Node> _exportedFunctions = new();

    /// <summary>Go: <c>exportEquals</c>：an export=/module.exports= declaration if one was present。</summary>
    internal Node? _exportEquals;

    /// <summary>Go: <c>hasExportStarsToExportValues</c>：whether this module contains export*。</summary>
    internal bool _hasExportStarsToExportValues;
}

/// <summary>Go: <c>type externalModuleInfoCollector struct</c>（externalmoduleinfo.go:24-32）。</summary>
internal sealed class ExternalModuleInfoCollector
{
    internal SourceFile _sourceFile = null!;
    internal CompilerOptions _compilerOptions = null!;
    internal EmitContext _emitContext = null!;
    internal ReferenceResolver? _resolver;
    internal Set<string> _uniqueExports = new();
    internal bool _hasExportDefault;
    internal ExternalModuleInfo _output = new();

    /// <summary>Go: <c>func (c *externalModuleInfoCollector) collect() *externalModuleInfo</c>。</summary>
    internal ExternalModuleInfo Collect()
    {
        var hasImportStar = false;
        var hasImportDefault = false;
        foreach (var node in _sourceFile.Statements!.Nodes ?? [])
        {
            // Look through NotEmittedStatement to find elided export= declarations
            // (e.g., `declare export = x` is elided by the type eraser but must still be collected)
            if (IsNotEmittedStatement(node))
            {
                var original = _emitContext.MostOriginal(node);
                if (original is not null && IsExportAssignment(original))
                {
                    var n = original.AsExportAssignment()!;
                    if (n.IsExportEquals && _output._exportEquals is null)
                    {
                        _output._exportEquals = n;
                    }
                }
                continue;
            }
            switch (node.Kind)
            {
                case Kind.ImportDeclaration:
                {
                    // import "mod"
                    // import x from "mod"
                    // import * as x from "mod"
                    // import { x, y } from "mod"
                    var n = node.AsImportDeclaration()!;
                    AddExternalImport(node);
                    if (!hasImportStar && ModuleTransforms.GetImportNeedsImportStarHelper(n))
                    {
                        hasImportStar = true;
                    }
                    if (!hasImportDefault && ModuleTransforms.GetImportNeedsImportDefaultHelper(n))
                    {
                        hasImportDefault = true;
                    }
                    break;
                }

                case Kind.ImportEqualsDeclaration:
                {
                    var n = node.AsImportEqualsDeclaration()!;
                    if (IsExternalModuleReference(n.ModuleReference!))
                    {
                        // import x = require("mod")
                        AddExternalImport(node);
                    }
                    break;
                }

                case Kind.ExportDeclaration:
                {
                    var n = node.AsExportDeclaration()!;
                    if (n.ModuleSpecifier is not null)
                    {
                        // export * from "mod"
                        // export * as ns from "mod"
                        // export { x, y } from "mod"
                        AddExternalImport(node);
                        if (n.ExportClause is null)
                        {
                            // export * from "mod"
                            _output._hasExportStarsToExportValues = true;
                        }
                        else if (IsNamedExports(n.ExportClause))
                        {
                            // export { x, y } from "mod"
                            AddExportedNamesForExportDeclaration(n);
                            if (!hasImportDefault)
                            {
                                hasImportDefault = ModuleTransforms.ContainsDefaultReference(n.ExportClause);
                            }
                        }
                        else
                        {
                            // export * as ns from "mod"
                            var name = n.ExportClause.AsNamespaceExport()!.Name()!;
                            var nameText = name.Text();
                            if (AddUniqueExport(nameText))
                            {
                                AddExportedBinding(node, name);
                                AddExportedName(name);
                            }
                            // we use the same helpers for `export * as ns` as we do for `import * as ns`
                            hasImportStar = true;
                        }
                    }
                    else
                    {
                        // export { x, y }
                        AddExportedNamesForExportDeclaration(n);
                    }
                    break;
                }

                case Kind.ExportAssignment:
                {
                    var n = node.AsExportAssignment()!;
                    if (n.IsExportEquals && _output._exportEquals is null)
                    {
                        // export = x
                        _output._exportEquals = n;
                    }
                    break;
                }

                case Kind.VariableStatement:
                {
                    var n = node.AsVariableStatement()!;
                    if (HasSyntacticModifier(node, ModifierFlags.Export))
                    {
                        foreach (var decl in n.DeclarationList!.AsVariableDeclarationList()!.Declarations!.Nodes ?? [])
                        {
                            CollectExportedVariableInfo(decl);
                        }
                    }
                    break;
                }

                case Kind.FunctionDeclaration:
                {
                    var n = node.AsFunctionDeclaration()!;
                    if (HasSyntacticModifier(node, ModifierFlags.Export))
                    {
                        AddExportedFunctionDeclaration(n, null /*name*/, HasSyntacticModifier(node, ModifierFlags.Default));
                    }
                    break;
                }

                case Kind.ClassDeclaration:
                {
                    var n = node.AsClassDeclaration()!;
                    if (HasSyntacticModifier(node, ModifierFlags.Export))
                    {
                        if (HasSyntacticModifier(node, ModifierFlags.Default))
                        {
                            // export default class { }
                            if (!_hasExportDefault)
                            {
                                var name = n.Name();
                                name ??= _emitContext.Factory.NewGeneratedNameForNode(node);
                                AddExportedBinding(node, name);
                                _hasExportDefault = true;
                            }
                        }
                        else
                        {
                            // export class x { }
                            var name = n.Name();
                            if (name is not null)
                            {
                                if (AddUniqueExport(name.Text()))
                                {
                                    AddExportedBinding(node, name);
                                    AddExportedName(name);
                                }
                            }
                        }
                    }
                    break;
                }
            }
        }

        return _output;
    }

    /// <summary>Go: <c>func (c *externalModuleInfoCollector) addUniqueExport(name string) bool</c>。</summary>
    private bool AddUniqueExport(string name)
    {
        if (!_uniqueExports.Has(name))
        {
            _uniqueExports.Add(name);
            return true;
        }
        return false;
    }

    /// <summary>Go: <c>func (c *externalModuleInfoCollector) addExportedBinding(decl *ast.Declaration, name *ast.ModuleExportName)</c>。</summary>
    private void AddExportedBinding(Node decl, Node name)
    {
        _output._exportedBindings.Add(_emitContext.MostOriginal(decl)!, name);
    }

    /// <summary>Go: <c>func (c *externalModuleInfoCollector) addExternalImport(node *ast.Node)</c>。</summary>
    private void AddExternalImport(Node node)
    {
        _output._externalImports.Add(node);
    }

    /// <summary>Go: <c>func (c *externalModuleInfoCollector) addExportedName(name *ast.ModuleExportName)</c>。</summary>
    private void AddExportedName(Node name)
    {
        _output._exportedNames.Add(name);
    }

    /// <summary>Go: <c>func (c *externalModuleInfoCollector) addExportedNamesForExportDeclaration(node *ast.ExportDeclaration)</c>。</summary>
    private void AddExportedNamesForExportDeclaration(ExportDeclaration node)
    {
        foreach (var specifier in node.ExportClause!.Elements() ?? [])
        {
            var specifierNameText = specifier.Name()!.Text();
            if (AddUniqueExport(specifierNameText))
            {
                var name = specifier.PropertyNameOrName()!;
                if (name.Kind != Kind.StringLiteral)
                {
                    if (node.ModuleSpecifier is null)
                    {
                        _output._exportSpecifiers.Add(name.Text(), specifier.AsExportSpecifier()!);
                    }

                    var decl = _resolver!.GetReferencedImportDeclaration(_emitContext.MostOriginal(name)!);
                    decl ??= _resolver.GetReferencedValueDeclaration(_emitContext.MostOriginal(name)!);
                    if (decl is not null)
                    {
                        if (decl.Kind == Kind.FunctionDeclaration)
                        {
                            _uniqueExports.Delete(specifierNameText);
                            AddExportedFunctionDeclaration(decl.AsFunctionDeclaration()!, specifier.Name(), ModuleExportNameIsDefault(specifier.Name()));
                            continue;
                        }
                        AddExportedBinding(decl, specifier.Name()!);
                    }
                }

                AddExportedName(specifier.Name()!);
            }
        }
    }

    /// <summary>Go: <c>func (c *externalModuleInfoCollector) addExportedFunctionDeclaration(node *ast.FunctionDeclaration, name *ast.ModuleExportName, isDefault bool)</c>。</summary>
    private void AddExportedFunctionDeclaration(FunctionDeclaration node, Node? name, bool isDefault)
    {
        _output._exportedFunctions.Add(_emitContext.MostOriginal(node.AsNode())!);
        if (isDefault)
        {
            // export default function() { }
            // function x() { } + export { x as default };
            if (!_hasExportDefault)
            {
                name ??= _emitContext.Factory.NewGeneratedNameForNode(node.AsNode());
                AddExportedBinding(node.AsNode(), name);
                _hasExportDefault = true;
            }
        }
        else
        {
            // export function x() { }
            // function x() { } + export { x }
            name ??= node.Name();
            var nameText = name!.Text();
            if (AddUniqueExport(nameText))
            {
                AddExportedBinding(node.AsNode(), name);
            }
        }
    }

    /// <summary>Go: <c>func (c *externalModuleInfoCollector) collectExportedVariableInfo(decl *ast.Node)</c>。</summary>
    private void CollectExportedVariableInfo(Node decl)
    {
        if (IsBindingPattern(decl.Name()))
        {
            foreach (var element in decl.Name()!.Elements() ?? [])
            {
                var e = element.AsBindingElement()!;
                if (e.Name() is not null)
                {
                    CollectExportedVariableInfo(element);
                }
            }
        }
        else if (!_emitContext.HasAutoGenerateInfo(decl.Name()))
        {
            var text = decl.Name()!.Text();
            if (AddUniqueExport(text))
            {
                AddExportedName(decl.Name()!);
                if (IsLocalName(_emitContext, decl.Name()!))
                {
                    AddExportedBinding(decl, decl.Name()!);
                }
            }
        }
    }
}

/// <summary>Go: moduletransforms 包的包级函数（externalmoduleinfo.go 的 helpers）。</summary>
public static partial class ModuleTransforms
{
    /// <summary>Go: <c>func collectExternalModuleInfo(sourceFile *ast.SourceFile, compilerOptions *core.CompilerOptions, emitContext *printer.EmitContext, resolver binder.ReferenceResolver) *externalModuleInfo</c>。</summary>
    internal static ExternalModuleInfo CollectExternalModuleInfo(
        SourceFile sourceFile,
        CompilerOptions compilerOptions,
        EmitContext emitContext,
        ReferenceResolver? resolver)
    {
        var c = new ExternalModuleInfoCollector
        {
            _sourceFile = sourceFile,
            _compilerOptions = compilerOptions,
            _emitContext = emitContext,
            _resolver = resolver,
            _output = new ExternalModuleInfo(),
        };
        return c.Collect();
    }

    /// <summary>Go: <c>const externalHelpersModuleNameText = "tslib"</c>。const 任何可见性都用 PascalCase（§2）。</summary>
    internal const string ExternalHelpersModuleNameText = "tslib";

    /// <summary>
    /// Go: <c>func createExternalHelpersImportDeclarationIfNeeded(emitContext *printer.EmitContext, sourceFile *ast.SourceFile, compilerOptions *core.CompilerOptions, fileModuleKind core.ModuleKind, hasExportStarsToExportValues bool, hasImportStar bool, hasImportDefault bool) *ast.Node</c>。
    /// </summary>
    internal static Node? CreateExternalHelpersImportDeclarationIfNeeded(
        EmitContext emitContext,
        SourceFile sourceFile,
        CompilerOptions compilerOptions,
        ModuleKind fileModuleKind,
        bool hasExportStarsToExportValues,
        bool hasImportStar,
        bool hasImportDefault)
    {
        if (compilerOptions.ImportHelpers.IsTrue() && IsEffectiveExternalModule(sourceFile, compilerOptions))
        {
            var moduleKind = compilerOptions.GetEmitModuleKind();
            var helpers = GetImportedHelpers(emitContext, sourceFile);
            if (fileModuleKind == ModuleKind.CommonJS || fileModuleKind == ModuleKind.None && moduleKind == ModuleKind.CommonJS)
            {
                // When we emit to a non-ES module, generate a synthetic `import tslib = require("tslib")` to be further transformed.
                var externalHelpersModuleName = GetOrCreateExternalHelpersModuleNameIfNeeded(emitContext, sourceFile, compilerOptions, helpers, hasExportStarsToExportValues, hasImportStar || hasImportDefault, fileModuleKind);
                if (externalHelpersModuleName is not null)
                {
                    var externalHelpersImportDeclaration = emitContext.Factory.NewImportEqualsDeclaration(
                        null,  /*modifiers*/
                        false, /*isTypeOnly*/
                        externalHelpersModuleName,
                        emitContext.Factory.NewExternalModuleReference(emitContext.Factory.NewStringLiteral(ExternalHelpersModuleNameText, TokenFlags.None)));
                    emitContext.AddEmitFlags(externalHelpersImportDeclaration, EmitFlags.CustomPrologue);
                    return externalHelpersImportDeclaration;
                }
            }
            else
            {
                // When we emit as an ES module, generate an `import` declaration that uses named imports for helpers.
                // If we cannot determine the implied module kind under `module: preserve` we assume ESM.
                var helperNames = Array.Empty<string>();
                foreach (var helper in helpers)
                {
                    var importName = helper.ImportName;
                    if (importName.Length > 0)
                    {
                        helperNames = AppendIfUnique(helperNames, importName);
                    }
                }
                if (helperNames.Length > 0)
                {
                    Array.Sort(helperNames, CompareStringsCaseSensitive);
                    // Alias the imports if the names are used somewhere in the file.
                    // NOTE: We don't need to care about global import collisions as this is a module.

                    var importSpecifiers = Map(helperNames, (string name) =>
                    {
                        if (emitContext.IsFileLevelUniqueName(sourceFile, name, null /*hasGlobalName*/))
                        {
                            return emitContext.Factory.NewImportSpecifier(false /*isTypeOnly*/, null /*propertyName*/, emitContext.Factory.NewIdentifier(name));
                        }
                        else
                        {
                            return emitContext.Factory.NewImportSpecifier(false /*isTypeOnly*/, emitContext.Factory.NewIdentifier(name), emitContext.Factory.NewUnscopedHelperName(name));
                        }
                    });
                    var namedBindings = emitContext.Factory.NewNamedImports(emitContext.Factory.NewNodeList(importSpecifiers));
                    var parseNode = emitContext.MostOriginal(sourceFile.AsNode())!;
                    emitContext.AddEmitFlags(parseNode, EmitFlags.ExternalHelpers);

                    var externalHelpersImportDeclaration = emitContext.Factory.NewImportDeclaration(
                        null, /*modifiers*/
                        emitContext.Factory.NewImportClause(Kind.Unknown /*phaseModifier*/, null /*name*/, namedBindings),
                        emitContext.Factory.NewStringLiteral(ExternalHelpersModuleNameText, TokenFlags.None),
                        null /*attributes*/);

                    emitContext.AddEmitFlags(externalHelpersImportDeclaration, EmitFlags.CustomPrologue);
                    return externalHelpersImportDeclaration;
                }
            }
        }
        return null;
    }

    /// <summary>Go: <c>func getImportedHelpers(emitContext *printer.EmitContext, sourceFile *ast.SourceFile) []*printer.EmitHelper</c>。</summary>
    internal static EmitHelper[] GetImportedHelpers(EmitContext emitContext, SourceFile sourceFile)
    {
        var helpers = new List<EmitHelper>();
        foreach (var helper in emitContext.GetEmitHelpers(sourceFile.AsNode()) ?? [])
        {
            if (!helper.Scoped)
            {
                helpers.Add(helper);
            }
        }
        return helpers.ToArray();
    }

    /// <summary>Go: <c>func getOrCreateExternalHelpersModuleNameIfNeeded(emitContext *printer.EmitContext, node *ast.SourceFile, compilerOptions *core.CompilerOptions, helpers []*printer.EmitHelper, hasExportStarsToExportValues bool, hasImportStarOrImportDefault bool, fileModuleKind core.ModuleKind) *ast.IdentifierNode</c>。</summary>
    internal static Node? GetOrCreateExternalHelpersModuleNameIfNeeded(
        EmitContext emitContext,
        SourceFile node,
        CompilerOptions compilerOptions,
        EmitHelper[] helpers,
        bool hasExportStarsToExportValues,
        bool hasImportStarOrImportDefault,
        ModuleKind fileModuleKind)
    {
        var externalHelpersModuleName = emitContext.GetExternalHelpersModuleName(node);
        if (externalHelpersModuleName is not null)
        {
            return externalHelpersModuleName;
        }

        var create = helpers.Length > 0 ||
            (hasExportStarsToExportValues || hasImportStarOrImportDefault) &&
                fileModuleKind < ModuleKind.System;

        if (create)
        {
            externalHelpersModuleName = emitContext.Factory.NewUniqueName(ExternalHelpersModuleNameText);
            emitContext.SetExternalHelpersModuleName(node, externalHelpersModuleName);
        }

        return externalHelpersModuleName;
    }

    /// <summary>Go: <c>func isNamedDefaultReference(e *ast.Node) bool</c>。</summary>
    internal static bool IsNamedDefaultReference(Node e) => ModuleExportNameIsDefault(e.PropertyNameOrName());

    /// <summary>Go: <c>func containsDefaultReference(node *ast.Node) bool</c>。</summary>
    internal static bool ContainsDefaultReference(Node? node) =>
        node is not null && (IsNamedImports(node) || IsNamedExports(node)) && Some(node.Elements(), IsNamedDefaultReference);

    /// <summary>Go: <c>func getExportNeedsImportStarHelper(node *ast.ExportDeclaration) bool</c>。</summary>
    internal static bool GetExportNeedsImportStarHelper(ExportDeclaration node) =>
        GetNamespaceDeclarationNode(node.AsNode()) is not null;

    /// <summary>Go: <c>func getImportNeedsImportStarHelper(node *ast.ImportDeclaration) bool</c>。</summary>
    internal static bool GetImportNeedsImportStarHelper(ImportDeclaration node)
    {
        if (GetNamespaceDeclarationNode(node.AsNode()) is not null)
        {
            return true;
        }
        if (node.ImportClause is null)
        {
            return false;
        }
        var bindings = node.ImportClause.AsImportClause()!.NamedBindings;
        if (bindings is null)
        {
            return false;
        }
        if (!IsNamedImports(bindings))
        {
            return false;
        }
        var namedImports = bindings.AsNamedImports()!;
        var defaultRefCount = 0;
        foreach (var binding in namedImports.Elements!.Nodes ?? [])
        {
            if (IsNamedDefaultReference(binding))
            {
                defaultRefCount++;
            }
        }
        // Import star is required if there's default named refs mixed with non-default refs, or if theres non-default refs and it has a default import
        return (defaultRefCount > 0 && defaultRefCount != (namedImports.Elements!.Nodes?.Length ?? 0)) || (((namedImports.Elements!.Nodes?.Length ?? 0) - defaultRefCount) != 0 && IsDefaultImport(node.AsNode()));
    }

    /// <summary>Go: <c>func getImportNeedsImportDefaultHelper(node *ast.ImportDeclaration) bool</c>。</summary>
    internal static bool GetImportNeedsImportDefaultHelper(ImportDeclaration node)
    {
        // Import default is needed if there's a default import or a default ref and no other refs (meaning an import star helper wasn't requested)
        return !GetImportNeedsImportStarHelper(node) && (IsDefaultImport(node.AsNode()) || (node.ImportClause is not null &&
            IsNamedImports(node.ImportClause.AsImportClause()!.NamedBindings!) &&
            ContainsDefaultReference(node.ImportClause.AsImportClause()!.NamedBindings)));
    }
}
