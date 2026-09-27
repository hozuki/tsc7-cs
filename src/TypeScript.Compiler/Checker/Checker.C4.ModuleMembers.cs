// ported from: internal/checker/checker.go:14901-15269
//   （getExternalModuleMember / getPropertyOfVariable / combineValueAndTypeSymbols / getExportOfModule /
//    isOnlyImportableAsDefault / canHaveSyntheticDefault / getEmitSyntaxForModuleSpecifierExpression /
//    errorNoModuleMemberSymbol / reportNonExportedMember / reportInvalidImportEqualsExportMember /
//    getTargetOfExportSpecifier / getTargetOfExportAssignment / getTargetOfBinaryExpression /
//    getTargetOfAliasLikeExpression / getTargetOfNamespaceExportDeclaration / getTargetOfAccessExpression）
//
// Go→C# 改写（本文件，逐条理由）：
// 1) `slices.Compact(slices.Concat(a, b))`（去相邻重复指针）在 C# 没有等价物 → 就地展开成
//    `Concatenate` + 一个"与前一个不同才加入"的小循环（见 `combineValueAndTypeSymbols` 的注释；不新增共享助手，
//    免得与别的片重名）。
// 2) `maps.Clone(x)` → `x is null ? null : new SymbolTable(x)`；`SymbolTable` 的索引读（Go 零值 nil）→
//    `GetValueOrDefault`（`moduleSymbol.Exports` 还是可空的，用 `?.`）。
// 3) `findInMap(exports, func(symbol) bool {...})` → `Utilities.FindInMap(exports, symbol => …)`（ChkLinksUtils 的
//    `internal static V FindInMap<K,V>(Dictionary<K,V>, Func<V,bool>)`）；`exports` 可空时按 Go 的 nil map 语义
//    先判空（Go 的 `findInMap(nil, …)` 返回零值 nil）。
// 4) 未导出成员命中 `artifacts/checker-method-collisions.md` 的改名照写：`resolveAlias` → `ResolveAliasWorker`、
//    `resolveExternalModuleName` → `ResolveExternalModuleNameWorker`、`resolveExternalModuleSymbol` →
//    `ResolveExternalModuleSymbolWorker`、`getFullyQualifiedName` → `GetFullyQualifiedNameWorker`、
//    `symbolToString` → `SymbolToStringWorker`、`getTypeOfSymbol` → `GetTypeOfSymbolWorker`、
//    `getPropertyOfType` → `GetPropertyOfTypeWorker`、`getTypeFromTypeNode` → `GetTypeFromTypeNodeWorker`。
// 5) 包级函数 → 同一类的 static 成员：`getExternalModuleRequireArgument` → `GetExternalModuleRequireArgument`、
//    `isShorthandAmbientModuleSymbol` → `IsShorthandAmbientModuleSymbol`、`isSyntacticDefault` → `IsSyntacticDefault`、
//    `hasExportAssignmentSymbol` → `HasExportAssignmentSymbol`（均在 utilities.go）、`isContainedByNamespace` →
//    `IsContainedByNamespace`（checker.go:5738）、`createDiagnosticForNode` → `CreateDiagnosticForNode`（本片）。
// 6) `c.program.*` → `_program.*`（Program 尚未落地 → 预期跨片缺失符号，见报告）。
namespace TypeScript.Checker;

using TypeScript.Ast;
using TypeScript.Core;
using TypeScript.Diagnostics;
using static TypeScript.Ast.Ast;
using static TypeScript.Core.Core;
using static TypeScript.Diagnostics.Diagnostics;
using static TypeScript.Scanner.Scanner;
using static TypeScript.TSPath.TSPath;

public sealed partial class Checker
{
    /// <summary>Go: <c>func (c *Checker) getExternalModuleMember(node *ast.Node, specifier *ast.Node, dontResolveAlias bool) *ast.Symbol</c>。</summary>
    internal Symbol? GetExternalModuleMember(Node node, Node specifier, bool dontResolveAlias)
    {
        // node is ImportDeclaration | ExportDeclaration | VariableDeclaration
        // specifier is ImportSpecifier | ExportSpecifier | BindingElement | PropertyAccessExpression
        var moduleSpecifier = GetExternalModuleRequireArgument(node);
        moduleSpecifier ??= GetExternalModuleName(node);
        Node? attributes = null;
        if (HasImportAttributes(node))
        {
            attributes = GetImportAttributes(node);
        }

        var importAttributesType = GetTypeFromImportAttributes(attributes);
        var moduleSymbol = ResolveExternalModuleNameWorker(node, moduleSpecifier!, false /*ignoreErrors*/, importAttributesType);
        Node? name;
        if (!IsPropertyAccessExpression(specifier))
        {
            name = specifier.PropertyNameOrName();
        }
        else
        {
            name = specifier.Name();
        }

        if (name is null || (!IsIdentifier(name) && !IsStringLiteral(name)))
        {
            return null;
        }

        var nameText = name.Text();
        var targetSymbol = ResolveESModuleSymbol(moduleSymbol, specifier, moduleSpecifier);
        if (targetSymbol is not null)
        {
            // Note: The empty string is a valid module export name:
            //
            //   import { "" as foo } from "./foo";
            //   export { foo as "" };
            //
            if (nameText != "" || name.Kind == Kind.StringLiteral)
            {
                if (IsShorthandAmbientModuleSymbol(moduleSymbol!))
                {
                    return moduleSymbol;
                }

                Symbol? symbolFromVariable;
                // First check if module was specified with "export=". If so, get the member from the resolved type
                if (moduleSymbol is not null && moduleSymbol.Exports?.GetValueOrDefault(InternalSymbolNameExportEquals) is not null)
                {
                    symbolFromVariable = GetPropertyOfTypeEx(GetTypeOfSymbolWorker(targetSymbol), nameText, true /*skipObjectFunctionPropertyAugment*/, false /*includeTypeOnlyMembers*/);
                }
                else
                {
                    symbolFromVariable = GetPropertyOfVariable(targetSymbol, nameText);
                }

                // if symbolFromVariable is export - get its final target
                symbolFromVariable = ResolveSymbolEx(symbolFromVariable, dontResolveAlias);
                var exportContainer = targetSymbol;
                if (moduleSymbol is not null && moduleSymbol.Exports?.GetValueOrDefault(InternalSymbolNameExportEquals) is not null)
                {
                    // For `export =` modules, supplemental type/namespace exports live on the original module symbol.
                    exportContainer = moduleSymbol;
                }

                var symbolFromModule = GetExportOfModule(exportContainer, nameText, specifier, dontResolveAlias);
                if (symbolFromModule is null && nameText == InternalSymbolNameDefault)
                {
                    var file = Find(moduleSymbol!.Declarations, IsSourceFile);
                    if (IsOnlyImportableAsDefault(moduleSpecifier, moduleSymbol, importAttributesType)
                        || CanHaveSyntheticDefault(file, moduleSymbol, dontResolveAlias, moduleSpecifier!))
                    {
                        symbolFromModule = ResolveExternalModuleSymbolWorker(moduleSymbol, dontResolveAlias);
                        symbolFromModule ??= ResolveSymbolEx(moduleSymbol, dontResolveAlias);
                    }
                }

                var symbol = symbolFromVariable;
                if (symbolFromModule is not null)
                {
                    symbol = symbolFromModule;
                    if (symbolFromVariable is not null)
                    {
                        symbol = CombineValueAndTypeSymbols(symbolFromVariable, symbolFromModule);
                    }
                }

                if (IsImportOrExportSpecifier(specifier)
                    && IsOnlyImportableAsDefault(moduleSpecifier, moduleSymbol, importAttributesType)
                    && nameText != InternalSymbolNameDefault)
                {
                    Error(name, Named_imports_from_a_JSON_file_into_an_ECMAScript_module_are_not_allowed_when_module_is_set_to_0, _moduleKind.String());
                }
                else if (symbol is null)
                {
                    ErrorNoModuleMemberSymbol(moduleSymbol!, targetSymbol, node, name);
                }

                return symbol;
            }
        }

        return null;
    }

    /// <summary>Go: <c>func (c *Checker) getPropertyOfVariable(symbol *ast.Symbol, name string) *ast.Symbol</c>。</summary>
    internal Symbol? GetPropertyOfVariable(Symbol symbol, string name)
    {
        if ((symbol.Flags & SymbolFlags.Variable) != 0)
        {
            var typeAnnotation = symbol.ValueDeclaration!.Type();
            if (typeAnnotation is not null)
            {
                return ResolveSymbol(GetPropertyOfTypeWorker(GetTypeFromTypeNodeWorker(typeAnnotation), name));
            }
        }

        return null;
    }

    // This function creates a synthetic symbol that combines the value side of one symbol with the
    // type/namespace side of another symbol. Consider this example:
    //
    //	declare module graphics {
    //	    interface Point {
    //	        x: number;
    //	        y: number;
    //	    }
    //	}
    //	declare var graphics: {
    //	    Point: new (x: number, y: number) => graphics.Point;
    //	}
    //	declare module "graphics" {
    //	    export = graphics;
    //	}
    //
    // An 'import { Point } from "graphics"' needs to create a symbol that combines the value side 'Point'
    // property with the type/namespace side interface 'Point'.
    /// <summary>Go: <c>func (c *Checker) combineValueAndTypeSymbols(valueSymbol *ast.Symbol, typeSymbol *ast.Symbol) *ast.Symbol</c>。</summary>
    internal Symbol CombineValueAndTypeSymbols(Symbol valueSymbol, Symbol typeSymbol)
    {
        if (valueSymbol == _unknownSymbol && typeSymbol == _unknownSymbol)
        {
            return _unknownSymbol!;
        }

        if ((typeSymbol.Flags & SymbolFlags.Value) != 0)
        {
            return typeSymbol;
        }

        if ((valueSymbol.Flags & (SymbolFlags.Type | SymbolFlags.Namespace)) != 0)
        {
            return valueSymbol;
        }

        var result = NewSymbol(valueSymbol.Flags | typeSymbol.Flags, valueSymbol.Name);
        Debug.Assert(valueSymbol.Declarations!.Length > 0 || typeSymbol.Declarations!.Length > 0);

        // Go: slices.Compact(slices.Concat(valueSymbol.Declarations, typeSymbol.Declarations))（去掉相邻的重复指针）
        var concatenated = Concatenate(valueSymbol.Declarations, typeSymbol.Declarations);
        var compacted = new List<Node>(concatenated.Length);
        foreach (var declaration in concatenated)
        {
            if (compacted.Count == 0 || compacted[^1] != declaration)
            {
                compacted.Add(declaration);
            }
        }

        result.Declarations = compacted.ToArray();
        result.Parent = valueSymbol.Parent;
        if (result.Parent is null)
        {
            result.Parent = typeSymbol.Parent;
        }

        result.ValueDeclaration = valueSymbol.ValueDeclaration;
        result.Members = typeSymbol.Members is null ? null : CloneSymbolTable(typeSymbol.Members);
        result.Exports = valueSymbol.Exports is null ? null : CloneSymbolTable(valueSymbol.Exports);
        return result;
    }

    /// <summary>Go: <c>func (c *Checker) getExportOfModule(symbol *ast.Symbol, nameText string, specifier *ast.Node, dontResolveAlias bool) *ast.Symbol</c>。</summary>
    internal Symbol? GetExportOfModule(Symbol symbol, string nameText, Node? specifier, bool dontResolveAlias)
    {
        if ((symbol.Flags & SymbolFlags.Module) != 0)
        {
            var exportSymbol = GetExportsOfSymbol(symbol).GetValueOrDefault(nameText);
            var resolved = ResolveSymbolEx(exportSymbol, dontResolveAlias);
            var exportStarDeclaration = _moduleSymbolLinks.Get(symbol)._typeOnlyExportStarMap?.GetValueOrDefault(nameText);
            MarkSymbolOfAliasDeclarationIfTypeOnly(specifier, exportStarDeclaration);
            return resolved;
        }

        return null;
    }

    /// <summary>Go: <c>func (c *Checker) isOnlyImportableAsDefault(usage *ast.Node, resolvedModule *ast.Symbol, importAttributesType *Type) bool</c>。</summary>
    internal bool IsOnlyImportableAsDefault(Node? usage, Symbol? resolvedModule, Type? importAttributesType)
    {
        // In Node.js, JSON modules don't get named exports
        if (ModuleKind.Node16 <= _moduleKind && _moduleKind <= ModuleKind.NodeNext)
        {
            var usageMode = GetEmitSyntaxForModuleSpecifierExpression(usage!);
            if (usageMode == ModuleKind.ESNext)
            {
                if (resolvedModule is null)
                {
                    resolvedModule = ResolveExternalModuleNameWorker(usage!, usage!, true /*ignoreErrors*/, importAttributesType);
                }

                SourceFile? targetFile = null;
                if (resolvedModule is not null)
                {
                    targetFile = GetSourceFileOfModule(resolvedModule);
                }

                return targetFile is not null
                    && (IsJsonSourceFile(targetFile) || GetDeclarationFileExtension(targetFile.FileName()) == ".d.json.ts");
            }
        }

        return false;
    }

    /// <summary>Go: <c>func (c *Checker) canHaveSyntheticDefault(file *ast.Node, moduleSymbol *ast.Symbol, dontResolveAlias bool, usage *ast.Node) bool</c>。</summary>
    internal bool CanHaveSyntheticDefault(Node? file, Symbol moduleSymbol, bool dontResolveAlias, Node usage)
    {
        var usageMode = ModuleKind.None;
        if (file is not null)
        {
            usageMode = GetEmitSyntaxForModuleSpecifierExpression(usage);
        }

        if (file is not null && usageMode != ModuleKind.None)
        {
            var targetMode = _program.GetImpliedNodeFormatForEmit(file.AsSourceFile()!);
            if (usageMode == ModuleKind.ESNext && targetMode == ModuleKind.CommonJS && ModuleKind.Node16 <= _moduleKind && _moduleKind <= ModuleKind.NodeNext)
            {
                // In Node.js, CommonJS modules always have a synthetic default when imported into ESM
                return true;
            }

            if (usageMode == ModuleKind.ESNext && targetMode == ModuleKind.ESNext)
            {
                // No matter what the `module` setting is, if we're confident that both files
                // are ESM, there cannot be a synthetic default.
                return false;
            }

            // For other files (not node16/nodenext with impliedNodeFormat), check if we can determine
            // the module format from project references
            if (targetMode == ModuleKind.None && file.AsSourceFile()!.IsDeclarationFile)
            {
                // Try to get the project reference - try both source file mapping and output file mapping
                // since declaration files can be mapped either way depending on how they're resolved
                if (_program.GetRedirectForResolution(file.AsSourceFile()!) is not null
                    || _program.GetProjectReferenceFromOutputDts(file.AsSourceFile()!.Path()) is not null)
                {
                    // This is a declaration file from a project reference, so we can determine
                    // its module format from the referenced project's options
                    var targetModuleKind = _program.GetEmitModuleFormatOfFile(file.AsSourceFile()!);
                    if (usageMode == ModuleKind.ESNext && ModuleKind.ES2015 <= targetModuleKind && targetModuleKind <= ModuleKind.ESNext)
                    {
                        return false;
                    }
                }
            }
        }

        // Declaration files (and ambient modules)
        if (file is null || file.AsSourceFile()!.IsDeclarationFile)
        {
            // Definitely cannot have a synthetic default if they have a syntactic default member specified
            var defaultExportSymbol = ResolveExportByName(moduleSymbol, InternalSymbolNameDefault /*sourceNode*/, null /*dontResolveAlias*/, true); // Dont resolve alias because we want the immediately exported symbol's declaration
            if (defaultExportSymbol is not null && Some(defaultExportSymbol.Declarations, IsSyntacticDefault))
            {
                return false;
            }

            // It _might_ still be incorrect to assume there is no __esModule marker on the import at runtime, even if there is no `default` member
            // So we check a bit more,
            if (ResolveExportByName(moduleSymbol, "__esModule", null /*sourceNode*/, dontResolveAlias) is not null)
            {
                // If there is an `__esModule` specified in the declaration (meaning someone explicitly added it or wrote it in their code),
                // it definitely is a module and does not have a synthetic default
                return false;
            }

            // There are _many_ declaration files not written with esmodules in mind that still get compiled into a format with __esModule set
            // Meaning there may be no default at runtime - however to be on the permissive side, we allow access to a synthetic default member
            // as there is no marker to indicate if the accompanying JS has `__esModule` or not, or is even native esm
            return true;
        }

        // TypeScript files never have a synthetic default (as they are always emitted with an __esModule marker) _unless_ they contain an export= statement
        if (!IsInJSFile(file))
        {
            return HasExportAssignmentSymbol(moduleSymbol);
        }

        // JS files have a synthetic default if they do not contain ES2015+ module syntax (export = is not valid in js) _and_ do not have an __esModule marker
        return (file.AsSourceFile()!.ExternalModuleIndicator is null || file.AsSourceFile()!.ExternalModuleIndicator == file)
            && ResolveExportByName(moduleSymbol, "__esModule", null /*sourceNode*/, dontResolveAlias) is null;
    }

    /// <summary>Go: <c>func (c *Checker) getEmitSyntaxForModuleSpecifierExpression(usage *ast.Node) core.ResolutionMode</c>。</summary>
    internal ModuleKind GetEmitSyntaxForModuleSpecifierExpression(Node usage)
    {
        if (IsStringLiteralLike(usage))
        {
            return _program.GetEmitSyntaxForUsageLocation(GetSourceFileOfNode(usage)!, usage);
        }

        return ModuleKind.None;
    }

    /// <summary>Go: <c>func (c *Checker) errorNoModuleMemberSymbol(moduleSymbol, targetSymbol *ast.Symbol, node, name *ast.Node)</c>。</summary>
    internal void ErrorNoModuleMemberSymbol(Symbol moduleSymbol, Symbol targetSymbol, Node node, Node name)
    {
        if (_compilerOptions.NoCheck.IsTrue())
        {
            return;
        }

        var moduleName = GetFullyQualifiedNameWorker(moduleSymbol, node);
        var declarationName = DeclarationNameToString(name);
        Symbol? suggestion = null;
        if (IsIdentifier(name))
        {
            suggestion = GetSuggestedSymbolForNonexistentModule(name, targetSymbol);
        }

        if (suggestion is not null)
        {
            var suggestionName = SymbolToStringWorker(suggestion);
            var diagnostic = Error(name, X_0_has_no_exported_member_named_1_Did_you_mean_2, moduleName, declarationName, suggestionName);
            if (suggestion.ValueDeclaration is not null)
            {
                diagnostic.AddRelatedInfo(CreateDiagnosticForNode(suggestion.ValueDeclaration, X_0_is_declared_here, suggestionName));
            }
        }
        else
        {
            if (moduleSymbol.Exports?.GetValueOrDefault(InternalSymbolNameDefault) is not null)
            {
                Error(name, Module_0_has_no_exported_member_1_Did_you_mean_to_use_import_1_from_0_instead, moduleName, declarationName);
            }
            else
            {
                ReportNonExportedMember(name, declarationName, moduleSymbol, moduleName);
            }
        }
    }

    /// <summary>Go: <c>func (c *Checker) reportNonExportedMember(name *ast.Node, declarationName string, moduleSymbol *ast.Symbol, moduleName string)</c>。</summary>
    internal void ReportNonExportedMember(Node name, string declarationName, Symbol moduleSymbol, string moduleName)
    {
        Symbol? localSymbol = null;
        var locals = moduleSymbol.ValueDeclaration!.Locals();
        if (locals is not null)
        {
            localSymbol = locals.GetValueOrDefault(name.Text());
        }

        var exports = moduleSymbol.Exports;
        if (localSymbol is not null)
        {
            var exportedEqualsSymbol = exports?.GetValueOrDefault(InternalSymbolNameExportEquals);
            if (exportedEqualsSymbol is not null)
            {
                if (GetSymbolIfSameReference(exportedEqualsSymbol, localSymbol) is not null)
                {
                    ReportInvalidImportEqualsExportMember(name, declarationName, moduleName);
                }
                else
                {
                    Error(name, Module_0_has_no_exported_member_1, moduleName, declarationName);
                }
            }
            else
            {
                var exportedSymbol = exports is not null
                    ? FindInMap(exports, symbol => GetSymbolIfSameReference(symbol, localSymbol) is not null)
                    : null;
                Diagnostic diagnostic;
                if (exportedSymbol is not null)
                {
                    diagnostic = Error(name, Module_0_declares_1_locally_but_it_is_exported_as_2, moduleName, declarationName, SymbolToStringWorker(exportedSymbol));
                }
                else
                {
                    diagnostic = Error(name, Module_0_declares_1_locally_but_it_is_not_exported, moduleName, declarationName);
                }

                var declarations = localSymbol.Declarations!;
                for (int i = 0; i < declarations.Length; i++)
                {
                    diagnostic.AddRelatedInfo(CreateDiagnosticForNode(declarations[i], IfElse(i == 0, X_0_is_declared_here, X_and_here), declarationName));
                }
            }
        }
        else
        {
            Error(name, Module_0_has_no_exported_member_1, moduleName, declarationName);
        }
    }

    /// <summary>Go: <c>func (c *Checker) reportInvalidImportEqualsExportMember(name *ast.Node, declarationName string, moduleName string)</c>。</summary>
    internal void ReportInvalidImportEqualsExportMember(Node name, string declarationName, string moduleName)
    {
        if (_moduleKind >= ModuleKind.ES2015)
        {
            Error(name, X_0_can_only_be_imported_by_using_a_default_import, declarationName);
        }
        else if (IsInJSFile(name))
        {
            Error(name, X_0_can_only_be_imported_by_using_a_require_call_or_by_using_a_default_import, declarationName);
        }
        else
        {
            Error(name, X_0_can_only_be_imported_by_using_import_1_require_2_or_a_default_import, declarationName, declarationName, moduleName);
        }
    }

    /// <summary>Go: <c>func (c *Checker) getTargetOfExportSpecifier(node *ast.Node, meaning ast.SymbolFlags, dontResolveAlias bool) *ast.Symbol</c>。</summary>
    internal Symbol? GetTargetOfExportSpecifier(Node node, SymbolFlags meaning, bool dontResolveAlias)
    {
        var name = node.PropertyNameOrName();
        if (ModuleExportNameIsDefault(name))
        {
            var specifier = GetModuleSpecifierForImportOrExport(node);
            if (specifier is not null)
            {
                var moduleSymbol = ResolveExternalModuleNameWorker(node, specifier, false /*ignoreErrors*/, GetTypeFromImportAttributes(GetImportAttributes(node.Parent!.Parent!)));
                if (moduleSymbol is not null)
                {
                    return GetTargetOfModuleDefault(moduleSymbol, node, dontResolveAlias);
                }
            }
        }

        var exportDeclaration = node.Parent!.Parent!;
        Symbol? resolved;
        if (exportDeclaration.ModuleSpecifier() is not null)
        {
            resolved = GetExternalModuleMember(exportDeclaration, node, dontResolveAlias);
        }
        else if (IsStringLiteral(name!))
        {
            resolved = null;
        }
        else
        {
            resolved = ResolveEntityName(name!, meaning, false /*ignoreErrors*/, dontResolveAlias, null /*location*/);
        }

        MarkSymbolOfAliasDeclarationIfTypeOnly(node, null);
        return resolved;
    }

    /// <summary>Go: <c>func (c *Checker) getTargetOfExportAssignment(node *ast.Node) *ast.Symbol</c>。</summary>
    internal Symbol? GetTargetOfExportAssignment(Node node)
    {
        // An `export =` / `export default` inside a namespace/module block is a grammar error;
        // checkExportAssignment reports it and returns without resolving the expression. Mirror that
        // bail-out here (using the same container computation) so that alias resolution triggered by
        // the emit resolver does not resolve — and report "Cannot find name" diagnostics on — the
        // expression, which would produce diagnostics inconsistent with checking.
        if (IsContainedByNamespace(node))
        {
            return null;
        }

        var resolved = GetTargetOfAliasLikeExpression(node.Expression()!);
        MarkSymbolOfAliasDeclarationIfTypeOnly(node, null);
        return resolved;
    }

    /// <summary>Go: <c>func (c *Checker) getTargetOfBinaryExpression(node *ast.Node) *ast.Symbol</c>。</summary>
    internal Symbol? GetTargetOfBinaryExpression(Node node)
    {
        var resolved = GetTargetOfAliasLikeExpression(node.AsBinaryExpression()!.Right!);
        MarkSymbolOfAliasDeclarationIfTypeOnly(node, null);
        return resolved;
    }

    /// <summary>Go: <c>func (c *Checker) getTargetOfAliasLikeExpression(expression *ast.Node) *ast.Symbol</c>。</summary>
    internal Symbol? GetTargetOfAliasLikeExpression(Node expression)
    {
        if (IsClassExpression(expression))
        {
            return CheckExpressionCached(expression)._symbol;
        }

        if (!IsEntityName(expression) && !IsEntityNameExpression(expression))
        {
            return null;
        }

        var aliasLike = ResolveEntityName(expression, SymbolFlags.Value | SymbolFlags.Type | SymbolFlags.Namespace, true /*ignoreErrors*/, true /*dontResolveAlias*/, null /*location*/);
        if (aliasLike is not null)
        {
            return aliasLike;
        }

        CheckExpressionCached(expression);
        return GetResolvedSymbolOrNil(expression);
    }

    /// <summary>Go: <c>func (c *Checker) getTargetOfNamespaceExportDeclaration(node *ast.Node) *ast.Symbol</c>。</summary>
    internal Symbol? GetTargetOfNamespaceExportDeclaration(Node node)
    {
        if (CanHaveSymbol(node.Parent!))
        {
            var resolved = ResolveExternalModuleSymbolWorker(node.Parent!.Symbol, true /*dontResolveAlias*/);
            MarkSymbolOfAliasDeclarationIfTypeOnly(node, null);
            return resolved;
        }

        return null;
    }

    /// <summary>Go: <c>func (c *Checker) getTargetOfAccessExpression(node *ast.Node) *ast.Symbol</c>。</summary>
    internal Symbol? GetTargetOfAccessExpression(Node node)
    {
        if (IsBinaryExpression(node.Parent!))
        {
            var expr = node.Parent!.AsBinaryExpression()!;
            if (expr.Left == node && expr.OperatorToken!.Kind == Kind.EqualsToken)
            {
                return GetTargetOfAliasLikeExpression(expr.Right!);
            }
        }

        return null;
    }
}
