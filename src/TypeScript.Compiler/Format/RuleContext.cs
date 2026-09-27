// ported from: internal/format/rulecontext.go
//
// 与 Go 的差异（C# 语言特性）：
// - 包级函数 → 静态类 `Format`（未导出的谓词 → internal + PascalCase）。
// - Go 的命名函数类型（`optionSelector`/`anyOptionSelector[T]`）→ C# 委托 `OptionSelector`/`AnyOptionSelector<T>`；
//   把函数当值传递（`isOptionEnabled(insertSpaceAfterCommaDelimiterOption)`）在 C# 里就是方法组转换。
// - `optionEquals[T comparable]` 的 `==` → `EqualityComparer<T>.Default.Equals`（T 只有
//   `SemicolonPreference` 一个实例化）。
// - Go 的 `case A, B: fallthrough` 链 → C# 的合并 case 标签（C# 只允许空 case 直落）。
// - `context.contextNode` 在 Go 里可能是 nil（`contextNode.Kind` 会 panic）；C# 用 `!`（UpdateContext 已断言非空）。
// - `panic(...)` → `Debug.Fail(...)`。
using System.Collections.Generic;
using TypeScript.Ast;
using TypeScript.Core;
using TypeScript.LsUtil;
using static TypeScript.Ast.Ast;
using static TypeScript.LsUtil.LsUtil;
using Scan = TypeScript.Scanner.Scanner;

namespace TypeScript.Format;

/// <summary>Go 的 <c>type optionSelector = func(options lsutil.FormatCodeSettings) core.Tristate</c>。</summary>
internal delegate Tristate OptionSelector(FormatCodeSettings options);

/// <summary>Go 的 <c>type anyOptionSelector[T comparable] = func(options lsutil.FormatCodeSettings) T</c>。</summary>
internal delegate T AnyOptionSelector<T>(FormatCodeSettings options);

public static partial class Format
{
    internal static SemicolonPreference SemicolonOption(FormatCodeSettings options)
    {
        return options.Semicolons;
    }

    internal static Tristate InsertSpaceAfterCommaDelimiterOption(FormatCodeSettings options)
    {
        return options.InsertSpaceAfterCommaDelimiter;
    }

    internal static Tristate InsertSpaceAfterSemicolonInForStatementsOption(FormatCodeSettings options)
    {
        return options.InsertSpaceAfterSemicolonInForStatements;
    }

    internal static Tristate InsertSpaceBeforeAndAfterBinaryOperatorsOption(FormatCodeSettings options)
    {
        return options.InsertSpaceBeforeAndAfterBinaryOperators;
    }

    internal static Tristate InsertSpaceAfterConstructorOption(FormatCodeSettings options)
    {
        return options.InsertSpaceAfterConstructor;
    }

    internal static Tristate InsertSpaceAfterKeywordsInControlFlowStatementsOption(FormatCodeSettings options)
    {
        return options.InsertSpaceAfterKeywordsInControlFlowStatements;
    }

    internal static Tristate InsertSpaceAfterFunctionKeywordForAnonymousFunctionsOption(FormatCodeSettings options)
    {
        return options.InsertSpaceAfterFunctionKeywordForAnonymousFunctions;
    }

    internal static Tristate InsertSpaceAfterOpeningAndBeforeClosingNonemptyParenthesisOption(FormatCodeSettings options)
    {
        return options.InsertSpaceAfterOpeningAndBeforeClosingNonemptyParenthesis;
    }

    internal static Tristate InsertSpaceAfterOpeningAndBeforeClosingNonemptyBracketsOption(FormatCodeSettings options)
    {
        return options.InsertSpaceAfterOpeningAndBeforeClosingNonemptyBrackets;
    }

    internal static Tristate InsertSpaceAfterOpeningAndBeforeClosingNonemptyBracesOption(FormatCodeSettings options)
    {
        return options.InsertSpaceAfterOpeningAndBeforeClosingNonemptyBraces;
    }

    internal static Tristate InsertSpaceAfterOpeningAndBeforeClosingEmptyBracesOption(FormatCodeSettings options)
    {
        return options.InsertSpaceAfterOpeningAndBeforeClosingEmptyBraces;
    }

    internal static Tristate InsertSpaceAfterOpeningAndBeforeClosingTemplateStringBracesOption(FormatCodeSettings options)
    {
        return options.InsertSpaceAfterOpeningAndBeforeClosingTemplateStringBraces;
    }

    internal static Tristate InsertSpaceAfterOpeningAndBeforeClosingJsxExpressionBracesOption(FormatCodeSettings options)
    {
        return options.InsertSpaceAfterOpeningAndBeforeClosingJsxExpressionBraces;
    }

    internal static Tristate InsertSpaceAfterTypeAssertionOption(FormatCodeSettings options)
    {
        return options.InsertSpaceAfterTypeAssertion;
    }

    internal static Tristate InsertSpaceBeforeFunctionParenthesisOption(FormatCodeSettings options)
    {
        return options.InsertSpaceBeforeFunctionParenthesis;
    }

    internal static Tristate PlaceOpenBraceOnNewLineForFunctionsOption(FormatCodeSettings options)
    {
        return options.PlaceOpenBraceOnNewLineForFunctions;
    }

    internal static Tristate PlaceOpenBraceOnNewLineForControlBlocksOption(FormatCodeSettings options)
    {
        return options.PlaceOpenBraceOnNewLineForControlBlocks;
    }

    internal static Tristate InsertSpaceBeforeTypeAnnotationOption(FormatCodeSettings options)
    {
        return options.InsertSpaceBeforeTypeAnnotation;
    }

    internal static Tristate IndentMultiLineObjectLiteralBeginningOnBlankLineOption(FormatCodeSettings options)
    {
        return options.IndentMultiLineObjectLiteralBeginningOnBlankLine;
    }

    internal static Tristate IndentSwitchCaseOption(FormatCodeSettings options)
    {
        return options.IndentSwitchCase;
    }

    internal static ContextPredicate OptionEquals<T>(AnyOptionSelector<T> optionName, T optionValue)
    {
        return context => EqualityComparer<T>.Default.Equals(optionName(context.Options), optionValue);
    }

    internal static ContextPredicate IsOptionEnabled(OptionSelector optionName)
    {
        return context => optionName(context.Options).IsTrue();
    }

    internal static ContextPredicate IsOptionDisabled(OptionSelector optionName)
    {
        return context => optionName(context.Options).IsFalse();
    }

    internal static ContextPredicate IsOptionDisabledOrUndefined(OptionSelector optionName)
    {
        return context => optionName(context.Options).IsFalseOrUnknown();
    }

    internal static ContextPredicate IsOptionDisabledOrUndefinedOrTokensOnSameLine(OptionSelector optionName)
    {
        return context => optionName(context.Options).IsFalseOrUnknown() || context.TokensAreOnSameLine();
    }

    internal static ContextPredicate IsOptionEnabledOrUndefined(OptionSelector optionName)
    {
        return context => optionName(context.Options).IsTrueOrUnknown();
    }

    internal static bool IsForContext(FormattingContext context)
    {
        return context._contextNode!.Kind == Kind.ForStatement;
    }

    internal static bool IsNotForContext(FormattingContext context)
    {
        return !IsForContext(context);
    }

    internal static bool IsBinaryOpContext(FormattingContext context)
    {
        switch (context._contextNode!.Kind)
        {
            case Kind.BinaryExpression:
                return context._contextNode!.AsBinaryExpression()!.OperatorToken!.Kind != Kind.CommaToken;
            case Kind.ConditionalExpression:
            case Kind.ConditionalType:
            case Kind.AsExpression:
            case Kind.ExportSpecifier:
            case Kind.ImportSpecifier:
            case Kind.TypePredicate:
            case Kind.UnionType:
            case Kind.IntersectionType:
            case Kind.SatisfiesExpression:
                return true;

            // equals in binding elements func foo([[x, y] = [1, 2]])
            case Kind.BindingElement:
            // equals in type X = ...
            case Kind.TypeAliasDeclaration:
            // equal in import a = module('a');
            case Kind.ImportEqualsDeclaration:
            // equal in export = 1
            case Kind.ExportAssignment:
            // equal in let a = 0
            case Kind.VariableDeclaration:
            // equal in p = 0
            case Kind.Parameter:
            case Kind.EnumMember:
            case Kind.PropertyDeclaration:
            case Kind.PropertySignature:
                return context._currentTokenSpan.Kind == Kind.EqualsToken || context._nextTokenSpan.Kind == Kind.EqualsToken;
            // "in" keyword in for (let x in []) { }
            case Kind.ForInStatement:
            // "in" keyword in [P in keyof T] T[P]
            case Kind.TypeParameter:
                return context._currentTokenSpan.Kind == Kind.InKeyword || context._nextTokenSpan.Kind == Kind.InKeyword || context._currentTokenSpan.Kind == Kind.EqualsToken || context._nextTokenSpan.Kind == Kind.EqualsToken;
            // Technically, "of" is not a binary operator, but format it the same way as "in"
            case Kind.ForOfStatement:
                return context._currentTokenSpan.Kind == Kind.OfKeyword || context._nextTokenSpan.Kind == Kind.OfKeyword;
        }

        return false;
    }

    internal static bool IsNotBinaryOpContext(FormattingContext context)
    {
        return !IsBinaryOpContext(context);
    }

    internal static bool IsNotTypeAnnotationContext(FormattingContext context)
    {
        return !IsTypeAnnotationContext(context);
    }

    internal static bool IsTypeAnnotationContext(FormattingContext context)
    {
        var contextKind = context._contextNode!.Kind;
        return contextKind == Kind.PropertyDeclaration ||
            contextKind == Kind.PropertySignature ||
            contextKind == Kind.Parameter ||
            contextKind == Kind.VariableDeclaration ||
            IsFunctionLikeKind(contextKind);
    }

    internal static bool IsOptionalPropertyContext(FormattingContext context)
    {
        return IsPropertyDeclaration(context._contextNode!) && HasQuestionToken(context._contextNode!);
    }

    internal static bool IsNonOptionalPropertyContext(FormattingContext context)
    {
        return !IsOptionalPropertyContext(context);
    }

    internal static bool IsConditionalOperatorContext(FormattingContext context)
    {
        return context._contextNode!.Kind == Kind.ConditionalExpression ||
            context._contextNode!.Kind == Kind.ConditionalType;
    }

    internal static bool IsSameLineTokenOrBeforeBlockContext(FormattingContext context)
    {
        return context.TokensAreOnSameLine() || IsBeforeBlockContext(context);
    }

    internal static bool IsBraceWrappedContext(FormattingContext context)
    {
        return context._contextNode!.Kind == Kind.ObjectBindingPattern ||
            context._contextNode!.Kind == Kind.MappedType ||
            IsSingleLineBlockContext(context);
    }

    // This check is done before an open brace in a control construct, a function, or a typescript block declaration
    internal static bool IsBeforeMultilineBlockContext(FormattingContext context)
    {
        return IsBeforeBlockContext(context) && !(context.NextNodeAllOnSameLine() || context.NextNodeBlockIsOnOneLine());
    }

    internal static bool IsMultilineBlockContext(FormattingContext context)
    {
        return IsBlockContext(context) && !(context.ContextNodeAllOnSameLine() || context.ContextNodeBlockIsOnOneLine());
    }

    internal static bool IsSingleLineBlockContext(FormattingContext context)
    {
        return IsBlockContext(context) && (context.ContextNodeAllOnSameLine() || context.ContextNodeBlockIsOnOneLine());
    }

    internal static bool IsBlockContext(FormattingContext context)
    {
        return NodeIsBlockContext(context._contextNode!);
    }

    internal static bool IsBeforeBlockContext(FormattingContext context)
    {
        return NodeIsBlockContext(context._nextTokenParent!);
    }

    // IMPORTANT!!! This method must return true ONLY for nodes with open and close braces as immediate children
    internal static bool NodeIsBlockContext(Node node)
    {
        if (NodeIsTypeScriptDeclWithBlockContext(node))
        {
            // This means we are in a context that looks like a block to the user, but in the grammar is actually not a node (it's a class, module, enum, object type literal, etc).
            return true;
        }

        switch (node.Kind)
        {
            case Kind.Block:
            case Kind.CaseBlock:
            case Kind.ObjectLiteralExpression:
            case Kind.ModuleBlock:
                return true;
        }

        return false;
    }

    internal static bool IsFunctionDeclContext(FormattingContext context)
    {
        switch (context._contextNode!.Kind)
        {
            case Kind.FunctionDeclaration:
            case Kind.MethodDeclaration:
            case Kind.MethodSignature:
            // case Kind.MemberFunctionDeclaration:
            case Kind.GetAccessor:
            case Kind.SetAccessor:
            // case Kind.MethodSignature:
            case Kind.CallSignature:
            case Kind.FunctionExpression:
            case Kind.Constructor:
            case Kind.ArrowFunction:
            // case Kind.ConstructorDeclaration:
            // case Kind.SimpleArrowFunctionExpression:
            // case Kind.ParenthesizedArrowFunctionExpression:
            case Kind.InterfaceDeclaration: // This one is not truly a function, but for formatting purposes, it acts just like one
                return true;
        }

        return false;
    }

    internal static bool IsNotFunctionDeclContext(FormattingContext context)
    {
        return !IsFunctionDeclContext(context);
    }

    internal static bool IsFunctionDeclarationOrFunctionExpressionContext(FormattingContext context)
    {
        return context._contextNode!.Kind == Kind.FunctionDeclaration || context._contextNode!.Kind == Kind.FunctionExpression;
    }

    internal static bool IsTypeScriptDeclWithBlockContext(FormattingContext context)
    {
        return NodeIsTypeScriptDeclWithBlockContext(context._contextNode!);
    }

    internal static bool NodeIsTypeScriptDeclWithBlockContext(Node node)
    {
        switch (node.Kind)
        {
            case Kind.ClassDeclaration:
            case Kind.ClassExpression:
            case Kind.InterfaceDeclaration:
            case Kind.EnumDeclaration:
            case Kind.TypeLiteral:
            case Kind.ModuleDeclaration:
            case Kind.ExportDeclaration:
            case Kind.NamedExports:
            case Kind.ImportDeclaration:
            case Kind.NamedImports:
                return true;
        }

        return false;
    }

    internal static bool IsAfterCodeBlockContext(FormattingContext context)
    {
        switch (context._currentTokenParent!.Kind)
        {
            case Kind.ClassDeclaration:
            case Kind.ModuleDeclaration:
            case Kind.EnumDeclaration:
            case Kind.CatchClause:
            case Kind.ModuleBlock:
            case Kind.SwitchStatement:
                return true;
            case Kind.Block:
                var blockParent = context._currentTokenParent!.Parent;
                // In a codefix scenario, we can't rely on parents being set. So just always return true.
                if (blockParent is null || (blockParent.Kind != Kind.ArrowFunction && blockParent.Kind != Kind.FunctionExpression))
                {
                    return true;
                }

                break;
        }

        return false;
    }

    internal static bool IsControlDeclContext(FormattingContext context)
    {
        switch (context._contextNode!.Kind)
        {
            case Kind.IfStatement:
            case Kind.SwitchStatement:
            case Kind.ForStatement:
            case Kind.ForInStatement:
            case Kind.ForOfStatement:
            case Kind.WhileStatement:
            case Kind.TryStatement:
            case Kind.DoStatement:
            case Kind.WithStatement:
            // TODO
            // case Kind.ElseClause:
            case Kind.CatchClause:
                return true;

            default:
                return false;
        }
    }

    internal static bool IsObjectContext(FormattingContext context)
    {
        return context._contextNode!.Kind == Kind.ObjectLiteralExpression;
    }

    internal static bool IsFunctionCallContext(FormattingContext context)
    {
        return context._contextNode!.Kind == Kind.CallExpression;
    }

    internal static bool IsNewContext(FormattingContext context)
    {
        return context._contextNode!.Kind == Kind.NewExpression;
    }

    internal static bool IsFunctionCallOrNewContext(FormattingContext context)
    {
        return IsFunctionCallContext(context) || IsNewContext(context);
    }

    internal static bool IsPreviousTokenNotComma(FormattingContext context)
    {
        return context._currentTokenSpan.Kind != Kind.CommaToken;
    }

    internal static bool IsNextTokenNotCloseBracket(FormattingContext context)
    {
        return context._nextTokenSpan.Kind != Kind.CloseBracketToken;
    }

    internal static bool IsNextTokenNotCloseParen(FormattingContext context)
    {
        return context._nextTokenSpan.Kind != Kind.CloseParenToken;
    }

    internal static bool IsArrowFunctionContext(FormattingContext context)
    {
        return context._contextNode!.Kind == Kind.ArrowFunction;
    }

    internal static bool IsImportTypeContext(FormattingContext context)
    {
        return context._contextNode!.Kind == Kind.ImportType;
    }

    internal static bool IsNonJsxSameLineTokenContext(FormattingContext context)
    {
        return context.TokensAreOnSameLine() && context._contextNode!.Kind != Kind.JsxText;
    }

    internal static bool IsNonJsxTextContext(FormattingContext context)
    {
        return context._contextNode!.Kind != Kind.JsxText;
    }

    internal static bool IsNonJsxElementOrFragmentContext(FormattingContext context)
    {
        return context._contextNode!.Kind != Kind.JsxElement && context._contextNode!.Kind != Kind.JsxFragment;
    }

    internal static bool IsJsxExpressionContext(FormattingContext context)
    {
        return context._contextNode!.Kind == Kind.JsxExpression || context._contextNode!.Kind == Kind.JsxSpreadAttribute;
    }

    internal static bool IsNextTokenParentJsxAttribute(FormattingContext context)
    {
        return context._nextTokenParent!.Kind == Kind.JsxAttribute || (context._nextTokenParent!.Kind == Kind.JsxNamespacedName && context._nextTokenParent!.Parent!.Kind == Kind.JsxAttribute);
    }

    internal static bool IsJsxAttributeContext(FormattingContext context)
    {
        return context._contextNode!.Kind == Kind.JsxAttribute;
    }

    internal static bool IsNextTokenParentNotJsxNamespacedName(FormattingContext context)
    {
        return context._nextTokenParent!.Kind != Kind.JsxNamespacedName;
    }

    internal static bool IsNextTokenParentJsxNamespacedName(FormattingContext context)
    {
        return context._nextTokenParent!.Kind == Kind.JsxNamespacedName;
    }

    internal static bool IsJsxSelfClosingElementContext(FormattingContext context)
    {
        return context._contextNode!.Kind == Kind.JsxSelfClosingElement;
    }

    internal static bool IsNotBeforeBlockInFunctionDeclarationContext(FormattingContext context)
    {
        return !IsFunctionDeclContext(context) && !IsBeforeBlockContext(context);
    }

    internal static bool IsEndOfDecoratorContextOnSameLine(FormattingContext context)
    {
        return context.TokensAreOnSameLine() &&
            HasDecorators(context._contextNode!) &&
            NodeIsInDecoratorContext(context._currentTokenParent!) &&
            !NodeIsInDecoratorContext(context._nextTokenParent!);
    }

    internal static bool NodeIsInDecoratorContext(Node? node)
    {
        while (node is not null && IsExpression(node))
        {
            node = node.Parent;
        }

        return node is not null && node.Kind == Kind.Decorator;
    }

    internal static bool IsStartOfVariableDeclarationList(FormattingContext context)
    {
        return context._currentTokenParent!.Kind == Kind.VariableDeclarationList &&
            Scan.GetTokenPosOfNode(context._currentTokenParent!, context.SourceFile, false) == context._currentTokenSpan.Loc.Pos();
    }

    internal static bool IsNotFormatOnEnter(FormattingContext context)
    {
        return context.FormattingRequestKind != FormatRequestKind.FormatOnEnter;
    }

    internal static bool IsModuleDeclContext(FormattingContext context)
    {
        return context._contextNode!.Kind == Kind.ModuleDeclaration;
    }

    internal static bool IsObjectTypeContext(FormattingContext context)
    {
        return context._contextNode!.Kind == Kind.TypeLiteral; // && context.contextNode.parent.Kind != ast.KindInterfaceDeclaration;
    }

    internal static bool IsConstructorSignatureContext(FormattingContext context)
    {
        return context._contextNode!.Kind == Kind.ConstructSignature;
    }

    internal static bool IsTypeArgumentOrParameterOrAssertion(TextRangeWithKind token, Node parent)
    {
        if (token.Kind != Kind.LessThanToken && token.Kind != Kind.GreaterThanToken)
        {
            return false;
        }

        switch (parent.Kind)
        {
            case Kind.TypeReference:
            case Kind.TypeAssertionExpression:
            case Kind.TypeAliasDeclaration:
            case Kind.ClassDeclaration:
            case Kind.ClassExpression:
            case Kind.InterfaceDeclaration:
            case Kind.FunctionDeclaration:
            case Kind.FunctionExpression:
            case Kind.ArrowFunction:
            case Kind.MethodDeclaration:
            case Kind.MethodSignature:
            case Kind.CallSignature:
            case Kind.ConstructSignature:
            case Kind.CallExpression:
            case Kind.NewExpression:
            case Kind.ExpressionWithTypeArguments:
                return true;
            default:
                return false;
        }
    }

    internal static bool IsTypeArgumentOrParameterOrAssertionContext(FormattingContext context)
    {
        return IsTypeArgumentOrParameterOrAssertion(context._currentTokenSpan, context._currentTokenParent!) ||
            IsTypeArgumentOrParameterOrAssertion(context._nextTokenSpan, context._nextTokenParent!);
    }

    internal static bool IsTypeAssertionContext(FormattingContext context)
    {
        return context._contextNode!.Kind == Kind.TypeAssertionExpression;
    }

    internal static bool IsNonTypeAssertionContext(FormattingContext context)
    {
        return !IsTypeAssertionContext(context);
    }

    internal static bool IsVoidOpContext(FormattingContext context)
    {
        return context._currentTokenSpan.Kind == Kind.VoidKeyword && context._currentTokenParent!.Kind == Kind.VoidExpression;
    }

    internal static bool IsYieldOrYieldStarWithOperand(FormattingContext context)
    {
        return context._contextNode!.Kind == Kind.YieldExpression && context._contextNode!.Expression() is not null;
    }

    internal static bool IsNonNullAssertionContext(FormattingContext context)
    {
        return context._contextNode!.Kind == Kind.NonNullExpression;
    }

    internal static bool IsNotStatementConditionContext(FormattingContext context)
    {
        return !IsStatementConditionContext(context);
    }

    internal static bool IsStatementConditionContext(FormattingContext context)
    {
        switch (context._contextNode!.Kind)
        {
            case Kind.IfStatement:
            case Kind.ForStatement:
            case Kind.ForInStatement:
            case Kind.ForOfStatement:
            case Kind.DoStatement:
            case Kind.WhileStatement:
                return true;

            default:
                return false;
        }
    }

    internal static bool IsSemicolonDeletionContext(FormattingContext context)
    {
        var nextTokenKind = context._nextTokenSpan.Kind;
        var nextTokenStart = context._nextTokenSpan.Loc.Pos();
        if (IsTrivia(nextTokenKind))
        {
            Node? nextRealToken;
            if (ReferenceEquals(context._nextTokenParent, context._currentTokenParent))
            {
                // !!! TODO: very different from strada, but strada's logic here is wonky - find the first ancestor without a parent? that's just the source file.
                nextRealToken = AstNav.AstNav.FindNextToken(context._nextTokenParent!, context.SourceFile.AsNode(), context.SourceFile);
            }
            else
            {
                nextRealToken = GetFirstToken(context._nextTokenParent!, context.SourceFile);
            }

            if (nextRealToken is null)
            {
                return true;
            }

            nextTokenKind = nextRealToken.Kind;
            nextTokenStart = Scan.GetTokenPosOfNode(nextRealToken, context.SourceFile, false);
        }

        var startLine = Scan.GetECMALineOfPosition(context.SourceFile, context._currentTokenSpan.Loc.Pos());
        var endLine = Scan.GetECMALineOfPosition(context.SourceFile, nextTokenStart);
        if (startLine == endLine)
        {
            return nextTokenKind == Kind.CloseBraceToken || nextTokenKind == Kind.EndOfFile;
        }

        if (nextTokenKind == Kind.SemicolonToken &&
            context._currentTokenSpan.Kind == Kind.SemicolonToken)
        {
            return true;
        }

        if (nextTokenKind == Kind.SemicolonClassElement ||
            nextTokenKind == Kind.SemicolonToken)
        {
            return false;
        }

        if (context._contextNode!.Kind == Kind.InterfaceDeclaration ||
            context._contextNode!.Kind == Kind.TypeAliasDeclaration)
        {
            // Can't remove semicolon after `foo`; it would parse as a method declaration:
            //
            // interface I {
            //   foo;
            //   () void
            // }
            return context._currentTokenParent!.Kind != Kind.PropertySignature ||
                context._currentTokenParent!.Type() is not null ||
                nextTokenKind != Kind.OpenParenToken;
        }

        if (IsPropertyDeclaration(context._currentTokenParent!))
        {
            return context._currentTokenParent!.Initializer() is null;
        }

        return context._currentTokenParent!.Kind != Kind.ForStatement &&
            context._currentTokenParent!.Kind != Kind.EmptyStatement &&
            context._currentTokenParent!.Kind != Kind.SemicolonClassElement &&
            nextTokenKind != Kind.OpenBracketToken &&
            nextTokenKind != Kind.OpenParenToken &&
            nextTokenKind != Kind.PlusToken &&
            nextTokenKind != Kind.MinusToken &&
            nextTokenKind != Kind.SlashToken &&
            nextTokenKind != Kind.RegularExpressionLiteral &&
            nextTokenKind != Kind.CommaToken &&
            nextTokenKind != Kind.TemplateExpression &&
            nextTokenKind != Kind.TemplateHead &&
            nextTokenKind != Kind.NoSubstitutionTemplateLiteral &&
            nextTokenKind != Kind.DotToken;
    }

    internal static bool IsSemicolonInsertionContext(FormattingContext context)
    {
        return PositionIsASICandidate(context._currentTokenSpan.Loc.End(), context._currentTokenParent, context.SourceFile);
    }

    internal static bool IsNotPropertyAccessOnIntegerLiteral(FormattingContext context)
    {
        return !IsPropertyAccessExpression(context._contextNode!) ||
            !IsNumericLiteral(context._contextNode!.Expression()!) ||
            context._contextNode!.Expression()!.Text().Contains('.');
    }
}
