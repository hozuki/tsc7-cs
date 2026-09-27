// ported from: internal/checker/checker.go:2745-3428
//   （checkPropertyDeclaration、checkPropertySignature、checkSignatureDeclaration、checkAsyncFunctionReturnType、
//     checkMethodDeclaration、checkClassStaticBlockDeclaration、checkConstructorDeclaration、findFirstSuperCall、
//     isInstancePropertyWithInitializerOrPrivateIdentifierProperty、superCallIsRootLevelInConstructor、
//     nodeImmediatelyReferencesSuperOrThis、checkAccessorDeclaration、checkTypeReferenceNode、checkTypeReferenceOrImport、
//     checkTypeArgumentConstraints、getDeprecatedSuggestionNode、checkTypePredicate、getTypePredicateParent、
//     checkIfTypePredicateVariableIsDeclaredInBindingPattern、checkTypeQuery、checkTypeLiteral、
//     checkObjectTypeForDuplicateDeclarations、reportDuplicateMemberErrors、checkArrayType、checkTupleType、
//     checkUnionOrIntersectionType、checkThisType、checkTypeOperator、checkConditionalType、checkInferType、
//     checkTemplateLiteralType、checkImportType、getResolutionModeOverride、checkNamedTupleMember、
//     checkIndexedAccessType、checkMappedType）
//
// 与 Go 的偏离（逐条；其余与 Go 逐行对应）：
// 1) 命中 artifacts/checker-method-collisions.md 的未导出成员改名：`getTypeFromTypeNode`→`GetTypeFromTypeNodeWorker`、
//    `getTypeOfSymbol`→`GetTypeOfSymbolWorker`、`getSignatureFromDeclaration`→`GetSignatureFromDeclarationWorker`、
//    `getTypePredicateOfSignature`→`GetTypePredicateOfSignatureWorker`、
//    `getConstraintOfTypeParameter`→`GetConstraintOfTypeParameterWorker`、`isArrayType`→`IsArrayTypeWorker`、
//    `isArrayLikeType`→`IsArrayLikeTypeWorker`、`symbolToString`→`SymbolToStringWorker`。
//    包级 `isTupleType`（checker.go:23884）已按同规则落成 `IsTupleTypeWorker`（Checker.C6.Tuples.cs）→ `isTupleType(t)` 写 `IsTupleTypeWorker(t)`。
// 2) 属性 vs 方法：`node.Symbol`/`node.SubtreeFacts`/`node.ModifierFlags` 在 Ast/Node.cs 是**属性**，
//    所以 Go 的 ModifierFlags 访问器（带括号）→ C# 的同名属性（无括号）。其余访问器（`Name()`/`Type()`/`Body()`/…）仍是方法。
// 3) 可空数组：Go 的 `range nil-slice` 是空循环，C# 的 `foreach (null)` 会 NRE；Go 直接 range 可空访问器
//    （`node.Members()`/`Parameters()`/`Statements()`/`Elements()`/`TypeArguments()`/`NodeList.Nodes`）的地方一律写 `?? []`，
//    保留 Go 的 nil 语义；把可空数组/可空节点传给非空形参（`CheckSourceElements(Node[])`、`NewTypeMapper`、
//    `AddDeprecatedSuggestion`、`CheckIndexConstraints`、`GetTypeFromTypeNodeWorker`…）时补 `!`。
// 4) Go 的 `if x := f(); cond`（语句内声明 + 条件）→ C# 先声明局部再判断（C# 的 `if` 不能声明变量）。
// 5) Go 的无条件 `switch { case <bool expr>: }` → if/else 链；`break loop` 不出现。
// 6) Go 的闭包 `visit := func(node *ast.Node) bool {...}` / `checkPropertyOrAccessor := func(...)` → C# 语言局部函数。
// 7) nil map 语义：Go 的 `m[k]` 读缺失键/读 nil map 得零值 → `m?.GetValueOrDefault(k) ?? 0`；写前 `if m == nil { m = make(...) }` → `m ??= new()`。
// 8) `core.ResolutionMode`（Go 类型别名）→ `ModuleKind`（Core/CompilerOptions.cs）；`ast.OEKAll` → `OuterExpressionKinds.All`。
// 9) `getResolutionModeOverride` 的 Go 函数类型形参 `func(node *ast.Node, message *diagnostics.Message, args ...any) bool`
//    → C# 具名委托 `GrammarErrorOnNode`（Ast/ImportAttributesNode.cs）；nil 回调照 Go 传 `null`。
// 10) checker 数据模型的访问器是**方法**（ChkTypes 裁决）：Go 的 `t.symbol` 是**未导出字段** → C# 的 `t._symbol`、
//     `signature.parameters` → `signature.Parameters()`、
//     `typePredicate.kind`/`.parameterIndex`/`.t`/`.parameterName` → `typePredicate.Kind()`/`.ParameterIndex()`/`.Type()`/`.ParameterName()`。
// 11) `getDeprecatedSuggestionNode` 的 Go 返回类型是 `*ast.Node`（调用点当非空用，见 checker.go:8541）→ C# 返回 `Node`，
//     内部对可空字段访问补 `!`。
// 12) `getGlobalPromiseTypeChecked` 是 Checker 的 `Func<Type>` **字段**（checker.go:851）→ 调用点 `_getGlobalPromiseTypeChecked()`。
// 13) `node.Parent` 在 C# 是 `Node?`（Go 的 `node.Parent` 直接 `.Kind` 会 panic）→ 保持 Go 的"假定非空"语义，写 `node.Parent!`。
namespace TypeScript.Checker;

using System;
using System.Collections.Generic;
using TypeScript.Ast;
using TypeScript.Core;
using TypeScript.Diagnostics;
using static TypeScript.Ast.Ast;
using static TypeScript.Core.Core;
using static TypeScript.Scanner.Scanner;

public sealed partial class Checker
{
    /// <summary>Go: checker.go:2745 <c>checkPropertyDeclaration</c>。</summary>
    internal void CheckPropertyDeclaration(Node node)
    {
        // Grammar checking
        if (!CheckGrammarModifiers(node) && !CheckGrammarProperty(node))
        {
            CheckGrammarComputedPropertyName(node.Name()!);
        }
        CheckVariableLikeDeclaration(node);
        SetNodeLinksForPrivateIdentifierScope(node);
        // property signatures already report "initializer not allowed in ambient context" elsewhere
        if (HasSyntacticModifier(node, ModifierFlags.Abstract) && IsPropertyDeclaration(node))
        {
            if (node.Initializer() != null)
            {
                Error(node, Diagnostics.Property_0_cannot_have_an_initializer_because_it_is_marked_abstract, DeclarationNameToString(node.Name()));
            }
        }
    }

    /// <summary>Go: checker.go:2760 <c>checkPropertySignature</c>。</summary>
    internal void CheckPropertySignature(Node node)
    {
        if (IsPrivateIdentifier(node.AsPropertySignatureDeclaration()!.Name()!))
        {
            Error(node, Diagnostics.Private_identifiers_are_not_allowed_outside_class_bodies);
        }
        CheckPropertyDeclaration(node);
    }

    /// <summary>Go: checker.go:2767 <c>checkSignatureDeclaration</c>。</summary>
    internal void CheckSignatureDeclaration(Node node)
    {
        // Grammar checking
        switch (node.Kind)
        {
            case Kind.IndexSignature:
                CheckGrammarIndexSignature(node.AsIndexSignatureDeclaration()!);
                break;
            case Kind.FunctionType:
            case Kind.FunctionDeclaration:
            case Kind.ConstructorType:
            case Kind.CallSignature:
            case Kind.Constructor:
            case Kind.ConstructSignature:
                CheckGrammarFunctionLikeDeclaration(node);
                break;
        }
        var functionFlags = GetFunctionFlags(node);
        if ((functionFlags & FunctionFlags.Invalid) == 0)
        {
            // Async generators prior to ES2018 require the __await and __asyncGenerator helpers
            if ((functionFlags & FunctionFlags.AsyncGenerator) == FunctionFlags.AsyncGenerator &&
                _languageVersion < LanguageFeatureMinimumTarget.AsyncGenerators)
            {
                CheckExternalEmitHelpers(node, ExternalEmitHelpers.AsyncGeneratorIncludes);
            }
            if ((functionFlags & FunctionFlags.AsyncGenerator) == FunctionFlags.Async && _languageVersion < LanguageFeatureMinimumTarget.AsyncFunctions)
            {
                CheckExternalEmitHelpers(node, ExternalEmitHelpers.Awaiter);
            }
        }
        CheckTypeParameters(node.TypeParameters());
        CheckUnmatchedJSDocParameters(node);
        CheckSourceElements(node.Parameters() ?? []);
        var returnTypeNode = node.Type();
        if (returnTypeNode != null)
        {
            CheckSourceElement(returnTypeNode);
        }
        if (_noImplicitAny && returnTypeNode == null)
        {
            switch (node.Kind)
            {
                case Kind.ConstructSignature:
                    Error(node, Diagnostics.Construct_signature_which_lacks_return_type_annotation_implicitly_has_an_any_return_type);
                    break;
                case Kind.CallSignature:
                    Error(node, Diagnostics.Call_signature_which_lacks_return_type_annotation_implicitly_has_an_any_return_type);
                    break;
            }
        }
        if (returnTypeNode != null)
        {
            if ((functionFlags & (FunctionFlags.Invalid | FunctionFlags.Generator)) == FunctionFlags.Generator)
            {
                var returnType = GetTypeFromTypeNodeWorker(returnTypeNode);
                if (returnType == _voidType)
                {
                    Error(returnTypeNode, Diagnostics.A_generator_cannot_have_a_void_type_annotation);
                }
                else
                {
                    CheckGeneratorInstantiationAssignabilityToReturnType(returnType, functionFlags, returnTypeNode);
                }
            }
            else if ((functionFlags & FunctionFlags.AsyncGenerator) == FunctionFlags.Async)
            {
                CheckAsyncFunctionReturnType(node, returnTypeNode);
            }
        }
        if (!IsIndexSignatureDeclaration(node))
        {
            RegisterForUnusedIdentifiersCheck(node);
        }
    }

    // Checks the return type of an async function to ensure it is a compatible
    // Promise implementation.
    //
    // This checks that an async function has a valid Promise-compatible return type.
    // An async function has a valid Promise-compatible return type if the resolved value
    // of the return type has a construct signature that takes in an `initializer` function
    // that in turn supplies a `resolve` function as one of its arguments and results in an
    // object with a callable `then` signature.
    /// <summary>Go: checker.go:2826 <c>checkAsyncFunctionReturnType</c>。</summary>
    internal void CheckAsyncFunctionReturnType(Node node, Node? returnTypeNode)
    {
        var returnType = GetTypeFromTypeNodeWorker(returnTypeNode!);
        if (IsErrorType(returnType))
        {
            return;
        }
        var globalPromiseType = _getGlobalPromiseTypeChecked();
        if (globalPromiseType != _emptyGenericType && !IsReferenceToType(returnType, globalPromiseType))
        {
            // The promise type was not a valid type reference to the global promise type, so we
            // report an error and return the unknown type.
            // Go 同处传 core.OrElse(getAwaitedTypeNoAlias(returnType), c.voidType)：c.voidType 在 newChecker 里必被赋值（Checker.New.cs:134）⇒ 结果非 nil。
            Error(returnTypeNode, Diagnostics.The_return_type_of_an_async_function_or_method_must_be_the_global_Promise_T_type_Did_you_mean_to_write_Promise_0, TypeToString(OrElse(GetAwaitedTypeNoAlias(returnType), _voidType)!));
            return;
        }
        CheckAwaitedType(returnType, false /*withAlias*/, node, Diagnostics.The_return_type_of_an_async_function_must_either_be_a_valid_promise_or_must_not_contain_a_callable_then_member);
    }

    /// <summary>Go: checker.go:2841 <c>checkMethodDeclaration</c>。</summary>
    internal void CheckMethodDeclaration(Node node)
    {
        // Grammar checking
        if (!CheckGrammarMethod(node))
        {
            CheckGrammarComputedPropertyName(node.Name()!);
            if (IsMethodDeclaration(node) && node.AsMethodDeclaration()!.AsteriskToken != null && IsIdentifier(node.Name()!) && node.Name()!.Text() == "constructor")
            {
                Error(node.Name(), Diagnostics.Class_constructor_may_not_be_a_generator);
            }
        }
        // Grammar checking for modifiers is done inside the function checkGrammarFunctionLikeDeclaration
        CheckFunctionOrMethodDeclaration(node);
        // method signatures already report "implementation not allowed in ambient context" elsewhere
        if (HasSyntacticModifier(node, ModifierFlags.Abstract) && IsMethodDeclaration(node) && node.Body() != null)
        {
            Error(node, Diagnostics.Method_0_cannot_have_an_implementation_because_it_is_marked_abstract, DeclarationNameToString(node.Name()));
        }
        // Private named methods are only allowed in class declarations
        if (IsPrivateIdentifier(node.Name()!) && GetContainingClass(node) == null)
        {
            Error(node, Diagnostics.Private_identifiers_are_not_allowed_outside_class_bodies);
        }
        SetNodeLinksForPrivateIdentifierScope(node);
    }

    /// <summary>Go: checker.go:2862 <c>checkClassStaticBlockDeclaration</c>。</summary>
    internal void CheckClassStaticBlockDeclaration(Node node)
    {
        // Grammar checking
        CheckGrammarModifiers(node);
        node.ForEachChild(CheckSourceElement);
    }

    /// <summary>Go: checker.go:2868 <c>checkConstructorDeclaration</c>。</summary>
    internal void CheckConstructorDeclaration(Node node)
    {
        // Grammar check on signature of constructor and modifier of the constructor is done in checkSignatureDeclaration function.
        CheckSignatureDeclaration(node);
        // Grammar check for checking only related to constructorDeclaration
        var ctor = node.AsConstructorDeclaration()!;
        if (!CheckGrammarConstructorTypeParameters(ctor))
        {
            CheckGrammarConstructorTypeAnnotation(ctor);
        }
        CheckSourceElement(node.Body());
        var symbol = GetSymbolOfDeclaration(node)!;
        CheckFunctionOrConstructorSymbol(symbol);
        // exit early in the case of signature - super checks are not relevant to them
        if (NodeIsMissing(node.Body()))
        {
            return;
        }
        // TS 1.0 spec (April 2014): 8.3.2
        // Constructors of classes with no extends clause may not contain super calls, whereas
        // constructors of derived classes must contain at least one super call somewhere in their function body.
        var containingClassDecl = node.Parent!;
        if (GetClassExtendsHeritageElement(containingClassDecl) == null)
        {
            return;
        }
        var classExtendsNull = ClassDeclarationExtendsNull(containingClassDecl);
        var superCall = FindFirstSuperCall(node.Body()!);
        if (superCall != null)
        {
            if (classExtendsNull)
            {
                Error(superCall, Diagnostics.A_constructor_cannot_contain_a_super_call_when_its_class_extends_null);
            }
            // A super call must be root-level in a constructor if both of the following are true:
            // - The containing class is a derived class.
            // - The constructor declares parameter properties
            //   or the containing class declares instance member variables with initializers.
            var superCallShouldBeRootLevel = !_emitStandardClassFields &&
                (Some(node.Parent!.Members(), IsInstancePropertyWithInitializerOrPrivateIdentifierProperty) ||
                    Some(node.Parameters(), p => HasSyntacticModifier(p, ModifierFlags.ParameterPropertyModifier)));
            if (superCallShouldBeRootLevel)
            {
                // Until we have better flow analysis, it is an error to place the super call within any kind of block or conditional
                // See GH #8277
                if (!SuperCallIsRootLevelInConstructor(superCall, node.Body()!))
                {
                    Error(superCall, Diagnostics.A_super_call_must_be_a_root_level_statement_within_a_constructor_of_a_derived_class_that_contains_initialized_properties_parameter_properties_or_private_identifiers);
                }
                else
                {
                    Node? superCallStatement = null;
                    foreach (var statement in node.Body()!.Statements() ?? [])
                    {
                        if (IsExpressionStatement(statement) && IsSuperCall(SkipOuterExpressions(statement.Expression(), OuterExpressionKinds.All)!))
                        {
                            superCallStatement = statement;
                            break;
                        }
                        if (NodeImmediatelyReferencesSuperOrThis(statement))
                        {
                            break;
                        }
                    }
                    // Until we have better flow analysis, it is an error to place the super call within any kind of block or conditional
                    // See GH #8277
                    if (superCallStatement == null)
                    {
                        Error(node, Diagnostics.A_super_call_must_be_the_first_statement_in_the_constructor_to_refer_to_super_or_this_when_a_derived_class_contains_initialized_properties_parameter_properties_or_private_identifiers);
                    }
                }
            }
        }
        else if (!classExtendsNull)
        {
            Error(node, Diagnostics.Constructors_for_derived_classes_must_contain_a_super_call);
        }
    }

    /// <summary>Go: checker.go:2933 <c>findFirstSuperCall</c>。</summary>
    internal Node? FindFirstSuperCall(Node node)
    {
        Node? superCall = null;
        bool Visit(Node node)
        {
            if (IsSuperCall(node))
            {
                superCall = node;
                return true;
            }
            if (IsFunctionLike(node))
            {
                return false;
            }
            return node.ForEachChild(Visit);
        }
        Visit(node);
        return superCall;
    }

    /// <summary>Go: checker.go:2950 <c>isInstancePropertyWithInitializerOrPrivateIdentifierProperty</c>（包级函数）。</summary>
    internal static bool IsInstancePropertyWithInitializerOrPrivateIdentifierProperty(Node n)
    {
        return IsPrivateIdentifierClassElementDeclaration(n) || IsPropertyDeclaration(n) && !IsStatic(n) && n.Initializer() != null;
    }

    /// <summary>Go: checker.go:2954 <c>superCallIsRootLevelInConstructor</c>（包级函数）。</summary>
    internal static bool SuperCallIsRootLevelInConstructor(Node superCall, Node body)
    {
        var superCallParent = WalkUpParenthesizedExpressions(superCall.Parent);
        return IsExpressionStatement(superCallParent!) && superCallParent!.Parent == body;
    }

    /// <summary>Go: checker.go:2959 <c>nodeImmediatelyReferencesSuperOrThis</c>（包级函数）。</summary>
    internal static bool NodeImmediatelyReferencesSuperOrThis(Node node)
    {
        switch (node.Kind)
        {
            case Kind.SuperKeyword:
            case Kind.ThisKeyword:
                return true;
            case Kind.ArrowFunction:
            case Kind.FunctionDeclaration:
            case Kind.FunctionExpression:
            case Kind.PropertyDeclaration:
                return false;
            case Kind.Block:
                switch (node.Parent!.Kind)
                {
                    case Kind.Constructor:
                    case Kind.MethodDeclaration:
                    case Kind.GetAccessor:
                    case Kind.SetAccessor:
                        return false;
                }
                break;
        }
        return node.ForEachChild(NodeImmediatelyReferencesSuperOrThis);
    }

    /// <summary>Go: checker.go:2974 <c>checkAccessorDeclaration</c>。</summary>
    internal void CheckAccessorDeclaration(Node node)
    {
        // Grammar checking accessors
        if (!CheckGrammarFunctionLikeDeclaration(node) && !CheckGrammarAccessor(node))
        {
            CheckGrammarComputedPropertyName(node.Name()!);
        }
        var name = node.Name();
        if (IsIdentifier(name!) && name!.Text() == "constructor" && IsClassLike(node.Parent!))
        {
            Error(node.Name(), Diagnostics.Class_constructor_may_not_be_an_accessor);
        }
        CheckDecorators(node);
        CheckSignatureDeclaration(node);
        if (IsGetAccessorDeclaration(node))
        {
            if ((node.Flags & NodeFlags.Ambient) == 0 && NodeIsPresent(node.Body()) && (node.Flags & NodeFlags.HasImplicitReturn) != 0)
            {
                if ((node.Flags & NodeFlags.HasExplicitReturn) == 0)
                {
                    Error(name, Diagnostics.A_get_accessor_must_return_a_value);
                }
            }
        }
        // Do not use hasDynamicName here, because that returns false for well known symbols.
        // We want to perform checkComputedPropertyName for all computed properties, including
        // well known symbols.
        if (IsComputedPropertyName(name!))
        {
            CheckComputedPropertyName(name!);
        }
        if (HasBindableName(node))
        {
            // TypeScript 1.0 spec (April 2014): 8.4.3
            // Accessors for the same member name must specify the same accessibility.
            var symbol = GetSymbolOfDeclaration(node)!;
            var getter = GetDeclarationOfKind(symbol, Kind.GetAccessor);
            var setter = GetDeclarationOfKind(symbol, Kind.SetAccessor);
            if (getter != null && setter != null && (_nodeLinks.Get(getter)._flags & NodeCheckFlags.TypeChecked) == 0)
            {
                _nodeLinks.Get(getter)._flags |= NodeCheckFlags.TypeChecked;
                var getterFlags = getter.ModifierFlags;
                var setterFlags = setter.ModifierFlags;
                if ((getterFlags & ModifierFlags.Abstract) != (setterFlags & ModifierFlags.Abstract))
                {
                    Error(getter.Name(), Diagnostics.Accessors_must_both_be_abstract_or_non_abstract);
                    Error(setter.Name(), Diagnostics.Accessors_must_both_be_abstract_or_non_abstract);
                }
                if (((getterFlags & ModifierFlags.Protected) != 0) && (setterFlags & (ModifierFlags.Protected | ModifierFlags.Private)) == 0 || ((getterFlags & ModifierFlags.Private) != 0) && (setterFlags & ModifierFlags.Private) == 0)
                {
                    Error(getter.Name(), Diagnostics.A_get_accessor_must_be_at_least_as_accessible_as_the_setter);
                    Error(setter.Name(), Diagnostics.A_get_accessor_must_be_at_least_as_accessible_as_the_setter);
                }
            }
        }
        var returnType = GetTypeOfAccessors(GetSymbolOfDeclaration(node)!);
        if (node.Kind == Kind.GetAccessor)
        {
            CheckAllCodePathsInNonVoidFunctionReturnOrThrow(node, returnType);
        }
        CheckSourceElement(node.Body());
        SetNodeLinksForPrivateIdentifierScope(node);
    }

    /// <summary>Go: checker.go:3026 <c>checkTypeReferenceNode</c>。</summary>
    internal void CheckTypeReferenceNode(Node node)
    {
        CheckGrammarTypeArguments(node, node.TypeArgumentList());
        if (IsTypeReferenceNode(node) && (node.Flags & NodeFlags.JSDoc) == 0)
        {
            var data = node.AsTypeReferenceNode()!;
            if (data.TypeArguments != null && data.TypeName!.End() != data.TypeArguments.Pos())
            {
                // If there was a token between the type name and the type arguments, check if it was a DotToken
                var sourceFile = GetSourceFileOfNode(node)!;
                if (ScanTokenAtPosition(sourceFile, data.TypeName!.End()) == Kind.DotToken)
                {
                    GrammarErrorAtPos(node, SkipTrivia(sourceFile.Text(), data.TypeName!.End()), 1, Diagnostics.JSDoc_types_can_only_be_used_inside_documentation_comments);
                }
            }
        }
        CheckSourceElements(node.TypeArguments() ?? []);
        if (!(IsConstTypeReference(node) && IsAssertionExpression(node.Parent)))
        {
            CheckTypeReferenceOrImport(node);
        }
    }

    /// <summary>Go: checker.go:3044 <c>checkTypeReferenceOrImport</c>。</summary>
    internal void CheckTypeReferenceOrImport(Node node)
    {
        var t = GetTypeFromTypeNodeWorker(node);
        if (!IsErrorType(t))
        {
            if ((node.TypeArguments()?.Length ?? 0) != 0)
            {
                var typeParameters = GetTypeParametersForTypeReferenceOrImport(node);
                if ((typeParameters?.Length ?? 0) != 0)
                {
                    CheckTypeArgumentConstraints(node, typeParameters);
                }
            }
            var symbol = GetResolvedSymbolOrNil(node);
            if (symbol != null)
            {
                if (Some(symbol.Declarations, d => IsTypeDeclaration(d) && IsDeprecatedDeclaration(d)))
                {
                    AddDeprecatedSuggestion(GetDeprecatedSuggestionNode(node), symbol.Declarations!, symbol.Name);
                }
            }
        }
    }

    /// <summary>Go: checker.go:3062 <c>checkTypeArgumentConstraints</c>。</summary>
    internal bool CheckTypeArgumentConstraints(Node node, Type[]? typeParameters)
    {
        Type[]? typeArguments = null;
        TypeMapper? mapper = null;
        var result = true;
        var parameters = typeParameters ?? [];
        for (var i = 0; i < parameters.Length; i++)
        {
            var typeParameter = parameters[i];
            var constraint = GetConstraintOfTypeParameterWorker(typeParameter);
            if (constraint != null)
            {
                if (typeArguments == null)
                {
                    typeArguments = GetEffectiveTypeArguments(node, typeParameters);
                    mapper = NewTypeMapper(parameters, typeArguments);
                }
                result = result && CheckTypeAssignableTo(typeArguments[i], InstantiateType(constraint, mapper), ElementOrNil(node.TypeArguments(), i), Diagnostics.Type_0_does_not_satisfy_the_constraint_1);
            }
        }
        return result;
    }

    /// <summary>Go: checker.go:3079 <c>getDeprecatedSuggestionNode</c>。</summary>
    internal Node GetDeprecatedSuggestionNode(Node node)
    {
        node = SkipParentheses(node)!;
        switch (node.Kind)
        {
            case Kind.CallExpression:
            case Kind.Decorator:
            case Kind.NewExpression:
                return GetDeprecatedSuggestionNode(node.Expression()!);
            case Kind.TaggedTemplateExpression:
                return GetDeprecatedSuggestionNode(node.AsTaggedTemplateExpression()!.Tag!);
            case Kind.JsxOpeningElement:
            case Kind.JsxSelfClosingElement:
                return GetDeprecatedSuggestionNode(node.TagName()!);
            case Kind.ElementAccessExpression:
                return node.AsElementAccessExpression()!.ArgumentExpression!;
            case Kind.PropertyAccessExpression:
                return node.Name()!;
            case Kind.TypeReference:
                {
                    var typeName = node.AsTypeReferenceNode()!.TypeName;
                    if (IsQualifiedName(typeName!))
                    {
                        return typeName!.AsQualifiedName()!.Right!;
                    }
                    break;
                }
        }
        return node;
    }

    /// <summary>Go: checker.go:3101 <c>checkTypePredicate</c>。</summary>
    internal void CheckTypePredicate(Node node)
    {
        // Always check the predicate's type so nested type errors are reported even when the
        // predicate is in an invalid position, keeping diagnostics stable.
        CheckSourceElement(node.Type());
        var parent = GetTypePredicateParent(node);
        if (parent == null)
        {
            // The parent must not be valid.
            Error(node, Diagnostics.A_type_predicate_is_only_allowed_in_return_type_position_for_functions_and_methods);
            return;
        }
        var signature = GetSignatureFromDeclarationWorker(parent);
        var typePredicate = GetTypePredicateOfSignatureWorker(signature);
        if (typePredicate == null)
        {
            return;
        }
        var parameterName = node.AsTypePredicateNode()!.ParameterName;
        if (typePredicate.Kind() != TypePredicateKind.This && typePredicate.Kind() != TypePredicateKind.AssertsThis)
        {
            if (typePredicate.ParameterIndex() >= 0)
            {
                if (SignatureHasRestParameter(signature) && typePredicate.ParameterIndex() == signature.Parameters().Length - 1)
                {
                    Error(parameterName, Diagnostics.A_type_predicate_cannot_reference_a_rest_parameter);
                }
                else
                {
                    if (typePredicate.Type() != null)
                    {
                        if (!CheckTypeAssignableToEx(typePredicate.Type()!, GetTypeOfSymbolWorker(signature.Parameters()[typePredicate.ParameterIndex()]), node.Type(), null /*headMessage*/, out var diags))
                        {
                            AddDiagnostic(NewDiagnosticChain(diags![0], Diagnostics.A_type_predicate_s_type_must_be_assignable_to_its_parameter_s_type));
                        }
                    }
                }
            }
            else if (parameterName != null)
            {
                var hasReportedError = false;
                foreach (var param in parent.Parameters() ?? [])
                {
                    var name = param.Name();
                    if (IsBindingPattern(name) && CheckIfTypePredicateVariableIsDeclaredInBindingPattern(name!, parameterName, typePredicate.ParameterName()))
                    {
                        hasReportedError = true;
                        break;
                    }
                }
                if (!hasReportedError)
                {
                    Error(parameterName, Diagnostics.Cannot_find_parameter_0, typePredicate.ParameterName());
                }
            }
        }
    }

    /// <summary>Go: checker.go:3145 <c>getTypePredicateParent</c>。</summary>
    internal Node? GetTypePredicateParent(Node node)
    {
        var parent = node.Parent!;
        switch (parent.Kind)
        {
            case Kind.ArrowFunction:
            case Kind.CallSignature:
            case Kind.FunctionDeclaration:
            case Kind.FunctionExpression:
            case Kind.FunctionType:
            case Kind.MethodDeclaration:
            case Kind.MethodSignature:
                if (node == parent.Type())
                {
                    return parent;
                }
                break;
        }
        return null;
    }

    /// <summary>Go: checker.go:3157 <c>checkIfTypePredicateVariableIsDeclaredInBindingPattern</c>。</summary>
    internal bool CheckIfTypePredicateVariableIsDeclaredInBindingPattern(Node pattern, Node predicateVariableNode, string predicateVariableName)
    {
        foreach (var element in pattern.Elements() ?? [])
        {
            var name = element.Name();
            if (name == null)
            {
                continue;
            }
            if (IsIdentifier(name) && name.Text() == predicateVariableName)
            {
                Error(predicateVariableNode, Diagnostics.A_type_predicate_cannot_reference_element_0_in_a_binding_pattern, predicateVariableName);
                return true;
            }
            if (IsArrayBindingPattern(name) || IsObjectBindingPattern(name))
            {
                if (CheckIfTypePredicateVariableIsDeclaredInBindingPattern(name, predicateVariableNode, predicateVariableName))
                {
                    return true;
                }
            }
        }
        return false;
    }

    /// <summary>Go: checker.go:3176 <c>checkTypeQuery</c>。</summary>
    internal void CheckTypeQuery(Node node)
    {
        GetTypeFromTypeQueryNode(node);
    }

    /// <summary>Go: checker.go:3180 <c>checkTypeLiteral</c>。</summary>
    internal void CheckTypeLiteral(Node node)
    {
        CheckSourceElements(node.Members() ?? []);
        var t = GetTypeFromTypeLiteralOrFunctionOrConstructorTypeNode(node);
        CheckIndexConstraints(t, t._symbol!, false /*isStaticIndex*/);
        CheckTypeForDuplicateIndexSignatures(node);
        CheckObjectTypeForDuplicateDeclarations(node, false /*checkPrivateNames*/);
    }

    /// <summary>Go: checker.go:3188 <c>checkObjectTypeForDuplicateDeclarations</c>。</summary>
    internal void CheckObjectTypeForDuplicateDeclarations(Node node, bool checkPrivateNames)
    {
        Dictionary<string, int>? instanceNames = null;
        Dictionary<string, int>? staticNames = null;
        Dictionary<string, int>? privateNames = null;
        var nodeInAmbientContext = (node.Flags & NodeFlags.Ambient) != 0;
        void CheckPropertyOrAccessor(Symbol? symbol, int kind, bool isStatic)
        {
            if ((symbol!.Declarations?.Length ?? 0) > 1)
            {
                Dictionary<string, int> names;
                if (isStatic)
                {
                    staticNames ??= new Dictionary<string, int>();
                    names = staticNames;
                }
                else
                {
                    instanceNames ??= new Dictionary<string, int>();
                    names = instanceNames;
                }
                var state = names.GetValueOrDefault(symbol!.Name);
                if (state == 0)
                {
                    // On first occurrence just record the kind
                    names[symbol!.Name] = kind;
                }
                else if (state == 1 || state == 2 && kind != 2)
                {
                    // Error on second property or combination of property and accessor
                    ReportDuplicateMemberErrors(node, symbol!.Name, true, isStatic, Diagnostics.Duplicate_identifier_0);
                    // Record that errors have been reported
                    names[symbol!.Name] = 3;
                }
            }
        }
        foreach (var member in node.Members() ?? [])
        {
            if (IsConstructorDeclaration(member))
            {
                foreach (var param in member.Parameters() ?? [])
                {
                    if (IsParameterPropertyDeclaration(param, member) && !IsBindingPattern(param.Name()))
                    {
                        CheckPropertyOrAccessor(GetSymbolOfDeclaration(param), 1, false /*isStatic*/);
                    }
                }
            }
            else
            {
                var symbol = GetSymbolOfDeclaration(member);
                var isStatic = HasStaticModifier(member);
                // In non-ambient contexts, check that static members are not named 'prototype'.
                if (!nodeInAmbientContext && isStatic && symbol != null && symbol.Name == "prototype")
                {
                    Error(member.Name(), Diagnostics.Static_property_0_conflicts_with_built_in_property_Function_0_of_constructor_function_1, symbol.Name, SymbolToStringWorker(GetSymbolOfDeclaration(node)!));
                }
                // Check that this object type declaration doesn't contain multiple declarations of the same property,
                // or accessor and property declarations with the same name.
                if (IsPropertyDeclaration(member) && !HasAccessorModifier(member) || IsPropertySignatureDeclaration(member))
                {
                    CheckPropertyOrAccessor(symbol, 1, isStatic);
                }
                else if (IsAccessor(member) || IsPropertyDeclaration(member) && HasAccessorModifier(member))
                {
                    CheckPropertyOrAccessor(symbol, 2, isStatic);
                }
                // Check that each private identifier is used only for instance members or only for static members. It is an
                // error for an instance and a static member to have the same private identifier.
                if (checkPrivateNames && member.Name() != null && IsPrivateIdentifier(member.Name()!))
                {
                    var flags = privateNames?.GetValueOrDefault(symbol!.Name) ?? 0;
                    if (flags != 3)
                    {
                        flags |= IfElse(IsStatic(member), 2, 1);
                        privateNames ??= new Dictionary<string, int>();
                        privateNames[symbol!.Name] = flags;
                        if (flags == 3)
                        {
                            ReportDuplicateMemberErrors(node, symbol!.Name, false, false, Diagnostics.Duplicate_identifier_0_Static_and_instance_elements_cannot_share_the_same_private_name);
                        }
                    }
                }
            }
        }
    }

    /// <summary>Go: checker.go:3259 <c>reportDuplicateMemberErrors</c>。</summary>
    internal void ReportDuplicateMemberErrors(Node node, string name, bool checkStatic, bool isStatic, Message message)
    {
        foreach (var member in node.Members() ?? [])
        {
            if (IsConstructorDeclaration(member))
            {
                foreach (var param in member.Parameters() ?? [])
                {
                    if (IsParameterPropertyDeclaration(param, member) && !IsBindingPattern(param.Name()))
                    {
                        var symbol = GetSymbolOfDeclaration(param);
                        if (symbol!.Name == name)
                        {
                            Error(param.Name(), message, SymbolToStringWorker(symbol!));
                        }
                    }
                }
            }
            else
            {
                var symbol = GetSymbolOfDeclaration(member);
                if (symbol != null && symbol.Name == name && (!checkStatic || isStatic == IsStatic(member)))
                {
                    Error(member.Name(), message, SymbolToStringWorker(symbol));
                }
            }
        }
    }

    /// <summary>Go: checker.go:3275 <c>checkArrayType</c>。</summary>
    internal void CheckArrayType(Node node)
    {
        CheckSourceElement(node.AsArrayTypeNode()!.ElementType);
    }

    /// <summary>Go: checker.go:3279 <c>checkTupleType</c>。</summary>
    internal void CheckTupleType(Node node)
    {
        var seenOptionalElement = false;
        var seenRestElement = false;
        var elements = node.Elements() ?? [];
        foreach (var e in elements)
        {
            var flags = GetTupleElementFlags(e);
            if ((flags & ElementFlags.Variadic) != 0)
            {
                var t = GetTypeFromTypeNodeWorker(e.Type()!);
                if (!IsArrayLikeTypeWorker(t))
                {
                    Error(e, Diagnostics.A_rest_element_type_must_be_an_array_type);
                    break;
                }
                if (IsArrayTypeWorker(t) || IsTupleTypeWorker(t) && (t.TargetTupleType()!._combinedFlags & ElementFlags.Rest) != 0)
                {
                    flags |= ElementFlags.Rest;
                }
            }
            if ((flags & ElementFlags.Rest) != 0)
            {
                if (seenRestElement)
                {
                    GrammarErrorOnNode(e, Diagnostics.A_rest_element_cannot_follow_another_rest_element);
                    break;
                }
                seenRestElement = true;
            }
            else if ((flags & ElementFlags.Optional) != 0)
            {
                if (seenRestElement)
                {
                    GrammarErrorOnNode(e, Diagnostics.An_optional_element_cannot_follow_a_rest_element);
                    break;
                }
                seenOptionalElement = true;
            }
            else if ((flags & ElementFlags.Required) != 0 && seenOptionalElement)
            {
                GrammarErrorOnNode(e, Diagnostics.A_required_element_cannot_follow_an_optional_element);
                break;
            }
        }
        CheckSourceElements(elements);
        GetTypeFromTypeNodeWorker(node);
    }

    /// <summary>Go: checker.go:3316 <c>checkUnionOrIntersectionType</c>。</summary>
    internal void CheckUnionOrIntersectionType(Node node)
    {
        node.ForEachChild(CheckSourceElement);
        GetTypeFromTypeNodeWorker(node);
    }

    /// <summary>Go: checker.go:3321 <c>checkThisType</c>。</summary>
    internal void CheckThisType(Node node)
    {
        GetTypeFromThisTypeNode(node);
    }

    /// <summary>Go: checker.go:3325 <c>checkTypeOperator</c>。</summary>
    internal void CheckTypeOperator(Node node)
    {
        CheckGrammarTypeOperatorNode(node.AsTypeOperatorNode()!);
        CheckSourceElement(node.Type());
    }

    /// <summary>Go: checker.go:3330 <c>checkConditionalType</c>。</summary>
    internal void CheckConditionalType(Node node)
    {
        node.ForEachChild(CheckSourceElement);
    }

    /// <summary>Go: checker.go:3334 <c>checkInferType</c>。</summary>
    internal void CheckInferType(Node node)
    {
        if (FindAncestor(node, n => n.Parent != null && n.Parent.Kind == Kind.ConditionalType && n.Parent.AsConditionalTypeNode()!.ExtendsType == n) == null)
        {
            GrammarErrorOnNode(node, Diagnostics.X_infer_declarations_are_only_permitted_in_the_extends_clause_of_a_conditional_type);
        }
        var typeParameterDeclarationNode = node.AsInferTypeNode()!.TypeParameter;
        CheckSourceElement(typeParameterDeclarationNode);
        var symbol = GetSymbolOfDeclaration(typeParameterDeclarationNode!)!;
        if ((symbol.Declarations?.Length ?? 0) > 1)
        {
            var links = _declaredTypeLinks.Get(symbol);
            if (!links._typeParametersChecked)
            {
                links._typeParametersChecked = true;
                var typeParameter = GetDeclaredTypeOfTypeParameter(symbol);
                var declarations = GetDeclarationsOfKind(symbol, Kind.TypeParameter) ?? [];
                if (!AreTypeParametersIdentical(declarations, [typeParameter], decl => [decl]))
                {
                    // Report an error on every conflicting declaration.
                    var name = SymbolToStringWorker(symbol);
                    foreach (var declaration in declarations)
                    {
                        Error(declaration.Name(), Diagnostics.All_declarations_of_0_must_have_identical_constraints, name);
                    }
                }
            }
        }
        RegisterForUnusedIdentifiersCheck(node);
    }

    /// <summary>Go: checker.go:3361 <c>checkTemplateLiteralType</c>。</summary>
    internal void CheckTemplateLiteralType(Node node)
    {
        foreach (var span in node.AsTemplateLiteralTypeNode()!.TemplateSpans!.Nodes ?? [])
        {
            CheckSourceElement(span.Type());
            var t = GetTypeFromTypeNodeWorker(span.Type()!);
            CheckTypeAssignableTo(t, _templateConstraintType, span.Type(), null);
        }
        GetTypeFromTypeNodeWorker(node);
    }

    /// <summary>Go: checker.go:3370 <c>checkImportType</c>。</summary>
    internal void CheckImportType(Node node)
    {
        CheckSourceElement(node.AsImportTypeNode()!.Argument);
        var attributes = node.AsImportTypeNode()!.Attributes;
        if (attributes != null)
        {
            var importAttributes = attributes.AsImportAttributes()!;
            CheckGrammarImportAttributeValues(importAttributes);
            GetResolutionModeOverride(importAttributes, true /*reportErrors*/);
        }
        CheckTypeReferenceOrImport(node);
        CheckImportAttributes(node);
    }

    /// <summary>Go: checker.go:3381 <c>getResolutionModeOverride</c>。</summary>
    internal ModuleKind GetResolutionModeOverride(ImportAttributes node, bool reportErrors)
    {
        TypeScript.Ast.GrammarErrorOnNode? grammarErrorOnNode = null;
        if (reportErrors)
        {
            grammarErrorOnNode = GrammarErrorOnNode;
        }
        var (mode, _) = node.GetResolutionModeOverride(grammarErrorOnNode);
        return mode;
    }

    /// <summary>Go: checker.go:3390 <c>checkNamedTupleMember</c>。</summary>
    internal void CheckNamedTupleMember(Node node)
    {
        var tupleMember = node.AsNamedTupleMember()!;
        if (tupleMember.DotDotDotToken != null && tupleMember.QuestionToken != null)
        {
            GrammarErrorOnNode(node, Diagnostics.A_tuple_member_cannot_be_both_optional_and_rest);
        }
        if (tupleMember.Type!.Kind == Kind.OptionalType)
        {
            GrammarErrorOnNode(tupleMember.Type!, Diagnostics.A_labeled_tuple_element_is_declared_as_optional_with_a_question_mark_after_the_name_and_before_the_colon_rather_than_after_the_type);
        }
        if (tupleMember.Type!.Kind == Kind.RestType)
        {
            GrammarErrorOnNode(tupleMember.Type!, Diagnostics.A_labeled_tuple_element_is_declared_as_rest_with_a_before_the_name_rather_than_before_the_type);
        }
        CheckSourceElement(node.Type());
        GetTypeFromTypeNodeWorker(node);
    }

    /// <summary>Go: checker.go:3405 <c>checkIndexedAccessType</c>。</summary>
    internal void CheckIndexedAccessType(Node node)
    {
        node.ForEachChild(CheckSourceElement);
        CheckIndexedAccessIndexType(GetTypeFromIndexedAccessTypeNode(node), node);
    }

    /// <summary>Go: checker.go:3410 <c>checkMappedType</c>。</summary>
    internal void CheckMappedType(Node node)
    {
        var mappedTypeNode = node.AsMappedTypeNode()!;
        CheckGrammarMappedType(mappedTypeNode);
        CheckSourceElement(mappedTypeNode.TypeParameter);
        CheckSourceElement(mappedTypeNode.NameType);
        CheckSourceElement(mappedTypeNode.Type);
        if (mappedTypeNode.Type == null)
        {
            ReportImplicitAny(node, _anyType, WideningKind.Normal);
        }
        var t = GetTypeFromMappedTypeNode(node);
        var nameType = GetNameTypeFromMappedType(t);
        if (nameType != null)
        {
            CheckTypeAssignableTo(nameType, _stringNumberSymbolType, mappedTypeNode.NameType, null);
        }
        else
        {
            var constraintType = GetConstraintTypeFromMappedType(t);
            CheckTypeAssignableTo(constraintType, _stringNumberSymbolType, mappedTypeNode.TypeParameter!.AsTypeParameterDeclaration()!.Constraint, null);
        }
    }
}
