// ported from: internal/checker/checker.go:6904-7573（别名符号检查、合并声明导出检查、未使用标识符检查、
//   类型别名/类型参数检查、表达式类型入口：checkAliasSymbol、areDeclarationFlagsIdentical、checkTypeAliasDeclaration、
//   checkTypeNameIsReserved、checkExportsOnMergedDeclarations、getDeclarationSpaces、checkTypeParameters、
//   checkTypeParametersNotReferenced、registerForUnusedIdentifiersCheck、checkUnusedIdentifiers、isReferenced、
//   UnusedKind、reportUnusedVariable、reportUnused、unusedIsError、checkUnusedClassMembers、
//   checkUnusedLocalsAndParameters、reportUnusedLocal、reportUnusedVariables、reportUnusedParameters、
//   reportUnusedBindingElements、reportUnusedVariableDeclarations、isUnreferencedVariableDeclaration、
//   reportUnusedImports、isIdentifierThatStartsWithUnderscore、importClauseFromImported、
//   checkUnusedInferTypeParameter、checkUnusedTypeParameters、isUnreferencedTypeParameter、
//   checkUnusedRenamedBindingElements、checkExpressionStatement、getTypeOfExpression、getQuickTypeOfExpression、
//   getReturnTypeOfSingleNonGenericSignature、getReturnTypeOfSingleNonGenericSignatureOfCallChain）
//
// 与 Go 的偏离（逐条；其余与 Go 逐行对应）：
// 1) Go 的 `type UnusedKind int32` + iota 常量组 → `public enum UnusedKind { Local = 0, Parameter = 1 }`
//    （成员名去 `UnusedKind` 前缀；非位掩码枚举不加 `[Flags]`，与 C1 的 `DeclarationSpaces` 同规则）。
// 2) Go 未导出字段 → `_camelCase`（`links.identifierCheckNodes` → `links._identifierCheckNodes`、
//    `local.symbolReferenceLinks` → `_symbolReferenceLinks`）；`symbolReferenceLinks.Get(s).referenceKinds` → `._referenceKinds`。
// 3) Go 的 map/slice 语义：
//    - `node.Locals()`（Go `SymbolTable = map[string]*Symbol`）→ C# `SymbolTable?`（`Dictionary<string,Symbol>`），
//      `for _, local := range node.Locals()` 取的是 **map 的值** → `foreach (var local in locals.Values)`；
//    - `sourceSymbol.Exports[key]`（Go 读缺失键得 nil）→ `Exports?.GetValueOrDefault(key)`（索引器会抛）；
//    - `importClauses[clause] = append(...)` → `Dictionary<Node, List<Node>>` + `TryGetValue`；
//    - `variableParents collections.Set[*ast.Node]` → `TypeScript.Collections.Set<Node>`（`Keys()` 遍历）。
// 4) `func NewDiagnosticForNode/SymbolName/...` 等走 `using static`；`Diagnostic` 的可变副本
//    （Go `suggestion := *diagnostic; suggestion.SetCategory(...)`）→ `diagnostic.Clone()`（Ast/Diagnostic.cs:210）。
// 5) 包级（无接收者）函数 `isIdentifierThatStartsWithUnderscore`/`importClauseFromImported` →
//    同一个 partial class 的 `internal static` 成员（docs/checker-plan.md §5.2）。
// 6) `defer` 在 Go 的 `debug.Assert`/`panic` → C# `Debug.Assert`/`Debug.Fail`（`TypeScript.Debug`，无需 using）；
//    Go 的 `for ast.IsBindingElement(location) || ast.IsBindingPattern(location) { location = location.Parent }` → `while`。
namespace TypeScript.Checker;

using TypeScript.Ast;
using TypeScript.Collections;
using TypeScript.Core;
using TypeScript.Diagnostics;
using static TypeScript.Ast.Ast;
using static TypeScript.Core.Core;
using static TypeScript.Scanner.Scanner;

/// <summary>Go: <c>type UnusedKind int32</c>（checker.go:7247）+ 常量组 <c>UnusedKindLocal</c>/<c>UnusedKindParameter</c>（7249-7253）。</summary>
public enum UnusedKind
{
    Local = 0,
    Parameter = 1,
}

public sealed partial class Checker
{
    internal void CheckAliasSymbol(Node node)
    {
        var symbol = GetSymbolOfDeclaration(node)!;
        var target = ResolveAliasWorker(symbol);
        if (target == _unknownSymbol)
        {
            return;
        }

        // For external modules, `symbol` represents the local symbol for an alias.
        // This local symbol will merge any other local declarations (excluding other aliases)
        // and symbol.flags will contains combined representation for all merged declaration.
        // Based on symbol.flags we can compute a set of excluded meanings (meaning that resolved alias should not have,
        // otherwise it will conflict with some local declaration). Note that in addition to normal flags we include matching SymbolFlags.Export*
        // in order to prevent collisions with declarations that were exported from the current module (they still contribute to local names).
        symbol = GetMergedSymbolWorker(OrElse(symbol.ExportSymbol, symbol))!;
        var targetFlags = GetSymbolFlagsWorker(target);
        // A type-only import/export will already have a grammar error in a JS file, so no need to issue more errors within
        if (IsInJSFile(node) && (targetFlags & SymbolFlags.Value) == 0 && !IsTypeOnlyImportOrExportDeclaration(node))
        {
            var errorNode = OrElse(node.PropertyNameOrName(), node);
            Debug.Assert(node.Kind != Kind.NamespaceExport);
            if (IsExportSpecifier(node))
            {
                var diag = Error(errorNode, Diagnostics.Types_cannot_appear_in_export_declarations_in_JavaScript_files);
                var sourceSymbol = GetSourceFileOfNode(node)!.Symbol;
                if (sourceSymbol != null)
                {
                    var alreadyExportedSymbol = sourceSymbol.Exports?.GetValueOrDefault(node.PropertyNameOrName()!.Text());
                    if (alreadyExportedSymbol == target)
                    {
                        var exportingDeclaration = Find(alreadyExportedSymbol!.Declarations, IsJSTypeAliasDeclaration);
                        if (exportingDeclaration != null)
                        {
                            diag.AddRelatedInfo(NewDiagnosticForNode(exportingDeclaration, Diagnostics.X_0_is_automatically_exported_here, alreadyExportedSymbol.Name));
                        }
                    }
                }
            }
            else
            {
                var identifierText = symbol.Name;
                if (IsIdentifier(errorNode!))
                {
                    identifierText = errorNode!.Text();
                }

                var specifierText = "...";
                var importDeclaration = FindAncestor(node, n => IsImportOrImportEqualsDeclaration(n) || IsVariableDeclaration(n));
                if (importDeclaration != null)
                {
                    var moduleSpecifier = TryGetModuleSpecifierFromDeclaration(importDeclaration);
                    if (moduleSpecifier != null)
                    {
                        specifierText = moduleSpecifier.Text();
                    }
                }

                var importText = "import(\"" + specifierText + "\")";
                if (IsImportSpecifier(node))
                {
                    importText = importText + "." + identifierText;
                }

                Error(errorNode!, Diagnostics.X_0_is_a_type_and_cannot_be_imported_in_JavaScript_files_Use_1_in_a_JSDoc_type_annotation, identifierText, importText);
            }

            return;
        }

        var excludedMeanings =
            IfElse((symbol.Flags & (SymbolFlags.Value | SymbolFlags.ExportValue)) != 0, SymbolFlags.Value, SymbolFlags.None) |
            IfElse((symbol.Flags & SymbolFlags.Type) != 0, SymbolFlags.Type, SymbolFlags.None) |
            IfElse((symbol.Flags & SymbolFlags.Namespace) != 0, SymbolFlags.Namespace, SymbolFlags.None);
        if ((targetFlags & excludedMeanings) != 0)
        {
            var message = IfElse(
                IsExportSpecifier(node),
                Diagnostics.Export_declaration_conflicts_with_exported_declaration_of_0,
                Diagnostics.Import_declaration_conflicts_with_local_declaration_of_0);
            Error(node, message, SymbolToStringWorker(symbol));
        }
        else if (!IsExportSpecifier(node))
        {
            // Look at 'compilerOptions.isolatedModules' and not 'getIsolatedModules(...)' (which considers 'verbatimModuleSyntax')
            // here because 'verbatimModuleSyntax' will already have an error for importing a type without 'import type'.
            var appearsValueyToTranspiler = _compilerOptions.IsolatedModules.IsTrue() && FindAncestor(node, IsTypeOnlyImportOrExportDeclaration) == null;
            if (appearsValueyToTranspiler && (symbol.Flags & (SymbolFlags.Value | SymbolFlags.ExportValue)) != 0)
            {
                Error(node, Diagnostics.Import_0_conflicts_with_local_value_so_must_be_declared_with_a_type_only_import_when_isolatedModules_is_enabled, SymbolToStringWorker(symbol), GetIsolatedModulesLikeFlagName());
            }
        }

        if (_compilerOptions.GetIsolatedModules() && !IsTypeOnlyImportOrExportDeclaration(node) && (node.Flags & NodeFlags.Ambient) == 0)
        {
            var typeOnlyAlias = GetTypeOnlyAliasDeclarationWorker(symbol);
            var isType = (targetFlags & SymbolFlags.Value) == 0;
            if (isType || typeOnlyAlias != null)
            {
                switch (node.Kind)
                {
                    case Kind.ImportClause:
                    case Kind.ImportSpecifier:
                    case Kind.ImportEqualsDeclaration:
                        if (_compilerOptions.VerbatimModuleSyntax.IsTrue())
                        {
                            Debug.Assert(node.Name() != null, "An ImportClause with a symbol should have a name");
                            Message message;
                            if (_compilerOptions.VerbatimModuleSyntax.IsTrue() && IsInternalModuleImportEqualsDeclaration(node))
                            {
                                message = Diagnostics.An_import_alias_cannot_resolve_to_a_type_or_type_only_declaration_when_verbatimModuleSyntax_is_enabled;
                            }
                            else if (isType)
                            {
                                message = Diagnostics.X_0_is_a_type_and_must_be_imported_using_a_type_only_import_when_verbatimModuleSyntax_is_enabled;
                            }
                            else
                            {
                                message = Diagnostics.X_0_resolves_to_a_type_only_declaration_and_must_be_imported_using_a_type_only_import_when_verbatimModuleSyntax_is_enabled;
                            }

                            var name = node.PropertyNameOrName()!.Text();
                            AddTypeOnlyDeclarationRelatedInfo(Error(node, message, name), IfElse(isType, null, typeOnlyAlias), name);
                        }

                        if (isType && node.Kind == Kind.ImportEqualsDeclaration && HasModifier(node, ModifierFlags.Export))
                        {
                            Error(node, Diagnostics.Cannot_use_export_import_on_a_type_or_type_only_namespace_when_0_is_enabled, GetIsolatedModulesLikeFlagName());
                        }

                        break;
                    case Kind.ExportSpecifier:
                        // Don't allow re-exporting an export that will be elided when `--isolatedModules` is set.
                        // The exception is that `import type { A } from './a'; export { A }` is allowed
                        // because single-file analysis can determine that the export should be dropped.
                        if (_compilerOptions.VerbatimModuleSyntax.IsTrue() || GetSourceFileOfNode(typeOnlyAlias) != GetSourceFileOfNode(node))
                        {
                            var name = node.PropertyNameOrName()!.Text();
                            Diagnostic diagnostic;
                            if (isType)
                            {
                                diagnostic = Error(node, Diagnostics.Re_exporting_a_type_when_0_is_enabled_requires_using_export_type, GetIsolatedModulesLikeFlagName());
                            }
                            else
                            {
                                diagnostic = Error(node, Diagnostics.X_0_resolves_to_a_type_only_declaration_and_must_be_re_exported_using_a_type_only_re_export_when_1_is_enabled, name, GetIsolatedModulesLikeFlagName());
                            }

                            AddTypeOnlyDeclarationRelatedInfo(diagnostic, IfElse(isType, null, typeOnlyAlias), name);
                        }

                        break;
                }
            }

            if (_compilerOptions.VerbatimModuleSyntax.IsTrue() && !IsImportEqualsDeclaration(node) && !IsInJSFile(node) && _program.GetEmitModuleFormatOfFile(GetSourceFileOfNode(node)!) == ModuleKind.CommonJS)
            {
                Error(node, GetVerbatimModuleSyntaxErrorMessage(node));
            }
            else if (_moduleKind == ModuleKind.Preserve && !IsImportEqualsDeclaration(node) && !IsVariableDeclaration(node) && !IsBindingElement(node) && _program.GetEmitModuleFormatOfFile(GetSourceFileOfNode(node)!) == ModuleKind.CommonJS)
            {
                // In `--module preserve`, ESM input syntax emits ESM output syntax, but there will be times
                // when we look at the `impliedNodeFormat` of this file and decide it's CommonJS (i.e., currently,
                // only if the file extension is .cjs/.cts). To avoid that inconsistency, we disallow ESM syntax
                // in files that are unambiguously CommonJS in this mode.
                Error(node, Diagnostics.ECMAScript_module_syntax_is_not_allowed_in_a_CommonJS_module_when_module_is_set_to_preserve);
            }

            if (_compilerOptions.VerbatimModuleSyntax.IsTrue() && !IsTypeOnlyImportOrExportDeclaration(node) && (node.Flags & NodeFlags.Ambient) == 0 && (targetFlags & SymbolFlags.ConstEnum) != 0)
            {
                var constEnumDeclaration = target.ValueDeclaration;
                if (constEnumDeclaration != null && (constEnumDeclaration.Flags & NodeFlags.Ambient) != 0)
                {
                    var redirect = _program.GetProjectReferenceFromOutputDts(GetSourceFileOfNode(constEnumDeclaration)!.Path());
                    if (redirect == null || !redirect.Resolved!.CompilerOptions()!.ShouldPreserveConstEnums())
                    {
                        Error(node, Diagnostics.Cannot_access_ambient_const_enums_when_0_is_enabled, GetIsolatedModulesLikeFlagName());
                    }
                }
            }
        }

        if (IsImportSpecifier(node))
        {
            var targetSymbol = ResolveAliasWithDeprecationCheck(symbol, node);
            if (IsDeprecatedSymbol(targetSymbol) && targetSymbol.Declarations != null)
            {
                AddDeprecatedSuggestion(node, targetSymbol.Declarations, targetSymbol.Name);
            }
        }
    }

    internal bool AreDeclarationFlagsIdentical(Node left, Node right)
    {
        if ((IsParameterDeclaration(left) && IsVariableDeclaration(right)) || (IsVariableDeclaration(left) && IsParameterDeclaration(right)))
        {
            // Differences in optionality between parameters and variables are allowed.
            return true;
        }

        if (IsOptionalDeclaration(left) != IsOptionalDeclaration(right))
        {
            return false;
        }

        var interestingFlags = ModifierFlags.Private | ModifierFlags.Protected | ModifierFlags.Async | ModifierFlags.Abstract | ModifierFlags.Readonly | ModifierFlags.Static;
        return GetSelectedModifierFlags(left, interestingFlags) == GetSelectedModifierFlags(right, interestingFlags);
    }

    internal void CheckTypeAliasDeclaration(Node node)
    {
        // Grammar checking
        CheckGrammarModifiers(node);
        CheckTypeNameIsReserved(node.Name()!, Diagnostics.Type_alias_name_cannot_be_0);
        if (!ContainerAllowsBlockScopedVariable(node.Parent!))
        {
            GrammarErrorOnNode(node, Diagnostics.X_0_declarations_can_only_be_declared_inside_a_block, "type");
        }

        CheckExportsOnMergedDeclarations(node);

        var typeNode = node.Type();
        var typeParameters = node.TypeParameters();
        CheckTypeParameters(typeParameters);
        if (typeNode != null && typeNode.Kind == Kind.IntrinsicKeyword)
        {
            if (!((typeParameters?.Length ?? 0) == 0 && node.Name()!.Text() == "BuiltinIteratorReturn" ||
                (typeParameters?.Length ?? 0) == 1 && IntrinsicTypeKinds.GetValueOrDefault(node.Name()!.Text()) != IntrinsicTypeKind.Unknown))
            {
                Error(typeNode, Diagnostics.The_intrinsic_keyword_can_only_be_used_to_declare_compiler_provided_intrinsic_types);
            }

            // The `intrinsic` keyword is a leaf type node with no child nodes to check,
            // so skipping the checkSourceElement below visits nothing.
            return;
        }

        CheckSourceElement(typeNode!);
        RegisterForUnusedIdentifiersCheck(node);
    }

    internal void CheckTypeNameIsReserved(Node name, Message message)
    {
        // TS 1.0 spec (April 2014): 3.6.1
        // The predefined type keywords are reserved and cannot be used as names of user defined types.
        switch (name.Text())
        {
            case "any":
            case "unknown":
            case "never":
            case "number":
            case "bigint":
            case "boolean":
            case "string":
            case "symbol":
            case "void":
            case "object":
            case "undefined":
                Error(name, message, name.Text());
                break;
        }
    }

    internal void CheckExportsOnMergedDeclarations(Node node)
    {
        // If localSymbol is defined on node then node itself is exported - check is required.
        var symbol = node.LocalSymbol();
        if (symbol == null)
        {
            // Local symbol is undefined => this declaration is non-exported.
            // However, symbol might contain other declarations that are exported.
            symbol = GetSymbolOfDeclaration(node)!;
            if (symbol.ExportSymbol == null)
            {
                // This is a pure local symbol (all declarations are non-exported) - no need to check anything.
                return;
            }
        }

        // Run the check only for the first declaration in the list.
        if (GetDeclarationOfKind(symbol, node.Kind) != node)
        {
            return;
        }

        var exportedDeclarationSpaces = DeclarationSpaces.None;
        var nonExportedDeclarationSpaces = DeclarationSpaces.None;
        var defaultExportedDeclarationSpaces = DeclarationSpaces.None;
        foreach (var d in symbol.Declarations ?? [])
        {
            var declarationSpaces = GetDeclarationSpaces(d);
            var effectiveDeclarationFlags = GetEffectiveDeclarationFlagsWorker(d, ModifierFlags.Export | ModifierFlags.Default);
            if ((effectiveDeclarationFlags & ModifierFlags.Export) != 0)
            {
                if ((effectiveDeclarationFlags & ModifierFlags.Default) != 0)
                {
                    defaultExportedDeclarationSpaces |= declarationSpaces;
                }
                else
                {
                    exportedDeclarationSpaces |= declarationSpaces;
                }
            }
            else
            {
                nonExportedDeclarationSpaces |= declarationSpaces;
            }
        }

        // Spaces for anything not declared a 'default export'.
        var nonDefaultExportedDeclarationSpaces = exportedDeclarationSpaces | nonExportedDeclarationSpaces;
        var commonDeclarationSpacesForExportsAndLocals = exportedDeclarationSpaces & nonExportedDeclarationSpaces;
        var commonDeclarationSpacesForDefaultAndNonDefault = defaultExportedDeclarationSpaces & nonDefaultExportedDeclarationSpaces;
        if (commonDeclarationSpacesForExportsAndLocals != 0 || commonDeclarationSpacesForDefaultAndNonDefault != 0)
        {
            // declaration spaces for exported and non-exported declarations intersect
            foreach (var d in symbol.Declarations ?? [])
            {
                var declarationSpaces = GetDeclarationSpaces(d);
                var name = GetNameOfDeclaration(d);
                // Only error on the declarations that contributed to the intersecting spaces.
                if ((declarationSpaces & commonDeclarationSpacesForDefaultAndNonDefault) != 0)
                {
                    Error(name!, Diagnostics.Merged_declaration_0_cannot_include_a_default_export_declaration_Consider_adding_a_separate_export_default_0_declaration_instead, DeclarationNameToString(name));
                }
                else if ((declarationSpaces & commonDeclarationSpacesForExportsAndLocals) != 0)
                {
                    Error(name!, Diagnostics.Individual_declarations_in_merged_declaration_0_must_be_all_exported_or_all_local, DeclarationNameToString(name));
                }
            }
        }
    }

    internal DeclarationSpaces GetDeclarationSpaces(Node node)
    {
        switch (node.Kind)
        {
            case Kind.InterfaceDeclaration:
            case Kind.TypeAliasDeclaration:
            case Kind.JSTypeAliasDeclaration:
            case Kind.JSDocTypedefTag:
            case Kind.JSDocCallbackTag:
                return DeclarationSpaces.ExportType;
            case Kind.ModuleDeclaration:
                if (IsAmbientModule(node) || GetModuleInstanceState(node) != ModuleInstanceState.NonInstantiated)
                {
                    return DeclarationSpaces.ExportNamespace | DeclarationSpaces.ExportValue;
                }

                return DeclarationSpaces.ExportNamespace;
            case Kind.ClassDeclaration:
            case Kind.EnumDeclaration:
            case Kind.EnumMember:
                return DeclarationSpaces.ExportType | DeclarationSpaces.ExportValue;
            case Kind.SourceFile:
                return DeclarationSpaces.ExportType | DeclarationSpaces.ExportValue | DeclarationSpaces.ExportNamespace;
            case Kind.ExportAssignment:
            case Kind.BinaryExpression:
                Node expression;
                if (IsExportAssignment(node))
                {
                    expression = node.Expression()!;
                }
                else
                {
                    expression = node.AsBinaryExpression()!.Right!;
                }

                // Export assigned entity name expressions act as aliases and should fall through, otherwise they export values.
                if (!IsEntityNameExpression(expression) || (GetSymbolOfDeclaration(node)!.Flags & SymbolFlags.Alias) == 0)
                {
                    return DeclarationSpaces.ExportValue;
                }

                // The below options all declare an Alias, which is allowed to merge with other values within the importing module.
                goto case Kind.ImportEqualsDeclaration;
            case Kind.ImportEqualsDeclaration:
            case Kind.NamespaceImport:
            case Kind.ImportClause:
                var result = DeclarationSpaces.None;
                var target = ResolveAliasWorker(GetSymbolOfDeclaration(node)!);
                foreach (var d in target.Declarations ?? [])
                {
                    result |= GetDeclarationSpaces(d);
                }

                return result;
            case Kind.VariableDeclaration:
            case Kind.BindingElement:
            case Kind.FunctionDeclaration:
            case Kind.ImportSpecifier:
                return DeclarationSpaces.ExportValue;
            case Kind.MethodSignature:
            case Kind.PropertySignature:
                return DeclarationSpaces.ExportType;
        }

        // Go: panic("Unhandled case in getDeclarationSpaces: " + node.Kind.String())
        Debug.Fail("Unhandled case in getDeclarationSpaces: " + node.Kind.ToString());
        return DeclarationSpaces.None;
    }

    internal void CheckTypeParameters(Node[]? typeParameterDeclarations)
    {
        var seenDefault = false;
        for (var i = 0; i < (typeParameterDeclarations?.Length ?? 0); i++)
        {
            var node = typeParameterDeclarations![i];
            CheckTypeParameter(node);
            var defaultTypeNode = node.AsTypeParameterDeclaration()!.DefaultType;
            if (defaultTypeNode != null)
            {
                seenDefault = true;
                CheckTypeParametersNotReferenced(defaultTypeNode, typeParameterDeclarations!, i);
            }
            else if (seenDefault)
            {
                Error(node, Diagnostics.Required_type_parameters_may_not_follow_optional_type_parameters);
            }

            for (var j = 0; j < i; j++)
            {
                if (typeParameterDeclarations![j].Symbol == node.Symbol)
                {
                    Error(node.Name()!, Diagnostics.Duplicate_identifier_0, DeclarationNameToString(node.Name()));
                }
            }
        }
    }

    // Check that type parameter defaults only reference previously declared type parameters */
    internal void CheckTypeParametersNotReferenced(Node root, Node[] typeParameters, int index)
    {
        bool Visit(Node node)
        {
            if (IsTypeReferenceNode(node))
            {
                var t = GetTypeFromTypeReference(node);
                if ((t._flags & TypeFlags.TypeParameter) != 0)
                {
                    for (var i = index; i < typeParameters.Length; i++)
                    {
                        if (t._symbol == GetSymbolOfDeclaration(typeParameters[i]))
                        {
                            Error(node, Diagnostics.Type_parameter_defaults_can_only_reference_previously_declared_type_parameters);
                        }
                    }
                }
            }

            return node.ForEachChild(Visit);
        }

        Visit(root);
    }

    internal void RegisterForUnusedIdentifiersCheck(Node node)
    {
        var sourceFile = GetSourceFileOfNode(node)!;
        var links = _sourceFileLinks.Get(sourceFile);
        // Go: `links.identifierCheckNodes = append(links.identifierCheckNodes, node)`；C# 字段是数组 → 追加后重建。
        links._identifierCheckNodes = [.. links._identifierCheckNodes ?? [], node];
    }

    internal void CheckUnusedIdentifiers(Node[]? potentiallyUnusedIdentifiers)
    {
        foreach (var node in potentiallyUnusedIdentifiers ?? [])
        {
            switch (node.Kind)
            {
                case Kind.ClassDeclaration:
                case Kind.ClassExpression:
                    CheckUnusedClassMembers(node);
                    CheckUnusedTypeParameters(node);
                    break;
                case Kind.SourceFile:
                case Kind.ModuleDeclaration:
                case Kind.Block:
                case Kind.CaseBlock:
                case Kind.ForStatement:
                case Kind.ForInStatement:
                case Kind.ForOfStatement:
                    CheckUnusedLocalsAndParameters(node);
                    break;
                case Kind.Constructor:
                case Kind.FunctionExpression:
                case Kind.FunctionDeclaration:
                case Kind.ArrowFunction:
                case Kind.MethodDeclaration:
                case Kind.GetAccessor:
                case Kind.SetAccessor:
                    // Only report unused parameters on the implementation, not overloads.
                    if (node.Body() != null)
                    {
                        CheckUnusedLocalsAndParameters(node);
                    }

                    CheckUnusedTypeParameters(node);
                    break;
                case Kind.MethodSignature:
                case Kind.CallSignature:
                case Kind.ConstructSignature:
                case Kind.FunctionType:
                case Kind.ConstructorType:
                case Kind.TypeAliasDeclaration:
                case Kind.JSTypeAliasDeclaration:
                case Kind.InterfaceDeclaration:
                    CheckUnusedTypeParameters(node);
                    break;
                case Kind.InferType:
                    CheckUnusedInferTypeParameter(node);
                    break;
                default:
                    // Go: panic("Unhandled case in checkUnusedIdentifiers")
                    Debug.Fail("Unhandled case in checkUnusedIdentifiers");
                    break;
            }
        }
    }

    internal bool IsReferenced(Symbol symbol)
    {
        return _symbolReferenceLinks.Get(symbol)._referenceKinds != 0;
    }

    internal void ReportUnusedVariable(Node location, Diagnostic diagnostic)
    {
        while (IsBindingElement(location) || IsBindingPattern(location))
        {
            location = location.Parent!;
        }

        ReportUnused(location, IfElse(IsParameterDeclaration(location), UnusedKind.Parameter, UnusedKind.Local), diagnostic);
    }

    internal void ReportUnused(Node location, UnusedKind kind, Diagnostic diagnostic)
    {
        if ((location.Flags & (NodeFlags.Ambient | NodeFlags.ThisNodeOrAnySubNodesHasError)) == 0)
        {
            var isError = UnusedIsError(kind);
            if (isError)
            {
                AddDiagnostic(diagnostic);
            }
            else
            {
                // Go: `suggestion := *diagnostic`（结构体浅拷贝）→ `Clone()`。
                var suggestion = diagnostic.Clone();
                suggestion.SetCategory(Category.Suggestion);
                AddSuggestionDiagnostic(suggestion);
            }
        }
    }

    internal bool UnusedIsError(UnusedKind kind)
    {
        switch (kind)
        {
            case UnusedKind.Local:
                return _compilerOptions.NoUnusedLocals.IsTrue();
            case UnusedKind.Parameter:
                return _compilerOptions.NoUnusedParameters.IsTrue();
            default:
                // Go: panic("Unhandled case in unusedIsError")
                Debug.Fail("Unhandled case in unusedIsError");
                return false;
        }
    }

    internal void CheckUnusedClassMembers(Node node)
    {
        foreach (var member in node.Members() ?? [])
        {
            switch (member.Kind)
            {
                case Kind.MethodDeclaration:
                case Kind.PropertyDeclaration:
                case Kind.GetAccessor:
                case Kind.SetAccessor:
                    if (IsSetAccessorDeclaration(member) && (member.Symbol!.Flags & SymbolFlags.GetAccessor) != 0)
                    {
                        break; // Already would have reported an error on the getter.
                    }

                    var symbol = GetSymbolOfDeclaration(member)!;
                    if (!IsReferenced(symbol) && (HasModifier(member, ModifierFlags.Private) || (member.Name() != null && IsPrivateIdentifier(member.Name()!))) && (member.Flags & NodeFlags.Ambient) == 0)
                    {
                        ReportUnused(member, UnusedKind.Local, NewDiagnosticForNode(member.Name(), Diagnostics.X_0_is_declared_but_its_value_is_never_read, SymbolToStringWorker(symbol)));
                    }

                    break;
                case Kind.Constructor:
                    foreach (var parameter in member.Parameters() ?? [])
                    {
                        if (!IsReferenced(parameter.Symbol!) && HasSyntacticModifier(parameter, ModifierFlags.Private))
                        {
                            ReportUnused(parameter, UnusedKind.Local, NewDiagnosticForNode(parameter.Name(), Diagnostics.Property_0_is_declared_but_its_value_is_never_read, SymbolName(parameter.Symbol!)));
                        }
                    }

                    break;
                case Kind.IndexSignature:
                case Kind.SemicolonClassElement:
                case Kind.ClassStaticBlockDeclaration:
                case Kind.JSTypeAliasDeclaration:
                    // Can't be private
                    break;
                default:
                    // Go: panic("Unhandled case in checkUnusedClassMembers")
                    Debug.Fail("Unhandled case in checkUnusedClassMembers");
                    break;
            }
        }
    }

    internal void CheckUnusedLocalsAndParameters(Node node)
    {
        var variableParents = new Set<Node>();
        Dictionary<Node, List<Node>>? importClauses = null;
        var locals = node.Locals();
        if (locals != null)
        {
            foreach (var local in locals.Values)
            {
                var referenceKinds = _symbolReferenceLinks.Get(local)._referenceKinds;
                if (((local.Flags & SymbolFlags.TypeParameter) != 0 && ((local.Flags & SymbolFlags.Variable) == 0 || (referenceKinds & SymbolFlags.Variable) != 0)) ||
                    ((local.Flags & SymbolFlags.TypeParameter) == 0 && (referenceKinds != 0 || local.ExportSymbol != null || (local.Flags & SymbolFlags.ModuleExports) != 0)))
                {
                    continue;
                }

                foreach (var declaration in local.Declarations ?? [])
                {
                    if (IsVariableDeclaration(declaration) || IsParameterDeclaration(declaration) || IsBindingElement(declaration))
                    {
                        variableParents.Add(GetRootDeclaration(declaration)!.Parent!);
                    }
                    else if (IsImportClause(declaration) || IsImportSpecifier(declaration) || IsNamespaceImport(declaration))
                    {
                        if (!IsIdentifierThatStartsWithUnderscore(declaration.Name()!))
                        {
                            importClauses ??= [];
                            var importClause = ImportClauseFromImported(declaration);
                            if (!importClauses.TryGetValue(importClause, out var list))
                            {
                                list = [];
                                importClauses[importClause] = list;
                            }

                            list.Add(declaration);
                        }
                    }
                    else
                    {
                        if (!IsTypeParameterDeclaration(declaration) && !IsAmbientModule(declaration))
                        {
                            ReportUnusedLocal(declaration, SymbolName(local));
                        }
                    }
                }
            }
        }

        foreach (var declaration in variableParents.Keys())
        {
            if (IsVariableDeclarationList(declaration))
            {
                ReportUnusedVariables(declaration);
            }
            else
            {
                ReportUnusedParameters(declaration);
            }
        }

        if (importClauses != null)
        {
            foreach (var (declaration, unuseds) in importClauses)
            {
                ReportUnusedImports(declaration, unuseds.ToArray());
            }
        }
    }

    internal void ReportUnusedLocal(Node node, string name)
    {
        var message = IfElse(IsTypeDeclaration(node), Diagnostics.X_0_is_declared_but_never_used, Diagnostics.X_0_is_declared_but_its_value_is_never_read);
        ReportUnused(node, UnusedKind.Local, NewDiagnosticForNode(OrElse(node.Name(), node), message, name));
    }

    internal void ReportUnusedVariables(Node node)
    {
        var declarations = node.AsVariableDeclarationList()!.Declarations!.Nodes!;
        if (declarations.Length > 1 && Every(declarations, IsUnreferencedVariableDeclaration))
        {
            ReportUnusedVariable(node, NewDiagnosticForNode(node, Diagnostics.All_variables_are_unused));
        }
        else
        {
            ReportUnusedVariableDeclarations(declarations);
        }
    }

    internal void ReportUnusedParameters(Node node)
    {
        ReportUnusedVariableDeclarations(node.Parameters() ?? []);
    }

    internal void ReportUnusedBindingElements(Node node)
    {
        var declarations = node.Elements();
        if ((declarations?.Length ?? 0) > 1 && Every(declarations, IsUnreferencedVariableDeclaration))
        {
            ReportUnusedVariable(node, NewDiagnosticForNode(node, Diagnostics.All_destructured_elements_are_unused));
        }
        else
        {
            ReportUnusedVariableDeclarations(declarations ?? []);
        }
    }

    internal void ReportUnusedVariableDeclarations(Node[]? declarations)
    {
        foreach (var declaration in declarations ?? [])
        {
            var name = declaration.Name();
            if (name != null && !IsParameterPropertyDeclaration(declaration, declaration.Parent!) && !IsThisParameter(declaration))
            {
                if (IsBindingPattern(name))
                {
                    ReportUnusedBindingElements(name);
                }
                else if (IsUnreferencedVariableDeclaration(declaration))
                {
                    ReportUnusedVariable(declaration, NewDiagnosticForNode(name, Diagnostics.X_0_is_declared_but_its_value_is_never_read, name.Text()));
                }
            }
        }
    }

    internal bool IsUnreferencedVariableDeclaration(Node node)
    {
        var name = node.Name();
        if (name == null)
        {
            return true;
        }

        if (IsBindingPattern(name))
        {
            return Every(node.Name()!.Elements(), IsUnreferencedVariableDeclaration);
        }

        if ((_symbolReferenceLinks.Get(GetSymbolOfDeclaration(node)!)._referenceKinds & SymbolFlags.Variable) != 0)
        {
            return false;
        }

        if (IsBindingElement(node) && IsObjectBindingPattern(node.Parent!))
        {
            // In `{ a, ...b }, `a` is considered used since it removes a property from `b`. `b` may still be unused though.
            var lastElement = LastOrNil(node.Parent!.Elements());
            if (node != lastElement && HasDotDotDotToken(lastElement!))
            {
                return false;
            }
        }

        if ((IsParameterDeclaration(node) ||
            (IsVariableDeclaration(node) && (IsForInOrOfStatement(node.Parent!.Parent!) || (GetCombinedNodeFlagsCached(node) & NodeFlags.Using) != 0)) ||
            (IsBindingElement(node) && !(IsObjectBindingPattern(node.Parent!) && node.PropertyName() == null))) &&
            IsIdentifierThatStartsWithUnderscore(name))
        {
            return false;
        }

        return true;
    }

    internal void ReportUnusedImports(Node node, Node[] unuseds)
    {
        var declarationCount = IfElse(node.Name() != null, 1, 0);
        var namedBindings = node.AsImportClause()!.NamedBindings;
        if (namedBindings != null)
        {
            if (IsNamespaceImport(namedBindings))
            {
                declarationCount++;
            }
            else
            {
                declarationCount += namedBindings.Elements()!.Length;
            }
        }

        if (declarationCount > 1 && declarationCount == unuseds.Length)
        {
            ReportUnused(node, UnusedKind.Local, NewDiagnosticForNode(node.Parent, Diagnostics.All_imports_in_import_declaration_are_unused));
        }
        else
        {
            foreach (var unused in unuseds)
            {
                ReportUnusedLocal(unused, unused.Name()!.Text());
            }
        }
    }

    // Go: 包级函数（无接收者）
    internal static bool IsIdentifierThatStartsWithUnderscore(Node node)
    {
        return IsIdentifier(node) && node.Text() != "" && node.Text()[0] == '_';
    }

    // Go: 包级函数（无接收者）
    internal static Node ImportClauseFromImported(Node node)
    {
        switch (node.Kind)
        {
            case Kind.ImportClause:
                return node;
            case Kind.NamespaceImport:
                return node.Parent!;
            default:
                return node.Parent!.Parent!;
        }
    }

    internal void CheckUnusedInferTypeParameter(Node node)
    {
        var typeParameter = node.AsInferTypeNode()!.TypeParameter!;
        if (IsUnreferencedTypeParameter(typeParameter))
        {
            ReportUnused(node, UnusedKind.Parameter, NewDiagnosticForNode(typeParameter.Name(), Diagnostics.X_0_is_declared_but_never_used, typeParameter.Name()!.Text()));
        }
    }

    internal void CheckUnusedTypeParameters(Node node)
    {
        if (!AllDeclarationsInSameSourceFile(GetSymbolOfDeclaration(node)!))
        {
            return;
        }

        var typeParameterList = node.TypeParameterList();
        if (typeParameterList == null)
        {
            return;
        }

        if (typeParameterList.Nodes!.Length > 1 && Every(typeParameterList.Nodes, IsUnreferencedTypeParameter))
        {
            var file = GetSourceFileOfNode(node)!;
            var loc = RangeOfTypeParameters(file, typeParameterList);
            ReportUnused(node, UnusedKind.Parameter, NewDiagnostic(file, loc, Diagnostics.All_type_parameters_are_unused));
        }
        else
        {
            foreach (var typeParameter in typeParameterList.Nodes!)
            {
                if (IsUnreferencedTypeParameter(typeParameter))
                {
                    ReportUnused(node, UnusedKind.Parameter, NewDiagnosticForNode(typeParameter, Diagnostics.X_0_is_declared_but_never_used, typeParameter.Name()!.Text()));
                }
            }
        }
    }

    internal bool IsUnreferencedTypeParameter(Node typeParameter)
    {
        return (_symbolReferenceLinks.Get(GetMergedSymbolWorker(typeParameter.Symbol!)!)._referenceKinds & SymbolFlags.TypeParameter) == 0 && !IsIdentifierThatStartsWithUnderscore(typeParameter.Name()!);
    }

    internal void CheckUnusedRenamedBindingElements()
    {
        foreach (var node in _renamedBindingElementsInTypes)
        {
            if (_symbolReferenceLinks.Get(GetSymbolOfDeclaration(node)!)._referenceKinds == 0)
            {
                var wrappingDeclaration = WalkUpBindingElementsAndPatterns(node)!;
                Debug.Assert(IsPartOfParameterDeclaration(wrappingDeclaration), "Only parameter declaration should be checked here");
                var diagnostic = NewDiagnosticForNode(node.Name(), Diagnostics.X_0_is_an_unused_renaming_of_1_Did_you_intend_to_use_it_as_a_type_annotation, DeclarationNameToString(node.Name()), DeclarationNameToString(node.PropertyName()));
                if (wrappingDeclaration.Type() == null)
                {
                    // entire parameter does not have type annotation, suggest adding an annotation
                    diagnostic.AddRelatedInfo(NewDiagnostic(GetSourceFileOfNode(wrappingDeclaration), NewTextRange(wrappingDeclaration.End(), wrappingDeclaration.End()), Diagnostics.We_can_only_write_a_type_for_0_by_adding_a_type_for_the_entire_parameter_here, DeclarationNameToString(node.PropertyName())));
                }

                AddDiagnostic(diagnostic);
            }
        }
    }

    internal void CheckExpressionStatement(Node node)
    {
        // Grammar checking
        CheckGrammarStatementInAmbientContext(node);
        CheckExpression(node.Expression()!);
    }

    // Returns the type of an expression. Unlike checkExpression, this function is simply concerned
    // with computing the type and may not fully check all contained sub-expressions for errors.
    internal Type GetTypeOfExpression(Node node)
    {
        // Don't bother caching types that require no flow analysis and are quick to compute.
        var quickType = GetQuickTypeOfExpression(node);
        if (quickType != null)
        {
            return quickType;
        }

        // If a type has been cached for the node, return it.
        if (_flowTypeCache.TryGetValue(node, out var cachedType))
        {
            return cachedType;
        }

        var startInvocationCount = _flowInvocationCount;
        var t = CheckExpressionEx(node, CheckMode.TypeOnly);
        // If control flow analysis was required to determine the type, it is worth caching.
        if (_flowInvocationCount != startInvocationCount)
        {
            _flowTypeCache[node] = t;
        }

        return t;
    }

    // Returns the type of an expression. Unlike checkExpression, this function is simply concerned
    // with computing the type and may not fully check all contained sub-expressions for errors.
    internal Type? GetQuickTypeOfExpression(Node node)
    {
        var expr = SkipParentheses(node)!;
        if (IsAwaitExpression(expr))
        {
            var t = GetQuickTypeOfExpression(expr.Expression()!);
            if (t != null)
            {
                return GetAwaitedTypeWorker(t);
            }

            return null;
        }

        // Optimize for the common case of a call to a function with a single non-generic call
        // signature where we can just fetch the return type without checking the arguments.
        if (IsCallExpression(expr) && expr.Expression()!.Kind != Kind.SuperKeyword && !IsRequireCall(expr, true /*requireStringLiteralLikeArgument*/) && !IsSymbolOrSymbolForCall(expr) && !IsImportCall(expr))
        {
            if (IsCallChain(expr))
            {
                return GetReturnTypeOfSingleNonGenericSignatureOfCallChain(expr);
            }

            return GetReturnTypeOfSingleNonGenericSignature(CheckNonNullExpression(expr.Expression()!), SignatureKind.Call);
        }

        if (IsNewExpression(expr))
        {
            return GetReturnTypeOfSingleNonGenericSignature(CheckNonNullExpression(expr.Expression()!), SignatureKind.Construct);
        }

        if (IsAssertionExpression(expr) && !IsConstTypeReference(expr.Type()!))
        {
            return GetTypeFromTypeNodeWorker(expr.Type()!);
        }

        if (IsLiteralExpression(node) || IsBooleanLiteral(node))
        {
            return CheckExpression(node);
        }

        return null;
    }

    internal Type? GetReturnTypeOfSingleNonGenericSignature(Type funcType, SignatureKind kind)
    {
        var signature = GetSingleSignature(funcType, kind, true /*allowMembers*/);
        if (signature != null && signature.TypeParameters().Length == 0)
        {
            return GetReturnTypeOfSignatureWorker(signature);
        }

        return null;
    }

    internal Type? GetReturnTypeOfSingleNonGenericSignatureOfCallChain(Node expr)
    {
        var funcType = CheckExpression(expr.Expression()!);
        var nonOptionalType = GetOptionalExpressionType(funcType, expr.Expression()!);
        var returnType = GetReturnTypeOfSingleNonGenericSignature(funcType, SignatureKind.Call);
        if (returnType != null)
        {
            return PropagateOptionalTypeMarker(returnType, expr, nonOptionalType != funcType);
        }

        return null;
    }
}
