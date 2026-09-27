// ported from: internal/checker/checker.go:5608-5935（checkImportEqualsDeclaration、checkExportDeclaration、
//   checkExternalModuleNameInGlobalScope、checkExportSpecifier、isContainedByNamespace、checkExportAssignment、
//   getVerbatimModuleSyntaxErrorMessage、checkExternalModuleExports、hasExportedMembersOfKind、hasShadowedNamespace、
//   isNotOverload、checkMissingDeclaration、checkVariableStatement）
//
// 与 Go 的偏离（逐条；其余与 Go 逐行对应）：
// 1) 未导出同名方法加 `Worker`（artifacts/checker-method-collisions.md）：`getSymbolFlags` → `GetSymbolFlagsWorker`、
//    `getTypeOnlyAliasDeclaration` → `GetTypeOnlyAliasDeclarationWorker`、`getExportsOfModule` → `GetExportsOfModuleWorker`、
//    `resolveAlias` → `ResolveAliasWorker`；`getSymbolFlagsEx`/`getTypeOnlyAliasDeclarationEx` 不在清单里 → 原名 PascalCase。
// 2) `moduleSymbol.Exports[...]`（Go 的 map 读缺失键返回 nil）→ `?.GetValueOrDefault(...)`。
// 3) `core.ResolutionMode*` → `ModuleKind.*`（Core/CompilerOptions.cs:526-528）。
// 4) 包级函数（`isContainedByNamespace`/`getVerbatimModuleSyntaxErrorMessage`/`isNotOverload`）→ 同一个
//    `partial class Checker` 的 `internal static` 成员（Parser/Binder 先例，docs/checker-plan §5.2/§5.5）。
namespace TypeScript.Checker;

using TypeScript.Ast;
using TypeScript.Core;
using TypeScript.Diagnostics;
using static TypeScript.Ast.Ast;
using static TypeScript.Core.Core;
using static TypeScript.Scanner.Scanner;
using static TypeScript.TSPath.TSPath;

public sealed partial class Checker
{
    internal void CheckImportEqualsDeclaration(Node node)
    {
        var diagnostic = IfElse(IsInJSFile(node),
            Diagnostics.An_import_declaration_can_only_be_used_at_the_top_level_of_a_module,
            Diagnostics.An_import_declaration_can_only_be_used_at_the_top_level_of_a_namespace_or_module);
        if (CheckGrammarModuleElementContext(node, diagnostic))
        {
            CheckExternalModuleNameInGlobalScope(node);
            return; // If we hit an import declaration in an illegal context, just bail out to avoid cascading errors.
        }

        CheckGrammarModifiers(node);
        if (ShouldCheckErasableSyntax(node) && (node.Flags & NodeFlags.Ambient) == 0)
        {
            Error(node, Diagnostics.This_syntax_is_not_allowed_when_erasableSyntaxOnly_is_enabled);
        }

        if (IsInternalModuleImportEqualsDeclaration(node) || CheckExternalImportOrExportDeclaration(node))
        {
            CheckImportBinding(node);
            MarkLinkedReferences(node, ReferenceHint.ExportImportEquals, null, null);
            var moduleReference = node.AsImportEqualsDeclaration()!.ModuleReference!;
            if (!IsExternalModuleReference(moduleReference))
            {
                var target = ResolveAliasWorker(GetSymbolOfDeclaration(node)!);
                if (target != _unknownSymbol)
                {
                    var targetFlags = GetSymbolFlagsWorker(target!);
                    if ((targetFlags & SymbolFlags.Value) != 0)
                    {
                        // Target is a value symbol, check that it is not hidden by a local declaration with the same name
                        var moduleName = GetFirstIdentifier(moduleReference)!;
                        if ((ResolveEntityName(moduleName, SymbolFlags.Value | SymbolFlags.Namespace, false, false, null)!.Flags & SymbolFlags.Namespace) == 0)
                        {
                            Error(moduleName, Diagnostics.Module_0_is_hidden_by_a_local_declaration_with_the_same_name, DeclarationNameToString(moduleName));
                        }
                    }

                    if ((targetFlags & SymbolFlags.Type) != 0)
                    {
                        CheckTypeNameIsReserved(node.Name()!, Diagnostics.Import_name_cannot_be_0);
                    }
                }

                if (node.IsTypeOnly())
                {
                    GrammarErrorOnNode(node, Diagnostics.An_import_alias_cannot_use_import_type);
                }
            }
            else
            {
                if (ModuleKind.ES2015 <= _moduleKind && _moduleKind <= ModuleKind.ESNext && !node.IsTypeOnly() && (node.Flags & NodeFlags.Ambient) == 0)
                {
                    // Import equals declaration cannot be emitted as ESM
                    GrammarErrorOnNode(node, Diagnostics.Import_assignment_cannot_be_used_when_targeting_ECMAScript_modules_Consider_using_import_Asterisk_as_ns_from_mod_import_a_from_mod_import_d_from_mod_or_another_module_format_instead);
                }
            }
        }
    }

    internal void CheckExportDeclaration(Node node)
    {
        var diagnostic = IfElse(IsInJSFile(node),
            Diagnostics.An_export_declaration_can_only_be_used_at_the_top_level_of_a_module,
            Diagnostics.An_export_declaration_can_only_be_used_at_the_top_level_of_a_namespace_or_module);
        if (CheckGrammarModuleElementContext(node, diagnostic))
        {
            CheckExternalModuleNameInGlobalScope(node);
            return; // If we hit an export in an illegal context, just bail out to avoid cascading errors.
        }

        var exportDecl = node.AsExportDeclaration()!;
        if (!CheckGrammarModifiers(node) && exportDecl.Modifiers() != null)
        {
            GrammarErrorOnFirstToken(node, Diagnostics.An_export_declaration_cannot_have_modifiers);
        }

        CheckGrammarExportDeclaration(exportDecl);
        if (exportDecl.ModuleSpecifier == null || CheckExternalImportOrExportDeclaration(node))
        {
            if (exportDecl.ExportClause != null && !IsNamespaceExport(exportDecl.ExportClause))
            {
                // export { x, y }
                // export { x, y } from "foo"
                foreach (var binding in exportDecl.ExportClause.Elements() ?? [])
                {
                    CheckExportSpecifier(binding);
                }

                var inAmbientExternalModule = IsModuleBlock(node.Parent!) && IsAmbientModule(node.Parent!.Parent!);
                var inAmbientNamespaceDeclaration = !inAmbientExternalModule && IsModuleBlock(node.Parent!) && exportDecl.ModuleSpecifier == null && (node.Flags & NodeFlags.Ambient) != 0;
                if (!IsSourceFile(node.Parent!) && !inAmbientExternalModule && !inAmbientNamespaceDeclaration)
                {
                    Error(node, Diagnostics.Export_declarations_are_not_permitted_in_a_namespace);
                }
            }
            else
            {
                // export * from "foo"
                // export * as ns from "foo";
                var moduleSymbol = ResolveExternalModuleNameWorker(node, exportDecl.ModuleSpecifier!, false, GetTypeFromImportAttributes(GetImportAttributes(node)));
                if (moduleSymbol != null && HasExportAssignmentSymbol(moduleSymbol))
                {
                    Error(exportDecl.ModuleSpecifier, Diagnostics.Module_0_uses_export_and_cannot_be_used_with_export_Asterisk, SymbolToStringWorker(moduleSymbol));
                }
                else if (exportDecl.ExportClause != null)
                {
                    CheckAliasSymbol(exportDecl.ExportClause);
                    CheckModuleExportName(exportDecl.ExportClause.Name(), true /*allowStringLiteral*/);
                }

                if (_program.GetEmitModuleFormatOfFile(GetSourceFileOfNode(node)!) == ModuleKind.CommonJS)
                {
                    if (node.AsExportDeclaration()!.ExportClause != null)
                    {
                        // export * as ns from "foo";
                        CheckExternalEmitHelpers(node, ExternalEmitHelpers.ImportStar);
                    }
                    else
                    {
                        // export * from "foo"
                        CheckExternalEmitHelpers(node, ExternalEmitHelpers.ExportStar);
                    }
                }
            }
        }

        CheckImportAttributes(node);
    }

    internal void CheckExternalModuleNameInGlobalScope(Node node)
    {
        if (GetEnclosingContainer(node)!.Kind != Kind.SourceFile || (IsImportDeclarationOrJSImportDeclaration(node) && node.ImportClause() == null))
        {
            return;
        }

        var moduleName = GetExternalModuleName(node);
        if (moduleName != null)
        {
            Node? attributes = null;
            if (HasImportAttributes(node))
            {
                attributes = GetImportAttributes(node);
            }

            var importAttributesType = GetTypeFromImportAttributes(attributes);
            ResolveExternalModuleNameWorker(node, moduleName, false, importAttributesType);
        }
    }

    internal void CheckExportSpecifier(Node node)
    {
        CheckAliasSymbol(node);
        var hasModuleSpecifier = node.Parent!.Parent!.ModuleSpecifier() != null;
        CheckModuleExportName(node.PropertyName(), hasModuleSpecifier);
        CheckModuleExportName(node.Name(), true /*allowStringLiteral*/);

        if (!hasModuleSpecifier)
        {
            var exportedName = node.PropertyNameOrName()!;
            if (exportedName.Kind == Kind.StringLiteral)
            {
                return; // Skip for invalid syntax like this: export { "x" }
            }

            // find immediate value referenced by exported name (SymbolFlags.Alias is set so we don't chase down aliases)
            var symbol = _resolveName(exportedName, exportedName.Text(), SymbolFlags.Value | SymbolFlags.Type | SymbolFlags.Namespace | SymbolFlags.Alias, null /*nameNotFoundMessage*/, true /*isUse*/, false);
            if (symbol != null && (symbol == _undefinedSymbol || symbol == _globalThisSymbol || (symbol.Declarations != null && IsGlobalSourceFile(GetDeclarationContainer(symbol.Declarations[0])))))
            {
                Error(exportedName, Diagnostics.Cannot_export_0_Only_local_declarations_can_be_exported_from_a_module, exportedName.Text());
            }
            else
            {
                MarkLinkedReferences(node, ReferenceHint.ExportSpecifier, null /*propSymbol*/, null /*parentType*/);
            }
        }
        else if (_program.GetEmitModuleFormatOfFile(GetSourceFileOfNode(node)!) == ModuleKind.CommonJS &&
            ModuleExportNameIsDefault(node.PropertyNameOrName()))
        {
            CheckExternalEmitHelpers(node, ExternalEmitHelpers.ImportDefault);
        }
    }

    internal static bool IsContainedByNamespace(Node node)
    {
        var container = node.Parent!;
        if (!IsSourceFile(container))
        {
            container = container.Parent!;
        }

        return IsModuleDeclaration(container) && !IsAmbientModule(container);
    }

    internal void CheckExportAssignment(Node node)
    {
        var isExportEquals = node.AsExportAssignment()!.IsExportEquals;

        // Always check the exported expression so its identifiers are resolved even when the
        // export assignment is misplaced (grammar error), keeping diagnostics stable
        // regardless of traversal order.
        var exprType = CheckExpressionCached(node.Expression()!);
        var illegalContextMessage = IfElse(isExportEquals,
            Diagnostics.An_export_assignment_must_be_at_the_top_level_of_a_file_or_module_declaration,
            Diagnostics.A_default_export_must_be_at_the_top_level_of_a_file_or_module_declaration);
        if (CheckGrammarModuleElementContext(node, illegalContextMessage))
        {
            return; // If we hit an export assignment in an illegal context, just bail out to avoid cascading errors.
        }

        if (ShouldCheckErasableSyntax(node) && node.AsExportAssignment()!.IsExportEquals && (node.Flags & NodeFlags.Ambient) == 0)
        {
            Error(node, Diagnostics.This_syntax_is_not_allowed_when_erasableSyntaxOnly_is_enabled);
        }

        if (IsContainedByNamespace(node))
        {
            // TODO(danielr): should these be grammar errors?
            if (isExportEquals)
            {
                Error(node, Diagnostics.An_export_assignment_cannot_be_used_in_a_namespace);
            }
            else
            {
                Error(node, Diagnostics.A_default_export_can_only_be_used_in_an_ECMAScript_style_module);
            }

            return;
        }

        if (!CheckGrammarModifiers(node) && IsExportAssignment(node) && node.AsExportAssignment()!.Modifiers() != null)
        {
            GrammarErrorOnFirstToken(node, Diagnostics.An_export_assignment_cannot_have_modifiers);
        }

        var isIllegalExportDefaultInCJS = !isExportEquals && (node.Flags & NodeFlags.Ambient) == 0 && _compilerOptions.VerbatimModuleSyntax.IsTrue() && _program.GetEmitModuleFormatOfFile(GetSourceFileOfNode(node)!) == ModuleKind.CommonJS;
        if (IsIdentifier(node.Expression()!))
        {
            var id = node.Expression()!;
            var sym = GetExportSymbolOfValueSymbolIfExported(ResolveEntityName(id, SymbolFlags.All, true /*ignoreErrors*/, true /*dontResolveAlias*/, node));
            if (sym != null)
            {
                MarkLinkedReferences(node, ReferenceHint.ExportAssignment, null, null);
                var typeOnlyDeclaration = GetTypeOnlyAliasDeclarationEx(sym, SymbolFlags.Value);

                // If not a value, we're interpreting the identifier as a type export, along the lines of (`export { Id as default }`)
                if ((GetSymbolFlagsWorker(sym) & SymbolFlags.Value) != 0)
                {
                    // However if it is a value, we need to check it's being used correctly
                    if (!isIllegalExportDefaultInCJS && (node.Flags & NodeFlags.Ambient) == 0 && _compilerOptions.VerbatimModuleSyntax.IsTrue() && typeOnlyDeclaration != null)
                    {
                        var message = IfElse(isExportEquals,
                            Diagnostics.An_export_declaration_must_reference_a_real_value_when_verbatimModuleSyntax_is_enabled_but_0_resolves_to_a_type_only_declaration,
                            Diagnostics.An_export_default_must_reference_a_real_value_when_verbatimModuleSyntax_is_enabled_but_0_resolves_to_a_type_only_declaration);
                        Error(id, message, id.Text());
                    }
                }
                else if (!isIllegalExportDefaultInCJS && (node.Flags & NodeFlags.Ambient) == 0 && _compilerOptions.VerbatimModuleSyntax.IsTrue())
                {
                    var message = IfElse(isExportEquals,
                        Diagnostics.An_export_declaration_must_reference_a_value_when_verbatimModuleSyntax_is_enabled_but_0_only_refers_to_a_type,
                        Diagnostics.An_export_default_must_reference_a_value_when_verbatimModuleSyntax_is_enabled_but_0_only_refers_to_a_type);
                    Error(id, message, id.Text());
                }

                if (!isIllegalExportDefaultInCJS && (node.Flags & NodeFlags.Ambient) == 0 && _compilerOptions.GetIsolatedModules() && (sym.Flags & SymbolFlags.Value) == 0)
                {
                    var nonLocalMeanings = GetSymbolFlagsEx(sym, false /*excludeTypeOnlyMeanings*/, true /*excludeLocalMeanings*/);
                    if ((sym.Flags & SymbolFlags.Alias) != 0 && (nonLocalMeanings & SymbolFlags.Type) != 0 && (nonLocalMeanings & SymbolFlags.Value) == 0 && (typeOnlyDeclaration == null || GetSourceFileOfNode(typeOnlyDeclaration) != GetSourceFileOfNode(node)))
                    {
                        // import { SomeType } from "./someModule";
                        // export default SomeType; OR
                        // export = SomeType;
                        var message = IfElse(isExportEquals,
                            Diagnostics.X_0_resolves_to_a_type_and_must_be_marked_type_only_in_this_file_before_re_exporting_when_1_is_enabled_Consider_using_import_type_where_0_is_imported,
                            Diagnostics.X_0_resolves_to_a_type_and_must_be_marked_type_only_in_this_file_before_re_exporting_when_1_is_enabled_Consider_using_export_type_0_as_default);
                        Error(id, message, id.Text(), GetIsolatedModulesLikeFlagName());
                    }
                    else if (typeOnlyDeclaration != null && GetSourceFileOfNode(typeOnlyDeclaration) != GetSourceFileOfNode(node))
                    {
                        // import { SomeTypeOnlyValue } from "./someModule";
                        // export default SomeTypeOnlyValue; OR
                        // export = SomeTypeOnlyValue;
                        var message = IfElse(isExportEquals,
                            Diagnostics.X_0_resolves_to_a_type_only_declaration_and_must_be_marked_type_only_in_this_file_before_re_exporting_when_1_is_enabled_Consider_using_import_type_where_0_is_imported,
                            Diagnostics.X_0_resolves_to_a_type_only_declaration_and_must_be_marked_type_only_in_this_file_before_re_exporting_when_1_is_enabled_Consider_using_export_type_0_as_default);
                        AddTypeOnlyDeclarationRelatedInfo(Error(id, message, id.Text(), GetIsolatedModulesLikeFlagName()), typeOnlyDeclaration, id.Text());
                    }
                }
            }
        }

        if (isIllegalExportDefaultInCJS)
        {
            Error(node, GetVerbatimModuleSyntaxErrorMessage(node));
        }

        var container = node.Parent!;
        if (!IsSourceFile(container))
        {
            container = container.Parent!;
        }

        CheckExternalModuleExports(container);
        var typeNode = node.Type();
        if (typeNode != null && node.Kind == Kind.ExportAssignment)
        {
            var t = GetTypeFromTypeNodeWorker(typeNode);
            CheckTypeAssignableToAndOptionallyElaborate(exprType, t, node.Expression(), node.Expression(), null /*headMessage*/, null);
        }

        if ((node.Flags & NodeFlags.Ambient) != 0 && !IsEntityNameExpression(node.Expression()))
        {
            GrammarErrorOnNode(node.Expression()!, Diagnostics.The_expression_of_an_export_assignment_must_be_an_identifier_or_qualified_name_in_an_ambient_context);
        }

        if (isExportEquals)
        {
            // Forbid export= in esm implementation files, and esm mode declaration files
            if (_moduleKind >= ModuleKind.ES2015 && _moduleKind != ModuleKind.Preserve && (((node.Flags & NodeFlags.Ambient) != 0 && _program.GetImpliedNodeFormatForEmit(GetSourceFileOfNode(node)!) == ModuleKind.ESNext) || ((node.Flags & NodeFlags.Ambient) == 0 && _program.GetImpliedNodeFormatForEmit(GetSourceFileOfNode(node)!) != ModuleKind.CommonJS)))
            {
                // export assignment is not supported in es6 modules
                GrammarErrorOnNode(node, Diagnostics.Export_assignment_cannot_be_used_when_targeting_ECMAScript_modules_Consider_using_export_default_or_another_module_format_instead);
            }
            else if (_moduleKind == ModuleKind.System && (node.Flags & NodeFlags.Ambient) == 0)
            {
                // system modules does not support export assignment
                GrammarErrorOnNode(node, Diagnostics.Export_assignment_is_not_supported_when_module_flag_is_system);
            }
        }
    }

    internal static Message GetVerbatimModuleSyntaxErrorMessage(Node node)
    {
        var sourceFile = GetSourceFileOfNode(node)!;
        var fileName = sourceFile.FileName();

        // Check if the file is .cts or .cjs (CommonJS-specific extensions)
        if (FileExtensionIsOneOf(fileName, [ExtensionCts, ExtensionCjs]))
        {
            return Diagnostics.ECMAScript_imports_and_exports_cannot_be_written_in_a_CommonJS_file_under_verbatimModuleSyntax;
        }

        // For .ts, .tsx, .js, etc.
        return Diagnostics.ECMAScript_imports_and_exports_cannot_be_written_in_a_CommonJS_file_under_verbatimModuleSyntax_Adjust_the_type_field_in_the_nearest_package_json_to_make_this_file_an_ECMAScript_module_or_adjust_your_verbatimModuleSyntax_module_and_moduleResolution_settings_in_TypeScript;
    }

    internal void CheckExternalModuleExports(Node node)
    {
        var moduleSymbol = GetSymbolOfDeclaration(node)!;
        var links = _moduleSymbolLinks.Get(moduleSymbol);
        if (!links._exportsChecked)
        {
            var exportEqualsSymbol = moduleSymbol.Exports?.GetValueOrDefault(InternalSymbolNameExportEquals);

            // An export assignment is in error if (a) the module exports value members or (b) if the module exports type or
            // namespace members and the exported entity also exports type or namespace members.
            if (exportEqualsSymbol != null && (HasExportedMembersOfKind(moduleSymbol, SymbolFlags.Value) || HasShadowedNamespace(exportEqualsSymbol)))
            {
                var declaration = OrElse(GetDeclarationOfAliasSymbol(exportEqualsSymbol), exportEqualsSymbol.ValueDeclaration);
                if (declaration != null && !IsTopLevelInExternalModuleAugmentation(declaration))
                {
                    Error(declaration, Diagnostics.An_export_assignment_cannot_be_used_in_a_module_with_other_exported_elements);
                }
            }

            // Checks for export * conflicts
            foreach (var (id, symbol) in GetExportsOfModuleWorker(moduleSymbol))
            {
                if (id == InternalSymbolNameExportStar)
                {
                    continue;
                }

                // ECMA262: 15.2.1.1 It is a Syntax Error if the ExportedNames of ModuleItemList contains any duplicate entries.
                // (TS Exceptions: namespaces, function overloads, enums, and interfaces)
                if ((symbol.Flags & (SymbolFlags.Namespace | SymbolFlags.Enum)) != 0)
                {
                    continue;
                }

                var exportedDeclarationsCount = CountWhere(symbol.Declarations, d => IsNotOverload(d) && !IsAccessor(d) && !IsInterfaceDeclaration(d));
                if ((symbol.Flags & SymbolFlags.TypeAlias) != 0 && exportedDeclarationsCount <= 2)
                {
                    // it is legal to merge type alias with other values
                    // so count should be either 1 (just type alias) or 2 (type alias + merged value)
                    continue;
                }

                if (exportedDeclarationsCount > 1 && !Every(symbol.Declarations, decl => GetAssignmentDeclarationKind(decl) == JSDeclarationKind.ExportsProperty))
                {
                    foreach (var declaration in symbol.Declarations ?? [])
                    {
                        if (IsNotOverload(declaration))
                        {
                            Error(declaration, Diagnostics.Cannot_redeclare_exported_variable_0, id);
                        }
                    }
                }
            }

            links._exportsChecked = true;
        }
    }

    internal bool HasExportedMembersOfKind(Symbol moduleSymbol, SymbolFlags kind)
    {
        // Go: `for _, symbol := range moduleSymbol.Exports`（nil map → 0 次迭代）
        if (moduleSymbol.Exports == null)
        {
            return false;
        }

        foreach (var symbol in moduleSymbol.Exports.Values)
        {
            if (symbol.Name != InternalSymbolNameExportEquals && (GetSymbolFlagsWorker(symbol) & kind) != 0)
            {
                return true;
            }
        }

        return false;
    }

    internal bool HasShadowedNamespace(Symbol symbol)
    {
        if ((symbol.Flags & SymbolFlags.NamespaceModule) != 0 && (symbol.Flags & SymbolFlags.Alias) != 0)
        {
            var target = ResolveAliasWorker(symbol);
            if (target != null && (target.Flags & SymbolFlags.Namespace) != 0 && HasExportedMembersOfKind(target, SymbolFlags.Type | SymbolFlags.Namespace))
            {
                return true;
            }
        }

        return false;
    }

    internal static bool IsNotOverload(Node node)
    {
        return (!IsFunctionDeclaration(node) && !IsMethodDeclaration(node)) || node.Body() != null;
    }

    internal void CheckMissingDeclaration(Node node)
    {
        CheckDecorators(node);
    }

    internal void CheckVariableStatement(Node node)
    {
        var varStatement = node.AsVariableStatement()!;
        var declarationList = varStatement.DeclarationList!;
        if (!CheckGrammarModifiers(node) && !CheckGrammarVariableDeclarationList(declarationList.AsVariableDeclarationList()!))
        {
            CheckGrammarForDisallowedBlockScopedVariableStatement(varStatement);
        }

        CheckVariableDeclarationList(declarationList);
    }
}
