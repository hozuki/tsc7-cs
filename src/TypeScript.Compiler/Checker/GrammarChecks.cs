// ported from: internal/checker/grammarchecks.go:1-997（文法/语义文法检查：grammarError* 家族、checkGrammarModifiers 与
//   修饰符/装饰器合法性、函数式/类声明、HeritageClause、参数表、index signature、类型实参、箭头函数、computed property
//   name、generator、?/! 后缀令牌）
//
// 与 Go 的差异（其余逐行对应；依据 docs/architecture.md §2/§3、docs/checker-plan.md §5）：
// - 类形状：`public sealed partial class Checker`（Binder/Parser 先例）。Go 的包级函数（无接收者）→ 同一个 partial 类的
//   `internal static` 成员（C# 不可能同时有静态类与同名实例类）。
// - Checker 的 Go 未导出字段 → `internal _camelCase`（docs/architecture.md §2）：`_legacyDecorators`、`_languageVersion`、
//   `_compilerOptions`、`_moduleKind`、`_program`、`_nodeLinks`、`_regExpScanner`。
// - `panic(...)` → `Debug.Fail(...)`（抛 DebugFailureException；`using System.Diagnostics;` 被禁止）。Go 的
//   `fmt.Sprintf("%q", kind)` panic 文本 → `$"...\"{kind.String()}\""`。
// - `Node.Modifiers() == nil` → `is null`；`len(x.Nodes) == 0` → `(x.Nodes?.Length ?? 0) == 0`（Go 的 nil slice 长度为 0）。
// - Go 的方法表达式/闭包参数遮蔽：`func(t *Type) bool` 与外层 `t` 同名，C# 不允许（CS0136）→ 形参改名。
// - `strings.ContainsFunc(arrowFullText, stringutil.IsLineBreak)` → 局部函数 `ContainsLineBreak`（span 遍历，零分配；
//   `strings.ContainsFunc` 按 rune 迭代，但 `IsLineBreak` 的四个目标字符都在 BMP，char 迭代等价）。
// - `s[from:to]` → `AsSpan(from, to - from)`（docs/architecture.md §3.8）。
namespace TypeScript.Checker;

using TypeScript.Ast;
using TypeScript.Binder;
using TypeScript.Core;
using TypeScript.Diagnostics;
using TypeScript.Scanner;
using static TypeScript.Ast.Ast;
using static TypeScript.Binder.Binder;
using static TypeScript.Core.Core;
using static TypeScript.Diagnostics.Diagnostics;
using static TypeScript.Scanner.Scanner;
using static TypeScript.StringUtil.StringUtil;
using static TypeScript.TSPath.TSPath;
// `Scanner` 这个名字在 `TypeScript.*` 命名空间里会先命中命名空间 `TypeScript.Scanner` 而不是类（与 Checker.Struct.cs:29 同一别名）。
using TsScanner = TypeScript.Scanner.Scanner;

public sealed partial class Checker
{
    // ported from: internal/checker/grammarchecks.go:19-24
    internal bool GrammarErrorOnFirstToken(Node? node, Message message, params object?[] args)
    {
        var sourceFile = GetSourceFileOfNode(node)!;
        if (!HasParseDiagnostics(sourceFile))
        {
            var span = GetRangeOfTokenAtPosition(sourceFile, node!.Pos());
            AddDiagnostic(NewDiagnostic(sourceFile, span, message, args));
            return true;
        }

        return false;
    }

    // ported from: internal/checker/grammarchecks.go:29-36
    internal bool GrammarErrorAtPos(Node? nodeForSourceFile, int start, int length, Message message, params object?[] args)
    {
        var sourceFile = GetSourceFileOfNode(nodeForSourceFile)!;
        if (!HasParseDiagnostics(sourceFile))
        {
            AddDiagnostic(NewDiagnostic(sourceFile, NewTextRange(start, start + length), message, args));
            return true;
        }

        return false;
    }

    // ported from: internal/checker/grammarchecks.go:38-45
    internal bool GrammarErrorOnNode(Node? node, Message message, params object?[] args)
    {
        var sourceFile = GetSourceFileOfNode(node)!;
        if (!HasParseDiagnostics(sourceFile))
        {
            Error(node, message, args);
            return true;
        }

        return false;
    }

    // ported from: internal/checker/grammarchecks.go:47-56
    internal bool GrammarErrorOnNodeSkippedOnNoEmit(Node? node, Message message, params object?[] args)
    {
        var sourceFile = GetSourceFileOfNode(node)!;
        if (!HasParseDiagnostics(sourceFile))
        {
            var d = NewDiagnosticForNode(node, message, args);
            d.SetSkippedOnNoEmit();
            AddDiagnostic(d);
            return true;
        }

        return false;
    }

    // ported from: internal/checker/grammarchecks.go:58-67（Go 包级函数）
    internal static Node? GetIdentifierFromEntityNameExpression(Node node)
    {
        switch (node.Kind)
        {
            case Kind.Identifier:
                return node;
            case Kind.PropertyAccessExpression:
                return node.AsPropertyAccessExpression()!.Name();
            default:
                return null;
        }
    }

    // ported from: internal/checker/grammarchecks.go:69-98
    internal bool CheckGrammarRegularExpressionLiteral(RegularExpressionLiteral node)
    {
        var sourceFile = GetSourceFileOfNode(node)!;
        if (!HasParseDiagnostics(sourceFile))
        {
            Diagnostic? lastError = null;
            if (_regExpScanner is null)
            {
                // Go: `c.regExpScanner = scanner.NewScanner()` —— NewScanner 等价于 defaultScanner()，
                // 其中 **skipTrivia = true**（C# 的默认构造是 false）。这里必须跳过前导 trivia：
                // ResetTokenState(node.Pos()) 的 node.Pos() 是 TokenFullStart()（含前导空白/换行），
                // 若不跳 trivia，Scan() 首个 token 是 WhitespaceTrivia，ReScanSlashToken() 原样返回，
                // 下面的 `tokenIsRegularExpressionLiteral` 断言必然失败。
                _regExpScanner = TsScanner.NewScanner();
            }

            _regExpScanner.SetScriptTarget(_languageVersion);
            _regExpScanner.SetLanguageVariant(sourceFile.LanguageVariant);
            _regExpScanner.SetOnError((message, start, length, args) =>
            {
                if (message.Category() == Category.Message && lastError is not null && start == lastError.Pos() && length == lastError.Len())
                {
                    // For providing spelling suggestions.
                    var err = NewDiagnostic(null, NewTextRange(start, start + length), message, args);
                    lastError.AddRelatedInfo(err);
                }
                else if (lastError is null || start != lastError.Pos())
                {
                    lastError = NewDiagnostic(sourceFile, NewTextRange(start, start + length), message, args);
                    lastError = AddDiagnostic(lastError);
                }
            });
            _regExpScanner.SetText(sourceFile.Text());
            _regExpScanner.ResetTokenState(node.Pos());
            _regExpScanner.Scan();
            var tokenIsRegularExpressionLiteral = _regExpScanner.ReScanSlashToken(true) == Kind.RegularExpressionLiteral;
            _regExpScanner.SetText("");
            _regExpScanner.SetOnError(null);
            Debug.Assert(tokenIsRegularExpressionLiteral);
            return lastError is not null;
        }

        return false;
    }

    // ported from: internal/checker/grammarchecks.go:100-118
    internal bool CheckGrammarPrivateIdentifierExpression(PrivateIdentifier privId)
    {
        var privIdAsNode = privId.AsNode();
        if (GetContainingClass(privIdAsNode) is null)
        {
            return GrammarErrorOnNode(privIdAsNode, Private_identifiers_are_not_allowed_outside_class_bodies);
        }

        if (!IsForInStatement(privId.Parent!))
        {
            if (!IsExpressionNode(privIdAsNode))
            {
                return GrammarErrorOnNode(privIdAsNode, Private_identifiers_are_only_allowed_in_class_bodies_and_may_only_be_used_as_part_of_a_class_member_declaration_property_access_or_on_the_left_hand_side_of_an_in_expression);
            }

            var isInOperation = IsBinaryExpression(privId.Parent!) && privId.Parent!.AsBinaryExpression()!.OperatorToken!.Kind == Kind.InKeyword;
            if (GetSymbolForPrivateIdentifierExpression(privIdAsNode) is null && !isInOperation)
            {
                return GrammarErrorOnNode(privIdAsNode, Cannot_find_name_0, privId.Text);
            }
        }

        return false;
    }

    // ported from: internal/checker/grammarchecks.go:120-125
    internal bool CheckGrammarMappedType(MappedTypeNode node)
    {
        if ((node.Members?.Nodes?.Length ?? 0) > 0)
        {
            return GrammarErrorOnNode(node.Members!.Nodes![0], A_mapped_type_may_not_declare_properties_or_methods);
        }

        return false;
    }

    // ported from: internal/checker/grammarchecks.go:127-197
    internal bool CheckGrammarDecorator(Decorator decorator)
    {
        var sourceFile = GetSourceFileOfNode(decorator)!;
        if (!HasParseDiagnostics(sourceFile))
        {
            var node = decorator.Expression!;

            // DecoratorParenthesizedExpression :
            //   `(` Expression `)`

            if (IsParenthesizedExpression(node))
            {
                return false;
            }

            var canHaveCallExpression = true;
            Node? errorNode = null;
            while (true)
            {
                // Allow TS syntax such as non-null assertions and instantiation expressions
                if (IsExpressionWithTypeArguments(node) || IsNonNullExpression(node))
                {
                    node = node.Expression()!;
                    continue;
                }

                // DecoratorCallExpression :
                //   DecoratorMemberExpression Arguments

                if (IsCallExpression(node))
                {
                    var callExpr = node.AsCallExpression()!;
                    if (!canHaveCallExpression)
                    {
                        errorNode = node;
                    }

                    if (callExpr.QuestionDotToken is not null)
                    {
                        // Even if we already have an error node, error at the `?.` token since it appears earlier.
                        errorNode = callExpr.QuestionDotToken;
                    }

                    node = callExpr.Expression!;
                    canHaveCallExpression = false;
                    continue;
                }

                // DecoratorMemberExpression :
                //   IdentifierReference
                //   DecoratorMemberExpression `.` IdentifierName
                //   DecoratorMemberExpression `.` PrivateIdentifier

                if (IsPropertyAccessExpression(node))
                {
                    var propertyAccessExpr = node.AsPropertyAccessExpression()!;
                    if (propertyAccessExpr.QuestionDotToken is not null)
                    {
                        // Even if we already have an error node, error at the `?.` token since it appears earlier.
                        errorNode = propertyAccessExpr.QuestionDotToken;
                    }

                    node = propertyAccessExpr.Expression!;
                    canHaveCallExpression = false;
                    continue;
                }

                if (!IsIdentifier(node))
                {
                    // Even if we already have an error node, error at this node since it appears earlier.
                    errorNode = node;
                }

                break;
            }

            if (errorNode is not null)
            {
                var err = Error(decorator.Expression, Expression_must_be_enclosed_in_parentheses_to_be_used_as_a_decorator);
                err.AddRelatedInfo(CreateDiagnosticForNode(errorNode, Invalid_syntax_in_decorator));
                return true;
            }
        }

        return false;
    }

    // ported from: internal/checker/grammarchecks.go:199-204
    internal bool CheckGrammarExportDeclaration(ExportDeclaration node)
    {
        if (node.IsTypeOnly && node.ExportClause is not null && node.ExportClause.Kind == Kind.NamedExports)
        {
            return CheckGrammarTypeOnlyNamedImportsOrExports(node.ExportClause);
        }

        return false;
    }

    // ported from: internal/checker/grammarchecks.go:206-212
    internal bool CheckGrammarModuleElementContext(Node node, Message errorMessage)
    {
        var isInAppropriateContext = node.Parent!.Kind == Kind.SourceFile || node.Parent.Kind == Kind.ModuleBlock || node.Parent.Kind == Kind.ModuleDeclaration;
        if (!isInAppropriateContext)
        {
            GrammarErrorOnFirstToken(node, errorMessage);
        }

        return !isInAppropriateContext;
    }

    // ported from: internal/checker/grammarchecks.go:214-569
    internal bool CheckGrammarModifiers(Node node)
    {
        if (node.Modifiers() is null)
        {
            return false;
        }

        if (ReportObviousDecoratorErrors(node) || ReportObviousModifierErrors(node))
        {
            return true;
        }

        if (IsThisParameter(node))
        {
            return GrammarErrorOnFirstToken(node, Neither_decorators_nor_modifiers_may_be_applied_to_this_parameters);
        }

        var blockScopeKind = NodeFlags.None;
        if (IsVariableStatement(node))
        {
            blockScopeKind = node.AsVariableStatement()!.DeclarationList!.Flags & NodeFlags.BlockScoped;
        }

        Node? lastStatic = null;
        Node? lastDeclare = null;
        Node? lastAsync = null;
        Node? lastOverride = null;
        Node? firstDecorator = null;
        var flags = ModifierFlags.None;
        var sawExportBeforeDecorators = false;
        // We parse decorators and modifiers in four contiguous chunks:
        // [...leadingDecorators, ...leadingModifiers, ...trailingDecorators, ...trailingModifiers]. It is an error to
        // have both leading and trailing decorators.
        var hasLeadingDecorators = false;
        // Go 的 `for _, modifier := range modifiers`：nil slice 迭代 0 次，所以这里用 `?? []`（不是 `!`）。
        var modifiers = node.ModifierNodes();
        foreach (var modifier in modifiers ?? [])
        {
            if (IsDecorator(modifier))
            {
                if (!NodeCanBeDecorated(_legacyDecorators, node, node.Parent, node.Parent!.Parent))
                {
                    if (node.Kind == Kind.MethodDeclaration && !NodeIsPresent(node.Body()))
                    {
                        return GrammarErrorOnFirstToken(node, A_decorator_can_only_decorate_a_method_implementation_not_an_overload);
                    }
                    else
                    {
                        return GrammarErrorOnFirstToken(node, Decorators_are_not_valid_here);
                    }
                }
                else if (_legacyDecorators && (node.Kind == Kind.GetAccessor || node.Kind == Kind.SetAccessor))
                {
                    var accessors = GetAllAccessorDeclarationsForDeclaration(node, GetSymbolOfDeclaration(node)!.Declarations ?? []);
                    if (HasDecorators(accessors.FirstAccessor!) && node == accessors.SecondAccessor)
                    {
                        return GrammarErrorOnFirstToken(node, Decorators_cannot_be_applied_to_multiple_get_Slashset_accessors_of_the_same_name);
                    }
                }

                // if we've seen any modifiers aside from `export`, `default`, or another decorator, then this is an invalid position
                if ((flags & ~(ModifierFlags.ExportDefault | ModifierFlags.Decorator)) != 0)
                {
                    return GrammarErrorOnNode(modifier, Decorators_are_not_valid_here);
                }

                // if we've already seen leading decorators and leading modifiers, then trailing decorators are an invalid position
                if (hasLeadingDecorators && (flags & ModifierFlags.Modifier) != 0)
                {
                    if (firstDecorator is null)
                    {
                        Debug.Fail("Expected firstDecorator to be set");
                    }

                    var sourceFile = GetSourceFileOfNode(modifier)!;
                    if (!HasParseDiagnostics(sourceFile))
                    {
                        var err = Error(modifier, Decorators_may_not_appear_after_export_or_export_default_if_they_also_appear_before_export);
                        err.AddRelatedInfo(CreateDiagnosticForNode(firstDecorator!, Decorator_used_before_export_here));
                        return true;
                    }

                    return false;
                }

                flags |= ModifierFlags.Decorator;

                // if we have not yet seen a modifier, then these are leading decorators
                if ((flags & ModifierFlags.Modifier) == 0)
                {
                    hasLeadingDecorators = true;
                }
                else if ((flags & ModifierFlags.Export) != 0)
                {
                    sawExportBeforeDecorators = true;
                }

                if (firstDecorator is null)
                {
                    firstDecorator = modifier;
                }
            }
            else
            {
                if (modifier.Kind != Kind.ReadonlyKeyword)
                {
                    if (node.Kind == Kind.PropertySignature || node.Kind == Kind.MethodSignature)
                    {
                        return GrammarErrorOnNode(modifier, X_0_modifier_cannot_appear_on_a_type_member, TokenToString(modifier.Kind));
                    }

                    if (node.Kind == Kind.IndexSignature && (modifier.Kind != Kind.StaticKeyword || !IsClassLike(node.Parent)))
                    {
                        return GrammarErrorOnNode(modifier, X_0_modifier_cannot_appear_on_an_index_signature, TokenToString(modifier.Kind));
                    }
                }

                if (modifier.Kind != Kind.InKeyword && modifier.Kind != Kind.OutKeyword && modifier.Kind != Kind.ConstKeyword)
                {
                    if (node.Kind == Kind.TypeParameter)
                    {
                        return GrammarErrorOnNode(modifier, X_0_modifier_cannot_appear_on_a_type_parameter, TokenToString(modifier.Kind));
                    }
                }

                switch (modifier.Kind)
                {
                    case Kind.ConstKeyword:
                    {
                        if (node.Kind != Kind.EnumDeclaration && node.Kind != Kind.TypeParameter)
                        {
                            return GrammarErrorOnNode(node, A_class_member_cannot_have_the_0_keyword, TokenToString(Kind.ConstKeyword));
                        }

                        var parent = node.Parent!;
                        if (node.Kind == Kind.TypeParameter)
                        {
                            if (!(IsFunctionLikeDeclaration(parent) || IsClassLike(parent) ||
                                IsFunctionTypeNode(parent) || IsConstructorTypeNode(parent) ||
                                IsCallSignatureDeclaration(parent) || IsConstructSignatureDeclaration(parent) ||
                                IsMethodSignatureDeclaration(parent)))
                            {
                                return GrammarErrorOnNode(modifier, X_0_modifier_can_only_appear_on_a_type_parameter_of_a_function_method_or_class, TokenToString(modifier.Kind));
                            }
                        }

                        break;
                    }
                    case Kind.OverrideKeyword:
                    {
                        // If node.kind === SyntaxKind.Parameter, checkParameter reports an error if it's not a parameter property.
                        if ((flags & ModifierFlags.Override) != 0)
                        {
                            return GrammarErrorOnNode(modifier, X_0_modifier_already_seen, "override");
                        }
                        else if ((flags & ModifierFlags.Ambient) != 0)
                        {
                            return GrammarErrorOnNode(modifier, X_0_modifier_cannot_be_used_with_1_modifier, "override", "declare");
                        }
                        else if ((flags & ModifierFlags.Readonly) != 0 && (modifier.Flags & NodeFlags.Reparsed) == 0)
                        {
                            return GrammarErrorOnNode(modifier, X_0_modifier_must_precede_1_modifier, "override", "readonly");
                        }
                        else if ((flags & ModifierFlags.Accessor) != 0 && (modifier.Flags & NodeFlags.Reparsed) == 0)
                        {
                            return GrammarErrorOnNode(modifier, X_0_modifier_must_precede_1_modifier, "override", "accessor");
                        }
                        else if ((flags & ModifierFlags.Async) != 0 && (modifier.Flags & NodeFlags.Reparsed) == 0)
                        {
                            return GrammarErrorOnNode(modifier, X_0_modifier_must_precede_1_modifier, "override", "async");
                        }

                        flags |= ModifierFlags.Override;
                        lastOverride = modifier;

                        break;
                    }
                    case Kind.PublicKeyword:
                    case Kind.ProtectedKeyword:
                    case Kind.PrivateKeyword:
                    {
                        var text = VisibilityToString(ModifierToFlag(modifier.Kind));

                        if ((flags & ModifierFlags.AccessibilityModifier) != 0)
                        {
                            return GrammarErrorOnNode(modifier, Accessibility_modifier_already_seen);
                        }
                        else if ((flags & ModifierFlags.Override) != 0 && (modifier.Flags & NodeFlags.Reparsed) == 0)
                        {
                            return GrammarErrorOnNode(modifier, X_0_modifier_must_precede_1_modifier, text, "override");
                        }
                        else if ((flags & ModifierFlags.Static) != 0 && (modifier.Flags & NodeFlags.Reparsed) == 0)
                        {
                            return GrammarErrorOnNode(modifier, X_0_modifier_must_precede_1_modifier, text, "static");
                        }
                        else if ((flags & ModifierFlags.Accessor) != 0 && (modifier.Flags & NodeFlags.Reparsed) == 0)
                        {
                            return GrammarErrorOnNode(modifier, X_0_modifier_must_precede_1_modifier, text, "accessor");
                        }
                        else if ((flags & ModifierFlags.Readonly) != 0 && (modifier.Flags & NodeFlags.Reparsed) == 0)
                        {
                            return GrammarErrorOnNode(modifier, X_0_modifier_must_precede_1_modifier, text, "readonly");
                        }
                        else if ((flags & ModifierFlags.Async) != 0 && (modifier.Flags & NodeFlags.Reparsed) == 0)
                        {
                            return GrammarErrorOnNode(modifier, X_0_modifier_must_precede_1_modifier, text, "async");
                        }
                        else if (node.Parent!.Kind == Kind.ModuleBlock || node.Parent.Kind == Kind.SourceFile)
                        {
                            return GrammarErrorOnNode(modifier, X_0_modifier_cannot_appear_on_a_module_or_namespace_element, text);
                        }
                        else if ((flags & ModifierFlags.Abstract) != 0)
                        {
                            if (modifier.Kind == Kind.PrivateKeyword)
                            {
                                return GrammarErrorOnNode(modifier, X_0_modifier_cannot_be_used_with_1_modifier, text, "abstract");
                            }
                            else if ((modifier.Flags & NodeFlags.Reparsed) == 0)
                            {
                                return GrammarErrorOnNode(modifier, X_0_modifier_must_precede_1_modifier, text, "abstract");
                            }
                        }
                        else if (IsPrivateIdentifierClassElementDeclaration(node))
                        {
                            return GrammarErrorOnNode(modifier, An_accessibility_modifier_cannot_be_used_with_a_private_identifier);
                        }

                        flags |= ModifierToFlag(modifier.Kind);

                        break;
                    }
                    case Kind.StaticKeyword:
                    {
                        if ((flags & ModifierFlags.Static) != 0)
                        {
                            return GrammarErrorOnNode(modifier, X_0_modifier_already_seen, "static");
                        }
                        else if ((flags & ModifierFlags.Readonly) != 0 && (modifier.Flags & NodeFlags.Reparsed) == 0)
                        {
                            return GrammarErrorOnNode(modifier, X_0_modifier_must_precede_1_modifier, "static", "readonly");
                        }
                        else if ((flags & ModifierFlags.Async) != 0 && (modifier.Flags & NodeFlags.Reparsed) == 0)
                        {
                            return GrammarErrorOnNode(modifier, X_0_modifier_must_precede_1_modifier, "static", "async");
                        }
                        else if ((flags & ModifierFlags.Accessor) != 0 && (modifier.Flags & NodeFlags.Reparsed) == 0)
                        {
                            return GrammarErrorOnNode(modifier, X_0_modifier_must_precede_1_modifier, "static", "accessor");
                        }
                        else if (node.Parent!.Kind == Kind.ModuleBlock || node.Parent.Kind == Kind.SourceFile)
                        {
                            return GrammarErrorOnNode(modifier, X_0_modifier_cannot_appear_on_a_module_or_namespace_element, "static");
                        }
                        else if (node.Kind == Kind.Parameter)
                        {
                            return GrammarErrorOnNode(modifier, X_0_modifier_cannot_appear_on_a_parameter, "static");
                        }
                        else if ((flags & ModifierFlags.Abstract) != 0)
                        {
                            return GrammarErrorOnNode(modifier, X_0_modifier_cannot_be_used_with_1_modifier, "static", "abstract");
                        }
                        else if ((flags & ModifierFlags.Override) != 0 && (modifier.Flags & NodeFlags.Reparsed) == 0)
                        {
                            return GrammarErrorOnNode(modifier, X_0_modifier_must_precede_1_modifier, "static", "override");
                        }

                        flags |= ModifierFlags.Static;
                        lastStatic = modifier;

                        break;
                    }
                    case Kind.AccessorKeyword:
                    {
                        if ((flags & ModifierFlags.Accessor) != 0)
                        {
                            return GrammarErrorOnNode(modifier, X_0_modifier_already_seen, "accessor");
                        }
                        else if ((flags & ModifierFlags.Readonly) != 0)
                        {
                            return GrammarErrorOnNode(modifier, X_0_modifier_cannot_be_used_with_1_modifier, "accessor", "readonly");
                        }
                        else if ((flags & ModifierFlags.Ambient) != 0)
                        {
                            return GrammarErrorOnNode(modifier, X_0_modifier_cannot_be_used_with_1_modifier, "accessor", "declare");
                        }
                        else if (node.Kind != Kind.PropertyDeclaration)
                        {
                            return GrammarErrorOnNode(modifier, X_accessor_modifier_can_only_appear_on_a_property_declaration);
                        }

                        flags |= ModifierFlags.Accessor;

                        break;
                    }
                    case Kind.ReadonlyKeyword:
                    {
                        if ((flags & ModifierFlags.Readonly) != 0)
                        {
                            return GrammarErrorOnNode(modifier, X_0_modifier_already_seen, "readonly");
                        }
                        else if (node.Kind != Kind.PropertyDeclaration && node.Kind != Kind.PropertySignature && node.Kind != Kind.IndexSignature && node.Kind != Kind.Parameter)
                        {
                            // If node.kind === SyntaxKind.Parameter, checkParameter reports an error if it's not a parameter property.
                            return GrammarErrorOnNode(modifier, X_readonly_modifier_can_only_appear_on_a_property_declaration_or_index_signature);
                        }
                        else if ((flags & ModifierFlags.Accessor) != 0)
                        {
                            return GrammarErrorOnNode(modifier, X_0_modifier_cannot_be_used_with_1_modifier, "readonly", "accessor");
                        }

                        flags |= ModifierFlags.Readonly;

                        break;
                    }
                    case Kind.ExportKeyword:
                    {
                        if (_compilerOptions.VerbatimModuleSyntax == Tristate.TSTrue && (node.Flags & NodeFlags.Ambient) == 0 && node.Kind != Kind.TypeAliasDeclaration && node.Kind != Kind.InterfaceDeclaration && node.Kind != Kind.ModuleDeclaration && node.Parent!.Kind == Kind.SourceFile && _program.GetEmitModuleFormatOfFile(GetSourceFileOfNode(node)!) == ModuleKind.CommonJS)
                        {
                            return GrammarErrorOnNode(modifier, A_top_level_export_modifier_cannot_be_used_on_value_declarations_in_a_CommonJS_module_when_verbatimModuleSyntax_is_enabled);
                        }

                        if ((flags & ModifierFlags.Export) != 0)
                        {
                            return GrammarErrorOnNode(modifier, X_0_modifier_already_seen, "export");
                        }
                        else if ((flags & ModifierFlags.Ambient) != 0 && (modifier.Flags & NodeFlags.Reparsed) == 0)
                        {
                            return GrammarErrorOnNode(modifier, X_0_modifier_must_precede_1_modifier, "export", "declare");
                        }
                        else if ((flags & ModifierFlags.Abstract) != 0 && (modifier.Flags & NodeFlags.Reparsed) == 0)
                        {
                            return GrammarErrorOnNode(modifier, X_0_modifier_must_precede_1_modifier, "export", "abstract");
                        }
                        else if ((flags & ModifierFlags.Async) != 0 && (modifier.Flags & NodeFlags.Reparsed) == 0)
                        {
                            return GrammarErrorOnNode(modifier, X_0_modifier_must_precede_1_modifier, "export", "async");
                        }
                        else if (IsClassLike(node.Parent) && !IsJSTypeAliasDeclaration(node))
                        {
                            return GrammarErrorOnNode(modifier, X_0_modifier_cannot_appear_on_class_elements_of_this_kind, "export");
                        }
                        else if (node.Kind == Kind.Parameter)
                        {
                            return GrammarErrorOnNode(modifier, X_0_modifier_cannot_appear_on_a_parameter, "export");
                        }
                        else if (blockScopeKind == NodeFlags.Using)
                        {
                            return GrammarErrorOnNode(modifier, X_0_modifier_cannot_appear_on_a_using_declaration, "export");
                        }
                        else if (blockScopeKind == NodeFlags.AwaitUsing)
                        {
                            return GrammarErrorOnNode(modifier, X_0_modifier_cannot_appear_on_an_await_using_declaration, "export");
                        }

                        flags |= ModifierFlags.Export;

                        break;
                    }
                    case Kind.DefaultKeyword:
                    {
                        Node container;
                        if (node.Parent!.Kind == Kind.SourceFile)
                        {
                            container = node.Parent;
                        }
                        else
                        {
                            container = node.Parent.Parent!;
                        }

                        if (container.Kind == Kind.ModuleDeclaration && !IsAmbientModule(container))
                        {
                            return GrammarErrorOnNode(modifier, A_default_export_can_only_be_used_in_an_ECMAScript_style_module);
                        }
                        else if (blockScopeKind == NodeFlags.Using)
                        {
                            return GrammarErrorOnNode(modifier, X_0_modifier_cannot_appear_on_a_using_declaration, "default");
                        }
                        else if (blockScopeKind == NodeFlags.AwaitUsing)
                        {
                            return GrammarErrorOnNode(modifier, X_0_modifier_cannot_appear_on_an_await_using_declaration, "default");
                        }
                        else if ((flags & ModifierFlags.Export) == 0 && (modifier.Flags & NodeFlags.Reparsed) == 0)
                        {
                            return GrammarErrorOnNode(modifier, X_0_modifier_must_precede_1_modifier, "export", "default");
                        }
                        else if (sawExportBeforeDecorators)
                        {
                            return GrammarErrorOnNode(firstDecorator, Decorators_are_not_valid_here);
                        }

                        flags |= ModifierFlags.Default;

                        break;
                    }
                    case Kind.DeclareKeyword:
                    {
                        if ((flags & ModifierFlags.Ambient) != 0)
                        {
                            return GrammarErrorOnNode(modifier, X_0_modifier_already_seen, "declare");
                        }
                        else if ((flags & ModifierFlags.Async) != 0)
                        {
                            return GrammarErrorOnNode(modifier, X_0_modifier_cannot_be_used_in_an_ambient_context, "async");
                        }
                        else if ((flags & ModifierFlags.Override) != 0)
                        {
                            return GrammarErrorOnNode(modifier, X_0_modifier_cannot_be_used_in_an_ambient_context, "override");
                        }
                        else if (IsClassLike(node.Parent) && !IsPropertyDeclaration(node))
                        {
                            return GrammarErrorOnNode(modifier, X_0_modifier_cannot_appear_on_class_elements_of_this_kind, "declare");
                        }
                        else if (node.Kind == Kind.Parameter)
                        {
                            return GrammarErrorOnNode(modifier, X_0_modifier_cannot_appear_on_a_parameter, "declare");
                        }
                        else if (blockScopeKind == NodeFlags.Using)
                        {
                            return GrammarErrorOnNode(modifier, X_0_modifier_cannot_appear_on_a_using_declaration, "declare");
                        }
                        else if (blockScopeKind == NodeFlags.AwaitUsing)
                        {
                            return GrammarErrorOnNode(modifier, X_0_modifier_cannot_appear_on_an_await_using_declaration, "declare");
                        }
                        else if ((node.Parent!.Flags & NodeFlags.Ambient) != 0 && node.Parent.Kind == Kind.ModuleBlock)
                        {
                            return GrammarErrorOnNode(modifier, A_declare_modifier_cannot_be_used_in_an_already_ambient_context);
                        }
                        else if (IsPrivateIdentifierClassElementDeclaration(node))
                        {
                            return GrammarErrorOnNode(modifier, X_0_modifier_cannot_be_used_with_a_private_identifier, "declare");
                        }
                        else if ((flags & ModifierFlags.Accessor) != 0)
                        {
                            return GrammarErrorOnNode(modifier, X_0_modifier_cannot_be_used_with_1_modifier, "declare", "accessor");
                        }

                        flags |= ModifierFlags.Ambient;
                        lastDeclare = modifier;

                        break;
                    }
                    case Kind.AbstractKeyword:
                    {
                        if ((flags & ModifierFlags.Abstract) != 0)
                        {
                            return GrammarErrorOnNode(modifier, X_0_modifier_already_seen, "abstract");
                        }

                        if (node.Kind != Kind.ClassDeclaration && node.Kind != Kind.ConstructorType)
                        {
                            if (node.Kind != Kind.MethodDeclaration && node.Kind != Kind.PropertyDeclaration && node.Kind != Kind.GetAccessor && node.Kind != Kind.SetAccessor)
                            {
                                return GrammarErrorOnNode(modifier, X_abstract_modifier_can_only_appear_on_a_class_method_or_property_declaration);
                            }

                            if (!(node.Parent!.Kind == Kind.ClassDeclaration && HasSyntacticModifier(node.Parent, ModifierFlags.Abstract)))
                            {
                                Message message;
                                if (node.Kind == Kind.PropertyDeclaration)
                                {
                                    message = Abstract_properties_can_only_appear_within_an_abstract_class;
                                }
                                else
                                {
                                    message = Abstract_methods_can_only_appear_within_an_abstract_class;
                                }

                                return GrammarErrorOnNode(modifier, message);
                            }

                            if ((flags & ModifierFlags.Static) != 0)
                            {
                                return GrammarErrorOnNode(modifier, X_0_modifier_cannot_be_used_with_1_modifier, "static", "abstract");
                            }

                            if ((flags & ModifierFlags.Private) != 0)
                            {
                                return GrammarErrorOnNode(modifier, X_0_modifier_cannot_be_used_with_1_modifier, "private", "abstract");
                            }

                            if ((flags & ModifierFlags.Async) != 0 && lastAsync is not null)
                            {
                                return GrammarErrorOnNode(lastAsync, X_0_modifier_cannot_be_used_with_1_modifier, "async", "abstract");
                            }

                            if ((flags & ModifierFlags.Override) != 0 && (modifier.Flags & NodeFlags.Reparsed) == 0)
                            {
                                return GrammarErrorOnNode(modifier, X_0_modifier_must_precede_1_modifier, "abstract", "override");
                            }

                            if ((flags & ModifierFlags.Accessor) != 0 && (modifier.Flags & NodeFlags.Reparsed) == 0)
                            {
                                return GrammarErrorOnNode(modifier, X_0_modifier_must_precede_1_modifier, "abstract", "accessor");
                            }
                        }

                        var name = node.Name();
                        if (name is not null && name.Kind == Kind.PrivateIdentifier)
                        {
                            return GrammarErrorOnNode(modifier, X_0_modifier_cannot_be_used_with_a_private_identifier, "abstract");
                        }

                        flags |= ModifierFlags.Abstract;

                        break;
                    }
                    case Kind.AsyncKeyword:
                    {
                        if ((flags & ModifierFlags.Async) != 0)
                        {
                            return GrammarErrorOnNode(modifier, X_0_modifier_already_seen, "async");
                        }
                        else if ((flags & ModifierFlags.Ambient) != 0 || (node.Parent!.Flags & NodeFlags.Ambient) != 0)
                        {
                            return GrammarErrorOnNode(modifier, X_0_modifier_cannot_be_used_in_an_ambient_context, "async");
                        }
                        else if (node.Kind == Kind.Parameter)
                        {
                            return GrammarErrorOnNode(modifier, X_0_modifier_cannot_appear_on_a_parameter, "async");
                        }

                        if ((flags & ModifierFlags.Abstract) != 0)
                        {
                            return GrammarErrorOnNode(modifier, X_0_modifier_cannot_be_used_with_1_modifier, "async", "abstract");
                        }

                        flags |= ModifierFlags.Async;
                        lastAsync = modifier;

                        break;
                    }
                    case Kind.InKeyword:
                    case Kind.OutKeyword:
                    {
                        ModifierFlags inOutFlag;
                        if (modifier.Kind == Kind.InKeyword)
                        {
                            inOutFlag = ModifierFlags.In;
                        }
                        else
                        {
                            inOutFlag = ModifierFlags.Out;
                        }

                        string inOutText;
                        if (modifier.Kind == Kind.InKeyword)
                        {
                            inOutText = "in";
                        }
                        else
                        {
                            inOutText = "out";
                        }

                        var parent = node.Parent;
                        if (node.Kind != Kind.TypeParameter || (parent is not null && !(IsInterfaceDeclaration(parent) || IsClassLike(parent) || IsTypeOrJSTypeAliasDeclaration(parent))))
                        {
                            return GrammarErrorOnNode(modifier, X_0_modifier_can_only_appear_on_a_type_parameter_of_a_class_interface_or_type_alias, inOutText);
                        }

                        if ((flags & inOutFlag) != 0)
                        {
                            return GrammarErrorOnNode(modifier, X_0_modifier_already_seen, inOutText);
                        }

                        if ((inOutFlag & ModifierFlags.In) != 0 && (flags & ModifierFlags.Out) != 0)
                        {
                            return GrammarErrorOnNode(modifier, X_0_modifier_must_precede_1_modifier, "in", "out");
                        }

                        flags |= inOutFlag;

                        break;
                    }
                }
            }
        }

        if (node.Kind == Kind.Constructor)
        {
            if ((flags & ModifierFlags.Static) != 0)
            {
                return GrammarErrorOnNode(lastStatic, X_0_modifier_cannot_appear_on_a_constructor_declaration, "static");
            }

            if ((flags & ModifierFlags.Override) != 0)
            {
                return GrammarErrorOnNode(lastOverride, X_0_modifier_cannot_appear_on_a_constructor_declaration, "override");
            }

            if ((flags & ModifierFlags.Async) != 0)
            {
                return GrammarErrorOnNode(lastAsync, X_0_modifier_cannot_appear_on_a_constructor_declaration, "async");
            }

            return false;
        }
        else if ((node.Kind == Kind.ImportDeclaration || node.Kind == Kind.JSImportDeclaration || node.Kind == Kind.ImportEqualsDeclaration) && (flags & ModifierFlags.Ambient) != 0)
        {
            return GrammarErrorOnNode(lastDeclare, A_0_modifier_cannot_be_used_with_an_import_declaration, "declare");
        }
        else if (node.Kind == Kind.Parameter && (flags & ModifierFlags.ParameterPropertyModifier) != 0 && IsBindingPattern(node.Name()))
        {
            return GrammarErrorOnNode(node, A_parameter_property_may_not_be_declared_using_a_binding_pattern);
        }
        else if (node.Kind == Kind.Parameter && (flags & ModifierFlags.ParameterPropertyModifier) != 0 && node.AsParameterDeclaration()!.DotDotDotToken is not null)
        {
            return GrammarErrorOnNode(node, A_parameter_property_cannot_be_declared_using_a_rest_parameter);
        }

        if ((flags & ModifierFlags.Async) != 0)
        {
            return CheckGrammarAsyncModifier(node, lastAsync!);
        }

        return false;
    }

    // ported from: internal/checker/grammarchecks.go:571-577
    internal bool ReportObviousModifierErrors(Node node)
    {
        var modifier = FindFirstIllegalModifier(node);
        if (modifier is null)
        {
            return false;
        }

        return GrammarErrorOnFirstToken(modifier, Modifiers_cannot_appear_here);
    }

    // ported from: internal/checker/grammarchecks.go:579-585
    internal Node? FindFirstModifierExcept(Node node, Kind allowedModifier)
    {
        Node? modifier = Find(node.ModifierNodes(), IsModifier);
        if (modifier is not null && modifier.Kind != allowedModifier)
        {
            return modifier;
        }

        return null;
    }

    // ported from: internal/checker/grammarchecks.go:587-640
    internal Node? FindFirstIllegalModifier(Node node)
    {
        switch (node.Kind)
        {
            case Kind.GetAccessor:
            case Kind.SetAccessor:
            case Kind.Constructor:
            case Kind.PropertyDeclaration:
            case Kind.PropertySignature:
            case Kind.MethodDeclaration:
            case Kind.MethodSignature:
            case Kind.IndexSignature:
            case Kind.ModuleDeclaration:
            case Kind.ImportDeclaration:
            case Kind.JSImportDeclaration:
            case Kind.ImportEqualsDeclaration:
            case Kind.ExportDeclaration:
            case Kind.ExportAssignment:
            case Kind.FunctionExpression:
            case Kind.ArrowFunction:
            case Kind.Parameter:
            case Kind.TypeParameter:
            case Kind.JSTypeAliasDeclaration:
                return null;
            case Kind.ClassStaticBlockDeclaration:
            case Kind.PropertyAssignment:
            case Kind.ShorthandPropertyAssignment:
            case Kind.NamespaceExportDeclaration:
            case Kind.MissingDeclaration:
                return Find(node.ModifierNodes(), IsModifier);
            default:
                if (node.Parent!.Kind == Kind.ModuleBlock || node.Parent.Kind == Kind.SourceFile)
                {
                    return null;
                }

                switch (node.Kind)
                {
                    case Kind.FunctionDeclaration:
                        return FindFirstModifierExcept(node, Kind.AsyncKeyword);
                    case Kind.ClassDeclaration:
                    case Kind.ConstructorType:
                        return FindFirstModifierExcept(node, Kind.AbstractKeyword);
                    case Kind.ClassExpression:
                    case Kind.InterfaceDeclaration:
                    case Kind.TypeAliasDeclaration:
                        return Find(node.ModifierNodes(), IsModifier);
                    case Kind.VariableStatement:
                        if ((node.AsVariableStatement()!.DeclarationList!.Flags & NodeFlags.Using) != 0)
                        {
                            return FindFirstModifierExcept(node, Kind.AwaitKeyword);
                        }

                        return Find(node.ModifierNodes(), IsModifier);
                    case Kind.EnumDeclaration:
                        return FindFirstModifierExcept(node, Kind.ConstKeyword);
                    default:
                        Debug.Fail("Unhandled case in findFirstIllegalModifier.");
                        return null;
                }
        }
    }

    // ported from: internal/checker/grammarchecks.go:642-648
    internal bool ReportObviousDecoratorErrors(Node node)
    {
        var decorator = FindFirstIllegalDecorator(node);
        if (decorator is null)
        {
            return false;
        }

        return GrammarErrorOnFirstToken(decorator, Decorators_are_not_valid_here);
    }

    // ported from: internal/checker/grammarchecks.go:650-657
    internal Node? FindFirstIllegalDecorator(Node node)
    {
        if (CanHaveIllegalDecorators(node))
        {
            Node? decorator = Find(node.ModifierNodes(), IsDecorator);
            return decorator;
        }
        else
        {
            return null;
        }
    }

    // ported from: internal/checker/grammarchecks.go:659-669
    internal bool CheckGrammarAsyncModifier(Node? node, Node? asyncModifier)
    {
        switch (node!.Kind)
        {
            case Kind.MethodDeclaration:
            case Kind.FunctionDeclaration:
            case Kind.FunctionExpression:
            case Kind.ArrowFunction:
                return false;
        }

        return GrammarErrorOnNode(asyncModifier, X_0_modifier_cannot_be_used_here, "async");
    }

    // ported from: internal/checker/grammarchecks.go:671-676
    internal bool CheckGrammarForDisallowedTrailingComma(NodeList? list, Message diag)
    {
        if (list is not null && list.HasTrailingComma())
        {
            return GrammarErrorAtPos(list.Nodes![0], list.End() - ",".Length, ",".Length, diag);
        }

        return false;
    }

    // ported from: internal/checker/grammarchecks.go:678-685
    internal bool CheckGrammarTypeParameterList(NodeList? typeParameters, SourceFile file)
    {
        if (typeParameters is not null && (typeParameters.Nodes?.Length ?? 0) == 0)
        {
            var start = typeParameters.Pos() - "<".Length;
            var end = SkipTrivia(file.Text(), typeParameters.End()) + ">".Length;
            return GrammarErrorAtPos(file, start, end - start, Type_parameter_list_cannot_be_empty);
        }

        return false;
    }

    // ported from: internal/checker/grammarchecks.go:687-720
    internal bool CheckGrammarParameterList(NodeList parameters)
    {
        var seenOptionalParameter = false;
        // Go: len(parameters.Nodes)（nil slice 长度为 0）
        var parameterCount = parameters.Nodes?.Length ?? 0;

        for (var i = 0; i < parameterCount; i++)
        {
            var parameter = parameters.Nodes![i].AsParameterDeclaration()!;
            if (parameter.DotDotDotToken is not null)
            {
                if (i != parameterCount - 1)
                {
                    return GrammarErrorOnNode(parameter.DotDotDotToken, A_rest_parameter_must_be_last_in_a_parameter_list);
                }

                if ((parameter.Flags & NodeFlags.Ambient) == 0)
                {
                    CheckGrammarForDisallowedTrailingComma(parameters, A_rest_parameter_or_binding_pattern_may_not_have_a_trailing_comma);
                }

                if (parameter.QuestionToken is not null)
                {
                    return GrammarErrorOnNode(parameter.QuestionToken, A_rest_parameter_cannot_be_optional);
                }

                if (parameter.Initializer is not null)
                {
                    return GrammarErrorOnNode(parameter.Name(), A_rest_parameter_cannot_have_an_initializer);
                }
            }
            else if (IsOptionalDeclaration(parameter))
            {
                seenOptionalParameter = true;
                // A reparsed '?' token indicates a bracketed name in @param tag
                if (parameter.QuestionToken is not null && (parameter.QuestionToken.Flags & NodeFlags.Reparsed) == 0 && parameter.Initializer is not null)
                {
                    return GrammarErrorOnNode(parameter.Name(), Parameter_cannot_have_question_mark_and_initializer);
                }
            }
            else if (seenOptionalParameter && parameter.Initializer is null)
            {
                return GrammarErrorOnNode(parameter.Name(), A_required_parameter_cannot_follow_an_optional_parameter);
            }
        }

        return false;
    }

    // ported from: internal/checker/grammarchecks.go:722-756
    internal bool CheckGrammarForUseStrictSimpleParameterList(Node node)
    {
        if (_languageVersion >= ScriptTarget.ES2016)
        {
            var body = node.Body();
            Node? useStrictDirective = null;
            if (body is not null && IsBlock(body))
            {
                useStrictDirective = FindUseStrictPrologue(GetSourceFileOfNode(node)!, body.Statements() ?? []);
            }

            if (useStrictDirective is not null)
            {
                var nonSimpleParameters = Filter(node.Parameters(), n =>
                {
                    var parameter = n.AsParameterDeclaration()!;
                    return parameter.Initializer is not null || IsBindingPattern(parameter.Name()) || IsRestParameter(parameter);
                });
                if (nonSimpleParameters.Length != 0)
                {
                    foreach (var parameter in nonSimpleParameters)
                    {
                        var err = Error(parameter, This_parameter_is_not_allowed_with_use_strict_directive);
                        err.AddRelatedInfo(CreateDiagnosticForNode(useStrictDirective, X_use_strict_directive_used_here));
                    }

                    var err2 = Error(useStrictDirective, X_use_strict_directive_cannot_be_used_with_non_simple_parameter_list);
                    for (var index = 0; index < nonSimpleParameters.Length; index++)
                    {
                        Message relatedMessage;
                        if (index == 0)
                        {
                            relatedMessage = Non_simple_parameter_declared_here;
                        }
                        else
                        {
                            relatedMessage = X_and_here;
                        }

                        err2.AddRelatedInfo(CreateDiagnosticForNode(nonSimpleParameters[index], relatedMessage));
                    }

                    return true;
                }
            }
        }

        return false;
    }

    // ported from: internal/checker/grammarchecks.go:758-765
    internal bool CheckGrammarFunctionLikeDeclaration(Node node)
    {
        // Prevent cascading error by short-circuit
        var file = GetSourceFileOfNode(node)!;
        var funcData = node.FunctionLikeData();
        return CheckGrammarModifiers(node) || CheckGrammarTypeParameterList(funcData!.TypeParameters, file) ||
            CheckGrammarParameterList(funcData.Parameters!) || CheckGrammarArrowFunction(node, file) ||
            (IsFunctionLikeDeclaration(node) && CheckGrammarForUseStrictSimpleParameterList(node));
    }

    // ported from: internal/checker/grammarchecks.go:767-770
    internal bool CheckGrammarClassLikeDeclaration(Node node)
    {
        var file = GetSourceFileOfNode(node)!;
        return CheckGrammarClassDeclarationHeritageClauses(node, file) || CheckGrammarTypeParameterList(node.TypeParameterList(), file);
    }

    // ported from: internal/checker/grammarchecks.go:772-794
    internal bool CheckGrammarArrowFunction(Node node, SourceFile file)
    {
        if (!IsArrowFunction(node))
        {
            return false;
        }

        var arrowFunc = node.AsArrowFunction()!;
        var typeParameters = arrowFunc.TypeParameters;
        if (typeParameters is not null)
        {
            var typeParamNodes = typeParameters.Nodes ?? [];
            var hasConstraint = typeParamNodes.Length > 0 && typeParamNodes[0].AsTypeParameterDeclaration()!.Constraint is not null;
            if (!(typeParamNodes.Length > 1 || typeParameters.HasTrailingComma() || hasConstraint))
            {
                if (FileExtensionIsOneOf(file.FileName(), [ExtensionMts, ExtensionCts]))
                {
                    // TODO(danielr): should we return early here?
                    GrammarErrorOnNode(typeParameters.Nodes![0], This_syntax_is_reserved_in_files_with_the_mts_or_cts_extension_Add_a_trailing_comma_or_explicit_constraint);
                }
            }
        }

        var equalsGreaterThanToken = arrowFunc.EqualsGreaterThanToken!;
        var arrowFullText = file.Text().AsSpan(equalsGreaterThanToken.Pos(), equalsGreaterThanToken.End() - equalsGreaterThanToken.Pos());
        return ContainsLineBreak(arrowFullText) &&
            GrammarErrorOnNode(equalsGreaterThanToken, Line_terminator_not_permitted_before_arrow);

        // Go: strings.ContainsFunc(arrowFullText, stringutil.IsLineBreak)（按 rune 迭代；IsLineBreak 的目标字符都在 BMP）。
        static bool ContainsLineBreak(ReadOnlySpan<char> s)
        {
            foreach (var ch in s)
            {
                if (IsLineBreak(ch))
                {
                    return true;
                }
            }

            return false;
        }
    }

    // ported from: internal/checker/grammarchecks.go:796-838
    internal bool CheckGrammarIndexSignatureParameters(IndexSignatureDeclaration node)
    {
        // Go: len(paramNodes)（nil slice 长度为 0）
        var paramNodes = node.Parameters!.Nodes ?? [];

        if (paramNodes.Length == 0)
        {
            return GrammarErrorOnNode(node, An_index_signature_must_have_exactly_one_parameter);
        }

        var parameter = paramNodes[0].AsParameterDeclaration()!;
        if (paramNodes.Length != 1)
        {
            return GrammarErrorOnNode(parameter.Name(), An_index_signature_must_have_exactly_one_parameter);
        }

        CheckGrammarForDisallowedTrailingComma(node.Parameters, An_index_signature_cannot_have_a_trailing_comma);
        if (parameter.DotDotDotToken is not null)
        {
            return GrammarErrorOnNode(parameter.DotDotDotToken, An_index_signature_cannot_have_a_rest_parameter);
        }

        if (parameter.Modifiers() is not null)
        {
            return GrammarErrorOnNode(parameter.Name(), An_index_signature_parameter_cannot_have_an_accessibility_modifier);
        }

        if (parameter.QuestionToken is not null)
        {
            return GrammarErrorOnNode(parameter.QuestionToken, An_index_signature_parameter_cannot_have_a_question_mark);
        }

        if (parameter.Initializer is not null)
        {
            return GrammarErrorOnNode(parameter.Name(), An_index_signature_parameter_cannot_have_an_initializer);
        }

        var typeNode = parameter.Type;
        if (typeNode is null)
        {
            return GrammarErrorOnNode(parameter.Name(), An_index_signature_parameter_must_have_a_type_annotation);
        }

        var t = GetTypeFromTypeNode(typeNode);
        if (SomeType(t, static type => (type._flags & TypeFlags.StringOrNumberLiteralOrUnique) != 0) || IsGenericType(t))
        {
            return GrammarErrorOnNode(parameter.Name(), An_index_signature_parameter_type_cannot_be_a_literal_type_or_generic_type_Consider_using_a_mapped_object_type_instead);
        }

        if (!EveryType(t, IsValidIndexKeyType))
        {
            return GrammarErrorOnNode(parameter.Name(), An_index_signature_parameter_type_must_be_string_number_symbol_or_a_template_literal_type);
        }

        if (node.Type is null)
        {
            return GrammarErrorOnNode(node, An_index_signature_must_have_a_type_annotation);
        }

        return false;
    }

    // ported from: internal/checker/grammarchecks.go:840-843
    internal bool CheckGrammarIndexSignature(IndexSignatureDeclaration node)
    {
        // Prevent cascading error by short-circuit
        return CheckGrammarModifiers(node) || CheckGrammarIndexSignatureParameters(node);
    }

    // ported from: internal/checker/grammarchecks.go:845-853
    internal bool CheckGrammarForAtLeastOneTypeArgument(Node node, NodeList? typeArguments)
    {
        if (typeArguments is not null && (typeArguments.Nodes?.Length ?? 0) == 0)
        {
            var sourceFile = GetSourceFileOfNode(node)!;
            var start = typeArguments.Pos() - "<".Length;
            var end = SkipTrivia(sourceFile.Text(), typeArguments.End()) + ">".Length;
            return GrammarErrorAtPos(sourceFile, start, end - start, Type_argument_list_cannot_be_empty);
        }

        return false;
    }

    // ported from: internal/checker/grammarchecks.go:855-857
    internal bool CheckGrammarTypeArguments(Node node, NodeList? typeArguments)
    {
        return CheckGrammarForDisallowedTrailingComma(typeArguments, Trailing_comma_not_allowed) || CheckGrammarForAtLeastOneTypeArgument(node, typeArguments);
    }

    // ported from: internal/checker/grammarchecks.go:859-864
    internal bool CheckGrammarTaggedTemplateChain(TaggedTemplateExpression node)
    {
        if (node.QuestionDotToken is not null || (node.Flags & NodeFlags.OptionalChain) != 0)
        {
            return GrammarErrorOnNode(node.Template, Tagged_template_expressions_are_not_permitted_in_an_optional_chain);
        }

        return false;
    }

    // ported from: internal/checker/grammarchecks.go:866-883
    internal bool CheckGrammarHeritageClause(HeritageClause node)
    {
        var types = node.Types;
        if (CheckGrammarForDisallowedTrailingComma(types, Trailing_comma_not_allowed))
        {
            return true;
        }

        if (types is not null && (types.Nodes?.Length ?? 0) == 0)
        {
            var listType = TokenToString(node.Token);
            // TODO(danielr): why not error on the token?
            return GrammarErrorAtPos(node, types.Pos(), 0, X_0_list_cannot_be_empty, listType);
        }

        if (types?.Nodes is not null)
        {
            foreach (var type in types.Nodes) //nolint:modernize
            {
                if (CheckGrammarExpressionWithTypeArguments(type))
                {
                    return true;
                }
            }
        }

        return false;
    }

    // ported from: internal/checker/grammarchecks.go:885-890
    internal bool CheckGrammarExpressionWithTypeArguments(Node node)
    {
        if (IsExpressionWithTypeArguments(node) && node.Expression()!.Kind == Kind.ImportKeyword && node.TypeArgumentList() is not null)
        {
            return GrammarErrorOnNode(node, This_use_of_import_is_invalid_import_calls_can_be_written_but_they_must_have_parentheses_and_cannot_have_type_arguments);
        }

        return CheckGrammarTypeArguments(node, node.TypeArgumentList());
    }

    // ported from: internal/checker/grammarchecks.go:892-933
    internal bool CheckGrammarClassDeclarationHeritageClauses(Node node, SourceFile file)
    {
        var seenExtendsClause = false;
        var seenImplementsClause = false;

        var classLikeData = node.ClassLikeData()!;

        if (!CheckGrammarModifiers(node) && classLikeData.HeritageClauses is not null)
        {
            foreach (var heritageClauseNode in classLikeData.HeritageClauses.Nodes ?? [])
            {
                var heritageClause = heritageClauseNode.AsHeritageClause()!;
                if (heritageClause.Token == Kind.ExtendsKeyword)
                {
                    if (seenExtendsClause)
                    {
                        return GrammarErrorOnFirstToken(heritageClauseNode, X_extends_clause_already_seen);
                    }

                    if (seenImplementsClause)
                    {
                        return GrammarErrorOnFirstToken(heritageClauseNode, X_extends_clause_must_precede_implements_clause);
                    }

                    var typeNodes = heritageClause.Types!.Nodes ?? [];
                    if (typeNodes.Length > 1)
                    {
                        return GrammarErrorOnFirstToken(typeNodes[1], Classes_can_only_extend_a_single_class);
                    }

                    seenExtendsClause = true;
                }
                else
                {
                    if (heritageClause.Token != Kind.ImplementsKeyword)
                    {
                        Debug.Fail($"Unexpected token \"{heritageClause.Token.String()}\"");
                    }

                    if (seenImplementsClause)
                    {
                        return GrammarErrorOnFirstToken(heritageClauseNode, X_implements_clause_already_seen);
                    }

                    seenImplementsClause = true;
                }

                // Grammar checking heritageClause inside class declaration
                CheckGrammarHeritageClause(heritageClause);
            }
        }

        return false;
    }

    // ported from: internal/checker/grammarchecks.go:935-959
    internal bool CheckGrammarInterfaceDeclaration(InterfaceDeclaration node)
    {
        if (node.HeritageClauses is not null)
        {
            var seenExtendsClause = false;
            foreach (var heritageClauseNode in node.HeritageClauses.Nodes ?? [])
            {
                var heritageClause = heritageClauseNode.AsHeritageClause()!;

                switch (heritageClause.Token)
                {
                    case Kind.ExtendsKeyword:
                        if (seenExtendsClause)
                        {
                            return GrammarErrorOnFirstToken(heritageClauseNode, X_extends_clause_already_seen);
                        }

                        seenExtendsClause = true;
                        break;
                    case Kind.ImplementsKeyword:
                        return GrammarErrorOnFirstToken(heritageClauseNode, Interface_declaration_cannot_have_implements_clause);
                    default:
                        Debug.Fail($"Unexpected token \"{heritageClause.Token.String()}\"");
                        break;
                }

                // Grammar checking heritageClause inside class declaration
                CheckGrammarHeritageClause(heritageClause);
            }
        }

        return false;
    }

    // ported from: internal/checker/grammarchecks.go:961-972
    internal bool CheckGrammarComputedPropertyName(Node node)
    {
        // If node is not a computedPropertyName, just skip the grammar checking
        if (node.Kind != Kind.ComputedPropertyName)
        {
            return false;
        }

        var computedPropertyName = node.AsComputedPropertyName()!;
        if (computedPropertyName.Expression!.Kind == Kind.BinaryExpression && computedPropertyName.Expression.AsBinaryExpression()!.OperatorToken!.Kind == Kind.CommaToken)
        {
            return GrammarErrorOnNode(computedPropertyName.Expression, A_comma_expression_is_not_allowed_in_a_computed_property_name);
        }

        return false;
    }

    // ported from: internal/checker/grammarchecks.go:974-988
    internal bool CheckGrammarForGenerator(Node node)
    {
        var bodyData = node.BodyData();
        if (bodyData is not null && bodyData.AsteriskToken is not null)
        {
            if (node.Kind != Kind.FunctionDeclaration && node.Kind != Kind.FunctionExpression && node.Kind != Kind.MethodDeclaration)
            {
                Debug.Fail($"Unexpected node kind \"{node.Kind.String()}\"");
            }

            if ((node.Flags & NodeFlags.Ambient) != 0)
            {
                return GrammarErrorOnNode(bodyData.AsteriskToken, Generators_are_not_allowed_in_an_ambient_context);
            }

            if (bodyData.Body is null)
            {
                return GrammarErrorOnNode(bodyData.AsteriskToken, An_overload_signature_cannot_be_declared_as_a_generator);
            }
        }

        return false;
    }

    // ported from: internal/checker/grammarchecks.go:990-992
    internal bool CheckGrammarForInvalidQuestionMark(Node? postfixToken, Message message)
    {
        return postfixToken is not null && postfixToken.Kind == Kind.QuestionToken && GrammarErrorOnNode(postfixToken, message);
    }

    // ported from: internal/checker/grammarchecks.go:994-996
    internal bool CheckGrammarForInvalidExclamationToken(Node? postfixToken, Message message)
    {
        return postfixToken is not null && postfixToken.Kind == Kind.ExclamationToken && GrammarErrorOnNode(postfixToken, message);
    }
}
