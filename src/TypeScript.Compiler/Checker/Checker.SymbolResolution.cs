// ported from: internal/checker/checker.go:1556-1963
//   （invalid initializer / missing prefix / onFailedToResolveSymbol / using-*-as-* 错误报告、
//     spelling suggestion 族、onSuccessfullyResolvedSymbol、checkResolvedBlockScopedVariable）
//
// 与 Go 的偏离（逐条）：
// 1) 命中 artifacts/checker-method-collisions.md 的未导出成员改名：getMergedSymbol→GetMergedSymbolWorker、
//    getTypeOfSymbol→GetTypeOfSymbolWorker、getPropertyOfType→GetPropertyOfTypeWorker、
//    getDeclaredTypeOfSymbol→GetDeclaredTypeOfSymbolWorker、getSymbolFlags→GetSymbolFlagsWorker、
//    symbolToString→SymbolToStringWorker。
// 2) `scanner.DeclarationNameToString` → `TsScanner.DeclarationNameToString`（裸 `Scanner` 在
//    `TypeScript.Checker` 里会解析成命名空间 `TypeScript.Scanner`，故用别名）。
// 3) `sync.OnceValue(func() map[string]*ast.Symbol)` → `static readonly Lazy<Dictionary<string, Symbol>>`
//    （ExecutionAndPublication = sync.Once 的至多执行一次语义，AOT 安全）。
// 4) `iter.Seq[*ast.Symbol]` → `IEnumerable<Symbol>`；`core.ConcatenateSeq(maps.Values(m), extras)` →
//    `ConcatenateSeq(m.Values, extras)`（Core 的签名就是 `params IEnumerable<T>?[]`）。
// 5) `core.GetSpellingSuggestion(..., c.compareSymbols)`：字段是 `Func<Symbol,Symbol,int>`，而参数要
//    `Comparison<Symbol>` → 适配成 `(a, b) => _compareSymbols(a, b)`。
// 6) `maybeMappedType` 的 `for { node = node.Parent; if !(...) { break } }` 用可空局部变量，`Parent == nil`
//    时跳出（Go 在该情形会 nil 解引用 panic；C# 走安全分支）。
namespace TypeScript.Checker;

using System;
using System.Collections.Generic;
using TypeScript.Ast;
using TypeScript.Core;
using TypeScript.Diagnostics;
using TsScanner = TypeScript.Scanner.Scanner;
using static TypeScript.Ast.Ast;
using static TypeScript.Core.Core;

public sealed partial class Checker
{
    /// <summary>Go: checker.go:1556 <c>checkAndReportErrorForInvalidInitializer</c>。</summary>
    internal bool CheckAndReportErrorForInvalidInitializer(Node? errorLocation, string name, Node propertyWithInvalidInitializer, Symbol? result)
    {
        if (!_compilerOptions.GetEmitStandardClassFields())
        {
            if (errorLocation is not null && result is null && CheckAndReportErrorForMissingPrefix(errorLocation, name))
            {
                return true;
            }
            var prop = propertyWithInvalidInitializer.AsPropertyDeclaration()!;
            var message = IfElse(
                errorLocation is not null && prop.Type is not null && prop.Type.Loc.ContainsInclusive(errorLocation.Pos()),
                Diagnostics.Type_of_instance_member_variable_0_cannot_reference_identifier_1_declared_in_the_constructor,
                Diagnostics.Initializer_of_instance_member_variable_0_cannot_reference_identifier_1_declared_in_the_constructor);
            Error(errorLocation, message, TsScanner.DeclarationNameToString(prop.Name()!), name);
            return true;
        }
        return false;
    }

    /// <summary>Go: checker.go:1574 <c>checkAndReportErrorForMissingPrefix</c>。</summary>
    internal bool CheckAndReportErrorForMissingPrefix(Node errorLocation, string name)
    {
        if (!IsIdentifier(errorLocation) || errorLocation.Text() != name || IsTypeReferenceIdentifier(errorLocation) || IsInTypeQuery(errorLocation))
        {
            return false;
        }
        var container = GetThisContainer(errorLocation, false /*includeArrowFunctions*/, false /*includeClassComputedPropertyName*/);
        for (var location = container; location.Parent is not null; location = location.Parent)
        {
            if (IsClassLike(location.Parent))
            {
                var classSymbol = GetSymbolOfDeclaration(location.Parent);
                if (classSymbol is null)
                {
                    break;
                }
                var constructorType = GetTypeOfSymbolWorker(classSymbol);
                if (GetPropertyOfTypeWorker(constructorType, name) is not null)
                {
                    Error(errorLocation, Diagnostics.Cannot_find_name_0_Did_you_mean_the_static_member_1_0, name, SymbolToStringWorker(classSymbol));
                    return true;
                }
                if (ReferenceEquals(location, container) && !IsStatic(location))
                {
                    var instanceType = GetDeclaredTypeOfSymbolWorker(classSymbol).AsInterfaceType()!._thisType!;
                    // TODO: GH#18217
                    if (GetPropertyOfTypeWorker(instanceType, name) is not null)
                    {
                        Error(errorLocation, Diagnostics.Cannot_find_name_0_Did_you_mean_the_instance_member_this_0, name);
                        return true;
                    }
                }
            }
        }
        return false;
    }

    /// <summary>Go: checker.go:1606 <c>onFailedToResolveSymbol</c>。</summary>
    internal void OnFailedToResolveSymbol(Node? errorLocation, string name, SymbolFlags meaning, Message nameNotFoundMessage)
    {
        // The `const` in a `const` assertion (`x as const`) is a syntactic marker, not a real
        // type reference, and must never be resolved or reported as an unresolvable name.
        if (IsConstTypeReferenceName(errorLocation))
        {
            return;
        }
        if (errorLocation is not null && (errorLocation.Parent!.Kind == Kind.JSDocLink ||
            CheckAndReportErrorForMissingPrefix(errorLocation, name) ||
            CheckAndReportErrorForExtendingInterface(errorLocation) ||
            CheckAndReportErrorForUsingTypeAsNamespace(errorLocation, name, meaning) ||
            CheckAndReportErrorForExportingPrimitiveType(errorLocation, name) ||
            CheckAndReportErrorForUsingNamespaceAsTypeOrValue(errorLocation, name, meaning) ||
            CheckAndReportErrorForUsingTypeAsValue(errorLocation, name, meaning) ||
            CheckAndReportErrorForUsingValueAsType(errorLocation, name, meaning)))
        {
            return;
        }
        var declarationName = name;
        if (errorLocation is not null && IsIdentifier(errorLocation) && errorLocation.Text() == name)
        {
            declarationName = TsScanner.DeclarationNameToString(errorLocation); // use escape sequences from original file
        }
        var suggestedLib = GetSuggestedLibForNonExistentName(name);
        if (suggestedLib != "")
        {
            Error(errorLocation, nameNotFoundMessage, declarationName, suggestedLib);
            return;
        }
        var suggestion = GetSuggestedSymbolForNonexistentSymbol(errorLocation, name, meaning);
        if (suggestion is not null && !(suggestion.ValueDeclaration is not null && IsAmbientModule(suggestion.ValueDeclaration) && IsGlobalScopeAugmentation(suggestion.ValueDeclaration)))
        {
            var suggestionName = SymbolToStringWorker(suggestion);
            var isUncheckedJS = IsUncheckedJSSuggestion(errorLocation, suggestion, false /*excludeClasses*/);
            var message = IfElse(meaning == SymbolFlags.Namespace, Diagnostics.Cannot_find_namespace_0_Did_you_mean_1,
                IfElse(isUncheckedJS, Diagnostics.Could_not_find_name_0_Did_you_mean_1, Diagnostics.Cannot_find_name_0_Did_you_mean_1));
            var diagnostic = NewDiagnosticForNode(errorLocation, message, declarationName, suggestionName);
            if (suggestion.ValueDeclaration is not null)
            {
                diagnostic.AddRelatedInfo(NewDiagnosticForNode(suggestion.ValueDeclaration, Diagnostics.X_0_is_declared_here, suggestionName));
            }
            AddErrorOrSuggestion(!isUncheckedJS, diagnostic);
            return;
        }
        Error(errorLocation, nameNotFoundMessage, declarationName);
    }

    /// <summary>Go: checker.go:1650 <c>checkAndReportErrorForUsingTypeAsNamespace</c>。</summary>
    internal bool CheckAndReportErrorForUsingTypeAsNamespace(Node errorLocation, string name, SymbolFlags meaning)
    {
        if (meaning == SymbolFlags.Namespace)
        {
            var symbol = ResolveSymbol(_resolveName(errorLocation, name, SymbolFlags.Type & ~SymbolFlags.Namespace, null /*nameNotFoundMessage*/, false /*isUse*/, false /*excludeGlobals*/));
            if (symbol is not null)
            {
                var parent = errorLocation.Parent!;
                if (IsQualifiedName(parent))
                {
                    Debug.Assert(ReferenceEquals(parent.AsQualifiedName()!.Left, errorLocation), "Should only be resolving left side of qualified name as a namespace");
                    var propName = parent.AsQualifiedName()!.Right!.Text();
                    var propType = GetPropertyOfTypeWorker(GetDeclaredTypeOfSymbolWorker(symbol), propName);
                    if (propType is not null)
                    {
                        Error(parent, Diagnostics.Cannot_access_0_1_because_0_is_a_type_but_not_a_namespace_Did_you_mean_to_retrieve_the_type_of_the_property_1_in_0_with_0_1, name, propName);
                        return true;
                    }
                }
                Error(errorLocation, Diagnostics.X_0_only_refers_to_a_type_but_is_being_used_as_a_namespace_here, name);
                return true;
            }
        }
        return false;
    }

    /// <summary>Go: checker.go:1671 <c>checkAndReportErrorForExportingPrimitiveType</c>。</summary>
    internal bool CheckAndReportErrorForExportingPrimitiveType(Node errorLocation, string name)
    {
        if (IsPrimitiveTypeName(name) && errorLocation.Parent!.Kind == Kind.ExportSpecifier)
        {
            Error(errorLocation, Diagnostics.Cannot_export_0_Only_local_declarations_can_be_exported_from_a_module, name);
            return true;
        }
        return false;
    }

    /// <summary>Go: checker.go:1679 <c>isPrimitiveTypeName</c>（包级函数 → static 成员）。</summary>
    internal static bool IsPrimitiveTypeName(string s)
    {
        return s == "any" || s == "string" || s == "number" || s == "boolean" || s == "never" || s == "unknown";
    }

    /// <summary>Go: checker.go:1683 <c>checkAndReportErrorForUsingNamespaceAsTypeOrValue</c>。</summary>
    internal bool CheckAndReportErrorForUsingNamespaceAsTypeOrValue(Node errorLocation, string name, SymbolFlags meaning)
    {
        if ((meaning & (SymbolFlags.Value & ~SymbolFlags.Type)) != 0)
        {
            var symbol = ResolveSymbol(_resolveName(errorLocation, name, SymbolFlags.NamespaceModule, null /*nameNotFoundMessage*/, false /*isUse*/, false /*excludeGlobals*/));
            if (symbol is not null)
            {
                if (!IsExportAssignmentExpressionName(errorLocation))
                {
                    Error(errorLocation, Diagnostics.Cannot_use_namespace_0_as_a_value, name);
                }
                return true;
            }
        }
        else if ((meaning & (SymbolFlags.Type & ~SymbolFlags.Value)) != 0)
        {
            var symbol = ResolveSymbol(_resolveName(errorLocation, name, SymbolFlags.Module, null /*nameNotFoundMessage*/, false /*isUse*/, false /*excludeGlobals*/));
            if (symbol is not null)
            {
                Error(errorLocation, Diagnostics.Cannot_use_namespace_0_as_a_type, name);
                return true;
            }
        }
        return false;
    }

    /// <summary>Go: checker.go:1704 <c>checkAndReportErrorForUsingTypeAsValue</c>。</summary>
    internal bool CheckAndReportErrorForUsingTypeAsValue(Node errorLocation, string name, SymbolFlags meaning)
    {
        if ((meaning & SymbolFlags.Value) != 0)
        {
            if (IsPrimitiveTypeName(name))
            {
                var grandparent = errorLocation.Parent!.Parent;
                if (grandparent is not null && grandparent.Parent is not null && IsHeritageClause(grandparent))
                {
                    var heritageKind = grandparent.AsHeritageClause()!.Token;
                    var containerKind = grandparent.Parent.Kind;
                    if (containerKind == Kind.InterfaceDeclaration && heritageKind == Kind.ExtendsKeyword)
                    {
                        Error(errorLocation, Diagnostics.An_interface_cannot_extend_a_primitive_type_like_0_It_can_only_extend_other_named_object_types, name);
                    }
                    else if (IsClassLike(grandparent.Parent) && heritageKind == Kind.ExtendsKeyword)
                    {
                        Error(errorLocation, Diagnostics.A_class_cannot_extend_a_primitive_type_like_0_Classes_can_only_extend_constructable_values, name);
                    }
                    else if (IsClassLike(grandparent.Parent) && heritageKind == Kind.ImplementsKeyword)
                    {
                        Error(errorLocation, Diagnostics.A_class_cannot_implement_a_primitive_type_like_0_It_can_only_implement_other_named_object_types, name);
                    }
                }
                else
                {
                    Error(errorLocation, Diagnostics.X_0_only_refers_to_a_type_but_is_being_used_as_a_value_here, name);
                }
                return true;
            }
            var symbol = ResolveSymbol(_resolveName(errorLocation, name, SymbolFlags.Type & ~SymbolFlags.Value, null /*nameNotFoundMessage*/, false /*isUse*/, false /*excludeGlobals*/));
            if (symbol is not null)
            {
                var allFlags = GetSymbolFlagsWorker(symbol);
                if ((allFlags & SymbolFlags.Value) == 0)
                {
                    if (IsExportAssignmentExpressionName(errorLocation))
                    {
                        return true;
                    }
                    if (IsES2015OrLaterConstructorName(name))
                    {
                        Error(errorLocation, Diagnostics.X_0_only_refers_to_a_type_but_is_being_used_as_a_value_here_Do_you_need_to_change_your_target_library_Try_changing_the_lib_compiler_option_to_es2015_or_later, name);
                    }
                    else if (MaybeMappedType(errorLocation, symbol))
                    {
                        Error(errorLocation, Diagnostics.X_0_only_refers_to_a_type_but_is_being_used_as_a_value_here_Did_you_mean_to_use_1_in_0, name, IfElse(name == "K", "P", "K"));
                    }
                    else
                    {
                        Error(errorLocation, Diagnostics.X_0_only_refers_to_a_type_but_is_being_used_as_a_value_here, name);
                    }
                    return true;
                }
            }
        }
        return false;
    }

    /// <summary>Go: checker.go:1746 <c>isES2015OrLaterConstructorName</c>（包级函数 → static 成员）。</summary>
    internal static bool IsES2015OrLaterConstructorName(string s)
    {
        return s == "Promise" || s == "Symbol" || s == "Map" || s == "WeakMap" || s == "Set" || s == "WeakSet";
    }

    /// <summary>Go: checker.go:1750 <c>maybeMappedType</c>。</summary>
    internal bool MaybeMappedType(Node node, Symbol symbol)
    {
        Node? n = node;
        while (true)
        {
            n = n.Parent;
            if (n is null || !(IsComputedPropertyName(n) || IsPropertySignatureDeclaration(n)))
            {
                break;
            }
        }
        if (n is not null && IsTypeLiteralNode(n) && (n.Members()?.Length ?? 0) == 1)
        {
            var t = GetDeclaredTypeOfSymbolWorker(symbol);
            return (t._flags & TypeFlags.Union) != 0 && AllTypesAssignableToKindEx(t, TypeFlags.StringOrNumberLiteral, true /*strict*/);
        }
        return false;
    }

    /// <summary>Go: checker.go:1764 <c>checkAndReportErrorForUsingValueAsType</c>。</summary>
    internal bool CheckAndReportErrorForUsingValueAsType(Node errorLocation, string name, SymbolFlags meaning)
    {
        if ((meaning & (SymbolFlags.Type & ~SymbolFlags.Namespace)) != 0)
        {
            var symbol = ResolveSymbol(_resolveName(errorLocation, name, ~SymbolFlags.Type & SymbolFlags.Value, null /*nameNotFoundMessage*/, false /*isUse*/, false /*excludeGlobals*/));
            if (symbol is not null && (symbol.Flags & SymbolFlags.Namespace) == 0)
            {
                Error(errorLocation, Diagnostics.X_0_refers_to_a_value_but_is_being_used_as_a_type_here_Did_you_mean_typeof_0, name);
                return true;
            }
        }
        return false;
    }

    /// <summary>Go: checker.go:1775 <c>getSuggestedLibForNonExistentName</c>。</summary>
    internal string GetSuggestedLibForNonExistentName(string name)
    {
        var featureMap = GetFeatureMap();
        if (featureMap.TryGetValue(name, out var typeFeatures))
        {
            return typeFeatures[0]._lib;
        }
        return "";
    }

    /// <summary>Go: checker.go:1783 <c>getSuggestedSymbolForNonexistentSymbol</c>。</summary>
    internal Symbol? GetSuggestedSymbolForNonexistentSymbol(Node? location, string outerName, SymbolFlags meaning)
    {
        return _resolveNameForSymbolSuggestion(location, outerName, meaning, null /*nameNotFoundMessage*/, false /*isUse*/, false /*excludeGlobals*/);
    }

    /// <summary>Go: checker.go:1787 的包级 var <c>primitiveTypeAliasSuggestions</c>（sync.OnceValue → Lazy）。</summary>
    internal static readonly Lazy<Dictionary<string, Symbol>> _primitiveTypeAliasSuggestions = new(() =>
    {
        var result = new Dictionary<string, Symbol>(6);
        foreach (var (primitive, builtin) in new[]
        {
            ("string", "String"),
            ("number", "Number"),
            ("boolean", "Boolean"),
            ("object", "Object"),
            ("bigint", "BigInt"),
            ("symbol", "Symbol"),
        })
        {
            var sym = new Symbol();
            sym.Flags = SymbolFlags.TypeAlias | SymbolFlags.Transient;
            sym.Name = primitive;
            result[builtin] = sym;
        }
        return result;
    });

    /// <summary>Go: checker.go:1805 <c>getPrimitiveTypeAliasSuggestions</c>（包级函数 → static 成员）。</summary>
    internal static IEnumerable<Symbol> GetPrimitiveTypeAliasSuggestions(SymbolTable symbols)
    {
        foreach (var (builtinName, suggestion) in _primitiveTypeAliasSuggestions.Value)
        {
            if (symbols.ContainsKey(builtinName))
            {
                yield return suggestion;
            }
        }
    }

    /// <summary>Go: checker.go:1817 <c>getSuggestionForSymbolNameLookup</c>。</summary>
    internal Symbol? GetSuggestionForSymbolNameLookup(SymbolTable symbols, string name, SymbolFlags meaning)
    {
        var symbol = GetSymbol(symbols, name, meaning);
        if (symbol is not null)
        {
            return symbol;
        }
        IEnumerable<Symbol>? extras = null;
        if ((meaning & SymbolFlags.GlobalLookup) != 0)
        {
            extras = GetPrimitiveTypeAliasSuggestions(symbols);
        }
        return GetSpellingSuggestionForName(name, ConcatenateSeq(symbols.Values, extras), meaning);
    }

    // Given a name and a list of symbols whose names are *not* equal to the name, return a spelling suggestion if there is
    // one that is close enough. Names less than length 3 only check for case-insensitive equality, not levenshtein distance.
    /// <summary>Go: checker.go:1842 <c>getSpellingSuggestionForName</c>。</summary>
    internal Symbol? GetSpellingSuggestionForName(string name, IEnumerable<Symbol>? symbols, SymbolFlags meaning)
    {
        string GetCandidateName(Symbol candidate)
        {
            var candidateName = SymbolName(candidate);
            if (candidateName.Length == 0 || candidateName[0] == '"' || candidateName[0] == '\u00FE')
            {
                return "";
            }
            if ((candidate.Flags & meaning) != 0)
            {
                return candidateName;
            }
            if ((candidate.Flags & SymbolFlags.Alias) != 0)
            {
                var alias = TryResolveAlias(candidate);
                if (alias is not null && (alias.Flags & meaning) != 0)
                {
                    return candidateName;
                }
            }
            return "";
        }
        return GetSpellingSuggestion(name, symbols, GetCandidateName, (a, b) => _compareSymbols(a, b));
    }

    /// <summary>Go: checker.go:1862 <c>onSuccessfullyResolvedSymbol</c>。</summary>
    internal void OnSuccessfullyResolvedSymbol(Node? errorLocation, Symbol result, SymbolFlags meaning, Node? lastLocation, Node? associatedDeclarationForContainingInitializerOrBindingName, bool withinDeferredContext)
    {
        var name = result.Name;
        var isInExternalModule = lastLocation is not null && IsSourceFile(lastLocation) && IsExternalOrCommonJSModule(lastLocation.AsSourceFile()!);
        if (errorLocation is not null && ((meaning & SymbolFlags.BlockScopedVariable) != 0 || (meaning & (SymbolFlags.Class | SymbolFlags.Enum)) != 0 && (meaning & SymbolFlags.Value) == SymbolFlags.Value))
        {
            var exportOrLocalSymbol = GetExportSymbolOfValueSymbolIfExported(result)!;
            if ((exportOrLocalSymbol.Flags & (SymbolFlags.BlockScopedVariable | SymbolFlags.Class | SymbolFlags.Enum)) != 0)
            {
                CheckResolvedBlockScopedVariable(exportOrLocalSymbol, errorLocation);
            }
        }
        // If we're in an external module, we can't reference value symbols created from UMD export declarations
        if (isInExternalModule && (meaning & SymbolFlags.Value) == SymbolFlags.Value && (errorLocation!.Flags & NodeFlags.JSDoc) == 0)
        {
            var merged = GetMergedSymbolWorker(result);
            if (merged is not null && merged.Declarations is { Length: > 0 } declarations &&
                Every(declarations, d => IsNamespaceExportDeclaration(d) || IsSourceFile(d) && d.AsSourceFile()!.GlobalExports is not null))
            {
                ErrorOrSuggestion(_compilerOptions.AllowUmdGlobalAccess != Tristate.TSTrue, errorLocation, Diagnostics.X_0_refers_to_a_UMD_global_but_the_current_file_is_a_module_Consider_adding_an_import_instead, name);
            }
        }
        // If we're in a parameter initializer or binding name, we can't reference the values of the parameter whose initializer we're within or parameters to the right
        if (associatedDeclarationForContainingInitializerOrBindingName is not null && !withinDeferredContext && (meaning & SymbolFlags.Value) == SymbolFlags.Value)
        {
            var candidate = GetMergedSymbolWorker(GetLateBoundSymbol(result));
            // Go checker.go:1898 直接解引用 root.Parent（非 nil 由 GetRootDeclaration 的不变量保证，入参非 nil 时返回值非 nil）。
            var root = GetRootDeclaration(associatedDeclarationForContainingInitializerOrBindingName)!;
            // A parameter initializer or binding pattern initializer within a parameter cannot refer to itself
            if (ReferenceEquals(candidate, GetSymbolOfDeclaration(associatedDeclarationForContainingInitializerOrBindingName)))
            {
                Error(errorLocation, Diagnostics.Parameter_0_cannot_reference_itself, TsScanner.DeclarationNameToString(associatedDeclarationForContainingInitializerOrBindingName.Name()!));
            }
            else if (candidate is not null && candidate.ValueDeclaration is not null &&
                candidate.ValueDeclaration.Pos() > associatedDeclarationForContainingInitializerOrBindingName.Pos() &&
                root.Parent!.Locals() is not null &&
                ReferenceEquals(GetSymbol(root.Parent.Locals()!, candidate.Name, meaning), candidate))
            {
                Error(errorLocation, Diagnostics.Parameter_0_cannot_reference_identifier_1_declared_after_it, TsScanner.DeclarationNameToString(associatedDeclarationForContainingInitializerOrBindingName.Name()!), TsScanner.DeclarationNameToString(errorLocation!));
            }
        }
        if (errorLocation is not null && (meaning & SymbolFlags.Value) != 0 && (result.Flags & SymbolFlags.Alias) != 0 && (result.Flags & SymbolFlags.Value) == 0 && !IsValidTypeOnlyAliasUseSite(errorLocation))
        {
            var typeOnlyDeclaration = GetTypeOnlyAliasDeclarationEx(result, SymbolFlags.Value);
            if (typeOnlyDeclaration is not null)
            {
                var message = IfElse(NodeKindIs(typeOnlyDeclaration, Kind.ExportSpecifier, Kind.ExportDeclaration, Kind.NamespaceExport),
                    Diagnostics.X_0_cannot_be_used_as_a_value_because_it_was_exported_using_export_type,
                    Diagnostics.X_0_cannot_be_used_as_a_value_because_it_was_imported_using_import_type);
                AddTypeOnlyDeclarationRelatedInfo(Error(errorLocation, message, name), typeOnlyDeclaration, name);
            }
        }
        // Look at 'compilerOptions.isolatedModules' and not 'getIsolatedModules(...)' (which considers 'verbatimModuleSyntax')
        // here because 'verbatimModuleSyntax' will already have an error for importing a type without 'import type'.
        if (_compilerOptions.IsolatedModules == Tristate.TSTrue && isInExternalModule && (meaning & SymbolFlags.Value) == SymbolFlags.Value)
        {
            var isGlobal = ReferenceEquals(GetSymbol(_globals, name, meaning), result);
            Symbol? nonValueSymbol = null;
            if (isGlobal && lastLocation is not null && IsSourceFile(lastLocation))
            {
                var locals = lastLocation.Locals();
                if (locals is not null)
                {
                    nonValueSymbol = GetSymbol(locals, name, ~SymbolFlags.Value);
                }
            }
            if (nonValueSymbol is not null)
            {
                var importDecl = Find(nonValueSymbol.Declarations, d => NodeKindIs(d, Kind.ImportSpecifier, Kind.ImportClause, Kind.NamespaceImport, Kind.ImportEqualsDeclaration));
                if (importDecl is not null && !IsTypeOnlyImportDeclaration(importDecl))
                {
                    Error(importDecl, Diagnostics.Import_0_conflicts_with_global_value_used_in_this_file_so_must_be_declared_with_a_type_only_import_when_isolatedModules_is_enabled, name);
                }
            }
        }
    }

    /// <summary>Go: checker.go:1930 <c>checkResolvedBlockScopedVariable</c>。</summary>
    internal void CheckResolvedBlockScopedVariable(Symbol result, Node errorLocation)
    {
        Debug.Assert((result.Flags & SymbolFlags.BlockScopedVariable) != 0 || (result.Flags & SymbolFlags.Class) != 0 || (result.Flags & SymbolFlags.Enum) != 0);
        if ((result.Flags & (SymbolFlags.Function | SymbolFlags.FunctionScopedVariable | SymbolFlags.Assignment)) != 0 && (result.Flags & SymbolFlags.Class) != 0)
        {
            // constructor functions aren't block scoped
            return;
        }
        // Block-scoped variables cannot be used before their definition
        var declaration = Find(result.Declarations, d => IsBlockOrCatchScoped(d) || IsClassLike(d) || IsEnumDeclaration(d));
        if (declaration is null)
        {
            Debug.Fail("checkResolvedBlockScopedVariable could not find block-scoped declaration");
        }
        if ((declaration!.Flags & NodeFlags.Ambient) == 0 && !IsBlockScopedNameDeclaredBeforeUse(declaration, errorLocation))
        {
            Diagnostic? diagnostic = null;
            var declarationName = TsScanner.DeclarationNameToString(GetNameOfDeclaration(declaration)!);
            if ((result.Flags & SymbolFlags.BlockScopedVariable) != 0)
            {
                diagnostic = Error(errorLocation, Diagnostics.Block_scoped_variable_0_used_before_its_declaration, declarationName);
            }
            else if ((result.Flags & SymbolFlags.Class) != 0)
            {
                diagnostic = Error(errorLocation, Diagnostics.Class_0_used_before_its_declaration, declarationName);
            }
            else if ((result.Flags & SymbolFlags.RegularEnum) != 0)
            {
                diagnostic = Error(errorLocation, Diagnostics.Enum_0_used_before_its_declaration, declarationName);
            }
            else
            {
                Debug.Assert((result.Flags & SymbolFlags.ConstEnum) != 0);
                if (_compilerOptions.GetIsolatedModules())
                {
                    diagnostic = Error(errorLocation, Diagnostics.Enum_0_used_before_its_declaration, declarationName);
                }
            }
            if (diagnostic is not null)
            {
                diagnostic.AddRelatedInfo(CreateDiagnosticForNode(declaration, Diagnostics.X_0_is_declared_here, declarationName));
            }
        }
    }
}
