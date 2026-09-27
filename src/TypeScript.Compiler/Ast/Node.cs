// ported from: internal/ast/ast.go（Node / MutableNode / NodeDefault 的访问器部分）
//
// 与 Go 的差异（C# 语言特性；另见 docs/ast.md §2）：
// - Go 用 `nodeData` 接口 + `NodeDefault` 提供默认实现；C# 直接在 Node 上用 virtual 方法表达
//   （`n.data.Name()` 的接口调用 ↔ C# 的虚调用），默认值逐条照 NodeDefault。
// - Go 的 `type MutableNode Node` + `func (n *Node) AsMutable() *MutableNode` 是"同一对象的另一视角"；
//   C# 没有指针重解释，所以 `AsMutable()` 返回 `this`，四个 Set* 方法直接落在 Node 上。
// - `SubtreeFacts`/`ModifierFlags`/`Symbol` 写成**属性**（名字与返回类型名相同；C# 里方法会遮蔽类型名，
//   属性不会——Color Color 规则），调用点由 Go 的 `x.SubtreeFacts()` 改成 `x.SubtreeFacts`。
// - Go 的 `panic("Unhandled case in …")` → `throw new DebugFailureException(...)`（Debug.Fail 只是它的包装；
//   throw 让编译器接受控制流）。
// - Go 的 `strings.Join(n.AsJSDocText().text, "")` → `JoinText(...)`（`string.Concat` 不接受 null 数组，
//   而 Go 的 nil slice join 结果为空串）。
// - `IterChildren` 在 Go 里是零分配的 `iter.Seq[*Node]`；C# 的 `IEnumerable<Node>` 需要缓冲（整个 TSGO 里
//   只有 1 处调用点：transformers/declarations），实现见方法注释。
using TypeScript.Core;
using static TypeScript.Core.Core;

namespace TypeScript.Ast;

/// <summary>
/// AST 节点头（Go 的 <c>type Node struct</c>）。身份语义 = 引用相等，**不要**覆写
/// <c>Equals</c>/<c>GetHashCode</c>（Go 里是 <c>*Node</c> 指针相等）。
/// </summary>
public abstract partial class Node : IKindString, INodeBase
{
    public Kind Kind;
    public NodeFlags Flags;
    public TextRange Loc;
    public Node? Parent;

    /// <summary>Go 的 NodeBase.Flags（NodeBase 在 C# 里是接口 <see cref="INodeBase"/>，所以这里显式实现）。</summary>
    NodeFlags INodeBase.Flags
    {
        get => Flags;
        set => Flags = value;
    }

    // Go: id atomic.Uint64（懒分配，见 Utilities.cs 的 GetNodeId）。未导出 → internal + _camelCase。
    internal ulong _id;

    public Node AsNode() => this;

    public int Pos() => Loc.Pos();

    public int End() => Loc.End();

    /// <summary>
    /// Go 的 <c>func (n *Node) IterChildren() iter.Seq[*Node]</c>。
    /// 偏离：Go 的迭代器可以零分配地提前停止；C# 的 <c>IEnumerable</c> 无法从回调里中断，
    /// 所以先把子节点收进列表再 yield（调用点在整棵树的重写上，不是热路径）。
    /// </summary>
    public IEnumerable<Node> IterChildren()
    {
        // ForEachChild 的回调约定是 **true = 停止遍历**（见 Ast.Visit/VisitNodes），
        // 所以要读完所有子节点必须返回 false。
        // （Go 里是 `return !yield(child)`：yield 返回 false 表示迭代器停止 → ForEachChild 返回 true。
        //   这里先把子节点收全，再交给 foreach 的 yield——调用点停止枚举时回调已经跑完了。）
        var buffer = new List<Node>();
        ForEachChild(child =>
        {
            buffer.Add(child);
            return false;
        });
        foreach (var child in buffer)
        {
            yield return child;
        }
    }

    // ── nodeData 方法集（NodeDefault 的默认值）──

    public virtual bool ForEachChild(Visitor v) => false;

    public virtual Node? VisitEachChild(NodeVisitor v) => this;

    public virtual Node? Clone(NodeFactoryCoercible f) => null;

    public virtual Node? Name() => null;

    public virtual ModifierList? Modifiers() => null;

    public virtual void SetModifiers(ModifierList? modifiers)
    {
    }

    public virtual IFlowNodeBase? FlowNodeData() => null;

    public virtual IDeclarationBase? DeclarationData() => null;

    public virtual IExportableBase? ExportableData() => null;

    public virtual ILocalsContainerBase? LocalsContainerData() => null;

    public virtual IFunctionLikeBase? FunctionLikeData() => null;

    public virtual IClassLikeBase? ClassLikeData() => null;

    public virtual IBodyBase? BodyData() => null;

    public virtual ILiteralLikeNodeBase? LiteralLikeData() => null;

    public virtual ITemplateLiteralLikeNodeBase? TemplateLiteralLikeData() => null;

    /// <summary>
    /// Go 的 <c>NodeDefault.SubtreeFacts()</c> + <c>CompositeBase.subtreeFactsWorker</c>：
    /// 非 composite 节点直接算；composite 节点把结果连同 <c>SubtreeFacts.Computed</c> 缓存到 `_facts`
    /// （Go 的注释：computeSubtreeFacts 是幂等的，所以竞争只影响耗时）。
    /// </summary>
    public virtual SubtreeFacts SubtreeFacts
    {
        get
        {
            if (this is ICompositeBase composite)
            {
                var facts = (SubtreeFacts)composite.Facts;
                if ((facts & SubtreeFacts.Computed) == 0)
                {
                    facts |= ComputeSubtreeFacts() | SubtreeFacts.Computed;
                    composite.Facts = (int)facts;
                }

                return facts & ~SubtreeFacts.Computed;
            }

            return ComputeSubtreeFacts();
        }
    }

    internal virtual SubtreeFacts ComputeSubtreeFacts() => SubtreeFacts.None;

    internal virtual SubtreeFacts SubtreeFactsWorker() => ComputeSubtreeFacts();

    internal virtual SubtreeFacts PropagateSubtreeFacts() => SubtreeFacts & ~SubtreeFacts.ExclusionsNode;

    // ── 其他访问器 ──

    public string KindString() => Kind.String();

    public short KindValue() => (short)Kind;

    public Node[]? Decorators()
    {
        var modifiers = Modifiers();
        if (modifiers is null)
        {
            return null;
        }

        return Filter(modifiers.Nodes, n => Ast.IsDecorator(n!));
    }

    public Node AsMutable() => this;

    /// <summary>Go 的 <c>func (m *MutableNode) SetExpression(expr *Node)</c>。</summary>
    public void SetExpression(Node? expr)
    {
        switch (Kind)
        {
            case Kind.PropertyAccessExpression:
                AsPropertyAccessExpression()!.Expression = expr;
                break;
            case Kind.ElementAccessExpression:
                AsElementAccessExpression()!.Expression = expr;
                break;
            case Kind.ParenthesizedExpression:
                AsParenthesizedExpression()!.Expression = expr;
                break;
            case Kind.CallExpression:
                AsCallExpression()!.Expression = expr;
                break;
            case Kind.NewExpression:
                AsNewExpression()!.Expression = expr;
                break;
            case Kind.ExpressionWithTypeArguments:
                AsExpressionWithTypeArguments()!.Expression = expr;
                break;
            case Kind.ComputedPropertyName:
                AsComputedPropertyName()!.Expression = expr;
                break;
            case Kind.NonNullExpression:
                AsNonNullExpression()!.Expression = expr;
                break;
            case Kind.TypeAssertionExpression:
                AsTypeAssertion()!.Expression = expr;
                break;
            case Kind.AsExpression:
                AsAsExpression()!.Expression = expr;
                break;
            case Kind.SatisfiesExpression:
                AsSatisfiesExpression()!.Expression = expr;
                break;
            case Kind.TypeOfExpression:
                AsTypeOfExpression()!.Expression = expr;
                break;
            case Kind.SpreadAssignment:
                AsSpreadAssignment()!.Expression = expr;
                break;
            case Kind.SpreadElement:
                AsSpreadElement()!.Expression = expr;
                break;
            case Kind.TemplateSpan:
                AsTemplateSpan()!.Expression = expr;
                break;
            case Kind.DeleteExpression:
                AsDeleteExpression()!.Expression = expr;
                break;
            case Kind.VoidExpression:
                AsVoidExpression()!.Expression = expr;
                break;
            case Kind.AwaitExpression:
                AsAwaitExpression()!.Expression = expr;
                break;
            case Kind.YieldExpression:
                AsYieldExpression()!.Expression = expr;
                break;
            case Kind.PartiallyEmittedExpression:
                AsPartiallyEmittedExpression()!.Expression = expr;
                break;
            case Kind.IfStatement:
                AsIfStatement()!.Expression = expr;
                break;
            case Kind.DoStatement:
                AsDoStatement()!.Expression = expr;
                break;
            case Kind.WhileStatement:
                AsWhileStatement()!.Expression = expr;
                break;
            case Kind.WithStatement:
                AsWithStatement()!.Expression = expr;
                break;
            case Kind.ForInStatement:
            case Kind.ForOfStatement:
                AsForInOrOfStatement()!.Expression = expr;
                break;
            case Kind.SwitchStatement:
                AsSwitchStatement()!.Expression = expr;
                break;
            case Kind.CaseClause:
            case Kind.DefaultClause:
                AsCaseOrDefaultClause()!.Expression = expr;
                break;
            case Kind.ExpressionStatement:
                AsExpressionStatement()!.Expression = expr;
                break;
            case Kind.ReturnStatement:
                AsReturnStatement()!.Expression = expr;
                break;
            case Kind.ThrowStatement:
                AsThrowStatement()!.Expression = expr;
                break;
            case Kind.ExternalModuleReference:
                AsExternalModuleReference()!.Expression = expr;
                break;
            case Kind.ExportAssignment:
                AsExportAssignment()!.Expression = expr;
                break;
            case Kind.Decorator:
                AsDecorator()!.Expression = expr;
                break;
            case Kind.JsxExpression:
                AsJsxExpression()!.Expression = expr;
                break;
            case Kind.JsxSpreadAttribute:
                AsJsxSpreadAttribute()!.Expression = expr;
                break;
            default:
                throw new DebugFailureException("Unhandled case in mutableNode.SetExpression: " + Kind.String());
        }
    }

    /// <summary>Go 的 <c>func (m *MutableNode) SetType(t *Node)</c>。</summary>
    public void SetType(Node? t)
    {
        switch (Kind)
        {
            case Kind.VariableDeclaration:
                AsVariableDeclaration()!.Type = t;
                break;
            case Kind.Parameter:
                AsParameterDeclaration()!.Type = t;
                break;
            case Kind.PropertySignature:
                AsPropertySignatureDeclaration()!.Type = t;
                break;
            case Kind.PropertyDeclaration:
                AsPropertyDeclaration()!.Type = t;
                break;
            case Kind.PropertyAssignment:
                AsPropertyAssignment()!.Type = t;
                break;
            case Kind.ShorthandPropertyAssignment:
                AsShorthandPropertyAssignment()!.Type = t;
                break;
            case Kind.TypePredicate:
                AsTypePredicateNode()!.Type = t;
                break;
            case Kind.ParenthesizedType:
                AsParenthesizedTypeNode()!.Type = t;
                break;
            case Kind.TypeOperator:
                AsTypeOperatorNode()!.Type = t;
                break;
            case Kind.MappedType:
                AsMappedTypeNode()!.Type = t;
                break;
            case Kind.TypeAssertionExpression:
                AsTypeAssertion()!.Type = t;
                break;
            case Kind.AsExpression:
                AsAsExpression()!.Type = t;
                break;
            case Kind.SatisfiesExpression:
                AsSatisfiesExpression()!.Type = t;
                break;
            case Kind.TypeAliasDeclaration:
            case Kind.JSTypeAliasDeclaration:
                AsTypeAliasDeclaration()!.Type = t;
                break;
            case Kind.NamedTupleMember:
                AsNamedTupleMember()!.Type = t;
                break;
            case Kind.OptionalType:
                AsOptionalTypeNode()!.Type = t;
                break;
            case Kind.RestType:
                AsRestTypeNode()!.Type = t;
                break;
            case Kind.TemplateLiteralTypeSpan:
                AsTemplateLiteralTypeSpan()!.Type = t;
                break;
            case Kind.JSDocTypeExpression:
                AsJSDocTypeExpression()!.Type = t;
                break;
            case Kind.JSDocParameterTag:
            case Kind.JSDocPropertyTag:
                AsJSDocParameterOrPropertyTag()!.TypeExpression = t;
                break;
            case Kind.JSDocNullableType:
                AsJSDocNullableType()!.Type = t;
                break;
            case Kind.JSDocNonNullableType:
                AsJSDocNonNullableType()!.Type = t;
                break;
            case Kind.JSDocOptionalType:
                AsJSDocOptionalType()!.Type = t;
                break;
            case Kind.ExportAssignment:
                AsExportAssignment()!.Type = t;
                break;
            case Kind.BinaryExpression:
                AsBinaryExpression()!.Type = t;
                break;
            default:
                var funcLike = FunctionLikeData();
                if (funcLike is not null)
                {
                    funcLike.Type = t;
                    break;
                }

                throw new DebugFailureException("Unhandled case in mutableNode.SetType: " + Kind.String());
        }
    }

    /// <summary>Go 的 <c>func (m *MutableNode) SetInitializer(initializer *Node)</c>。</summary>
    public void SetInitializer(Node? initializer)
    {
        switch (Kind)
        {
            case Kind.VariableDeclaration:
                AsVariableDeclaration()!.Initializer = initializer;
                break;
            case Kind.Parameter:
                AsParameterDeclaration()!.Initializer = initializer;
                break;
            case Kind.BindingElement:
                AsBindingElement()!.Initializer = initializer;
                break;
            case Kind.PropertyDeclaration:
                AsPropertyDeclaration()!.Initializer = initializer;
                break;
            case Kind.PropertySignature:
                AsPropertySignatureDeclaration()!.Initializer = initializer;
                break;
            case Kind.PropertyAssignment:
                AsPropertyAssignment()!.Initializer = initializer;
                break;
            case Kind.EnumMember:
                AsEnumMember()!.Initializer = initializer;
                break;
            case Kind.ForStatement:
                AsForStatement()!.Initializer = initializer;
                break;
            case Kind.ForInStatement:
            case Kind.ForOfStatement:
                AsForInOrOfStatement()!.Initializer = initializer;
                break;
            case Kind.JsxAttribute:
                AsJsxAttribute()!.Initializer = initializer;
                break;
            default:
                throw new DebugFailureException("Unhandled case in mutableNode.SetInitializer");
        }
    }

    /// <summary>Go: <c>func (n *Node) Symbol() *Symbol</c>（见文件头：名字与类型名相同 → 属性）。</summary>
    public virtual Symbol? Symbol
    {
        get
        {
            var data = DeclarationData();
            if (data is not null)
            {
                return data.Symbol;
            }

            return null;
        }
    }

    public Symbol? LocalSymbol()
    {
        var data = ExportableData();
        if (data is not null)
        {
            return data.LocalSymbol;
        }

        return null;
    }

    public SymbolTable? Locals()
    {
        var data = LocalsContainerData();
        if (data is not null)
        {
            return data.Locals;
        }

        return null;
    }

    public Node? Body()
    {
        var data = BodyData();
        if (data is not null)
        {
            return data.Body;
        }

        return null;
    }

    /// <summary>Go: <c>func (n *Node) Text() string</c>。</summary>
    public string Text()
    {
        switch (Kind)
        {
            case Kind.Identifier:
                return AsIdentifier()!.Text;
            case Kind.PrivateIdentifier:
                return AsPrivateIdentifier()!.Text;
            case Kind.StringLiteral:
                return AsStringLiteral()!.Text;
            case Kind.NumericLiteral:
                return AsNumericLiteral()!.Text;
            case Kind.BigIntLiteral:
                return AsBigIntLiteral()!.Text;
            case Kind.MetaProperty:
                return AsMetaProperty()!.Name()!.Text();
            case Kind.NoSubstitutionTemplateLiteral:
                return AsNoSubstitutionTemplateLiteral()!.Text;
            case Kind.TemplateHead:
                return AsTemplateHead()!.Text;
            case Kind.TemplateMiddle:
                return AsTemplateMiddle()!.Text;
            case Kind.TemplateTail:
                return AsTemplateTail()!.Text;
            case Kind.JsxNamespacedName:
                return AsJsxNamespacedName()!.Namespace!.Text() + ":" + AsJsxNamespacedName()!._name!.Text();
            case Kind.RegularExpressionLiteral:
                return AsRegularExpressionLiteral()!.Text;
            case Kind.JSDocText:
                return JoinText(AsJSDocText()!._text);
            case Kind.JSDocLink:
                return JoinText(AsJSDocLink()!._text);
            case Kind.JSDocLinkCode:
                return JoinText(AsJSDocLinkCode()!._text);
            case Kind.JSDocLinkPlain:
                return JoinText(AsJSDocLinkPlain()!._text);
            default:
                throw new DebugFailureException("Unhandled case in Node.Text: " + Kind.String());
        }
    }

    /// <summary>Go: <c>func (n *Node) Expression() *Node</c>。</summary>
    public Node? Expression()
    {
        switch (Kind)
        {
            case Kind.PropertyAccessExpression:
                return AsPropertyAccessExpression()!.Expression;
            case Kind.ElementAccessExpression:
                return AsElementAccessExpression()!.Expression;
            case Kind.ParenthesizedExpression:
                return AsParenthesizedExpression()!.Expression;
            case Kind.CallExpression:
                return AsCallExpression()!.Expression;
            case Kind.NewExpression:
                return AsNewExpression()!.Expression;
            case Kind.ExpressionWithTypeArguments:
                return AsExpressionWithTypeArguments()!.Expression;
            case Kind.ComputedPropertyName:
                return AsComputedPropertyName()!.Expression;
            case Kind.NonNullExpression:
                return AsNonNullExpression()!.Expression;
            case Kind.TypeAssertionExpression:
                return AsTypeAssertion()!.Expression;
            case Kind.AsExpression:
                return AsAsExpression()!.Expression;
            case Kind.SatisfiesExpression:
                return AsSatisfiesExpression()!.Expression;
            case Kind.TypeOfExpression:
                return AsTypeOfExpression()!.Expression;
            case Kind.SpreadAssignment:
                return AsSpreadAssignment()!.Expression;
            case Kind.SpreadElement:
                return AsSpreadElement()!.Expression;
            case Kind.TemplateSpan:
                return AsTemplateSpan()!.Expression;
            case Kind.DeleteExpression:
                return AsDeleteExpression()!.Expression;
            case Kind.VoidExpression:
                return AsVoidExpression()!.Expression;
            case Kind.AwaitExpression:
                return AsAwaitExpression()!.Expression;
            case Kind.YieldExpression:
                return AsYieldExpression()!.Expression;
            case Kind.PartiallyEmittedExpression:
                return AsPartiallyEmittedExpression()!.Expression;
            case Kind.IfStatement:
                return AsIfStatement()!.Expression;
            case Kind.DoStatement:
                return AsDoStatement()!.Expression;
            case Kind.WhileStatement:
                return AsWhileStatement()!.Expression;
            case Kind.WithStatement:
                return AsWithStatement()!.Expression;
            case Kind.ForInStatement:
            case Kind.ForOfStatement:
                return AsForInOrOfStatement()!.Expression;
            case Kind.SwitchStatement:
                return AsSwitchStatement()!.Expression;
            case Kind.CaseClause:
            case Kind.DefaultClause:
                return AsCaseOrDefaultClause()!.Expression;
            case Kind.ExpressionStatement:
                return AsExpressionStatement()!.Expression;
            case Kind.ReturnStatement:
                return AsReturnStatement()!.Expression;
            case Kind.ThrowStatement:
                return AsThrowStatement()!.Expression;
            case Kind.ExternalModuleReference:
                return AsExternalModuleReference()!.Expression;
            case Kind.ExportAssignment:
                return AsExportAssignment()!.Expression;
            case Kind.Decorator:
                return AsDecorator()!.Expression;
            case Kind.JsxExpression:
                return AsJsxExpression()!.Expression;
            case Kind.JsxSpreadAttribute:
                return AsJsxSpreadAttribute()!.Expression;
            default:
                throw new DebugFailureException("Unhandled case in Node.Expression: " + Kind.String());
        }
    }

    /// <summary>Go: <c>func (n *Node) RawText() string</c>。</summary>
    public string RawText()
    {
        switch (Kind)
        {
            case Kind.TemplateHead:
                return AsTemplateHead()!.RawText;
            case Kind.TemplateMiddle:
                return AsTemplateMiddle()!.RawText;
            case Kind.TemplateTail:
                return AsTemplateTail()!.RawText;
            default:
                throw new DebugFailureException("Unhandled case in Node.RawText: " + Kind.String());
        }
    }

    /// <summary>Go: <c>func (n *Node) ArgumentList() *NodeList</c>。</summary>
    public NodeList? ArgumentList()
    {
        switch (Kind)
        {
            case Kind.CallExpression:
                return AsCallExpression()!.Arguments;
            case Kind.NewExpression:
                return AsNewExpression()!.Arguments;
            default:
                throw new DebugFailureException("Unhandled case in Node.Arguments: " + Kind.String());
        }
    }

    public Node[]? Arguments() => ArgumentList()?.Nodes;

    /// <summary>Go: <c>func (n *Node) ParameterList() *ParameterList</c>。</summary>
    public NodeList? ParameterList()
    {
        var funcLike = FunctionLikeData();
        if (funcLike is not null)
        {
            return funcLike.Parameters;
        }

        throw new DebugFailureException("Unhandled case in Node.ParameterList: " + Kind.String());
    }

    public Node[]? Parameters() => ParameterList()?.Nodes;

    /// <summary>Go: <c>func (n *Node) TypeArgumentList() *NodeList</c>。</summary>
    public NodeList? TypeArgumentList()
    {
        switch (Kind)
        {
            case Kind.CallExpression:
                return AsCallExpression()!.TypeArguments!;
            case Kind.NewExpression:
                return AsNewExpression()!.TypeArguments!;
            case Kind.TaggedTemplateExpression:
                return AsTaggedTemplateExpression()!.TypeArguments!;
            case Kind.TypeReference:
                return AsTypeReferenceNode()!.TypeArguments!;
            case Kind.ExpressionWithTypeArguments:
                return AsExpressionWithTypeArguments()!.TypeArguments!;
            case Kind.ImportType:
                return AsImportTypeNode()!.TypeArguments!;
            case Kind.TypeQuery:
                return AsTypeQueryNode()!.TypeArguments!;
            case Kind.JsxOpeningElement:
                return AsJsxOpeningElement()!.TypeArguments!;
            case Kind.JsxSelfClosingElement:
                return AsJsxSelfClosingElement()!.TypeArguments!;
            default:
                throw new DebugFailureException("Unhandled case in Node.TypeArguments");
        }
    }

    public Node[]? TypeArguments() => TypeArgumentList()?.Nodes;

    /// <summary>Go: <c>func (n *Node) TypeParameterList() *NodeList</c>。</summary>
    public NodeList? TypeParameterList()
    {
        switch (Kind)
        {
            case Kind.ClassDeclaration:
                return AsClassDeclaration()!.TypeParameters;
            case Kind.ClassExpression:
                return AsClassExpression()!.TypeParameters;
            case Kind.InterfaceDeclaration:
                return AsInterfaceDeclaration()!.TypeParameters;
            case Kind.TypeAliasDeclaration:
            case Kind.JSTypeAliasDeclaration:
                return AsTypeAliasDeclaration()!.TypeParameters;
            case Kind.JSDocTemplateTag:
                return AsJSDocTemplateTag()!.TypeParameters;
            default:
                var funcLike = FunctionLikeData();
                if (funcLike is not null)
                {
                    return funcLike.TypeParameters;
                }

                throw new DebugFailureException("Unhandled case in Node.TypeParameterList");
        }
    }

    public Node[]? TypeParameters() => TypeParameterList()?.Nodes;

    /// <summary>Go: <c>func (n *Node) MemberList() *NodeList</c>。</summary>
    public NodeList? MemberList()
    {
        switch (Kind)
        {
            case Kind.ClassDeclaration:
                return AsClassDeclaration()!.Members;
            case Kind.ClassExpression:
                return AsClassExpression()!.Members;
            case Kind.InterfaceDeclaration:
                return AsInterfaceDeclaration()!.Members;
            case Kind.EnumDeclaration:
                return AsEnumDeclaration()!.Members;
            case Kind.TypeLiteral:
                return AsTypeLiteralNode()!.Members;
            case Kind.MappedType:
                return AsMappedTypeNode()!.Members;
            default:
                throw new DebugFailureException("Unhandled case in Node.MemberList: " + Kind.String());
        }
    }

    public Node[]? Members() => MemberList()?.Nodes;

    /// <summary>Go: <c>func (n *Node) StatementList() *NodeList</c>。</summary>
    public NodeList? StatementList()
    {
        switch (Kind)
        {
            case Kind.SourceFile:
                return AsSourceFile()!.Statements;
            case Kind.Block:
                return AsBlock()!.Statements;
            case Kind.ModuleBlock:
                return AsModuleBlock()!.Statements;
            case Kind.CaseClause:
            case Kind.DefaultClause:
                return AsCaseOrDefaultClause()!.Statements;
            default:
                throw new DebugFailureException("Unhandled case in Node.StatementList: " + Kind.String());
        }
    }

    public Node[]? Statements() => StatementList()?.Nodes;

    public bool CanHaveStatements() => Kind is Kind.SourceFile or Kind.Block or Kind.ModuleBlock or Kind.CaseClause or Kind.DefaultClause;

    /// <summary>Go: <c>func (n *Node) ModifierFlags() ModifierFlags</c>（名字与类型名相同 → 属性）。</summary>
    public virtual ModifierFlags ModifierFlags
    {
        get
        {
            var modifiers = Modifiers();
            if (modifiers is not null)
            {
                return modifiers.ModifierFlags;
            }

            return ModifierFlags.None;
        }
    }

    public Node[]? ModifierNodes()
    {
        var modifiers = Modifiers();
        if (modifiers is not null)
        {
            return modifiers.Nodes;
        }

        return null;
    }

    /// <summary>Go: <c>func (n *Node) Type() *Node</c>。</summary>
    public Node? Type()
    {
        switch (Kind)
        {
            case Kind.VariableDeclaration:
                return AsVariableDeclaration()!.Type;
            case Kind.Parameter:
                return AsParameterDeclaration()!.Type;
            case Kind.PropertySignature:
                return AsPropertySignatureDeclaration()!.Type;
            case Kind.PropertyDeclaration:
                return AsPropertyDeclaration()!.Type;
            case Kind.PropertyAssignment:
                return AsPropertyAssignment()!.Type;
            case Kind.ShorthandPropertyAssignment:
                return AsShorthandPropertyAssignment()!.Type;
            case Kind.TypePredicate:
                return AsTypePredicateNode()!.Type;
            case Kind.ParenthesizedType:
                return AsParenthesizedTypeNode()!.Type;
            case Kind.TypeOperator:
                return AsTypeOperatorNode()!.Type;
            case Kind.MappedType:
                return AsMappedTypeNode()!.Type;
            case Kind.TypeAssertionExpression:
                return AsTypeAssertion()!.Type;
            case Kind.AsExpression:
                return AsAsExpression()!.Type;
            case Kind.SatisfiesExpression:
                return AsSatisfiesExpression()!.Type;
            case Kind.TypeAliasDeclaration:
            case Kind.JSTypeAliasDeclaration:
                return AsTypeAliasDeclaration()!.Type;
            case Kind.NamedTupleMember:
                return AsNamedTupleMember()!.Type;
            case Kind.OptionalType:
                return AsOptionalTypeNode()!.Type;
            case Kind.RestType:
                return AsRestTypeNode()!.Type;
            case Kind.TemplateLiteralTypeSpan:
                return AsTemplateLiteralTypeSpan()!.Type;
            case Kind.JSDocTypeExpression:
                return AsJSDocTypeExpression()!.Type;
            case Kind.JSDocParameterTag:
            case Kind.JSDocPropertyTag:
                return AsJSDocParameterOrPropertyTag()!.TypeExpression;
            case Kind.JSDocNullableType:
                return AsJSDocNullableType()!.Type;
            case Kind.JSDocNonNullableType:
                return AsJSDocNonNullableType()!.Type;
            case Kind.JSDocOptionalType:
                return AsJSDocOptionalType()!.Type;
            case Kind.ExportAssignment:
                return AsExportAssignment()!.Type;
            case Kind.BinaryExpression:
                return AsBinaryExpression()!.Type;
            default:
                var funcLike = FunctionLikeData();
                if (funcLike is not null)
                {
                    return funcLike.Type;
                }

                return null;
        }
    }

    /// <summary>Go: <c>func (n *Node) Initializer() *Node</c>。</summary>
    public Node? Initializer()
    {
        switch (Kind)
        {
            case Kind.VariableDeclaration:
                return AsVariableDeclaration()!.Initializer;
            case Kind.Parameter:
                return AsParameterDeclaration()!.Initializer;
            case Kind.BindingElement:
                return AsBindingElement()!.Initializer;
            case Kind.PropertyDeclaration:
                return AsPropertyDeclaration()!.Initializer;
            case Kind.PropertySignature:
                return AsPropertySignatureDeclaration()!.Initializer;
            case Kind.PropertyAssignment:
                return AsPropertyAssignment()!.Initializer;
            case Kind.EnumMember:
                return AsEnumMember()!.Initializer;
            case Kind.ForStatement:
                return AsForStatement()!.Initializer;
            case Kind.ForInStatement:
            case Kind.ForOfStatement:
                return AsForInOrOfStatement()!.Initializer;
            case Kind.JsxAttribute:
                return AsJsxAttribute()!.Initializer;
            default:
                throw new DebugFailureException("Unhandled case in Node.Initializer");
        }
    }

    /// <summary>Go: <c>func (n *Node) TagName() *Node</c>。</summary>
    public Node? TagName()
    {
        switch (Kind)
        {
            case Kind.JsxOpeningElement:
                return AsJsxOpeningElement()!.TagName;
            case Kind.JsxClosingElement:
                return AsJsxClosingElement()!.TagName;
            case Kind.JsxSelfClosingElement:
                return AsJsxSelfClosingElement()!.TagName;
            case Kind.JSDocUnknownTag:
                return AsJSDocUnknownTag()!.TagName;
            case Kind.JSDocAugmentsTag:
                return AsJSDocAugmentsTag()!.TagName;
            case Kind.JSDocImplementsTag:
                return AsJSDocImplementsTag()!.TagName;
            case Kind.JSDocDeprecatedTag:
                return AsJSDocDeprecatedTag()!.TagName;
            case Kind.JSDocPublicTag:
                return AsJSDocPublicTag()!.TagName;
            case Kind.JSDocPrivateTag:
                return AsJSDocPrivateTag()!.TagName;
            case Kind.JSDocProtectedTag:
                return AsJSDocProtectedTag()!.TagName;
            case Kind.JSDocReadonlyTag:
                return AsJSDocReadonlyTag()!.TagName;
            case Kind.JSDocOverrideTag:
                return AsJSDocOverrideTag()!.TagName;
            case Kind.JSDocCallbackTag:
                return AsJSDocCallbackTag()!.TagName;
            case Kind.JSDocOverloadTag:
                return AsJSDocOverloadTag()!.TagName;
            case Kind.JSDocParameterTag:
            case Kind.JSDocPropertyTag:
                return AsJSDocParameterOrPropertyTag()!.TagName;
            case Kind.JSDocReturnTag:
                return AsJSDocReturnTag()!.TagName;
            case Kind.JSDocThisTag:
                return AsJSDocThisTag()!.TagName;
            case Kind.JSDocTypeTag:
                return AsJSDocTypeTag()!.TagName;
            case Kind.JSDocTemplateTag:
                return AsJSDocTemplateTag()!.TagName;
            case Kind.JSDocTypedefTag:
                return AsJSDocTypedefTag()!.TagName;
            case Kind.JSDocSeeTag:
                return AsJSDocSeeTag()!.TagName;
            case Kind.JSDocSatisfiesTag:
                return AsJSDocSatisfiesTag()!.TagName;
            case Kind.JSDocThrowsTag:
                return AsJSDocThrowsTag()!.TagName;
            case Kind.JSDocImportTag:
                return AsJSDocImportTag()!.TagName;
            default:
                throw new DebugFailureException("Unhandled case in Node.TagName: " + Kind.String());
        }
    }

    /// <summary>Go: <c>func (n *Node) PropertyName() *Node</c>。</summary>
    public Node? PropertyName()
    {
        switch (Kind)
        {
            case Kind.ImportSpecifier:
                return AsImportSpecifier()!.PropertyName;
            case Kind.ExportSpecifier:
                return AsExportSpecifier()!.PropertyName;
            case Kind.BindingElement:
                return AsBindingElement()!.PropertyName;
            default:
                return null;
        }
    }

    public Node? PropertyNameOrName() => PropertyName() ?? Name();

    /// <summary>Go: <c>func (n *Node) IsTypeOnly() bool</c>。</summary>
    public bool IsTypeOnly()
    {
        switch (Kind)
        {
            case Kind.ImportEqualsDeclaration:
                return AsImportEqualsDeclaration()!.IsTypeOnly;
            case Kind.ImportSpecifier:
                return AsImportSpecifier()!.IsTypeOnly;
            case Kind.ImportClause:
                return AsImportClause()!.PhaseModifier == Kind.TypeKeyword;
            case Kind.ExportDeclaration:
                return AsExportDeclaration()!.IsTypeOnly;
            case Kind.ExportSpecifier:
                return AsExportSpecifier()!.IsTypeOnly;
            default:
                return false;
        }
    }

    /// <summary>Go: <c>func (n *Node) CommentList() *NodeList</c>（Go 注释：改这里也要改 hasComment）。</summary>
    public NodeList? CommentList()
    {
        switch (Kind)
        {
            case Kind.JSDoc:
                return AsJSDoc()!.Comment;
            case Kind.JSDocUnknownTag:
                return AsJSDocUnknownTag()!.Comment;
            case Kind.JSDocAugmentsTag:
                return AsJSDocAugmentsTag()!.Comment;
            case Kind.JSDocImplementsTag:
                return AsJSDocImplementsTag()!.Comment;
            case Kind.JSDocDeprecatedTag:
                return AsJSDocDeprecatedTag()!.Comment;
            case Kind.JSDocPublicTag:
                return AsJSDocPublicTag()!.Comment;
            case Kind.JSDocPrivateTag:
                return AsJSDocPrivateTag()!.Comment;
            case Kind.JSDocProtectedTag:
                return AsJSDocProtectedTag()!.Comment;
            case Kind.JSDocReadonlyTag:
                return AsJSDocReadonlyTag()!.Comment;
            case Kind.JSDocOverrideTag:
                return AsJSDocOverrideTag()!.Comment;
            case Kind.JSDocCallbackTag:
                return AsJSDocCallbackTag()!.Comment;
            case Kind.JSDocOverloadTag:
                return AsJSDocOverloadTag()!.Comment;
            case Kind.JSDocParameterTag:
            case Kind.JSDocPropertyTag:
                return AsJSDocParameterOrPropertyTag()!.Comment;
            case Kind.JSDocReturnTag:
                return AsJSDocReturnTag()!.Comment;
            case Kind.JSDocThisTag:
                return AsJSDocThisTag()!.Comment;
            case Kind.JSDocTypeTag:
                return AsJSDocTypeTag()!.Comment;
            case Kind.JSDocTemplateTag:
                return AsJSDocTemplateTag()!.Comment;
            case Kind.JSDocTypedefTag:
                return AsJSDocTypedefTag()!.Comment;
            case Kind.JSDocSeeTag:
                return AsJSDocSeeTag()!.Comment;
            case Kind.JSDocSatisfiesTag:
                return AsJSDocSatisfiesTag()!.Comment;
            case Kind.JSDocThrowsTag:
                return AsJSDocThrowsTag()!.Comment;
            case Kind.JSDocImportTag:
                return AsJSDocImportTag()!.Comment;
            default:
                throw new DebugFailureException("Unhandled case in Node.CommentList: " + Kind.String());
        }
    }

    public Node[]? Comments() => CommentList()?.Nodes;

    /// <summary>Go: <c>func (n *Node) Label() *Node</c>。</summary>
    public Node? Label()
    {
        switch (Kind)
        {
            case Kind.LabeledStatement:
                return AsLabeledStatement()!.Label;
            case Kind.BreakStatement:
                return AsBreakStatement()!.Label;
            case Kind.ContinueStatement:
                return AsContinueStatement()!.Label;
            default:
                throw new DebugFailureException("Unhandled case in Node.Label: " + Kind.String());
        }
    }

    /// <summary>Go: <c>func (n *Node) Attributes() *Node</c>。</summary>
    public Node? Attributes()
    {
        switch (Kind)
        {
            case Kind.JsxOpeningElement:
                return AsJsxOpeningElement()!.Attributes;
            case Kind.JsxSelfClosingElement:
                return AsJsxSelfClosingElement()!.Attributes;
            case Kind.ModuleDeclaration:
                return AsModuleDeclaration()!.Attributes;
            default:
                throw new DebugFailureException("Unhandled case in Node.Attributes: " + Kind.String());
        }
    }

    /// <summary>Go: <c>func (n *Node) Children() *NodeList</c>。</summary>
    public NodeList? Children()
    {
        switch (Kind)
        {
            case Kind.JsxElement:
                return AsJsxElement()!.Children;
            case Kind.JsxFragment:
                return AsJsxFragment()!.Children;
            default:
                throw new DebugFailureException("Unhandled case in Node.Children: " + Kind.String());
        }
    }

    /// <summary>Go: <c>func (n *Node) ModuleSpecifier() *Expression</c>。</summary>
    public Node? ModuleSpecifier()
    {
        switch (Kind)
        {
            case Kind.ImportDeclaration:
            case Kind.JSImportDeclaration:
                return AsImportDeclaration()!.ModuleSpecifier;
            case Kind.ExportDeclaration:
                return AsExportDeclaration()!.ModuleSpecifier;
            case Kind.JSDocImportTag:
                return AsJSDocImportTag()!.ModuleSpecifier;
            default:
                throw new DebugFailureException("Unhandled case in Node.ModuleSpecifier: " + Kind.String());
        }
    }

    /// <summary>Go: <c>func (n *Node) ImportClause() *Node</c>。</summary>
    public Node? ImportClause()
    {
        switch (Kind)
        {
            case Kind.ImportDeclaration:
            case Kind.JSImportDeclaration:
                return AsImportDeclaration()!.ImportClause;
            case Kind.JSDocImportTag:
                return AsJSDocImportTag()!.ImportClause;
            default:
                throw new DebugFailureException("Unhandled case in Node.ImportClause: " + Kind.String());
        }
    }

    /// <summary>Go: <c>func (n *Node) Statement() *Statement</c>。</summary>
    public Node? Statement()
    {
        switch (Kind)
        {
            case Kind.DoStatement:
                return AsDoStatement()!.Statement;
            case Kind.WhileStatement:
                return AsWhileStatement()!.Statement;
            case Kind.ForStatement:
                return AsForStatement()!.Statement;
            case Kind.ForInStatement:
            case Kind.ForOfStatement:
                return AsForInOrOfStatement()!.Statement;
            case Kind.WithStatement:
                return AsWithStatement()!.Statement;
            case Kind.LabeledStatement:
                return AsLabeledStatement()!.Statement;
            default:
                throw new DebugFailureException("Unhandled case in Node.Statement: " + Kind.String());
        }
    }

    /// <summary>Go: <c>func (n *Node) PropertyList() *NodeList</c>。</summary>
    public NodeList? PropertyList()
    {
        switch (Kind)
        {
            case Kind.ObjectLiteralExpression:
                return AsObjectLiteralExpression()!.Properties;
            case Kind.JsxAttributes:
                return AsJsxAttributes()!.Properties;
            default:
                throw new DebugFailureException("Unhandled case in Node.PropertyList: " + Kind.String());
        }
    }

    public Node[]? Properties() => PropertyList()?.Nodes;

    /// <summary>Go: <c>func (n *Node) ElementList() *NodeList</c>。</summary>
    public NodeList? ElementList()
    {
        switch (Kind)
        {
            case Kind.NamedImports:
                return AsNamedImports()!.Elements;
            case Kind.NamedExports:
                return AsNamedExports()!.Elements;
            case Kind.ObjectBindingPattern:
            case Kind.ArrayBindingPattern:
                return AsBindingPattern()!.Elements;
            case Kind.ArrayLiteralExpression:
                return AsArrayLiteralExpression()!.Elements;
            case Kind.TupleType:
                return AsTupleTypeNode()!.Elements;
            default:
                throw new DebugFailureException("Unhandled case in Node.ElementList: " + Kind.String());
        }
    }

    public Node[]? Elements() => ElementList()?.Nodes;

    /// <summary>Go: <c>func (n *Node) PostfixToken() *Node</c>。</summary>
    public Node? PostfixToken()
    {
        switch (Kind)
        {
            case Kind.MethodDeclaration:
                return AsMethodDeclaration()!.PostfixToken;
            case Kind.ShorthandPropertyAssignment:
                return AsShorthandPropertyAssignment()!.PostfixToken;
            case Kind.MethodSignature:
                return AsMethodSignatureDeclaration()!.PostfixToken;
            case Kind.PropertySignature:
                return AsPropertySignatureDeclaration()!.PostfixToken;
            case Kind.PropertyAssignment:
                return AsPropertyAssignment()!.PostfixToken;
            case Kind.PropertyDeclaration:
                return AsPropertyDeclaration()!.PostfixToken;
            case Kind.EnumMember:
                return AsEnumMember()!.PostfixToken;
            case Kind.GetAccessor:
                return AsGetAccessorDeclaration()!.PostfixToken;
            case Kind.SetAccessor:
                return AsSetAccessorDeclaration()!.PostfixToken;
            default:
                return null;
        }
    }

    /// <summary>Go: <c>func (n *Node) QuestionToken() *TokenNode</c>。</summary>
    public Node? QuestionToken()
    {
        switch (Kind)
        {
            case Kind.Parameter:
                return AsParameterDeclaration()!.QuestionToken;
            case Kind.ConditionalExpression:
                return AsConditionalExpression()!.QuestionToken;
            case Kind.MappedType:
                return AsMappedTypeNode()!.QuestionToken;
            case Kind.NamedTupleMember:
                return AsNamedTupleMember()!.QuestionToken;
            default:
                var postfix = PostfixToken();
                if (postfix is not null && postfix.Kind == Kind.QuestionToken)
                {
                    return postfix;
                }

                return null;
        }
    }

    /// <summary>Go: <c>func (n *Node) QuestionDotToken() *Node</c>。</summary>
    public Node? QuestionDotToken()
    {
        switch (Kind)
        {
            case Kind.ElementAccessExpression:
                return AsElementAccessExpression()!.QuestionDotToken;
            case Kind.PropertyAccessExpression:
                return AsPropertyAccessExpression()!.QuestionDotToken;
            case Kind.CallExpression:
                return AsCallExpression()!.QuestionDotToken;
            case Kind.TaggedTemplateExpression:
                return AsTaggedTemplateExpression()!.QuestionDotToken;
            default:
                throw new DebugFailureException("Unhandled case in Node.QuestionDotToken: " + Kind.String());
        }
    }

    /// <summary>Go: <c>func (n *Node) TypeExpression() *Node</c>。</summary>
    public Node? TypeExpression()
    {
        switch (Kind)
        {
            case Kind.JSDocParameterTag:
            case Kind.JSDocPropertyTag:
                return AsJSDocParameterOrPropertyTag()!.TypeExpression;
            case Kind.JSDocReturnTag:
                return AsJSDocReturnTag()!.TypeExpression;
            case Kind.JSDocTypeTag:
                return AsJSDocTypeTag()!.TypeExpression;
            case Kind.JSDocTypedefTag:
                return AsJSDocTypedefTag()!.TypeExpression;
            case Kind.JSDocCallbackTag:
                return AsJSDocCallbackTag()!.TypeExpression;
            case Kind.JSDocSatisfiesTag:
                return AsJSDocSatisfiesTag()!.TypeExpression;
            case Kind.JSDocThrowsTag:
                return AsJSDocThrowsTag()!.TypeExpression;
            default:
                throw new DebugFailureException("Unhandled case in Node.TypeExpression: " + Kind.String());
        }
    }

    /// <summary>Go: <c>func (n *Node) ClassName() *Node</c>。</summary>
    public Node? ClassName()
    {
        switch (Kind)
        {
            case Kind.JSDocAugmentsTag:
                return AsJSDocAugmentsTag()!.ClassName;
            case Kind.JSDocImplementsTag:
                return AsJSDocImplementsTag()!.ClassName;
            default:
                throw new DebugFailureException("Unhandled case in Node.ClassName: " + Kind.String());
        }
    }

    /// <summary>
    /// Go: <c>func (n *Node) Contains(descendant *Node) bool</c>：沿 Parent 指针上溯判断包含关系；
    /// 未挂到树上的节点（除 SourceFile 外）会 panic（Go 原文如此）。
    /// </summary>
    public bool Contains(Node? descendant)
    {
        while (descendant is not null)
        {
            if (ReferenceEquals(descendant, this))
            {
                return true;
            }

            var parent = descendant.Parent;
            if (parent is null && !Ast.IsSourceFile(descendant))
            {
                throw new DebugFailureException("descendant is not parented");
            }

            descendant = parent;
        }

        return false;
    }

    public bool IsJSDoc() => Kind == Kind.JSDoc;

    /// <summary>
    /// Go: <c>func (node *Node) JSDoc(file *SourceFile) []*Node</c>（ast.go:1563-1577）。file 为 nil 时沿 Parent 上溯
    /// 找所属文件；节点没有 JSDoc 标记时返回 nil（与 Go 的 nil 切片一致）。
    /// </summary>
    public Node[]? JSDoc(SourceFile? file)
    {
        if ((Flags & NodeFlags.HasJSDoc) == 0)
        {
            return null;
        }

        file ??= Ast.GetSourceFileOfNode(this);
        if (file is null)
        {
            return null;
        }

        return file._hasLazyJSDoc ? file.ResolveJSDoc(this) : file.GetCachedJSDoc(this);
    }

    /// <summary>
    /// Go: <c>func (node *Node) EagerJSDoc(file *SourceFile) []*Node</c>（ast.go:1579-1590）：只返回已经解析并缓存的
    /// JSDoc 节点，不触发懒解析。
    /// </summary>
    public Node[]? EagerJSDoc(SourceFile? file)
    {
        if ((Flags & NodeFlags.HasJSDoc) == 0)
        {
            return null;
        }

        file ??= Ast.GetSourceFileOfNode(this);
        if (file is null)
        {
            return null;
        }

        return file._hasLazyJSDoc ? file.GetCachedJSDocUnderLock(this) : file.GetCachedJSDoc(this);
    }

    // ── Node casts（Go 的 type assertion：类型不符时 panic）──

    public FlowSwitchClauseData AsFlowSwitchClauseData() =>
        this as FlowSwitchClauseData ?? throw new DebugFailureException("node is not a FlowSwitchClauseData");

    public FlowReduceLabelData AsFlowReduceLabelData() =>
        this as FlowReduceLabelData ?? throw new DebugFailureException("node is not a FlowReduceLabelData");

    /// <summary>Go 的 <c>strings.Join(text, "")</c>：nil slice join 出空串，而 <c>string.Concat</c> 不接受 null。</summary>
    private static string JoinText(string[]? parts) => parts is null ? "" : string.Concat(parts);
}
