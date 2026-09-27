// ported from: internal/checker/grammarchecks.go:998-1757（对象字面量/JSX/for-in-of/访问器/类型操作符/方法/break-continue/
//   绑定元素/变量声明/await-await using 的文法检查）
//
// 与 Go 的差异（其余逐行对应；依据 docs/architecture.md §2/§3、docs/checker-plan.md §5）：
// - `map[string]DeclarationMeaning` → `Dictionary<string, DeclarationMeaning>`。Go 的 `existingKind := seen[name]`
//   （缺失键 = 零值 0）→ `seen.TryGetValue(name, out var existingKind)` 后判 `== 0`（C# 索引器读缺失键会抛异常）。
// - `ast.ForInOrOfStatement` 在 C# 是 `Node`（Go 的 union 结构体在 C# 里也是 `ForInOrOfStatement` 类，但参数取 `Node`
//   以便调用点两种实参都能传）→ 字段访问走 `AsForInOrOfStatement()`。
// - Go 的 `switch` + `fallthrough`（moduleKind → ES2022 一组 → default）→ C# `goto case`/`goto default`（逐分支等价，
//   `break` 仍然只跳出 switch）。
// - Go 的 `INamedMemberBase` 嵌入指针 `commonProp` → C# 基础接口 `INamedMemberBase`（`PostfixToken` 属性）。
// - Go 的 `node.Elements()` 可能是 nil slice（range 0 次）→ `if (node.Elements() is { } elements)`。
// - `name.AsJsxNamespacedName().Namespace.Text()`：C# 里 `Namespace` 是 `Identifier`，`Text` 是字段（Go 的 `.Text()`
//   是 Node 方法，取同一个值）。
namespace TypeScript.Checker;

using TypeScript.Ast;
using TypeScript.Collections;
using TypeScript.Core;
using TypeScript.Diagnostics;
using static TypeScript.Ast.Ast;
using static TypeScript.Core.Core;
using static TypeScript.Diagnostics.Diagnostics;
using static TypeScript.Scanner.Scanner;

public sealed partial class Checker
{
    // ported from: internal/checker/grammarchecks.go:998-1136
    internal bool CheckGrammarObjectLiteralExpression(ObjectLiteralExpression node, bool inDestructuring)
    {
        var seen = new Dictionary<string, DeclarationMeaning>();

        var properties = node.Properties?.Nodes;
        if (properties is not null)
        {
            foreach (var prop in properties)
            {
                if (prop.Kind == Kind.SpreadAssignment)
                {
                    var spreadAssignment = prop.AsSpreadAssignment()!;
                    if (inDestructuring)
                    {
                        // a rest property cannot be destructured any further
                        var expression = SkipParentheses(spreadAssignment.Expression);
                        if (IsArrayLiteralExpression(expression!) || IsObjectLiteralExpression(expression!))
                        {
                            return GrammarErrorOnNode(spreadAssignment.Expression, A_rest_element_cannot_contain_a_binding_pattern);
                        }
                    }

                    continue;
                }

                var name = prop.Name()!;
                if (name.Kind == Kind.ComputedPropertyName)
                {
                    // If the name is not a ComputedPropertyName, the grammar checking will skip it
                    CheckGrammarComputedPropertyName(name);
                }

                if (prop.Kind == Kind.ShorthandPropertyAssignment && !inDestructuring)
                {
                    var shorthandProp = prop.AsShorthandPropertyAssignment()!;
                    if (shorthandProp.ObjectAssignmentInitializer is not null)
                    {
                        // having objectAssignmentInitializer is only valid in an ObjectAssignmentPattern.
                        // Outside of destructuring, it is a syntax error.

                        // Try to grab the last node prior to the initializer,
                        // then error on the first token following (which should be the `=` token).
                        Node? lastNodeBeforeInitializer = null;
                        shorthandProp.ForEachChild(child =>
                        {
                            if (child != shorthandProp.ObjectAssignmentInitializer)
                            {
                                lastNodeBeforeInitializer = child;
                                return false;
                            }

                            return true;
                        });

                        GrammarErrorOnFirstToken(lastNodeBeforeInitializer, Did_you_mean_to_use_a_Colon_An_can_only_follow_a_property_name_when_the_containing_object_literal_is_part_of_a_destructuring_pattern);
                    }
                }

                if (name.Kind == Kind.PrivateIdentifier)
                {
                    GrammarErrorOnNode(name, Private_identifiers_are_not_allowed_outside_class_bodies);
                }

                // Modifiers are never allowed on properties except for 'async' on a method declaration
                var modifiers = prop.ModifierNodes();
                if (modifiers is not null && modifiers.Length != 0)
                {
                    if (CanHaveModifiers(prop))
                    {
                        foreach (var mod in modifiers)
                        {
                            if (IsModifier(mod) && (mod.Kind != Kind.AsyncKeyword || prop.Kind != Kind.MethodDeclaration))
                            {
                                GrammarErrorOnNode(mod, X_0_modifier_cannot_be_used_here, GetTextOfNode(mod));
                            }
                        }
                    }
                    else if (CanHaveIllegalModifiers(prop))
                    {
                        foreach (var mod in modifiers)
                        {
                            if (IsModifier(mod))
                            {
                                GrammarErrorOnNode(mod, X_0_modifier_cannot_be_used_here, GetTextOfNode(mod));
                            }
                        }
                    }
                }

                // ECMA-262 11.1.5 Object Initializer
                // If previous is not undefined then throw a SyntaxError exception if any of the following conditions are true
                // a.This production is contained in strict code and IsDataDescriptor(previous) is true and
                // IsDataDescriptor(propId.descriptor) is true.
                //    b.IsDataDescriptor(previous) is true and IsAccessorDescriptor(propId.descriptor) is true.
                //    c.IsAccessorDescriptor(previous) is true and IsDataDescriptor(propId.descriptor) is true.
                //    d.IsAccessorDescriptor(previous) is true and IsAccessorDescriptor(propId.descriptor) is true
                // and either both previous and propId.descriptor have[[Get]] fields or both previous and propId.descriptor have[[Set]] fields
                DeclarationMeaning currentKind;
                switch (prop.Kind)
                {
                    case Kind.ShorthandPropertyAssignment:
                    case Kind.PropertyAssignment:
                    {
                        INamedMemberBase commonProp;
                        if (prop.Kind == Kind.ShorthandPropertyAssignment)
                        {
                            prop.ClassLikeData();
                            commonProp = prop.AsShorthandPropertyAssignment()!;
                        }
                        else
                        {
                            commonProp = prop.AsPropertyAssignment()!;
                        }

                        // Grammar checking for computedPropertyName and shorthandPropertyAssignment
                        CheckGrammarForInvalidExclamationToken(commonProp.PostfixToken, A_definite_assignment_assertion_is_not_permitted_in_this_context);
                        CheckGrammarForInvalidQuestionMark(commonProp.PostfixToken, An_object_member_cannot_be_declared_optional);

                        if (name.Kind == Kind.NumericLiteral)
                        {
                            CheckGrammarNumericLiteral(name.AsNumericLiteral()!);
                        }

                        if (name.Kind == Kind.BigIntLiteral)
                        {
                            AddErrorOrSuggestion(true, CreateDiagnosticForNode(name, A_bigint_literal_cannot_be_used_as_a_property_name));
                        }

                        currentKind = DeclarationMeaning.PropertyAssignment;

                        break;
                    }
                    case Kind.MethodDeclaration:
                        currentKind = DeclarationMeaning.Method;
                        break;
                    case Kind.GetAccessor:
                        currentKind = DeclarationMeaning.GetAccessor;
                        break;
                    case Kind.SetAccessor:
                        currentKind = DeclarationMeaning.SetAccessor;
                        break;
                    default:
                        Debug.Fail($"Unexpected node kind \"{prop.Kind.String()}\"");
                        currentKind = DeclarationMeaning.PropertyAssignment;

                        break;
                }

                if (!inDestructuring)
                {
                    if (!TryGetEffectivePropertyNameForPropertyNameNode(name, out var effectiveName))
                    {
                        continue;
                    }

                    seen.TryGetValue(effectiveName, out var existingKind);
                    if (existingKind == 0)
                    {
                        seen[effectiveName] = currentKind;
                    }
                    else
                    {
                        if ((currentKind & DeclarationMeaning.Method) != 0 && (existingKind & DeclarationMeaning.Method) != 0)
                        {
                            GrammarErrorOnNode(name, Duplicate_identifier_0, GetTextOfNode(name));
                        }
                        else if ((currentKind & DeclarationMeaning.PropertyAssignment) != 0 && (existingKind & DeclarationMeaning.PropertyAssignment) != 0)
                        {
                            GrammarErrorOnNode(name, An_object_literal_cannot_have_multiple_properties_with_the_same_name, GetTextOfNode(name));
                        }
                        else if ((currentKind & DeclarationMeaning.GetOrSetAccessor) != 0 && (existingKind & DeclarationMeaning.GetOrSetAccessor) != 0)
                        {
                            if (existingKind != DeclarationMeaning.GetOrSetAccessor && currentKind != existingKind)
                            {
                                seen[effectiveName] = currentKind | existingKind;
                            }
                            else
                            {
                                return GrammarErrorOnNode(name, An_object_literal_cannot_have_multiple_get_Slashset_accessors_with_the_same_name);
                            }
                        }
                        else
                        {
                            return GrammarErrorOnNode(name, An_object_literal_cannot_have_property_and_accessor_with_the_same_name);
                        }
                    }
                }
            }
        }

        return false;
    }

    // ported from: internal/checker/grammarchecks.go:1138-1160
    internal bool CheckGrammarJsxElement(Node node)
    {
        CheckGrammarJsxName(node.TagName()!);
        CheckGrammarTypeArguments(node, node.TypeArgumentList());
        var seen = new Set<string>();
        if (node.Attributes()?.Properties() is not { } attributes)
        {
            return false;
        }

        foreach (var attrNode in attributes)
        {
            if (attrNode.Kind == Kind.JsxSpreadAttribute)
            {
                continue;
            }

            var attr = attrNode.AsJsxAttribute()!;
            var name = attr.Name()!;
            var initializer = attr.Initializer;
            var textOfName = name.Text();
            if (!seen.Has(textOfName))
            {
                seen.Add(textOfName);
            }
            else
            {
                return GrammarErrorOnNode(name, JSX_elements_cannot_have_multiple_attributes_with_the_same_name);
            }

            if (initializer is not null && initializer.Kind == Kind.JsxExpression && initializer.Expression() is null)
            {
                return GrammarErrorOnNode(initializer, JSX_attributes_must_only_be_assigned_a_non_empty_expression);
            }
        }

        return false;
    }

    // ported from: internal/checker/grammarchecks.go:1162-1172
    internal bool CheckGrammarJsxName(Node node)
    {
        if (IsPropertyAccessExpression(node) && IsJsxNamespacedName(node.Expression()!))
        {
            return GrammarErrorOnNode(node.Expression(), JSX_property_access_expressions_cannot_include_JSX_namespace_names);
        }

        if (IsJsxNamespacedName(node) && _compilerOptions.GetJSXTransformEnabled() && !IsIntrinsicJsxName(node.AsJsxNamespacedName()!.Namespace!.Text()))
        {
            return GrammarErrorOnNode(node, React_components_cannot_include_JSX_namespace_names);
        }

        return false;
    }

    // ported from: internal/checker/grammarchecks.go:1174-1180
    internal bool CheckGrammarJsxExpression(JsxExpression node)
    {
        if (node.Expression is not null && IsCommaSequence(node.Expression))
        {
            return GrammarErrorOnNode(node.Expression, JSX_expressions_may_not_use_the_comma_operator_Did_you_mean_to_write_an_array);
        }

        return false;
    }

    // ported from: internal/checker/grammarchecks.go:1182-1287
    internal bool CheckGrammarForInOrForOfStatement(Node forInOrOfStatement)
    {
        var asNode = forInOrOfStatement.AsNode();
        if (CheckGrammarStatementInAmbientContext(asNode))
        {
            return true;
        }

        var data = forInOrOfStatement.AsForInOrOfStatement()!;
        if (forInOrOfStatement.Kind == Kind.ForOfStatement && data.AwaitModifier is not null)
        {
            if ((forInOrOfStatement.Flags & NodeFlags.AwaitContext) == 0)
            {
                var sourceFile = GetSourceFileOfNode(asNode)!;
                if (IsInTopLevelContext(asNode))
                {
                    if (!HasParseDiagnostics(sourceFile))
                    {
                        if (!IsEffectiveExternalModule(sourceFile, _compilerOptions))
                        {
                            AddDiagnostic(CreateDiagnosticForNode(data.AwaitModifier, X_for_await_loops_are_only_allowed_at_the_top_level_of_a_file_when_that_file_is_a_module_but_this_file_has_no_imports_or_exports_Consider_adding_an_empty_export_to_make_this_file_a_module));
                        }

                        switch (_moduleKind)
                        {
                            case ModuleKind.Node16:
                            case ModuleKind.Node18:
                            case ModuleKind.Node20:
                            case ModuleKind.NodeNext:
                            {
                                var sourceFileMetaData = _program.GetSourceFileMetaData(sourceFile.Path());
                                if (sourceFileMetaData.ImpliedNodeFormat == ModuleKind.CommonJS)
                                {
                                    AddDiagnostic(CreateDiagnosticForNode(data.AwaitModifier, The_current_file_is_a_CommonJS_module_and_cannot_use_await_at_the_top_level));
                                    break;
                                }

                                goto case ModuleKind.ES2022;
                            }
                            case ModuleKind.ES2022:
                            case ModuleKind.ESNext:
                            case ModuleKind.Preserve:
                            case ModuleKind.System:
                                if (_languageVersion >= ScriptTarget.ES2017)
                                {
                                    break;
                                }

                                goto default;
                            default:
                                AddDiagnostic(CreateDiagnosticForNode(data.AwaitModifier, Top_level_for_await_loops_are_only_allowed_when_the_module_option_is_set_to_es2022_esnext_system_node16_node18_node20_nodenext_or_preserve_and_the_target_option_is_set_to_es2017_or_higher));
                                break;
                        }
                    }
                }
                else
                {
                    // use of 'for-await-of' in non-async function
                    if (!HasParseDiagnostics(sourceFile))
                    {
                        var diagnostic = CreateDiagnosticForNode(data.AwaitModifier, X_for_await_loops_are_only_allowed_within_async_functions_and_at_the_top_levels_of_modules);
                        var containingFunc = GetContainingFunction(asNode);
                        if (containingFunc is not null && containingFunc.Kind != Kind.Constructor)
                        {
                            Debug.Assert((GetFunctionFlags(containingFunc) & FunctionFlags.Async) == 0, "Enclosing function should never be an async function.");
                            var relatedInfo = CreateDiagnosticForNode(containingFunc, Did_you_mean_to_mark_this_function_as_async);
                            diagnostic.AddRelatedInfo(relatedInfo);
                        }

                        AddDiagnostic(diagnostic);
                        return true;
                    }
                }
            }
        }

        if (IsForOfStatement(asNode) && (forInOrOfStatement.Flags & NodeFlags.AwaitContext) == 0 && IsIdentifier(data.Initializer!) && data.Initializer!.Text() == "async")
        {
            GrammarErrorOnNode(data.Initializer, The_left_hand_side_of_a_for_of_statement_may_not_be_async);
            return false;
        }

        if (data.Initializer!.Kind == Kind.VariableDeclarationList)
        {
            var variableList = data.Initializer.AsVariableDeclarationList()!;
            if (!CheckGrammarVariableDeclarationList(variableList))
            {
                var declarations = variableList.Declarations;

                // declarations.length can be zero if there is an error in variable declaration in for-of or for-in
                // See http://www.ecma-international.org/ecma-262/6.0/#sec-for-in-and-for-of-statements for details
                // For example:
                //      var let = 10;
                //      for (let of [1,2,3]) {} // this is invalid ES6 syntax
                //      for (let in [1,2,3]) {} // this is invalid ES6 syntax
                // We will then want to skip on grammar checking on variableList declaration
                var declarationNodes = declarations?.Nodes ?? [];
                if (declarationNodes.Length == 0)
                {
                    return false;
                }

                if (declarationNodes.Length > 1)
                {
                    Message diagnostic;
                    if (forInOrOfStatement.Kind == Kind.ForInStatement)
                    {
                        diagnostic = Only_a_single_variable_declaration_is_allowed_in_a_for_in_statement;
                    }
                    else
                    {
                        diagnostic = Only_a_single_variable_declaration_is_allowed_in_a_for_of_statement;
                    }

                    return GrammarErrorOnFirstToken(declarationNodes[1], diagnostic);
                }

                var firstVariableDeclaration = declarationNodes[0].AsVariableDeclaration()!;
                if (firstVariableDeclaration.Initializer is not null)
                {
                    Message diagnostic;
                    if (forInOrOfStatement.Kind == Kind.ForInStatement)
                    {
                        diagnostic = The_variable_declaration_of_a_for_in_statement_cannot_have_an_initializer;
                    }
                    else
                    {
                        diagnostic = The_variable_declaration_of_a_for_of_statement_cannot_have_an_initializer;
                    }

                    return GrammarErrorOnNode(firstVariableDeclaration.Name(), diagnostic);
                }

                if (firstVariableDeclaration.Type is not null)
                {
                    Message diagnostic;
                    if (forInOrOfStatement.Kind == Kind.ForInStatement)
                    {
                        diagnostic = The_left_hand_side_of_a_for_in_statement_cannot_use_a_type_annotation;
                    }
                    else
                    {
                        diagnostic = The_left_hand_side_of_a_for_of_statement_cannot_use_a_type_annotation;
                    }

                    return GrammarErrorOnNode(firstVariableDeclaration, diagnostic);
                }
            }
        }

        return false;
    }

    // ported from: internal/checker/grammarchecks.go:1289-1343
    internal bool CheckGrammarAccessor(Node accessor)
    {
        var body = accessor.Body();
        if ((accessor.Flags & NodeFlags.Ambient) == 0 && accessor.Parent!.Kind != Kind.TypeLiteral && accessor.Parent.Kind != Kind.InterfaceDeclaration)
        {
            if (body is null && !HasSyntacticModifier(accessor, ModifierFlags.Abstract))
            {
                return GrammarErrorAtPos(accessor, accessor.End() - 1, ";".Length, X_0_expected, "{");
            }
        }

        if (body is not null)
        {
            if (HasSyntacticModifier(accessor, ModifierFlags.Abstract))
            {
                return GrammarErrorOnNode(accessor, An_abstract_accessor_cannot_have_an_implementation);
            }

            if (accessor.Parent!.Kind == Kind.TypeLiteral || accessor.Parent.Kind == Kind.InterfaceDeclaration)
            {
                return GrammarErrorOnNode(body, An_implementation_cannot_be_declared_in_ambient_contexts);
            }
        }

        var funcData = accessor.FunctionLikeData();
        NodeList? typeParameters = null;
        if (funcData is not null)
        {
            typeParameters = funcData.TypeParameters;
        }

        if (typeParameters is not null)
        {
            return GrammarErrorOnNode(accessor.Name(), An_accessor_cannot_have_type_parameters);
        }

        if (!DoesAccessorHaveCorrectParameterCount(accessor))
        {
            return GrammarErrorOnNode(accessor.Name(), IfElse(accessor.Kind == Kind.GetAccessor, A_get_accessor_cannot_have_parameters, A_set_accessor_must_have_exactly_one_parameter));
        }

        if (accessor.Kind == Kind.SetAccessor)
        {
            if (funcData!.Type is not null)
            {
                return GrammarErrorOnNode(accessor.Name(), A_set_accessor_cannot_have_a_return_type_annotation);
            }

            var parameterNode = GetSetAccessorValueParameter(accessor);
            if (parameterNode is null)
            {
                Debug.Fail("Return value does not match parameter count assertion.");
            }

            var parameter = parameterNode!.AsParameterDeclaration()!;
            if (parameter.DotDotDotToken is not null)
            {
                return GrammarErrorOnNode(parameter.DotDotDotToken, A_set_accessor_cannot_have_rest_parameter);
            }

            if (parameter.QuestionToken is not null)
            {
                return GrammarErrorOnNode(parameter.QuestionToken, A_set_accessor_cannot_have_an_optional_parameter);
            }

            if (parameter.Initializer is not null)
            {
                return GrammarErrorOnNode(accessor.Name(), A_set_accessor_parameter_cannot_have_an_initializer);
            }
        }

        return false;
    }

    // ported from: internal/checker/grammarchecks.go:1345-1349
    //
    // Does the accessor have the right number of parameters?
    //
    //	A `get` accessor has no parameters or a single `this` parameter.
    //	A `set` accessor has one parameter or a `this` parameter and one more parameter.
    internal bool DoesAccessorHaveCorrectParameterCount(Node accessor)
    {
        // `getAccessorThisParameter` returns `null` if the accessor's arity is incorrect,
        // even if there is a `this` parameter declared.
        return GetAccessorThisParameter(accessor) is not null || (accessor.Parameters()?.Length ?? 0) == IfElse(accessor.Kind == Kind.GetAccessor, 0, 1);
    }

    // ported from: internal/checker/grammarchecks.go:1351-1389
    internal bool CheckGrammarTypeOperatorNode(TypeOperatorNode node)
    {
        if (node.Operator == Kind.UniqueKeyword)
        {
            var innerType = node.Type!;
            if (innerType.Kind != Kind.SymbolKeyword)
            {
                return GrammarErrorOnNode(innerType, X_0_expected, TokenToString(Kind.SymbolKeyword));
            }

            var parent = WalkUpParenthesizedTypes(node.Parent);
            switch (parent!.Kind)
            {
                case Kind.VariableDeclaration:
                {
                    var decl = parent.AsVariableDeclaration()!;
                    if (decl.Name()!.Kind != Kind.Identifier)
                    {
                        return GrammarErrorOnNode(node, X_unique_symbol_types_may_not_be_used_on_a_variable_declaration_with_a_binding_name);
                    }

                    if (!IsVariableDeclarationInVariableStatement(decl))
                    {
                        return GrammarErrorOnNode(node, X_unique_symbol_types_are_only_allowed_on_variables_in_a_variable_statement);
                    }

                    if ((decl.Parent!.Flags & NodeFlags.Const) == 0)
                    {
                        return GrammarErrorOnNode(parent.AsVariableDeclaration()!.Name(), A_variable_whose_type_is_a_unique_symbol_type_must_be_const);
                    }

                    break;
                }
                case Kind.PropertyDeclaration:
                    if (!IsStatic(parent) || !HasReadonlyModifier(parent))
                    {
                        return GrammarErrorOnNode(parent.AsPropertyDeclaration()!.Name(), A_property_of_a_class_whose_type_is_a_unique_symbol_type_must_be_both_static_and_readonly);
                    }

                    break;
                case Kind.PropertySignature:
                    if (!HasSyntacticModifier(parent, ModifierFlags.Readonly))
                    {
                        return GrammarErrorOnNode(parent.AsPropertySignatureDeclaration()!.Name(), A_property_of_an_interface_or_type_literal_whose_type_is_a_unique_symbol_type_must_be_readonly);
                    }

                    break;
                default:
                    return GrammarErrorOnNode(node, X_unique_symbol_types_are_not_allowed_here);
            }
        }
        else if (node.Operator == Kind.ReadonlyKeyword)
        {
            var innerType = node.Type!;
            if (innerType.Kind != Kind.ArrayType && innerType.Kind != Kind.TupleType)
            {
                return GrammarErrorOnFirstToken(node, X_readonly_type_modifier_is_only_permitted_on_array_and_tuple_literal_types, TokenToString(Kind.SymbolKeyword));
            }
        }

        return false;
    }

    // ported from: internal/checker/grammarchecks.go:1391-1408
    internal bool CheckGrammarForInvalidDynamicName(Node node, Message message)
    {
        if (!IsNonBindableDynamicName(node))
        {
            return false;
        }

        Node expression;
        if (IsElementAccessExpression(node))
        {
            expression = SkipParentheses(node.AsElementAccessExpression()!.ArgumentExpression)!;
        }
        else
        {
            expression = node.Expression()!;
        }

        if (!IsEntityNameExpression(expression))
        {
            return GrammarErrorOnNode(node, message);
        }

        return false;
    }

    // ported from: internal/checker/grammarchecks.go:1410-1412
    //
    // Indicates whether a declaration name is a dynamic name that cannot be late-bound.
    internal bool IsNonBindableDynamicName(Node node)
    {
        return IsDynamicName(node) && !IsLateBindableName(node);
    }

    // ported from: internal/checker/grammarchecks.go:1414-1460
    internal bool CheckGrammarMethod(Node node)
    {
        if (CheckGrammarFunctionLikeDeclaration(node))
        {
            return true;
        }

        if (node.Kind == Kind.MethodDeclaration)
        {
            if (node.Parent!.Kind == Kind.ObjectLiteralExpression)
            {
                // We only disallow modifier on a method declaration if it is a property of object-literal-expression
                var modifiers = node.Modifiers();
                if (modifiers is not null && !((modifiers.Nodes?.Length ?? 0) == 1 && modifiers.Nodes![0].Kind == Kind.AsyncKeyword))
                {
                    return GrammarErrorOnFirstToken(node, Modifiers_cannot_appear_here);
                }

                var methodDecl = node.AsMethodDeclaration()!;
                if (CheckGrammarForInvalidQuestionMark(methodDecl.PostfixToken, An_object_member_cannot_be_declared_optional))
                {
                    return true;
                }

                if (CheckGrammarForInvalidExclamationToken(methodDecl.PostfixToken, A_definite_assignment_assertion_is_not_permitted_in_this_context))
                {
                    return true;
                }

                if (node.Body() is null)
                {
                    return GrammarErrorAtPos(node, node.End() - 1, ";".Length, X_0_expected, "{");
                }
            }

            if (CheckGrammarForGenerator(node))
            {
                return true;
            }
        }

        if (IsClassLike(node.Parent))
        {
            // Technically, computed properties in ambient contexts is disallowed
            // for property declarations and accessors too, not just methods.
            // However, property declarations disallow computed names in general,
            // and accessors are not allowed in ambient contexts in general,
            // so this error only really matters for methods.
            if ((node.Flags & NodeFlags.Ambient) != 0)
            {
                return CheckGrammarForInvalidDynamicName(node.Name()!, A_computed_property_name_in_an_ambient_context_must_refer_to_an_expression_whose_type_is_a_literal_type_or_a_unique_symbol_type);
            }
            else if (node.Kind == Kind.MethodDeclaration && node.Body() is null)
            {
                return CheckGrammarForInvalidDynamicName(node.Name()!, A_computed_property_name_in_a_method_overload_must_refer_to_an_expression_whose_type_is_a_literal_type_or_a_unique_symbol_type);
            }
        }
        else if (node.Parent!.Kind == Kind.InterfaceDeclaration)
        {
            return CheckGrammarForInvalidDynamicName(node.Name()!, A_computed_property_name_in_an_interface_must_refer_to_an_expression_whose_type_is_a_literal_type_or_a_unique_symbol_type);
        }
        else if (node.Parent.Kind == Kind.TypeLiteral)
        {
            return CheckGrammarForInvalidDynamicName(node.Name()!, A_computed_property_name_in_a_type_literal_must_refer_to_an_expression_whose_type_is_a_literal_type_or_a_unique_symbol_type);
        }

        return false;
    }

    // ported from: internal/checker/grammarchecks.go:1462-1516
    internal bool CheckGrammarBreakOrContinueStatement(Node node)
    {
        var targetLabel = node.Label();
        Node? current = node;
        while (current is not null)
        {
            if (IsFunctionLikeOrClassStaticBlockDeclaration(current))
            {
                return GrammarErrorOnNode(node, Jump_target_cannot_cross_function_boundary);
            }

            switch (current.Kind)
            {
                case Kind.LabeledStatement:
                    if (targetLabel is not null && current.Label()!.Text() == targetLabel.Text())
                    {
                        // found matching label - verify that label usage is correct
                        // continue can only target labels that are on iteration statements
                        var isMisplacedContinueLabel = node.Kind == Kind.ContinueStatement && !IsIterationStatement(current.Statement(), true /*lookInLabeledStatements*/);

                        if (isMisplacedContinueLabel)
                        {
                            return GrammarErrorOnNode(node, A_continue_statement_can_only_jump_to_a_label_of_an_enclosing_iteration_statement);
                        }

                        return false;
                    }

                    break;
                case Kind.SwitchStatement:
                    if (node.Kind == Kind.BreakStatement && targetLabel is null)
                    {
                        // unlabeled break within switch statement - ok
                        return false;
                    }

                    break;
                default:
                    if (IsIterationStatement(current, false /*lookInLabeledStatements*/) && targetLabel is null)
                    {
                        // unlabeled break or continue within iteration statement - ok
                        return false;
                    }

                    break;
            }

            current = current.Parent;
        }

        if (targetLabel is not null)
        {
            Message message;
            if (node.Kind == Kind.BreakStatement)
            {
                message = A_break_statement_can_only_jump_to_a_label_of_an_enclosing_statement;
            }
            else
            {
                message = A_continue_statement_can_only_jump_to_a_label_of_an_enclosing_iteration_statement;
            }

            return GrammarErrorOnNode(node, message);
        }
        else
        {
            Message message;
            if (node.Kind == Kind.BreakStatement)
            {
                message = A_break_statement_can_only_be_used_within_an_enclosing_iteration_or_switch_statement;
            }
            else
            {
                message = A_continue_statement_can_only_be_used_within_an_enclosing_iteration_statement;
            }

            return GrammarErrorOnNode(node, message);
        }
    }

    // ported from: internal/checker/grammarchecks.go:1518-1537
    internal bool CheckGrammarBindingElement(BindingElement node)
    {
        if (node.DotDotDotToken is not null)
        {
            var elements = node.Parent!.ElementList()!;
            if (node != LastOrNil(elements.Nodes))
            {
                return GrammarErrorOnNode(node, A_rest_element_must_be_last_in_a_destructuring_pattern);
            }

            CheckGrammarForDisallowedTrailingComma(elements, A_rest_parameter_or_binding_pattern_may_not_have_a_trailing_comma);

            if (node.PropertyName is not null)
            {
                return GrammarErrorOnNode(node.Name(), A_rest_element_cannot_have_a_property_name);
            }
        }

        if (node.DotDotDotToken is not null && node.Initializer is not null)
        {
            // Error on equals token which immediately precedes the initializer
            return GrammarErrorAtPos(node, node.Initializer.Pos() - 1, 1, A_rest_element_cannot_have_an_initializer);
        }

        return false;
    }

    // ported from: internal/checker/grammarchecks.go:1539-1594
    internal bool CheckGrammarVariableDeclaration(VariableDeclaration node)
    {
        var nodeFlags = GetCombinedNodeFlagsCached(node);
        var blockScopeKind = nodeFlags & NodeFlags.BlockScoped;
        if (IsBindingPattern(node.Name()))
        {
            switch (blockScopeKind)
            {
                case NodeFlags.AwaitUsing:
                    return GrammarErrorOnNode(node, X_0_declarations_may_not_have_binding_patterns, "await using");
                case NodeFlags.Using:
                    return GrammarErrorOnNode(node, X_0_declarations_may_not_have_binding_patterns, "using");
            }
        }

        if (node.Parent!.Parent!.Kind != Kind.ForInStatement && node.Parent.Parent.Kind != Kind.ForOfStatement)
        {
            if ((nodeFlags & NodeFlags.Ambient) != 0)
            {
                CheckAmbientInitializer(node);
            }
            else if (node.Initializer is null)
            {
                if (IsBindingPattern(node.Name()) && !IsBindingPattern(node.Parent))
                {
                    return GrammarErrorOnNode(node, A_destructuring_declaration_must_have_an_initializer);
                }

                switch (blockScopeKind)
                {
                    case NodeFlags.AwaitUsing:
                        return GrammarErrorOnNode(node, X_0_declarations_must_be_initialized, "await using");
                    case NodeFlags.Using:
                        return GrammarErrorOnNode(node, X_0_declarations_must_be_initialized, "using");
                    case NodeFlags.Const:
                        return GrammarErrorOnNode(node, X_0_declarations_must_be_initialized, "const");
                }
            }
        }

        if (node.ExclamationToken is not null && (node.Parent.Parent.Kind != Kind.VariableStatement || node.Type is null || node.Initializer is not null || (nodeFlags & NodeFlags.Ambient) != 0))
        {
            Message message;
            if (node.Initializer is not null)
            {
                message = Declarations_with_initializers_cannot_also_have_definite_assignment_assertions;
            }
            else if (node.Type is null)
            {
                message = Declarations_with_definite_assignment_assertions_must_also_have_type_annotations;
            }
            else
            {
                message = A_definite_assignment_assertion_is_not_permitted_in_this_context;
            }

            return GrammarErrorOnNode(node.ExclamationToken, message);
        }

        if (_program.GetEmitModuleFormatOfFile(GetSourceFileOfNode(node)!) < ModuleKind.System && (node.Parent.Parent.Flags & NodeFlags.Ambient) == 0 && HasSyntacticModifier(node.Parent.Parent, ModifierFlags.Export))
        {
            CheckGrammarForEsModuleMarkerInBindingName(node.Name()!);
        }

        // 1. LexicalDeclaration : LetOrConst BindingList ;
        // It is a Syntax Error if the BoundNames of BindingList contains "let".
        // 2. ForDeclaration: ForDeclaration : LetOrConst ForBinding
        // It is a Syntax Error if the BoundNames of ForDeclaration contains "let".

        // It is a SyntaxError if a VariableDeclaration or VariableDeclarationNoIn occurs within strict code
        // and its Identifier is eval or arguments
        return blockScopeKind != 0 && CheckGrammarNameInLetOrConstDeclarations(node.Name()!);
    }

    // ported from: internal/checker/grammarchecks.go:1596-1609
    internal bool CheckGrammarForEsModuleMarkerInBindingName(Node name)
    {
        if (IsIdentifier(name))
        {
            if (name.Text() == "__esModule")
            {
                return GrammarErrorOnNodeSkippedOnNoEmit(name, Identifier_expected_esModule_is_reserved_as_an_exported_marker_when_transforming_ECMAScript_modules);
            }
        }
        else
        {
            if (name.Elements() is { } elements)
            {
                foreach (var element in elements)
                {
                    if (element.Name() is not null)
                    {
                        return CheckGrammarForEsModuleMarkerInBindingName(element.Name()!);
                    }
                }
            }
        }

        return false;
    }

    // ported from: internal/checker/grammarchecks.go:1611-1626
    internal bool CheckGrammarNameInLetOrConstDeclarations(Node name)
    {
        if (name.Kind == Kind.Identifier)
        {
            if (name.Text() == "let")
            {
                return GrammarErrorOnNode(name, X_let_is_not_allowed_to_be_used_as_a_name_in_let_or_const_declarations);
            }
        }
        else
        {
            if (name.Elements() is { } elements)
            {
                foreach (var element in elements)
                {
                    var bindingElement = element.AsBindingElement()!;
                    if (bindingElement.Name() is not null)
                    {
                        CheckGrammarNameInLetOrConstDeclarations(bindingElement.Name()!);
                    }
                }
            }
        }

        return false;
    }

    // ported from: internal/checker/grammarchecks.go:1628-1656
    internal bool CheckGrammarVariableDeclarationList(VariableDeclarationList declarationList)
    {
        var declarations = declarationList.Declarations;
        if (CheckGrammarForDisallowedTrailingComma(declarations, Trailing_comma_not_allowed))
        {
            return true;
        }

        if ((declarations?.Nodes?.Length ?? 0) == 0)
        {
            return GrammarErrorAtPos(declarationList, declarations!.Pos(), declarations.End() - declarations.Pos(), Variable_declaration_list_cannot_be_empty);
        }

        var blockScopeFlags = declarationList.Flags & NodeFlags.BlockScoped;
        if (blockScopeFlags == NodeFlags.Using || blockScopeFlags == NodeFlags.AwaitUsing)
        {
            if (IsForInStatement(declarationList.Parent!))
            {
                return GrammarErrorOnNode(declarationList, IfElse(blockScopeFlags == NodeFlags.Using, The_left_hand_side_of_a_for_in_statement_cannot_be_a_using_declaration, The_left_hand_side_of_a_for_in_statement_cannot_be_an_await_using_declaration));
            }

            if ((declarationList.Flags & NodeFlags.Ambient) != 0)
            {
                return GrammarErrorOnNode(declarationList, IfElse(blockScopeFlags == NodeFlags.Using, X_using_declarations_are_not_allowed_in_ambient_contexts, X_await_using_declarations_are_not_allowed_in_ambient_contexts));
            }

            if (IsVariableStatement(declarationList.Parent!) && (IsCaseClause(declarationList.Parent!.Parent!) || IsDefaultClause(declarationList.Parent!.Parent!)))
            {
                return GrammarErrorOnNode(declarationList, IfElse(blockScopeFlags == NodeFlags.Using, X_using_declarations_are_not_allowed_in_case_or_default_clauses_unless_contained_within_a_block, X_await_using_declarations_are_not_allowed_in_case_or_default_clauses_unless_contained_within_a_block));
            }
        }

        if (blockScopeFlags == NodeFlags.AwaitUsing)
        {
            return CheckGrammarAwaitOrAwaitUsing(declarationList);
        }

        return false;
    }

    // ported from: internal/checker/grammarchecks.go:1658-1757
    internal bool CheckGrammarAwaitOrAwaitUsing(Node node)
    {
        // Grammar checking
        var hasError = false;
        var container = GetContainingFunctionOrClassStaticBlock(node);
        if (container is not null && IsClassStaticBlockDeclaration(container))
        {
            // NOTE: We report this regardless as to whether there are parse diagnostics.
            Message message;
            if (IsAwaitExpression(node))
            {
                message = X_await_expression_cannot_be_used_inside_a_class_static_block;
            }
            else
            {
                message = X_await_using_statements_cannot_be_used_inside_a_class_static_block;
            }

            Error(node, message);
            hasError = true;
        }
        else if ((node.Flags & NodeFlags.AwaitContext) == 0)
        {
            if (IsInTopLevelContext(node))
            {
                var sourceFile = GetSourceFileOfNode(node)!;
                if (!HasParseDiagnostics(sourceFile))
                {
                    var span = default(TextRange);
                    var spanCalculated = false;
                    if (!IsEffectiveExternalModule(sourceFile, _compilerOptions))
                    {
                        span = GetRangeOfTokenAtPosition(sourceFile, node.Pos());
                        spanCalculated = true;
                        Message message;
                        if (IsAwaitExpression(node))
                        {
                            message = X_await_expressions_are_only_allowed_at_the_top_level_of_a_file_when_that_file_is_a_module_but_this_file_has_no_imports_or_exports_Consider_adding_an_empty_export_to_make_this_file_a_module;
                        }
                        else
                        {
                            message = X_await_using_statements_are_only_allowed_at_the_top_level_of_a_file_when_that_file_is_a_module_but_this_file_has_no_imports_or_exports_Consider_adding_an_empty_export_to_make_this_file_a_module;
                        }

                        var diagnostic = NewDiagnostic(sourceFile, span, message);
                        AddDiagnostic(diagnostic);
                        hasError = true;
                    }

                    switch (_moduleKind)
                    {
                        case ModuleKind.Node16:
                        case ModuleKind.Node18:
                        case ModuleKind.Node20:
                        case ModuleKind.NodeNext:
                        {
                            var sourceFileMetaData = _program.GetSourceFileMetaData(sourceFile.Path());
                            if (sourceFileMetaData.ImpliedNodeFormat == ModuleKind.CommonJS)
                            {
                                if (!spanCalculated)
                                {
                                    span = GetRangeOfTokenAtPosition(sourceFile, node.Pos());
                                }

                                AddDiagnostic(NewDiagnostic(sourceFile, span, The_current_file_is_a_CommonJS_module_and_cannot_use_await_at_the_top_level));
                                hasError = true;
                                break;
                            }

                            goto case ModuleKind.ES2022;
                        }
                        case ModuleKind.ES2022:
                        case ModuleKind.ESNext:
                        case ModuleKind.Preserve:
                        case ModuleKind.System:
                            if (_languageVersion >= ScriptTarget.ES2017)
                            {
                                break;
                            }

                            goto default;
                        default:
                            if (!spanCalculated)
                            {
                                span = GetRangeOfTokenAtPosition(sourceFile, node.Pos());
                            }

                            Message topLevelMessage;
                            if (IsAwaitExpression(node))
                            {
                                topLevelMessage = Top_level_await_expressions_are_only_allowed_when_the_module_option_is_set_to_es2022_esnext_system_node16_node18_node20_nodenext_or_preserve_and_the_target_option_is_set_to_es2017_or_higher;
                            }
                            else
                            {
                                topLevelMessage = Top_level_await_using_statements_are_only_allowed_when_the_module_option_is_set_to_es2022_esnext_system_node16_node18_node20_nodenext_or_preserve_and_the_target_option_is_set_to_es2017_or_higher;
                            }

                            AddDiagnostic(NewDiagnostic(sourceFile, span, topLevelMessage));
                            hasError = true;
                            break;
                    }
                }
            }
            else
            {
                // use of 'await' in non-async function
                var containingSourceFile = GetSourceFileOfNode(node)!;
                if (!HasParseDiagnostics(containingSourceFile))
                {
                    var containingSpan = GetRangeOfTokenAtPosition(containingSourceFile, node.Pos());
                    Message containingMessage;
                    if (IsAwaitExpression(node))
                    {
                        containingMessage = X_await_expressions_are_only_allowed_within_async_functions_and_at_the_top_levels_of_modules;
                    }
                    else
                    {
                        containingMessage = X_await_using_statements_are_only_allowed_within_async_functions_and_at_the_top_levels_of_modules;
                    }

                    var diagnostic = NewDiagnostic(containingSourceFile, containingSpan, containingMessage);
                    if (container is not null && container.Kind != Kind.Constructor && !HasAsyncModifier(container))
                    {
                        var relatedInfo = NewDiagnosticForNode(container, Did_you_mean_to_mark_this_function_as_async);
                        diagnostic.AddRelatedInfo(relatedInfo);
                    }

                    AddDiagnostic(diagnostic);
                    hasError = true;
                }
            }
        }

        if (IsAwaitExpression(node) && IsInParameterInitializerBeforeContainingFunction(node))
        {
            // NOTE: We report this regardless as to whether there are parse diagnostics.
            Error(node, X_await_expressions_cannot_be_used_in_a_parameter_initializer);
            hasError = true;
        }

        return hasError;
    }
}
