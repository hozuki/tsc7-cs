// ported from: internal/checker/checker.go:5221-5607（checkModuleDeclaration、checkImportAttributesType、
//   getTypeOfModuleDeclarationImportAttributes、getTypeOfModuleImportAttributes、isInstantiatedModule、
//   getFirstNonAmbientClassOrFunctionDeclaration、getIsolatedModulesLikeFlagName、checkModuleAugmentationElement、
//   checkImportDeclaration、checkExternalImportOrExportDeclaration、checkImportBinding、checkModuleExportName、
//   hasTypeJsonImportAttribute、checkImportAttributes、getTypeFromImportAttributes、checkImportAttributesExpression、
//   getImportAttributesTypeForModuleSpecifier）
//
// 与 Go 的偏离（逐条；其余与 Go 逐行对应）：
// 1) 未导出同名方法加 `Worker`（artifacts/checker-method-collisions.md）：Go 的 4 参 `resolveExternalModuleName` →
//    `ResolveExternalModuleNameWorker`；Go 自带的 6 参 `resolveExternalModuleNameWorker` → `ResolveExternalModuleNameWorkerImpl`
//    （Main 的二次冲突规则：未导出门面 `getX` → `GetXWorker`，Go 自带 `getXWorker` → `GetXWorkerImpl`）。
//    `getTypeFromImportAttributes` 不在清单里 → `GetTypeFromImportAttributes`。
// 2) `core.ResolutionMode*` 在 C# 里一律写 `ModuleKind.*`（`Core/CompilerOptions.cs:526-528`：Go 的 `type ResolutionMode = ModuleKind`
//    在 C# 没有类型别名；`ResolutionModeNone` → `ModuleKind.None`）。
// 3) Go 的 `attributes *ast.TypeLiteralNodeNode`（`TypeLiteralNodeNode = Node` 别名）→ C# 参数 `Node?`。
// 4) `map` 读缺失键（`c.moduleImportAttributesTypes[symbol]`）→ `TryGetValue`；`symbol.Exports[...]` → `?.GetValueOrDefault(...)`。
// 5) `switch` 里的 `fallthrough`（`checkModuleAugmentationElement`）→ `goto case Kind.ImportDeclaration`（C# 无 fallthrough）。
// 6) `tspath.*` → `using static TypeScript.TSPath.TSPath;` 后的裸名。
namespace TypeScript.Checker;

using TypeScript.Ast;
using TypeScript.Core;
using TypeScript.Diagnostics;
using static TypeScript.Ast.Ast;
using static TypeScript.Core.Core;
using static TypeScript.TSPath.TSPath;

public sealed partial class Checker
{
    internal void CheckModuleDeclaration(Node node)
    {
        var body = node.Body();
        if (body != null)
        {
            CheckSourceElement(body);
            if (!IsGlobalScopeAugmentation(node))
            {
                RegisterForUnusedIdentifiersCheck(node);
            }
        }

        var isGlobalAugmentation = IsGlobalScopeAugmentation(node);
        var inAmbientContext = (node.Flags & NodeFlags.Ambient) != 0;
        if (isGlobalAugmentation && !inAmbientContext)
        {
            Error(node.Name(), Diagnostics.Augmentations_for_the_global_scope_should_have_declare_modifier_unless_they_appear_in_already_ambient_context);
        }

        var attributes = node.AsModuleDeclaration()!.Attributes;
        if (attributes != null)
        {
            CheckImportAttributesType(attributes);
        }

        var isAmbientExternalModule = IsAmbientModule(node);
        var contextErrorMessage = IfElse(isAmbientExternalModule,
            Diagnostics.An_ambient_module_declaration_is_only_allowed_at_the_top_level_in_a_file,
            Diagnostics.A_namespace_declaration_is_only_allowed_at_the_top_level_of_a_namespace_or_module);
        if (CheckGrammarModuleElementContext(node, contextErrorMessage))
        {
            // If we hit a module declaration in an illegal context, just bail out to avoid cascading errors.
            return;
        }

        if (!CheckGrammarModifiers(node))
        {
            if (!inAmbientContext && IsStringLiteral(node.Name()!))
            {
                GrammarErrorOnNode(node.Name()!, Diagnostics.Only_ambient_modules_can_use_quoted_names);
            }
        }

        if (IsIdentifier(node.Name()!))
        {
            CheckCollisionsForDeclarationName(node, node.Name());
            if (node.AsModuleDeclaration()!.Keyword == Kind.ModuleKeyword)
            {
                Error(node.Name(), Diagnostics.A_namespace_declaration_should_not_be_declared_using_the_module_keyword_Please_use_the_namespace_keyword_instead);
            }
        }

        CheckExportsOnMergedDeclarations(node);
        var symbol = GetSymbolOfDeclaration(node)!;

        // The following checks only apply on a non-ambient instantiated module declaration.
        if ((symbol.Flags & SymbolFlags.ValueModule) != 0 && !inAmbientContext && IsInstantiatedModule(node, _compilerOptions.ShouldPreserveConstEnums()))
        {
            if (ShouldCheckErasableSyntax(node))
            {
                Error(node, Diagnostics.This_syntax_is_not_allowed_when_erasableSyntaxOnly_is_enabled);
            }

            if (_compilerOptions.GetIsolatedModules() && GetSourceFileOfNode(node)!.ExternalModuleIndicator == null)
            {
                // This could be loosened a little if needed. The only problem we are trying to avoid is unqualified
                // references to namespace members declared in other files. But use of namespaces is discouraged anyway,
                // so for now we will just not allow them in scripts, which is the only place they can merge cross-file.
                Error(node.Name(), Diagnostics.Namespaces_are_not_allowed_in_global_script_files_when_0_is_enabled_If_this_file_is_not_intended_to_be_a_global_script_set_moduleDetection_to_force_or_add_an_empty_export_statement, GetIsolatedModulesLikeFlagName());
            }

            if ((symbol.Declarations?.Length ?? 0) > 1)
            {
                var firstNonAmbientClassOrFunc = GetFirstNonAmbientClassOrFunctionDeclaration(symbol);
                if (firstNonAmbientClassOrFunc != null)
                {
                    if (GetSourceFileOfNode(node) != GetSourceFileOfNode(firstNonAmbientClassOrFunc))
                    {
                        Error(node.Name(), Diagnostics.A_namespace_declaration_cannot_be_in_a_different_file_from_a_class_or_function_with_which_it_is_merged);
                    }
                    else if (node.Pos() < firstNonAmbientClassOrFunc.Pos())
                    {
                        Error(node.Name(), Diagnostics.A_namespace_declaration_cannot_be_located_prior_to_a_class_or_function_with_which_it_is_merged);
                    }
                }
            }

            if (_compilerOptions.VerbatimModuleSyntax.IsTrue() && IsSourceFile(node.Parent!) && (node.ModifierFlags & ModifierFlags.Export) != 0 && _program.GetEmitModuleFormatOfFile(node.Parent!.AsSourceFile()!) == ModuleKind.CommonJS)
            {
                var exportModifier = Find(node.ModifierNodes(), m => m.Kind == Kind.ExportKeyword);
                Error(exportModifier, Diagnostics.A_top_level_export_modifier_cannot_be_used_on_value_declarations_in_a_CommonJS_module_when_verbatimModuleSyntax_is_enabled);
            }
        }

        if (isAmbientExternalModule)
        {
            if (IsExternalModuleAugmentation(node))
            {
                if (attributes != null)
                {
                    Error(attributes, Diagnostics.Import_attributes_are_not_allowed_on_a_module_augmentation);
                }

                // body of the augmentation should be checked for consistency only if augmentation was applied to its target (either global scope or module)
                // otherwise we'll be swamped in cascading errors.
                // We can detect if augmentation was applied using following rules:
                // - augmentation for a global scope is always applied
                // - augmentation for some external module is applied if symbol for augmentation is merged (it was combined with target module).
                var checkBody = isGlobalAugmentation || (GetSymbolOfDeclaration(node)!.Flags & SymbolFlags.Transient) != 0;
                if (checkBody && node.Body() != null)
                {
                    foreach (var statement in node.Body()!.Statements() ?? [])
                    {
                        CheckModuleAugmentationElement(statement);
                    }
                }
            }
            else if (IsGlobalSourceFile(node.Parent!))
            {
                if (isGlobalAugmentation)
                {
                    Error(node.Name(), Diagnostics.Augmentations_for_the_global_scope_can_only_be_directly_nested_in_external_modules_or_ambient_module_declarations);
                }
                else if (IsExternalModuleNameRelative(node.Name()!.Text()))
                {
                    Error(node.Name(), Diagnostics.Ambient_module_declaration_cannot_specify_relative_module_name);
                }
            }
            else
            {
                if (isGlobalAugmentation)
                {
                    Error(node.Name(), Diagnostics.Augmentations_for_the_global_scope_can_only_be_directly_nested_in_external_modules_or_ambient_module_declarations);
                }
                else
                {
                    // Node is not an augmentation and is not located on the script level.
                    // This means that this is declaration of ambient module that is located in other module or namespace which is prohibited.
                    Error(node.Name(), Diagnostics.Ambient_modules_cannot_be_nested_in_other_modules_or_namespaces);
                }
            }
        }
    }

    internal void CheckImportAttributesType(Node attributes)
    {
        CheckGrammarImportAttributesType(attributes.AsTypeLiteralNode()!);
        CheckSourceElement(attributes);
        var importAttributesType = _getGlobalImportAttributesTypeChecked();
        var moduleAttributesType = GetTypeOfModuleDeclarationImportAttributes(attributes);
        if (importAttributesType != _emptyObjectType)
        {
            CheckTypeAssignableTo(moduleAttributesType, importAttributesType, attributes, null);
        }
    }

    internal Type GetTypeOfModuleDeclarationImportAttributes(Node? attributes)
    {
        if (attributes == null)
        {
            return _emptyObjectType;
        }

        return GetTypeFromTypeNodeWorker(attributes);
    }

    internal Type GetTypeOfModuleImportAttributes(Symbol symbol)
    {
        if (_moduleImportAttributesTypes.TryGetValue(symbol, out var cached))
        {
            return cached;
        }

        Type result;
        var moduleDecl = Find(symbol.Declarations, IsModuleWithStringLiteralName);
        if (moduleDecl == null)
        {
            result = _emptyObjectType;
        }
        else
        {
            result = GetTypeOfModuleDeclarationImportAttributes(moduleDecl.AsModuleDeclaration()!.Attributes);
        }

        _moduleImportAttributesTypes[symbol] = result;
        return result;
    }

    internal static bool IsInstantiatedModule(Node node, bool preserveConstEnums)
    {
        var moduleState = GetModuleInstanceState(node);
        return moduleState == ModuleInstanceState.Instantiated || (preserveConstEnums && moduleState == ModuleInstanceState.ConstEnumOnly);
    }

    internal static Node? GetFirstNonAmbientClassOrFunctionDeclaration(Symbol symbol)
    {
        foreach (var declaration in symbol.Declarations ?? [])
        {
            if ((IsClassDeclaration(declaration) || (IsFunctionDeclaration(declaration) && NodeIsPresent(declaration.Body()))) && (declaration.Flags & NodeFlags.Ambient) == 0)
            {
                return declaration;
            }
        }

        return null;
    }

    internal string GetIsolatedModulesLikeFlagName()
    {
        return IfElse(_compilerOptions.VerbatimModuleSyntax.IsTrue(), "verbatimModuleSyntax", "isolatedModules");
    }

    internal void CheckModuleAugmentationElement(Node node)
    {
        switch (node.Kind)
        {
            case Kind.VariableStatement:
                // error each individual name in variable statement instead of marking the entire variable statement
                // Go: `for _, decl := range ...Declarations.Nodes`（NodeList.Nodes 是 nil 切片；`Declarations` 本身是指针，保留 `!`）
                foreach (var decl in node.AsVariableStatement()!.DeclarationList!.AsVariableDeclarationList()!.Declarations!.Nodes ?? [])
                {
                    CheckModuleAugmentationElement(decl);
                }

                break;
            case Kind.ExportAssignment:
            case Kind.ExportDeclaration:
                GrammarErrorOnFirstToken(node, Diagnostics.Exports_and_export_assignments_are_not_permitted_in_module_augmentations);
                break;
            case Kind.ImportEqualsDeclaration:
                // import a = e.x; in module augmentation is ok, but not import a = require('fs)
                if (IsInternalModuleImportEqualsDeclaration(node))
                {
                    break;
                }

                goto case Kind.ImportDeclaration;
            case Kind.ImportDeclaration:
            case Kind.JSImportDeclaration:
                GrammarErrorOnFirstToken(node, Diagnostics.Imports_are_not_permitted_in_module_augmentations_Consider_moving_them_to_the_enclosing_external_module);
                break;
            case Kind.BindingElement:
            case Kind.VariableDeclaration:
                {
                    var name = node.Name();
                    if (IsBindingPattern(name!))
                    {
                        foreach (var el in name!.Elements() ?? [])
                        {
                            // mark individual names in binding pattern
                            CheckModuleAugmentationElement(el);
                        }
                    }

                    break;
                }
        }
    }

    internal void CheckImportDeclaration(Node node)
    {
        // Grammar checking
        Message diagnostic;
        if (IsInJSFile(node))
        {
            diagnostic = Diagnostics.An_import_declaration_can_only_be_used_at_the_top_level_of_a_module;
        }
        else
        {
            diagnostic = Diagnostics.An_import_declaration_can_only_be_used_at_the_top_level_of_a_namespace_or_module;
        }

        if (CheckGrammarModuleElementContext(node, diagnostic))
        {
            // If we hit an import declaration in an illegal context, just bail out to avoid cascading errors.
            CheckExternalModuleNameInGlobalScope(node);
            return;
        }

        if (!CheckGrammarModifiers(node) && node.Modifiers() != null)
        {
            GrammarErrorOnFirstToken(node, Diagnostics.An_import_declaration_cannot_have_modifiers);
        }

        if (CheckExternalImportOrExportDeclaration(node))
        {
            var attributes = GetImportAttributes(node);
            Symbol? resolvedModule = null;
            var importClause = node.ImportClause();
            var moduleSpecifier = node.ModuleSpecifier()!;
            if (importClause != null && !CheckGrammarImportClause(importClause.AsImportClause()!))
            {
                if (importClause.Name() != null)
                {
                    CheckImportBinding(importClause);
                }

                var needsImportStar = false;
                var namedBindings = importClause.AsImportClause()!.NamedBindings;
                if (namedBindings != null)
                {
                    if (IsNamespaceImport(namedBindings))
                    {
                        CheckImportBinding(namedBindings);
                        if (_program.GetEmitModuleFormatOfFile(GetSourceFileOfNode(node)!) == ModuleKind.CommonJS)
                        {
                            // import * as ns from "foo";
                            needsImportStar = true;
                            CheckExternalEmitHelpers(node, ExternalEmitHelpers.ImportStar);
                        }
                    }
                    else
                    {
                        resolvedModule = ResolveExternalModuleNameWorker(node, node.ModuleSpecifier()!, false, GetTypeFromImportAttributes(attributes));
                        if (resolvedModule != null)
                        {
                            foreach (var binding in namedBindings.Elements() ?? [])
                            {
                                CheckImportBinding(binding);
                            }
                        }
                    }
                }

                if (importClause.Name() != null &&
                    !needsImportStar &&
                    _program.GetEmitModuleFormatOfFile(GetSourceFileOfNode(node)!) == ModuleKind.CommonJS)
                {
                    // import d from "foo";
                    CheckExternalEmitHelpers(node, ExternalEmitHelpers.ImportDefault);
                }

                if (!importClause.IsTypeOnly() &&
                    ModuleKind.Node18 <= _moduleKind && _moduleKind <= ModuleKind.NodeNext &&
                    IsOnlyImportableAsDefault(moduleSpecifier, resolvedModule, GetTypeFromImportAttributes(attributes)) &&
                    !HasTypeJsonImportAttribute(node))
                {
                    Error(moduleSpecifier, Diagnostics.Importing_a_JSON_file_into_an_ECMAScript_module_requires_a_type_Colon_json_import_attribute_when_module_is_set_to_0, _moduleKind.String());
                }
            }
            else if (_compilerOptions.NoUncheckedSideEffectImports.IsTrueOrUnknown() && importClause == null)
            {
                var ignoreErrors = _compilerOptions.NoCheck.IsTrue();
                Message? errorMessage = null;
                if (!ignoreErrors)
                {
                    errorMessage = Diagnostics.Cannot_find_module_or_type_declarations_for_side_effect_import_of_0;
                }

                ResolveExternalModuleNameWorkerImpl(node, moduleSpecifier, errorMessage, ignoreErrors, false /*isForAugmentation*/, GetTypeFromImportAttributes(attributes));
            }
        }

        CheckImportAttributes(node);
    }

    internal bool CheckExternalImportOrExportDeclaration(Node node)
    {
        var moduleName = GetExternalModuleName(node);
        if (moduleName == null || NodeIsMissing(moduleName))
        {
            // Should be a parse error.
            return false;
        }

        if (!IsStringLiteral(moduleName))
        {
            Error(moduleName, Diagnostics.String_literal_expected);
            return false;
        }

        var inAmbientExternalModule = IsModuleBlock(node.Parent!) && IsAmbientModule(node.Parent!.Parent!);
        if (!IsSourceFile(node.Parent!) && !inAmbientExternalModule)
        {
            Error(moduleName, IfElse(IsExportDeclaration(node), Diagnostics.Export_declarations_are_not_permitted_in_a_namespace, Diagnostics.Import_declarations_in_a_namespace_cannot_reference_a_module));
            return false;
        }

        if (inAmbientExternalModule && IsExternalModuleNameRelative(moduleName.Text()))
        {
            // we have already reported errors on top level imports/exports in external module augmentations in checkModuleDeclaration
            // no need to do this again.
            if (!IsTopLevelInExternalModuleAugmentation(node))
            {
                // TypeScript 1.0 spec (April 2013): 12.1.6
                // An ExternalImportDeclaration in an AmbientExternalModuleDeclaration may reference
                // other external modules only through top - level external module names.
                // Relative external module names are not permitted.
                Error(node, Diagnostics.Import_or_export_declaration_in_an_ambient_module_declaration_cannot_reference_module_through_relative_module_name);
                return false;
            }
        }

        if (!IsImportEqualsDeclaration(node))
        {
            var attributes = GetImportAttributes(node);
            if (attributes != null)
            {
                if (CheckGrammarImportAttributeValues(attributes.AsImportAttributes()!))
                {
                    return false;
                }
            }
        }

        return true;
    }

    internal void CheckImportBinding(Node node)
    {
        CheckCollisionsForDeclarationName(node, node.Name());
        CheckAliasSymbol(node);
        if (IsImportSpecifier(node))
        {
            CheckModuleExportName(node.PropertyName(), true /*allowStringLiteral*/);
            if (ModuleExportNameIsDefault(node.PropertyNameOrName()) &&
                _program.GetEmitModuleFormatOfFile(GetSourceFileOfNode(node)!) == ModuleKind.CommonJS)
            {
                CheckExternalEmitHelpers(node, ExternalEmitHelpers.ImportDefault);
            }
        }
    }

    internal void CheckModuleExportName(Node? name, bool allowStringLiteral)
    {
        if (name == null || name.Kind != Kind.StringLiteral)
        {
            return;
        }

        if (!allowStringLiteral)
        {
            GrammarErrorOnNode(name, Diagnostics.Identifier_expected);
        }
        else if (_moduleKind == ModuleKind.ES2015 || _moduleKind == ModuleKind.ES2020)
        {
            if (!GetSourceFileOfNode(name)!.IsDeclarationFile)
            {
                GrammarErrorOnNode(name, Diagnostics.String_literal_import_and_export_names_are_not_supported_when_the_module_flag_is_set_to_es2015_or_es2020);
            }
        }
    }

    internal static bool HasTypeJsonImportAttribute(Node node)
    {
        var attributes = node.AsImportDeclaration()!.Attributes;
        return attributes != null && Some(attributes.AsImportAttributes()!.Attributes!.Nodes, attr => attr!.Name()!.Text() == "type" && IsStringLiteralLike(attr.AsImportAttribute()!.Value!) && attr.AsImportAttribute()!.Value!.Text() == "json");
    }

    internal void CheckImportAttributes(Node declaration)
    {
        var node = GetImportAttributes(declaration);
        if (node == null)
        {
            return;
        }

        var importAttributesType = _getGlobalImportAttributesTypeChecked();
        if (importAttributesType != _emptyObjectType)
        {
            CheckTypeAssignableTo(GetTypeFromImportAttributes(node)!, GetNullableType(importAttributesType, TypeFlags.Undefined), node, null);
        }

        var isTypeOnly = IsExclusivelyTypeOnlyImportOrExport(declaration) || IsImportTypeNode(declaration);
        var @override = GetResolutionModeOverride(node.AsImportAttributes()!, isTypeOnly);
        if (isTypeOnly)
        {
            return; // Other grammar checks do not apply to type-only imports with import attributes
        }

        if (!_moduleKind.SupportsImportAttributes())
        {
            GrammarErrorOnNode(node, Diagnostics.Import_attributes_are_only_supported_when_the_module_option_is_set_to_esnext_node18_node20_nodenext_or_preserve);
            return;
        }

        var moduleSpecifier = GetExternalModuleName(declaration);
        if (moduleSpecifier != null)
        {
            if (GetEmitSyntaxForModuleSpecifierExpression(moduleSpecifier) == ModuleKind.CommonJS)
            {
                GrammarErrorOnNode(node, Diagnostics.Import_attributes_are_not_allowed_on_statements_that_compile_to_CommonJS_require_calls);
                return;
            }
        }

        if (@override != ModuleKind.None)
        {
            GrammarErrorOnNode(node, Diagnostics.X_resolution_mode_can_only_be_set_for_type_only_imports);
        }
    }

    internal Type? GetTypeFromImportAttributes(Node? node)
    {
        if (node == null)
        {
            return null;
        }

        if (IsImportAttributes(node))
        {
            return CheckImportAttributesExpression(node);
        }

        return CheckExpressionCached(node);
    }

    internal Type CheckImportAttributesExpression(Node node)
    {
        var links = _typeNodeLinks.Get(node);
        if (links._resolvedType == null)
        {
            var symbol = NewSymbol(SymbolFlags.ObjectLiteral, InternalSymbolNameImportAttributes);
            var members = new SymbolTable();
            foreach (var attribute in node.AsImportAttributes()!.Attributes!.Nodes ?? [])
            {
                var member = NewSymbol(SymbolFlags.Property, attribute!.Name()!.Text());
                _valueSymbolLinks.Get(member)._resolvedType = GetRegularTypeOfLiteralType(CheckExpressionCached(attribute.AsImportAttribute()!.Value!));
                members[member.Name] = member;
            }

            var t = NewAnonymousType(symbol, members, null, null, null);
            t._objectFlags |= ObjectFlags.ObjectLiteral | ObjectFlags.NonInferrableType;
            links._resolvedType = t;
        }

        return links._resolvedType;
    }

    internal Type? GetImportAttributesTypeForModuleSpecifier(Node moduleSpecifier)
    {
        var parent = moduleSpecifier.Parent!;
        switch (parent)
        {
            case { } when IsImportDeclarationOrJSImportDeclaration(parent) || IsExportDeclaration(parent):
                return GetTypeFromImportAttributes(GetImportAttributes(parent));
            case { } when IsLiteralTypeNode(parent) && IsLiteralImportTypeNode(parent.Parent!):
                return GetTypeFromImportAttributes(GetImportAttributes(parent.Parent!));
            case { } when IsImportCall(parent) && (parent.Arguments()?.Length ?? 0) > 1:
                {
                    var options = parent.Arguments()![1];
                    return GetTypeOfPropertyOfTypeWorker(CheckExpressionCached(options), "with");
                }

            default:
                return null;
        }
    }
}
