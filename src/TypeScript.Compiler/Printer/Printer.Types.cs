// ported from: internal/printer/printer.go:1873-2434（类型节点）
//   + internal/printer/printer.go:2439-2470（JSDoc 类型 emit，追加于本文件末尾；由 Main 指派本分片）
//
// 与 Go 的偏离（逐条；其余与 Go 逐行对应）：
// 1) Go 的别名 `TypePredicateParameterName`/`TypeArgument`/`TypeNode`/`TypeElement`/`TemplateLiteralTypeSpanNode`/
//    `BindingElementNode` 都是 `= Node`（ast_generated.go）→ C# 里一律用 `Node`；`TypeArgumentList`/`NodeList` 同义。
// 2) `node.AsXxx()` 在 C# 返回可空 → 分支已由 `case Kind.Xxx`（或 Go 里本就当非空用的字段）保证非空，补 `!`
//    （与 Go 类型断言失败即 panic 的语义一一对应）。同理 Go 里 `node.QuestionToken.Kind` 这类"已判空后直接解引用"
//    的写法保持原样（C# 的可空流分析覆盖成员访问）。
// 3) Go 的多值 `case A, B:` → C# `case Kind.A or Kind.B:`；`emitTypeNode` 的 `default:`（panic）在 C# 里放
//    switch 最后（Go 里它本来也在最后）。
// 4) `panic(fmt.Sprintf(...))` → `Debug.Fail($"...")`；`%v` 打在 `ast.Kind` 上（有 stringer）→ `node.Kind.String()`；
//    C# 的 switch 分支仍要 `break`（`Debug.Fail` 不是 `[DoesNotReturn]`，与 Printer.Emit.cs 总派发一致）。
// 5) `core.IfElse` → `IfElse`（Core 静态类）；`p.inExtends` → `_inExtends`；`p.writeXxx`/`p.line`/`p.indent` →
//    `WriteXxx`/`WriteLine`/`IncreaseIndent`（§2 命名规则）。
// 6) `ast.TypePrecedenceLowest`/`ast.TypePrecedenceHighest` 是 Go 的 enum 别名 → `Ast` 静态类里的 const（docs §3.3），
//    `using static TypeScript.Ast.Ast` 后裸名可用；其余优先级成员是 `TypePrecedence` 枚举本身。
// 7) `greatestEnd(end, node.QuestionToken)`：Go 的 tryGetEnd 对 nil 节点跳过，故 C# 走 `GreatestEnd(int, params object?[])`
//    重载（Main 冻结的签名，与 Printer.Jsx.cs 的同款调用一致；`Node` → `object?` 是引用转换，不装箱）；**不能**
//    改写成 `node.QuestionToken!.Loc`——`[name: T]`（无问号）时 QuestionToken 为 nil，`.Loc` 会 NRE。
// 8) Go 的 `len(node.Members.Nodes) > 0` → C# 属性模式 `node.Members.Nodes is { Length: > 0 }`：C# 的 `Node[]?` 里
//    nil 与空数组是两回事，`!.Length` 对 nil 会 NRE。
// 9) `EmitPunctuationNode`/`EmitTokenNode` 在 C# 里参数为 `Node?`（TokensShard 保留 Go 的 nil 早返回），
//    故 `p.emitPunctuationNode(node.DotDotDotToken)` 这类"未判空直接传"的调用点照 Go 原样写。
// 10) Go 的 `emitIntersectionType` 里 `LFIntersectionTypeConstituents /*, parenthesizer…*/`（行尾 `// !!!`）与
//     `emitTypeArguments` 里被注释掉的 `LFAllowTrailingComma` 表达式都照抄为注释，不落成代码。
using TypeScript.Ast;
using TypeScript.Core;
using static TypeScript.Ast.Ast;
using static TypeScript.Core.Core;

namespace TypeScript.Printer;

public sealed partial class Printer
{
    internal void EmitTypePredicateParameterName(Node node)
    {
        switch (node.Kind)
        {
            case Kind.Identifier:
                EmitIdentifierReference(node.AsIdentifier()!);
                break;
            case Kind.ThisType:
                EmitThisType(node.AsThisTypeNode()!);
                break;
            default:
                Debug.Fail($"unexpected TypePredicateParameterName: {node.Kind.String()}");
                break;
        }
    }

    internal void EmitTypePredicate(TypePredicateNode node)
    {
        var state = EnterNode(node);
        if (node.AssertsModifier is not null)
        {
            EmitTokenNode(node.AssertsModifier);
            WriteSpace();
        }
        EmitTypePredicateParameterName(node.ParameterName!);
        if (node.Type is not null)
        {
            WriteSpace();
            WriteKeyword("is");
            WriteSpace();
            EmitTypeNodeOutsideExtends(node.Type);
        }
        ExitNode(node, state);
    }

    internal void EmitTypeArgument(Node node)
    {
        EmitTypeNodeOutsideExtends(node);
    }

    internal void EmitTypeArguments(Node parentNode, NodeList? nodes)
    {
        if (nodes is null)
        {
            return;
        }
        EmitList(EmitTypeParameterDeclarationNode, parentNode, nodes, ListFormat.TypeArguments /*|IfElse(ShouldAllowTrailingComma(parentNode, nodes), ListFormat.AllowTrailingComma, ListFormat.None)*/); // TODO: preserve trailing comma after Strada migration
    }

    internal void EmitTypeReference(TypeReferenceNode node)
    {
        var state = EnterNode(node);
        EmitEntityName(node.TypeName!);
        EmitTypeArguments(node, node.TypeArguments);
        ExitNode(node, state);
    }

    /// <summary>Emits the return type of a FunctionTypeNode or ConstructorTypeNode, including the arrow (`=&gt;`)</summary>
    internal void EmitReturnType(Node? node)
    {
        if (node is null)
        {
            return;
        }
        WritePunctuation("=>");
        WriteSpace();
        if (_inExtends && node.Kind == Kind.InferType && node.AsInferTypeNode()!.TypeParameter!.AsTypeParameterDeclaration()!.Constraint is not null)
        {
            // if the parent FunctionTypeNode or ConstructorTypeNode is in the `extends` clause of a ConditionalTypeNode,
            // we must parenthesize `infer ... extends ...` so as not to result in an ambiguous parse.
            //
            // `T extends () => infer U extends V ? W : X` would parse the `? W : X` as part of a ConditionalTypeNode in the
            // return type of the FunctionTypeNode, thus we must emit as `T extends () => (infer U extends V) ? W : X`
            EmitTypeNodePreservingExtends(node, TypePrecedenceHighest);
        }
        else
        {
            EmitTypeNodePreservingExtends(node, TypePrecedenceLowest);
        }
    }

    internal void EmitFunctionType(FunctionTypeNode node)
    {
        var state = EnterNode(node);
        var indented = ShouldEmitIndented(node);
        IncreaseIndentIf(indented);
        PushNameGenerationScope(node);
        // !!! in the old emitter, quickinfo uses type arguments in place of type parameters for instantiated signatures
        EmitTypeParameters(node, node.TypeParameters);
        EmitParameters(node, node.Parameters);
        WriteSpace();
        EmitReturnType(node.Type);
        PopNameGenerationScope(node);
        DecreaseIndentIf(indented);
        ExitNode(node, state);
    }

    internal void EmitConstructorType(ConstructorTypeNode node)
    {
        var state = EnterNode(node);
        EmitModifierList(node, node.Modifiers(), false /*allowDecorators*/);
        WriteKeyword("new");
        WriteSpace();
        var indented = ShouldEmitIndented(node);
        IncreaseIndentIf(indented);
        PushNameGenerationScope(node);
        // !!! in the old emitter, quickinfo uses type arguments in place of type parameters for instantiated signatures
        EmitTypeParameters(node, node.TypeParameters);
        EmitParameters(node, node.Parameters);
        WriteSpace();
        EmitReturnType(node.Type);
        PopNameGenerationScope(node);
        DecreaseIndentIf(indented);
        ExitNode(node, state);
    }

    internal void EmitTypeQuery(TypeQueryNode node)
    {
        var state = EnterNode(node);
        WriteKeyword("typeof");
        WriteSpace();
        EmitEntityName(node.ExprName!);
        EmitTypeArguments(node, node.TypeArguments);
        ExitNode(node, state);
    }

    internal void EmitTypeLiteral(TypeLiteralNode node)
    {
        var state = EnterNode(node);
        PushNameGenerationScope(node);
        GenerateAllMemberNames(node.Members);
        WritePunctuation("{");
        var flags = IfElse(ShouldEmitOnSingleLine(node), ListFormat.SingleLineTypeLiteralMembers, ListFormat.MultiLineTypeLiteralMembers);
        EmitList(EmitTypeElement, node, node.Members, flags | ListFormat.NoSpaceIfEmpty);
        WritePunctuation("}");
        PopNameGenerationScope(node);
        ExitNode(node, state);
    }

    internal void EmitArrayType(ArrayTypeNode node)
    {
        var state = EnterNode(node);
        EmitPostfixTypeOperand(node.ElementType!, node);
        WritePunctuation("[");
        WritePunctuation("]");
        ExitNode(node, state);
    }

    /// <summary>
    /// emitPostfixTypeOperand emits the operand of a postfix type (ArrayType, IndexedAccessType,
    /// OptionalType). It is equivalent to `emitTypeNode(operand, TypePrecedencePostfix)` except
    /// that it preserves a parsed `typeof X` operand without adding parentheses (e.g.,
    /// `typeof C[K]` instead of `(typeof C)[K]`). TypeScript's `parenthesizeNonArrayTypeOfPostfixType`
    /// factory rule wraps `TypeQuery` in `ParenthesizedType` only when a postfix type is constructed
    /// via the factory, so parsed postfix types preserve the source as written during round-trip
    /// emit while synthesized postfix types (e.g., from declaration emit) still get the parentheses.
    /// </summary>
    internal void EmitPostfixTypeOperand(Node operand, Node parent)
    {
        if (IsParseTreeNode(parent) && operand.Kind == Kind.TypeQuery)
        {
            EmitTypeNode(operand, TypePrecedence.TypeOperator);
            return;
        }
        EmitTypeNode(operand, TypePrecedence.Postfix);
    }

    internal void EmitTupleElementType(Node node)
    {
        EmitTypeNodeOutsideExtends(node);
    }

    internal void EmitTupleType(TupleTypeNode node)
    {
        var state = EnterNode(node);
        EmitToken(Kind.OpenBracketToken, node.Pos(), WriteKind.Punctuation, node);
        var flags = IfElse(ShouldEmitOnSingleLine(node), ListFormat.SingleLineTupleTypeElements, ListFormat.MultiLineTupleTypeElements);
        EmitList(EmitTupleElementType, node, node.Elements, flags | ListFormat.NoSpaceIfEmpty);
        EmitToken(Kind.CloseBracketToken, node.Elements!.End(), WriteKind.Punctuation, node);
        ExitNode(node, state);
    }

    internal void EmitRestType(RestTypeNode node)
    {
        var state = EnterNode(node);
        WritePunctuation("...");
        EmitTypeNodeOutsideExtends(node.Type!);
        ExitNode(node, state);
    }

    internal void EmitOptionalType(OptionalTypeNode node)
    {
        var state = EnterNode(node);
        // !!! May need extra parenthesization if we also have JSDocNullableType
        EmitPostfixTypeOperand(node.Type!, node);
        WritePunctuation("?");
        ExitNode(node, state);
    }

    internal void EmitNamedTupleMember(NamedTupleMember node)
    {
        var state = EnterNode(node);
        EmitPunctuationNode(node.DotDotDotToken);
        EmitIdentifierName(node.Name()!.AsIdentifier()!);
        EmitPunctuationNode(node.QuestionToken);
        EmitToken(Kind.ColonToken, GreatestEnd(node.Name()!.End(), node.QuestionToken!), WriteKind.Punctuation, node);
        WriteSpace();
        EmitTypeNodeOutsideExtends(node.Type!);
        ExitNode(node, state);
    }

    internal void EmitUnionTypeConstituent(Node node)
    {
        EmitTypeNode(node, TypePrecedence.TypeOperator);
    }

    internal void EmitUnionType(UnionTypeNode node)
    {
        var state = EnterNode(node);
        EmitList(EmitUnionTypeConstituent, node, node.Types, ListFormat.UnionTypeConstituents);
        ExitNode(node, state);
    }

    internal void EmitIntersectionTypeConstituent(Node node)
    {
        EmitTypeNode(node, TypePrecedence.TypeOperator);
    }

    internal void EmitIntersectionType(IntersectionTypeNode node)
    {
        var state = EnterNode(node);
        EmitList(EmitIntersectionTypeConstituent, node, node.Types, ListFormat.IntersectionTypeConstituents /*, parenthesizer.parenthesizeConstituentTypeOfIntersectionType*/); // !!!
        ExitNode(node, state);
    }

    internal void EmitConditionalType(ConditionalTypeNode node)
    {
        var state = EnterNode(node);
        EmitTypeNode(node.CheckType!, TypePrecedence.Union);
        WriteSpace();
        WriteKeyword("extends");
        WriteSpace();
        EmitTypeNodeInExtends(node.ExtendsType!);
        WriteSpace();
        WritePunctuation("?");
        WriteSpace();
        EmitTypeNodeOutsideExtends(node.TrueType!);
        WriteSpace();
        WritePunctuation(":");
        WriteSpace();
        EmitTypeNodeOutsideExtends(node.FalseType!);
        ExitNode(node, state);
    }

    internal void EmitInferTypeParameter(TypeParameterDeclaration node)
    {
        var state = EnterNode(node);
        EmitBindingIdentifier(node.Name()!.AsIdentifier()!);
        if (node.Constraint is not null)
        {
            WriteSpace();
            WriteKeyword("extends");
            WriteSpace();
            EmitTypeNodeInExtends(node.Constraint);
        }
        ExitNode(node, state);
    }

    internal void EmitInferType(InferTypeNode node)
    {
        var state = EnterNode(node);
        WriteKeyword("infer");
        WriteSpace();
        EmitInferTypeParameter(node.TypeParameter!.AsTypeParameterDeclaration()!);
        ExitNode(node, state);
    }

    internal void EmitParenthesizedType(ParenthesizedTypeNode node)
    {
        var state = EnterNode(node);
        WritePunctuation("(");
        EmitTypeNodeOutsideExtends(node.Type!);
        WritePunctuation(")");
        ExitNode(node, state);
    }

    internal void EmitThisType(ThisTypeNode node)
    {
        var state = EnterNode(node);
        WriteKeyword("this");
        ExitNode(node, state);
    }

    internal void EmitTypeOperator(TypeOperatorNode node)
    {
        var state = EnterNode(node);
        EmitToken(node.Operator, node.Pos(), WriteKind.Keyword, node);
        WriteSpace();
        EmitTypeNode(node.Type!, IfElse(node.Operator == Kind.ReadonlyKeyword, TypePrecedence.Postfix, TypePrecedence.TypeOperator));
        ExitNode(node, state);
    }

    internal void EmitIndexedAccessType(IndexedAccessTypeNode node)
    {
        var state = EnterNode(node);
        EmitPostfixTypeOperand(node.ObjectType!, node);
        WritePunctuation("[");
        EmitTypeNodeOutsideExtends(node.IndexType!);
        WritePunctuation("]");
        ExitNode(node, state);
    }

    internal void EmitMappedTypeParameter(TypeParameterDeclaration node)
    {
        var state = EnterNode(node);
        EmitBindingIdentifier(node.Name()!.AsIdentifier()!);
        WriteSpace();
        WriteKeyword("in");
        WriteSpace();
        EmitTypeNodeOutsideExtends(node.Constraint!);
        ExitNode(node, state);
    }

    internal void EmitMappedType(MappedTypeNode node)
    {
        var state = EnterNode(node);
        var singleLine = ShouldEmitOnSingleLine(node);
        WritePunctuation("{");
        if (singleLine)
        {
            WriteSpace();
        }
        else
        {
            WriteLine();
            IncreaseIndent();
        }
        if (node.ReadonlyToken is not null)
        {
            EmitTokenNode(node.ReadonlyToken);
            if (node.ReadonlyToken.Kind != Kind.ReadonlyKeyword)
            {
                WriteKeyword("readonly");
            }
            WriteSpace();
        }
        WritePunctuation("[");
        EmitMappedTypeParameter(node.TypeParameter!.AsTypeParameterDeclaration()!);
        if (node.NameType is not null)
        {
            WriteSpace();
            WriteKeyword("as");
            WriteSpace();
            EmitTypeNodeOutsideExtends(node.NameType);
        }
        WritePunctuation("]");
        if (node.QuestionToken is not null)
        {
            EmitPunctuationNode(node.QuestionToken);
            if (node.QuestionToken.Kind != Kind.QuestionToken)
            {
                WritePunctuation("?");
            }
        }
        if (node.Type is not null)
        {
            WritePunctuation(":");
            WriteSpace();
            EmitTypeNodeOutsideExtends(node.Type);
        }
        WriteTrailingSemicolon();
        if (node.Members is not null)
        {
            if (node.Members.Nodes is { Length: > 0 })
            {
                if (singleLine)
                {
                    WriteSpace();
                }
                else
                {
                    WriteLine();
                }
                EmitList(EmitTypeElement, node, node.Members, ListFormat.PreserveLines);
            }
        }
        if (singleLine)
        {
            WriteSpace();
        }
        else
        {
            WriteLine();
            DecreaseIndent();
        }
        WritePunctuation("}");
        ExitNode(node, state);
    }

    internal void EmitLiteralType(LiteralTypeNode node)
    {
        var state = EnterNode(node);
        EmitExpression(node.Literal!, OperatorPrecedence.Comma);
        ExitNode(node, state);
    }

    internal void EmitTemplateTypeSpan(TemplateLiteralTypeSpan node)
    {
        var state = EnterNode(node);
        EmitTypeNodeOutsideExtends(node.Type!);
        EmitTemplateMiddleTail(node.Literal!);
        ExitNode(node, state);
    }

    internal void EmitTemplateTypeSpanNode(Node node)
    {
        EmitTemplateTypeSpan(node.AsTemplateLiteralTypeSpan()!);
    }

    internal void EmitTemplateType(TemplateLiteralTypeNode node)
    {
        var state = EnterNode(node);
        EmitTemplateHead(node.Head!.AsTemplateHead()!);
        EmitList(EmitTemplateTypeSpanNode, node, node.TemplateSpans, ListFormat.TemplateExpressionSpans);
        ExitNode(node, state);
    }

    internal void EmitImportTypeNodeAttributes(ImportAttributes node)
    {
        var state = EnterNode(node);
        WritePunctuation("{");
        WriteSpace();
        WriteKeyword(IfElse(node.Token == Kind.AssertKeyword, "assert", "with"));
        WritePunctuation(":");
        WriteSpace();
        EmitList(EmitImportAttributeNode, node, node.Attributes, ListFormat.ImportAttributes);
        WriteSpace();
        WritePunctuation("}");
        ExitNode(node, state);
    }

    internal void EmitImportTypeNode(ImportTypeNode node)
    {
        var state = EnterNode(node);
        if (node.IsTypeOf)
        {
            WriteKeyword("typeof");
            WriteSpace();
        }
        WriteKeyword("import");
        WritePunctuation("(");
        EmitTypeNodeOutsideExtends(node.Argument!);
        if (node.Attributes is not null)
        {
            WritePunctuation(",");
            WriteSpace();
            EmitImportTypeNodeAttributes(node.Attributes.AsImportAttributes()!);
        }
        WritePunctuation(")");
        if (node.Qualifier is not null)
        {
            WritePunctuation(".");
            EmitEntityName(node.Qualifier);
        }
        EmitTypeArguments(node, node.TypeArguments);
        ExitNode(node, state);
    }

    /// <summary>emits a Type node in the `extends` clause of a ConditionalType</summary>
    internal void EmitTypeNodeInExtends(Node node)
    {
        var savedInExtends = _inExtends;
        _inExtends = true;
        EmitTypeNodePreservingExtends(node, TypePrecedenceLowest);
        _inExtends = savedInExtends;
    }

    /// <summary>emits a Type node not in the `extends` clause of a ConditionalType or InferType</summary>
    internal void EmitTypeNodeOutsideExtends(Node node)
    {
        var savedInExtends = _inExtends;
        _inExtends = false;
        EmitTypeNodePreservingExtends(node, TypePrecedenceLowest);
        _inExtends = savedInExtends;
    }

    /// <summary>emits a Type node preserving whether or not we are currently in the `extends` clause of a ConditionalType or InferType</summary>
    internal void EmitTypeNodePreservingExtends(Node node, TypePrecedence precedence)
    {
        EmitTypeNode(node, precedence);
    }

    internal void EmitTypeNode(Node node, TypePrecedence precedence)
    {
        if (_inExtends && precedence <= TypePrecedence.Conditional)
        {
            // in the `extends` clause of a ConditionalType or InferType, a ConditionalType must be parenthesized
            precedence = TypePrecedence.Function;
        }

        var savedInExtends = _inExtends;
        var parens = GetTypeNodePrecedence(node) < precedence;
        if (parens)
        {
            _inExtends = false;
            WritePunctuation("(");
        }

        switch (node.Kind)
        {
            // Keyword Types
            case Kind.AnyKeyword
                or Kind.UnknownKeyword
                or Kind.NumberKeyword
                or Kind.BigIntKeyword
                or Kind.ObjectKeyword
                or Kind.BooleanKeyword
                or Kind.StringKeyword
                or Kind.SymbolKeyword
                or Kind.VoidKeyword
                or Kind.UndefinedKeyword
                or Kind.NeverKeyword
                or Kind.IntrinsicKeyword:
                EmitKeywordTypeNode(node.AsKeywordTypeNode()!);
                break;

            // Types
            case Kind.TypePredicate:
                EmitTypePredicate(node.AsTypePredicateNode()!);
                break;
            case Kind.TypeReference:
                EmitTypeReference(node.AsTypeReferenceNode()!);
                break;
            case Kind.FunctionType:
                EmitFunctionType(node.AsFunctionTypeNode()!);
                break;
            case Kind.ConstructorType:
                EmitConstructorType(node.AsConstructorTypeNode()!);
                break;
            case Kind.TypeQuery:
                EmitTypeQuery(node.AsTypeQueryNode()!);
                break;
            case Kind.TypeLiteral:
                EmitTypeLiteral(node.AsTypeLiteralNode()!);
                break;
            case Kind.ArrayType:
                EmitArrayType(node.AsArrayTypeNode()!);
                break;
            case Kind.TupleType:
                EmitTupleType(node.AsTupleTypeNode()!);
                break;
            case Kind.OptionalType:
                EmitOptionalType(node.AsOptionalTypeNode()!);
                break;
            case Kind.RestType:
                EmitRestType(node.AsRestTypeNode()!);
                break;
            case Kind.UnionType:
                EmitUnionType(node.AsUnionTypeNode()!);
                break;
            case Kind.IntersectionType:
                EmitIntersectionType(node.AsIntersectionTypeNode()!);
                break;
            case Kind.ConditionalType:
                EmitConditionalType(node.AsConditionalTypeNode()!);
                break;
            case Kind.InferType:
                EmitInferType(node.AsInferTypeNode()!);
                break;
            case Kind.ParenthesizedType:
                EmitParenthesizedType(node.AsParenthesizedTypeNode()!);
                break;
            case Kind.ThisType:
                EmitThisType(node.AsThisTypeNode()!);
                break;
            case Kind.TypeOperator:
                EmitTypeOperator(node.AsTypeOperatorNode()!);
                break;
            case Kind.IndexedAccessType:
                EmitIndexedAccessType(node.AsIndexedAccessTypeNode()!);
                break;
            case Kind.MappedType:
                EmitMappedType(node.AsMappedTypeNode()!);
                break;
            case Kind.LiteralType:
                EmitLiteralType(node.AsLiteralTypeNode()!);
                break;
            case Kind.NamedTupleMember:
                EmitNamedTupleMember(node.AsNamedTupleMember()!);
                break;
            case Kind.TemplateLiteralType:
                EmitTemplateType(node.AsTemplateLiteralTypeNode()!);
                break;
            case Kind.TemplateLiteralTypeSpan:
                EmitTemplateTypeSpan(node.AsTemplateLiteralTypeSpan()!);
                break;
            case Kind.ImportType:
                EmitImportTypeNode(node.AsImportTypeNode()!);
                break;

            case Kind.PropertyAccessExpression:
                // Occurs in pseudo-types such as `f<T>.C`, where `f` is a generic function and `C` is a local type
                EmitPropertyAccessExpression(node.AsPropertyAccessExpression()!);
                break;
            case Kind.ExpressionWithTypeArguments:
                // !!! Should this actually be considered a type?
                EmitExpressionWithTypeArguments(node.AsExpressionWithTypeArguments()!);
                break;

            case Kind.JSDocAllType:
                EmitJSDocAllType(node);
                break;
            case Kind.JSDocNonNullableType:
                EmitJSDocNonNullableType(node.AsJSDocNonNullableType()!);
                break;
            case Kind.JSDocNullableType:
                EmitJSDocNullableType(node.AsJSDocNullableType()!);
                break;
            case Kind.JSDocOptionalType:
                EmitJSDocOptionalType(node.AsJSDocOptionalType()!);
                break;
            case Kind.JSDocVariadicType:
                EmitJSDocVariadicType(node.AsJSDocVariadicType()!);
                break;

            default:
                Debug.Fail($"unhandled TypeNode: {node.Kind.String()}");
                break;
        }

        if (parens)
        {
            WritePunctuation(")");
        }

        _inExtends = savedInExtends;
    }

    //
    // Binding patterns
    //

    internal void EmitObjectBindingPattern(BindingPattern node)
    {
        var state = EnterNode(node);
        WritePunctuation("{");
        EmitList(EmitBindingElementNode, node, node.Elements, ListFormat.ObjectBindingPatternElements);
        WritePunctuation("}");
        ExitNode(node, state);
    }

    internal void EmitArrayBindingPattern(BindingPattern node)
    {
        var state = EnterNode(node);
        WritePunctuation("[");
        EmitList(EmitBindingElementNode, node, node.Elements, ListFormat.ArrayBindingPatternElements);
        WritePunctuation("]");
        ExitNode(node, state);
    }

    internal void EmitBindingElement(BindingElement node)
    {
        var state = EnterNode(node);
        EmitTokenNode(node.DotDotDotToken);
        if (node.PropertyName is not null)
        {
            EmitPropertyName(node.PropertyName);
            WritePunctuation(":");
            WriteSpace();
        }
        // Old parser used `OmittedExpression` as a substitute for `Elision`. New parser uses a `BindingElement` with nil members
        var name = node.Name();
        if (name is not null)
        {
            EmitBindingName(name);
            EmitInitializer(node.Initializer, node.Name()!.End(), node);
        }
        ExitNode(node, state);
    }

    internal void EmitBindingElementNode(Node node)
    {
        EmitBindingElement(node.AsBindingElement()!);
    }

    // JSDoc 类型的 5 个 emitter（Go printer.go:2439-2470）在 Printer.Expressions.cs（ExpressionShard 的行范围
    // 始于 2439）里定义；这里只保留调用点，避免 CS0111 重复定义。
}
