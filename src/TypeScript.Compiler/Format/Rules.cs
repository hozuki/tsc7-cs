// ported from: internal/format/rules.go
//
// 与 Go 的差异（C# 语言特性 / 已核实的行为差异）：
// - 包级函数 → 静态类 `Format`。规则表是纯数据，构造一次后只读。
// - `append([]ast.Kind{…}, typeKeywords...)` → C# 集合表达式 `[Kind.Identifier, .. typeKeywords]`；
//   `allTokens` 用 `List<Kind>`（Go 的 `make(…, 0, cap)` + append 预占容量在 C# 里没有 len&lt;cap 视图，§3.8）。
// - **Go 的 in-place append 别名（不复现，已在报告里说明）**：`tokenRangeFromEx(prefix, tokens...)` 在 Go 里是
//   `append(prefix, tokens...)`；`allTokens` 的 cap(167) 恰好比 len(166) 大 1，所以
//   `anyTokenIncludingMultilineComments := tokenRangeFromEx(allTokens, KindMultiLineCommentTrivia)` 与
//   `anyTokenIncludingEOF := tokenRangeFromEx(allTokens, KindEndOfFile)` **共享同一个底层数组**，第二次 append
//   把 index 166 从 MultiLineCommentTrivia 覆写成 EndOfFile —— 于是 Go 里 `anyTokenIncludingMultilineComments`
//   实际等于「全部 token + EndOfFile」（MultiLineCommentTrivia 本来就在 allTokens 里，因为
//   `KindFirstToken = KindUnknown = 0`、trivia 的 kind 号 2/3 都落在 [0, 166] 内）。
//   C# 的集合表达式必然分配新数组，不会产生别名，因此 `AnyTokenIncludingMultilineComments` 就是
//   allTokens + MultiLineCommentTrivia（即 allTokens 本身）。差异只影响「左 token = EndOfFile」的规则配对，
//   而 `NewLineBeforeCloseBraceInBlockContext`/`NewLineBeforeOpenBraceInFunction` 这类规则的左 token 不可能是
//   EOF（EOF 之后没有 `}` 也没有 `{`），因此不可观察。
using TypeScript.Ast;
using TypeScript.Core;
using TypeScript.LsUtil;
using static TypeScript.Ast.Ast;
using static TypeScript.LsUtil.LsUtil;

namespace TypeScript.Format;

public static partial class Format
{
    internal static RuleSpec[] GetAllRules()
    {
        var allTokens = new List<Kind>(Kind.LastToken - Kind.FirstToken + 1);
        for (var token = Kind.FirstToken; token <= Kind.LastToken; token++)
        {
            if (token != Kind.EndOfFile)
            {
                allTokens.Add(token);
            }
        }

        TokenRange AnyTokenExcept(params ReadOnlySpan<Kind> tokens)
        {
            var newTokens = new List<Kind>(allTokens.Count);
            foreach (var token in allTokens)
            {
                if (tokens.Contains(token))
                {
                    continue;
                }

                newTokens.Add(token);
            }

            return new TokenRange
            {
                _isSpecific = false,
                _tokens = [.. newTokens],
            };
        }

        var anyToken = new TokenRange
        {
            _isSpecific = false,
            _tokens = [.. allTokens],
        };

        var anyTokenIncludingMultilineComments = TokenRangeFromEx([.. allTokens], Kind.MultiLineCommentTrivia);
        var anyTokenIncludingEOF = TokenRangeFromEx([.. allTokens], Kind.EndOfFile);
        var keywords = TokenRangeFromRange(Kind.FirstKeyword, Kind.LastKeyword);
        var binaryOperators = TokenRangeFromRange(Kind.FirstBinaryOperator, Kind.LastBinaryOperator);
        Kind[] binaryKeywordOperators =
        [
            Kind.InKeyword,
            Kind.InstanceOfKeyword,
            Kind.OfKeyword,
            Kind.AsKeyword,
            Kind.IsKeyword,
            Kind.SatisfiesKeyword,
        ];
        Kind[] unaryPrefixOperators = [Kind.PlusPlusToken, Kind.MinusToken, Kind.TildeToken, Kind.ExclamationToken];
        Kind[] unaryPrefixExpressions =
        [
            Kind.NumericLiteral,
            Kind.BigIntLiteral,
            Kind.Identifier,
            Kind.OpenParenToken,
            Kind.OpenBracketToken,
            Kind.OpenBraceToken,
            Kind.ThisKeyword,
            Kind.NewKeyword,
        ];
        Kind[] unaryPreincrementExpressions = [Kind.Identifier, Kind.OpenParenToken, Kind.ThisKeyword, Kind.NewKeyword];
        Kind[] unaryPostincrementExpressions = [Kind.Identifier, Kind.CloseParenToken, Kind.CloseBracketToken, Kind.NewKeyword];
        Kind[] unaryPredecrementExpressions = [Kind.Identifier, Kind.OpenParenToken, Kind.ThisKeyword, Kind.NewKeyword];
        Kind[] unaryPostdecrementExpressions = [Kind.Identifier, Kind.CloseParenToken, Kind.CloseBracketToken, Kind.NewKeyword];
        Kind[] comments = [Kind.SingleLineCommentTrivia, Kind.MultiLineCommentTrivia];
        Kind[] typeKeywords =
        [
            Kind.AnyKeyword,
            Kind.AssertsKeyword,
            Kind.BigIntKeyword,
            Kind.BooleanKeyword,
            Kind.FalseKeyword,
            Kind.InferKeyword,
            Kind.KeyOfKeyword,
            Kind.NeverKeyword,
            Kind.NullKeyword,
            Kind.NumberKeyword,
            Kind.ObjectKeyword,
            Kind.ReadonlyKeyword,
            Kind.StringKeyword,
            Kind.SymbolKeyword,
            Kind.TypeOfKeyword,
            Kind.TrueKeyword,
            Kind.VoidKeyword,
            Kind.UndefinedKeyword,
            Kind.UniqueKeyword,
            Kind.UnknownKeyword,
        ];
        Kind[] typeNames = [Kind.Identifier, .. typeKeywords];

        // Place a space before open brace in a function declaration
        // TypeScript: Function can have return types, which can be made of tons of different token kinds
        var functionOpenBraceLeftTokenRange = anyTokenIncludingMultilineComments;

        // Place a space before open brace in a TypeScript declaration that has braces as children (class, module, enum, etc)
        var typeScriptOpenBraceLeftTokenRange = TokenRangeFrom(Kind.Identifier, Kind.GreaterThanToken, Kind.MultiLineCommentTrivia, Kind.ClassKeyword, Kind.ExportKeyword, Kind.ImportKeyword);

        // Place a space before open brace in a control flow construct
        var controlOpenBraceLeftTokenRange = TokenRangeFrom(Kind.CloseParenToken, Kind.MultiLineCommentTrivia, Kind.DoKeyword, Kind.TryKeyword, Kind.FinallyKeyword, Kind.ElseKeyword, Kind.CatchKeyword);

        // These rules are higher in priority than user-configurable
        RuleSpec[] highPriorityCommonRules =
        [
            // Leave comments alone
            Rule("IgnoreBeforeComment", anyToken, comments, AnyContext, RuleAction.StopProcessingSpaceActions),
            Rule("IgnoreAfterLineComment", Kind.SingleLineCommentTrivia, anyToken, AnyContext, RuleAction.StopProcessingSpaceActions),

            Rule("NotSpaceBeforeColon", anyToken, Kind.ColonToken, [IsNonJsxSameLineTokenContext, IsNotBinaryOpContext, IsNotTypeAnnotationContext], RuleAction.DeleteSpace),
            Rule("SpaceAfterColon", Kind.ColonToken, anyToken, [IsNonJsxSameLineTokenContext, IsNotBinaryOpContext, IsNextTokenParentNotJsxNamespacedName], RuleAction.InsertSpace),
            Rule("NoSpaceBeforeQuestionMark", anyToken, Kind.QuestionToken, [IsNonJsxSameLineTokenContext, IsNotBinaryOpContext, IsNotTypeAnnotationContext], RuleAction.DeleteSpace),
            // insert space after '?' only when it is used in conditional operator
            Rule("SpaceAfterQuestionMarkInConditionalOperator", Kind.QuestionToken, anyToken, [IsNonJsxSameLineTokenContext, IsConditionalOperatorContext], RuleAction.InsertSpace),

            // in other cases there should be no space between '?' and next token
            Rule("NoSpaceAfterQuestionMark", Kind.QuestionToken, anyToken, [IsNonJsxSameLineTokenContext, IsNonOptionalPropertyContext], RuleAction.DeleteSpace),

            Rule("NoSpaceBeforeDot", anyToken, new[] { Kind.DotToken, Kind.QuestionDotToken }, [IsNonJsxSameLineTokenContext, IsNotPropertyAccessOnIntegerLiteral], RuleAction.DeleteSpace),
            Rule("NoSpaceAfterDot", new[] { Kind.DotToken, Kind.QuestionDotToken }, anyToken, [IsNonJsxSameLineTokenContext], RuleAction.DeleteSpace),

            Rule("NoSpaceBetweenImportParenInImportType", Kind.ImportKeyword, Kind.OpenParenToken, [IsNonJsxSameLineTokenContext, IsImportTypeContext], RuleAction.DeleteSpace),

            // Special handling of unary operators.
            // Prefix operators generally shouldn't have a space between
            // them and their target unary expression.
            Rule("NoSpaceAfterUnaryPrefixOperator", unaryPrefixOperators, unaryPrefixExpressions, [IsNonJsxSameLineTokenContext, IsNotBinaryOpContext], RuleAction.DeleteSpace),
            Rule("NoSpaceAfterUnaryPreincrementOperator", Kind.PlusPlusToken, unaryPreincrementExpressions, [IsNonJsxSameLineTokenContext], RuleAction.DeleteSpace),
            Rule("NoSpaceAfterUnaryPredecrementOperator", Kind.MinusMinusToken, unaryPredecrementExpressions, [IsNonJsxSameLineTokenContext], RuleAction.DeleteSpace),
            Rule("NoSpaceBeforeUnaryPostincrementOperator", unaryPostincrementExpressions, Kind.PlusPlusToken, [IsNonJsxSameLineTokenContext, IsNotStatementConditionContext], RuleAction.DeleteSpace),
            Rule("NoSpaceBeforeUnaryPostdecrementOperator", unaryPostdecrementExpressions, Kind.MinusMinusToken, [IsNonJsxSameLineTokenContext, IsNotStatementConditionContext], RuleAction.DeleteSpace),

            // More unary operator special-casing.
            // DevDiv 181814: Be careful when removing leading whitespace
            // around unary operators.  Examples:
            //      1 - -2  --X--> 1--2
            //      a + ++b --X--> a+++b
            Rule("SpaceAfterPostincrementWhenFollowedByAdd", Kind.PlusPlusToken, Kind.PlusToken, [IsNonJsxSameLineTokenContext, IsBinaryOpContext], RuleAction.InsertSpace),
            Rule("SpaceAfterAddWhenFollowedByUnaryPlus", Kind.PlusToken, Kind.PlusToken, [IsNonJsxSameLineTokenContext, IsBinaryOpContext], RuleAction.InsertSpace),
            Rule("SpaceAfterAddWhenFollowedByPreincrement", Kind.PlusToken, Kind.PlusPlusToken, [IsNonJsxSameLineTokenContext, IsBinaryOpContext], RuleAction.InsertSpace),
            Rule("SpaceAfterPostdecrementWhenFollowedBySubtract", Kind.MinusMinusToken, Kind.MinusToken, [IsNonJsxSameLineTokenContext, IsBinaryOpContext], RuleAction.InsertSpace),
            Rule("SpaceAfterSubtractWhenFollowedByUnaryMinus", Kind.MinusToken, Kind.MinusToken, [IsNonJsxSameLineTokenContext, IsBinaryOpContext], RuleAction.InsertSpace),
            Rule("SpaceAfterSubtractWhenFollowedByPredecrement", Kind.MinusToken, Kind.MinusMinusToken, [IsNonJsxSameLineTokenContext, IsBinaryOpContext], RuleAction.InsertSpace),

            Rule("NoSpaceAfterCloseBrace", Kind.CloseBraceToken, new[] { Kind.CommaToken, Kind.SemicolonToken }, [IsNonJsxSameLineTokenContext], RuleAction.DeleteSpace),
            // For functions and control block place } on a new line [multi-line rule]
            Rule("NewLineBeforeCloseBraceInBlockContext", anyTokenIncludingMultilineComments, Kind.CloseBraceToken, [IsMultilineBlockContext], RuleAction.InsertNewLine),

            // Space/new line after }.
            Rule("SpaceAfterCloseBrace", Kind.CloseBraceToken, AnyTokenExcept(Kind.CloseParenToken), [IsNonJsxSameLineTokenContext, IsAfterCodeBlockContext], RuleAction.InsertSpace),
            // Special case for (}, else) and (}, while) since else & while tokens are not part of the tree which makes SpaceAfterCloseBrace rule not applied
            // Also should not apply to })
            Rule("SpaceBetweenCloseBraceAndElse", Kind.CloseBraceToken, Kind.ElseKeyword, [IsNonJsxSameLineTokenContext], RuleAction.InsertSpace),
            Rule("SpaceBetweenCloseBraceAndWhile", Kind.CloseBraceToken, Kind.WhileKeyword, [IsNonJsxSameLineTokenContext], RuleAction.InsertSpace),
            Rule("NoSpaceBetweenEmptyBraceBrackets", Kind.OpenBraceToken, Kind.CloseBraceToken, [IsNonJsxSameLineTokenContext, IsObjectContext], RuleAction.DeleteSpace),

            // Add a space after control dec context if the next character is an open bracket ex: 'if (false) {a, b} = {1, 2};' -> 'if (false) {a, b} = {1, 2};'
            Rule("SpaceAfterConditionalClosingParen", Kind.CloseParenToken, Kind.OpenBracketToken, [IsControlDeclContext], RuleAction.InsertSpace),

            Rule("NoSpaceBetweenFunctionKeywordAndStar", Kind.FunctionKeyword, Kind.AsteriskToken, [IsFunctionDeclarationOrFunctionExpressionContext], RuleAction.DeleteSpace),
            Rule("SpaceAfterStarInGeneratorDeclaration", Kind.AsteriskToken, Kind.Identifier, [IsFunctionDeclarationOrFunctionExpressionContext], RuleAction.InsertSpace),

            Rule("SpaceAfterFunctionInFuncDecl", Kind.FunctionKeyword, anyToken, [IsFunctionDeclContext], RuleAction.InsertSpace),
            // Insert new line after { and before } in multi-line contexts.
            Rule("NewLineAfterOpenBraceInBlockContext", Kind.OpenBraceToken, anyToken, [IsMultilineBlockContext], RuleAction.InsertNewLine),

            // For get/set members, we check for (identifier,identifier) since get/set don't have tokens and they are represented as just an identifier token.
            // Though, we do extra check on the context to make sure we are dealing with get/set node. Example:
            //      get x() {}
            //      set x(val) {}
            Rule("SpaceAfterGetSetInMember", new[] { Kind.GetKeyword, Kind.SetKeyword }, Kind.Identifier, [IsFunctionDeclContext], RuleAction.InsertSpace),

            Rule("NoSpaceBetweenYieldKeywordAndStar", Kind.YieldKeyword, Kind.AsteriskToken, [IsNonJsxSameLineTokenContext, IsYieldOrYieldStarWithOperand], RuleAction.DeleteSpace),
            Rule("SpaceBetweenYieldOrYieldStarAndOperand", new[] { Kind.YieldKeyword, Kind.AsteriskToken }, anyToken, [IsNonJsxSameLineTokenContext, IsYieldOrYieldStarWithOperand], RuleAction.InsertSpace),

            Rule("NoSpaceBetweenReturnAndSemicolon", Kind.ReturnKeyword, Kind.SemicolonToken, [IsNonJsxSameLineTokenContext], RuleAction.DeleteSpace),
            Rule("SpaceAfterCertainKeywords", new[] { Kind.VarKeyword, Kind.ThrowKeyword, Kind.NewKeyword, Kind.DeleteKeyword, Kind.ReturnKeyword, Kind.TypeOfKeyword, Kind.AwaitKeyword }, anyToken, [IsNonJsxSameLineTokenContext], RuleAction.InsertSpace),
            Rule("SpaceAfterLetConstInVariableDeclaration", new[] { Kind.LetKeyword, Kind.ConstKeyword }, anyToken, [IsNonJsxSameLineTokenContext, IsStartOfVariableDeclarationList], RuleAction.InsertSpace),
            Rule("NoSpaceBeforeOpenParenInFuncCall", anyToken, Kind.OpenParenToken, [IsNonJsxSameLineTokenContext, IsFunctionCallOrNewContext, IsPreviousTokenNotComma], RuleAction.DeleteSpace),

            // Special case for binary operators (that are keywords). For these we have to add a space and shouldn't follow any user options.
            Rule("SpaceBeforeBinaryKeywordOperator", anyToken, binaryKeywordOperators, [IsNonJsxSameLineTokenContext, IsBinaryOpContext], RuleAction.InsertSpace),
            Rule("SpaceAfterBinaryKeywordOperator", binaryKeywordOperators, anyToken, [IsNonJsxSameLineTokenContext, IsBinaryOpContext], RuleAction.InsertSpace),

            Rule("SpaceAfterVoidOperator", Kind.VoidKeyword, anyToken, [IsNonJsxSameLineTokenContext, IsVoidOpContext], RuleAction.InsertSpace),

            // Async-await
            Rule("SpaceBetweenAsyncAndOpenParen", Kind.AsyncKeyword, Kind.OpenParenToken, [IsArrowFunctionContext, IsNonJsxSameLineTokenContext], RuleAction.InsertSpace),
            Rule("SpaceBetweenAsyncAndFunctionKeyword", Kind.AsyncKeyword, new[] { Kind.FunctionKeyword, Kind.Identifier }, [IsNonJsxSameLineTokenContext], RuleAction.InsertSpace),

            // Template string
            Rule("NoSpaceBetweenTagAndTemplateString", new[] { Kind.Identifier, Kind.CloseParenToken }, new[] { Kind.NoSubstitutionTemplateLiteral, Kind.TemplateHead }, [IsNonJsxSameLineTokenContext], RuleAction.DeleteSpace),

            // JSX opening elements
            Rule("SpaceBeforeJsxAttribute", anyToken, Kind.Identifier, [IsNextTokenParentJsxAttribute, IsNonJsxSameLineTokenContext], RuleAction.InsertSpace),
            Rule("SpaceBeforeSlashInJsxOpeningElement", anyToken, Kind.SlashToken, [IsJsxSelfClosingElementContext, IsNonJsxSameLineTokenContext], RuleAction.InsertSpace),
            Rule("NoSpaceBeforeGreaterThanTokenInJsxOpeningElement", Kind.SlashToken, Kind.GreaterThanToken, [IsJsxSelfClosingElementContext, IsNonJsxSameLineTokenContext], RuleAction.DeleteSpace),
            Rule("NoSpaceBeforeEqualInJsxAttribute", anyToken, Kind.EqualsToken, [IsJsxAttributeContext, IsNonJsxSameLineTokenContext], RuleAction.DeleteSpace),
            Rule("NoSpaceAfterEqualInJsxAttribute", Kind.EqualsToken, anyToken, [IsJsxAttributeContext, IsNonJsxSameLineTokenContext], RuleAction.DeleteSpace),
            Rule("NoSpaceBeforeJsxNamespaceColon", Kind.Identifier, Kind.ColonToken, [IsNextTokenParentJsxNamespacedName], RuleAction.DeleteSpace),
            Rule("NoSpaceAfterJsxNamespaceColon", Kind.ColonToken, Kind.Identifier, [IsNextTokenParentJsxNamespacedName], RuleAction.DeleteSpace),

            // TypeScript-specific rules
            // Use of module as a function call. e.g.: import m2 = module("m2");
            Rule("NoSpaceAfterModuleImport", new[] { Kind.ModuleKeyword, Kind.RequireKeyword }, Kind.OpenParenToken, [IsNonJsxSameLineTokenContext], RuleAction.DeleteSpace),
            // Add a space around certain TypeScript keywords
            Rule(
                "SpaceAfterCertainTypeScriptKeywords",
                new[]
                {
                    Kind.AbstractKeyword,
                    Kind.AccessorKeyword,
                    Kind.ClassKeyword,
                    Kind.DeclareKeyword,
                    Kind.DefaultKeyword,
                    Kind.EnumKeyword,
                    Kind.ExportKeyword,
                    Kind.ExtendsKeyword,
                    Kind.GetKeyword,
                    Kind.ImplementsKeyword,
                    Kind.ImportKeyword,
                    Kind.InterfaceKeyword,
                    Kind.ModuleKeyword,
                    Kind.NamespaceKeyword,
                    Kind.OverrideKeyword,
                    Kind.PrivateKeyword,
                    Kind.PublicKeyword,
                    Kind.ProtectedKeyword,
                    Kind.ReadonlyKeyword,
                    Kind.SetKeyword,
                    Kind.StaticKeyword,
                    Kind.TypeKeyword,
                    Kind.FromKeyword,
                    Kind.KeyOfKeyword,
                    Kind.InferKeyword,
                },
                anyToken,
                [IsNonJsxSameLineTokenContext],
                RuleAction.InsertSpace),
            Rule(
                "SpaceBeforeCertainTypeScriptKeywords",
                anyToken,
                new[] { Kind.ExtendsKeyword, Kind.ImplementsKeyword, Kind.FromKeyword },
                [IsNonJsxSameLineTokenContext],
                RuleAction.InsertSpace),
            // Treat string literals in module names as identifiers, and add a space between the literal and the opening Brace braces, e.g.: module "m2" {
            Rule("SpaceAfterModuleName", Kind.StringLiteral, Kind.OpenBraceToken, [IsModuleDeclContext], RuleAction.InsertSpace),

            // Lambda expressions
            Rule("SpaceBeforeArrow", anyToken, Kind.EqualsGreaterThanToken, [IsNonJsxSameLineTokenContext], RuleAction.InsertSpace),
            Rule("SpaceAfterArrow", Kind.EqualsGreaterThanToken, anyToken, [IsNonJsxSameLineTokenContext], RuleAction.InsertSpace),

            // Optional parameters and let args
            Rule("NoSpaceAfterEllipsis", Kind.DotDotDotToken, Kind.Identifier, [IsNonJsxSameLineTokenContext], RuleAction.DeleteSpace),
            Rule("NoSpaceAfterOptionalParameters", Kind.QuestionToken, new[] { Kind.CloseParenToken, Kind.CommaToken }, [IsNonJsxSameLineTokenContext, IsNotBinaryOpContext], RuleAction.DeleteSpace),

            // Remove spaces in empty interface literals. e.g.: x: {}
            Rule("NoSpaceBetweenEmptyInterfaceBraceBrackets", Kind.OpenBraceToken, Kind.CloseBraceToken, [IsNonJsxSameLineTokenContext, IsObjectTypeContext], RuleAction.DeleteSpace),

            // generics and type assertions
            Rule("NoSpaceBeforeOpenAngularBracket", typeNames, Kind.LessThanToken, [IsNonJsxSameLineTokenContext, IsTypeArgumentOrParameterOrAssertionContext], RuleAction.DeleteSpace),
            Rule("NoSpaceBetweenCloseParenAndAngularBracket", Kind.CloseParenToken, Kind.LessThanToken, [IsNonJsxSameLineTokenContext, IsTypeArgumentOrParameterOrAssertionContext], RuleAction.DeleteSpace),
            Rule("NoSpaceAfterOpenAngularBracket", Kind.LessThanToken, anyToken, [IsNonJsxSameLineTokenContext, IsTypeArgumentOrParameterOrAssertionContext], RuleAction.DeleteSpace),
            Rule("NoSpaceBeforeCloseAngularBracket", anyToken, Kind.GreaterThanToken, [IsNonJsxSameLineTokenContext, IsTypeArgumentOrParameterOrAssertionContext], RuleAction.DeleteSpace),
            Rule("NoSpaceAfterCloseAngularBracket", Kind.GreaterThanToken, new[] { Kind.OpenParenToken, Kind.OpenBracketToken, Kind.GreaterThanToken, Kind.CommaToken }, [
                IsNonJsxSameLineTokenContext,
                IsTypeArgumentOrParameterOrAssertionContext,
                IsNotFunctionDeclContext, /*To prevent an interference with the SpaceBeforeOpenParenInFuncDecl rule*/
                IsNonTypeAssertionContext,
            ], RuleAction.DeleteSpace),

            // decorators
            Rule("SpaceBeforeAt", new[] { Kind.CloseParenToken, Kind.Identifier }, Kind.AtToken, [IsNonJsxSameLineTokenContext], RuleAction.InsertSpace),
            Rule("NoSpaceAfterAt", Kind.AtToken, anyToken, [IsNonJsxSameLineTokenContext], RuleAction.DeleteSpace),
            // Insert space after @ in decorator
            Rule(
                "SpaceAfterDecorator",
                anyToken,
                new[]
                {
                    Kind.AbstractKeyword,
                    Kind.Identifier,
                    Kind.ExportKeyword,
                    Kind.DefaultKeyword,
                    Kind.ClassKeyword,
                    Kind.StaticKeyword,
                    Kind.PublicKeyword,
                    Kind.PrivateKeyword,
                    Kind.ProtectedKeyword,
                    Kind.GetKeyword,
                    Kind.SetKeyword,
                    Kind.OpenBracketToken,
                    Kind.AsteriskToken,
                },
                [IsEndOfDecoratorContextOnSameLine],
                RuleAction.InsertSpace),

            Rule("NoSpaceBeforeNonNullAssertionOperator", anyToken, Kind.ExclamationToken, [IsNonJsxSameLineTokenContext, IsNonNullAssertionContext], RuleAction.DeleteSpace),
            Rule("NoSpaceAfterNewKeywordOnConstructorSignature", Kind.NewKeyword, Kind.OpenParenToken, [IsNonJsxSameLineTokenContext, IsConstructorSignatureContext], RuleAction.DeleteSpace),
            Rule("SpaceLessThanAndNonJSXTypeAnnotation", Kind.LessThanToken, Kind.LessThanToken, [IsNonJsxSameLineTokenContext], RuleAction.InsertSpace),
        ];

        // These rules are applied after high priority
        RuleSpec[] userConfigurableRules =
        [
            // Treat constructor as an identifier in a function declaration, and remove spaces between constructor and following left parentheses
            Rule("SpaceAfterConstructor", Kind.ConstructorKeyword, Kind.OpenParenToken, [IsOptionEnabled(InsertSpaceAfterConstructorOption), IsNonJsxSameLineTokenContext], RuleAction.InsertSpace),
            Rule("NoSpaceAfterConstructor", Kind.ConstructorKeyword, Kind.OpenParenToken, [IsOptionDisabledOrUndefined(InsertSpaceAfterConstructorOption), IsNonJsxSameLineTokenContext], RuleAction.DeleteSpace),

            Rule("SpaceAfterComma", Kind.CommaToken, anyToken, [IsOptionEnabled(InsertSpaceAfterCommaDelimiterOption), IsNonJsxSameLineTokenContext, IsNonJsxElementOrFragmentContext, IsNextTokenNotCloseBracket, IsNextTokenNotCloseParen], RuleAction.InsertSpace),
            Rule("NoSpaceAfterComma", Kind.CommaToken, anyToken, [IsOptionDisabledOrUndefined(InsertSpaceAfterCommaDelimiterOption), IsNonJsxSameLineTokenContext, IsNonJsxElementOrFragmentContext], RuleAction.DeleteSpace),

            // Insert space after function keyword for anonymous functions
            Rule("SpaceAfterAnonymousFunctionKeyword", new[] { Kind.FunctionKeyword, Kind.AsteriskToken }, Kind.OpenParenToken, [IsOptionEnabled(InsertSpaceAfterFunctionKeywordForAnonymousFunctionsOption), IsFunctionDeclContext], RuleAction.InsertSpace),
            Rule("NoSpaceAfterAnonymousFunctionKeyword", new[] { Kind.FunctionKeyword, Kind.AsteriskToken }, Kind.OpenParenToken, [IsOptionDisabledOrUndefined(InsertSpaceAfterFunctionKeywordForAnonymousFunctionsOption), IsFunctionDeclContext], RuleAction.DeleteSpace),

            // Insert space after keywords in control flow statements
            Rule("SpaceAfterKeywordInControl", keywords, Kind.OpenParenToken, [IsOptionEnabled(InsertSpaceAfterKeywordsInControlFlowStatementsOption), IsControlDeclContext], RuleAction.InsertSpace),
            Rule("NoSpaceAfterKeywordInControl", keywords, Kind.OpenParenToken, [IsOptionDisabledOrUndefined(InsertSpaceAfterKeywordsInControlFlowStatementsOption), IsControlDeclContext], RuleAction.DeleteSpace),

            // Insert space after opening and before closing nonempty parenthesis
            Rule("SpaceAfterOpenParen", Kind.OpenParenToken, anyToken, [IsOptionEnabled(InsertSpaceAfterOpeningAndBeforeClosingNonemptyParenthesisOption), IsNonJsxSameLineTokenContext], RuleAction.InsertSpace),
            Rule("SpaceBeforeCloseParen", anyToken, Kind.CloseParenToken, [IsOptionEnabled(InsertSpaceAfterOpeningAndBeforeClosingNonemptyParenthesisOption), IsNonJsxSameLineTokenContext], RuleAction.InsertSpace),
            Rule("SpaceBetweenOpenParens", Kind.OpenParenToken, Kind.OpenParenToken, [IsOptionEnabled(InsertSpaceAfterOpeningAndBeforeClosingNonemptyParenthesisOption), IsNonJsxSameLineTokenContext], RuleAction.InsertSpace),
            Rule("NoSpaceBetweenParens", Kind.OpenParenToken, Kind.CloseParenToken, [IsNonJsxSameLineTokenContext], RuleAction.DeleteSpace),
            Rule("NoSpaceAfterOpenParen", Kind.OpenParenToken, anyToken, [IsOptionDisabledOrUndefined(InsertSpaceAfterOpeningAndBeforeClosingNonemptyParenthesisOption), IsNonJsxSameLineTokenContext], RuleAction.DeleteSpace),
            Rule("NoSpaceBeforeCloseParen", anyToken, Kind.CloseParenToken, [IsOptionDisabledOrUndefined(InsertSpaceAfterOpeningAndBeforeClosingNonemptyParenthesisOption), IsNonJsxSameLineTokenContext], RuleAction.DeleteSpace),

            // Insert space after opening and before closing nonempty brackets
            Rule("SpaceAfterOpenBracket", Kind.OpenBracketToken, anyToken, [IsOptionEnabled(InsertSpaceAfterOpeningAndBeforeClosingNonemptyBracketsOption), IsNonJsxSameLineTokenContext], RuleAction.InsertSpace),
            Rule("SpaceBeforeCloseBracket", anyToken, Kind.CloseBracketToken, [IsOptionEnabled(InsertSpaceAfterOpeningAndBeforeClosingNonemptyBracketsOption), IsNonJsxSameLineTokenContext], RuleAction.InsertSpace),
            Rule("NoSpaceBetweenBrackets", Kind.OpenBracketToken, Kind.CloseBracketToken, [IsNonJsxSameLineTokenContext], RuleAction.DeleteSpace),
            Rule("NoSpaceAfterOpenBracket", Kind.OpenBracketToken, anyToken, [IsOptionDisabledOrUndefined(InsertSpaceAfterOpeningAndBeforeClosingNonemptyBracketsOption), IsNonJsxSameLineTokenContext], RuleAction.DeleteSpace),
            Rule("NoSpaceBeforeCloseBracket", anyToken, Kind.CloseBracketToken, [IsOptionDisabledOrUndefined(InsertSpaceAfterOpeningAndBeforeClosingNonemptyBracketsOption), IsNonJsxSameLineTokenContext], RuleAction.DeleteSpace),

            // Insert a space after { and before } in single-line contexts, but remove space from empty object literals {}.
            Rule("SpaceAfterOpenBrace", Kind.OpenBraceToken, anyToken, [IsOptionEnabledOrUndefined(InsertSpaceAfterOpeningAndBeforeClosingNonemptyBracesOption), IsBraceWrappedContext], RuleAction.InsertSpace),
            Rule("SpaceBeforeCloseBrace", anyToken, Kind.CloseBraceToken, [IsOptionEnabledOrUndefined(InsertSpaceAfterOpeningAndBeforeClosingNonemptyBracesOption), IsBraceWrappedContext], RuleAction.InsertSpace),
            Rule("NoSpaceBetweenEmptyBraceBrackets", Kind.OpenBraceToken, Kind.CloseBraceToken, [IsNonJsxSameLineTokenContext, IsObjectContext], RuleAction.DeleteSpace),
            Rule("NoSpaceAfterOpenBrace", Kind.OpenBraceToken, anyToken, [IsOptionDisabled(InsertSpaceAfterOpeningAndBeforeClosingNonemptyBracesOption), IsNonJsxSameLineTokenContext], RuleAction.DeleteSpace),
            Rule("NoSpaceBeforeCloseBrace", anyToken, Kind.CloseBraceToken, [IsOptionDisabled(InsertSpaceAfterOpeningAndBeforeClosingNonemptyBracesOption), IsNonJsxSameLineTokenContext], RuleAction.DeleteSpace),

            // Insert a space after opening and before closing empty brace brackets
            Rule("SpaceBetweenEmptyBraceBrackets", Kind.OpenBraceToken, Kind.CloseBraceToken, [IsOptionEnabled(InsertSpaceAfterOpeningAndBeforeClosingEmptyBracesOption)], RuleAction.InsertSpace),
            Rule("NoSpaceBetweenEmptyBraceBrackets", Kind.OpenBraceToken, Kind.CloseBraceToken, [IsOptionDisabled(InsertSpaceAfterOpeningAndBeforeClosingEmptyBracesOption), IsNonJsxSameLineTokenContext], RuleAction.DeleteSpace),

            // Insert space after opening and before closing template string braces
            Rule("SpaceAfterTemplateHeadAndMiddle", new[] { Kind.TemplateHead, Kind.TemplateMiddle }, anyToken, [IsOptionEnabled(InsertSpaceAfterOpeningAndBeforeClosingTemplateStringBracesOption), IsNonJsxTextContext], RuleAction.InsertSpace, RuleFlags.CanDeleteNewLines),
            Rule("SpaceBeforeTemplateMiddleAndTail", anyToken, new[] { Kind.TemplateMiddle, Kind.TemplateTail }, [IsOptionEnabled(InsertSpaceAfterOpeningAndBeforeClosingTemplateStringBracesOption), IsNonJsxSameLineTokenContext], RuleAction.InsertSpace),
            Rule("NoSpaceAfterTemplateHeadAndMiddle", new[] { Kind.TemplateHead, Kind.TemplateMiddle }, anyToken, [IsOptionDisabledOrUndefined(InsertSpaceAfterOpeningAndBeforeClosingTemplateStringBracesOption), IsNonJsxTextContext], RuleAction.DeleteSpace, RuleFlags.CanDeleteNewLines),
            Rule("NoSpaceBeforeTemplateMiddleAndTail", anyToken, new[] { Kind.TemplateMiddle, Kind.TemplateTail }, [IsOptionDisabledOrUndefined(InsertSpaceAfterOpeningAndBeforeClosingTemplateStringBracesOption), IsNonJsxSameLineTokenContext], RuleAction.DeleteSpace),

            // No space after { and before } in JSX expression
            Rule("SpaceAfterOpenBraceInJsxExpression", Kind.OpenBraceToken, anyToken, [IsOptionEnabled(InsertSpaceAfterOpeningAndBeforeClosingJsxExpressionBracesOption), IsNonJsxSameLineTokenContext, IsJsxExpressionContext], RuleAction.InsertSpace),
            Rule("SpaceBeforeCloseBraceInJsxExpression", anyToken, Kind.CloseBraceToken, [IsOptionEnabled(InsertSpaceAfterOpeningAndBeforeClosingJsxExpressionBracesOption), IsNonJsxSameLineTokenContext, IsJsxExpressionContext], RuleAction.InsertSpace),
            Rule("NoSpaceAfterOpenBraceInJsxExpression", Kind.OpenBraceToken, anyToken, [IsOptionDisabledOrUndefined(InsertSpaceAfterOpeningAndBeforeClosingJsxExpressionBracesOption), IsNonJsxSameLineTokenContext, IsJsxExpressionContext], RuleAction.DeleteSpace),
            Rule("NoSpaceBeforeCloseBraceInJsxExpression", anyToken, Kind.CloseBraceToken, [IsOptionDisabledOrUndefined(InsertSpaceAfterOpeningAndBeforeClosingJsxExpressionBracesOption), IsNonJsxSameLineTokenContext, IsJsxExpressionContext], RuleAction.DeleteSpace),

            // Insert space after semicolon in for statement
            Rule("SpaceAfterSemicolonInFor", Kind.SemicolonToken, anyToken, [IsOptionEnabled(InsertSpaceAfterSemicolonInForStatementsOption), IsNonJsxSameLineTokenContext, IsForContext], RuleAction.InsertSpace),
            Rule("NoSpaceAfterSemicolonInFor", Kind.SemicolonToken, anyToken, [IsOptionDisabledOrUndefined(InsertSpaceAfterSemicolonInForStatementsOption), IsNonJsxSameLineTokenContext, IsForContext], RuleAction.DeleteSpace),

            // Insert space before and after binary operators
            Rule("SpaceBeforeBinaryOperator", anyToken, binaryOperators, [IsOptionEnabled(InsertSpaceBeforeAndAfterBinaryOperatorsOption), IsNonJsxSameLineTokenContext, IsBinaryOpContext], RuleAction.InsertSpace),
            Rule("SpaceAfterBinaryOperator", binaryOperators, anyToken, [IsOptionEnabled(InsertSpaceBeforeAndAfterBinaryOperatorsOption), IsNonJsxSameLineTokenContext, IsBinaryOpContext], RuleAction.InsertSpace),
            Rule("NoSpaceBeforeBinaryOperator", anyToken, binaryOperators, [IsOptionDisabledOrUndefined(InsertSpaceBeforeAndAfterBinaryOperatorsOption), IsNonJsxSameLineTokenContext, IsBinaryOpContext], RuleAction.DeleteSpace),
            Rule("NoSpaceAfterBinaryOperator", binaryOperators, anyToken, [IsOptionDisabledOrUndefined(InsertSpaceBeforeAndAfterBinaryOperatorsOption), IsNonJsxSameLineTokenContext, IsBinaryOpContext], RuleAction.DeleteSpace),

            Rule("SpaceBeforeOpenParenInFuncDecl", anyToken, Kind.OpenParenToken, [IsOptionEnabled(InsertSpaceBeforeFunctionParenthesisOption), IsNonJsxSameLineTokenContext, IsFunctionDeclContext], RuleAction.InsertSpace),
            Rule("NoSpaceBeforeOpenParenInFuncDecl", anyToken, Kind.OpenParenToken, [IsOptionDisabledOrUndefined(InsertSpaceBeforeFunctionParenthesisOption), IsNonJsxSameLineTokenContext, IsFunctionDeclContext], RuleAction.DeleteSpace),

            // Open Brace braces after control block
            Rule("NewLineBeforeOpenBraceInControl", controlOpenBraceLeftTokenRange, Kind.OpenBraceToken, [IsOptionEnabled(PlaceOpenBraceOnNewLineForControlBlocksOption), IsControlDeclContext, IsBeforeMultilineBlockContext], RuleAction.InsertNewLine, RuleFlags.CanDeleteNewLines),

            // Open Brace braces after function
            // TypeScript: Function can have return types, which can be made of tons of different token kinds
            Rule("NewLineBeforeOpenBraceInFunction", functionOpenBraceLeftTokenRange, Kind.OpenBraceToken, [IsOptionEnabled(PlaceOpenBraceOnNewLineForFunctionsOption), IsFunctionDeclContext, IsBeforeMultilineBlockContext], RuleAction.InsertNewLine, RuleFlags.CanDeleteNewLines),
            // Open Brace braces after TypeScript module/class/interface
            Rule("NewLineBeforeOpenBraceInTypeScriptDeclWithBlock", typeScriptOpenBraceLeftTokenRange, Kind.OpenBraceToken, [IsOptionEnabled(PlaceOpenBraceOnNewLineForFunctionsOption), IsTypeScriptDeclWithBlockContext, IsBeforeMultilineBlockContext], RuleAction.InsertNewLine, RuleFlags.CanDeleteNewLines),

            Rule("SpaceAfterTypeAssertion", Kind.GreaterThanToken, anyToken, [IsOptionEnabled(InsertSpaceAfterTypeAssertionOption), IsNonJsxSameLineTokenContext, IsTypeAssertionContext], RuleAction.InsertSpace),
            Rule("NoSpaceAfterTypeAssertion", Kind.GreaterThanToken, anyToken, [IsOptionDisabledOrUndefined(InsertSpaceAfterTypeAssertionOption), IsNonJsxSameLineTokenContext, IsTypeAssertionContext], RuleAction.DeleteSpace),

            Rule("SpaceBeforeTypeAnnotation", anyToken, new[] { Kind.QuestionToken, Kind.ColonToken }, [IsOptionEnabled(InsertSpaceBeforeTypeAnnotationOption), IsNonJsxSameLineTokenContext, IsTypeAnnotationContext], RuleAction.InsertSpace),
            Rule("NoSpaceBeforeTypeAnnotation", anyToken, new[] { Kind.QuestionToken, Kind.ColonToken }, [IsOptionDisabledOrUndefined(InsertSpaceBeforeTypeAnnotationOption), IsNonJsxSameLineTokenContext, IsTypeAnnotationContext], RuleAction.DeleteSpace),

            Rule("NoOptionalSemicolon", Kind.SemicolonToken, anyTokenIncludingEOF, [OptionEquals<SemicolonPreference>(SemicolonOption, SemicolonPreferenceRemove), IsSemicolonDeletionContext], RuleAction.DeleteToken),
            Rule("OptionalSemicolon", anyToken, anyTokenIncludingEOF, [OptionEquals<SemicolonPreference>(SemicolonOption, SemicolonPreferenceInsert), IsSemicolonInsertionContext], RuleAction.InsertTrailingSemicolon),
        ];

        // These rules are lower in priority than user-configurable. Rules earlier in this list have priority over rules later in the list.
        RuleSpec[] lowPriorityCommonRules =
        [
            // Space after keyword but not before ; or : or ?
            Rule("NoSpaceBeforeSemicolon", anyToken, Kind.SemicolonToken, [IsNonJsxSameLineTokenContext], RuleAction.DeleteSpace),

            Rule("SpaceBeforeOpenBraceInControl", controlOpenBraceLeftTokenRange, Kind.OpenBraceToken, [IsOptionDisabledOrUndefinedOrTokensOnSameLine(PlaceOpenBraceOnNewLineForControlBlocksOption), IsControlDeclContext, IsNotFormatOnEnter, IsSameLineTokenOrBeforeBlockContext], RuleAction.InsertSpace, RuleFlags.CanDeleteNewLines),
            Rule("SpaceBeforeOpenBraceInFunction", functionOpenBraceLeftTokenRange, Kind.OpenBraceToken, [IsOptionDisabledOrUndefinedOrTokensOnSameLine(PlaceOpenBraceOnNewLineForFunctionsOption), IsFunctionDeclContext, IsBeforeBlockContext, IsNotFormatOnEnter, IsSameLineTokenOrBeforeBlockContext], RuleAction.InsertSpace, RuleFlags.CanDeleteNewLines),
            Rule("SpaceBeforeOpenBraceInTypeScriptDeclWithBlock", typeScriptOpenBraceLeftTokenRange, Kind.OpenBraceToken, [IsOptionDisabledOrUndefinedOrTokensOnSameLine(PlaceOpenBraceOnNewLineForFunctionsOption), IsTypeScriptDeclWithBlockContext, IsNotFormatOnEnter, IsSameLineTokenOrBeforeBlockContext], RuleAction.InsertSpace, RuleFlags.CanDeleteNewLines),

            Rule("NoSpaceBeforeComma", anyToken, Kind.CommaToken, [IsNonJsxSameLineTokenContext], RuleAction.DeleteSpace),

            // No space before and after indexer `x[]`
            Rule("NoSpaceBeforeOpenBracket", AnyTokenExcept(Kind.AsyncKeyword, Kind.CaseKeyword), Kind.OpenBracketToken, [IsNonJsxSameLineTokenContext], RuleAction.DeleteSpace),
            Rule("NoSpaceAfterCloseBracket", Kind.CloseBracketToken, anyToken, [IsNonJsxSameLineTokenContext, IsNotBeforeBlockInFunctionDeclarationContext], RuleAction.DeleteSpace),
            Rule("SpaceAfterSemicolon", Kind.SemicolonToken, anyToken, [IsNonJsxSameLineTokenContext], RuleAction.InsertSpace),

            // Remove extra space between for and await
            Rule("SpaceBetweenForAndAwaitKeyword", Kind.ForKeyword, Kind.AwaitKeyword, [IsNonJsxSameLineTokenContext], RuleAction.InsertSpace),

            // Remove extra spaces between ... and type name in tuple spread
            Rule("SpaceBetweenDotDotDotAndTypeName", Kind.DotDotDotToken, typeNames, [IsNonJsxSameLineTokenContext], RuleAction.DeleteSpace),

            // Add a space between statements. All keywords except (do,else,case) has open/close parens after them.
            // So, we have a rule to add a space for {),Any}, {do,Any}, {else,Any}, and {case,Any}
            Rule(
                "SpaceBetweenStatements",
                new[] { Kind.CloseParenToken, Kind.DoKeyword, Kind.ElseKeyword, Kind.CaseKeyword },
                anyToken,
                [IsNonJsxSameLineTokenContext, IsNonJsxElementOrFragmentContext, IsNotForContext],
                RuleAction.InsertSpace),
            // This low-pri rule takes care of "try {", "catch {" and "finally {" in case the rule SpaceBeforeOpenBraceInControl didn't execute on FormatOnEnter.
            Rule("SpaceAfterTryCatchFinally", new[] { Kind.TryKeyword, Kind.CatchKeyword, Kind.FinallyKeyword }, Kind.OpenBraceToken, [IsNonJsxSameLineTokenContext], RuleAction.InsertSpace),
        ];

        var result = new List<RuleSpec>(highPriorityCommonRules.Length + userConfigurableRules.Length + lowPriorityCommonRules.Length);
        result.AddRange(highPriorityCommonRules);
        result.AddRange(userConfigurableRules);
        result.AddRange(lowPriorityCommonRules);
        return [.. result];
    }

    internal static TokenRange TokenRangeFrom(params Kind[] tokens)
    {
        return new TokenRange
        {
            _isSpecific = true,
            _tokens = tokens,
        };
    }

    internal static TokenRange TokenRangeFromEx(Kind[] prefix, params Kind[] tokens)
    {
        return new TokenRange
        {
            _isSpecific = true,
            _tokens = [.. prefix, .. tokens],
        };
    }

    internal static TokenRange TokenRangeFromRange(Kind start, Kind end)
    {
        var tokens = new List<Kind>(end - start + 1);
        for (var token = start; token <= end; token++)
        {
            tokens.Add(token);
        }

        return TokenRangeFrom([.. tokens]);
    }
}
