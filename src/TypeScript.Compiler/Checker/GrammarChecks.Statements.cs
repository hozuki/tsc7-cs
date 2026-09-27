// ported from: internal/checker/grammarchecks.go:1759-2237（yield/块级变量语句/元属性/构造器类型参数与注解/属性/环境上下文
//   初始化器/顶层 declare 修饰符/环境上下文语句/数字与大整数字面量/导入子句与导入属性/动态 import 调用的文法检查）
//
// 与 Go 的差异（其余逐行对应；依据 docs/architecture.md §2/§3、docs/checker-plan.md §5）：
// - Go 的 `switch { case x: ...; default: panic }`（无 tag）→ C# 的 if/else 链 + `Debug.Fail`（`debug.Fail` 会抛，
//   其后为不可达；Go 的 panic 同理）。
// - `Debug.Fail(...)` 之后的局部变量需要显式赋值（C# 的确定赋值分析不认"抛异常"）。
// - `jsnum.FromString`/`jsnum.MaxSafeInteger` → `using static TypeScript.JsNum.JsNum;` 后的裸名。
// - `c.nodeLinks.Get(node)` → `_nodeLinks.Get(node)`（`LinkStore<Node, NodeLinks>`；Go 未导出字段 → `_camelCase`）。
// - `Node.ModifierFlags` 在 C# 是属性（不涉及本文件）。
namespace TypeScript.Checker;

using TypeScript.Ast;
using TypeScript.Core;
using TypeScript.Diagnostics;
using static TypeScript.Ast.Ast;
using static TypeScript.Core.Core;
using static TypeScript.Diagnostics.Diagnostics;
using static TypeScript.JsNum.JsNum;
using static TypeScript.Scanner.Scanner;

public sealed partial class Checker
{
    // ported from: internal/checker/grammarchecks.go:1759-1770
    internal bool CheckGrammarYieldExpression(Node node)
    {
        var hasError = false;
        if ((node.Flags & NodeFlags.YieldContext) == 0)
        {
            GrammarErrorOnFirstToken(node, A_yield_expression_is_only_allowed_in_a_generator_body);
            hasError = true;
        }

        if (IsInParameterInitializerBeforeContainingFunction(node))
        {
            Error(node, X_yield_expressions_cannot_be_used_in_a_parameter_initializer);
            hasError = true;
        }

        return hasError;
    }

    // ported from: internal/checker/grammarchecks.go:1772-1794
    internal bool CheckGrammarForDisallowedBlockScopedVariableStatement(VariableStatement node)
    {
        if (!ContainerAllowsBlockScopedVariable(node.Parent))
        {
            var blockScopeKind = GetCombinedNodeFlagsCached(node.DeclarationList!) & NodeFlags.BlockScoped;
            if (blockScopeKind != 0)
            {
                string keyword;
                if (blockScopeKind == NodeFlags.Let)
                {
                    keyword = "let";
                }
                else if (blockScopeKind == NodeFlags.Const)
                {
                    keyword = "const";
                }
                else if (blockScopeKind == NodeFlags.Using)
                {
                    keyword = "using";
                }
                else if (blockScopeKind == NodeFlags.AwaitUsing)
                {
                    keyword = "await using";
                }
                else
                {
                    Debug.Fail("Unknown BlockScope flag");
                    keyword = "";
                }

                Error(node, X_0_declarations_can_only_be_declared_inside_a_block, keyword);
            }
        }

        return false;
    }

    // ported from: internal/checker/grammarchecks.go:1796-1811
    internal bool ContainerAllowsBlockScopedVariable(Node? parent)
    {
        switch (parent!.Kind)
        {
            case Kind.IfStatement:
            case Kind.DoStatement:
            case Kind.WhileStatement:
            case Kind.WithStatement:
            case Kind.ForStatement:
            case Kind.ForInStatement:
            case Kind.ForOfStatement:
                return false;
            case Kind.LabeledStatement:
                return ContainerAllowsBlockScopedVariable(parent.Parent);
        }

        return true;
    }

    // ported from: internal/checker/grammarchecks.go:1813-1839
    internal bool CheckGrammarMetaProperty(MetaProperty node)
    {
        var nodeName = node.Name()!;
        var nameText = nodeName.Text();

        switch (node.KeywordToken)
        {
            case Kind.NewKeyword:
                if (nameText != "target")
                {
                    return GrammarErrorOnNode(nodeName, X_0_is_not_a_valid_meta_property_for_keyword_1_Did_you_mean_2, nameText, TokenToString(node.KeywordToken), "target");
                }

                break;
            case Kind.ImportKeyword:
                if (nameText != "meta")
                {
                    var isCallee = IsCallExpression(node.Parent!) && node.Parent!.Expression() == node.AsNode();
                    if (nameText == "defer")
                    {
                        if (!isCallee)
                        {
                            return GrammarErrorAtPos(node, node.End(), 0, X_0_expected, "(");
                        }
                    }
                    else
                    {
                        if (isCallee)
                        {
                            return GrammarErrorOnNode(nodeName, X_0_is_not_a_valid_meta_property_for_keyword_import_Did_you_mean_meta_or_defer, nameText);
                        }

                        return GrammarErrorOnNode(nodeName, X_0_is_not_a_valid_meta_property_for_keyword_1_Did_you_mean_2, nameText, TokenToString(node.KeywordToken), "meta");
                    }
                }

                break;
        }

        return false;
    }

    // ported from: internal/checker/grammarchecks.go:1841-1854
    internal bool CheckGrammarConstructorTypeParameters(ConstructorDeclaration node)
    {
        var typeParameters = node.TypeParameters;
        if (typeParameters is not null)
        {
            int pos;
            if (typeParameters.Pos() == typeParameters.End())
            {
                pos = typeParameters.Pos();
            }
            else
            {
                pos = SkipTrivia(GetSourceFileOfNode(node)!.Text(), typeParameters.Pos());
            }

            return GrammarErrorAtPos(node, pos, typeParameters.End() - pos, Type_parameters_cannot_appear_on_a_constructor_declaration);
        }

        return false;
    }

    // ported from: internal/checker/grammarchecks.go:1856-1862
    internal bool CheckGrammarConstructorTypeAnnotation(ConstructorDeclaration node)
    {
        var t = node.Type;
        if (t is not null)
        {
            return GrammarErrorOnNode(t, Type_annotation_cannot_appear_on_a_constructor_declaration);
        }

        return false;
    }

    // ported from: internal/checker/grammarchecks.go:1864-1923
    internal bool CheckGrammarProperty(Node node)
    {
        var propertyName = node.Name()!;
        if (IsComputedPropertyName(propertyName) && IsBinaryExpression(propertyName.Expression()!) && propertyName.Expression()!.AsBinaryExpression()!.OperatorToken!.Kind == Kind.InKeyword)
        {
            return GrammarErrorOnNode(node.Parent!.Members()![0], A_mapped_type_may_not_declare_properties_or_methods);
        }

        if (IsClassLike(node.Parent))
        {
            if (IsStringLiteral(propertyName) && propertyName.Text() == "constructor")
            {
                return GrammarErrorOnNode(propertyName, Classes_may_not_have_a_field_named_constructor);
            }

            if (CheckGrammarForInvalidDynamicName(propertyName, A_computed_property_name_in_a_class_property_declaration_must_have_a_simple_literal_type_or_a_unique_symbol_type))
            {
                return true;
            }

            if (IsAutoAccessorPropertyDeclaration(node) && CheckGrammarForInvalidQuestionMark(node.PostfixToken(), An_accessor_property_cannot_be_declared_optional))
            {
                return true;
            }
        }
        else if (IsInterfaceDeclaration(node.Parent!))
        {
            if (CheckGrammarForInvalidDynamicName(propertyName, A_computed_property_name_in_an_interface_must_refer_to_an_expression_whose_type_is_a_literal_type_or_a_unique_symbol_type))
            {
                return true;
            }

            if (!IsPropertySignatureDeclaration(node))
            {
                // Interfaces cannot contain property declarations
                Debug.Fail($"Unexpected node kind \"{node.Kind.String()}\"");
            }

            var initializer = node.Initializer();
            if (initializer is not null)
            {
                return GrammarErrorOnNode(initializer, An_interface_property_cannot_have_an_initializer);
            }
        }
        else if (IsTypeLiteralNode(node.Parent!))
        {
            if (CheckGrammarForInvalidDynamicName(node.Name()!, A_computed_property_name_in_a_type_literal_must_refer_to_an_expression_whose_type_is_a_literal_type_or_a_unique_symbol_type))
            {
                return true;
            }

            if (!IsPropertySignatureDeclaration(node))
            {
                // Type literals cannot contain property declarations
                Debug.Fail($"Unexpected node kind \"{node.Kind.String()}\"");
            }

            var initializer = node.Initializer();
            if (initializer is not null)
            {
                return GrammarErrorOnNode(initializer, A_type_literal_property_cannot_have_an_initializer);
            }
        }

        if ((node.Flags & NodeFlags.Ambient) != 0)
        {
            CheckAmbientInitializer(node);
        }

        if (IsPropertyDeclaration(node))
        {
            var propDecl = node.AsPropertyDeclaration()!;
            var postfixToken = propDecl.PostfixToken;
            if (postfixToken is not null && postfixToken.Kind == Kind.ExclamationToken)
            {
                if (propDecl.Initializer is not null)
                {
                    return GrammarErrorOnNode(postfixToken, Declarations_with_initializers_cannot_also_have_definite_assignment_assertions);
                }
                else if (propDecl.Type is null)
                {
                    return GrammarErrorOnNode(postfixToken, Declarations_with_definite_assignment_assertions_must_also_have_type_annotations);
                }
                else if (!IsClassLike(node.Parent) || (node.Flags & NodeFlags.Ambient) != 0 || IsStatic(node) || HasAbstractModifier(node))
                {
                    return GrammarErrorOnNode(postfixToken, A_definite_assignment_assertion_is_not_permitted_in_this_context);
                }
            }
        }

        return false;
    }

    // ported from: internal/checker/grammarchecks.go:1925-1958
    internal bool CheckAmbientInitializer(Node node)
    {
        Node? initializer;
        Node? typeNode;
        switch (node.Kind)
        {
            case Kind.VariableDeclaration:
            {
                var varDecl = node.AsVariableDeclaration()!;
                initializer = varDecl.Initializer;
                typeNode = varDecl.Type;

                break;
            }
            case Kind.PropertyDeclaration:
            {
                var propDecl = node.AsPropertyDeclaration()!;
                initializer = propDecl.Initializer;
                typeNode = propDecl.Type;

                break;
            }
            case Kind.PropertySignature:
            {
                var propSig = node.AsPropertySignatureDeclaration()!;
                initializer = propSig.Initializer;
                typeNode = propSig.Type;

                break;
            }
            default:
                Debug.Fail($"Unexpected node kind \"{node.Kind.String()}\"");
                initializer = null;
                typeNode = null;

                break;
        }

        if (initializer is not null)
        {
            var isInvalidInitializer = !(IsInitializerStringOrNumberLiteralExpression(initializer) || IsInitializerSimpleLiteralEnumReference(initializer) || initializer.Kind == Kind.TrueKeyword || initializer.Kind == Kind.FalseKeyword || IsInitializerBigIntLiteralExpression(initializer));
            var isConstOrReadonly = IsDeclarationReadonly(node) || (IsVariableDeclaration(node) && IsVarConstLike(node));
            if (isConstOrReadonly && typeNode is null)
            {
                if (isInvalidInitializer)
                {
                    return GrammarErrorOnNode(initializer, A_const_initializer_in_an_ambient_context_must_be_a_string_or_numeric_literal_or_literal_enum_reference);
                }
            }
            else
            {
                return GrammarErrorOnNode(initializer, Initializers_are_not_allowed_in_ambient_contexts);
            }
        }

        return false;
    }

    // ported from: internal/checker/grammarchecks.go:1960-1963（Go 包级函数）
    internal static bool IsInitializerStringOrNumberLiteralExpression(Node expr)
    {
        return IsStringOrNumericLiteralLike(expr) ||
            (expr.Kind == Kind.PrefixUnaryExpression && expr.AsPrefixUnaryExpression()!.Operator == Kind.MinusToken && expr.AsPrefixUnaryExpression()!.Operand!.Kind == Kind.NumericLiteral);
    }

    // ported from: internal/checker/grammarchecks.go:1965-1976（Go 包级函数）
    internal static bool IsInitializerBigIntLiteralExpression(Node expr)
    {
        if (expr.Kind == Kind.BigIntLiteral)
        {
            return true;
        }

        if (expr.Kind == Kind.PrefixUnaryExpression)
        {
            var unaryExpr = expr.AsPrefixUnaryExpression()!;
            return unaryExpr.Operator == Kind.MinusToken && unaryExpr.Operand!.Kind == Kind.BigIntLiteral;
        }

        return false;
    }

    // ported from: internal/checker/grammarchecks.go:1978-1992
    internal bool IsInitializerSimpleLiteralEnumReference(Node expr)
    {
        if (IsPropertyAccessExpression(expr))
        {
            return (CheckExpressionCached(expr)._flags & TypeFlags.EnumLike) != 0;
        }

        if (IsElementAccessExpression(expr))
        {
            var elementAccess = expr.AsElementAccessExpression()!;

            return IsInitializerStringOrNumberLiteralExpression(elementAccess.ArgumentExpression!) &&
                IsEntityNameExpression(elementAccess.Expression!) &&
                (CheckExpressionCached(expr)._flags & TypeFlags.EnumLike) != 0;
        }

        return false;
    }

    // ported from: internal/checker/grammarchecks.go:1994-2012
    internal bool CheckGrammarTopLevelElementForRequiredDeclareModifier(Node node)
    {
        // A declare modifier is required for any top level .d.ts declaration except export=, export default, export as namespace
        // interfaces and imports categories:
        //
        //  DeclarationElement:
        //     ExportAssignment
        //     export_opt   InterfaceDeclaration
        //     export_opt   TypeAliasDeclaration
        //     export_opt   ImportDeclaration
        //     export_opt   ExternalImportDeclaration
        //     export_opt   AmbientDeclaration
        //
        // TODO: The spec needs to be amended to reflect this grammar.
        if (node.Kind == Kind.InterfaceDeclaration || node.Kind == Kind.TypeAliasDeclaration || node.Kind == Kind.ImportDeclaration || node.Kind == Kind.JSImportDeclaration || node.Kind == Kind.ImportEqualsDeclaration || node.Kind == Kind.ExportDeclaration || node.Kind == Kind.ExportAssignment || node.Kind == Kind.NamespaceExportDeclaration || HasSyntacticModifier(node, ModifierFlags.Ambient | ModifierFlags.Export | ModifierFlags.Default))
        {
            return false;
        }

        return GrammarErrorOnFirstToken(node, Top_level_declarations_in_d_ts_files_must_start_with_either_a_declare_or_export_modifier);
    }

    // ported from: internal/checker/grammarchecks.go:2014-2023
    internal bool CheckGrammarTopLevelElementsForRequiredDeclareModifier(SourceFile file)
    {
        if (file.Statements?.Nodes is not { } statements)
        {
            return false;
        }

        foreach (var decl in statements)
        {
            if (IsDeclarationNode(decl) || decl.Kind == Kind.VariableStatement)
            {
                if (CheckGrammarTopLevelElementForRequiredDeclareModifier(decl))
                {
                    return true;
                }
            }
        }

        return false;
    }

    // ported from: internal/checker/grammarchecks.go:2025-2027
    internal bool CheckGrammarSourceFile(SourceFile node)
    {
        return (node.Flags & NodeFlags.Ambient) != 0 && CheckGrammarTopLevelElementsForRequiredDeclareModifier(node);
    }

    // ported from: internal/checker/grammarchecks.go:2029-2057
    internal bool CheckGrammarStatementInAmbientContext(Node node)
    {
        if ((node.Flags & NodeFlags.Ambient) != 0)
        {
            // Find containing block which is either Block, ModuleBlock, SourceFile
            var links = _nodeLinks.Get(node);
            if (!links._hasReportedStatementInAmbientContext && (IsFunctionLike(node.Parent) || IsAccessor(node.Parent)))
            {
                links._hasReportedStatementInAmbientContext = GrammarErrorOnFirstToken(node, An_implementation_cannot_be_declared_in_ambient_contexts);
                return links._hasReportedStatementInAmbientContext;
            }

            // We are either parented by another statement, or some sort of block.
            // If we're in a block, we only want to really report an error once
            // to prevent noisiness.  So use a bit on the block to indicate if
            // this has already been reported, and don't report if it has.
            //
            if (node.Parent!.Kind == Kind.Block || node.Parent.Kind == Kind.ModuleBlock || node.Parent.Kind == Kind.SourceFile)
            {
                var parentLinks = _nodeLinks.Get(node.Parent);
                // Check if the containing block ever report this error
                if (!parentLinks._hasReportedStatementInAmbientContext)
                {
                    parentLinks._hasReportedStatementInAmbientContext = GrammarErrorOnFirstToken(node, Statements_are_not_allowed_in_ambient_contexts);
                    return parentLinks._hasReportedStatementInAmbientContext;
                }
            }
            else
            {
                // We must be parented by a statement.  If so, there's no need
                // to report the error as our parent will have already done it.
                // debug.Assert(ast.IsStatement(node.Parent)) // !!! commented out in strada - fails if uncommented
            }
        }

        return false;
    }

    // ported from: internal/checker/grammarchecks.go:2059-2085
    internal void CheckGrammarNumericLiteral(NumericLiteral node)
    {
        var nodeText = GetTextOfNode(node);

        // Realism (size) checking
        // We should test against `getTextOfNode(node)` rather than `node.text`, because `node.text` for large numeric literals can contain "."
        // e.g. `node.text` for numeric literal `1100000000000000000000` is `1.1e21`.
        var isFractional = nodeText.Contains('.');
        var isScientific = (node.TokenFlags & TokenFlags.Scientific) != 0;

        // Scientific notation (e.g. 2e54 and 1e00000000010) can't be converted to bigint
        // Fractional numbers (e.g. 9000000000000000.001) are inherently imprecise anyway
        if (isFractional || isScientific)
        {
            return;
        }

        // Here `node` is guaranteed to be a numeric literal representing an integer.
        // We need to judge whether the integer `node` represents is <= 2 ** 53 - 1, which can be accomplished by comparing to `value` defined below because:
        // 1) when `node` represents an integer <= 2 ** 53 - 1, `node.text` is its exact string representation and thus `value` precisely represents the integer.
        // 2) otherwise, although `node.text` may be imprecise string representation, its mathematical value and consequently `value` cannot be less than 2 ** 53,
        //    thus the result of the predicate won't be affected.
        var value = FromString(node.Text);
        if (value <= MaxSafeInteger)
        {
            return;
        }

        AddErrorOrSuggestion(false, CreateDiagnosticForNode(node, Numeric_literals_with_absolute_values_equal_to_2_53_or_greater_are_too_large_to_be_represented_accurately_as_integers));
    }

    // ported from: internal/checker/grammarchecks.go:2087-2098
    internal bool CheckGrammarBigIntLiteral(BigIntLiteral node)
    {
        var literalType = IsLiteralTypeNode(node.Parent!) || (IsPrefixUnaryExpression(node.Parent!) && IsLiteralTypeNode(node.Parent!.Parent!));
        if (!literalType)
        {
            // Don't error on BigInt literals in ambient contexts
            if ((node.Flags & NodeFlags.Ambient) == 0 && _languageVersion < ScriptTarget.ES2020)
            {
                if (GrammarErrorOnNode(node, BigInt_literals_are_not_available_when_targeting_lower_than_ES2020))
                {
                    return true;
                }
            }
        }

        return false;
    }

    // ported from: internal/checker/grammarchecks.go:2100-2121
    internal bool CheckGrammarImportClause(ImportClause node)
    {
        switch (node.PhaseModifier)
        {
            case Kind.TypeKeyword:
                if ((node.Flags & NodeFlags.JSDoc) == 0 && node.Name() is not null && node.NamedBindings is not null)
                {
                    return GrammarErrorOnNode(node, A_type_only_import_can_specify_a_default_import_or_named_bindings_but_not_both);
                }

                if (node.NamedBindings is not null && node.NamedBindings.Kind == Kind.NamedImports)
                {
                    return CheckGrammarTypeOnlyNamedImportsOrExports(node.NamedBindings);
                }

                break;
            case Kind.DeferKeyword:
                if (node.Name() is not null)
                {
                    return GrammarErrorOnNode(node, Default_imports_are_not_allowed_in_a_deferred_import);
                }

                if (node.NamedBindings is not null && node.NamedBindings.Kind == Kind.NamedImports)
                {
                    return GrammarErrorOnNode(node, Named_imports_are_not_allowed_in_a_deferred_import);
                }

                if (_moduleKind != ModuleKind.ESNext && _moduleKind != ModuleKind.Preserve)
                {
                    return GrammarErrorOnNode(node, Deferred_imports_are_only_supported_when_the_module_flag_is_set_to_esnext_or_preserve);
                }

                break;
        }

        return false;
    }

    // ported from: internal/checker/grammarchecks.go:2123-2134
    internal bool CheckGrammarImportAttributeValues(ImportAttributes node)
    {
        var hasError = false;
        if (node.Attributes?.Nodes is { } attributes)
        {
            foreach (var attribute in attributes)
            {
                var value = attribute.AsImportAttribute()!.Value;
                if (IsStringLiteral(value!))
                {
                    continue;
                }

                hasError = true;
                Error(value, Import_attribute_values_must_be_string_literal_expressions);
            }
        }

        return hasError;
    }

    // ported from: internal/checker/grammarchecks.go:2136-2155
    internal bool CheckGrammarTypeOnlyNamedImportsOrExports(Node namedBindings)
    {
        var nodeList = namedBindings.ElementList()!;
        if (nodeList.Nodes is not { } specifiers)
        {
            return false;
        }

        foreach (var specifier in specifiers)
        {
            bool specifierIsTypeOnly;
            Message message;
            if (specifier.Kind == Kind.ImportSpecifier)
            {
                specifierIsTypeOnly = specifier.IsTypeOnly();
                message = The_type_modifier_cannot_be_used_on_a_named_import_when_import_type_is_used_on_its_import_statement;
            }
            else
            {
                specifierIsTypeOnly = specifier.IsTypeOnly();
                message = The_type_modifier_cannot_be_used_on_a_named_export_when_export_type_is_used_on_its_export_statement;
            }

            if (specifierIsTypeOnly)
            {
                return GrammarErrorOnFirstToken(specifier, message);
            }
        }

        return false;
    }

    // ported from: internal/checker/grammarchecks.go:2157-2198
    internal bool CheckGrammarImportCallExpression(Node node)
    {
        if (_compilerOptions.VerbatimModuleSyntax == Tristate.TSTrue && _moduleKind == ModuleKind.CommonJS)
        {
            return GrammarErrorOnNode(node, GetVerbatimModuleSyntaxErrorMessage(node));
        }

        if (node.Expression()!.Kind == Kind.MetaProperty)
        {
            if (_moduleKind != ModuleKind.ESNext && _moduleKind != ModuleKind.Preserve)
            {
                return GrammarErrorOnNode(node, Deferred_imports_are_only_supported_when_the_module_flag_is_set_to_esnext_or_preserve);
            }
        }
        else if (_moduleKind == ModuleKind.ES2015)
        {
            return GrammarErrorOnNode(node, Dynamic_imports_are_only_supported_when_the_module_flag_is_set_to_es2020_es2022_esnext_commonjs_amd_system_umd_node16_node18_node20_or_nodenext);
        }

        var nodeAsCall = node.AsCallExpression()!;
        if (nodeAsCall.TypeArguments is not null)
        {
            return GrammarErrorOnNode(node, This_use_of_import_is_invalid_import_calls_can_be_written_but_they_must_have_parentheses_and_cannot_have_type_arguments);
        }

        var nodeArguments = nodeAsCall.Arguments;
        // Go: len(argumentNodes)（nil slice 长度为 0）
        var argumentNodes = nodeArguments!.Nodes ?? [];
        if (!(ModuleKind.Node16 <= _moduleKind && _moduleKind <= ModuleKind.NodeNext) && _moduleKind != ModuleKind.ESNext && _moduleKind != ModuleKind.Preserve)
        {
            // We are allowed trailing comma after proposal-import-assertions.
            CheckGrammarForDisallowedTrailingComma(nodeArguments, Trailing_comma_not_allowed);

            if (argumentNodes.Length > 1)
            {
                var importAttributesArgument = argumentNodes[1];
                return GrammarErrorOnNode(importAttributesArgument, Dynamic_imports_only_support_a_second_argument_when_the_module_option_is_set_to_esnext_node16_node18_node20_nodenext_or_preserve);
            }
        }

        if (argumentNodes.Length == 0 || argumentNodes.Length > 2)
        {
            return GrammarErrorOnNode(node, Dynamic_imports_can_only_accept_a_module_specifier_and_an_optional_set_of_attributes_as_arguments);
        }

        // see: parseArgumentOrArrayLiteralElement...we use this function which parse arguments of callExpression to parse specifier for dynamic import.
        // parseArgumentOrArrayLiteralElement allows spread element to be in an argument list which is not allowed as specifier in dynamic import.
        var spreadElement = Find(argumentNodes, IsSpreadElement);
        if (spreadElement is not null)
        {
            return GrammarErrorOnNode(spreadElement, Argument_of_dynamic_import_cannot_be_spread_element);
        }

        return false;
    }

    // ported from: internal/checker/grammarchecks.go:2200-2237
    internal bool CheckGrammarImportAttributesType(TypeLiteralNode attributes)
    {
        var members = attributes.Members;
        if (members is null)
        {
            return false;
        }

        if (members.Nodes is { } memberNodes)
        {
            foreach (var member in memberNodes)
            {
                if (member.Kind != Kind.PropertySignature)
                {
                    return GrammarErrorOnNode(member, An_import_attributes_type_may_only_contain_property_signatures);
                }

                var propertySignature = member.AsPropertySignatureDeclaration()!;
                var modifiers = propertySignature.Modifiers();
                if (modifiers is not null && modifiers.Nodes is { } modifierNodes)
                {
                    foreach (var modifier in modifierNodes)
                    {
                        if (modifier.Kind == Kind.ReadonlyKeyword)
                        {
                            return GrammarErrorOnNode(modifier, An_import_attributes_property_cannot_have_a_readonly_modifier);
                        }
                    }
                }

                if (propertySignature.Type is null)
                {
                    return GrammarErrorOnNode(member, An_import_attributes_property_must_have_a_type_annotation);
                }

                if (propertySignature.QuestionToken() is not null)
                {
                    return GrammarErrorOnNode(member, An_import_attributes_property_cannot_be_optional);
                }

                var name = propertySignature.Name()!;
                if (!(IsStringLiteralLike(name) || IsIdentifier(name)))
                {
                    return GrammarErrorOnNode(name, An_import_attributes_property_must_have_a_string_literal_or_identifier_name);
                }

                if (name.Text() == "resolution-mode")
                {
                    return GrammarErrorOnNode(name, X_0_is_not_a_valid_key_for_an_import_attributes_type, name.Text());
                }

                var typeNode = propertySignature.Type;
                if (!IsStringLiteralLikeType(typeNode))
                {
                    return GrammarErrorOnNode(typeNode, An_import_attributes_property_must_have_a_string_literal_type_annotation);
                }
            }
        }

        return false;
    }
}
