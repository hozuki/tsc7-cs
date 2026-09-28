// ported from: internal/ls/inlay_hints.go:1-950
//
// 与 Go 的偏离（逐条说明）：
//
// 1) Go 的**联合类型别名**（`*ast.CallOrNewExpression` / `*ast.FunctionLikeDeclaration` / `*ast.VariableOrPropertyDeclaration`
//    / `*ast.IdentifierNode` / `*ast.EnumMemberNode` / `*ast.LiteralLikeNode` / `*ast.Expression` / `*ast.SignatureDeclaration`
//    / `*ast.VariableOrParameterDeclaration` …）在 Go 里都是 `= Node` 的别名（internal/ast/ast_generated.go:253-589）
//    ⇒ C# 参数/返回一律写 `Node`（本仓既有做法，见 Ls/FindAllReferences.cs 的 `GetFileAndStartPosFromDeclaration`）。
//
// 2) Go 的 `type inlayHintState struct` → namespace 级 `internal sealed class InlayHintState`（Go 未导出类型 → internal；
//    成员名 PascalCase、未导出字段 `_camelCase`，见 artifacts/ls-g1-helpers.md §0）。
//
// 3) Go 的 `type parameterInfo struct` → namespace 级 `internal sealed class ParameterInfo`（字段同 Go 未导出一律
//    `_parameter`/`_name`/`_isRestParameter`；与 `System.Reflection.ParameterInfo` 不同命名空间，`ImplicitUsings`
//    不引入 `System.Reflection`，无歧义）。
//
// 4) Go 的**包级**函数（`shouldShowParameterNameHints` / `shouldShowLiteralParameterNameHintsOnly` /
//    `isSignatureSupportingReturnAnnotation` / `isHintableDeclaration` / `isHintableLiteral` / `isModuleReferenceType` /
//    `getParameterDeclarationIdentifier` / `identifierOrAccessExpressionPostfixMatchesParameterName` /
//    `isAnyInlayHintEnabled`）→ `public static partial class Ls` 的 `internal static` 成员（§2：包级函数挂同名静态类；
//    未导出 → internal、PascalCase 不加 `_`）。
//
// 5) `func (s *inlayHintState) getInlayHintLabelParts` 里的三个**互递归局部闭包**（`visitForDisplayParts` /
//    `visitDisplayPartList` / `visitParametersAndTypeParameters`，Go 闭包捕获 `parts` 与 `s`）→ C# 的
//    `private` 实例方法，把 Go 捕获的 `parts` 显式作为首参传入（语义等价；C# 局部函数受 .editorconfig 的
//    IDE1006 命名规则约束，用私有方法更稳）。
//
// 6) `s.ctx.Err() != nil` → `_ctx.CancellationToken.IsCancellationRequested`（Core/Context.cs 只暴露
//    CancellationToken，见 Ls/CrossProject.cs 文件头第 2 条）。
//
// 7) Go 的 `strings.TrimFunc(s, f)`（按 rune 修剪）→ 本文件 `TrimFunc(string, Func<char, bool>)`：Go 的谓词
//    只对 Unicode 空白 + `/` + `*`（全是 BMP）成立，按 UTF-16 代码单元修剪的结果逐字符一致。
//
// 8) `map[*ast.IdentifierNode]*ast.Symbol`（`idToSymbol`）→ `Dictionary<Node, Symbol>`：与
//    `Checker.TypeToTypeNode(Type, Node?, NodeBuilderFlags, Dictionary<Node, Symbol>?)` 的签名一致（Node 不覆写
//    Equals ⇒ 引用相等 = Go 的指针相等）。
//
// 9) `lsproto.InlayHintsOrNull{InlayHints: &result}`：Go 的 `result` 是切片 → C# 用 `List<InlayHint>` 累积后
//    `.ToArray()`。返回双值 `(InlayHintsOrNull, error)` → C# `(InlayHintsOrNull Response, Exception? Error)`
//    （本仓既有约定，见 Ls/Diagnostics.cs 文件头第 3 条）。
//
// 10) `evaluator.AnyToString`（Go 包级函数，C# 落在 `EvaluatorFunctions`）→ `using static
//     TypeScript.Evaluator.EvaluatorFunctions;` 后裸名（先例 Checker/Checker.C8.ContextualTypes.cs:91）。
//
// 11) `program.GetTypeCheckerForFile(ctx, projection)` + `defer done()`（Go 的 defer 在**函数**结束才跑，循环里
//     会一直攒着）→ C# 每轮 `try/finally { done(); }`：释放时机提前，循环体不依赖前一轮的 checker 存活 ⇒ 等价。
//
// 12) 深递归：本文件与 Go 一样用 `ForEachChild` 递归（入口由调用方跑在大栈线程上）；
//     `GetInlayHintLabelParts` 的互递归深度只跟类型节点树深度相关。
using TypeScript.Ast;
using TypeScript.Checker;
using TypeScript.Compiler;
using TypeScript.Core;
using TypeScript.LsConv;
using TypeScript.LsProto;
using TypeScript.Printer;
using TypeScript.SpanMap;
using static TypeScript.Ast.Ast;
using static TypeScript.Core.Core;
using static TypeScript.Evaluator.EvaluatorFunctions;
using static TypeScript.Ls.Ls;
using static TypeScript.LsConv.LsConv;
using static TypeScript.LsUtil.LsUtil;
using static TypeScript.Printer.Printer;
using static TypeScript.Scanner.Scanner;
using static TypeScript.StringUtil.StringUtil;
using Kind = TypeScript.Ast.Kind;
using NodeBuilderFlags = TypeScript.NodeBuilder.Flags;
using Type = TypeScript.Checker.Type;

namespace TypeScript.Ls;

public sealed partial class LanguageService
{
    /// <summary>Go: <c>func (l *LanguageService) ProvideInlayHint(ctx, params *lsproto.InlayHintParams) (lsproto.InlayHintResponse, error)</c>（inlay_hints.go:26-57）。</summary>
    public (InlayHintsOrNull Response, Exception? Error) ProvideInlayHint(Context ctx, InlayHintParams @params)
    {
        var userPreferences = UserPreferences();
        var inlayHintPreferences = userPreferences.InlayHints;
        if (!IsAnyInlayHintEnabled(inlayHintPreferences))
        {
            return (new InlayHintsOrNull { InlayHints = null }, null);
        }

        var (program, file) = GetProgramAndFile(@params.TextDocument.Uri);
        var quotePreference = GetQuotePreference(file, userPreferences);

        var mappedRanges = _converters!.FromLSPRangeIntersectingForSourceFile(file, @params.Range, Feature.InlayHints);
        var result = new List<InlayHint>(mappedRanges.Length);
        foreach (var mapped in mappedRanges)
        {
            var projection = mapped.Script;
            var (checker, done) = program.GetTypeCheckerForFile(ctx, projection);
            try
            {
                var inlayHintState = new InlayHintState
                {
                    _ctx = ctx,
                    _span = mapped.Span,
                    _preferences = inlayHintPreferences,
                    _quotePreference = quotePreference,
                    _file = projection,
                    _checker = checker,
                    _converters = _converters!,
                };
                inlayHintState.Visit(projection.AsNode());
                result.AddRange(inlayHintState._result);
            }
            finally
            {
                done();
            }
        }

        return (new InlayHintsOrNull { InlayHints = result.ToArray() }, null);
    }
}

public static partial class Ls
{
    /// <summary>Go: <c>func shouldShowParameterNameHints(preferences lsutil.InlayHintsPreferences) bool</c>（inlay_hints.go:385-389）。</summary>
    internal static bool ShouldShowParameterNameHints(LsUtil.InlayHintsPreferences preferences)
    {
        return preferences.IncludeInlayParameterNameHints == IncludeInlayParameterNameHintsLiterals ||
            preferences.IncludeInlayParameterNameHints == IncludeInlayParameterNameHintsAll;
    }

    /// <summary>Go: <c>func shouldShowLiteralParameterNameHintsOnly(preferences lsutil.InlayHintsPreferences) bool</c>（inlay_hints.go:385-389）。</summary>
    internal static bool ShouldShowLiteralParameterNameHintsOnly(LsUtil.InlayHintsPreferences preferences)
    {
        return preferences.IncludeInlayParameterNameHints == IncludeInlayParameterNameHintsLiterals;
    }

    /// <summary>node is FunctionDeclaration | ArrowFunction | FunctionExpression | MethodDeclaration | GetAccessor（inlay_hints.go:391-395）。</summary>
    internal static bool IsSignatureSupportingReturnAnnotation(Node node)
    {
        return IsArrowFunction(node) || IsFunctionExpression(node) || IsFunctionDeclaration(node) ||
            IsMethodDeclaration(node) || IsGetAccessorDeclaration(node);
    }

    /// <summary>Go: <c>func isHintableDeclaration(node *ast.VariableOrParameterDeclaration) bool</c>（inlay_hints.go:397-406）。</summary>
    internal static bool IsHintableDeclaration(Node node)
    {
        if ((IsPartOfParameterDeclaration(node) || (IsVariableDeclaration(node) && IsVarConst(node))) &&
            node.Initializer() is not null)
        {
            var initializer = SkipParentheses(node.Initializer())!;
            return !(IsHintableLiteral(initializer) || IsNewExpression(initializer) ||
                IsObjectLiteralExpression(initializer) || IsAssertionExpression(initializer));
        }
        return true;
    }

    /// <summary>Go: <c>func isHintableLiteral(node *ast.Node) bool</c>（inlay_hints.go:408-425）。</summary>
    internal static bool IsHintableLiteral(Node node)
    {
        switch (node.Kind)
        {
            case Kind.PrefixUnaryExpression:
                var operand = node.AsPrefixUnaryExpression()!.Operand;
                return IsLiteralExpression(operand) ||
                    (IsIdentifier(operand!) && IsInfinityOrNaNString(operand!.Text()));
            case Kind.TrueKeyword:
            case Kind.FalseKeyword:
            case Kind.NullKeyword:
            case Kind.NoSubstitutionTemplateLiteral:
            case Kind.TemplateExpression:
                return true;
            case Kind.Identifier:
                var name = node.Text();
                return name == "undefined" || IsInfinityOrNaNString(name);
        }
        return IsLiteralExpression(node);
    }

    /// <summary>Go: <c>func isModuleReferenceType(t *checker.Type) bool</c>（inlay_hints.go:427-430）。</summary>
    internal static bool IsModuleReferenceType(Type t)
    {
        var symbol = t.Symbol();
        return symbol is not null && (symbol.Flags & SymbolFlags.Module) != 0;
    }

    /// <summary>Go: <c>func getParameterDeclarationIdentifier(symbol *ast.Symbol) *ast.IdentifierNode</c>（inlay_hints.go:933-938）。</summary>
    internal static Node? GetParameterDeclarationIdentifier(Symbol symbol)
    {
        if (symbol.ValueDeclaration is not null && IsParameterDeclaration(symbol.ValueDeclaration) &&
            IsIdentifier(symbol.ValueDeclaration.Name()!))
        {
            return symbol.ValueDeclaration.Name();
        }
        return null;
    }

    /// <summary>Go: <c>func identifierOrAccessExpressionPostfixMatchesParameterName(expr *ast.Expression, parameterName string) bool</c>（inlay_hints.go:940-948）。</summary>
    internal static bool IdentifierOrAccessExpressionPostfixMatchesParameterName(Node expr, string parameterName)
    {
        if (IsIdentifier(expr))
        {
            return expr.Text() == parameterName;
        }
        if (IsPropertyAccessExpression(expr))
        {
            return expr.Name()!.Text() == parameterName;
        }
        return false;
    }

    /// <summary>Go: <c>func isAnyInlayHintEnabled(preferences lsutil.InlayHintsPreferences) bool</c>（inlay_hints.go:940-...）。</summary>
    internal static bool IsAnyInlayHintEnabled(LsUtil.InlayHintsPreferences preferences)
    {
        return preferences.IncludeInlayParameterNameHints != IncludeInlayParameterNameHintsNone ||
            preferences.IncludeInlayFunctionParameterTypeHints.IsTrue() ||
            preferences.IncludeInlayVariableTypeHints.IsTrue() ||
            preferences.IncludeInlayPropertyDeclarationTypeHints.IsTrue() ||
            preferences.IncludeInlayFunctionLikeReturnTypeHints.IsTrue() ||
            preferences.IncludeInlayEnumMemberValueHints.IsTrue();
    }
}

/// <summary>Go: <c>type inlayHintState struct</c>（inlay_hints.go:59-69，见文件头第 2 条）。</summary>
internal sealed class InlayHintState
{
    internal Context _ctx = null!;
    internal TextRange _span;
    internal LsUtil.InlayHintsPreferences _preferences;
    internal LsUtil.QuotePreference _quotePreference;
    internal SourceFile _file = null!;
    internal Checker.Checker _checker = null!;
    internal LsConv.Converters _converters = null!;
    internal List<InlayHint> _result = [];

    /// <summary>Go: <c>func (s *inlayHintState) visit(node *ast.Node) bool</c>（inlay_hints.go:71-114）。</summary>
    internal bool Visit(Node node)
    {
        if (node.End() - node.Pos() == 0 || (node.Flags & NodeFlags.Reparsed) != 0)
        {
            return false;
        }

        if (node.Kind is Kind.ModuleDeclaration or Kind.ClassDeclaration or Kind.InterfaceDeclaration
            or Kind.FunctionDeclaration or Kind.ClassExpression or Kind.FunctionExpression
            or Kind.MethodDeclaration or Kind.ArrowFunction)
        {
            if (_ctx.CancellationToken.IsCancellationRequested)
            {
                return true;
            }
        }

        if (!_span.Intersects(node.Loc))
        {
            return false;
        }

        if (IsTypeNode(node) && !IsExpressionWithTypeArguments(node))
        {
            return false;
        }

        if (_preferences.IncludeInlayVariableTypeHints.IsTrue() && IsVariableDeclaration(node))
        {
            VisitVariableLikeDeclaration(node);
        }
        else if (_preferences.IncludeInlayPropertyDeclarationTypeHints.IsTrue() && IsPropertyDeclaration(node))
        {
            VisitVariableLikeDeclaration(node);
        }
        else if (_preferences.IncludeInlayEnumMemberValueHints.IsTrue() && IsEnumMember(node))
        {
            VisitEnumMember(node);
        }
        else if (ShouldShowParameterNameHints(_preferences) && (IsCallExpression(node) || IsNewExpression(node)))
        {
            VisitCallOrNewExpression(node);
        }
        else
        {
            if (_preferences.IncludeInlayFunctionParameterTypeHints.IsTrue() &&
                IsFunctionLikeDeclaration(node) &&
                HasContextSensitiveParameters(node))
            {
                VisitFunctionLikeForParameterType(node);
            }
            if (_preferences.IncludeInlayFunctionLikeReturnTypeHints.IsTrue() &&
                IsSignatureSupportingReturnAnnotation(node))
            {
                VisitFunctionDeclarationLikeForReturnType(node);
            }
        }
        return node.ForEachChild(Visit);
    }

    /// <summary>FunctionDeclaration | MethodDeclaration | GetAccessor | FunctionExpression | ArrowFunction（inlay_hints.go:115-150）。</summary>
    private void VisitFunctionDeclarationLikeForReturnType(Node decl)
    {
        if (IsArrowFunction(decl))
        {
            if (AstNav.AstNav.FindChildOfKind(decl, Kind.OpenParenToken, _file) is null)
            {
                return;
            }
        }

        var typeAnnotation = decl.Type();
        if (typeAnnotation is not null || decl.Body() is null)
        {
            return;
        }

        var signature = _checker.GetSignatureFromDeclaration(decl);
        if (signature is null)
        {
            return;
        }

        var typePredicate = _checker.GetTypePredicateOfSignature(signature);

        if (typePredicate is not null && typePredicate.Type() is not null)
        {
            var hintParts = TypePredicateToInlayHintParts(typePredicate);
            AddTypeHints(hintParts, GetTypeAnnotationPosition(decl));
            return;
        }

        var returnType = _checker.GetReturnTypeOfSignature(signature);
        if (IsModuleReferenceType(returnType))
        {
            return;
        }

        var returnHintParts = TypeToInlayHintParts(returnType);
        AddTypeHints(returnHintParts, GetTypeAnnotationPosition(decl));
    }

    /// <summary>Go: <c>func (s *inlayHintState) visitCallOrNewExpression(expr *ast.CallOrNewExpression)</c>（inlay_hints.go:151-212）。</summary>
    private void VisitCallOrNewExpression(Node expr)
    {
        var args = expr.Arguments();
        if (args is null || args.Length == 0)
        {
            return;
        }

        var signature = _checker.GetResolvedSignature(expr);
        if (signature is null)
        {
            return;
        }

        var signatureParamPos = 0;
        foreach (var originalArg in args)
        {
            var arg = SkipParentheses(originalArg)!;
            if (ShouldShowLiteralParameterNameHintsOnly(_preferences) && !IsHintableLiteral(arg))
            {
                signatureParamPos++;
                continue;
            }

            var spreadArgs = 0;
            if (IsSpreadElement(arg))
            {
                var spreadType = _checker.GetTypeAtLocation(arg.Expression()!);
                if (spreadType.IsTupleType())
                {
                    var elementFlags = spreadType.Target()!.AsTupleType()!.ElementFlags();
                    var fixedLength = spreadType.Target()!.AsTupleType()!.FixedLength();
                    if (fixedLength == 0)
                    {
                        continue;
                    }
                    var firstOptionalIndex = FindIndex(elementFlags, f => (f & ElementFlags.Required) == 0);
                    var requiredArgs = IfElse(firstOptionalIndex < 0, fixedLength, firstOptionalIndex);
                    if (requiredArgs > 0)
                    {
                        spreadArgs = requiredArgs;
                    }
                }
            }

            var identifierInfo = GetParameterIdentifierInfoAtPosition(signature, signatureParamPos);
            signatureParamPos += IfElse(spreadArgs > 0, spreadArgs, 1);
            if (identifierInfo is null)
            {
                return;
            }

            var parameter = identifierInfo._parameter;
            var parameterName = identifierInfo._name;
            var isFirstVariadicArgument = identifierInfo._isRestParameter;
            var parameterNameNotSameAsArgument = _preferences.IncludeInlayParameterNameHintsWhenArgumentMatchesName.IsTrue() ||
                !IdentifierOrAccessExpressionPostfixMatchesParameterName(arg, parameterName);
            if (!parameterNameNotSameAsArgument && !isFirstVariadicArgument)
            {
                continue;
            }

            if (LeadingCommentsContainsParameterName(arg, parameterName))
            {
                continue;
            }

            AddParameterHints(
                parameterName,
                parameter,
                AstNav.AstNav.GetStartOfNode(originalArg, _file, false /*includeJSDoc*/),
                isFirstVariadicArgument);
        }
    }

    /// <summary>Go: <c>func (s *inlayHintState) visitEnumMember(member *ast.EnumMemberNode)</c>（inlay_hints.go:213-223）。</summary>
    private void VisitEnumMember(Node member)
    {
        if (member.Initializer() is not null)
        {
            return;
        }

        var enumValue = _checker.GetConstantValue(member);
        if (enumValue is not null)
        {
            AddEnumMemberValueHints(AnyToString(enumValue), member.End());
        }
    }

    /// <summary>Go: <c>func (s *inlayHintState) visitVariableLikeDeclaration(decl *ast.VariableOrPropertyDeclaration)</c>（inlay_hints.go:224-252）。</summary>
    private void VisitVariableLikeDeclaration(Node decl)
    {
        if ((decl.Initializer() is null &&
                !(IsPropertyDeclaration(decl) && (_checker.GetTypeAtLocation(decl).Flags() & TypeFlags.Any) == 0)) ||
            IsBindingPattern(decl.Name()) ||
            (IsVariableDeclaration(decl) && !IsHintableDeclaration(decl)))
        {
            return;
        }

        var typeAnnotation = decl.Type();
        if (typeAnnotation is not null)
        {
            return;
        }

        var declarationType = _checker.GetTypeAtLocation(decl);
        if (IsModuleReferenceType(declarationType))
        {
            return;
        }

        var hintParts = TypeToInlayHintParts(declarationType);
        var hintText = "";
        if (hintParts.String is not null)
        {
            hintText = hintParts.String;
        }
        else if (hintParts.InlayHintLabelParts is not null)
        {
            var b = new System.Text.StringBuilder();
            foreach (var part in hintParts.InlayHintLabelParts)
            {
                b.Append(part?.Value);
            }
            hintText = b.ToString();
        }
        if (!_preferences.IncludeInlayVariableTypeHintsWhenTypeMatchesName.IsTrue() &&
            !IsComputedPropertyName(decl.Name()!) &&
            EquateStringCaseInsensitive(decl.Name()!.Text(), hintText))
        {
            return;
        }
        AddTypeHints(hintParts, decl.Name()!.End());
    }

    /// <summary>Go: <c>func (s *inlayHintState) visitFunctionLikeForParameterType(node *ast.FunctionLikeDeclaration)</c>（inlay_hints.go:254-279）。</summary>
    private void VisitFunctionLikeForParameterType(Node node)
    {
        var signature = _checker.GetSignatureFromDeclaration(node);
        if (signature is null)
        {
            return;
        }

        var pos = 0;
        foreach (var param in node.Parameters() ?? [])
        {
            if (IsHintableDeclaration(param))
            {
                Symbol? symbol;
                if (IsThisParameter(param))
                {
                    symbol = signature.ThisParameter();
                }
                else
                {
                    symbol = signature.Parameters()[pos];
                }
                AddParameterTypeHint(param, symbol);
            }
            if (IsThisParameter(param))
            {
                continue;
            }
            pos++;
        }
    }

    /// <summary>Go: <c>func (s *inlayHintState) addParameterTypeHint(node *ast.ParameterDeclarationNode, symbol *ast.Symbol)</c>（inlay_hints.go:281-296）。</summary>
    private void AddParameterTypeHint(Node node, Symbol? symbol)
    {
        var typeAnnotation = node.Type();
        if (typeAnnotation is not null || symbol is null)
        {
            return;
        }
        var typeHints = GetParameterDeclarationTypeHints(symbol);
        if (typeHints is null)
        {
            return;
        }
        int pos;
        if (node.QuestionToken() is not null)
        {
            pos = node.QuestionToken()!.End();
        }
        else
        {
            pos = node.Name()!.End();
        }
        AddTypeHints(typeHints, pos);
    }

    /// <summary>Go: <c>func (s *inlayHintState) getParameterDeclarationTypeHints(symbol *ast.Symbol) *lsproto.StringOrInlayHintLabelParts</c>（inlay_hints.go:298-309）。</summary>
    private StringOrInlayHintLabelParts? GetParameterDeclarationTypeHints(Symbol symbol)
    {
        var valueDeclaration = symbol.ValueDeclaration;
        if (valueDeclaration is null || !IsParameterDeclaration(valueDeclaration))
        {
            return null;
        }

        var signatureParamType = _checker.GetTypeOfSymbolAtLocation(symbol, valueDeclaration);
        if (IsModuleReferenceType(signatureParamType))
        {
            return null;
        }

        return TypeToInlayHintParts(signatureParamType);
    }

    /// <summary>Go: <c>func (s *inlayHintState) typeToInlayHintParts(t *checker.Type) lsproto.StringOrInlayHintLabelParts</c>（inlay_hints.go:311-321）。</summary>
    private StringOrInlayHintLabelParts TypeToInlayHintParts(Type t)
    {
        const NodeBuilderFlags flags = NodeBuilderFlags.IgnoreErrors | NodeBuilderFlags.AllowUniqueESSymbolType |
            NodeBuilderFlags.UseAliasDefinedOutsideCurrentScope;
        var idToSymbol = new Dictionary<Node, Symbol>();
        // !!! Avoid type node reuse so we collect identifier symbols.
        var typeNode = _checker.TypeToTypeNode(t, null /*enclosingDeclaration*/, flags, idToSymbol);
        Debug.Assert(typeNode is not null, "should always get typenode");
        return new StringOrInlayHintLabelParts
        {
            InlayHintLabelParts = GetInlayHintLabelParts(typeNode!, idToSymbol),
        };
    }

    /// <summary>Go: <c>func (s *inlayHintState) typePredicateToInlayHintParts(typePredicate *checker.TypePredicate)</c>（inlay_hints.go:323-333）。</summary>
    private StringOrInlayHintLabelParts TypePredicateToInlayHintParts(TypePredicate typePredicate)
    {
        const NodeBuilderFlags flags = NodeBuilderFlags.IgnoreErrors | NodeBuilderFlags.AllowUniqueESSymbolType |
            NodeBuilderFlags.UseAliasDefinedOutsideCurrentScope;
        var idToSymbol = new Dictionary<Node, Symbol>();
        // !!! Avoid type node reuse so we collect identifier symbols.
        var typeNode = _checker.TypePredicateToTypePredicateNode(typePredicate, null /*enclosingDeclaration*/, flags, idToSymbol);
        Debug.Assert(typeNode is not null, "should always get typePredicateNode");
        return new StringOrInlayHintLabelParts
        {
            InlayHintLabelParts = GetInlayHintLabelParts(typeNode!, idToSymbol),
        };
    }

    /// <summary>Go: <c>func (s *inlayHintState) addTypeHints(hint lsproto.StringOrInlayHintLabelParts, position int)</c>（inlay_hints.go:335-351）。</summary>
    private void AddTypeHints(StringOrInlayHintLabelParts hint, int position)
    {
        var (lspPosition, fidelity) = _converters.ToLSPPositionForFeature(_file, new TextPos(position), Feature.InlayHints);
        if (fidelity.IsNone())
        {
            return;
        }
        if (hint.String is not null)
        {
            hint.String = ": " + hint.String;
        }
        else
        {
            var existing = hint.InlayHintLabelParts ?? [];
            var parts = new InlayHintLabelPart?[existing.Length + 1];
            parts[0] = new InlayHintLabelPart { Value = ": " };
            Array.Copy(existing, 0, parts, 1, existing.Length);
            hint.InlayHintLabelParts = parts;
        }
        _result.Add(new InlayHint
        {
            Label = hint,
            Position = lspPosition,
            Kind = InlayHintKind.Type,
            PaddingLeft = true,
        });
    }

    /// <summary>Go: <c>func (s *inlayHintState) addEnumMemberValueHints(text string, position int)</c>（inlay_hints.go:353-363）。</summary>
    private void AddEnumMemberValueHints(string text, int position)
    {
        var (lspPosition, fidelity) = _converters.ToLSPPositionForFeature(_file, new TextPos(position), Feature.InlayHints);
        if (fidelity.IsNone())
        {
            return;
        }
        _result.Add(new InlayHint
        {
            Label = new StringOrInlayHintLabelParts { String = "= " + text },
            Position = lspPosition,
            PaddingLeft = true,
        });
    }

    /// <summary>Go: <c>func (s *inlayHintState) addParameterHints(text string, parameter *ast.IdentifierNode, position int, isFirstVariadicArgument bool)</c>（inlay_hints.go:365-383）。</summary>
    private void AddParameterHints(string text, Node parameter, int position, bool isFirstVariadicArgument)
    {
        var (lspPosition, fidelity) = _converters.ToLSPPositionForFeature(_file, new TextPos(position), Feature.InlayHints);
        if (fidelity.IsNone())
        {
            return;
        }
        var hintText = IfElse(isFirstVariadicArgument, "...", "") + text;
        var displayParts = new InlayHintLabelPart?[]
        {
            GetNodeDisplayPart(hintText, parameter),
            new InlayHintLabelPart { Value = ":" },
        };
        var labelParts = new StringOrInlayHintLabelParts { InlayHintLabelParts = displayParts };

        _result.Add(new InlayHint
        {
            Label = labelParts,
            Position = lspPosition,
            Kind = InlayHintKind.Parameter,
            PaddingRight = true,
        });
    }

    /// <summary>Go: <c>func (s *inlayHintState) getInlayHintLabelParts(node, idToSymbol) []*lsproto.InlayHintLabelPart</c>（inlay_hints.go:433-825）。</summary>
    private InlayHintLabelPart?[] GetInlayHintLabelParts(Node node, Dictionary<Node, Symbol> idToSymbol)
    {
        var parts = new List<InlayHintLabelPart?>();
        VisitForDisplayParts(parts, node, idToSymbol);
        return parts.ToArray();
    }

    private void VisitForDisplayParts(List<InlayHintLabelPart?> parts, Node? node, Dictionary<Node, Symbol> idToSymbol)
    {
        if (node is null)
        {
            return;
        }

        var tokenString = TokenToString(node.Kind);
        if (tokenString != "")
        {
            parts.Add(new InlayHintLabelPart { Value = tokenString });
            return;
        }

        if (IsLiteralExpression(node))
        {
            parts.Add(new InlayHintLabelPart { Value = GetLiteralText(node) });
            return;
        }

        switch (node.Kind)
        {
            case Kind.Identifier:
            {
                var identifierText = node.Text();
                Node? name = null;
                if (idToSymbol.TryGetValue(node, out var symbol) && (symbol.Declarations?.Length ?? 0) != 0)
                {
                    name = GetNameOfDeclaration(symbol.Declarations![0]);
                }
                if (name is not null)
                {
                    parts.Add(GetNodeDisplayPart(identifierText, name));
                }
                else
                {
                    parts.Add(new InlayHintLabelPart { Value = identifierText });
                }
                break;
            }
            case Kind.QualifiedName:
                VisitForDisplayParts(parts, node.AsQualifiedName()!.Left, idToSymbol);
                parts.Add(new InlayHintLabelPart { Value = "." });
                VisitForDisplayParts(parts, node.AsQualifiedName()!.Right, idToSymbol);
                break;
            case Kind.TypePredicate:
                if (node.AsTypePredicateNode()!.AssertsModifier is not null)
                {
                    parts.Add(new InlayHintLabelPart { Value = "asserts " });
                }
                VisitForDisplayParts(parts, node.AsTypePredicateNode()!.ParameterName, idToSymbol);
                if (node.Type() is not null)
                {
                    parts.Add(new InlayHintLabelPart { Value = " is " });
                    VisitForDisplayParts(parts, node.Type(), idToSymbol);
                }
                break;
            case Kind.TypeReference:
                VisitForDisplayParts(parts, node.AsTypeReferenceNode()!.TypeName, idToSymbol);
                if ((node.TypeArguments()?.Length ?? 0) > 0)
                {
                    parts.Add(new InlayHintLabelPart { Value = "<" });
                    VisitDisplayPartList(parts, node.TypeArguments(), ",", idToSymbol);
                    parts.Add(new InlayHintLabelPart { Value = ">" });
                }
                break;
            case Kind.TypeParameter:
                if ((node.ModifierNodes()?.Length ?? 0) > 0)
                {
                    VisitDisplayPartList(parts, node.ModifierNodes(), "", idToSymbol);
                }
                VisitForDisplayParts(parts, node.Name(), idToSymbol);
                if (node.AsTypeParameterDeclaration()!.Constraint is not null)
                {
                    parts.Add(new InlayHintLabelPart { Value = " extends " });
                    VisitForDisplayParts(parts, node.AsTypeParameterDeclaration()!.Constraint, idToSymbol);
                }
                if (node.AsTypeParameterDeclaration()!.DefaultType is not null)
                {
                    parts.Add(new InlayHintLabelPart { Value = " = " });
                    VisitForDisplayParts(parts, node.AsTypeParameterDeclaration()!.DefaultType, idToSymbol);
                }
                break;
            case Kind.Parameter:
                if ((node.ModifierNodes()?.Length ?? 0) > 0)
                {
                    VisitDisplayPartList(parts, node.ModifierNodes(), " ", idToSymbol);
                }
                if (node.AsParameterDeclaration()!.DotDotDotToken is not null)
                {
                    parts.Add(new InlayHintLabelPart { Value = "..." });
                }
                VisitForDisplayParts(parts, node.Name(), idToSymbol);
                if (node.QuestionToken() is not null)
                {
                    parts.Add(new InlayHintLabelPart { Value = "?" });
                }
                if (node.Type() is not null)
                {
                    parts.Add(new InlayHintLabelPart { Value = ": " });
                    VisitForDisplayParts(parts, node.Type(), idToSymbol);
                }
                break;
            case Kind.ConstructorType:
                parts.Add(new InlayHintLabelPart { Value = "new " });
                VisitParametersAndTypeParameters(parts, node, idToSymbol);
                parts.Add(new InlayHintLabelPart { Value = " => " });
                VisitForDisplayParts(parts, node.Type(), idToSymbol);
                break;
            case Kind.TypeQuery:
                parts.Add(new InlayHintLabelPart { Value = "typeof " });
                VisitForDisplayParts(parts, node.AsTypeQueryNode()!.ExprName, idToSymbol);
                if ((node.TypeArguments()?.Length ?? 0) > 0)
                {
                    parts.Add(new InlayHintLabelPart { Value = "<" });
                    VisitDisplayPartList(parts, node.TypeArguments(), ", ", idToSymbol);
                    parts.Add(new InlayHintLabelPart { Value = ">" });
                }
                break;
            case Kind.TypeLiteral:
                parts.Add(new InlayHintLabelPart { Value = "{" });
                if ((node.Members()?.Length ?? 0) > 0)
                {
                    parts.Add(new InlayHintLabelPart { Value = " " });
                    VisitDisplayPartList(parts, node.Members(), "; ", idToSymbol);
                    parts.Add(new InlayHintLabelPart { Value = " " });
                }
                parts.Add(new InlayHintLabelPart { Value = "}" });
                break;
            case Kind.ArrayType:
                VisitForDisplayParts(parts, node.AsArrayTypeNode()!.ElementType, idToSymbol);
                parts.Add(new InlayHintLabelPart { Value = "[]" });
                break;
            case Kind.TupleType:
                parts.Add(new InlayHintLabelPart { Value = "[" });
                VisitDisplayPartList(parts, node.Elements(), ", ", idToSymbol);
                parts.Add(new InlayHintLabelPart { Value = "]" });
                break;
            case Kind.NamedTupleMember:
                if (node.AsNamedTupleMember()!.DotDotDotToken is not null)
                {
                    parts.Add(new InlayHintLabelPart { Value = "..." });
                }
                VisitForDisplayParts(parts, node.Name(), idToSymbol);
                if (node.QuestionToken() is not null)
                {
                    parts.Add(new InlayHintLabelPart { Value = "?" });
                }
                parts.Add(new InlayHintLabelPart { Value = ": " });
                VisitForDisplayParts(parts, node.Type(), idToSymbol);
                break;
            case Kind.OptionalType:
                VisitForDisplayParts(parts, node.Type(), idToSymbol);
                parts.Add(new InlayHintLabelPart { Value = "?" });
                break;
            case Kind.RestType:
                parts.Add(new InlayHintLabelPart { Value = "..." });
                VisitForDisplayParts(parts, node.Type(), idToSymbol);
                break;
            case Kind.UnionType:
                if (node.AsUnionTypeNode()!.Types is not null)
                {
                    VisitDisplayPartList(parts, node.AsUnionTypeNode()!.Types!.Nodes, " | ", idToSymbol);
                }
                break;
            case Kind.IntersectionType:
                if (node.AsIntersectionTypeNode()!.Types is not null)
                {
                    VisitDisplayPartList(parts, node.AsIntersectionTypeNode()!.Types!.Nodes, " & ", idToSymbol);
                }
                break;
            case Kind.ConditionalType:
                VisitForDisplayParts(parts, node.AsConditionalTypeNode()!.CheckType, idToSymbol);
                parts.Add(new InlayHintLabelPart { Value = " extends " });
                VisitForDisplayParts(parts, node.AsConditionalTypeNode()!.ExtendsType, idToSymbol);
                parts.Add(new InlayHintLabelPart { Value = " ? " });
                VisitForDisplayParts(parts, node.AsConditionalTypeNode()!.TrueType, idToSymbol);
                parts.Add(new InlayHintLabelPart { Value = " : " });
                VisitForDisplayParts(parts, node.AsConditionalTypeNode()!.FalseType, idToSymbol);
                break;
            case Kind.InferType:
                parts.Add(new InlayHintLabelPart { Value = "infer " });
                VisitForDisplayParts(parts, node.AsInferTypeNode()!.TypeParameter, idToSymbol);
                break;
            case Kind.ParenthesizedType:
                parts.Add(new InlayHintLabelPart { Value = "(" });
                VisitForDisplayParts(parts, node.Type(), idToSymbol);
                parts.Add(new InlayHintLabelPart { Value = ")" });
                break;
            case Kind.TypeOperator:
                parts.Add(new InlayHintLabelPart { Value = TokenToString(node.AsTypeOperatorNode()!.Operator) });
                VisitForDisplayParts(parts, node.Type(), idToSymbol);
                break;
            case Kind.IndexedAccessType:
                VisitForDisplayParts(parts, node.AsIndexedAccessTypeNode()!.ObjectType, idToSymbol);
                parts.Add(new InlayHintLabelPart { Value = "[" });
                VisitForDisplayParts(parts, node.AsIndexedAccessTypeNode()!.IndexType, idToSymbol);
                parts.Add(new InlayHintLabelPart { Value = "]" });
                break;
            case Kind.MappedType:
                parts.Add(new InlayHintLabelPart { Value = "{ " });
                if (node.AsMappedTypeNode()!.ReadonlyToken is not null)
                {
                    if (node.AsMappedTypeNode()!.ReadonlyToken!.Kind == Kind.PlusToken)
                    {
                        parts.Add(new InlayHintLabelPart { Value = "+" });
                    }
                    else if (node.AsMappedTypeNode()!.ReadonlyToken!.Kind == Kind.MinusToken)
                    {
                        parts.Add(new InlayHintLabelPart { Value = "-" });
                    }
                    parts.Add(new InlayHintLabelPart { Value = "readonly " });
                }
                parts.Add(new InlayHintLabelPart { Value = "[" });
                VisitForDisplayParts(parts, node.AsMappedTypeNode()!.TypeParameter, idToSymbol);
                if (node.AsMappedTypeNode()!.NameType is not null)
                {
                    parts.Add(new InlayHintLabelPart { Value = " as " });
                    VisitForDisplayParts(parts, node.AsMappedTypeNode()!.NameType, idToSymbol);
                }
                parts.Add(new InlayHintLabelPart { Value = "]" });
                if (node.QuestionToken() is not null)
                {
                    if (node.QuestionToken()!.Kind == Kind.PlusToken)
                    {
                        parts.Add(new InlayHintLabelPart { Value = "+" });
                    }
                    else if (node.QuestionToken()!.Kind == Kind.MinusToken)
                    {
                        parts.Add(new InlayHintLabelPart { Value = "-" });
                    }
                    parts.Add(new InlayHintLabelPart { Value = "?" });
                }
                parts.Add(new InlayHintLabelPart { Value = ": " });
                if (node.Type() is not null)
                {
                    VisitForDisplayParts(parts, node.Type(), idToSymbol);
                }
                parts.Add(new InlayHintLabelPart { Value = "; }" });
                break;
            case Kind.LiteralType:
                VisitForDisplayParts(parts, node.AsLiteralTypeNode()!.Literal, idToSymbol);
                break;
            case Kind.FunctionType:
                VisitParametersAndTypeParameters(parts, node, idToSymbol);
                parts.Add(new InlayHintLabelPart { Value = " => " });
                VisitForDisplayParts(parts, node.Type(), idToSymbol);
                break;
            case Kind.ImportType:
                if (node.AsImportTypeNode()!.IsTypeOf)
                {
                    parts.Add(new InlayHintLabelPart { Value = "typeof " });
                }
                parts.Add(new InlayHintLabelPart { Value = "import(" });
                VisitForDisplayParts(parts, node.AsImportTypeNode()!.Argument, idToSymbol);
                parts.Add(new InlayHintLabelPart { Value = ")" });
                if (node.AsImportTypeNode()!.Qualifier is not null)
                {
                    parts.Add(new InlayHintLabelPart { Value = "." });
                    VisitForDisplayParts(parts, node.AsImportTypeNode()!.Qualifier, idToSymbol);
                }
                if ((node.TypeArguments()?.Length ?? 0) > 0)
                {
                    parts.Add(new InlayHintLabelPart { Value = "<" });
                    VisitDisplayPartList(parts, node.TypeArguments(), ", ", idToSymbol);
                    parts.Add(new InlayHintLabelPart { Value = ">" });
                }
                break;
            case Kind.PropertySignature:
                if ((node.ModifierNodes()?.Length ?? 0) > 0)
                {
                    VisitDisplayPartList(parts, node.ModifierNodes(), " ", idToSymbol);
                    parts.Add(new InlayHintLabelPart { Value = " " });
                }
                VisitForDisplayParts(parts, node.Name(), idToSymbol);
                if (node.PostfixToken() is not null)
                {
                    parts.Add(new InlayHintLabelPart { Value = TokenToString(node.PostfixToken()!.Kind) });
                }
                if (node.Type() is not null)
                {
                    parts.Add(new InlayHintLabelPart { Value = ": " });
                    VisitForDisplayParts(parts, node.Type(), idToSymbol);
                }
                break;
            case Kind.IndexSignature:
                parts.Add(new InlayHintLabelPart { Value = "[" });
                VisitDisplayPartList(parts, node.Parameters(), ", ", idToSymbol);
                parts.Add(new InlayHintLabelPart { Value = "]" });
                if (node.Type() is not null)
                {
                    parts.Add(new InlayHintLabelPart { Value = ": " });
                    VisitForDisplayParts(parts, node.Type(), idToSymbol);
                }
                break;
            case Kind.MethodSignature:
                if ((node.ModifierNodes()?.Length ?? 0) > 0)
                {
                    VisitDisplayPartList(parts, node.ModifierNodes(), " ", idToSymbol);
                    parts.Add(new InlayHintLabelPart { Value = " " });
                }
                VisitForDisplayParts(parts, node.Name(), idToSymbol);
                if (node.PostfixToken() is not null)
                {
                    parts.Add(new InlayHintLabelPart { Value = TokenToString(node.PostfixToken()!.Kind) });
                }
                VisitParametersAndTypeParameters(parts, node, idToSymbol);
                if (node.Type() is not null)
                {
                    parts.Add(new InlayHintLabelPart { Value = ": " });
                    VisitForDisplayParts(parts, node.Type(), idToSymbol);
                }
                break;
            case Kind.CallSignature:
                VisitParametersAndTypeParameters(parts, node, idToSymbol);
                if (node.Type() is not null)
                {
                    parts.Add(new InlayHintLabelPart { Value = ": " });
                    VisitForDisplayParts(parts, node.Type(), idToSymbol);
                }
                break;
            case Kind.ConstructSignature:
                parts.Add(new InlayHintLabelPart { Value = "new " });
                VisitParametersAndTypeParameters(parts, node, idToSymbol);
                if (node.Type() is not null)
                {
                    parts.Add(new InlayHintLabelPart { Value = ": " });
                    VisitForDisplayParts(parts, node.Type(), idToSymbol);
                }
                break;
            case Kind.ArrayBindingPattern:
                parts.Add(new InlayHintLabelPart { Value = "[" });
                VisitDisplayPartList(parts, node.Elements(), ", ", idToSymbol);
                parts.Add(new InlayHintLabelPart { Value = "]" });
                break;
            case Kind.ObjectBindingPattern:
                parts.Add(new InlayHintLabelPart { Value = "{" });
                if ((node.Elements()?.Length ?? 0) > 0)
                {
                    parts.Add(new InlayHintLabelPart { Value = " " });
                    VisitDisplayPartList(parts, node.Elements(), ", ", idToSymbol);
                    parts.Add(new InlayHintLabelPart { Value = " " });
                }
                parts.Add(new InlayHintLabelPart { Value = "}" });
                break;
            case Kind.BindingElement:
                VisitForDisplayParts(parts, node.Name(), idToSymbol);
                break;
            case Kind.PrefixUnaryExpression:
                parts.Add(new InlayHintLabelPart { Value = TokenToString(node.AsPrefixUnaryExpression()!.Operator) });
                VisitForDisplayParts(parts, node.AsPrefixUnaryExpression()!.Operand, idToSymbol);
                break;
            case Kind.TemplateLiteralType:
                VisitForDisplayParts(parts, node.AsTemplateLiteralTypeNode()!.Head, idToSymbol);
                foreach (var span in node.AsTemplateLiteralTypeNode()!.TemplateSpans!.Nodes ?? [])
                {
                    VisitForDisplayParts(parts, span, idToSymbol);
                }
                break;
            case Kind.TemplateHead:
                parts.Add(new InlayHintLabelPart { Value = GetLiteralText(node) });
                break;
            case Kind.TemplateLiteralTypeSpan:
                VisitForDisplayParts(parts, node.Type(), idToSymbol);
                VisitForDisplayParts(parts, node.AsTemplateLiteralTypeSpan()!.Literal, idToSymbol);
                break;
            case Kind.TemplateMiddle:
            case Kind.TemplateTail:
                parts.Add(new InlayHintLabelPart { Value = GetLiteralText(node) });
                break;
            case Kind.ThisType:
                parts.Add(new InlayHintLabelPart { Value = "this" });
                break;
            case Kind.ComputedPropertyName:
                parts.Add(new InlayHintLabelPart { Value = "[" });
                VisitForDisplayParts(parts, node.Expression(), idToSymbol);
                parts.Add(new InlayHintLabelPart { Value = "]" });
                break;
            case Kind.PropertyAccessExpression:
                VisitForDisplayParts(parts, node.Expression(), idToSymbol);
                parts.Add(new InlayHintLabelPart { Value = "." });
                VisitForDisplayParts(parts, node.Name(), idToSymbol);
                break;
            case Kind.ElementAccessExpression:
                VisitForDisplayParts(parts, node.Expression(), idToSymbol);
                parts.Add(new InlayHintLabelPart { Value = "[" });
                VisitForDisplayParts(parts, node.AsElementAccessExpression()!.ArgumentExpression, idToSymbol);
                parts.Add(new InlayHintLabelPart { Value = "]" });
                break;
            default:
                Debug.FailBadSyntaxKind(node);
                break;
        }
    }

    private void VisitDisplayPartList(List<InlayHintLabelPart?> parts, Node[]? nodes, string separator, Dictionary<Node, Symbol> idToSymbol)
    {
        if (nodes is null)
        {
            return;
        }
        for (var i = 0; i < nodes.Length; i++)
        {
            if (i > 0)
            {
                parts.Add(new InlayHintLabelPart { Value = separator });
            }
            VisitForDisplayParts(parts, nodes[i], idToSymbol);
        }
    }

    private void VisitParametersAndTypeParameters(List<InlayHintLabelPart?> parts, Node node, Dictionary<Node, Symbol> idToSymbol)
    {
        if ((node.TypeParameters()?.Length ?? 0) > 0)
        {
            parts.Add(new InlayHintLabelPart { Value = "<" });
            VisitDisplayPartList(parts, node.TypeParameters(), ", ", idToSymbol);
            parts.Add(new InlayHintLabelPart { Value = ">" });
        }
        parts.Add(new InlayHintLabelPart { Value = "(" });
        VisitDisplayPartList(parts, node.Parameters(), ", ", idToSymbol);
        parts.Add(new InlayHintLabelPart { Value = ")" });
    }

    /// <summary>Go: <c>func (s *inlayHintState) getNodeDisplayPart(text string, node *ast.Node) *lsproto.InlayHintLabelPart</c>（inlay_hints.go:827-844）。</summary>
    private InlayHintLabelPart GetNodeDisplayPart(string text, Node node)
    {
        var file = GetSourceFileOfNode(node)!;
        var pos = AstNav.AstNav.GetStartOfNode(node, file, false /*includeJSDoc*/);
        var end = node.End();
        var part = new InlayHintLabelPart { Value = text };
        // The location is an optional go-to target for the name. Only attach it when the name maps back to a
        // single concrete span in the original text; an approximate or synthesized mapping would point the
        // user somewhere wrong, so it is better to omit the target than to fabricate one.
        var (lspRange, fidelity) = _converters.ToLSPRangeForFeature(file, NewTextRange(pos, end), Feature.InlayHints);
        if (fidelity.IsSingleSegment())
        {
            part.Location = new Location
            {
                Uri = FileNameToDocumentURI(file.OriginalFileName()),
                Range = lspRange,
            };
        }
        return part;
    }

    /// <summary>Go: <c>func (s *inlayHintState) getLiteralText(node *ast.LiteralLikeNode) string</c>（inlay_hints.go:846-874）。</summary>
    private string GetLiteralText(Node node)
    {
        switch (node.Kind)
        {
            case Kind.StringLiteral:
                if (_quotePreference == QuotePreferenceSingle)
                {
                    return "'" + EscapeString(node.Text(), QuoteChar.SingleQuote) + "'";
                }
                return "\"" + EscapeString(node.Text(), QuoteChar.DoubleQuote) + "\"";
            case Kind.TemplateHead:
            case Kind.TemplateMiddle:
            case Kind.TemplateTail:
                var rawText = node.RawText();
                if (rawText == "")
                {
                    rawText = EscapeString(node.Text(), QuoteChar.Backtick);
                }
                switch (node.Kind)
                {
                    case Kind.TemplateHead:
                        return "`" + rawText + "${";
                    case Kind.TemplateMiddle:
                        return "}" + rawText + "${";
                    case Kind.TemplateTail:
                        return "}" + rawText + "`";
                }
                break;
        }
        return node.Text();
    }

    /// <summary>Go: <c>func (s *inlayHintState) getParameterIdentifierInfoAtPosition(signature *checker.Signature, pos int) *parameterInfo</c>（inlay_hints.go:877-931）。</summary>
    private ParameterInfo? GetParameterIdentifierInfoAtPosition(Checker.Signature signature, int pos)
    {
        var parameters = signature.Parameters();
        var paramCount = parameters.Length - IfElse(signature.HasRestParameter(), 1, 0);
        if (pos < paramCount)
        {
            var param = parameters[pos];
            var paramId = GetParameterDeclarationIdentifier(param);
            if (paramId is null)
            {
                return null;
            }
            return new ParameterInfo
            {
                _parameter = paramId,
                _name = paramId.Text(),
                _isRestParameter = false,
            };
        }

        Symbol? restParameter = null;
        Node? restId = null;
        if (paramCount < parameters.Length)
        {
            restParameter = parameters[paramCount];
            restId = GetParameterDeclarationIdentifier(restParameter);
        }
        if (restId is null)
        {
            return null;
        }

        var restType = _checker.GetTypeOfSymbol(restParameter!);
        if (restType.IsTupleType())
        {
            var elementInfos = restType.Target()!.AsTupleType()!.ElementInfos();
            var associatedNames = new List<Node?>(elementInfos.Length);
            foreach (var elementInfo in elementInfos)
            {
                associatedNames.Add(elementInfo.LabeledDeclaration());
            }
            var index = pos - paramCount;
            if (index < associatedNames.Count)
            {
                var associatedName = associatedNames[index];
                if (associatedName is not null)
                {
                    Debug.Assert(IsIdentifier(associatedName.Name()!));
                    bool isRestTupleElement;
                    if (IsNamedTupleMember(associatedName))
                    {
                        isRestTupleElement = associatedName.AsNamedTupleMember()!.DotDotDotToken is not null;
                    }
                    else
                    {
                        isRestTupleElement = associatedName.AsParameterDeclaration()!.DotDotDotToken is not null;
                    }
                    return new ParameterInfo
                    {
                        _parameter = associatedName.Name()!,
                        _name = associatedName.Name()!.Text(),
                        _isRestParameter = isRestTupleElement,
                    };
                }
            }

            return null;
        }

        if (pos == paramCount)
        {
            return new ParameterInfo
            {
                _parameter = restId,
                _name = restParameter!.Name,
                _isRestParameter = true,
            };
        }
        return null;
    }

    /// <summary>Go: <c>func (s *inlayHintState) leadingCommentsContainsParameterName(node *ast.Node, name string) bool</c>（inlay_hints.go:950-...）。</summary>
    private bool LeadingCommentsContainsParameterName(Node node, string name)
    {
        if (!Scanner.Scanner.IsIdentifierText(name, _file.LanguageVariant))
        {
            return false;
        }

        var ranges = GetLeadingCommentRangesOfNode(node, _file);
        var fileText = _file.Text();
        foreach (var r in ranges ?? [])
        {
            var commentText = TrimFunc(
                fileText.Substring(r.Range.Pos(), r.Range.End() - r.Range.Pos()),
                static c => char.IsWhiteSpace(c) || c == '/' || c == '*');
            if (commentText == name)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Go 的 <c>strings.TrimFunc</c>（见文件头第 7 条）。</summary>
    private static string TrimFunc(string s, Func<char, bool> f)
    {
        var start = 0;
        var end = s.Length;
        while (start < end && f(s[start]))
        {
            start++;
        }
        while (end > start && f(s[end - 1]))
        {
            end--;
        }
        return s[start..end];
    }

    private int GetTypeAnnotationPosition(Node decl)
    {
        var closeParenToken = AstNav.AstNav.FindChildOfKind(decl, Kind.CloseParenToken, _file);
        if (closeParenToken is not null)
        {
            return closeParenToken.End();
        }
        return decl.ParameterList()!.End();
    }
}

/// <summary>Go: <c>type parameterInfo struct</c>（inlay_hints.go:876 之前，见文件头第 3 条）。</summary>
internal sealed class ParameterInfo
{
    internal Node _parameter = null!;
    internal string _name = "";
    internal bool _isRestParameter;
}
