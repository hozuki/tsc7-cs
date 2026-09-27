// ported from: internal/checker/checker.go:11442-11727（属性访问/限定名检查：checkPropertyAccessExpression、
//   checkPropertyAccessChain、checkPropertyAccessExpressionOrQualifiedName、getFlowTypeOfAccessExpression、
//   getControlFlowContainer、getFlowTypeOfProperty、getTypeOfPropertyInBaseClass、isMethodAccessForCall、
//   lookupSymbolForPrivateIdentifierDeclaration、getPrivateIdentifierPropertyOfType、checkPrivateIdentifierPropertyAccess）
//
// 与 Go 的偏离（逐条；其余与 Go 逐行对应）：
// 1) Worker 改名（artifacts/checker-method-collisions.md）：`getTypeOfSymbol` → `GetTypeOfSymbolWorker`、
//    `getPropertyOfType` → `GetPropertyOfTypeWorker`、`getWidenedType` → `GetWidenedTypeWorker`、
//    `getUnionType` → `GetUnionTypeWorker`、`getIndexInfoOfType` → `GetIndexInfoOfTypeWorker`、
//    `getPropertiesOfType` → `GetPropertiesOfTypeWorker`、`getTypeOfPropertyOfType` → `GetTypeOfPropertyOfTypeWorker`、
//    `isReadonlySymbol` → `IsReadonlySymbolWorker`、`symbolToString` → `SymbolToStringWorker`、
//    `isTypeAssignableTo` → `IsTypeAssignableToWorker`。
// 2) Go 的 map 读 `m[k]`（缺键返回零值 nil）→ `GetValueOrDefault(k)`（`SymbolTable : Dictionary<string,Symbol>`，
//    Binder 先例；Dictionary 索引器缺键会抛 KeyNotFoundException）。`Symbol.Members`/`Exports` 在 C# 是可空的
//    `SymbolTable?` → 加 `?.`。
// 3) `indexInfo.isReadonly/valueType/declaration` → `_isReadonly`/`_valueType`/`_declaration`（§5.5）。
// 4) `leftType.symbol` → `leftType._symbol`，`c.globalThisSymbol` → `_globalThisSymbol`（§5.5）。
// 5) `c.symbolNodeLinks.Get(node).resolvedSymbol = prop` → `_symbolNodeLinks.Get(node)._resolvedSymbol = prop`。
namespace TypeScript.Checker;

using TypeScript.Ast;
using TypeScript.Binder;
using TypeScript.Core;
using TypeScript.Diagnostics;
using static TypeScript.Ast.Ast;
using static TypeScript.Core.Core;
using static TypeScript.Scanner.Scanner;

public sealed partial class Checker
{
    internal Type CheckPropertyAccessExpression(Node node, CheckMode checkMode, bool writeOnly)
    {
        if ((node.Flags & NodeFlags.OptionalChain) != 0)
        {
            return CheckPropertyAccessChain(node, checkMode);
        }
        var expr = node.Expression()!;
        return CheckPropertyAccessExpressionOrQualifiedName(node, expr, CheckNonNullExpression(expr), node.AsPropertyAccessExpression()!.Name()!, checkMode, writeOnly);
    }

    internal Type CheckPropertyAccessChain(Node node, CheckMode checkMode)
    {
        var leftType = CheckExpression(node.Expression()!);
        var nonOptionalType = GetOptionalExpressionType(leftType, node.Expression()!);
        return PropagateOptionalTypeMarker(CheckPropertyAccessExpressionOrQualifiedName(node, node.Expression()!, CheckNonNullType(nonOptionalType, node.Expression()!), node.Name()!, checkMode, false), node, nonOptionalType != leftType);
    }

    internal Type CheckPropertyAccessExpressionOrQualifiedName(Node node, Node left, Type leftType, Node right, CheckMode checkMode, bool writeOnly)
    {
        var parentSymbol = GetResolvedSymbolOrNil(left);
        var assignmentKind = GetAssignmentTargetKind(node);
        var widenedType = leftType;
        if (assignmentKind != AssignmentKind.None || IsMethodAccessForCall(node))
        {
            widenedType = GetWidenedTypeWorker(leftType);
        }
        var apparentType = GetApparentTypeWorker(widenedType);
        var isAnyLike = IsTypeAny(apparentType) || apparentType == _silentNeverType;
        Symbol? prop;
        if (IsPrivateIdentifier(right))
        {
            if (_languageVersion < LanguageFeatureMinimumTarget.PrivateNamesAndClassStaticBlocks ||
                _languageVersion < LanguageFeatureMinimumTarget.ClassAndClassElementDecorators ||
                !_compilerOptions.GetUseDefineForClassFields())
            {
                if (assignmentKind != AssignmentKind.None)
                {
                    CheckExternalEmitHelpers(node, ExternalEmitHelpers.ClassPrivateFieldSet);
                }
                if (assignmentKind != AssignmentKind.Definite)
                {
                    CheckExternalEmitHelpers(node, ExternalEmitHelpers.ClassPrivateFieldGet);
                }
            }
            var lexicallyScopedSymbol = LookupSymbolForPrivateIdentifierDeclaration(right.Text(), right);
            if (assignmentKind != AssignmentKind.None && lexicallyScopedSymbol != null && lexicallyScopedSymbol.ValueDeclaration != null && IsMethodDeclaration(lexicallyScopedSymbol.ValueDeclaration))
            {
                GrammarErrorOnNode(right, Diagnostics.Cannot_assign_to_private_method_0_Private_methods_are_not_writable, right.Text());
            }
            if (isAnyLike)
            {
                if (lexicallyScopedSymbol != null)
                {
                    if (IsErrorType(apparentType))
                    {
                        return _errorType;
                    }
                    return apparentType;
                }
                if (GetContainingClassExcludingClassDecorators(right) == null)
                {
                    GrammarErrorOnNode(right, Diagnostics.Private_identifiers_are_not_allowed_outside_class_bodies);
                    return _anyType;
                }
            }
            if (lexicallyScopedSymbol != null)
            {
                prop = GetPrivateIdentifierPropertyOfType(leftType, lexicallyScopedSymbol);
            }
            else
            {
                prop = null;
            }
            if (prop == null)
            {
                // Check for private-identifier-specific shadowing and lexical-scoping errors.
                if (CheckPrivateIdentifierPropertyAccess(leftType, right, lexicallyScopedSymbol))
                {
                    return _errorType;
                }
                var containingClass = GetContainingClassExcludingClassDecorators(right);
                if (containingClass != null && IsPlainJSFile(GetSourceFileOfNode(containingClass), _compilerOptions.CheckJs))
                {
                    GrammarErrorOnNode(right, Diagnostics.Private_field_0_must_be_declared_in_an_enclosing_class, right.Text());
                }
            }
            else
            {
                var isSetonlyAccessor = (prop.Flags & SymbolFlags.SetAccessor) != 0 && (prop.Flags & SymbolFlags.GetAccessor) == 0;
                if (isSetonlyAccessor && assignmentKind != AssignmentKind.Definite)
                {
                    Error(node, Diagnostics.Private_accessor_was_defined_without_a_getter);
                }
            }
        }
        else
        {
            if (isAnyLike)
            {
                if (IsIdentifier(left) && parentSymbol != null)
                {
                    MarkLinkedReferences(node, ReferenceHint.Property, null /*propSymbol*/, leftType);
                }
                if (IsErrorType(apparentType))
                {
                    return _errorType;
                }
                return apparentType;
            }

            prop = GetPropertyOfTypeEx(apparentType, right.Text(), IsConstEnumObjectType(apparentType) /*skipObjectFunctionPropertyAugment*/, node.Kind == Kind.QualifiedName /*includeTypeOnlyMembers*/);
        }
        MarkLinkedReferences(node, ReferenceHint.Property, prop, leftType);
        Type propType;
        if (prop == null)
        {
            IndexInfo? indexInfo = null;
            if (!IsPrivateIdentifier(right) && (assignmentKind == AssignmentKind.None || !IsGenericObjectType(leftType) || IsThisTypeParameter(leftType)))
            {
                indexInfo = GetApplicableIndexInfoForName(apparentType, right.Text());
            }
            if (indexInfo == null)
            {
                var isUncheckedJS = IsUncheckedJSSuggestion(node, leftType._symbol, true /*excludeClasses*/);
                if (!isUncheckedJS && IsJSLiteralType(leftType))
                {
                    return _anyType;
                }
                if (leftType._symbol == _globalThisSymbol)
                {
                    var globalSymbol = _globalThisSymbol.Exports?.GetValueOrDefault(right.Text());
                    if (globalSymbol != null && (globalSymbol.Flags & SymbolFlags.BlockScoped) != 0)
                    {
                        Error(right, Diagnostics.Property_0_does_not_exist_on_type_1, right.Text(), TypeToString(leftType));
                    }
                    else if (_noImplicitAny)
                    {
                        Error(right, Diagnostics.Element_implicitly_has_an_any_type_because_type_0_has_no_index_signature, TypeToString(leftType));
                    }
                    return _anyType;
                }
                if (right.Text() != "" && !CheckAndReportErrorForExtendingInterface(node))
                {
                    AddDeferredDiagnostic(() =>
                    {
                        // must be deferred because reporting this error can cause us to materialize the containing type completely (to print it), leading to erroneous circularity errors
                        ReportNonexistentProperty(right, IfElse(IsThisTypeParameter(leftType), apparentType, leftType), isUncheckedJS);
                    });
                }
                return _errorType;
            }
            if (indexInfo._isReadonly && (IsAssignmentTarget(node) || IsDeleteTarget(node)))
            {
                Error(node, Diagnostics.Index_signature_in_type_0_only_permits_reading, TypeToString(apparentType));
            }
            propType = indexInfo._valueType!;
            if (_compilerOptions.NoUncheckedIndexedAccess == Tristate.TSTrue && GetAssignmentTargetKind(node) != AssignmentKind.Definite)
            {
                propType = GetUnionTypeWorker([propType, _missingType]);
            }
            if (_compilerOptions.NoPropertyAccessFromIndexSignature == Tristate.TSTrue && IsPropertyAccessExpression(node))
            {
                Error(right, Diagnostics.Property_0_comes_from_an_index_signature_so_it_must_be_accessed_with_0, right.Text());
            }
            if (indexInfo._declaration != null && IsDeprecatedDeclaration(indexInfo._declaration))
            {
                AddDeprecatedSuggestion(right, [indexInfo._declaration], right.Text());
            }
        }
        else
        {
            var targetPropSymbol = ResolveAliasWithDeprecationCheck(prop, right);
            if (IsDeprecatedSymbol(targetPropSymbol) && IsUncalledFunctionReference(node, targetPropSymbol) && targetPropSymbol.Declarations != null)
            {
                AddDeprecatedSuggestion(right, targetPropSymbol.Declarations, right.Text());
            }
            CheckPropertyNotUsedBeforeDeclaration(prop, node, right);
            MarkPropertyAsReferenced(prop, node, IsSelfTypeAccess(left, parentSymbol));
            _symbolNodeLinks.Get(node)._resolvedSymbol = prop;
            CheckPropertyAccessibility(node, left.Kind == Kind.SuperKeyword, IsWriteAccess(node), apparentType, prop);
            if (IsAssignmentToReadonlyEntity(node, prop, assignmentKind))
            {
                Error(right, Diagnostics.Cannot_assign_to_0_because_it_is_a_read_only_property, right.Text());
                return _errorType;
            }
            if (IsThisPropertyAccessInConstructor(node, prop))
            {
                propType = _autoType;
            }
            else if (writeOnly || IsWriteOnlyAccess(node))
            {
                propType = GetWriteTypeOfSymbol(prop);
            }
            else
            {
                propType = GetTypeOfSymbolWorker(prop);
            }
        }
        return GetFlowTypeOfAccessExpression(node, prop, propType, right, checkMode);
    }

    internal Type GetFlowTypeOfAccessExpression(Node node, Symbol? prop, Type propType, Node? errorNode, CheckMode checkMode)
    {
        // Only compute control flow type if this is a property access expression that isn't an
        // assignment target, and the referenced property was declared as a variable, property,
        // accessor, or optional method.
        var assignmentKind = GetAssignmentTargetKind(node);
        if (assignmentKind == AssignmentKind.Definite)
        {
            return RemoveMissingType(propType, prop != null && (prop.Flags & SymbolFlags.Optional) != 0);
        }
        if (prop != null && (prop.Flags & (SymbolFlags.Variable | SymbolFlags.Property | SymbolFlags.Accessor)) == 0 && !((prop.Flags & SymbolFlags.Method) != 0 && (propType._flags & TypeFlags.Union) != 0))
        {
            return propType;
        }
        if (propType == _autoType)
        {
            return GetFlowTypeOfProperty(node, prop);
        }
        propType = GetNarrowableTypeForReference(propType, node, checkMode);
        // If strict null checks and strict property initialization checks are enabled, if we have
        // a this.xxx property access, if the property is an instance property without an initializer,
        // and if we are in a constructor of the same class as the property declaration, assume that
        // the property is uninitialized at the top of the control flow.
        var assumeUninitialized = false;
        if (_strictNullChecks && prop != null)
        {
            var declaration = prop.ValueDeclaration;
            if (declaration != null)
            {
                if (_strictPropertyInitialization && IsAccessExpression(node) && node.Expression()!.Kind == Kind.ThisKeyword &&
                    IsPropertyWithoutInitializer(declaration) && !IsStatic(declaration))
                {
                    var flowContainer = GetControlFlowContainer(node);
                    if (IsConstructorDeclaration(flowContainer!) && flowContainer!.Parent == declaration.Parent && (declaration.Flags & NodeFlags.Ambient) == 0)
                    {
                        assumeUninitialized = true;
                    }
                }
                else if (IsBinaryExpression(declaration) && IsPropertyAccessExpression(declaration.AsBinaryExpression()!.Left!) &&
                    GetControlFlowContainer(node) == GetControlFlowContainer(declaration))
                {
                    assumeUninitialized = true;
                }
            }
        }
        var flowType = GetFlowTypeOfReferenceEx(node, propType, AddOptionalityEx(propType, false /*isProperty*/, assumeUninitialized), null, null);
        if (assumeUninitialized && !ContainsUndefinedType(propType) && ContainsUndefinedType(flowType))
        {
            Error(errorNode, Diagnostics.Property_0_is_used_before_being_assigned, SymbolToStringWorker(prop!));
            // Return the declared type to reduce follow-on errors
            return propType;
        }
        if (assignmentKind != AssignmentKind.None)
        {
            return GetBaseTypeOfLiteralTypeWorker(flowType);
        }
        return flowType;
    }

    internal Node? GetControlFlowContainer(Node node)
    {
        return FindAncestor(node.Parent, node => IsFunctionLike(node) && GetImmediatelyInvokedFunctionExpression(node) == null || IsModuleBlock(node) || IsSourceFile(node) || IsPropertyDeclaration(node));
    }

    internal Type GetFlowTypeOfProperty(Node reference, Symbol? prop)
    {
        var initialType = _undefinedType;
        if (prop != null && prop.ValueDeclaration != null && (!IsAutoTypedProperty(prop) || (prop.ValueDeclaration.ModifierFlags & ModifierFlags.Ambient) != 0))
        {
            var baseType = GetTypeOfPropertyInBaseClass(prop);
            if (baseType != null)
            {
                initialType = baseType;
            }
        }
        return GetFlowTypeOfReferenceEx(reference, _autoType, initialType, null, null);
    }

    // Return the inherited type of the given property or undefined if property doesn't exist in a base class.
    internal Type? GetTypeOfPropertyInBaseClass(Symbol property)
    {
        var classType = GetDeclaringClass(property);
        if (classType != null)
        {
            var baseClassTypes = GetBaseTypesWorker(classType) ?? [];
            if (baseClassTypes.Length > 0)
            {
                return GetTypeOfPropertyOfTypeWorker(baseClassTypes[0], property.Name);
            }
        }
        return null;
    }

    internal bool IsMethodAccessForCall(Node node)
    {
        while (IsParenthesizedExpression(node.Parent!))
        {
            node = node.Parent!;
        }
        return IsCallOrNewExpression(node.Parent!) && node.Parent!.Expression() == node;
    }

    // Lookup the private identifier lexically.
    internal Symbol? LookupSymbolForPrivateIdentifierDeclaration(string propName, Node location)
    {
        for (var containingClass = GetContainingClassExcludingClassDecorators(location); containingClass != null; containingClass = GetContainingClass(containingClass))
        {
            var symbol = containingClass.Symbol!;
            var name = Binder.GetSymbolNameForPrivateIdentifier(symbol, propName);
            var prop = symbol.Members?.GetValueOrDefault(name);
            if (prop != null)
            {
                return prop;
            }
            prop = symbol.Exports?.GetValueOrDefault(name);
            if (prop != null)
            {
                return prop;
            }
        }
        return null;
    }

    internal Symbol? GetPrivateIdentifierPropertyOfType(Type leftType, Symbol lexicallyScopedIdentifier)
    {
        return GetPropertyOfTypeWorker(leftType, lexicallyScopedIdentifier.Name);
    }

    internal bool CheckPrivateIdentifierPropertyAccess(Type leftType, Node right, Symbol? lexicallyScopedIdentifier)
    {
        // Either the identifier could not be looked up in the lexical scope OR the lexically scoped identifier did not exist on the type.
        // Find a private identifier with the same description on the type.
        var properties = GetPropertiesOfTypeWorker(leftType) ?? [];
        Symbol? propertyOnType = null;
        foreach (var symbol in properties)
        {
            var decl = symbol.ValueDeclaration;
            if (decl != null && decl.Name() != null && IsPrivateIdentifier(decl.Name()!) && decl.Name()!.Text() == right.Text())
            {
                propertyOnType = symbol;
                break;
            }
        }
        var diagName = DeclarationNameToString(right);
        if (propertyOnType != null)
        {
            var typeValueDecl = propertyOnType.ValueDeclaration!;
            var typeClass = GetContainingClass(typeValueDecl);
            // We found a private identifier property with the same description.
            // Either:
            // - There is a lexically scoped private identifier AND it shadows the one we found on the type.
            // - It is an attempt to access the private identifier outside of the class.
            if (lexicallyScopedIdentifier != null && lexicallyScopedIdentifier.ValueDeclaration != null)
            {
                var lexicalValueDecl = lexicallyScopedIdentifier.ValueDeclaration;
                var lexicalClass = GetContainingClass(lexicalValueDecl);
                if (FindAncestor(lexicalClass, n => typeClass == n) != null)
                {
                    var diagnostic = Error(right, Diagnostics.The_property_0_cannot_be_accessed_on_type_1_within_this_class_because_it_is_shadowed_by_another_private_identifier_with_the_same_spelling, diagName, TypeToString(leftType));
                    diagnostic.AddRelatedInfo(CreateDiagnosticForNode(lexicalValueDecl, Diagnostics.The_shadowing_declaration_of_0_is_defined_here, diagName));
                    diagnostic.AddRelatedInfo(CreateDiagnosticForNode(typeValueDecl, Diagnostics.The_declaration_of_0_that_you_probably_intended_to_use_is_defined_here, diagName));
                    return true;
                }
            }
            Error(right, Diagnostics.Property_0_is_not_accessible_outside_class_1_because_it_has_a_private_identifier, diagName, SymbolToStringWorker(typeClass!.Symbol!));
            return true;
        }
        return false;
    }
}
