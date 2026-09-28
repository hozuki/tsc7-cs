// ported from: internal/ast/utilities.go（4631 行，ast 包最大的手写文件）
//
// 本文件是 utilities.go 的完整移植：4631 行里的每一个顶层函数/类型/常量组都有对应物，顺序与 Go 一致
// （每个成员上方的 `/// <summary>Go: <c>func …</c>。</summary>` 给出 Go 原文的签名，可与 Go 源并排阅读）。
// 其中 4 组函数是本 milestone 更早一步先行移植的，合并时已按 Go 行号插回对应位置：
// GetNodeId/GetSymbolId（Go:22-45）、IsThisIdentifier（Go:1367-1370）、
// GetImpliedNodeFormatForEmitWorker（Go:2622-2638）、IsJsxOpeningLikeElement（Go:3760-3763）。
//
// Go 里 utilities.go 的函数分散在别的 C# 文件（本文件**不**重复声明，见各文件的文件头）：
//   * ModifierToFlag / ModifiersToFlags / HasSyntacticModifier / HasAccessorModifier / HasStaticModifier
//     → ModifierFlags.cs（Go utilities.go:990-1046）
//   * IsAmbientModuleSymbolName → Symbol.cs（Go utilities.go:1662）
//   * TryGetAmbientModuleNameFromSymbolName 在本文件（Symbol.cs 的 IsAmbientModuleSymbolName 只调用它）
//
// 与 Go 的差异（C# 语言特性；逐条依据 docs/architecture.md §2/§3 与 docs/ast.md）：
//   * 包级函数 → 与 package 同名的静态类 `Ast`（partial）。Go 的导出函数名原样保留；未导出函数按 C# 习惯
//     PascalCase（`isPartOfTypeNodeInParent` → `IsPartOfTypeNodeInParent`），可见性 `internal static`。
//   * Go 的类型别名（`Statement`/`Expression`/`TypeNode`/`HeritageClauseElement`/`AccessorDeclaration`…）
//     在 Go 里都是 `= Node`（ast_generated.go 的 type 别名块），C# 没有别名，一律写 `Node`。
//     同理 `StatementList`/`ArgumentList`… 都是 `= NodeList`。
//   * `(T, bool)` 形式的 Go 函数 → `bool TryX(…, out T result)`（docs/architecture.md §3.2）；
//     其余多返回值用具名元组（如 findImportOrRequire 的 `(int Index, int Size)`）。
//   * 只读切片参数 → `ReadOnlySpan<T>`（§3.8），Go 的变参 → `params T[]`，返回的切片 → `T[]`
//     （Go 的 nil 切片用空数组表达）。span 不能进字段，长期持有的切片仍是数组。
//     例外：`NodeKindIs` 的变参用 `params ReadOnlySpan<Kind>` —— Go 的 `slices.Contains([]ast.Kind{…}, x)`
//     会被内联成栈上数组（零堆分配），而 C# 的 `params Kind[]` 每次 new 一个数组（实测 parse 相位
//     `Ast.Kind[]` ≈ 7.8 MB／776 MB，−1.0%）；`params ReadOnlySpan<T>` 由编译器在调用点 stackalloc，等价于 Go。
//   * Go 的 nil 接收者/参数分支：接收者分支删掉（C# 实例方法在 null 上不可分派），参数分支保留。
//   * Go 的位掩码类型（OuterExpressionKinds/SemanticMeaning）→ `[Flags] enum`，数值与 Go 一致，成员名去
//     类型前缀（`OEKParentheses` → `Parentheses`）；非位掩码枚举（JSDeclarationKind/ModuleInstanceState/
//     FindAncestorResult）同样去前缀。
//   * `panic`/`debug.Fail` → `Debug.Fail` / `throw new DebugFailureException`；
//     `sync.Pool` → `[ThreadStatic]` 槽位（Core.cs 的 levenshtein 池同款）。

using System.Threading;
using TypeScript.Core;
using static TypeScript.Core.Core;
using static TypeScript.StringUtil.StringUtil;
using System.Text;
using static TypeScript.TSPath.TSPath;
using System.Runtime.InteropServices;

namespace TypeScript.Ast;

[Flags]
public enum OuterExpressionKinds : ushort
{
    Parentheses = 1 << 0,
    TypeAssertions = 1 << 1,
    NonNullAssertions = 1 << 2,
    PartiallyEmittedExpressions = 1 << 3,
    ExpressionsWithTypeArguments = 1 << 4,
    Satisfies = 1 << 5,
    ExcludeJSDocTypeAssertion = 1 << 6,
    Assignments = 1 << 7,
    Comma = 1 << 8,
    Assertions = TypeAssertions | NonNullAssertions | Satisfies,
    All = Parentheses | Assertions | PartiallyEmittedExpressions | ExpressionsWithTypeArguments,
    AllExceptAssertionsOrExpressionsWithTypeArguments = All & ~Assertions & ~ExpressionsWithTypeArguments,
    ExpressionTypePassthrough = Parentheses | Assignments | Comma,
}

public enum FindAncestorResult : int
{
    False = 0,
    True = 1,
    Quit = 2,
}

public enum JSDeclarationKind : int
{
    None = 0,
    // module.exports = expr, except for module.exports = exports
    ModuleExports = 1,
    // exports.name = expr
    // module.exports.name = expr
    ExportsProperty = 2,
    // this.name = expr
    ThisProperty = 3,
    // F.name = expr, F[name] = expr, in JS or TS file
    Property = 4,
    // Object.defineProperty(x, 'name', { value: any, writable?: boolean (false by default) });
    // Object.defineProperty(x, 'name', { get: Function, set: Function });
    // Object.defineProperty(x, 'name', { get: Function });
    // Object.defineProperty(x, 'name', { set: Function });
    ObjectDefinePropertyValue = 5,
    // Object.defineProperty(exports || module.exports, 'name', ...);
    ObjectDefinePropertyExports = 6,
}

[Flags]
public enum SemanticMeaning : int
{
    /// <summary>Go: <c>SemanticMeaningNone</c>。</summary>
    None = 0,
    /// <summary>Go: <c>SemanticMeaningValue</c>。</summary>
    Value = 1 << 0,
    /// <summary>Go: <c>SemanticMeaningType</c>。</summary>
    Type = 1 << 1,
    /// <summary>Go: <c>SemanticMeaningNamespace</c>。</summary>
    Namespace = 1 << 2,
    /// <summary>Go: <c>SemanticMeaningValue | SemanticMeaningType | SemanticMeaningNamespace</c>。</summary>
    All = Value | Type | Namespace,
}

public enum ModuleInstanceState : int
{
    /// <summary>Go: <c>ModuleInstanceStateUnknown</c>。</summary>
    Unknown = 0,
    /// <summary>Go: <c>ModuleInstanceStateNonInstantiated</c>。</summary>
    NonInstantiated = 1,
    /// <summary>Go: <c>ModuleInstanceStateInstantiated</c>。</summary>
    Instantiated = 2,
    /// <summary>Go: <c>ModuleInstanceStateConstEnumOnly</c>。</summary>
    ConstEnumOnly = 3,
}

public sealed class AllAccessorDeclarations
{
    public Node? FirstAccessor;
    public Node? SecondAccessor;
    public Node? SetAccessor;
    public Node? GetAccessor;
}

public static partial class Ast
{
// Go: var nextNodeId / nextSymbolId atomic.Uint64
    private static ulong _nextNodeId;
    private static ulong _nextSymbolId;

    /// <summary>Go: <c>func GetNodeId(node *Node) NodeId</c>（懒分配：0 表示未分配）。</summary>
    public static NodeId GetNodeId(Node node)
    {
        var id = Volatile.Read(ref node._id);
        if (id == 0)
        {
            // Worst case, we burn a few ids if we have to CAS.
            id = Interlocked.Increment(ref _nextNodeId);
            if (Interlocked.CompareExchange(ref node._id, id, 0) != 0)
            {
                id = Volatile.Read(ref node._id);
            }
        }

        return new NodeId(id);
    }

    /// <summary>Go: <c>func GetSymbolId(symbol *Symbol) SymbolId</c>。</summary>
    public static SymbolId GetSymbolId(Symbol symbol)
    {
        var id = Volatile.Read(ref symbol._id);
        if (id == 0)
        {
            id = Interlocked.Increment(ref _nextSymbolId);
            if (Interlocked.CompareExchange(ref symbol._id, id, 0) != 0)
            {
                id = Volatile.Read(ref symbol._id);
            }
        }

        return new SymbolId(id);
    }



    /// <summary>Go: <c>func GetSymbolTable(data *SymbolTable) SymbolTable</c>。</summary>
    public static SymbolTable GetSymbolTable(ref SymbolTable? data)
    {
        if (data is null)
        {
            data = new SymbolTable();
        }

        return data;
    }

    /// <summary>Go: <c>func GetMembers(symbol *Symbol) SymbolTable</c>。</summary>
    public static SymbolTable GetMembers(Symbol? symbol)
    {
        var s = symbol!;
        return GetSymbolTable(ref s.Members);
    }

    /// <summary>Go: <c>func GetExports(symbol *Symbol) SymbolTable</c>。</summary>
    public static SymbolTable GetExports(Symbol? symbol)
    {
        var s = symbol!;
        return GetSymbolTable(ref s.Exports);
    }

    /// <summary>Go: <c>func GetLocals(container *Node) SymbolTable</c>。</summary>
    public static SymbolTable GetLocals(Node? container)
    {
        var data = container!.LocalsContainerData()!;
        var locals = data.Locals;
        if (locals is null)
        {
            locals = new SymbolTable();
            data.Locals = locals;
        }

        return locals;
    }

    // Determines if a node is missing (either `nil` or empty)
    /// <summary>Go: <c>func NodeIsMissing(node *Node) bool</c>。</summary>
    public static bool NodeIsMissing(Node? node)
    {
        return node is null ||
            (node.Loc.Pos() == node.Loc.End() && node.Loc.Pos() >= 0 && node.Kind != Kind.EndOfFile);
    }

    // Determines if a node is present
    /// <summary>Go: <c>func NodeIsPresent(node *Node) bool</c>。</summary>
    public static bool NodeIsPresent(Node? node) => !NodeIsMissing(node);

    // Determines if a node contains synthetic positions
    /// <summary>Go: <c>func NodeIsSynthesized(node *Node) bool</c>。</summary>
    public static bool NodeIsSynthesized(Node? node)
    {
        var n = node!;
        return PositionIsSynthesized(n.Loc.Pos()) || PositionIsSynthesized(n.Loc.End());
    }

    /// <summary>Go: <c>func RangeIsSynthesized(loc core.TextRange) bool</c>。</summary>
    public static bool RangeIsSynthesized(TextRange loc) =>
        PositionIsSynthesized(loc.Pos()) || PositionIsSynthesized(loc.End());

    // Determines whether a position is synthetic
    /// <summary>Go: <c>func PositionIsSynthesized(pos int) bool</c>。</summary>
    public static bool PositionIsSynthesized(int pos) => pos < 0;

    /// <summary>Go: <c>func FindLastVisibleNode(nodes []*Node) *Node</c>。</summary>
    public static Node? FindLastVisibleNode(ReadOnlySpan<Node> nodes)
    {
        var fromEnd = 1;
        while (fromEnd <= nodes.Length && (nodes[nodes.Length - fromEnd].Flags & NodeFlags.Reparsed) != 0)
        {
            fromEnd++;
        }

        if (fromEnd <= nodes.Length)
        {
            return nodes[nodes.Length - fromEnd];
        }

        return null;
    }

    /// <summary>Go: <c>func NodeKindIs(node *Node, kinds ...Kind) bool</c>。</summary>
    // Go 的 `kinds ...Kind` 在调用点构造切片字面量；Go 编译器对 `slices.Contains([]T{…}, x)` 这种形态做栈上分配
    // （NodeKindIs 足够小、会被内联），所以 Go 侧**零堆分配**。C# 的 `params Kind[]` 每次都 new 一个数组
    // （parse 相位实测 `Ast.Kind[]` ≈ 7.8 MB / 30 reps，全是这个函数产生的）→ 改用 C# 13 的 `params ReadOnlySpan<Kind>`，
    // 编译器在调用点用 stackalloc 构造（同样零堆分配），语义与 Go 一致。
    public static bool NodeKindIs(Node? node, params ReadOnlySpan<Kind> kinds) => kinds.IndexOf(node!.Kind) >= 0;

    /// <summary>Go: <c>func IsModifier(node *Node) bool</c>。</summary>
    public static bool IsModifier(Node? node) => IsModifierKind(node!.Kind);

    /// <summary>Go: <c>func IsModifierLike(node *Node) bool</c>。</summary>
    public static bool IsModifierLike(Node? node) => IsModifier(node) || IsDecorator(node!);

    /// <summary>Go: <c>func IsCompoundAssignment(token Kind) bool</c>。</summary>
    public static bool IsCompoundAssignment(Kind token) =>
        token >= Kind.FirstCompoundAssignment && token <= Kind.LastCompoundAssignment;

    /// <summary>Go: <c>func IsAssignmentExpression(node *Node, excludeCompoundAssignment bool) bool</c>。</summary>
    public static bool IsAssignmentExpression(Node? node, bool excludeCompoundAssignment)
    {
        var n = node!;
        if (n.Kind == Kind.BinaryExpression)
        {
            var expr = n.AsBinaryExpression()!;
            return (expr.OperatorToken!.Kind == Kind.EqualsToken ||
                    (!excludeCompoundAssignment && IsAssignmentOperator(expr.OperatorToken!.Kind))) &&
                IsLeftHandSideExpression(expr.Left);
        }

        return false;
    }

    /// <summary>Go: <c>func GetRightMostAssignedExpression(node *Node) *Node</c>。</summary>
    public static Node GetRightMostAssignedExpression(Node? node)
    {
        var n = node!;
        while (IsAssignmentExpression(n, false /*excludeCompoundAssignment*/))
        {
            n = n.AsBinaryExpression()!.Right!;
        }

        return n;
    }

    /// <summary>Go: <c>func IsDestructuringAssignment(node *Node) bool</c>。</summary>
    public static bool IsDestructuringAssignment(Node? node)
    {
        var n = node!;
        if (IsAssignmentExpression(n, true /*excludeCompoundAssignment*/))
        {
            var kind = n.AsBinaryExpression()!.Left!.Kind;
            return kind == Kind.ObjectLiteralExpression || kind == Kind.ArrayLiteralExpression;
        }

        return false;
    }

    /// <summary>Go: <c>func IsObjectBindingOrAssignmentElement(node *Node) bool</c>。</summary>
    public static bool IsObjectBindingOrAssignmentElement(Node? node) => node!.Kind is
        Kind.BindingElement or
        Kind.PropertyAssignment or
        Kind.ShorthandPropertyAssignment or
        Kind.SpreadAssignment;

    /// <summary>Go: <c>func IsArrayBindingOrAssignmentElement(node *Node) bool</c>。</summary>
    public static bool IsArrayBindingOrAssignmentElement(Node? node)
    {
        var n = node!;
        if (n.Kind is
            Kind.BindingElement or
            Kind.OmittedExpression or
            Kind.SpreadElement or
            Kind.ArrayLiteralExpression or
            Kind.ObjectLiteralExpression or
            Kind.Identifier or
            Kind.PropertyAccessExpression or
            Kind.ElementAccessExpression)
        {
            return true;
        }

        return IsAssignmentExpression(n, true /*excludeCompoundAssignment*/);
    }

    /// <summary>Go: <c>func IsBindingPattern(node *Node) bool</c>。</summary>
    public static bool IsBindingPattern(Node? node) =>
        node!.Kind is Kind.ObjectBindingPattern or Kind.ArrayBindingPattern;

    /// <summary>Go: <c>func IsForInOrOfStatement(node *Node) bool</c>。</summary>
    public static bool IsForInOrOfStatement(Node? node) =>
        node is not null && node.Kind is Kind.ForInStatement or Kind.ForOfStatement;

    // A node is an assignment target if it is on the left hand side of an '=' token, if it is parented by a property
    // assignment in an object literal that is an assignment target, or if it is parented by an array literal that is
    // an assignment target. Examples include 'a = xxx', '{ p: a } = xxx', '[{ a }] = xxx'.
    // (Note that `p` is not a target in the above examples, only `a`.)
    /// <summary>Go: <c>func IsAssignmentTarget(node *Node) bool</c>。</summary>
    public static bool IsAssignmentTarget(Node? node) => GetAssignmentTarget(node) is not null;

    // Returns the BinaryExpression, PrefixUnaryExpression, PostfixUnaryExpression, or ForInOrOfStatement that references
    // the given node as an assignment target
    /// <summary>Go: <c>func GetAssignmentTarget(node *Node) *Node</c>。</summary>
    public static Node? GetAssignmentTarget(Node? node)
    {
        var n = node!;
        while (true)
        {
            var parent = n.Parent!;
            switch (parent.Kind)
            {
                case Kind.BinaryExpression:
                    if (IsAssignmentOperator(parent.AsBinaryExpression()!.OperatorToken!.Kind) &&
                        ReferenceEquals(parent.AsBinaryExpression()!.Left, n))
                    {
                        return parent;
                    }

                    return null;
                case Kind.PrefixUnaryExpression:
                    if (parent.AsPrefixUnaryExpression()!.Operator is Kind.PlusPlusToken or Kind.MinusMinusToken)
                    {
                        return parent;
                    }

                    return null;
                case Kind.PostfixUnaryExpression:
                    if (parent.AsPostfixUnaryExpression()!.Operator is Kind.PlusPlusToken or Kind.MinusMinusToken)
                    {
                        return parent;
                    }

                    return null;
                case Kind.ForInStatement:
                case Kind.ForOfStatement:
                    if (ReferenceEquals(parent.Initializer(), n))
                    {
                        return parent;
                    }

                    return null;
                case Kind.ParenthesizedExpression:
                case Kind.ArrayLiteralExpression:
                case Kind.SpreadElement:
                case Kind.NonNullExpression:
                    n = parent;
                    break;
                case Kind.SpreadAssignment:
                    n = parent.Parent!;
                    break;
                case Kind.ShorthandPropertyAssignment:
                    if (!ReferenceEquals(parent.AsShorthandPropertyAssignment()!.Name(), n))
                    {
                        return null;
                    }

                    n = parent.Parent!;
                    break;
                case Kind.PropertyAssignment:
                    if (ReferenceEquals(parent.AsPropertyAssignment()!.Name(), n))
                    {
                        return null;
                    }

                    n = parent.Parent!;
                    break;
                default:
                    return null;
            }
        }
    }

    /// <summary>Go: <c>func IsLogicalBinaryOperator(token Kind) bool</c>。</summary>
    public static bool IsLogicalBinaryOperator(Kind token) =>
        token is Kind.BarBarToken or Kind.AmpersandAmpersandToken;

    /// <summary>Go: <c>func IsLogicalOrCoalescingBinaryOperator(token Kind) bool</c>。</summary>
    public static bool IsLogicalOrCoalescingBinaryOperator(Kind token) =>
        IsLogicalBinaryOperator(token) || token == Kind.QuestionQuestionToken;

    /// <summary>Go: <c>func IsLogicalOrCoalescingBinaryExpression(expr *Node) bool</c>。</summary>
    public static bool IsLogicalOrCoalescingBinaryExpression(Node? expr)
    {
        var e = expr!;
        return IsBinaryExpression(e) && IsLogicalOrCoalescingBinaryOperator(e.AsBinaryExpression()!.OperatorToken!.Kind);
    }

    /// <summary>Go: <c>func IsLogicalOrCoalescingAssignmentExpression(expr *Node) bool</c>。</summary>
    public static bool IsLogicalOrCoalescingAssignmentExpression(Node? expr)
    {
        var e = expr!;
        return IsBinaryExpression(e) && IsLogicalOrCoalescingAssignmentOperator(e.AsBinaryExpression()!.OperatorToken!.Kind);
    }

    /// <summary>Go: <c>func IsLogicalExpression(node *Node) bool</c>。</summary>
    public static bool IsLogicalExpression(Node? node)
    {
        var n = node!;
        while (true)
        {
            if (n.Kind == Kind.ParenthesizedExpression)
            {
                n = n.Expression()!;
            }
            else if (n.Kind == Kind.PrefixUnaryExpression &&
                n.AsPrefixUnaryExpression()!.Operator == Kind.ExclamationToken)
            {
                n = n.AsPrefixUnaryExpression()!.Operand!;
            }
            else
            {
                return IsLogicalOrCoalescingBinaryExpression(n);
            }
        }
    }

    /// <summary>Go: <c>func IsAccessor(node *Node) bool</c>。</summary>
    public static bool IsAccessor(Node? node) => node!.Kind is Kind.GetAccessor or Kind.SetAccessor;

    /// <summary>Go: <c>func IsPropertyNameLiteral(node *Node) bool</c>。</summary>
    public static bool IsPropertyNameLiteral(Node? node) => node!.Kind is
        Kind.Identifier or
        Kind.StringLiteral or
        Kind.NoSubstitutionTemplateLiteral or
        Kind.NumericLiteral;

    /// <summary>Go: <c>func IsMemberName(node *Node) bool</c>。</summary>
    public static bool IsMemberName(Node? node) => node!.Kind is Kind.Identifier or Kind.PrivateIdentifier;

    /// <summary>Go: <c>func IsEntityName(node *Node) bool</c>。</summary>
    public static bool IsEntityName(Node? node) => node!.Kind is Kind.Identifier or Kind.QualifiedName;

    /// <summary>Go: <c>func IsPropertyName(node *Node) bool</c>。</summary>
    public static bool IsPropertyName(Node? node) => node!.Kind is
        Kind.Identifier or
        Kind.PrivateIdentifier or
        Kind.StringLiteral or
        Kind.NumericLiteral or
        Kind.ComputedPropertyName;

    // Return true if the given identifier is classified as an IdentifierName by inspecting the parent of the node
    /// <summary>Go: <c>func IsIdentifierName(node *Node) bool</c>。</summary>
    public static bool IsIdentifierName(Node? node)
    {
        var n = node!;
        var parent = n.Parent!;
        switch (parent.Kind)
        {
            case Kind.PropertyDeclaration:
            case Kind.PropertySignature:
            case Kind.MethodDeclaration:
            case Kind.MethodSignature:
            case Kind.GetAccessor:
            case Kind.SetAccessor:
            case Kind.EnumMember:
            case Kind.PropertyAssignment:
            case Kind.PropertyAccessExpression:
                return ReferenceEquals(parent.Name(), n);
            case Kind.QualifiedName:
                return ReferenceEquals(parent.AsQualifiedName()!.Right, n);
            case Kind.BindingElement:
                return ReferenceEquals(parent.PropertyName(), n);
            case Kind.ImportSpecifier:
                return ReferenceEquals(parent.PropertyName(), n);
            case Kind.ExportSpecifier:
            case Kind.JsxAttribute:
            case Kind.JsxSelfClosingElement:
            case Kind.JsxOpeningElement:
            case Kind.JsxClosingElement:
                return true;
        }

        return false;
    }

    /// <summary>Go: <c>func IsPushOrUnshiftIdentifier(node *Node) bool</c>。</summary>
    public static bool IsPushOrUnshiftIdentifier(Node? node)
    {
        var text = node!.Text();
        return text == "push" || text == "unshift";
    }

    /// <summary>Go: <c>func IsBooleanLiteral(node *Node) bool</c>。</summary>
    public static bool IsBooleanLiteral(Node? node) => node!.Kind is Kind.TrueKeyword or Kind.FalseKeyword;

    /// <summary>Go: <c>func IsLiteralExpression(node *Node) bool</c>。</summary>
    public static bool IsLiteralExpression(Node? node) => IsLiteralKind(node!.Kind);

    /// <summary>Go: <c>func IsStringLiteralLike(node *Node) bool</c>。</summary>
    public static bool IsStringLiteralLike(Node? node) =>
        node!.Kind is Kind.StringLiteral or Kind.NoSubstitutionTemplateLiteral;

    /// <summary>Go: <c>func IsStringOrNumericLiteralLike(node *Node) bool</c>。</summary>
    public static bool IsStringOrNumericLiteralLike(Node? node) => IsStringLiteralLike(node) || IsNumericLiteral(node!);

    /// <summary>Go: <c>func IsSignedNumericLiteral(node *Node) bool</c>。</summary>
    public static bool IsSignedNumericLiteral(Node? node)
    {
        var n = node!;
        if (n.Kind == Kind.PrefixUnaryExpression)
        {
            // Go 里 `node := node.AsPrefixUnaryExpression()` 遮蔽了参数；C# 用另一个名字。
            var unary = n.AsPrefixUnaryExpression()!;
            return (unary.Operator is Kind.PlusToken or Kind.MinusToken) && IsNumericLiteral(unary.Operand!);
        }

        return false;
    }

    // Determines if a node is part of an OptionalChain
    /// <summary>Go: <c>func IsOptionalChain(node *Node) bool</c>。</summary>
    public static bool IsOptionalChain(Node? node)
    {
        var n = node!;
        if ((n.Flags & NodeFlags.OptionalChain) != 0)
        {
            switch (n.Kind)
            {
                case Kind.PropertyAccessExpression:
                case Kind.ElementAccessExpression:
                case Kind.CallExpression:
                case Kind.NonNullExpression:
                    return true;
            }
        }

        return false;
    }

    /// <summary>Go: <c>func getQuestionDotToken(node *Expression) *TokenNode</c>。</summary>
    internal static Node? GetQuestionDotToken(Node? node) => node!.QuestionDotToken();

    // Determines if node is the root expression of an OptionalChain
    /// <summary>Go: <c>func IsOptionalChainRoot(node *Expression) bool</c>。</summary>
    public static bool IsOptionalChainRoot(Node? node)
    {
        var n = node!;
        return IsOptionalChain(n) && !IsNonNullExpression(n) && GetQuestionDotToken(n) is not null;
    }

    // Determines whether a node is the outermost `OptionalChain` in an ECMAScript `OptionalExpression`:
    //
    //  1. For `a?.b.c`, the outermost chain is `a?.b.c` (`c` is the end of the chain starting at `a?.`)
    //  2. For `a?.b!`, the outermost chain is `a?.b` (`b` is the end of the chain starting at `a?.`)
    //  3. For `(a?.b.c).d`, the outermost chain is `a?.b.c` (`c` is the end of the chain starting at `a?.` since parens end the chain)
    //  4. For `a?.b.c?.d`, both `a?.b.c` and `a?.b.c?.d` are outermost (`c` is the end of the chain starting at `a?.`, and `d` is
    //     the end of the chain starting at `c?.`)
    //  5. For `a?.(b?.c).d`, both `b?.c` and `a?.(b?.c)d` are outermost (`c` is the end of the chain starting at `b`, and `d` is
    //     the end of the chain starting at `a?.`)
    /// <summary>Go: <c>func IsOutermostOptionalChain(node *Expression) bool</c>。</summary>
    public static bool IsOutermostOptionalChain(Node? node)
    {
        var n = node!;
        var parent = n.Parent!;
        return !IsOptionalChain(parent) || // cases 1, 2, and 3
            IsOptionalChainRoot(parent) || // case 4
            !ReferenceEquals(n, parent.Expression()); // case 5
    }

    // Determines whether a node is the expression preceding an optional chain (i.e. `a` in `a?.b`).
    /// <summary>Go: <c>func IsExpressionOfOptionalChainRoot(node *Node) bool</c>。</summary>
    public static bool IsExpressionOfOptionalChainRoot(Node? node)
    {
        var n = node!;
        var parent = n.Parent!;
        return IsOptionalChainRoot(parent) && ReferenceEquals(parent.Expression(), n);
    }

    /// <summary>Go: <c>func IsNullishCoalesce(node *Node) bool</c>。</summary>
    public static bool IsNullishCoalesce(Node? node)
    {
        var n = node!;
        return n.Kind == Kind.BinaryExpression &&
            n.AsBinaryExpression()!.OperatorToken!.Kind == Kind.QuestionQuestionToken;
    }

    /// <summary>Go: <c>func IsAssertionExpression(node *Node) bool</c>。</summary>
    public static bool IsAssertionExpression(Node? node)
    {
        var kind = node!.Kind;
        return kind is Kind.TypeAssertionExpression or Kind.AsExpression;
    }

    /// <summary>Go: <c>func isLeftHandSideExpressionKind(kind Kind) bool</c>。</summary>
    internal static bool IsLeftHandSideExpressionKind(Kind kind) => kind is
        Kind.PropertyAccessExpression or
        Kind.ElementAccessExpression or
        Kind.NewExpression or
        Kind.CallExpression or
        Kind.JsxElement or
        Kind.JsxSelfClosingElement or
        Kind.JsxFragment or
        Kind.TaggedTemplateExpression or
        Kind.ArrayLiteralExpression or
        Kind.ParenthesizedExpression or
        Kind.ObjectLiteralExpression or
        Kind.ClassExpression or
        Kind.FunctionExpression or
        Kind.Identifier or
        Kind.PrivateIdentifier or
        Kind.RegularExpressionLiteral or
        Kind.NumericLiteral or
        Kind.BigIntLiteral or
        Kind.StringLiteral or
        Kind.NoSubstitutionTemplateLiteral or
        Kind.TemplateExpression or
        Kind.FalseKeyword or
        Kind.NullKeyword or
        Kind.ThisKeyword or
        Kind.TrueKeyword or
        Kind.SuperKeyword or
        Kind.NonNullExpression or
        Kind.ExpressionWithTypeArguments or
        Kind.MetaProperty or
        Kind.ImportKeyword or
        Kind.MissingDeclaration;

    // Determines whether a node is a LeftHandSideExpression based only on its kind.
    /// <summary>Go: <c>func IsLeftHandSideExpression(node *Node) bool</c>。</summary>
    public static bool IsLeftHandSideExpression(Node? node)
    {
        var n = SkipPartiallyEmittedExpressions(node!);
        return IsLeftHandSideExpressionKind(n!.Kind);
    }

    /// <summary>Go: <c>func isUnaryExpressionKind(kind Kind) bool</c>。</summary>
    internal static bool IsUnaryExpressionKind(Kind kind)
    {
        if (kind is
            Kind.PrefixUnaryExpression or
            Kind.PostfixUnaryExpression or
            Kind.DeleteExpression or
            Kind.TypeOfExpression or
            Kind.VoidExpression or
            Kind.AwaitExpression or
            Kind.TypeAssertionExpression)
        {
            return true;
        }

        return IsLeftHandSideExpressionKind(kind);
    }

    // Determines whether a node is a UnaryExpression based only on its kind.
    /// <summary>Go: <c>func IsUnaryExpression(node *Node) bool</c>。</summary>
    public static bool IsUnaryExpression(Node? node)
    {
        var n = SkipPartiallyEmittedExpressions(node!);
        return IsUnaryExpressionKind(n!.Kind);
    }

    /// <summary>Go: <c>func isExpressionKind(kind Kind) bool</c>。</summary>
    internal static bool IsExpressionKind(Kind kind)
    {
        if (kind is
            Kind.ConditionalExpression or
            Kind.YieldExpression or
            Kind.ArrowFunction or
            Kind.BinaryExpression or
            Kind.SpreadElement or
            Kind.AsExpression or
            Kind.OmittedExpression or
            Kind.PartiallyEmittedExpression or
            Kind.SatisfiesExpression)
        {
            return true;
        }

        return IsUnaryExpressionKind(kind);
    }

    // Determines whether a node is an expression based only on its kind.
    /// <summary>Go: <c>func IsExpression(node *Node) bool</c>。</summary>
    public static bool IsExpression(Node? node)
    {
        var n = SkipPartiallyEmittedExpressions(node!);
        return IsExpressionKind(n!.Kind);
    }

    /// <summary>Go: <c>func IsCommaExpression(node *Node) bool</c>。</summary>
    public static bool IsCommaExpression(Node? node)
    {
        var n = node!;
        return n.Kind == Kind.BinaryExpression && n.AsBinaryExpression()!.OperatorToken!.Kind == Kind.CommaToken;
    }

    /// <summary>Go: <c>func IsCommaSequence(node *Node) bool</c>。</summary>
    public static bool IsCommaSequence(Node? node) => IsCommaExpression(node);

    /// <summary>Go: <c>func IsIterationStatement(node *Node, lookInLabeledStatements bool) bool</c>。</summary>
    public static bool IsIterationStatement(Node? node, bool lookInLabeledStatements)
    {
        var n = node!;
        switch (n.Kind)
        {
            case Kind.ForStatement:
            case Kind.ForInStatement:
            case Kind.ForOfStatement:
            case Kind.DoStatement:
            case Kind.WhileStatement:
                return true;
            case Kind.LabeledStatement:
                return lookInLabeledStatements && IsIterationStatement(n.Statement(), lookInLabeledStatements);
        }

        return false;
    }

    // Determines if a node is a property or element access expression
    /// <summary>Go: <c>func IsAccessExpression(node *Node) bool</c>。</summary>
    public static bool IsAccessExpression(Node? node) =>
        node!.Kind is Kind.PropertyAccessExpression or Kind.ElementAccessExpression;

    /// <summary>Go: <c>func isFunctionLikeDeclarationKind(kind Kind) bool</c>。</summary>
    internal static bool IsFunctionLikeDeclarationKind(Kind kind) => kind is
        Kind.FunctionDeclaration or
        Kind.MethodDeclaration or
        Kind.Constructor or
        Kind.GetAccessor or
        Kind.SetAccessor or
        Kind.FunctionExpression or
        Kind.ArrowFunction;

    // Determines if a node is function-like (but is not a signature declaration)
    // TODO(rbuckton): Move `node != nil` test to call sites
    /// <summary>Go: <c>func IsFunctionLikeDeclaration(node *Node) bool</c>。</summary>
    public static bool IsFunctionLikeDeclaration(Node? node) =>
        node is not null && IsFunctionLikeDeclarationKind(node.Kind);

    /// <summary>Go: <c>func IsFunctionLikeKind(kind Kind) bool</c>。</summary>
    public static bool IsFunctionLikeKind(Kind kind)
    {
        if (kind is
            Kind.MethodSignature or
            Kind.CallSignature or
            Kind.JSDocSignature or
            Kind.ConstructSignature or
            Kind.IndexSignature or
            Kind.FunctionType or
            Kind.ConstructorType)
        {
            return true;
        }

        return IsFunctionLikeDeclarationKind(kind);
    }

    // Determines if a node is function- or signature-like.
    // TODO(rbuckton): Move `node != nil` test to call sites
    /// <summary>Go: <c>func IsFunctionLike(node *Node) bool</c>。</summary>
    public static bool IsFunctionLike(Node? node) => node is not null && IsFunctionLikeKind(node.Kind);

    /// <summary>Go: <c>func IsFunctionLikeOrClassStaticBlockDeclaration(node *Node) bool</c>。</summary>
    public static bool IsFunctionLikeOrClassStaticBlockDeclaration(Node? node) =>
        node is not null && (IsFunctionLike(node) || IsClassStaticBlockDeclaration(node));

    /// <summary>Go: <c>func IsFunctionOrSourceFile(node *Node) bool</c>。</summary>
    public static bool IsFunctionOrSourceFile(Node? node)
    {
        var n = node!;
        return IsFunctionLike(n) || IsSourceFile(n);
    }

    /// <summary>Go: <c>func IsClassLike(node *Node) bool</c>。</summary>
    public static bool IsClassLike(Node? node) =>
        node is not null && node.Kind is Kind.ClassDeclaration or Kind.ClassExpression;

    /// <summary>Go: <c>func IsClassOrInterfaceLike(node *Node) bool</c>。</summary>
    public static bool IsClassOrInterfaceLike(Node? node) =>
        node is not null && node.Kind is Kind.ClassDeclaration or Kind.ClassExpression or Kind.InterfaceDeclaration;

/// <summary>Go: <c>func IsClassElement(node *Node) bool</c>。</summary>
    public static bool IsClassElement(Node node) => node.Kind is
        Kind.Constructor or
        Kind.PropertyDeclaration or
        Kind.MethodDeclaration or
        Kind.GetAccessor or
        Kind.SetAccessor or
        Kind.IndexSignature or
        Kind.ClassStaticBlockDeclaration or
        Kind.SemicolonClassElement;

    /// <summary>Go: <c>func IsMethodOrAccessor(node *Node) bool</c>。</summary>
    public static bool IsMethodOrAccessor(Node node) =>
        node.Kind is Kind.MethodDeclaration or Kind.GetAccessor or Kind.SetAccessor;

    /// <summary>Go: <c>func IsPrivateIdentifierClassElementDeclaration(node *Node) bool</c>。</summary>
    public static bool IsPrivateIdentifierClassElementDeclaration(Node node) =>
        (IsPropertyDeclaration(node) || IsMethodOrAccessor(node)) && IsPrivateIdentifier(node.Name()!);



    /// <summary>Go: <c>func IsObjectLiteralOrClassExpressionMethodOrAccessor(node *Node) bool</c>。</summary>
    public static bool IsObjectLiteralOrClassExpressionMethodOrAccessor(Node? node)
    {
        var n = node!;
        var kind = n.Kind;
        return (kind == Kind.MethodDeclaration || kind == Kind.GetAccessor || kind == Kind.SetAccessor) &&
            (n.Parent!.Kind == Kind.ObjectLiteralExpression || n.Parent!.Kind == Kind.ClassExpression);
    }

    /// <summary>Go: <c>func IsTypeElement(node *Node) bool</c>。</summary>
    public static bool IsTypeElement(Node? node)
    {
        var n = node!;
        return n.Kind is
            Kind.ConstructSignature or
            Kind.CallSignature or
            Kind.PropertySignature or
            Kind.MethodSignature or
            Kind.IndexSignature or
            Kind.GetAccessor or
            Kind.SetAccessor or
            Kind.NotEmittedTypeElement;
    }

    /// <summary>Go: <c>func IsObjectLiteralElement(node *Node) bool</c>。</summary>
    public static bool IsObjectLiteralElement(Node? node)
    {
        var n = node!;
        return n.Kind is
            Kind.PropertyAssignment or
            Kind.ShorthandPropertyAssignment or
            Kind.SpreadAssignment or
            Kind.MethodDeclaration or
            Kind.GetAccessor or
            Kind.SetAccessor;
    }

    /// <summary>Go: <c>func IsObjectLiteralMethod(node *Node) bool</c>。</summary>
    public static bool IsObjectLiteralMethod(Node? node) =>
        node is { } n && n.Kind == Kind.MethodDeclaration && n.Parent!.Kind == Kind.ObjectLiteralExpression;

    /// <summary>Go: <c>func IsAutoAccessorPropertyDeclaration(node *Node) bool</c>。</summary>
    public static bool IsAutoAccessorPropertyDeclaration(Node? node)
    {
        var n = node!;
        return IsPropertyDeclaration(n) && HasAccessorModifier(n);
    }

    /// <summary>Go: <c>func IsParameterPropertyDeclaration(node *Node, parent *Node) bool</c>。</summary>
    public static bool IsParameterPropertyDeclaration(Node? node, Node? parent)
    {
        var n = node!;
        var p = parent!;
        return IsParameterDeclaration(n) && HasSyntacticModifier(n, ModifierFlags.ParameterPropertyModifier) &&
            p.Kind == Kind.Constructor;
    }

    /// <summary>Go: <c>func IsJsxChild(node *Node) bool</c>。</summary>
    public static bool IsJsxChild(Node? node)
    {
        var n = node!;
        return n.Kind is
            Kind.JsxElement or
            Kind.JsxExpression or
            Kind.JsxSelfClosingElement or
            Kind.JsxText or
            Kind.JsxFragment;
    }

    /// <summary>Go: <c>func IsJsxAttributeLike(node *Node) bool</c>。</summary>
    public static bool IsJsxAttributeLike(Node? node)
    {
        var n = node!;
        return IsJsxAttribute(n) || IsJsxSpreadAttribute(n);
    }

    /// <summary>Go: <c>func isDeclarationStatementKind(kind Kind) bool</c>。</summary>
    internal static bool IsDeclarationStatementKind(Kind kind) => kind is
        Kind.FunctionDeclaration or
        Kind.MissingDeclaration or
        Kind.ClassDeclaration or
        Kind.InterfaceDeclaration or
        Kind.TypeAliasDeclaration or
        Kind.JSTypeAliasDeclaration or
        Kind.EnumDeclaration or
        Kind.ModuleDeclaration or
        Kind.ImportDeclaration or
        Kind.JSImportDeclaration or
        Kind.ImportEqualsDeclaration or
        Kind.ExportDeclaration or
        Kind.ExportAssignment or
        Kind.NamespaceExportDeclaration;

    // Determines whether a node is a DeclarationStatement. Ideally this does not use Parent pointers, but it may use them
    // to rule out a Block node that is part of `try` or `catch` or is the Block-like body of a function.
    //
    // NOTE: ECMA262 would just call this a Declaration
    /// <summary>Go: <c>func IsDeclarationStatement(node *Node) bool</c>。</summary>
    public static bool IsDeclarationStatement(Node? node)
    {
        var n = node!;
        return IsDeclarationStatementKind(n.Kind);
    }

    /// <summary>Go: <c>func isStatementKindButNotDeclarationKind(kind Kind) bool</c>。</summary>
    internal static bool IsStatementKindButNotDeclarationKind(Kind kind) => kind is
        Kind.BreakStatement or
        Kind.ContinueStatement or
        Kind.DebuggerStatement or
        Kind.DoStatement or
        Kind.ExpressionStatement or
        Kind.EmptyStatement or
        Kind.ForInStatement or
        Kind.ForOfStatement or
        Kind.ForStatement or
        Kind.IfStatement or
        Kind.LabeledStatement or
        Kind.ReturnStatement or
        Kind.SwitchStatement or
        Kind.ThrowStatement or
        Kind.TryStatement or
        Kind.VariableStatement or
        Kind.WhileStatement or
        Kind.WithStatement or
        Kind.NotEmittedStatement;

    // Determines whether a node is a Statement that is not also a Declaration. Ideally this does not use Parent pointers,
    // but it may use them to rule out a Block node that is part of `try` or `catch` or is the Block-like body of a function.
    //
    // NOTE: ECMA262 would just call this a Statement
    /// <summary>Go: <c>func IsStatementButNotDeclaration(node *Node) bool</c>。</summary>
    public static bool IsStatementButNotDeclaration(Node? node)
    {
        var n = node!;
        return IsStatementKindButNotDeclarationKind(n.Kind);
    }

    // Determines whether a node is a Statement. Ideally this does not use Parent pointers, but it may use
    // them to rule out a Block node that is part of `try` or `catch` or is the Block-like body of a function.
    //
    // NOTE: ECMA262 would call this either a StatementListItem or ModuleListItem
    /// <summary>Go: <c>func IsStatement(node *Node) bool</c>。</summary>
    public static bool IsStatement(Node? node)
    {
        var n = node!;
        var kind = n.Kind;
        return IsStatementKindButNotDeclarationKind(kind) || IsDeclarationStatementKind(kind) || IsBlockStatement(n);
    }

    // Determines whether a node is a BlockStatement. If parents are available, this ensures the Block is
    // not part of a `try` statement, `catch` clause, or the Block-like body of a function
    /// <summary>Go: <c>func isBlockStatement(node *Node) bool</c>。</summary>
    internal static bool IsBlockStatement(Node? node)
    {
        var n = node!;
        if (n.Kind != Kind.Block)
        {
            return false;
        }

        if (n.Parent != null && (n.Parent.Kind == Kind.TryStatement || n.Parent.Kind == Kind.CatchClause))
        {
            return false;
        }

        return !IsFunctionBlock(n);
    }

    // Determines whether a node is the Block-like body of a function by walking the parent of the node
    /// <summary>Go: <c>func IsFunctionBlock(node *Node) bool</c>。</summary>
    public static bool IsFunctionBlock(Node? node) =>
        node is { } n && n.Kind == Kind.Block && n.Parent != null && IsFunctionLike(n.Parent);

    /// <summary>Go: <c>func IsBlockOrCatchScoped(declaration *Node) bool</c>。</summary>
    public static bool IsBlockOrCatchScoped(Node? declaration)
    {
        var d = declaration!;
        return (GetCombinedNodeFlags(d) & NodeFlags.BlockScoped) != 0 ||
            IsCatchClauseVariableDeclarationOrBindingElement(d);
    }

    /// <summary>Go: <c>func IsCatchClauseVariableDeclarationOrBindingElement(declaration *Node) bool</c>。</summary>
    public static bool IsCatchClauseVariableDeclarationOrBindingElement(Node? declaration)
    {
        var d = declaration!;
        var node = GetRootDeclaration(d)!;
        return node.Kind == Kind.VariableDeclaration && node.Parent!.Kind == Kind.CatchClause;
    }

    /// <summary>Go: <c>func IsTypeNodeKind(kind Kind) bool</c>。</summary>
    public static bool IsTypeNodeKind(Kind kind)
    {
        switch (kind)
        {
            case Kind.AnyKeyword:
            case Kind.UnknownKeyword:
            case Kind.NumberKeyword:
            case Kind.BigIntKeyword:
            case Kind.ObjectKeyword:
            case Kind.BooleanKeyword:
            case Kind.StringKeyword:
            case Kind.SymbolKeyword:
            case Kind.VoidKeyword:
            case Kind.UndefinedKeyword:
            case Kind.NeverKeyword:
            case Kind.IntrinsicKeyword:
            case Kind.ExpressionWithTypeArguments:
            case Kind.JSDocAllType:
            case Kind.JSDocNullableType:
            case Kind.JSDocNonNullableType:
            case Kind.JSDocOptionalType:
            case Kind.JSDocVariadicType:
                return true;
        }

        return kind >= Kind.FirstTypeNode && kind <= Kind.LastTypeNode;
    }

    /// <summary>Go: <c>func IsTypeNode(node *Node) bool</c>。</summary>
    public static bool IsTypeNode(Node? node)
    {
        var n = node!;
        return IsTypeNodeKind(n.Kind);
    }

    /// <summary>Go: <c>func IsJSDocKind(kind Kind) bool</c>。</summary>
    public static bool IsJSDocKind(Kind kind) => Kind.FirstJSDocNode <= kind && kind <= Kind.LastJSDocNode;

    /// <summary>Go: <c>func IsJSDocTypeAssertion(node *Node) bool</c>。</summary>
    public static bool IsJSDocTypeAssertion(Node? node)
    {
        if (node == null || !IsParenthesizedExpression(node) || !IsInJSFile(node))
        {
            return false;
        }

        var expr = node.Expression()!;
        return IsAsExpression(expr) && expr.Type() != null && (expr.Type()!.Flags & NodeFlags.Reparsed) != 0;
    }

    /// <summary>Go: <c>func IsPrologueDirective(node *Node) bool</c>。</summary>
    public static bool IsPrologueDirective(Node? node)
    {
        var n = node!;
        return n.Kind == Kind.ExpressionStatement && n.Expression()!.Kind == Kind.StringLiteral;
    }

    /// <summary>Go: <c>type OuterExpressionKinds uint16</c>（含 Go 的包级常量组 OEK*：数值逐条照抄，成员名去 `OEK` 前缀）。</summary>

    // Determines whether node is an "outer expression" of the provided kinds
    /// <summary>Go: <c>func IsOuterExpression(node *Expression, kinds OuterExpressionKinds) bool</c>。</summary>
    public static bool IsOuterExpression(Node? node, OuterExpressionKinds kinds)
    {
        var n = node!;
        switch (n.Kind)
        {
            case Kind.ParenthesizedExpression:
                return (kinds & OuterExpressionKinds.Parentheses) != 0 &&
                    !((kinds & OuterExpressionKinds.ExcludeJSDocTypeAssertion) != 0 && IsJSDocTypeAssertion(n));
            case Kind.TypeAssertionExpression:
            case Kind.AsExpression:
                return (kinds & OuterExpressionKinds.TypeAssertions) != 0;
            case Kind.SatisfiesExpression:
                return (kinds & (OuterExpressionKinds.ExpressionsWithTypeArguments | OuterExpressionKinds.Satisfies)) != 0;
            case Kind.ExpressionWithTypeArguments:
                return (kinds & OuterExpressionKinds.ExpressionsWithTypeArguments) != 0;
            case Kind.NonNullExpression:
                return (kinds & OuterExpressionKinds.NonNullAssertions) != 0;
            case Kind.PartiallyEmittedExpression:
                return (kinds & OuterExpressionKinds.PartiallyEmittedExpressions) != 0;
            case Kind.BinaryExpression:
                var operatorToken = n.AsBinaryExpression()!.OperatorToken!;
                switch (operatorToken.Kind)
                {
                    case Kind.EqualsToken:
                        return (kinds & OuterExpressionKinds.Assignments) != 0;
                    case Kind.CommaToken:
                        return (kinds & OuterExpressionKinds.Comma) != 0;
                }

                break;
        }

        return false;
    }

    // Descends into an expression, skipping past "outer expressions" of the provided kinds
    /// <summary>Go: <c>func SkipOuterExpressions(node *Expression, kinds OuterExpressionKinds) *Expression</c>。</summary>
    public static Node? SkipOuterExpressions(Node? node, OuterExpressionKinds kinds)
    {
        while (IsOuterExpression(node, kinds))
        {
            if (IsBinaryExpression(node!))
            {
                node = node!.AsBinaryExpression()!.Right;
            }
            else
            {
                node = node!.Expression();
            }
        }

        return node;
    }

    // Skips past the parentheses of an expression
    /// <summary>Go: <c>func SkipParentheses(node *Expression) *Expression</c>。</summary>
    public static Node? SkipParentheses(Node? node) => SkipOuterExpressions(node, OuterExpressionKinds.Parentheses);

    /// <summary>Go: <c>func SkipTypeParentheses(node *Node) *Node</c>。</summary>
    public static Node? SkipTypeParentheses(Node? node)
    {
        while (IsParenthesizedTypeNode(node!))
        {
            node = node!.Type();
        }

        return node;
    }

    /// <summary>Go: <c>func SkipPartiallyEmittedExpressions(node *Expression) *Expression</c>。</summary>
    public static Node? SkipPartiallyEmittedExpressions(Node? node) =>
        SkipOuterExpressions(node, OuterExpressionKinds.PartiallyEmittedExpressions);

    // Walks up the parents of a parenthesized expression to find the containing node
    /// <summary>Go: <c>func WalkUpParenthesizedExpressions(node *Expression) *Node</c>。</summary>
    public static Node? WalkUpParenthesizedExpressions(Node? node)
    {
        while (node != null && node.Kind == Kind.ParenthesizedExpression)
        {
            node = node.Parent;
        }

        return node;
    }

    // Walks up the parents of a parenthesized type to find the containing node
    /// <summary>Go: <c>func WalkUpParenthesizedTypes(node *TypeNode) *Node</c>。</summary>
    public static Node? WalkUpParenthesizedTypes(Node? node)
    {
        while (node != null && node.Kind == Kind.ParenthesizedType)
        {
            node = node.Parent;
        }

        return node;
    }

    // Walks up the parents of a node to find the containing SourceFile
    /// <summary>Go: <c>func GetSourceFileOfNode(node *Node) *SourceFile</c>。</summary>
    public static SourceFile? GetSourceFileOfNode(Node? node)
    {
        while (node != null)
        {
            if (node.Kind == Kind.SourceFile)
            {
                return node.AsSourceFile();
            }

            node = node.Parent;
        }

        return null;
    }

    // Go 的状态容器：Go 原文把 parent/visit 两个状态合并成一次分配（Consolidate state into one allocation.）
    private sealed class ParentInChildrenState
    {
        public Node? Parent;
        public Visitor? Visit;
    }

    // Go: var setParentInChildrenPool = sync.Pool{New: func() any { return newParentInChildrenSetter() }}
    // 与 Go 的差异：sync.Pool → [ThreadStatic] 槽位（取出时置空，重入时另分配一份），
    // 与 Core.cs 的 levenshtein 缓冲区池同一套语义。
    [ThreadStatic]
    private static Func<Node, bool>? _tlsParentInChildrenSetter;

    /// <summary>Go: <c>func newParentInChildrenSetter() func(node *Node) bool</c>。</summary>
    internal static Func<Node, bool> NewParentInChildrenSetter()
    {
        // Consolidate state into one allocation.
        // Similar to https://go.dev/cl/552375.
        var state = new ParentInChildrenState();

        state.Visit = node =>
        {
            if (state.Parent != null)
            {
                node.Parent = state.Parent;
            }

            var saveParent = state.Parent;
            state.Parent = node;
            node.ForEachChild(state.Visit!);
            state.Parent = saveParent;
            return false;
        };

        return node => state.Visit!(node);
    }

    /// <summary>Go: <c>func SetParentInChildren(node *Node)</c>。</summary>
    public static void SetParentInChildren(Node? node)
    {
        var fn = _tlsParentInChildrenSetter;
        if (fn == null)
        {
            fn = NewParentInChildrenSetter();
        }
        else
        {
            _tlsParentInChildrenSetter = null;
        }

        try
        {
            fn(node!);
        }
        finally
        {
            _tlsParentInChildrenSetter = fn;
        }
    }

    // This should never be called outside the parser
    /// <summary>Go: <c>func SetImportsOfSourceFile(node *SourceFile, imports []*LiteralLikeNode)</c>。</summary>
    public static void SetImportsOfSourceFile(SourceFile? node, Node[] imports)
    {
        var n = node!;
        // 依赖 SourceFile._imports（Go ast.go:2473 的未导出字段 imports，属另一片段）。
        n._imports = imports;
    }

    // Walks up the parents of a node to find the ancestor that matches the callback
    /// <summary>Go: <c>func FindAncestor(node *Node, callback func(*Node) bool) *Node</c>。</summary>
    public static Node? FindAncestor(Node? node, Func<Node, bool> callback)
    {
        while (node != null)
        {
            if (callback(node))
            {
                return node;
            }

            node = node.Parent;
        }

        return null;
    }

    /// <summary>Go: <c>func FindManyAncestors(node *Node, callbacks ...func(*Node) bool) []*Node</c>。</summary>
    public static Node?[] FindManyAncestors(Node? node, params Func<Node, bool>[] callbacks)
    {
        var ancestors = new Node?[callbacks.Length];
        var found = 0;
        while (node != null)
        {
            for (var i = 0; i < callbacks.Length; i++)
            {
                if (ancestors[i] == null && callbacks[i](node))
                {
                    ancestors[i] = node;
                    found++;
                    if (found == callbacks.Length)
                    {
                        return ancestors;
                    }

                    break;
                }
            }

            node = node.Parent;
        }

        return ancestors;
    }

    // Walks up the parents of a node to find the ancestor that matches the kind
    /// <summary>Go: <c>func FindAncestorKind(node *Node, kind Kind) *Node</c>。</summary>
    public static Node? FindAncestorKind(Node? node, Kind kind)
    {
        while (node != null)
        {
            if (node.Kind == kind)
            {
                return node;
            }

            node = node.Parent;
        }

        return null;
    }

    /// <summary>Go: <c>type FindAncestorResult int32</c>（Go 常量组 FindAncestorFalse/FindAncestorTrue/FindAncestorQuit 的数值 0/1/2）。</summary>

    /// <summary>Go: <c>func ToFindAncestorResult(b bool) FindAncestorResult</c>。</summary>
    public static FindAncestorResult ToFindAncestorResult(bool b) =>
        b ? FindAncestorResult.True : FindAncestorResult.False;

    // Walks up the parents of a node to find the ancestor that matches the callback
    /// <summary>Go: <c>func FindAncestorOrQuit(node *Node, callback func(*Node) FindAncestorResult) *Node</c>。</summary>
    public static Node? FindAncestorOrQuit(Node? node, Func<Node, FindAncestorResult> callback)
    {
        while (node != null)
        {
            switch (callback(node))
            {
                case FindAncestorResult.Quit:
                    return null;
                case FindAncestorResult.True:
                    return node;
            }

            node = node.Parent;
        }

        return null;
    }

    /// <summary>Go: <c>func IsNodeDescendantOf(node *Node, ancestor *Node) bool</c>。</summary>
    public static bool IsNodeDescendantOf(Node? node, Node? ancestor)
    {
        while (node != null)
        {
            if (node == ancestor)
            {
                return true;
            }

            node = node.Parent;
        }

        return false;
    }


    /// <summary>Go: <c>func IsStatic(node *Node) bool</c>。</summary>
    public static bool IsStatic(Node? node)
    {
        var n = node!;
        // https://tc39.es/ecma262/#sec-static-semantics-isstatic
        return IsClassElement(n) && HasStaticModifier(n) || IsClassStaticBlockDeclaration(n);
    }

    /// <summary>Go: <c>func CanHaveSymbol(node *Node) bool</c>。</summary>
    public static bool CanHaveSymbol(Node? node)
    {
        var n = node!;
        switch (n.Kind)
        {
            case Kind.ArrowFunction:
            case Kind.BinaryExpression:
            case Kind.BindingElement:
            case Kind.CallExpression:
            case Kind.CallSignature:
            case Kind.ClassDeclaration:
            case Kind.ClassExpression:
            case Kind.ClassStaticBlockDeclaration:
            case Kind.Constructor:
            case Kind.ConstructorType:
            case Kind.ConstructSignature:
            case Kind.ElementAccessExpression:
            case Kind.EnumDeclaration:
            case Kind.EnumMember:
            case Kind.ExportAssignment:
            case Kind.ExportDeclaration:
            case Kind.ExportSpecifier:
            case Kind.FunctionDeclaration:
            case Kind.FunctionExpression:
            case Kind.FunctionType:
            case Kind.GetAccessor:
            case Kind.ImportClause:
            case Kind.ImportEqualsDeclaration:
            case Kind.ImportSpecifier:
            case Kind.IndexSignature:
            case Kind.InterfaceDeclaration:
            case Kind.JSTypeAliasDeclaration:
            case Kind.JsxAttribute:
            case Kind.JsxAttributes:
            case Kind.JsxSpreadAttribute:
            case Kind.MappedType:
            case Kind.MethodDeclaration:
            case Kind.MethodSignature:
            case Kind.ModuleDeclaration:
            case Kind.NamedTupleMember:
            case Kind.NamespaceExport:
            case Kind.NamespaceExportDeclaration:
            case Kind.NamespaceImport:
            case Kind.NewExpression:
            case Kind.NoSubstitutionTemplateLiteral:
            case Kind.NumericLiteral:
            case Kind.ObjectLiteralExpression:
            case Kind.Parameter:
            case Kind.PropertyAccessExpression:
            case Kind.PropertyAssignment:
            case Kind.PropertyDeclaration:
            case Kind.PropertySignature:
            case Kind.SetAccessor:
            case Kind.ShorthandPropertyAssignment:
            case Kind.SourceFile:
            case Kind.SpreadAssignment:
            case Kind.StringLiteral:
            case Kind.TypeAliasDeclaration:
            case Kind.TypeLiteral:
            case Kind.TypeParameter:
            case Kind.VariableDeclaration:
                return true;
        }
        return false;
    }

    /// <summary>Go: <c>func CanHaveIllegalDecorators(node *Node) bool</c>。</summary>
    public static bool CanHaveIllegalDecorators(Node? node)
    {
        var n = node!;
        switch (n.Kind)
        {
            case Kind.PropertyAssignment:
            case Kind.ShorthandPropertyAssignment:
            case Kind.FunctionDeclaration:
            case Kind.Constructor:
            case Kind.IndexSignature:
            case Kind.ClassStaticBlockDeclaration:
            case Kind.MissingDeclaration:
            case Kind.VariableStatement:
            case Kind.InterfaceDeclaration:
            case Kind.TypeAliasDeclaration:
            case Kind.EnumDeclaration:
            case Kind.ModuleDeclaration:
            case Kind.ImportEqualsDeclaration:
            case Kind.ImportDeclaration:
            case Kind.JSImportDeclaration:
            case Kind.NamespaceExportDeclaration:
            case Kind.ExportDeclaration:
            case Kind.ExportAssignment:
                return true;
        }
        return false;
    }

    /// <summary>Go: <c>func CanHaveIllegalModifiers(node *Node) bool</c>。</summary>
    public static bool CanHaveIllegalModifiers(Node? node)
    {
        var n = node!;
        switch (n.Kind)
        {
            case Kind.ClassStaticBlockDeclaration:
            case Kind.PropertyAssignment:
            case Kind.ShorthandPropertyAssignment:
            case Kind.MissingDeclaration:
            case Kind.NamespaceExportDeclaration:
                return true;
        }
        return false;
    }

    /// <summary>Go: <c>func CanHaveModifiers(node *Node) bool</c>。</summary>
    public static bool CanHaveModifiers(Node? node)
    {
        var n = node!;
        switch (n.Kind)
        {
            case Kind.TypeParameter:
            case Kind.Parameter:
            case Kind.PropertySignature:
            case Kind.PropertyDeclaration:
            case Kind.MethodSignature:
            case Kind.MethodDeclaration:
            case Kind.Constructor:
            case Kind.GetAccessor:
            case Kind.SetAccessor:
            case Kind.IndexSignature:
            case Kind.ConstructorType:
            case Kind.FunctionExpression:
            case Kind.ArrowFunction:
            case Kind.ClassExpression:
            case Kind.VariableStatement:
            case Kind.FunctionDeclaration:
            case Kind.ClassDeclaration:
            case Kind.InterfaceDeclaration:
            case Kind.TypeAliasDeclaration:
            case Kind.EnumDeclaration:
            case Kind.ModuleDeclaration:
            case Kind.ImportEqualsDeclaration:
            case Kind.ImportDeclaration:
            case Kind.JSImportDeclaration:
            case Kind.ExportAssignment:
            case Kind.ExportDeclaration:
                return true;
        }
        return false;
    }

    /// <summary>Go: <c>func CanHaveDecorators(node *Node) bool</c>。</summary>
    public static bool CanHaveDecorators(Node? node)
    {
        var n = node!;
        switch (n.Kind)
        {
            case Kind.Parameter:
            case Kind.PropertyDeclaration:
            case Kind.MethodDeclaration:
            case Kind.GetAccessor:
            case Kind.SetAccessor:
            case Kind.ClassExpression:
            case Kind.ClassDeclaration:
                return true;
        }
        return false;
    }

    /// <summary>Go: <c>func IsFunctionOrModuleBlock(node *Node) bool</c>。</summary>
    public static bool IsFunctionOrModuleBlock(Node? node)
    {
        var n = node!;
        return IsSourceFile(n) || IsModuleBlock(n) || IsBlock(n) && IsFunctionLike(n.Parent!);
    }

    /// <summary>Go: <c>func IsFunctionExpressionOrArrowFunction(node *Node) bool</c>。</summary>
    public static bool IsFunctionExpressionOrArrowFunction(Node? node)
    {
        var n = node!;
        return IsFunctionExpression(n) || IsArrowFunction(n);
    }

    // Warning: This has the same semantics as the forEach family of functions in that traversal terminates
    // in the event that 'visitor' returns true.
    /// <summary>Go: <c>func ForEachReturnStatement(body *Node, visitor func(stmt *Node) bool) bool</c>。</summary>
    public static bool ForEachReturnStatement(Node? body, Func<Node, bool> visitor)
    {
        // 与 Go 的差异：Go 的 `var traverse func(*Node) bool` 递归闭包 → C# 局部函数；局部函数形参必须非空
        // （要转成 Visitor 委托）。
        bool Traverse(Node node)
        {
            switch (node.Kind)
            {
                case Kind.ReturnStatement:
                    return visitor(node);
                case Kind.CaseBlock:
                case Kind.Block:
                case Kind.IfStatement:
                case Kind.DoStatement:
                case Kind.WhileStatement:
                case Kind.ForStatement:
                case Kind.ForInStatement:
                case Kind.ForOfStatement:
                case Kind.WithStatement:
                case Kind.SwitchStatement:
                case Kind.CaseClause:
                case Kind.DefaultClause:
                case Kind.LabeledStatement:
                case Kind.TryStatement:
                case Kind.CatchClause:
                    return node.ForEachChild(Traverse);
            }
            return false;
        }

        return Traverse(body!);
    }

    /// <summary>Go: <c>func GetRootDeclaration(node *Node) *Node</c>。</summary>
    public static Node? GetRootDeclaration(Node? node)
    {
        while (node!.Kind == Kind.BindingElement)
        {
            node = node!.Parent!.Parent;
        }
        return node;
    }

    /// <summary>Go: <c>func GetCombinedModifierFlags(node *Node) ModifierFlags</c>。</summary>
    public static ModifierFlags GetCombinedModifierFlags(Node? node)
    {
        // 与 Go 的差异：Go 直接重写形参 `node`（随后可能变成 nil）；C# 用显式 `Node? n` 局部。
        Node? n = GetRootDeclaration(node)!;
        var flags = n.ModifierFlags;
        if (n.Kind == Kind.VariableDeclaration)
        {
            n = n.Parent;
        }
        if (n is not null && n.Kind == Kind.VariableDeclarationList)
        {
            flags |= n.ModifierFlags;
            n = n.Parent;
        }
        if (n is not null && n.Kind == Kind.VariableStatement)
        {
            flags |= n.ModifierFlags;
        }
        return flags;
    }

    /// <summary>Go: <c>func GetCombinedNodeFlags(node *Node) NodeFlags</c>。</summary>
    public static NodeFlags GetCombinedNodeFlags(Node? node)
    {
        // 与 Go 的差异：Go 直接重写形参 `node`（随后可能变成 nil）；C# 用显式 `Node? n` 局部。
        Node? n = GetRootDeclaration(node)!;
        var flags = n.Flags;
        if (n.Kind == Kind.VariableDeclaration)
        {
            n = n.Parent;
        }
        if (n is not null && n.Kind == Kind.VariableDeclarationList)
        {
            flags |= n.Flags;
            n = n.Parent;
        }
        if (n is not null && n.Kind == Kind.VariableStatement)
        {
            flags |= n.Flags;
        }
        return flags;
    }

    // Gets whether a bound `VariableDeclaration` or `VariableDeclarationList` is part of an `await using` declaration.
    /// <summary>Go: <c>func IsVarAwaitUsing(node *Node) bool</c>。</summary>
    public static bool IsVarAwaitUsing(Node? node)
    {
        return (GetCombinedNodeFlags(node) & NodeFlags.BlockScoped) == NodeFlags.AwaitUsing;
    }

    // Gets whether a bound `VariableDeclaration` or `VariableDeclarationList` is part of a `using` declaration.
    /// <summary>Go: <c>func IsVarUsing(node *Node) bool</c>。</summary>
    public static bool IsVarUsing(Node? node)
    {
        return (GetCombinedNodeFlags(node) & NodeFlags.BlockScoped) == NodeFlags.Using;
    }

    // GetJSDocDeprecatedTag returns the first @deprecated JSDoc tag for the given node, or nil if none exists.
    /// <summary>Go: <c>func GetJSDocDeprecatedTag(node *Node) *Node</c>。</summary>
    public static Node? GetJSDocDeprecatedTag(Node? node)
    {
        var n = node!;
        // 与 Go 的差异：Node.JSDoc(file *SourceFile) 属 ast.go 切片（C# 侧尚未落地），照 Go 名字调用；
        // Go 的 nil file → null；Go 的 nil slice 可 range，C# 用 `?? []` 兜住 null 数组。
        foreach (var jsdoc in n.JSDoc(null) ?? [])
        {
            var tags = jsdoc.AsJSDoc()!.Tags;
            if (tags is not null)
            {
                foreach (var tag in tags.Nodes ?? [])
                {
                    if (IsJSDocDeprecatedTag(tag))
                    {
                        return tag;
                    }
                }
            }
        }
        return null;
    }

    // IsDeprecatedDeclaration reports whether the given declaration is marked as @deprecated.
    // It checks NodeFlagsPossiblyContainsDeprecatedTag on combined node flags, then confirms
    // by walking up to find the node with the flag and performing a JSDoc lookup.
    /// <summary>Go: <c>func IsDeprecatedDeclaration(declaration *Node) bool</c>。</summary>
    public static bool IsDeprecatedDeclaration(Node? declaration)
    {
        return IsDeprecatedDeclarationWithCachedFlags(declaration, GetCombinedNodeFlags(declaration));
    }

    // IsDeprecatedDeclarationWithCachedFlags is the core logic for IsDeprecatedDeclaration,
    // parameterized on pre-computed combined flags so the checker can supply cached flags.
    /// <summary>Go: <c>func IsDeprecatedDeclarationWithCachedFlags(declaration *Node, combinedFlags NodeFlags) bool</c>。</summary>
    public static bool IsDeprecatedDeclarationWithCachedFlags(Node? declaration, NodeFlags combinedFlags)
    {
        if ((combinedFlags & NodeFlags.PossiblyContainsDeprecatedTag) == 0)
        {
            return false;
        }
        // Walk up to find the node that directly has the flag, since JSDoc is
        // attached to that node (e.g. VariableStatement, not VariableDeclaration).
        for (Node? n = declaration; n is not null; n = n.Parent)
        {
            if ((n.Flags & NodeFlags.PossiblyContainsDeprecatedTag) != 0)
            {
                return GetJSDocDeprecatedTag(n) is not null;
            }
        }
        return false;
    }

    // Gets whether a bound `VariableDeclaration` or `VariableDeclarationList` is part of a `const` declaration.
    /// <summary>Go: <c>func IsVarConst(node *Node) bool</c>。</summary>
    public static bool IsVarConst(Node? node)
    {
        return (GetCombinedNodeFlags(node) & NodeFlags.BlockScoped) == NodeFlags.Const;
    }

    // Gets whether a bound `VariableDeclaration` or `VariableDeclarationList` is part of a `const`, `using` or `await using` declaration.
    /// <summary>Go: <c>func IsVarConstLike(node *Node) bool</c>。</summary>
    public static bool IsVarConstLike(Node? node)
    {
        switch (GetCombinedNodeFlags(node) & NodeFlags.BlockScoped)
        {
            case NodeFlags.Const:
            case NodeFlags.Using:
            case NodeFlags.AwaitUsing:
                return true;
        }
        return false;
    }

    // Gets whether a bound `VariableDeclaration` or `VariableDeclarationList` is part of a `let` declaration.
    /// <summary>Go: <c>func IsVarLet(node *Node) bool</c>。</summary>
    public static bool IsVarLet(Node? node)
    {
        return (GetCombinedNodeFlags(node) & NodeFlags.BlockScoped) == NodeFlags.Let;
    }

    /// <summary>Go: <c>func IsImportMeta(node *Node) bool</c>。</summary>
    public static bool IsImportMeta(Node? node)
    {
        var n = node!;
        if (n.Kind == Kind.MetaProperty)
        {
            return n.AsMetaProperty()!.KeywordToken == Kind.ImportKeyword && n.AsMetaProperty()!.Name()!.Text() == "meta";
        }
        return false;
    }

    /// <summary>Go: <c>func WalkUpBindingElementsAndPatterns(binding *Node) *Node</c>。</summary>
    public static Node? WalkUpBindingElementsAndPatterns(Node? binding)
    {
        var n = binding!;
        var node = n.Parent;
        while (IsBindingElement(node!.Parent!))
        {
            node = node!.Parent!.Parent;
        }
        return node!.Parent;
    }

    /// <summary>Go: <c>func IsSourceFileJS(file *SourceFile) bool</c>。</summary>
    public static bool IsSourceFileJS(SourceFile? file)
    {
        var f = file!;
        return f.ScriptKind == ScriptKind.JS || f.ScriptKind == ScriptKind.JSX;
    }

    /// <summary>Go: <c>func IsInJSFile(node *Node) bool</c>。</summary>
    public static bool IsInJSFile(Node? node)
    {
        return node is not null && (node.Flags & NodeFlags.JavaScriptFile) != 0;
    }

    /// <summary>Go: <c>func IsDeclaration(node *Node) bool</c>。</summary>
    public static bool IsDeclaration(Node? node)
    {
        var n = node!;
        if (n.Kind == Kind.TypeParameter)
        {
            return n.Parent is not null;
        }
        return IsDeclarationNode(n);
    }

    // True if `name` is the name of a declaration node
    /// <summary>Go: <c>func IsDeclarationName(name *Node) bool</c>。</summary>
    public static bool IsDeclarationName(Node? name)
    {
        var n = name!;
        return !IsSourceFile(n) && !IsBindingPattern(n) && IsDeclaration(n.Parent) && n.Parent!.Name() == n;
    }

    // Like 'isDeclarationName', but returns true for LHS of `import { x as y }` or `export { x as y }`.
    /// <summary>Go: <c>func IsDeclarationNameOrImportPropertyName(name *Node) bool</c>。</summary>
    public static bool IsDeclarationNameOrImportPropertyName(Node? name)
    {
        var n = name!;
        switch (n.Parent!.Kind)
        {
            case Kind.ImportSpecifier:
            case Kind.ExportSpecifier:
                return IsIdentifier(n) || n.Kind == Kind.StringLiteral;
            default:
                return IsDeclarationName(n);
        }
    }

    /// <summary>Go: <c>func IsLiteralComputedPropertyDeclarationName(node *Node) bool</c>。</summary>
    public static bool IsLiteralComputedPropertyDeclarationName(Node? node)
    {
        var n = node!;
        return IsStringOrNumericLiteralLike(n) &&
            n.Parent!.Kind == Kind.ComputedPropertyName &&
            IsDeclaration(n.Parent!.Parent);
    }

    /// <summary>Go: <c>func IsExternalModuleImportEqualsDeclaration(node *Node) bool</c>。</summary>
    public static bool IsExternalModuleImportEqualsDeclaration(Node? node)
    {
        var n = node!;
        return n.Kind == Kind.ImportEqualsDeclaration && n.AsImportEqualsDeclaration()!.ModuleReference!.Kind == Kind.ExternalModuleReference;
    }

    /// <summary>Go: <c>func IsModuleOrEnumDeclaration(node *Node) bool</c>。</summary>
    public static bool IsModuleOrEnumDeclaration(Node? node)
    {
        var n = node!;
        return n.Kind == Kind.ModuleDeclaration || n.Kind == Kind.EnumDeclaration;
    }

    /// <summary>Go: <c>func IsLiteralImportTypeNode(node *Node) bool</c>。</summary>
    public static bool IsLiteralImportTypeNode(Node? node)
    {
        var n = node!;
        return IsImportTypeNode(n) && IsLiteralTypeNode(n.AsImportTypeNode()!.Argument!) && IsStringLiteral(n.AsImportTypeNode()!.Argument!.AsLiteralTypeNode()!.Literal!);
    }

    /// <summary>Go: <c>func IsJsxTagName(node *Node) bool</c>。</summary>
    public static bool IsJsxTagName(Node? node)
    {
        var n = node!;
        var parent = n.Parent!;
        switch (parent.Kind)
        {
            case Kind.JsxOpeningElement:
            case Kind.JsxClosingElement:
            case Kind.JsxSelfClosingElement:
                return parent.TagName() == n;
        }
        return false;
    }

    /// <summary>Go: <c>func IsImportOrExportSpecifier(node *Node) bool</c>。</summary>
    public static bool IsImportOrExportSpecifier(Node? node)
    {
        var n = node!;
        return IsImportSpecifier(n) || IsExportSpecifier(n);
    }

    /// <summary>Go: <c>func IsVoidZero(node *Node) bool</c>。</summary>
    public static bool IsVoidZero(Node? node)
    {
        var n = node!;
        return IsVoidExpression(n) && IsNumericLiteral(n.Expression()!) && n.Expression()!.Text() == "0";
    }

    /// <summary>Go: <c>func IsExportsIdentifier(node *Node) bool</c>。</summary>
    public static bool IsExportsIdentifier(Node? node)
    {
        var n = node!;
        return IsIdentifier(n) && n.Text() == "exports";
    }

    /// <summary>Go: <c>func IsModuleIdentifier(node *Node) bool</c>。</summary>
    public static bool IsModuleIdentifier(Node? node)
    {
        var n = node!;
        return IsIdentifier(n) && n.Text() == "module";
    }

    // Go: func IsThisIdentifier(node *Node) bool —— 见 Utilities.cs（Go utilities.go:1367），不在本片段声明。


/// <summary>Go: <c>func IsThisIdentifier(node *Node) bool</c>。</summary>
    public static bool IsThisIdentifier(Node node) => IsIdentifier(node) && node.Text() == "this";


    /// <summary>Go: <c>func IsThisParameter(node *Node) bool</c>。</summary>
    public static bool IsThisParameter(Node? node)
    {
        var n = node!;
        return IsParameterDeclaration(n) && n.Name() is not null && IsThisIdentifier(n.Name()!);
    }

    /// <summary>Go: <c>func IsBindableStaticAccessExpression(node *Node, excludeThisKeyword bool) bool</c>。</summary>
    public static bool IsBindableStaticAccessExpression(Node? node, bool excludeThisKeyword)
    {
        var n = node!;
        return IsPropertyAccessExpression(n) &&
            ((!excludeThisKeyword && n.Expression()!.Kind == Kind.ThisKeyword) ||
                (IsIdentifier(n.Name()!) && IsBindableStaticNameExpression(n.Expression()!, true /*excludeThisKeyword*/))) ||
            IsBindableStaticElementAccessExpression(n, excludeThisKeyword);
    }

    /// <summary>Go: <c>func IsBindableStaticElementAccessExpression(node *Node, excludeThisKeyword bool) bool</c>。</summary>
    public static bool IsBindableStaticElementAccessExpression(Node? node, bool excludeThisKeyword)
    {
        var n = node!;
        return IsLiteralLikeElementAccess(n) &&
            ((!excludeThisKeyword && n.Expression()!.Kind == Kind.ThisKeyword) ||
                IsEntityNameExpression(n.Expression()!) ||
                IsBindableStaticAccessExpression(n.Expression()!, true /*excludeThisKeyword*/));
    }

    /// <summary>Go: <c>func IsPrototypeAccess(node *Node) bool</c>。</summary>
    public static bool IsPrototypeAccess(Node? node)
    {
        if (IsBindableStaticAccessExpression(node, false /*excludeThisKeyword*/))
        {
            var name = GetElementOrPropertyAccessName(node);
            if (name is not null)
            {
                return name.Text() == "prototype";
            }
        }
        return false;
    }

    /// <summary>Go: <c>func IsLiteralLikeElementAccess(node *Node) bool</c>。</summary>
    public static bool IsLiteralLikeElementAccess(Node? node)
    {
        var n = node!;
        return IsElementAccessExpression(n) && IsStringOrNumericLiteralLike(n.AsElementAccessExpression()!.ArgumentExpression!);
    }

    /// <summary>Go: <c>func IsBindableStaticNameExpression(node *Node, excludeThisKeyword bool) bool</c>。</summary>
    public static bool IsBindableStaticNameExpression(Node? node, bool excludeThisKeyword)
    {
        var n = node!;
        return IsEntityNameExpression(n) || IsBindableStaticAccessExpression(n, excludeThisKeyword);
    }

    // Does not handle signed numeric names like `a[+0]` - handling those would require handling prefix unary expressions
    // throughout late binding handling as well, which is awkward (but ultimately probably doable if there is demand)
    /// <summary>Go: <c>func GetElementOrPropertyAccessName(node *Node) *Node</c>。</summary>
    public static Node? GetElementOrPropertyAccessName(Node? node)
    {
        var n = node!;
        switch (n.Kind)
        {
            case Kind.PropertyAccessExpression:
                if (IsIdentifier(n.Name()!))
                {
                    return n.Name();
                }
                return null;
            case Kind.ElementAccessExpression:
                var arg = SkipParentheses(n.AsElementAccessExpression()!.ArgumentExpression!);
                if (IsStringOrNumericLiteralLike(arg!))
                {
                    return arg;
                }
                return null;
        }
        // 与 Go 的差异：Go 的 panic(...) → C# throw（此处需要返回值）。
        throw new DebugFailureException("Unhandled case in GetElementOrPropertyAccessName");
    }

    /// <summary>Go: <c>func GetInitializerOfBinaryExpression(expr *BinaryExpression) *Expression</c>。</summary>
    public static Node? GetInitializerOfBinaryExpression(BinaryExpression? expr)
    {
        var e = expr!;
        while (IsBinaryExpression(e.Right!))
        {
            e = e.Right!.AsBinaryExpression()!;
        }
        return e.Right!.Expression();
    }

    /// <summary>Go: <c>func IsExpressionWithTypeArgumentsInClassExtendsClause(node *Node) bool</c>。</summary>
    public static bool IsExpressionWithTypeArgumentsInClassExtendsClause(Node? node)
    {
        return TryGetClassExtendingExpressionWithTypeArguments(node) is not null;
    }

    /// <summary>Go: <c>func TryGetClassExtendingExpressionWithTypeArguments(node *Node) *ClassLikeDeclaration</c>。</summary>
    public static Node? TryGetClassExtendingExpressionWithTypeArguments(Node? node)
    {
        var n = node!;
        if (!IsExpressionWithTypeArguments(n))
        {
            return null;
        }
        var found = TryGetClassImplementingOrExtendingHeritageClauseElement(n, out var cls, out var isImplements);
        if (found && cls is not null && !isImplements)
        {
            return cls;
        }
        return null;
    }

    /// <summary>Go: <c>func TryGetClassImplementingOrExtendingHeritageClauseElement(node *Node) (class *ClassLikeDeclaration, isImplements bool)</c>。</summary>
    public static bool TryGetClassImplementingOrExtendingHeritageClauseElement(Node? node, out Node? classNode, out bool isImplements)
    {
        var n = node!;
        if ((IsExpressionWithTypeArguments(n) || IsTypeReferenceNode(n)) &&
            IsHeritageClause(n.Parent!) && IsClassLike(n.Parent!.Parent!))
        {
            classNode = n.Parent!.Parent;
            isImplements = n.Parent!.AsHeritageClause()!.Token == Kind.ImplementsKeyword;
            return true;
        }
        classNode = null;
        isImplements = false;
        return false;
    }

    /// <summary>Go: <c>func GetNameOfDeclaration(declaration *Node) *Node</c>。</summary>
    public static Node? GetNameOfDeclaration(Node? declaration)
    {
        if (declaration is null)
        {
            return null;
        }
        var nonAssignedName = GetNonAssignedNameOfDeclaration(declaration);
        if (nonAssignedName is not null)
        {
            return nonAssignedName;
        }
        if (IsFunctionExpression(declaration) || IsArrowFunction(declaration) || IsClassExpression(declaration))
        {
            return GetAssignedName(declaration);
        }
        return null;
    }

    /// <summary>Go: <c>func GetNonAssignedNameOfDeclaration(declaration *Node) *Node</c>。</summary>
    public static Node? GetNonAssignedNameOfDeclaration(Node? declaration)
    {
        var d = declaration!;
        // !!!
        switch (d.Kind)
        {
            case Kind.BinaryExpression:
            case Kind.CallExpression:
                switch (GetAssignmentDeclarationKind(d))
                {
                    case JSDeclarationKind.Property:
                    case JSDeclarationKind.ThisProperty:
                    case JSDeclarationKind.ExportsProperty:
                        var left = d.AsBinaryExpression()!.Left!;
                        var name = GetElementOrPropertyAccessName(left);
                        if (name is not null)
                        {
                            return name;
                        }
                        return left;
                    case JSDeclarationKind.ObjectDefinePropertyValue:
                    case JSDeclarationKind.ObjectDefinePropertyExports:
                        return d.Arguments()![1];
                }
                return null;
            case Kind.ExportAssignment:
                var expr = d.Expression();
                if (IsIdentifier(expr!))
                {
                    return expr;
                }
                return null;
        }
        return d.Name();
    }

    /// <summary>Go: <c>func GetAssignedName(node *Node) *Node</c>。</summary>
    public static Node? GetAssignedName(Node? node)
    {
        var n = node!;
        var parent = n.Parent;
        if (parent is not null)
        {
            switch (parent.Kind)
            {
                case Kind.PropertyAssignment:
                    return parent.AsPropertyAssignment()!.Name();
                case Kind.BindingElement:
                    return parent.AsBindingElement()!.Name();
                case Kind.BinaryExpression:
                    if (n == parent.AsBinaryExpression()!.Right)
                    {
                        var left = parent.AsBinaryExpression()!.Left!;
                        switch (left.Kind)
                        {
                            case Kind.Identifier:
                                return left;
                            case Kind.PropertyAccessExpression:
                                return left.AsPropertyAccessExpression()!.Name();
                            case Kind.ElementAccessExpression:
                                var arg = SkipParentheses(left.AsElementAccessExpression()!.ArgumentExpression!);
                                if (IsStringOrNumericLiteralLike(arg!))
                                {
                                    return arg;
                                }
                                break;
                        }
                    }
                    break;
                case Kind.VariableDeclaration:
                    var declName = parent.AsVariableDeclaration()!.Name();
                    if (IsIdentifier(declName!))
                    {
                        return declName;
                    }
                    break;
            }
        }
        return null;
    }

    /// <summary>Go: <c>func GetAssignmentDeclarationKind(node *Node) JSDeclarationKind</c>。</summary>
    public static JSDeclarationKind GetAssignmentDeclarationKind(Node? node)
    {
        var n = node!;
        switch (n.Kind)
        {
            case Kind.BinaryExpression:
                var bin = n.AsBinaryExpression()!;
                if (bin.OperatorToken!.Kind == Kind.EqualsToken && IsAccessExpression(bin.Left!))
                {
                    if (IsInJSFile(bin.Left))
                    {
                        if (IsModuleExportsAccessExpression(bin.Left!) && !IsExportsIdentifier(bin.Right))
                        {
                            return JSDeclarationKind.ModuleExports;
                        }
                        if ((IsModuleExportsAccessExpression(bin.Left!.Expression()!) || IsExportsIdentifier(bin.Left!.Expression())) &&
                            GetElementOrPropertyAccessName(bin.Left) is not null)
                        {
                            return JSDeclarationKind.ExportsProperty;
                        }
                        if (bin.Left!.Expression()!.Kind == Kind.ThisKeyword)
                        {
                            return JSDeclarationKind.ThisProperty;
                        }
                    }
                    if ((bin.Left!.Kind == Kind.PropertyAccessExpression && IsEntityNameExpressionEx(bin.Left!.Expression()!, IsInJSFile(bin.Left)) && IsIdentifier(bin.Left!.Name()!)) ||
                        (bin.Left!.Kind == Kind.ElementAccessExpression && IsEntityNameExpressionEx(bin.Left!.Expression()!, IsInJSFile(bin.Left))))
                    {
                        return JSDeclarationKind.Property;
                    }
                }
                break;
            case Kind.CallExpression:
                if (IsInJSFile(n) && IsBindableObjectDefinePropertyCall(n))
                {
                    var entityName = n.Arguments()![0];
                    if (IsExportsIdentifier(entityName) || IsModuleExportsAccessExpression(entityName))
                    {
                        return JSDeclarationKind.ObjectDefinePropertyExports;
                    }
                    return JSDeclarationKind.ObjectDefinePropertyValue;
                }
                break;
        }
        return JSDeclarationKind.None;
    }

    /// <summary>Go: <c>func IsBindableObjectDefinePropertyCall(node *Node) bool</c>。</summary>
    public static bool IsBindableObjectDefinePropertyCall(Node? node)
    {
        var n = node!;
        var args = n.Arguments();
        // 与 Go 的差异：Go 的 `len(args) == 3` → C# 的 `args is { Length: 3 }`。
        if (args is { Length: 3 })
        {
            var expr = n.Expression();
            if (IsPropertyAccessExpression(expr!) &&
                IsIdentifier(expr!.Expression()!) && expr!.Expression()!.Text() == "Object" &&
                expr!.Name()!.Text() == "defineProperty" &&
                IsStringOrNumericLiteralLike(args[1]) &&
                IsBindableStaticNameExpression(args[0] /*excludeThisKeyword*/, true))
            {
                return true;
            }
        }
        return false;
    }


    /// <summary>Go: <c>func HasDynamicName(declaration *Node) bool</c>。</summary>
    public static bool HasDynamicName(Node? declaration)
    {
        var name = GetNameOfDeclaration(declaration!);
        return name != null && IsDynamicName(name);
    }

    /// <summary>Go: <c>func IsDynamicName(name *Node) bool</c>。</summary>
    public static bool IsDynamicName(Node? name)
    {
        var n = name!;
        Node? expr;
        switch (n.Kind)
        {
            case Kind.ComputedPropertyName:
                expr = n.Expression();
                break;
            case Kind.ElementAccessExpression:
                expr = SkipParentheses(n.AsElementAccessExpression()!.ArgumentExpression!);
                break;
            default:
                return false;
        }
        return !IsStringOrNumericLiteralLike(expr!) && !IsSignedNumericLiteral(expr);
    }

    /// <summary>Go: <c>func IsEntityNameExpression(node *Node) bool</c>。</summary>
    public static bool IsEntityNameExpression(Node? node)
    {
        return IsEntityNameExpressionEx(node, false /*allowJS*/);
    }

    /// <summary>Go: <c>func IsEntityNameExpressionEx(node *Node, allowJS bool) bool</c>。</summary>
    public static bool IsEntityNameExpressionEx(Node? node, bool allowJS)
    {
        var n = node!;
        return IsIdentifier(n) ||
            IsPropertyAccessEntityNameExpression(n, allowJS) ||
            allowJS && (n.Kind == Kind.ThisKeyword || IsElementAccessEntityNameExpression(n, allowJS));
    }

    /// <summary>Go: <c>func IsPropertyAccessEntityNameExpression(node *Node, allowJS bool) bool</c>。</summary>
    public static bool IsPropertyAccessEntityNameExpression(Node? node, bool allowJS)
    {
        var n = node!;
        return IsPropertyAccessExpression(n) && IsIdentifier(n.Name()!) && IsEntityNameExpressionEx(n.Expression(), allowJS);
    }

    /// <summary>Go: <c>func isElementAccessEntityNameExpression(node *Node, allowJS bool) bool</c>。</summary>
    internal static bool IsElementAccessEntityNameExpression(Node? node, bool allowJS)
    {
        var n = node!;
        return IsElementAccessExpression(n) &&
            IsStringOrNumericLiteralLike(n.AsElementAccessExpression()!.ArgumentExpression!) &&
            IsEntityNameExpressionEx(n.Expression(), allowJS);
    }

    /// <summary>Go: <c>func IsDottedName(node *Node) bool</c>。</summary>
    public static bool IsDottedName(Node? node)
    {
        var n = node!;
        switch (n.Kind)
        {
            case Kind.Identifier:
            case Kind.ThisKeyword:
            case Kind.SuperKeyword:
            case Kind.MetaProperty:
                return true;
            case Kind.PropertyAccessExpression:
            case Kind.ParenthesizedExpression:
                return IsDottedName(n.Expression());
        }
        return false;
    }

    /// <summary>Go: <c>func HasSamePropertyAccessName(node1, node2 *Node) bool</c>。</summary>
    public static bool HasSamePropertyAccessName(Node? node1, Node? node2)
    {
        var n1 = node1!;
        var n2 = node2!;
        if (n1.Kind == Kind.Identifier && n2.Kind == Kind.Identifier)
        {
            return n1.Text() == n2.Text();
        }
        else if (n1.Kind == Kind.PropertyAccessExpression && n2.Kind == Kind.PropertyAccessExpression)
        {
            return n1.AsPropertyAccessExpression()!.Name()!.Text() == n2.AsPropertyAccessExpression()!.Name()!.Text() &&
                HasSamePropertyAccessName(n1.Expression(), n2.Expression());
        }
        return false;
    }

    /// <summary>Go: <c>func IsAmbientModule(node *Node) bool</c>。</summary>
    public static bool IsAmbientModule(Node? node)
    {
        var n = node!;
        return IsModuleDeclaration(n) && (n.AsModuleDeclaration()!.Name()!.Kind == Kind.StringLiteral || IsGlobalScopeAugmentation(n));
    }

    // Ambient module symbols are either of the form `"modulename"` or `InternalSymbolNamePrefix + "\"modulename\"pattern@nodeId"`;
    // see `getDeclarationName`.
    /// <summary>Go: <c>func TryGetAmbientModuleNameFromSymbolName(s string) (string, bool)</c>。</summary>
    public static bool TryGetAmbientModuleNameFromSymbolName(string s, out string result)
    {
        if (HasPrefix(s, "\"", true) && HasSuffix(s, "\"", true))
        {
            result = s[1..^1];
            return true;
        }

        var patternPrefix = InternalSymbolNamePrefix + "\"";
        if (!HasPrefix(s, patternPrefix, true))
        {
            result = "";
            return false;
        }

        var rest = s[patternPrefix.Length..];
        var markerIndex = rest.LastIndexOf("\"pattern@", StringComparison.Ordinal);
        if (markerIndex < 1)
        {
            result = "";
            return false;
        }
        result = rest[..markerIndex];
        return true;
    }

    /// <summary>Go: <c>func IsExternalModule(file *SourceFile) bool</c>。</summary>
    public static bool IsExternalModule(SourceFile? file)
    {
        var f = file!;
        return f.ExternalModuleIndicator != null;
    }

    /// <summary>Go: <c>func IsExternalOrCommonJSModule(file *SourceFile) bool</c>。</summary>
    public static bool IsExternalOrCommonJSModule(SourceFile? file)
    {
        var f = file!;
        // 依赖 SourceFile.CommonJSModuleIndicator（Go ast.go:2494；C# 侧 SourceFile.cs 尚未落地该字段）。
        return f.ExternalModuleIndicator != null || f.CommonJSModuleIndicator != null;
    }

    // TODO: Should we deprecate `IsExternalOrCommonJSModule` in favor of this function?
    /// <summary>Go: <c>func IsEffectiveExternalModule(node *SourceFile, compilerOptions *core.CompilerOptions) bool</c>。</summary>
    public static bool IsEffectiveExternalModule(SourceFile? node, CompilerOptions? compilerOptions)
    {
        var n = node!;
        var options = compilerOptions!;
        // 依赖 SourceFile.CommonJSModuleIndicator（Go ast.go:2494；C# 侧 SourceFile.cs 尚未落地该字段）。
        return IsExternalModule(n) ||
            (IsCommonJSContainingModuleKind(options.GetEmitModuleKind()) && n.CommonJSModuleIndicator != null);
    }

    /// <summary>Go: <c>func isCommonJSContainingModuleKind(kind core.ModuleKind) bool</c>。</summary>
    internal static bool IsCommonJSContainingModuleKind(ModuleKind kind)
    {
        return kind == ModuleKind.CommonJS || ModuleKind.Node16 <= kind && kind <= ModuleKind.NodeNext;
    }

    /// <summary>Go: <c>func IsExternalModuleIndicator(node *Statement) bool</c>。</summary>
    public static bool IsExternalModuleIndicator(Node? node)
    {
        var n = node!;
        // Exported top-level member indicates moduleness
        return IsAnyImportOrReExport(n) || IsExportAssignment(n) || HasSyntacticModifier(n, ModifierFlags.Export);
    }

    /// <summary>Go: <c>func IsExportNamespaceAsDefaultDeclaration(node *Node) bool</c>。</summary>
    public static bool IsExportNamespaceAsDefaultDeclaration(Node? node)
    {
        var n = node!;
        if (IsExportDeclaration(n))
        {
            var decl = n.AsExportDeclaration()!;
            var exportClause = decl.ExportClause!;
            return IsNamespaceExport(exportClause) && ModuleExportNameIsDefault(exportClause.Name()!);
        }
        return false;
    }

    /// <summary>Go: <c>func IsGlobalScopeAugmentation(node *Node) bool</c>。</summary>
    public static bool IsGlobalScopeAugmentation(Node? node)
    {
        var n = node!;
        return IsModuleDeclaration(n) && n.AsModuleDeclaration()!.Keyword == Kind.GlobalKeyword;
    }

    /// <summary>Go: <c>func IsModuleAugmentationExternal(node *Node) bool</c>。</summary>
    public static bool IsModuleAugmentationExternal(Node? node)
    {
        var n = node!;
        // external module augmentation is a ambient module declaration that is either:
        // - defined in the top level scope and source file is an external module
        // - defined inside ambient module declaration located in the top level scope and source file not an external module
        var parent = n.Parent!;
        switch (parent.Kind)
        {
            case Kind.SourceFile:
                return IsExternalModule(parent.AsSourceFile());
            case Kind.ModuleBlock:
                var grandParent = parent.Parent!;
                // 与 Go 的差异：Go 的 grandParent.Parent 用局部变量提前取出（C# 的可空流分析无法复用 Go 的隐式解引用）。
                var grandParentParent = grandParent.Parent!;
                return IsAmbientModule(grandParent) &&
                    IsSourceFile(grandParentParent) &&
                    !IsExternalModule(grandParentParent.AsSourceFile());
        }
        return false;
    }

    /// <summary>Go: <c>func IsModuleWithStringLiteralName(node *Node) bool</c>。</summary>
    public static bool IsModuleWithStringLiteralName(Node? node)
    {
        var n = node!;
        return IsModuleDeclaration(n) && n.Name()!.Kind == Kind.StringLiteral;
    }

    /// <summary>Go: <c>func GetContainingClass(node *Node) *Node</c>。</summary>
    public static Node? GetContainingClass(Node? node)
    {
        var n = node!;
        return FindAncestor(n.Parent, candidate => IsClassLike(candidate));
    }

    /// <summary>Go: <c>func GetExtendsHeritageClauseElements(node *Node) []*HeritageClauseElement</c>。</summary>
    public static Node[] GetExtendsHeritageClauseElements(Node? node)
    {
        return GetHeritageElements(node, Kind.ExtendsKeyword);
    }

    /// <summary>Go: <c>func GetImplementsHeritageClauseElements(node *Node) []*HeritageClauseElement</c>。</summary>
    public static Node[] GetImplementsHeritageClauseElements(Node? node)
    {
        return GetHeritageElements(node, Kind.ImplementsKeyword);
    }

    /// <summary>Go: <c>func GetHeritageElements(node *Node, kind Kind) []*HeritageClauseElement</c>。</summary>
    public static Node[] GetHeritageElements(Node? node, Kind kind)
    {
        var n = node!;
        var clause = GetHeritageClause(n, kind);
        if (clause != null)
        {
            return clause.AsHeritageClause()!.Types!.Nodes ?? [];
        }
        return [];
    }

    // GetHeritageClauseElementName returns the expression or type name of a heritage clause element.
    /// <summary>Go: <c>func GetHeritageClauseElementName(node *HeritageClauseElement) *Node</c>。</summary>
    public static Node? GetHeritageClauseElementName(Node? node)
    {
        var n = node!;
        if (IsTypeReferenceNode(n))
        {
            return n.AsTypeReferenceNode()!.TypeName;
        }
        return n.AsExpressionWithTypeArguments()!.Expression;
    }

    /// <summary>Go: <c>func IsNameOfHeritageClauseTypeReference(node *Node) bool</c>。</summary>
    public static bool IsNameOfHeritageClauseTypeReference(Node? node)
    {
        var n = node!;
        while (IsQualifiedName(n.Parent!))
        {
            n = n.Parent!;
        }
        var parent = n.Parent!;
        return IsTypeReferenceNode(parent) &&
            parent.AsTypeReferenceNode()!.TypeName == n &&
            IsHeritageClause(parent.Parent!);
    }

    /// <summary>Go: <c>func GetHeritageClause(node *Node, kind Kind) *Node</c>。</summary>
    public static Node? GetHeritageClause(Node? node, Kind kind)
    {
        var n = node!;
        var clauses = GetHeritageClauses(n);
        if (clauses != null)
        {
            foreach (var clause in clauses.Nodes ?? [])
            {
                if (clause.AsHeritageClause()!.Token == kind)
                {
                    return clause;
                }
            }
        }
        return null;
    }

    /// <summary>Go: <c>func getHeritageClauses(node *Node) *NodeList</c>。</summary>
    internal static NodeList? GetHeritageClauses(Node? node)
    {
        var n = node!;
        switch (n.Kind)
        {
            case Kind.ClassDeclaration:
                return n.AsClassDeclaration()!.HeritageClauses;
            case Kind.ClassExpression:
                return n.AsClassExpression()!.HeritageClauses;
            case Kind.InterfaceDeclaration:
                return n.AsInterfaceDeclaration()!.HeritageClauses;
        }
        return null;
    }

    /// <summary>Go: <c>func IsPartOfTypeQuery(node *Node) bool</c>。</summary>
    public static bool IsPartOfTypeQuery(Node? node)
    {
        while (node!.Kind == Kind.QualifiedName || node.Kind == Kind.Identifier)
        {
            node = node.Parent;
        }
        return node!.Kind == Kind.TypeQuery;
    }

    // This function returns true if the this node's root declaration is a parameter.
    // For example, passing a `ParameterDeclaration` will return true, as will passing a
    // binding element that is a child of a `ParameterDeclaration`.
    //
    // If you are looking to test that a `Node` is a `ParameterDeclaration`, use `isParameter`.
    /// <summary>Go: <c>func IsPartOfParameterDeclaration(node *Node) bool</c>。</summary>
    public static bool IsPartOfParameterDeclaration(Node? node)
    {
        return GetRootDeclaration(node!)!.Kind == Kind.Parameter;
    }

    /// <summary>Go: <c>func IsInTopLevelContext(node *Node) bool</c>。</summary>
    public static bool IsInTopLevelContext(Node? node)
    {
        // The name of a class or function declaration is a BindingIdentifier in its surrounding scope.
        if (IsIdentifier(node!))
        {
            var parent = node!.Parent;
            if ((IsClassDeclaration(parent!) || IsFunctionDeclaration(parent!)) && parent!.Name() == node)
            {
                node = parent;
            }
        }
        var container = GetThisContainer(node, true /*includeArrowFunctions*/, false /*includeClassComputedPropertyName*/);
        return IsSourceFile(container);
    }

    /// <summary>Go: <c>func GetThisContainer(node *Node, includeArrowFunctions bool, includeClassComputedPropertyName bool) *Node</c>。</summary>
    public static Node GetThisContainer(Node? node, bool includeArrowFunctions, bool includeClassComputedPropertyName)
    {
        while (true)
        {
            node = node!.Parent;
            if (node == null)
            {
                throw new DebugFailureException("nil parent in getThisContainer");
            }
            switch (node.Kind)
            {
                case Kind.ComputedPropertyName:
                    if (includeClassComputedPropertyName && IsClassLike(node.Parent!.Parent!))
                    {
                        return node;
                    }
                    node = node.Parent!.Parent;
                    break;
                case Kind.Decorator:
                {
                    var parent = node.Parent!;
                    if (parent.Kind == Kind.Parameter && IsClassElement(parent.Parent!))
                    {
                        // If the decorator's parent is a ParameterDeclaration, we resolve the this container from
                        // the grandparent class declaration.
                        node = parent.Parent;
                    }
                    else if (IsClassElement(parent))
                    {
                        // If the decorator's parent is a class element, we resolve the 'this' container
                        // from the parent class declaration.
                        node = parent;
                    }
                    break;
                }
                case Kind.ArrowFunction:
                    if (includeArrowFunctions)
                    {
                        return node;
                    }
                    break;
                case Kind.FunctionDeclaration:
                case Kind.FunctionExpression:
                case Kind.ModuleDeclaration:
                case Kind.ClassStaticBlockDeclaration:
                case Kind.PropertyDeclaration:
                case Kind.PropertySignature:
                case Kind.MethodDeclaration:
                case Kind.MethodSignature:
                case Kind.Constructor:
                case Kind.GetAccessor:
                case Kind.SetAccessor:
                case Kind.CallSignature:
                case Kind.ConstructSignature:
                case Kind.IndexSignature:
                case Kind.EnumDeclaration:
                case Kind.SourceFile:
                    return node;
            }
        }
    }

    /// <summary>Go: <c>func GetSuperContainer(node *Node, stopOnFunctions bool) *Node</c>。</summary>
    public static Node? GetSuperContainer(Node? node, bool stopOnFunctions)
    {
        for (node = node!.Parent; node != null; node = node!.Parent)
        {
            switch (node.Kind)
            {
                case Kind.ComputedPropertyName:
                    node = node.Parent;
                    break;
                case Kind.FunctionDeclaration:
                case Kind.FunctionExpression:
                case Kind.ArrowFunction:
                    if (!stopOnFunctions)
                    {
                        continue;
                    }
                    return node;
                case Kind.PropertyDeclaration:
                case Kind.PropertySignature:
                case Kind.MethodDeclaration:
                case Kind.MethodSignature:
                case Kind.Constructor:
                case Kind.GetAccessor:
                case Kind.SetAccessor:
                case Kind.ClassStaticBlockDeclaration:
                    return node;
                case Kind.Decorator:
                {
                    // Decorators are always applied outside of the body of a class or method.
                    var parent = node.Parent!;
                    if (parent.Kind == Kind.Parameter && IsClassElement(parent.Parent!))
                    {
                        // If the decorator's parent is a ParameterDeclaration, we resolve the this container from
                        // the grandparent class declaration.
                        node = parent.Parent;
                    }
                    else if (IsClassElement(parent))
                    {
                        // If the decorator's parent is a class element, we resolve the 'this' container
                        // from the parent class declaration.
                        node = parent;
                    }
                    break;
                }
            }
        }
        return null;
    }

    /// <summary>Go: <c>func GetImmediatelyInvokedFunctionExpression(fn *Node) *Node</c>。</summary>
    public static Node? GetImmediatelyInvokedFunctionExpression(Node? fn)
    {
        if (IsFunctionExpressionOrArrowFunction(fn!))
        {
            var prev = fn;
            var parent = fn!.Parent;
            while (IsParenthesizedExpression(parent!))
            {
                prev = parent;
                parent = parent!.Parent;
            }
            if (IsCallExpression(parent!) && parent!.Expression() == prev)
            {
                return parent;
            }
        }
        return null;
    }

    /// <summary>Go: <c>func IsEnumConst(node *Node) bool</c>。</summary>
    public static bool IsEnumConst(Node? node)
    {
        return (GetCombinedModifierFlags(node!) & ModifierFlags.Const) != 0;
    }

    /// <summary>Go: <c>func ExpressionIsAlias(node *Node) bool</c>。</summary>
    public static bool ExpressionIsAlias(Node? node)
    {
        return IsEntityNameExpression(node) || IsClassExpression(node!);
    }

    /// <summary>Go: <c>func IsInstanceOfExpression(node *Node) bool</c>。</summary>
    public static bool IsInstanceOfExpression(Node? node)
    {
        var n = node!;
        return IsBinaryExpression(n) && n.AsBinaryExpression()!.OperatorToken!.Kind == Kind.InstanceOfKeyword;
    }

    /// <summary>Go: <c>func IsAnyImportOrReExport(node *Node) bool</c>。</summary>
    public static bool IsAnyImportOrReExport(Node? node)
    {
        return IsImportNode(node) || IsExportDeclaration(node!);
    }

    /// <summary>Go: <c>func IsImportNode(node *Node) bool</c>。</summary>
    public static bool IsImportNode(Node? node)
    {
        return IsAnyImportSyntax(node) || NodeKindIs(node!, Kind.JSImportDeclaration);
    }

    // Checks if the node is a genuine import declation. In particular the re-parsed KindJSImportDeclaration
    // is explicitly excluded because the callers of this function are typically not prepared to handle it properly.
    // For more permissive check, use IsImportNode.
    /// <summary>Go: <c>func IsAnyImportSyntax(node *Node) bool</c>。</summary>
    public static bool IsAnyImportSyntax(Node? node)
    {
        return NodeKindIs(node!, Kind.ImportDeclaration, Kind.ImportEqualsDeclaration);
    }

    /// <summary>Go: <c>func IsJsonSourceFile(file *SourceFile) bool</c>。</summary>
    public static bool IsJsonSourceFile(SourceFile? file)
    {
        var f = file!;
        return f.ScriptKind == ScriptKind.JSON;
    }

    /// <summary>Go: <c>func IsInJsonFile(node *Node) bool</c>。</summary>
    public static bool IsInJsonFile(Node? node)
    {
        var n = node!;
        return (n.Flags & NodeFlags.JsonFile) != 0;
    }

    /// <summary>Go: <c>func GetExternalModuleName(node *Node) *Expression</c>。</summary>
    public static Node? GetExternalModuleName(Node? node)
    {
        var n = node!;
        switch (n.Kind)
        {
            case Kind.ImportDeclaration:
            case Kind.JSImportDeclaration:
            case Kind.ExportDeclaration:
                return n.ModuleSpecifier();
            case Kind.ImportEqualsDeclaration:
                var moduleReference = n.AsImportEqualsDeclaration()!.ModuleReference;
                if (moduleReference!.Kind == Kind.ExternalModuleReference)
                {
                    return moduleReference.Expression();
                }
                return null;
            case Kind.ImportType:
                return GetImportTypeNodeLiteral(n);
            case Kind.CallExpression:
                return FirstOrNil(n.Arguments());
            case Kind.ModuleDeclaration:
                var moduleSpecifier = n.AsModuleDeclaration()!.Name();
                if (IsStringLiteral(moduleSpecifier!))
                {
                    return moduleSpecifier;
                }
                return null;
        }
        throw new DebugFailureException("Unhandled case in getExternalModuleName");
    }

    /// <summary>Go: <c>func HasImportAttributes(node *Node) bool</c>。</summary>
    public static bool HasImportAttributes(Node? node)
    {
        switch (node!.Kind)
        {
            case Kind.ImportDeclaration:
            case Kind.JSImportDeclaration:
            case Kind.ExportDeclaration:
            case Kind.ImportType:
                return true;
        }
        return false;
    }

    /// <summary>Go: <c>func GetImportAttributes(node *Node) *Node</c>。</summary>
    public static Node? GetImportAttributes(Node? node)
    {
        var n = node!;
        switch (n.Kind)
        {
            case Kind.ImportDeclaration:
            case Kind.JSImportDeclaration:
                return n.AsImportDeclaration()!.Attributes;
            case Kind.ExportDeclaration:
                return n.AsExportDeclaration()!.Attributes;
            case Kind.ImportType:
                return n.AsImportTypeNode()!.Attributes;
        }
        throw new DebugFailureException("Unhandled case in getImportAttributes: " + n.Kind.String());
    }

    /// <summary>Go: <c>func getImportTypeNodeLiteral(node *Node) *Node</c>。</summary>
    internal static Node? GetImportTypeNodeLiteral(Node? node)
    {
        var n = node!;
        if (IsImportTypeNode(n))
        {
            var importTypeNode = n.AsImportTypeNode()!;
            var argument = importTypeNode.Argument!;
            if (IsLiteralTypeNode(argument))
            {
                var literalTypeNode = argument.AsLiteralTypeNode()!;
                if (IsStringLiteral(literalTypeNode.Literal!))
                {
                    return literalTypeNode.Literal;
                }
            }
        }
        return null;
    }

    /// <summary>Go: <c>func IsExpressionNode(node *Node) bool</c>。</summary>
    public static bool IsExpressionNode(Node? node)
    {
        switch (node!.Kind)
        {
            case Kind.SuperKeyword:
            case Kind.NullKeyword:
            case Kind.TrueKeyword:
            case Kind.FalseKeyword:
            case Kind.RegularExpressionLiteral:
            case Kind.ArrayLiteralExpression:
            case Kind.ObjectLiteralExpression:
            case Kind.PropertyAccessExpression:
            case Kind.ElementAccessExpression:
            case Kind.CallExpression:
            case Kind.NewExpression:
            case Kind.TaggedTemplateExpression:
            case Kind.AsExpression:
            case Kind.TypeAssertionExpression:
            case Kind.SatisfiesExpression:
            case Kind.NonNullExpression:
            case Kind.ParenthesizedExpression:
            case Kind.FunctionExpression:
            case Kind.ClassExpression:
            case Kind.ArrowFunction:
            case Kind.VoidExpression:
            case Kind.DeleteExpression:
            case Kind.TypeOfExpression:
            case Kind.PrefixUnaryExpression:
            case Kind.PostfixUnaryExpression:
            case Kind.BinaryExpression:
            case Kind.ConditionalExpression:
            case Kind.SpreadElement:
            case Kind.TemplateExpression:
            case Kind.OmittedExpression:
            case Kind.JsxElement:
            case Kind.JsxSelfClosingElement:
            case Kind.JsxFragment:
            case Kind.YieldExpression:
            case Kind.AwaitExpression:
                return true;
            case Kind.MetaProperty:
                // `import.defer` in `import.defer(...)` is not an expression
                return !IsImportCall(node.Parent!) || node.Parent!.Expression() != node;
            case Kind.ExpressionWithTypeArguments:
                return !IsHeritageClause(node.Parent!);
            case Kind.QualifiedName:
            {
                while (node!.Parent!.Kind == Kind.QualifiedName)
                {
                    node = node.Parent;
                }
                var parent = node!.Parent!;
                return IsTypeQueryNode(parent) || IsJSDocLinkLike(parent) || IsJSDocNameReference(parent) || IsJsxTagName(node!);
            }
            case Kind.PrivateIdentifier:
            {
                var parent = node!.Parent;
                return IsBinaryExpression(parent!) &&
                    parent!.AsBinaryExpression()!.Left == node &&
                    parent.AsBinaryExpression()!.OperatorToken!.Kind == Kind.InKeyword;
            }
            case Kind.Identifier:
            {
                var parent = node.Parent!;
                if (IsTypeQueryNode(parent) || IsJSDocLinkLike(parent) || IsJSDocNameReference(parent) || IsJsxTagName(node))
                {
                    return true;
                }
                goto case Kind.NumericLiteral;
            }
            case Kind.NumericLiteral:
            case Kind.BigIntLiteral:
            case Kind.StringLiteral:
            case Kind.NoSubstitutionTemplateLiteral:
            case Kind.ThisKeyword:
                return IsInExpressionContext(node);
            default:
                return false;
        }
    }

    /// <summary>Go: <c>func IsInExpressionContext(node *Node) bool</c>。</summary>
    public static bool IsInExpressionContext(Node? node)
    {
        var parent = node!.Parent!;
        switch (parent.Kind)
        {
            case Kind.VariableDeclaration:
            case Kind.Parameter:
            case Kind.PropertyDeclaration:
            case Kind.PropertySignature:
            case Kind.EnumMember:
            case Kind.PropertyAssignment:
            case Kind.BindingElement:
                return parent.Initializer() == node;
            case Kind.ExpressionStatement:
            case Kind.IfStatement:
            case Kind.DoStatement:
            case Kind.WhileStatement:
            case Kind.ReturnStatement:
            case Kind.WithStatement:
            case Kind.SwitchStatement:
            case Kind.CaseClause:
            case Kind.DefaultClause:
            case Kind.ThrowStatement:
            case Kind.TypeAssertionExpression:
            case Kind.AsExpression:
            case Kind.TemplateSpan:
            case Kind.ComputedPropertyName:
            case Kind.SatisfiesExpression:
                return parent.Expression() == node;
            case Kind.ForStatement:
            {
                var s = parent.AsForStatement()!;
                return s.Initializer == node && s.Initializer!.Kind != Kind.VariableDeclarationList ||
                    s.Condition == node ||
                    s.Incrementor == node;
            }
            case Kind.ForInStatement:
            case Kind.ForOfStatement:
            {
                var s = parent.AsForInOrOfStatement()!;
                return s.Initializer == node && s.Initializer!.Kind != Kind.VariableDeclarationList ||
                    s.Expression == node;
            }
            case Kind.Decorator:
            case Kind.JsxExpression:
            case Kind.JsxSpreadAttribute:
            case Kind.SpreadAssignment:
                return true;
            case Kind.ExpressionWithTypeArguments:
                return parent.Expression() == node && !IsPartOfTypeNode(parent);
            case Kind.ShorthandPropertyAssignment:
                return parent.AsShorthandPropertyAssignment()!.ObjectAssignmentInitializer == node;
            default:
                return IsExpressionNode(parent);
        }
    }

    /// <summary>Go: <c>func IsPartOfTypeNode(node *Node) bool</c>。</summary>
    public static bool IsPartOfTypeNode(Node? node)
    {
        var n = node!;
        var kind = n.Kind;
        if (kind >= Kind.FirstTypeNode && kind <= Kind.LastTypeNode)
        {
            return true;
        }
        switch (n.Kind)
        {
            case Kind.AnyKeyword:
            case Kind.UnknownKeyword:
            case Kind.NumberKeyword:
            case Kind.BigIntKeyword:
            case Kind.StringKeyword:
            case Kind.BooleanKeyword:
            case Kind.SymbolKeyword:
            case Kind.ObjectKeyword:
            case Kind.UndefinedKeyword:
            case Kind.NullKeyword:
            case Kind.NeverKeyword:
                return true;
            case Kind.VoidKeyword:
                return n.Parent!.Kind != Kind.VoidExpression;
            case Kind.ExpressionWithTypeArguments:
                return IsPartOfTypeExpressionWithTypeArguments(n);
            case Kind.TypeParameter:
            {
                var typeParameterParent = n.Parent!;
                return typeParameterParent.Kind == Kind.MappedType || typeParameterParent.Kind == Kind.InferType;
            }
            case Kind.Identifier:
            {
                var parent = n.Parent;
                if (IsQualifiedName(parent!) && parent!.AsQualifiedName()!.Right == n)
                {
                    return IsPartOfTypeNodeInParent(parent);
                }
                if (IsPropertyAccessExpression(parent!) && parent!.AsPropertyAccessExpression()!.Name() == n)
                {
                    return IsPartOfTypeNodeInParent(parent);
                }
                return IsPartOfTypeNodeInParent(n);
            }
            case Kind.QualifiedName:
            case Kind.PropertyAccessExpression:
            case Kind.ThisKeyword:
                return IsPartOfTypeNodeInParent(n);
        }
        return false;
    }

    /// <summary>Go: <c>func isPartOfTypeNodeInParent(node *Node) bool</c>。</summary>
    internal static bool IsPartOfTypeNodeInParent(Node? node)
    {
        var n = node!;
        var parent = n.Parent!;
        if (parent.Kind == Kind.TypeQuery)
        {
            return false;
        }
        if (parent.Kind == Kind.ImportType)
        {
            return !parent.AsImportTypeNode()!.IsTypeOf;
        }

        // Do not recursively call isPartOfTypeNode on the parent. In the example:
        //
        //     let a: A.B.C;
        //
        // Calling isPartOfTypeNode would consider the qualified name A.B a type node.
        // Only C and A.B.C are type nodes.
        if (parent.Kind >= Kind.FirstTypeNode && parent.Kind <= Kind.LastTypeNode)
        {
            return true;
        }
        switch (parent.Kind)
        {
            case Kind.ExpressionWithTypeArguments:
                return IsPartOfTypeExpressionWithTypeArguments(parent);
            case Kind.TypeParameter:
                return n == parent.AsTypeParameterDeclaration()!.Constraint;
            case Kind.VariableDeclaration:
            case Kind.Parameter:
            case Kind.PropertyDeclaration:
            case Kind.PropertySignature:
            case Kind.FunctionDeclaration:
            case Kind.FunctionExpression:
            case Kind.ArrowFunction:
            case Kind.Constructor:
            case Kind.MethodDeclaration:
            case Kind.MethodSignature:
            case Kind.GetAccessor:
            case Kind.SetAccessor:
            case Kind.CallSignature:
            case Kind.ConstructSignature:
            case Kind.IndexSignature:
            case Kind.TypeAssertionExpression:
                return n == parent.Type();
            case Kind.CallExpression:
            case Kind.NewExpression:
            case Kind.TaggedTemplateExpression:
                var typeArguments = parent.TypeArguments();
                return typeArguments != null && Array.IndexOf(typeArguments, n) >= 0;
        }
        return false;
    }

    /// <summary>Go: <c>func isPartOfTypeExpressionWithTypeArguments(node *Node) bool</c>。</summary>
    internal static bool IsPartOfTypeExpressionWithTypeArguments(Node? node)
    {
        var n = node!;
        var parent = n.Parent!;
        return IsHeritageClause(parent) && (!IsClassLike(parent.Parent!) || parent.AsHeritageClause()!.Token == Kind.ImplementsKeyword) ||
            IsJSDocImplementsTag(parent) ||
            IsJSDocAugmentsTag(parent);
    }


    /// <summary>Go: <c>func isPartOfTypeExpressionWithTypeArguments(node *Node) bool</c>。</summary>
    /// <summary>Go: <c>func IsJSDocLinkLike(node *Node) bool</c>。</summary>
    public static bool IsJSDocLinkLike(Node? node) => NodeKindIs(node!, Kind.JSDocLink, Kind.JSDocLinkCode, Kind.JSDocLinkPlain);

    /// <summary>Go: <c>func IsJSDocTag(node *Node) bool</c>。</summary>
    public static bool IsJSDocTag(Node? node)
    {
        var n = node!;
        return n.Kind >= Kind.FirstJSDocTagNode && n.Kind <= Kind.LastJSDocTagNode;
    }

    /// <summary>Go: <c>func IsSuperCall(node *Node) bool</c>。</summary>
    public static bool IsSuperCall(Node? node)
    {
        var n = node!;
        return IsCallExpression(n) && n.Expression()!.Kind == Kind.SuperKeyword;
    }

    /// <summary>Go: <c>func IsImportCall(node *Node) bool</c>。</summary>
    public static bool IsImportCall(Node? node)
    {
        var n = node!;
        if (!IsCallExpression(n))
        {
            return false;
        }

        var e = n.Expression()!;
        return e.Kind == Kind.ImportKeyword || IsMetaProperty(e) && e.AsMetaProperty()!.KeywordToken == Kind.ImportKeyword && e.Text() == "defer";
    }

    /// <summary>Go: <c>func IsComputedNonLiteralName(name *Node) bool</c>。</summary>
    public static bool IsComputedNonLiteralName(Node? name)
    {
        var n = name!;
        return IsComputedPropertyName(n) && !IsStringOrNumericLiteralLike(n.Expression()!);
    }

    /// <summary>Go: <c>func IsQuestionToken(node *Node) bool</c>。</summary>
    public static bool IsQuestionToken(Node? node) => node != null && node.Kind == Kind.QuestionToken;

    /// <summary>Go: <c>func EntityNameToString(name *Node, getTextOfNode func(*Node) string) string</c>。</summary>
    public static string EntityNameToString(Node? name, Func<Node, string> getTextOfNode)
    {
        var n = name!;
        switch (n.Kind)
        {
            case Kind.ThisKeyword:
                return "this";
            case Kind.Identifier:
            case Kind.PrivateIdentifier:
                if (NodeIsSynthesized(n) || getTextOfNode == null)
                {
                    return n.Text();
                }

                return getTextOfNode(n);
            case Kind.QualifiedName:
                {
                    // 与 Go 的差异：Go 这里用 `+` 拼串，C# 按拼接约定改用 StringBuilder（结果相同）。
                    var qualifiedName = n.AsQualifiedName()!;
                    var builder = new StringBuilder();
                    builder.Append(EntityNameToString(qualifiedName.Left, getTextOfNode));
                    builder.Append('.');
                    builder.Append(EntityNameToString(qualifiedName.Right, getTextOfNode));
                    return builder.ToString();
                }

            case Kind.PropertyAccessExpression:
                {
                    var builder = new StringBuilder();
                    builder.Append(EntityNameToString(n.Expression(), getTextOfNode));
                    builder.Append('.');
                    builder.Append(EntityNameToString(n.AsPropertyAccessExpression()!.Name(), getTextOfNode));
                    return builder.ToString();
                }

            case Kind.JsxNamespacedName:
                {
                    var jsxNamespacedName = n.AsJsxNamespacedName()!;
                    var builder = new StringBuilder();
                    builder.Append(EntityNameToString(jsxNamespacedName.Namespace, getTextOfNode));
                    builder.Append(':');
                    builder.Append(EntityNameToString(jsxNamespacedName.Name(), getTextOfNode));
                    return builder.ToString();
                }
        }

        throw new DebugFailureException("Unhandled case in EntityNameToString");
    }

    /// <summary>Go: <c>func GetTextOfPropertyName(name *Node) string</c>。</summary>
    public static string GetTextOfPropertyName(Node? name)
    {
        TryGetTextOfPropertyName(name, out string text);
        return text;
    }

    /// <summary>Go: <c>func TryGetTextOfPropertyName(name *Node) (string, bool)</c>。</summary>
    public static bool TryGetTextOfPropertyName(Node? name, out string result)
    {
        var n = name!;
        switch (n.Kind)
        {
            case Kind.Identifier:
            case Kind.PrivateIdentifier:
            case Kind.StringLiteral:
            case Kind.NumericLiteral:
            case Kind.BigIntLiteral:
            case Kind.NoSubstitutionTemplateLiteral:
                result = n.Text();
                return true;
            case Kind.ComputedPropertyName:
                if (IsStringOrNumericLiteralLike(n.Expression()!))
                {
                    result = n.Expression()!.Text();
                    return true;
                }

                break;
            case Kind.JsxNamespacedName:
                result = n.AsJsxNamespacedName()!.Namespace!.Text() + ":" + n.Name()!.Text();
                return true;
        }

        result = "";
        return false;
    }

    /// <summary>Go: <c>func IsJSDocNode(node *Node) bool</c>。</summary>
    public static bool IsJSDocNode(Node? node)
    {
        var n = node!;
        return n.Kind >= Kind.FirstJSDocNode && n.Kind <= Kind.LastJSDocNode;
    }

    /// <summary>Go: <c>func IsNonWhitespaceToken(node *Node) bool</c>。</summary>
    public static bool IsNonWhitespaceToken(Node? node)
    {
        var n = node!;
        return IsTokenKind(n.Kind) && !IsWhitespaceOnlyJsxText(n);
    }

    /// <summary>Go: <c>func IsWhitespaceOnlyJsxText(node *Node) bool</c>。</summary>
    public static bool IsWhitespaceOnlyJsxText(Node? node)
    {
        var n = node!;
        return n.Kind == Kind.JsxText && n.AsJsxText()!.ContainsOnlyTriviaWhiteSpaces;
    }

    /// <summary>Go: <c>func GetNewTargetContainer(node *Node) *Node</c>。</summary>
    public static Node? GetNewTargetContainer(Node? node)
    {
        var n = node!;
        Node? container = GetThisContainer(n, false /*includeArrowFunctions*/, false /*includeClassComputedPropertyName*/);
        if (container != null)
        {
            switch (container.Kind)
            {
                case Kind.Constructor:
                case Kind.FunctionDeclaration:
                case Kind.FunctionExpression:
                    return container;
            }
        }

        return null;
    }

    /// <summary>Go: <c>func GetEnclosingBlockScopeContainer(node *Node) *Node</c>。</summary>
    public static Node? GetEnclosingBlockScopeContainer(Node? node)
    {
        var n = node!;
        return FindAncestor(n.Parent, current => IsBlockScope(current, current.Parent));
    }

    /// <summary>Go: <c>func IsBlockScope(node *Node, parentNode *Node) bool</c>。</summary>
    public static bool IsBlockScope(Node? node, Node? parentNode)
    {
        var n = node!;
        switch (n.Kind)
        {
            case Kind.SourceFile:
            case Kind.CaseBlock:
            case Kind.CatchClause:
            case Kind.ModuleDeclaration:
            case Kind.ForStatement:
            case Kind.ForInStatement:
            case Kind.ForOfStatement:
            case Kind.Constructor:
            case Kind.MethodDeclaration:
            case Kind.GetAccessor:
            case Kind.SetAccessor:
            case Kind.FunctionDeclaration:
            case Kind.FunctionExpression:
            case Kind.ArrowFunction:
            case Kind.PropertyDeclaration:
            case Kind.ClassStaticBlockDeclaration:
                return true;
            case Kind.Block:
                // function block is not considered block-scope container
                // see comment in binder.ts: bind(...), case for SyntaxKind.Block
                return !IsFunctionLikeOrClassStaticBlockDeclaration(parentNode);
        }

        return false;
    }

    /// <summary>Go: <c>type SemanticMeaning int32</c>；Go 的常量组 Go:2243-2247。</summary>

    /// <summary>Go: <c>func GetMeaningFromDeclaration(node *Node) SemanticMeaning</c>。</summary>
    public static SemanticMeaning GetMeaningFromDeclaration(Node? node)
    {
        var n = node!;
        switch (n.Kind)
        {
            case Kind.VariableDeclaration:
                return SemanticMeaning.Value;
            case Kind.Parameter:
            case Kind.BindingElement:
            case Kind.PropertyDeclaration:
            case Kind.PropertySignature:
            case Kind.PropertyAssignment:
            case Kind.ShorthandPropertyAssignment:
            case Kind.MethodDeclaration:
            case Kind.MethodSignature:
            case Kind.Constructor:
            case Kind.GetAccessor:
            case Kind.SetAccessor:
            case Kind.FunctionDeclaration:
            case Kind.FunctionExpression:
            case Kind.ArrowFunction:
            case Kind.CatchClause:
            case Kind.JsxAttribute:
                return SemanticMeaning.Value;

            case Kind.TypeParameter:
            case Kind.InterfaceDeclaration:
            case Kind.TypeAliasDeclaration:
            case Kind.JSTypeAliasDeclaration:
            case Kind.TypeLiteral:
                return SemanticMeaning.Type;
            case Kind.EnumMember:
            case Kind.ClassDeclaration:
                return SemanticMeaning.Value | SemanticMeaning.Type;

            case Kind.ModuleDeclaration:
                if (IsAmbientModule(n))
                {
                    return SemanticMeaning.Namespace | SemanticMeaning.Value;
                }
                else if (GetModuleInstanceState(n) == ModuleInstanceState.Instantiated)
                {
                    return SemanticMeaning.Namespace | SemanticMeaning.Value;
                }
                else
                {
                    return SemanticMeaning.Namespace;
                }

            case Kind.EnumDeclaration:
            case Kind.NamedImports:
            case Kind.ImportSpecifier:
            case Kind.ImportEqualsDeclaration:
            case Kind.ImportDeclaration:
            case Kind.JSImportDeclaration:
            case Kind.ExportAssignment:
            case Kind.ExportDeclaration:
                return SemanticMeaning.All;

            // An external module can be a Value
            case Kind.SourceFile:
                return SemanticMeaning.Namespace | SemanticMeaning.Value;
        }

        return SemanticMeaning.All;
    }

    /// <summary>Go: <c>func IsPropertyAccessOrQualifiedName(node *Node) bool</c>。</summary>
    public static bool IsPropertyAccessOrQualifiedName(Node? node)
    {
        var n = node!;
        return n.Kind == Kind.PropertyAccessExpression || n.Kind == Kind.QualifiedName;
    }

    /// <summary>Go: <c>func IsLabelName(node *Node) bool</c>。</summary>
    public static bool IsLabelName(Node? node) => IsLabelOfLabeledStatement(node) || IsJumpStatementTarget(node);

    /// <summary>Go: <c>func IsLabelOfLabeledStatement(node *Node) bool</c>。</summary>
    public static bool IsLabelOfLabeledStatement(Node? node)
    {
        var n = node!;
        if (!IsIdentifier(n))
        {
            return false;
        }

        if (!IsLabeledStatement(n.Parent!))
        {
            return false;
        }

        return n == n.Parent!.Label();
    }

    /// <summary>Go: <c>func IsJumpStatementTarget(node *Node) bool</c>。</summary>
    public static bool IsJumpStatementTarget(Node? node)
    {
        var n = node!;
        if (!IsIdentifier(n))
        {
            return false;
        }

        if (!IsBreakOrContinueStatement(n.Parent))
        {
            return false;
        }

        return n == n.Parent!.Label();
    }

    /// <summary>Go: <c>func IsBreakOrContinueStatement(node *Node) bool</c>。</summary>
    public static bool IsBreakOrContinueStatement(Node? node) => NodeKindIs(node!, Kind.BreakStatement, Kind.ContinueStatement);

    // GetModuleInstanceState is used during binding as well as in transformations and tests, and therefore may be invoked
    // with a node that does not yet have its `Parent` pointer set. In this case, an `ancestors` represents a stack of
    // virtual `Parent` pointers that can be used to walk up the tree. Since `getModuleInstanceStateForAliasTarget` may
    // potentially walk up out of the provided `Node`, merely setting the parent pointers for a given `ModuleDeclaration`
    // prior to invoking `GetModuleInstanceState` is not sufficient. It is, however, necessary that the `Parent` pointers
    // for all ancestors of the `Node` provided to `GetModuleInstanceState` have been set.

    /// <summary>Go: <c>func pushAncestor(ancestors []*Node, parent *Node) []*Node</c>。</summary>
    internal static List<Node> PushAncestor(List<Node>? ancestors, Node? parent)
    {
        // 与 Go 的差异：Go 的 append 可能复用底层数组，但不会改变调用者切片的长度（调用者看不到压入的元素）；
        // C# 的 List.Add 会让调用者看到。为保持语义（否则兄弟子树会把栈项留在列表里、popAncestor 会弹错），
        // 这里返回新列表（拷贝 + Add）。
        var result = ancestors == null ? new List<Node>() : new List<Node>(ancestors);
        result.Add(parent!);
        return result;
    }

    /// <summary>Go: <c>func popAncestor(ancestors []*Node, node *Node) ([]*Node, *Node)</c>。</summary>
    internal static (List<Node>? ancestors, Node? parent) PopAncestor(List<Node>? ancestors, Node? node)
    {
        if (ancestors == null || ancestors.Count == 0)
        {
            // Go: return nil, node.Parent
            return (null, node!.Parent);
        }

        // 与 Go 的差异：Go 返回 `ancestors[:n]`（同一底层数组的视图）；C# 就地 RemoveAt（列表始终由本调用链独占，
        // 见 PushAncestor 的说明），观察行为与 Go 一致。
        int n = ancestors.Count - 1;
        Node parent = ancestors[n];
        ancestors.RemoveAt(n);
        return (ancestors, parent);
    }

    /// <summary>Go: <c>type ModuleInstanceState int32</c>；Go 的常量组 Go:2365-2369（iota = 0..3）。</summary>

    /// <summary>Go: <c>func GetModuleInstanceState(node *Node) ModuleInstanceState</c>。</summary>
    public static ModuleInstanceState GetModuleInstanceState(Node? node)
    {
        return GetModuleInstanceState(node, null, null);
    }

    /// <summary>Go: <c>func getModuleInstanceState(node *Node, ancestors []*Node, visited map[NodeId]ModuleInstanceState) ModuleInstanceState</c>。</summary>
    internal static ModuleInstanceState GetModuleInstanceState(Node? node, List<Node>? ancestors, Dictionary<NodeId, ModuleInstanceState>? visited)
    {
        var n = node!;
        var module = n.AsModuleDeclaration()!;
        Node? body = module.Body;
        if (body != null)
        {
            return GetModuleInstanceStateCached(body, PushAncestor(ancestors, n), visited);
        }
        else
        {
            return ModuleInstanceState.Instantiated;
        }
    }

    /// <summary>Go: <c>func getModuleInstanceStateCached(node *Node, ancestors []*Node, visited map[NodeId]ModuleInstanceState) ModuleInstanceState</c>。</summary>
    internal static ModuleInstanceState GetModuleInstanceStateCached(Node? node, List<Node>? ancestors, Dictionary<NodeId, ModuleInstanceState>? visited)
    {
        var n = node!;
        if (visited == null)
        {
            visited = new Dictionary<NodeId, ModuleInstanceState>();
        }

        NodeId nodeId = GetNodeId(n);
        if (visited.TryGetValue(nodeId, out ModuleInstanceState cached))
        {
            if (cached != ModuleInstanceState.Unknown)
            {
                return cached;
            }

            return ModuleInstanceState.NonInstantiated;
        }

        visited[nodeId] = ModuleInstanceState.Unknown;
        ModuleInstanceState result = GetModuleInstanceStateWorker(n, ancestors, visited);
        visited[nodeId] = result;
        return result;
    }

    /// <summary>Go: <c>func getModuleInstanceStateWorker(node *Node, ancestors []*Node, visited map[NodeId]ModuleInstanceState) ModuleInstanceState</c>。</summary>
    internal static ModuleInstanceState GetModuleInstanceStateWorker(Node? node, List<Node>? ancestors, Dictionary<NodeId, ModuleInstanceState>? visited)
    {
        var n = node!;
        // A module is uninstantiated if it contains only
        switch (n.Kind)
        {
            case Kind.InterfaceDeclaration:
            case Kind.TypeAliasDeclaration:
            case Kind.JSTypeAliasDeclaration:
                return ModuleInstanceState.NonInstantiated;
            case Kind.EnumDeclaration:
                if (IsEnumConst(n))
                {
                    return ModuleInstanceState.ConstEnumOnly;
                }

                break;
            case Kind.ImportDeclaration:
            case Kind.JSImportDeclaration:
            case Kind.ImportEqualsDeclaration:
                if (!HasSyntacticModifier(n, ModifierFlags.Export))
                {
                    return ModuleInstanceState.NonInstantiated;
                }

                break;
            case Kind.ExportDeclaration:
                {
                    var decl = n.AsExportDeclaration()!;
                    Node? moduleSpecifier = decl.ModuleSpecifier;
                    Node? exportClause = decl.ExportClause;
                    if (moduleSpecifier == null && exportClause != null && exportClause.Kind == Kind.NamedExports)
                    {
                        ModuleInstanceState state = ModuleInstanceState.NonInstantiated;
                        ancestors = PushAncestor(ancestors, n);
                        ancestors = PushAncestor(ancestors, exportClause);
                        foreach (Node specifier in exportClause.Elements() ?? [])
                        {
                            ModuleInstanceState specifierState = GetModuleInstanceStateForAliasTarget(specifier, ancestors, visited);
                            if (specifierState > state)
                            {
                                state = specifierState;
                            }

                            if (state == ModuleInstanceState.Instantiated)
                            {
                                return state;
                            }
                        }

                        return state;
                    }

                    break;
                }

            case Kind.ModuleBlock:
                {
                    ModuleInstanceState state = ModuleInstanceState.NonInstantiated;
                    ancestors = PushAncestor(ancestors, n);
                    n.ForEachChild(child =>
                    {
                        ModuleInstanceState childState = GetModuleInstanceStateCached(child, ancestors, visited);
                        switch (childState)
                        {
                            case ModuleInstanceState.NonInstantiated:
                                return false;
                            case ModuleInstanceState.ConstEnumOnly:
                                state = ModuleInstanceState.ConstEnumOnly;
                                return false;
                            case ModuleInstanceState.Instantiated:
                                state = ModuleInstanceState.Instantiated;
                                return true;
                        }

                        throw new DebugFailureException("Unhandled case in getModuleInstanceStateWorker");
                    });
                    return state;
                }

            case Kind.ModuleDeclaration:
                return GetModuleInstanceState(n, ancestors, visited);
        }

        return ModuleInstanceState.Instantiated;
    }

    /// <summary>Go: <c>func getModuleInstanceStateForAliasTarget(node *Node, ancestors []*Node, visited map[NodeId]ModuleInstanceState) ModuleInstanceState</c>。</summary>
    internal static ModuleInstanceState GetModuleInstanceStateForAliasTarget(Node? node, List<Node>? ancestors, Dictionary<NodeId, ModuleInstanceState>? visited)
    {
        var n = node!;
        Node? name = n.PropertyNameOrName();
        // Go: name.Kind（name 为 nil 时 Go 会 panic，这里同样不做检查）
        var nameNode = name!;
        if (nameNode.Kind != Kind.Identifier)
        {
            // Skip for invalid syntax like this: export { "x" }
            return ModuleInstanceState.Instantiated;
        }

        // 与 Go 的差异：Go 的 `for ancestors, p := popAncestor(...); p != nil; ancestors, p = popAncestor(...)`
        // 在 C# 里写成 while（Go 那种 init/post 声明式 for 无对应写法），栈本身放在局部变量里原地更新。
        (List<Node>? currentAncestors, Node? p) = PopAncestor(ancestors, n);
        while (p != null)
        {
            if (IsBlock(p) || IsModuleBlock(p) || IsSourceFile(p))
            {
                ModuleInstanceState found = ModuleInstanceState.Unknown;
                List<Node> statementsAncestors = PushAncestor(currentAncestors, p);
                foreach (Node statement in p.Statements() ?? [])
                {
                    if (NodeHasName(statement, nameNode))
                    {
                        ModuleInstanceState state = GetModuleInstanceStateCached(statement, statementsAncestors, visited);
                        if (found == ModuleInstanceState.Unknown || state > found)
                        {
                            found = state;
                        }

                        if (found == ModuleInstanceState.Instantiated)
                        {
                            return found;
                        }

                        if (statement.Kind == Kind.ImportEqualsDeclaration)
                        {
                            // Treat re-exports of import aliases as instantiated since they're ambiguous. This is consistent
                            // with `export import x = mod.x` being treated as instantiated:
                            //   import x = mod.x;
                            //   export { x };
                            found = ModuleInstanceState.Instantiated;
                        }
                    }
                }

                if (found != ModuleInstanceState.Unknown)
                {
                    return found;
                }
            }

            (currentAncestors, p) = PopAncestor(currentAncestors, p);
        }

        // Couldn't locate, assume could refer to a value
        return ModuleInstanceState.Instantiated;
    }

    /// <summary>Go: <c>func IsInstantiatedModule(node *Node, preserveConstEnums bool) bool</c>。</summary>
    public static bool IsInstantiatedModule(Node? node, bool preserveConstEnums)
    {
        ModuleInstanceState moduleState = GetModuleInstanceState(node!);
        return moduleState == ModuleInstanceState.Instantiated ||
            (preserveConstEnums && moduleState == ModuleInstanceState.ConstEnumOnly);
    }

    /// <summary>Go: <c>func NodeHasName(statement *Node, id *Node) bool</c>。</summary>
    public static bool NodeHasName(Node? statement, Node? id)
    {
        var statementNode = statement!;
        var idNode = id!;
        Node? name = statementNode.Name();
        if (name != null)
        {
            return IsIdentifier(name) && name.Text() == idNode.Text();
        }

        if (IsVariableStatement(statementNode))
        {
            Node[]? declarations = statementNode.AsVariableStatement()!.DeclarationList!.AsVariableDeclarationList()!.Declarations!.Nodes;
            return Some(declarations, d => NodeHasName(d, idNode));
        }

        return false;
    }

    /// <summary>Go: <c>func IsInternalModuleImportEqualsDeclaration(node *Node) bool</c>。</summary>
    public static bool IsInternalModuleImportEqualsDeclaration(Node? node)
    {
        var n = node!;
        return IsImportEqualsDeclaration(n) && n.AsImportEqualsDeclaration()!.ModuleReference!.Kind != Kind.ExternalModuleReference;
    }

    /// <summary>Go: <c>func IsConstAssertion(node *Node) bool</c>。</summary>
    public static bool IsConstAssertion(Node? node)
    {
        var n = node!;
        switch (n.Kind)
        {
            case Kind.AsExpression:
            case Kind.TypeAssertionExpression:
                return IsConstTypeReference(n.Type()!);
        }

        return false;
    }

    /// <summary>Go: <c>func IsConstTypeReference(node *Node) bool</c>。</summary>
    public static bool IsConstTypeReference(Node? node)
    {
        var n = node!;
        return IsTypeReferenceNode(n) && (n.TypeArguments()?.Length ?? 0) == 0 && IsIdentifier(n.AsTypeReferenceNode()!.TypeName!) && n.AsTypeReferenceNode()!.TypeName!.Text() == "const";
    }

    /// <summary>Go: <c>func IsGlobalSourceFile(node *Node) bool</c>。</summary>
    public static bool IsGlobalSourceFile(Node? node)
    {
        var n = node!;
        return n.Kind == Kind.SourceFile && !IsExternalOrCommonJSModule(n.AsSourceFile()!);
    }

    /// <summary>Go: <c>func IsParameterLike(node *Node) bool</c>。</summary>
    public static bool IsParameterLike(Node? node)
    {
        var n = node!;
        switch (n.Kind)
        {
            case Kind.Parameter:
            case Kind.TypeParameter:
                return true;
        }

        return false;
    }

    /// <summary>Go: <c>func GetDeclarationOfKind(symbol *Symbol, kind Kind) *Node</c>。</summary>
    public static Node? GetDeclarationOfKind(Symbol? symbol, Kind kind)
    {
        var s = symbol!;
        Node[]? declarations = s.Declarations;
        if (declarations != null)
        {
            foreach (Node declaration in declarations)
            {
                if (declaration.Kind == kind)
                {
                    return declaration;
                }
            }
        }

        return null;
    }

    /// <summary>Go: <c>func FindConstructorDeclaration(node *ClassLikeDeclaration) *Node</c>。</summary>
    public static Node? FindConstructorDeclaration(Node? node)
    {
        var n = node!;
        Node[]? members = n.Members();
        if (members != null)
        {
            foreach (Node member in members)
            {
                if (IsConstructorDeclaration(member) && NodeIsPresent(member.Body()))
                {
                    return member;
                }
            }
        }

        return null;
    }

    /// <summary>Go: <c>func GetFirstIdentifier(node *Node) *Node</c>。</summary>
    public static Node? GetFirstIdentifier(Node? node)
    {
        var n = node!;
        switch (n.Kind)
        {
            case Kind.Identifier:
                return n;
            case Kind.QualifiedName:
                return GetFirstIdentifier(n.AsQualifiedName()!.Left);
            case Kind.PropertyAccessExpression:
                return GetFirstIdentifier(n.AsPropertyAccessExpression()!.Expression);
        }

        throw new DebugFailureException("Unhandled case in GetFirstIdentifier");
    }

    /// <summary>Go: <c>func GetNamespaceDeclarationNode(node *Node) *Node</c>。</summary>
    public static Node? GetNamespaceDeclarationNode(Node? node)
    {
        var n = node!;
        switch (n.Kind)
        {
            case Kind.ImportDeclaration:
            case Kind.JSImportDeclaration:
                Node? importClause = n.ImportClause();
                if (importClause != null)
                {
                    ImportClause? clause = importClause.AsImportClause();
                    Node? namedBindings = clause?.NamedBindings;
                    if (namedBindings != null && IsNamespaceImport(namedBindings))
                    {
                        return namedBindings;
                    }
                }

                break;
            case Kind.ImportEqualsDeclaration:
                return n;
            case Kind.ExportDeclaration:
                Node? exportClause = n.AsExportDeclaration()!.ExportClause;
                if (exportClause != null && IsNamespaceExport(exportClause))
                {
                    return exportClause;
                }

                break;
            default:
                throw new DebugFailureException("Unhandled case in getNamespaceDeclarationNode");
        }

        return null;
    }

    /// <summary>Go: <c>func ModuleExportNameIsDefault(node *Node) bool</c>。</summary>
    public static bool ModuleExportNameIsDefault(Node? node)
    {
        var n = node!;
        return n.Text() == InternalSymbolNameDefault;
    }

    /// <summary>Go: <c>func IsDefaultImport(node *Node /*ImportDeclaration | ImportEqualsDeclaration | ExportDeclaration*/) bool</c>。</summary>
    public static bool IsDefaultImport(Node? node)
    {
        var n = node!;
        switch (n.Kind)
        {
            case Kind.ImportDeclaration:
            case Kind.JSImportDeclaration:
                Node? importClause = n.ImportClause();
                return importClause != null && importClause.AsImportClause()!._name != null;
        }

        return false;
    }


    /// <summary>Go: <c>func GetImpliedNodeFormatForFile(path string, packageJsonType string) core.ModuleKind</c>。</summary>
    public static ModuleKind GetImpliedNodeFormatForFile(string path, string packageJsonType)
    {
        var impliedNodeFormat = ResolutionModeNone;
        if (FileExtensionIsOneOf(path, [ExtensionDmts, ExtensionMts, ExtensionMjs]))
        {
            impliedNodeFormat = ResolutionModeESM;
        }
        else if (FileExtensionIsOneOf(path, [ExtensionDcts, ExtensionCts, ExtensionCjs]))
        {
            impliedNodeFormat = ResolutionModeCommonJS;
        }
        else if (FileExtensionIsOneOf(path, [ExtensionDts, ExtensionTs, ExtensionTsx, ExtensionJs, ExtensionJsx]))
        {
            impliedNodeFormat = IfElse(packageJsonType == "module", ResolutionModeESM, ResolutionModeCommonJS);
        }

        return impliedNodeFormat;
    }

    /// <summary>Go: <c>func GetEmitModuleFormatOfFileWorker(fileName string, options *core.CompilerOptions, sourceFileMetaData SourceFileMetaData) core.ModuleKind</c>。</summary>
    public static ModuleKind GetEmitModuleFormatOfFileWorker(
        string fileName,
        CompilerOptions? options,
        SourceFileMetaData sourceFileMetaData)
    {
        var opts = options!;
        var result = GetImpliedNodeFormatForEmitWorker(fileName, opts.GetEmitModuleKind(), sourceFileMetaData);
        if (result != ModuleKind.None)
        {
            return result;
        }

        return opts.GetEmitModuleKind();
    }


/// <summary>Go: <c>func GetImpliedNodeFormatForEmitWorker(fileName string, emitModuleKind core.ModuleKind, sourceFileMetaData SourceFileMetaData) core.ResolutionMode</c>。</summary>
    public static ModuleKind GetImpliedNodeFormatForEmitWorker(
        string fileName,
        ModuleKind emitModuleKind,
        SourceFileMetaData sourceFileMetaData)
    {
        if (ModuleKind.Node16 <= emitModuleKind && emitModuleKind <= ModuleKind.NodeNext)
        {
            return sourceFileMetaData.ImpliedNodeFormat;
        }

        if (sourceFileMetaData.ImpliedNodeFormat == ModuleKind.CommonJS &&
            (sourceFileMetaData.PackageJsonType == "commonjs" ||
                TSPath.TSPath.FileExtensionIsOneOf(fileName, [TSPath.TSPath.ExtensionCjs, TSPath.TSPath.ExtensionCts])))
        {
            return ModuleKind.CommonJS;
        }

        if (sourceFileMetaData.ImpliedNodeFormat == ModuleKind.ESNext &&
            (sourceFileMetaData.PackageJsonType == "module" ||
                TSPath.TSPath.FileExtensionIsOneOf(fileName, [TSPath.TSPath.ExtensionMjs, TSPath.TSPath.ExtensionMts])))
        {
            return ModuleKind.ESNext;
        }

        return ModuleKind.None;
    }


    /// <summary>Go: <c>func GetDeclarationContainer(node *Node) *Node</c>。</summary>
    public static Node? GetDeclarationContainer(Node? node)
    {
        return FindAncestor(GetRootDeclaration(node!), n => n.Kind switch
        {
            Kind.VariableDeclaration or
            Kind.VariableDeclarationList or
            Kind.ImportSpecifier or
            Kind.NamedImports or
            Kind.NamespaceImport or
            Kind.ImportClause => false,
            _ => true,
        })!.Parent;
    }

    /// <summary>Go: <c>func IsNonLocalAlias(symbol *Symbol, excludes SymbolFlags) bool</c>。</summary>
    // Indicates that a symbol is an alias that does not merge with a local declaration.
    // OR Is a JSContainer which may merge an alias with a local declaration
    public static bool IsNonLocalAlias(Symbol? symbol, SymbolFlags excludes)
    {
        if (symbol is null)
        {
            return false;
        }

        return (symbol.Flags & (SymbolFlags.Alias | excludes)) == SymbolFlags.Alias ||
            (symbol.Flags & SymbolFlags.Alias) != 0 && (symbol.Flags & SymbolFlags.Assignment) != 0;
    }

    /// <summary>Go: <c>func IsAliasSymbolDeclaration(node *Node) bool</c>。</summary>
    // An alias symbol is created by one of the following declarations:
    //
    //	import <symbol> = ...
    //	const <symbol> = ... (JS only)
    //	const { <symbol>, ... } = ... (JS only)
    //	import <symbol> from ...
    //	import * as <symbol> from ...
    //	import { x as <symbol> } from ...
    //	export { x as <symbol> } from ...
    //	export * as ns <symbol> from ...
    //	export = <EntityNameExpression>
    //	export default <EntityNameExpression>
    //	module.exports = <EntityNameExpression> (JS only)
    //	module.exports.<symbol> = <EntityNameExpression> (JS only)
    //	exports.<symbol> = <EntityNameExpression> (JS only)
    public static bool IsAliasSymbolDeclaration(Node? node)
    {
        var n = node!;
        switch (n.Kind)
        {
            case Kind.ImportEqualsDeclaration:
            case Kind.NamespaceExportDeclaration:
            case Kind.NamespaceImport:
            case Kind.NamespaceExport:
            case Kind.ImportSpecifier:
            case Kind.ExportSpecifier:
                return true;
            case Kind.ImportClause:
                return n.AsImportClause()!.Name() is not null;
            case Kind.ExportAssignment:
                return ExpressionIsAlias(n.Expression()!);
            case Kind.VariableDeclaration:
            case Kind.BindingElement:
                return IsVariableDeclarationInitializedToRequire(n);
            case Kind.BinaryExpression:
                switch (GetAssignmentDeclarationKind(n))
                {
                    case JSDeclarationKind.ModuleExports:
                    case JSDeclarationKind.ExportsProperty:
                        return ExpressionIsAlias(n.AsBinaryExpression()!.Right!);
                }

                break;
        }

        return false;
    }

    /// <summary>Go: <c>func IsParseTreeNode(node *Node) bool</c>。</summary>
    public static bool IsParseTreeNode(Node? node) => (node!.Flags & NodeFlags.Synthesized) == 0;

    /// <summary>Go: <c>func GetNodeAtPosition(file *SourceFile, position int, includeJSDoc bool) *Node</c>。</summary>
    // Returns a token if position is in [start-of-leading-trivia, end), includes JSDoc only if requested
    public static Node? GetNodeAtPosition(SourceFile? file, int position, bool includeJSDoc)
    {
        var sourceFile = file!;
        var current = sourceFile.AsNode();
        while (true)
        {
            Node? child = null;
            if (includeJSDoc)
            {
                foreach (var jsdoc in current.JSDoc(sourceFile) ?? [])
                {
                    if (NodeContainsPosition(jsdoc, position))
                    {
                        child = jsdoc;
                        break;
                    }
                }
            }

            if (child is null)
            {
                current.ForEachChild(node =>
                {
                    if (NodeContainsPosition(node, position))
                    {
                        child = node;
                        return true;
                    }

                    return false;
                });
            }

            if (child is null || IsMetaProperty(child!))
            {
                return current;
            }

            current = child!;
        }
    }

    /// <summary>Go: <c>func nodeContainsPosition(node *Node, position int) bool</c>。</summary>
    internal static bool NodeContainsPosition(Node? node, int position)
    {
        var n = node!;
        return n.Kind >= Kind.FirstNode &&
            n.Pos() <= position &&
            (position < n.End() || position == n.End() && n.Kind == Kind.EndOfFile);
    }

    /// <summary>Go: <c>func findImportOrRequire(text string, start int) (index int, size int)</c>。</summary>
    internal static (int Index, int Size) FindImportOrRequire(string text, int start)
    {
        var index = Math.Max(start, 0);
        var n = text.Length;
        var size = 0;
        while (index < n)
        {
            var next = text.AsSpan(index).IndexOfAny('i', 'r');
            if (next < 0)
            {
                break;
            }

            index += next;

            string expected;
            if (text[index] == 'i')
            {
                size = 6;
                expected = "import";
            }
            else
            {
                size = 7;
                expected = "require";
            }

            if (index + size <= n && text.AsSpan(index, size).SequenceEqual(expected))
            {
                return (index, size);
            }

            index++;
        }

        return (-1, 0);
    }

    /// <summary>Go: <c>func ForEachDynamicImportOrRequireCall(file *SourceFile, includeTypeSpaceImports bool, requireStringLiteralLikeArgument bool, cb func(node *Node, argument *Expression) bool) bool</c>。</summary>
    public static bool ForEachDynamicImportOrRequireCall(
        SourceFile? file,
        bool includeTypeSpaceImports,
        bool requireStringLiteralLikeArgument,
        Func<Node, Node?, bool> cb)
    {
        var sourceFile = file!;
        var isJavaScriptFile = IsInJSFile(sourceFile.AsNode());
        var (lastIndex, size) = FindImportOrRequire(sourceFile.Text(), 0);
        while (lastIndex >= 0)
        {
            var node = GetNodeAtPosition(sourceFile, lastIndex, isJavaScriptFile && includeTypeSpaceImports);
            if (isJavaScriptFile && IsRequireCall(node, requireStringLiteralLikeArgument))
            {
                if (cb(node!, node!.Arguments()![0]))
                {
                    return true;
                }
            }
            else if (IsImportCall(node!) &&
                node!.Arguments() is { Length: > 0 } args &&
                (!requireStringLiteralLikeArgument || IsStringLiteralLike(args[0])))
            {
                if (cb(node!, args[0]))
                {
                    return true;
                }
            }
            else if (includeTypeSpaceImports && IsLiteralImportTypeNode(node!))
            {
                if (cb(node!, node!.AsImportTypeNode()!.Argument!.AsLiteralTypeNode()!.Literal))
                {
                    return true;
                }
            }

            // skip past import/require
            lastIndex += size;
            (lastIndex, size) = FindImportOrRequire(sourceFile.Text(), lastIndex);
        }

        return false;
    }

    /// <summary>Go: <c>func IsRequireCall(node *Node, requireStringLiteralLikeArgument bool) bool</c>。</summary>
    // Returns true if the node is a CallExpression to the identifier 'require' with
    // exactly one argument (of the form 'require("name")').
    // This function does not test if the node is in a JavaScript file or not.
    public static bool IsRequireCall(Node? node, bool requireStringLiteralLikeArgument)
    {
        if (!IsCallExpression(node!))
        {
            return false;
        }

        var call = node!.AsCallExpression()!;
        if (!IsIdentifier(call.Expression!) || call.Expression!.Text() != "require")
        {
            return false;
        }

        var arguments = call.Arguments!.Nodes ?? [];
        if (arguments.Length != 1)
        {
            return false;
        }

        return !requireStringLiteralLikeArgument || IsStringLiteralLike(arguments[0]);
    }

    /// <summary>Go: <c>func IsRequireVariableStatement(node *Node) bool</c>。</summary>
    public static bool IsRequireVariableStatement(Node? node)
    {
        if (IsVariableStatement(node!))
        {
            var declarations = node!.AsVariableStatement()!.DeclarationList!.AsVariableDeclarationList()!.Declarations!.Nodes;
            if (declarations is { Length: > 0 })
            {
                return Every(declarations, n => IsVariableDeclarationInitializedToRequire(n));
            }
        }

        return false;
    }

    /// <summary>Go: <c>func GetJSXImplicitImportBase(compilerOptions *core.CompilerOptions, file *SourceFile) string</c>。</summary>
    public static string GetJSXImplicitImportBase(CompilerOptions? compilerOptions, SourceFile? file)
    {
        var options = compilerOptions!;
        var jsxImportSourcePragma = GetPragmaFromSourceFile(file, "jsximportsource");
        var jsxRuntimePragma = GetPragmaFromSourceFile(file, "jsxruntime");
        if (GetPragmaArgument(jsxRuntimePragma, "factory") == "classic")
        {
            return "";
        }

        if (options.Jsx == JsxEmit.ReactJSX ||
            options.Jsx == JsxEmit.ReactJSXDev ||
            options.JsxImportSource != "" ||
            jsxImportSourcePragma is not null ||
            GetPragmaArgument(jsxRuntimePragma, "factory") == "automatic")
        {
            var result = GetPragmaArgument(jsxImportSourcePragma, "factory");
            if (result == "")
            {
                result = options.JsxImportSource;
            }

            if (result == "")
            {
                result = "react";
            }

            return result;
        }

        return "";
    }

    /// <summary>Go: <c>func GetJSXRuntimeImport(base string, options *core.CompilerOptions) string</c>。</summary>
    public static string GetJSXRuntimeImport(string @base, CompilerOptions? options)
    {
        if (@base == "")
        {
            return @base;
        }

        return @base + "/" + IfElse(options!.Jsx == JsxEmit.ReactJSXDev, "jsx-dev-runtime", "jsx-runtime");
    }

    /// <summary>Go: <c>func GetPragmaFromSourceFile(file *SourceFile, name string) *Pragma</c>。</summary>
    public static Pragma? GetPragmaFromSourceFile(SourceFile? file, string name)
    {
        Pragma? result = null;
        if (file?.Pragmas is { } pragmas)
        {
            for (var i = 0; i < pragmas.Length; i++)
            {
                if (pragmas[i].Name == name)
                {
                    // 与 Go 的差异：Go 取 `&file.Pragmas[i]`（切片元素地址）；C# 的 Pragma 是引用类型，
                    // 直接取元素即同一对象，别名语义一致。
                    result = pragmas[i]; // Last one wins
                }
            }
        }

        return result;
    }

    /// <summary>Go: <c>func GetPragmaArgument(pragma *Pragma, name string) string</c>。</summary>
    public static string GetPragmaArgument(Pragma? pragma, string name)
    {
        if (pragma?.Args is { } args && args.TryGetValue(name, out var arg))
        {
            return arg.Value;
        }

        return "";
    }

    /// <summary>Go: <c>func IsVariableDeclarationInitializedToRequire(node *Node) bool</c>。</summary>
    // Of the form: `const x = require("x")` or `const { x } = require("x")` or with `var` or `let`
    // The variable must not be exported and must not have a type annotation, even a jsdoc one.
    // The initializer must be a call to `require` with a string literal or a string literal-like argument.
    public static bool IsVariableDeclarationInitializedToRequire(Node? node)
    {
        var n = node!;
        if (n.Kind == Kind.BindingElement)
        {
            n = n.Parent!.Parent!;
        }

        return IsVariableDeclarationInitializedWithRequireHelper(n, false /*allowAccessedRequire*/);
    }

    /// <summary>Go: <c>func IsVariableDeclarationInitializedToBareOrAccessedRequire(node *Node) bool</c>。</summary>
    public static bool IsVariableDeclarationInitializedToBareOrAccessedRequire(Node? node) =>
        IsVariableDeclarationInitializedWithRequireHelper(node, true /*allowAccessedRequire*/);

    /// <summary>Go: <c>func isVariableDeclarationInitializedWithRequireHelper(node *Node, allowAccessedRequire bool) bool</c>。</summary>
    internal static bool IsVariableDeclarationInitializedWithRequireHelper(Node? node, bool allowAccessedRequire)
    {
        if (!IsInJSFile(node!))
        {
            return false;
        }

        var declaration = node!;
        if (declaration.Kind != Kind.VariableDeclaration)
        {
            return false;
        }

        var initializer = declaration.Initializer();
        if (initializer is null)
        {
            return false;
        }

        if (allowAccessedRequire)
        {
            initializer = GetLeftmostAccessExpression(initializer);
        }

        return (declaration.Parent!.Parent!.ModifierFlags & ModifierFlags.Export) == 0 &&
            declaration.Type() is null &&
            IsRequireCall(initializer, true /*requireStringLiteralLikeArgument*/);
    }

    /// <summary>Go: <c>func GetModuleSpecifierOfBareOrAccessedRequire(node *Node) *Node</c>。</summary>
    public static Node? GetModuleSpecifierOfBareOrAccessedRequire(Node? node)
    {
        var n = node!;
        if (IsVariableDeclarationInitializedWithRequireHelper(n, false /*allowAccessedRequire*/))
        {
            return n.Initializer()!.Arguments()![0];
        }

        if (IsVariableDeclarationInitializedWithRequireHelper(n, true /*allowAccessedRequire*/))
        {
            var leftmost = GetLeftmostAccessExpression(n.Initializer()!);
            if (IsRequireCall(leftmost, true /*requireStringLiteralLikeArgument*/))
            {
                return leftmost!.Arguments()![0];
            }
        }

        return null;
    }

    /// <summary>Go: <c>func IsModuleExportsAccessExpression(node *Node) bool</c>。</summary>
    public static bool IsModuleExportsAccessExpression(Node? node)
    {
        var n = node!;
        if (IsAccessExpression(n) && IsModuleIdentifier(n.Expression()!))
        {
            if (GetElementOrPropertyAccessName(n) is { } name)
            {
                return name.Text() == "exports";
            }
        }

        return false;
    }

    /// <summary>Go: <c>func IsModuleExportsQualifiedName(node *Node) bool</c>。</summary>
    public static bool IsModuleExportsQualifiedName(Node? node) =>
        IsQualifiedName(node!) &&
        IsModuleIdentifier(node!.AsQualifiedName()!.Left) &&
        node!.AsQualifiedName()!.Right!.Text() == "exports";

    /// <summary>Go: <c>func IsCheckJSEnabledForFile(sourceFile *SourceFile, compilerOptions *core.CompilerOptions) bool</c>。</summary>
    public static bool IsCheckJSEnabledForFile(SourceFile? sourceFile, CompilerOptions? compilerOptions)
    {
        if (sourceFile!.CheckJsDirective is { } checkJsDirective)
        {
            return checkJsDirective.Enabled;
        }

        return compilerOptions!.CheckJs == Tristate.TSTrue;
    }

    /// <summary>Go: <c>func IsPlainJSFile(file *SourceFile, checkJs core.Tristate) bool</c>。</summary>
    public static bool IsPlainJSFile(SourceFile? file, Tristate checkJs) =>
        file is not null &&
        (file.ScriptKind == ScriptKind.JS || file.ScriptKind == ScriptKind.JSX) &&
        file.CheckJsDirective is null &&
        checkJs == Tristate.TSUnknown;

    /// <summary>Go: <c>func GetLeftmostAccessExpression(expr *Node) *Node</c>。</summary>
    public static Node? GetLeftmostAccessExpression(Node? expr)
    {
        while (IsAccessExpression(expr!))
        {
            expr = expr!.Expression();
        }

        return expr;
    }

    /// <summary>Go: <c>func IsTypeOnlyImportDeclaration(node *Node) bool</c>。</summary>
    public static bool IsTypeOnlyImportDeclaration(Node? node)
    {
        var n = node!;
        switch (n.Kind)
        {
            case Kind.ImportSpecifier:
                return n.IsTypeOnly() || n.Parent!.Parent!.IsTypeOnly();
            case Kind.NamespaceImport:
                return n.Parent!.IsTypeOnly();
            case Kind.ImportClause:
            case Kind.ImportEqualsDeclaration:
                return n.IsTypeOnly();
        }

        return false;
    }

    /// <summary>Go: <c>func isTypeOnlyExportDeclaration(node *Node) bool</c>。</summary>
    internal static bool IsTypeOnlyExportDeclaration(Node? node)
    {
        var n = node!;
        switch (n.Kind)
        {
            case Kind.ExportSpecifier:
                return n.IsTypeOnly() || n.Parent!.Parent!.IsTypeOnly();
            case Kind.ExportDeclaration:
                var d = n.AsExportDeclaration()!;
                return d.IsTypeOnly && d.ModuleSpecifier is not null && d.ExportClause is null;
            case Kind.NamespaceExport:
                return n.Parent!.IsTypeOnly();
        }

        return false;
    }

    /// <summary>Go: <c>func IsTypeOnlyImportOrExportDeclaration(node *Node) bool</c>。</summary>
    public static bool IsTypeOnlyImportOrExportDeclaration(Node? node) =>
        IsTypeOnlyImportDeclaration(node) || IsTypeOnlyExportDeclaration(node);

    /// <summary>Go: <c>func IsExclusivelyTypeOnlyImportOrExport(node *Node) bool</c>。</summary>
    public static bool IsExclusivelyTypeOnlyImportOrExport(Node? node)
    {
        var n = node!;
        switch (n.Kind)
        {
            case Kind.ExportDeclaration:
                return n.IsTypeOnly();
            case Kind.ImportDeclaration:
            case Kind.JSImportDeclaration:
                if (n.ImportClause() is { } importClause)
                {
                    return importClause.AsImportClause()!.IsTypeOnly();
                }

                break;
            case Kind.JSDocImportTag:
                if (n.ImportClause() is { } jsdocImportClause)
                {
                    return jsdocImportClause.AsImportClause()!.IsTypeOnly();
                }

                break;
        }

        return false;
    }

    /// <summary>Go: <c>func GetClassLikeDeclarationOfSymbol(symbol *Symbol) *Node</c>。</summary>
    public static Node? GetClassLikeDeclarationOfSymbol(Symbol? symbol) =>
        Find(symbol!.Declarations, n => IsClassLike(n));

    /// <summary>Go: <c>func IsCallLikeExpression(node *Node) bool</c>。</summary>
    public static bool IsCallLikeExpression(Node? node)
    {
        var n = node!;
        switch (n.Kind)
        {
            case Kind.JsxOpeningElement:
            case Kind.JsxSelfClosingElement:
            case Kind.JsxOpeningFragment:
            case Kind.CallExpression:
            case Kind.NewExpression:
            case Kind.TaggedTemplateExpression:
            case Kind.Decorator:
                return true;
            case Kind.BinaryExpression:
                return n.AsBinaryExpression()!.OperatorToken!.Kind == Kind.InstanceOfKeyword;
        }

        return false;
    }

    /// <summary>Go: <c>func IsJsxCallLike(node *Node) bool</c>。</summary>
    public static bool IsJsxCallLike(Node? node)
    {
        switch (node!.Kind)
        {
            case Kind.JsxOpeningElement:
            case Kind.JsxSelfClosingElement:
            case Kind.JsxOpeningFragment:
                return true;
        }

        return false;
    }

    /// <summary>Go: <c>func IsCallLikeOrFunctionLikeExpression(node *Node) bool</c>。</summary>
    public static bool IsCallLikeOrFunctionLikeExpression(Node? node) =>
        IsCallLikeExpression(node) || IsFunctionExpressionOrArrowFunction(node!);

    /// <summary>Go: <c>func NodeHasKind(node *Node, kind Kind) bool</c>。</summary>
    public static bool NodeHasKind(Node? node, Kind kind)
    {
        if (node is null)
        {
            return false;
        }

        return node.Kind == kind;
    }

    /// <summary>Go: <c>func IsContextualKeyword(token Kind) bool</c>。</summary>
    public static bool IsContextualKeyword(Kind token) =>
        Kind.FirstContextualKeyword <= token && token <= Kind.LastContextualKeyword;

    /// <summary>Go: <c>func IsThisInTypeQuery(node *Node) bool</c>。</summary>
    public static bool IsThisInTypeQuery(Node? node)
    {
        if (!IsThisIdentifier(node!))
        {
            return false;
        }

        while (IsQualifiedName(node!.Parent!) && node!.Parent!.AsQualifiedName()!.Left == node)
        {
            node = node.Parent;
        }

        return node!.Parent!.Kind == Kind.TypeQuery;
    }

    /// <summary>Go: <c>func IsLet(node *Node) bool</c>。</summary>
    // Gets whether a bound `VariableDeclaration` or `VariableDeclarationList` is part of a `let` declaration.
    public static bool IsLet(Node? node) =>
        (GetCombinedNodeFlags(node!) & NodeFlags.BlockScoped) == NodeFlags.Let;

    /// <summary>Go: <c>func IsClassMemberModifier(token Kind) bool</c>。</summary>
    public static bool IsClassMemberModifier(Kind token) =>
        IsParameterPropertyModifier(token) ||
        token == Kind.StaticKeyword ||
        token == Kind.OverrideKeyword ||
        token == Kind.AccessorKeyword;

    /// <summary>Go: <c>func IsParameterPropertyModifier(kind Kind) bool</c>。</summary>
    public static bool IsParameterPropertyModifier(Kind kind) =>
        (ModifierToFlag(kind) & ModifierFlags.ParameterPropertyModifier) != 0;

    /// <summary>Go: <c>func ForEachChildAndJSDoc(node *Node, sourceFile *SourceFile, v Visitor) bool</c>。</summary>
    public static bool ForEachChildAndJSDoc(Node? node, SourceFile? sourceFile, Visitor v)
    {
        if (VisitNodes(v, node!.JSDoc(sourceFile)))
        {
            return true;
        }

        return node!.ForEachChild(v);
    }


    /// <summary>Go: <c>func HasTypeArguments(node *Node) bool</c>。</summary>
    public static bool HasTypeArguments(Node? node)
    {
        // Go: switch node.Kind（node 为 nil 时立即 panic）。
        switch (node!.Kind)
        {
            case Kind.CallExpression:
            case Kind.NewExpression:
            case Kind.TaggedTemplateExpression:
            case Kind.TypeReference:
            case Kind.ExpressionWithTypeArguments:
            case Kind.ImportType:
            case Kind.TypeQuery:
            case Kind.JsxOpeningElement:
            case Kind.JsxSelfClosingElement:
                return true;
        }

        return false;
    }

    /// <summary>Go: <c>func IsTypeReferenceType(node *Node) bool</c>。</summary>
    public static bool IsTypeReferenceType(Node? node)
    {
        var n = node!;
        return n.Kind == Kind.TypeReference || n.Kind == Kind.ExpressionWithTypeArguments;
    }

    /// <summary>Go: <c>func IsVariableLike(node *Node) bool</c>。</summary>
    public static bool IsVariableLike(Node? node)
    {
        switch (node!.Kind)
        {
            case Kind.BindingElement:
            case Kind.EnumMember:
            case Kind.Parameter:
            case Kind.PropertyAssignment:
            case Kind.PropertyDeclaration:
            case Kind.PropertySignature:
            case Kind.ShorthandPropertyAssignment:
            case Kind.VariableDeclaration:
                return true;
        }

        return false;
    }

    /// <summary>Go: <c>func HasInitializer(node *Node) bool</c>。</summary>
    public static bool HasInitializer(Node? node)
    {
        var n = node!;
        switch (n.Kind)
        {
            case Kind.VariableDeclaration:
            case Kind.Parameter:
            case Kind.BindingElement:
            case Kind.PropertyDeclaration:
            case Kind.PropertyAssignment:
            case Kind.EnumMember:
            case Kind.ForStatement:
            case Kind.ForInStatement:
            case Kind.ForOfStatement:
            case Kind.JsxAttribute:
                return n.Initializer() is not null;
            default:
                return false;
        }
    }

    /// <summary>Go: <c>func IsVariableParameterOrProperty(node *Node) bool</c>。</summary>
    public static bool IsVariableParameterOrProperty(Node? node)
    {
        switch (node!.Kind)
        {
            case Kind.VariableDeclaration:
            case Kind.Parameter:
            case Kind.PropertySignature:
            case Kind.PropertyDeclaration:
                return true;
            default:
                return false;
        }
    }

    /// <summary>Go: <c>func GetTypeAnnotationNode(node *Node) *TypeNode</c>。</summary>
    public static Node? GetTypeAnnotationNode(Node? node)
    {
        var n = node!;
        switch (n.Kind)
        {
            case Kind.VariableDeclaration:
            case Kind.Parameter:
            case Kind.PropertySignature:
            case Kind.PropertyDeclaration:
            case Kind.TypePredicate:
            case Kind.ParenthesizedType:
            case Kind.TypeOperator:
            case Kind.MappedType:
            case Kind.TypeAssertionExpression:
            case Kind.AsExpression:
            case Kind.SatisfiesExpression:
            case Kind.TypeAliasDeclaration:
            case Kind.JSTypeAliasDeclaration:
            case Kind.NamedTupleMember:
            case Kind.OptionalType:
            case Kind.RestType:
            case Kind.TemplateLiteralTypeSpan:
            case Kind.JSDocTypeExpression:
            case Kind.JSDocPropertyTag:
            case Kind.JSDocNullableType:
            case Kind.JSDocNonNullableType:
            case Kind.JSDocOptionalType:
                return n.Type();
            default:
                var funcLike = n.FunctionLikeData();
                if (funcLike is not null)
                {
                    return funcLike.Type;
                }

                return null;
        }
    }

    /// <summary>Go: <c>func IsObjectTypeDeclaration(node *Node) bool</c>。</summary>
    public static bool IsObjectTypeDeclaration(Node? node)
    {
        var n = node!;
        return IsClassLike(n) || IsInterfaceDeclaration(n) || IsTypeLiteralNode(n);
    }

    /// <summary>Go: <c>func IsClassOrTypeElement(node *Node) bool</c>。</summary>
    public static bool IsClassOrTypeElement(Node? node)
    {
        var n = node!;
        return IsClassElement(n) || IsTypeElement(n);
    }

    /// <summary>Go: <c>func GetClassExtendsHeritageElement(node *Node) *ExpressionWithTypeArgumentsNode</c>。</summary>
    public static Node? GetClassExtendsHeritageElement(Node? node)
    {
        var heritageElements = GetHeritageElements(node!, Kind.ExtendsKeyword);
        if (heritageElements.Length > 0)
        {
            return heritageElements[0];
        }

        return null;
    }

    /// <summary>Go: <c>func IsTypeKeywordToken(node *Node) bool</c>。</summary>
    public static bool IsTypeKeywordToken(Node? node) => node!.Kind == Kind.TypeKeyword;

    // See `IsJSDocSingleCommentNode`.
    /// <summary>Go: <c>func IsJSDocSingleCommentNodeList(nodeList *NodeList) bool</c>。</summary>
    public static bool IsJSDocSingleCommentNodeList(NodeList? nodeList)
    {
        // Go 的 nil slice 长度为 0，所以 `len(nodeList.Nodes) == 0` 与 C# 的 `Nodes is null` 是同一条分支。
        var nodes = nodeList?.Nodes;
        if (nodes is null || nodes.Length == 0)
        {
            return false;
        }

        var parent = nodes[0].Parent;
        if (parent is null)
        {
            return false;
        }

        return IsJSDocSingleCommentNode(parent) && ReferenceEquals(nodeList, parent.CommentList());
    }

    // See `IsJSDocSingleCommentNode`.
    /// <summary>Go: <c>func IsJSDocSingleCommentNodeComment(node *Node) bool</c>。</summary>
    public static bool IsJSDocSingleCommentNodeComment(Node? node)
    {
        if (node is null || node.Parent is null)
        {
            return false;
        }

        var parent = node.Parent!;
        // Go: node == node.Parent.CommentList().Nodes[0]（CommentList() 非 nil 由上一条的短路保证）。
        return IsJSDocSingleCommentNode(parent) && ReferenceEquals(node, parent.CommentList()!.Nodes![0]);
    }

    // In Strada, if a JSDoc node has a single comment, that comment is represented as a string property
    // as a simplification, and therefore that comment is not visited by `forEachChild`.
    /// <summary>Go: <c>func IsJSDocSingleCommentNode(node *Node) bool</c>。</summary>
    public static bool IsJSDocSingleCommentNode(Node? node)
    {
        var n = node!;
        // Go: hasComment(node.Kind) && node.CommentList() != nil && len(node.CommentList().Nodes) == 1
        // （保持 Go 的短路顺序：hasComment 为 false 时不调用 CommentList()；nil slice 的长度为 0，与 null 同义）。
        return HasComment(n.Kind) && n.CommentList() is { } comments && comments.Nodes is { Length: 1 };
    }

    /// <summary>Go: <c>func IsValidTypeOnlyAliasUseSite(useSite *Node) bool</c>。</summary>
    public static bool IsValidTypeOnlyAliasUseSite(Node? useSite)
    {
        var node = useSite!;
        return (node.Flags & (NodeFlags.Ambient | NodeFlags.JSDoc)) != 0 ||
            IsPartOfTypeQuery(node) ||
            IsIdentifierInNonEmittingHeritageClause(node) ||
            IsPartOfPossiblyValidTypeOrAbstractComputedPropertyName(node) ||
            !(IsExpressionNode(node) || IsShorthandPropertyNameUseSite(node));
    }

    /// <summary>Go: <c>func isIdentifierInNonEmittingHeritageClause(node *Node) bool</c>。</summary>
    internal static bool IsIdentifierInNonEmittingHeritageClause(Node? node)
    {
        var n = node!;
        if (!IsIdentifier(n))
        {
            return false;
        }

        // Go: parent := node.Parent（Parent 为 nil 时下一行的 IsPropertyAccessExpression 里立即 panic）。
        var parent = n.Parent!;
        while (IsPropertyAccessExpression(parent) || IsExpressionWithTypeArguments(parent))
        {
            parent = parent.Parent!;
        }

        return IsHeritageClause(parent) &&
            (parent.AsHeritageClause()!.Token == Kind.ImplementsKeyword || IsInterfaceDeclaration(parent.Parent!));
    }

    /// <summary>Go: <c>func isPartOfPossiblyValidTypeOrAbstractComputedPropertyName(node *Node) bool</c>。</summary>
    internal static bool IsPartOfPossiblyValidTypeOrAbstractComputedPropertyName(Node? node)
    {
        // Go 重新赋值形参 node；C# 形参是 Node?，所以用非空局部变量承载同一循环。
        var current = node!;
        while (NodeKindIs(current, Kind.Identifier, Kind.PropertyAccessExpression))
        {
            current = current.Parent!;
        }

        if (current.Kind != Kind.ComputedPropertyName)
        {
            return false;
        }

        if (HasSyntacticModifier(current.Parent!, ModifierFlags.Abstract))
        {
            return true;
        }

        return NodeKindIs(current.Parent!.Parent!, Kind.InterfaceDeclaration, Kind.TypeLiteral);
    }

    /// <summary>Go: <c>func isShorthandPropertyNameUseSite(useSite *Node) bool</c>。</summary>
    internal static bool IsShorthandPropertyNameUseSite(Node? useSite)
    {
        var node = useSite!;
        return IsIdentifier(node) && IsShorthandPropertyAssignment(node.Parent!) &&
            ReferenceEquals(node.Parent!.AsShorthandPropertyAssignment()!.Name(), node);
    }

    /// <summary>Go: <c>func GetPropertyNameForPropertyNameNode(name *Node) string</c>。</summary>
    public static string GetPropertyNameForPropertyNameNode(Node? name)
    {
        var n = name!;
        switch (n.Kind)
        {
            case Kind.Identifier:
            case Kind.PrivateIdentifier:
            case Kind.StringLiteral:
            case Kind.NoSubstitutionTemplateLiteral:
            case Kind.NumericLiteral:
            case Kind.BigIntLiteral:
            case Kind.JsxNamespacedName:
                return n.Text();
            case Kind.ComputedPropertyName:
                var nameExpression = n.Expression()!;
                if (IsStringOrNumericLiteralLike(nameExpression))
                {
                    return nameExpression.Text();
                }

                if (IsSignedNumericLiteral(nameExpression))
                {
                    var text = nameExpression.AsPrefixUnaryExpression()!.Operand!.Text();
                    if (nameExpression.AsPrefixUnaryExpression()!.Operator == Kind.MinusToken)
                    {
                        text = "-" + text;
                    }

                    return text;
                }

                return InternalSymbolNameMissing;
        }

        throw new DebugFailureException("Unhandled case in getPropertyNameForPropertyNameNode");
    }

    /// <summary>Go: <c>func IsPartOfTypeOnlyImportOrExportDeclaration(node *Node) bool</c>。</summary>
    public static bool IsPartOfTypeOnlyImportOrExportDeclaration(Node? node) =>
        FindAncestor(node, n => IsTypeOnlyImportOrExportDeclaration(n)) is not null;

    /// <summary>Go: <c>func IsEmittableImport(node *Node) bool</c>。</summary>
    public static bool IsEmittableImport(Node? node)
    {
        var n = node!;
        switch (n.Kind)
        {
            case Kind.ImportDeclaration:
                return n.ImportClause() is { } importClause && !importClause.IsTypeOnly();
            case Kind.ExportDeclaration:
            case Kind.ImportEqualsDeclaration:
                return !n.IsTypeOnly();
            case Kind.CallExpression:
                return IsImportCall(n);
        }

        return false;
    }

    /// <summary>Go: <c>func IsResolutionModeOverrideHost(node *Node) bool</c>。</summary>
    public static bool IsResolutionModeOverrideHost(Node? node)
    {
        if (node is null)
        {
            return false;
        }

        switch (node.Kind)
        {
            case Kind.ImportType:
            case Kind.ExportDeclaration:
            case Kind.ImportDeclaration:
            case Kind.JSImportDeclaration:
                return true;
        }

        return false;
    }

    /// <summary>Go: <c>func HasResolutionModeOverride(node *Node) bool</c>。</summary>
    public static bool HasResolutionModeOverride(Node? node)
    {
        if (node is null)
        {
            return false;
        }

        ImportAttributes? attributes = null;
        switch (node.Kind)
        {
            case Kind.ImportType:
                attributes = node.AsImportTypeNode()!.Attributes as ImportAttributes;
                break;
            case Kind.ImportDeclaration:
            case Kind.JSImportDeclaration:
                attributes = node.AsImportDeclaration()!.Attributes as ImportAttributes;
                break;
            case Kind.ExportDeclaration:
                attributes = node.AsExportDeclaration()!.Attributes as ImportAttributes;
                break;
        }

        if (attributes is not null)
        {
            // Go: _, ok := attributes.GetResolutionModeOverride(nil)
            // 与 Go 的差异：Go 的 `ImportAttributesNode` 是 `= Node` 别名；C# 侧落成 Ast 上的扩展方法
            // `(ModuleKind ResolutionMode, bool Ok) GetResolutionModeOverride(this Node? node, GrammarErrorOnNode? handler)`
            // （接收者可空，Go 的 `attributes == nil` 分支在里面），所以这里直接解构元组。
            var (_, ok) = attributes.GetResolutionModeOverride(null);
            return ok;
        }

        return false;
    }

    /// <summary>Go: <c>func IsStringTextContainingNode(node *Node) bool</c>。</summary>
    public static bool IsStringTextContainingNode(Node? node)
    {
        var n = node!;
        return n.Kind == Kind.StringLiteral || IsTemplateLiteralKind(n.Kind);
    }

    /// <summary>Go: <c>func IsTemplateLiteralKind(kind Kind) bool</c>。</summary>
    public static bool IsTemplateLiteralKind(Kind kind) =>
        Kind.FirstTemplateToken <= kind && kind <= Kind.LastTemplateToken;

    /// <summary>Go: <c>func IsTemplateLiteralToken(node *Node) bool</c>。</summary>
    public static bool IsTemplateLiteralToken(Node? node) => IsTemplateLiteralKind(node!.Kind);

    /// <summary>Go: <c>func GetExternalModuleImportEqualsDeclarationExpression(node *Node) *Node</c>。</summary>
    public static Node? GetExternalModuleImportEqualsDeclarationExpression(Node? node)
    {
        var n = node!;
        Debug.Assert(IsExternalModuleImportEqualsDeclaration(n));
        return n.AsImportEqualsDeclaration()!.ModuleReference!.Expression();
    }

    /// <summary>Go: <c>func CreateModifiersFromModifierFlags(flags ModifierFlags, createModifier func(kind Kind) *Node) []*Node</c>。</summary>
    public static Node[] CreateModifiersFromModifierFlags(ModifierFlags flags, Func<Kind, Node> createModifier)
    {
        var result = new List<Node>();
        if ((flags & ModifierFlags.Export) != 0)
        {
            result.Add(createModifier(Kind.ExportKeyword));
        }

        if ((flags & ModifierFlags.Ambient) != 0)
        {
            result.Add(createModifier(Kind.DeclareKeyword));
        }

        if ((flags & ModifierFlags.Default) != 0)
        {
            result.Add(createModifier(Kind.DefaultKeyword));
        }

        if ((flags & ModifierFlags.Const) != 0)
        {
            result.Add(createModifier(Kind.ConstKeyword));
        }

        if ((flags & ModifierFlags.Public) != 0)
        {
            result.Add(createModifier(Kind.PublicKeyword));
        }

        if ((flags & ModifierFlags.Private) != 0)
        {
            result.Add(createModifier(Kind.PrivateKeyword));
        }

        if ((flags & ModifierFlags.Protected) != 0)
        {
            result.Add(createModifier(Kind.ProtectedKeyword));
        }

        if ((flags & ModifierFlags.Abstract) != 0)
        {
            result.Add(createModifier(Kind.AbstractKeyword));
        }

        if ((flags & ModifierFlags.Static) != 0)
        {
            result.Add(createModifier(Kind.StaticKeyword));
        }

        if ((flags & ModifierFlags.Override) != 0)
        {
            result.Add(createModifier(Kind.OverrideKeyword));
        }

        if ((flags & ModifierFlags.Readonly) != 0)
        {
            result.Add(createModifier(Kind.ReadonlyKeyword));
        }

        if ((flags & ModifierFlags.Accessor) != 0)
        {
            result.Add(createModifier(Kind.AccessorKeyword));
        }

        if ((flags & ModifierFlags.Async) != 0)
        {
            result.Add(createModifier(Kind.AsyncKeyword));
        }

        if ((flags & ModifierFlags.In) != 0)
        {
            result.Add(createModifier(Kind.InKeyword));
        }

        if ((flags & ModifierFlags.Out) != 0)
        {
            result.Add(createModifier(Kind.OutKeyword));
        }

        return result.ToArray();
    }

    /// <summary>Go: <c>func GetThisParameter(signature *Node) *Node</c>。</summary>
    public static Node? GetThisParameter(Node? signature)
    {
        var s = signature!;
        // callback tags do not currently support this parameters
        var parameters = s.Parameters();
        if (parameters is not null && parameters.Length != 0)
        {
            var thisParameter = parameters[0];
            if (IsThisParameter(thisParameter))
            {
                return thisParameter;
            }
        }

        return null;
    }

    /// <summary>Go: <c>func ReplaceModifiers(factory *NodeFactory, node *Node, modifierArray *ModifierList) *Node</c>。</summary>
    public static Node ReplaceModifiers(NodeFactory factory, Node? node, ModifierList? modifierArray)
    {
        var n = node!;
        switch (n.Kind)
        {
            case Kind.TypeParameter:
                return factory.UpdateTypeParameterDeclaration(
                    n.AsTypeParameterDeclaration()!,
                    modifierArray,
                    n.Name(),
                    n.AsTypeParameterDeclaration()!.Constraint,
                    n.AsTypeParameterDeclaration()!.Expression,
                    n.AsTypeParameterDeclaration()!.DefaultType);
            case Kind.Parameter:
                return factory.UpdateParameterDeclaration(
                    n.AsParameterDeclaration()!,
                    modifierArray,
                    n.AsParameterDeclaration()!.DotDotDotToken,
                    n.Name(),
                    n.QuestionToken(),
                    n.Type(),
                    n.Initializer());
            case Kind.ConstructorType:
                return factory.UpdateConstructorTypeNode(
                    n.AsConstructorTypeNode()!,
                    modifierArray,
                    n.TypeParameterList(),
                    n.ParameterList(),
                    n.Type());
            case Kind.PropertySignature:
                return factory.UpdatePropertySignatureDeclaration(
                    n.AsPropertySignatureDeclaration()!,
                    modifierArray,
                    n.Name(),
                    n.PostfixToken(),
                    n.Type(),
                    n.Initializer());
            case Kind.PropertyDeclaration:
                return factory.UpdatePropertyDeclaration(
                    n.AsPropertyDeclaration()!,
                    modifierArray,
                    n.Name(),
                    n.PostfixToken(),
                    n.Type(),
                    n.Initializer());
            case Kind.MethodSignature:
                return factory.UpdateMethodSignatureDeclaration(
                    n.AsMethodSignatureDeclaration()!,
                    modifierArray,
                    n.Name(),
                    n.PostfixToken(),
                    n.TypeParameterList(),
                    n.ParameterList(),
                    n.Type());
            case Kind.MethodDeclaration:
                return factory.UpdateMethodDeclaration(
                    n.AsMethodDeclaration()!,
                    modifierArray,
                    n.AsMethodDeclaration()!.AsteriskToken,
                    n.Name(),
                    n.PostfixToken(),
                    n.TypeParameterList(),
                    n.ParameterList(),
                    n.Type(),
                    n.AsMethodDeclaration()!.FullSignature,
                    n.Body());
            case Kind.Constructor:
                return factory.UpdateConstructorDeclaration(
                    n.AsConstructorDeclaration()!,
                    modifierArray,
                    n.TypeParameterList(),
                    n.ParameterList(),
                    n.Type(),
                    n.AsConstructorDeclaration()!.FullSignature,
                    n.Body());
            case Kind.GetAccessor:
                return factory.UpdateGetAccessorDeclaration(
                    n.AsGetAccessorDeclaration()!,
                    modifierArray,
                    n.Name(),
                    n.TypeParameterList(),
                    n.ParameterList(),
                    n.Type(),
                    n.AsGetAccessorDeclaration()!.FullSignature,
                    n.Body());
            case Kind.SetAccessor:
                return factory.UpdateSetAccessorDeclaration(
                    n.AsSetAccessorDeclaration()!,
                    modifierArray,
                    n.Name(),
                    n.TypeParameterList(),
                    n.ParameterList(),
                    n.Type(),
                    n.AsSetAccessorDeclaration()!.FullSignature,
                    n.Body());
            case Kind.IndexSignature:
                return factory.UpdateIndexSignatureDeclaration(
                    n.AsIndexSignatureDeclaration()!,
                    modifierArray,
                    n.ParameterList(),
                    n.Type());
            case Kind.FunctionExpression:
                return factory.UpdateFunctionExpression(
                    n.AsFunctionExpression()!,
                    modifierArray,
                    n.AsFunctionExpression()!.AsteriskToken,
                    n.Name(),
                    n.TypeParameterList(),
                    n.ParameterList(),
                    n.Type(),
                    n.AsFunctionExpression()!.FullSignature,
                    n.Body());
            case Kind.ArrowFunction:
                return factory.UpdateArrowFunction(
                    n.AsArrowFunction()!,
                    modifierArray,
                    n.TypeParameterList(),
                    n.ParameterList(),
                    n.Type(),
                    n.AsArrowFunction()!.FullSignature,
                    n.AsArrowFunction()!.EqualsGreaterThanToken,
                    n.Body());
            case Kind.ClassExpression:
                return factory.UpdateClassExpression(
                    n.AsClassExpression()!,
                    modifierArray,
                    n.Name(),
                    n.TypeParameterList(),
                    n.AsClassExpression()!.HeritageClauses,
                    n.MemberList());
            case Kind.VariableStatement:
                return factory.UpdateVariableStatement(
                    n.AsVariableStatement()!,
                    modifierArray,
                    n.AsVariableStatement()!.DeclarationList);
            case Kind.FunctionDeclaration:
                return factory.UpdateFunctionDeclaration(
                    n.AsFunctionDeclaration()!,
                    modifierArray,
                    n.AsFunctionDeclaration()!.AsteriskToken,
                    n.Name(),
                    n.TypeParameterList(),
                    n.ParameterList(),
                    n.Type(),
                    n.AsFunctionDeclaration()!.FullSignature,
                    n.Body());
            case Kind.ClassDeclaration:
                return factory.UpdateClassDeclaration(
                    n.AsClassDeclaration()!,
                    modifierArray,
                    n.Name(),
                    n.TypeParameterList(),
                    n.AsClassDeclaration()!.HeritageClauses,
                    n.MemberList());
            case Kind.InterfaceDeclaration:
                return factory.UpdateInterfaceDeclaration(
                    n.AsInterfaceDeclaration()!,
                    modifierArray,
                    n.Name(),
                    n.TypeParameterList(),
                    n.AsInterfaceDeclaration()!.HeritageClauses,
                    n.MemberList());
            case Kind.TypeAliasDeclaration:
                return factory.UpdateTypeAliasDeclaration(
                    n.AsTypeAliasDeclaration()!,
                    modifierArray,
                    n.Name(),
                    n.TypeParameterList(),
                    n.Type());
            case Kind.EnumDeclaration:
                return factory.UpdateEnumDeclaration(
                    n.AsEnumDeclaration()!,
                    modifierArray,
                    n.Name(),
                    n.MemberList());
            case Kind.ModuleDeclaration:
                return factory.UpdateModuleDeclaration(
                    n.AsModuleDeclaration()!,
                    modifierArray,
                    n.AsModuleDeclaration()!.Keyword,
                    n.Name(),
                    n.Attributes(),
                    n.Body());
            case Kind.ImportEqualsDeclaration:
                return factory.UpdateImportEqualsDeclaration(
                    n.AsImportEqualsDeclaration()!,
                    modifierArray,
                    n.IsTypeOnly(),
                    n.Name(),
                    n.AsImportEqualsDeclaration()!.ModuleReference);
            case Kind.ImportDeclaration:
                return factory.UpdateImportDeclaration(
                    n.AsImportDeclaration()!,
                    modifierArray,
                    n.ImportClause(),
                    n.ModuleSpecifier(),
                    n.AsImportDeclaration()!.Attributes);
            case Kind.ExportAssignment:
                return factory.UpdateExportAssignment(
                    n.AsExportAssignment()!,
                    modifierArray,
                    n.AsExportAssignment()!.IsExportEquals,
                    n.Type(),
                    n.Expression());
            case Kind.ExportDeclaration:
                return factory.UpdateExportDeclaration(
                    n.AsExportDeclaration()!,
                    modifierArray,
                    n.IsTypeOnly(),
                    n.AsExportDeclaration()!.ExportClause,
                    n.ModuleSpecifier(),
                    n.AsExportDeclaration()!.Attributes);
        }

        throw new DebugFailureException($"Node that does not have modifiers tried to have modifier replaced: {(int)n.Kind}");
    }


    /// <summary>Go: <c>func IsLateVisibilityPaintedStatement(node *Node) bool</c>。</summary>
    public static bool IsLateVisibilityPaintedStatement(Node node)
    {
        switch (node.Kind)
        {
            case Kind.ImportDeclaration:
            case Kind.JSImportDeclaration:
            case Kind.ImportEqualsDeclaration:
            case Kind.VariableStatement:
            case Kind.ClassDeclaration:
            case Kind.FunctionDeclaration:
            case Kind.ModuleDeclaration:
            case Kind.TypeAliasDeclaration:
            case Kind.JSTypeAliasDeclaration:
            case Kind.InterfaceDeclaration:
            case Kind.EnumDeclaration:
                return true;
            default:
                return false;
        }
    }

    /// <summary>Go: <c>func IsExternalModuleAugmentation(node *Node) bool</c>。</summary>
    public static bool IsExternalModuleAugmentation(Node node) => IsAmbientModule(node) && IsModuleAugmentationExternal(node);

    /// <summary>Go: <c>func GetSourceFileOfModule(module *Symbol) *SourceFile</c>。</summary>
    public static SourceFile? GetSourceFileOfModule(Symbol module)
    {
        var declaration = module.ValueDeclaration;
        if (declaration == null)
        {
            declaration = GetNonAugmentationDeclaration(module);
        }
        return GetSourceFileOfNode(declaration!);
    }

    /// <summary>Go: <c>func GetNonAugmentationDeclaration(symbol *Symbol) *Node</c>。</summary>
    public static Node? GetNonAugmentationDeclaration(Symbol symbol)
    {
        return Find(symbol.Declarations, d => !IsExternalModuleAugmentation(d) && !IsGlobalScopeAugmentation(d));
    }

    /// <summary>Go: <c>func IsTypeDeclaration(node *Node) bool</c>。</summary>
    public static bool IsTypeDeclaration(Node node)
    {
        switch (node.Kind)
        {
            case Kind.TypeParameter:
            case Kind.ClassDeclaration:
            case Kind.InterfaceDeclaration:
            case Kind.TypeAliasDeclaration:
            case Kind.JSTypeAliasDeclaration:
            case Kind.EnumDeclaration:
                return true;
            case Kind.ImportClause:
                return node.IsTypeOnly();
            case Kind.ImportSpecifier:
            case Kind.ExportSpecifier:
                return node.Parent!.Parent!.IsTypeOnly();
            default:
                return false;
        }
    }

    /// <summary>Go: <c>func IsTypeDeclarationName(name *Node) bool</c>。</summary>
    public static bool IsTypeDeclarationName(Node name)
    {
        return name.Kind == Kind.Identifier &&
            IsTypeDeclaration(name.Parent!) &&
            GetNameOfDeclaration(name.Parent!) == name;
    }

    /// <summary>Go: <c>func IsRightSideOfPropertyAccess(node *Node) bool</c>。</summary>
    public static bool IsRightSideOfPropertyAccess(Node node)
    {
        return node.Parent!.Kind == Kind.PropertyAccessExpression && node.Parent!.Name() == node;
    }

    /// <summary>Go: <c>func IsArgumentExpressionOfElementAccess(node *Node) bool</c>。</summary>
    public static bool IsArgumentExpressionOfElementAccess(Node node)
    {
        return node.Parent is { } parent &&
            parent.Kind == Kind.ElementAccessExpression &&
            parent.AsElementAccessExpression()!.ArgumentExpression == node;
    }

    /// <summary>Go: <c>func ClimbPastPropertyAccess(node *Node) *Node</c>。</summary>
    public static Node? ClimbPastPropertyAccess(Node node)
    {
        if (IsRightSideOfPropertyAccess(node))
        {
            return node.Parent;
        }
        return node;
    }

    /// <summary>Go: <c>func climbPastPropertyOrElementAccess(node *Node) *Node</c>。</summary>
    internal static Node? ClimbPastPropertyOrElementAccess(Node node)
    {
        if (IsRightSideOfPropertyAccess(node) || IsArgumentExpressionOfElementAccess(node))
        {
            return node.Parent;
        }
        return node;
    }

    /// <summary>Go: <c>func selectExpressionOfCallOrNewExpressionOrDecorator(node *Node) *Node</c>。</summary>
    internal static Node? SelectExpressionOfCallOrNewExpressionOrDecorator(Node node)
    {
        if (IsCallExpression(node) || IsNewExpression(node) || IsDecorator(node))
        {
            return node.Expression();
        }
        return null;
    }

    /// <summary>Go: <c>func selectTagOfTaggedTemplateExpression(node *Node) *Node</c>。</summary>
    internal static Node? SelectTagOfTaggedTemplateExpression(Node node)
    {
        if (IsTaggedTemplateExpression(node))
        {
            return node.AsTaggedTemplateExpression()!.Tag;
        }
        return null;
    }

    /// <summary>Go: <c>func selectTagNameOfJsxOpeningLikeElement(node *Node) *Node</c>。</summary>
    internal static Node? SelectTagNameOfJsxOpeningLikeElement(Node node)
    {
        if (IsJsxOpeningElement(node) || IsJsxSelfClosingElement(node))
        {
            return node.TagName();
        }
        return null;
    }

    /// <summary>Go: <c>func IsCallExpressionTarget(node *Node, includeElementAccess bool, skipPastOuterExpressions bool) bool</c>。</summary>
    public static bool IsCallExpressionTarget(Node node, bool includeElementAccess, bool skipPastOuterExpressions)
    {
        return IsCalleeWorker(
            node,
            IsCallExpression,
            SelectExpressionOfCallOrNewExpressionOrDecorator,
            includeElementAccess,
            skipPastOuterExpressions);
    }

    /// <summary>Go: <c>func IsNewExpressionTarget(node *Node, includeElementAccess bool, skipPastOuterExpressions bool) bool</c>。</summary>
    public static bool IsNewExpressionTarget(Node node, bool includeElementAccess, bool skipPastOuterExpressions)
    {
        return IsCalleeWorker(
            node,
            IsNewExpression,
            SelectExpressionOfCallOrNewExpressionOrDecorator,
            includeElementAccess,
            skipPastOuterExpressions);
    }

    /// <summary>Go: <c>func IsCallOrNewExpressionTarget(node *Node, includeElementAccess bool, skipPastOuterExpressions bool) bool</c>。</summary>
    public static bool IsCallOrNewExpressionTarget(Node node, bool includeElementAccess, bool skipPastOuterExpressions)
    {
        return IsCalleeWorker(
            node,
            IsCallOrNewExpression,
            SelectExpressionOfCallOrNewExpressionOrDecorator,
            includeElementAccess,
            skipPastOuterExpressions);
    }

    /// <summary>Go: <c>func IsTaggedTemplateTag(node *Node, includeElementAccess bool, skipPastOuterExpressions bool) bool</c>。</summary>
    public static bool IsTaggedTemplateTag(Node node, bool includeElementAccess, bool skipPastOuterExpressions)
    {
        return IsCalleeWorker(
            node,
            IsTaggedTemplateExpression,
            SelectTagOfTaggedTemplateExpression,
            includeElementAccess,
            skipPastOuterExpressions);
    }

    /// <summary>Go: <c>func IsDecoratorTarget(node *Node, includeElementAccess bool, skipPastOuterExpressions bool) bool</c>。</summary>
    public static bool IsDecoratorTarget(Node node, bool includeElementAccess, bool skipPastOuterExpressions)
    {
        return IsCalleeWorker(
            node,
            IsDecorator,
            SelectExpressionOfCallOrNewExpressionOrDecorator,
            includeElementAccess,
            skipPastOuterExpressions);
    }

    /// <summary>Go: <c>func IsJsxOpeningLikeElementTagName(node *Node, includeElementAccess bool, skipPastOuterExpressions bool) bool</c>。</summary>
    public static bool IsJsxOpeningLikeElementTagName(Node node, bool includeElementAccess, bool skipPastOuterExpressions)
    {
        return IsCalleeWorker(
            node,
            IsJsxOpeningLikeElement,
            SelectTagNameOfJsxOpeningLikeElement,
            includeElementAccess,
            skipPastOuterExpressions);
    }

    /// <summary>Go: <c>func isCalleeWorker(node *Node, pred func(*Node) bool, calleeSelector func(*Node) *Node, includeElementAccess bool, skipPastOuterExpressions bool) bool</c>。</summary>
    internal static bool IsCalleeWorker(
        Node node,
        Func<Node, bool> pred,
        Func<Node, Node?> calleeSelector,
        bool includeElementAccess,
        bool skipPastOuterExpressions)
    {
        Node? target;
        if (includeElementAccess)
        {
            target = ClimbPastPropertyOrElementAccess(node);
        }
        else
        {
            target = ClimbPastPropertyAccess(node);
        }
        if (skipPastOuterExpressions)
        {
            // Only skip outer expressions if the target is actually an expression node
            if (IsExpression(target!))
            {
                target = SkipOuterExpressions(target!, OuterExpressionKinds.All);
            }
        }
        return target != null && target.Parent != null && pred(target.Parent!) && calleeSelector(target.Parent!) == target;
    }

    /// <summary>Go: <c>func IsRightSideOfQualifiedNameOrPropertyAccess(node *Node) bool</c>。</summary>
    public static bool IsRightSideOfQualifiedNameOrPropertyAccess(Node node)
    {
        var parent = node.Parent!;
        switch (parent.Kind)
        {
            case Kind.QualifiedName:
                return parent.AsQualifiedName()!.Right == node;
            case Kind.PropertyAccessExpression:
                return parent.AsPropertyAccessExpression()!.Name() == node;
            case Kind.MetaProperty:
                return parent.AsMetaProperty()!.Name() == node;
        }
        return false;
    }

    /// <summary>Go: <c>func ShouldTransformImportCall(fileName string, options *core.CompilerOptions, impliedNodeFormatForEmit core.ModuleKind) bool</c>。</summary>
    public static bool ShouldTransformImportCall(string fileName, CompilerOptions? options, ModuleKind impliedNodeFormatForEmit)
    {
        var moduleKind = options!.GetEmitModuleKind();
        if (ModuleKind.Node16 <= moduleKind && moduleKind <= ModuleKind.NodeNext || moduleKind == ModuleKind.Preserve)
        {
            return false;
        }
        return impliedNodeFormatForEmit < ModuleKind.ES2015;
    }

    /// <summary>Go: <c>func HasQuestionToken(node *Node) bool</c>。</summary>
    public static bool HasQuestionToken(Node node)
    {
        // 与 Go 的差异：Go 把 nil 判断放在 IsQuestionToken 内部（`node != nil && node.Kind == KindQuestionToken`），
        // 这里显式提到调用点，避免依赖该函数 C# 形参是否可空（nil 语义保持：无 QuestionToken 时为 false）。
        return node.QuestionToken() is { } questionToken && IsQuestionToken(questionToken);
    }


/// <summary>Go: <c>func IsJsxOpeningLikeElement(node *Node) bool</c>。</summary>
    public static bool IsJsxOpeningLikeElement(Node node) => IsJsxOpeningElement(node) || IsJsxSelfClosingElement(node);

    /// <summary>Go: <c>func GetInvokedExpression(node *Node) *Node</c>。</summary>
    public static Node? GetInvokedExpression(Node node)
    {
        switch (node.Kind)
        {
            case Kind.TaggedTemplateExpression:
                return node.AsTaggedTemplateExpression()!.Tag;
            case Kind.JsxOpeningElement:
            case Kind.JsxSelfClosingElement:
                return node.TagName();
            case Kind.BinaryExpression:
                return node.AsBinaryExpression()!.Right;
            case Kind.JsxOpeningFragment:
                return node;
            default:
                return node.Expression();
        }
    }

    /// <summary>Go: <c>func IsCallOrNewExpression(node *Node) bool</c>。</summary>
    public static bool IsCallOrNewExpression(Node node) => IsCallExpression(node) || IsNewExpression(node);

    /// <summary>Go: <c>func IndexOfNode(nodes []*Node, node *Node) int</c>。</summary>
    public static int IndexOfNode(ReadOnlySpan<Node> nodes, Node node)
    {
        // 与 Go 的差异：Go 用 `slices.BinarySearchFunc(nodes, node, CompareNodePositions)`（标准库），这里就地
        // 展开成与之逐条等价的实现：下界二分（high 为开区间上界），再判相等；ok=false 时返回 -1。
        var low = 0;
        var high = nodes.Length;
        while (low < high)
        {
            var middle = low + ((high - low) >> 1);
            if (CompareNodePositions(nodes[middle], node) < 0)
            {
                low = middle + 1;
            }
            else
            {
                high = middle;
            }
        }
        return low < nodes.Length && CompareNodePositions(nodes[low], node) == 0 ? low : -1;
    }

    /// <summary>Go: <c>func CompareNodePositions(n1, n2 *Node) int</c>。</summary>
    public static int CompareNodePositions(Node n1, Node n2) => CompareTextRanges(n1.Loc, n2.Loc);

    /// <summary>Go: <c>func IsUnterminatedLiteral(node *Node) bool</c>。</summary>
    public static bool IsUnterminatedLiteral(Node node)
    {
        return IsLiteralKind(node.Kind) && (node.LiteralLikeData()!.TokenFlags & TokenFlags.Unterminated) != 0 ||
            IsTemplateLiteralKind(node.Kind) && (node.TemplateLiteralLikeData()!.TemplateFlags & TokenFlags.Unterminated) != 0;
    }

    // Gets a value indicating whether a class element is either a static or an instance property declaration with an initializer.
    /// <summary>Go: <c>func IsInitializedProperty(member *ClassElement) bool</c>。</summary>
    public static bool IsInitializedProperty(Node member)
    {
        return member.Kind == Kind.PropertyDeclaration &&
            member.Initializer() != null;
    }

    /// <summary>Go: <c>func IsTrivia(token Kind) bool</c>。</summary>
    public static bool IsTrivia(Kind token) => Kind.FirstTriviaToken <= token && token <= Kind.LastTriviaToken;

    /// <summary>Go: <c>func HasDecorators(node *Node) bool</c>。</summary>
    public static bool HasDecorators(Node node) => HasSyntacticModifier(node, ModifierFlags.Decorator);

    // Go: type hasFileNameImpl struct { fileName string; path tspath.Path }
    /// <summary>Go: <c>type hasFileNameImpl struct</c>（HasFileName 接口的实现，见 AstTypes.cs）。</summary>
    private sealed class HasFileNameImpl : HasFileName
    {
        private readonly string _fileName;
        private readonly TSPath.Path _path;

        public HasFileNameImpl(string fileName, TSPath.Path path)
        {
            _fileName = fileName;
            _path = path;
        }

        /// <summary>Go: <c>func (h *hasFileNameImpl) FileName() string</c>。</summary>
        public string FileName() => _fileName;

        /// <summary>Go: <c>func (h *hasFileNameImpl) Path() tspath.Path</c>。</summary>
        public TSPath.Path Path() => _path;
    }

    /// <summary>Go: <c>func NewHasFileName(fileName string, path tspath.Path) HasFileName</c>。</summary>
    public static HasFileName NewHasFileName(string fileName, TSPath.Path path) => new HasFileNameImpl(fileName, path);

    /// <summary>Go: <c>func GetSemanticJsxChildren(children []*JsxChild) []*JsxChild</c>。</summary>
    public static Node[] GetSemanticJsxChildren(ReadOnlySpan<Node> children)
    {
        // 与 Go 的差异：Go 的 `core.Filter` 在没有任何元素被过滤时返回原切片（视图、零拷贝）；C# 数组没有
        // len<cap 视图，这里总是分配新数组（元素引用不变，过滤结果一致）。
        var result = new List<Node>(children.Length);
        foreach (var child in children)
        {
            switch (child.Kind)
            {
                case Kind.JsxExpression when child.Expression() == null:
                    break;
                case Kind.JsxText when child.AsJsxText()!.ContainsOnlyTriviaWhiteSpaces:
                    break;
                default:
                    result.Add(child);
                    break;
            }
        }
        return result.ToArray();
    }

    // Returns true if the node kind has a comment property.
    /// <summary>Go: <c>func hasComment(kind Kind) bool</c>。</summary>
    internal static bool HasComment(Kind kind)
    {
        switch (kind)
        {
            case Kind.JSDoc:
            case Kind.JSDocUnknownTag:
            case Kind.JSDocAugmentsTag:
            case Kind.JSDocImplementsTag:
            case Kind.JSDocDeprecatedTag:
            case Kind.JSDocPublicTag:
            case Kind.JSDocPrivateTag:
            case Kind.JSDocProtectedTag:
            case Kind.JSDocReadonlyTag:
            case Kind.JSDocOverrideTag:
            case Kind.JSDocCallbackTag:
            case Kind.JSDocOverloadTag:
            case Kind.JSDocParameterTag:
            case Kind.JSDocPropertyTag:
            case Kind.JSDocReturnTag:
            case Kind.JSDocThisTag:
            case Kind.JSDocTypeTag:
            case Kind.JSDocTemplateTag:
            case Kind.JSDocTypedefTag:
            case Kind.JSDocSeeTag:
            case Kind.JSDocThrowsTag:
            case Kind.JSDocSatisfiesTag:
            case Kind.JSDocImportTag:
                return true;
            default:
                return false;
        }
    }

    /// <summary>Go: <c>func IsAssignmentPattern(node *Node) bool</c>。</summary>
    public static bool IsAssignmentPattern(Node node) =>
        node.Kind == Kind.ArrayLiteralExpression || node.Kind == Kind.ObjectLiteralExpression;

    /// <summary>Go: <c>func GetElementsOfBindingOrAssignmentPattern(name *Node) []*Node</c>。</summary>
    public static Node[] GetElementsOfBindingOrAssignmentPattern(Node name)
    {
        switch (name.Kind)
        {
            case Kind.ObjectBindingPattern:
            case Kind.ArrayBindingPattern:
            case Kind.ArrayLiteralExpression:
                // `a` in `{a}`
                // `a` in `[a]`
                return name.Elements() ?? [];
            case Kind.ObjectLiteralExpression:
                // `a` in `{a}`
                return name.Properties() ?? [];
        }
        return [];
    }

    /// <summary>Go: <c>func IsDeclarationBindingElement(bindingElement *Node) bool</c>。</summary>
    public static bool IsDeclarationBindingElement(Node bindingElement)
    {
        switch (bindingElement.Kind)
        {
            case Kind.VariableDeclaration:
            case Kind.Parameter:
            case Kind.BindingElement:
                return true;
            default:
                return false;
        }
    }

    // Gets the name of an BindingOrAssignmentElement.
    /// <summary>Go: <c>func GetTargetOfBindingOrAssignmentElement(bindingElement *Node) *Node</c>。</summary>
    public static Node? GetTargetOfBindingOrAssignmentElement(Node bindingElement)
    {
        if (IsDeclarationBindingElement(bindingElement))
        {
            // `a` in `let { a } = ...`
            // `a` in `let { a = 1 } = ...`
            // `b` in `let { a: b } = ...`
            // `b` in `let { a: b = 1 } = ...`
            // `a` in `let { ...a } = ...`
            // `{b}` in `let { a: {b} } = ...`
            // `{b}` in `let { a: {b} = 1 } = ...`
            // `[b]` in `let { a: [b] } = ...`
            // `[b]` in `let { a: [b] = 1 } = ...`
            // `a` in `let [a] = ...`
            // `a` in `let [a = 1] = ...`
            // `a` in `let [...a] = ...`
            // `{a}` in `let [{a}] = ...`
            // `{a}` in `let [{a} = 1] = ...`
            // `[a]` in `let [[a]] = ...`
            // `[a]` in `let [[a] = 1] = ...`
            return bindingElement.Name();
        }

        if (IsObjectLiteralElement(bindingElement))
        {
            switch (bindingElement.Kind)
            {
                case Kind.PropertyAssignment:
                    // `b` in `({ a: b } = ...)`
                    // `b` in `({ a: b = 1 } = ...)`
                    // `{b}` in `({ a: {b} } = ...)`
                    // `{b}` in `({ a: {b} = 1 } = ...)`
                    // `[b]` in `({ a: [b] } = ...)`
                    // `[b]` in `({ a: [b] = 1 } = ...)`
                    // `b.c` in `({ a: b.c } = ...)`
                    // `b.c` in `({ a: b.c = 1 } = ...)`
                    // `b[0]` in `({ a: b[0] } = ...)`
                    // `b[0]` in `({ a: b[0] = 1 } = ...)`
                    return GetTargetOfBindingOrAssignmentElement(bindingElement.Initializer()!);
                case Kind.ShorthandPropertyAssignment:
                    // `a` in `({ a } = ...)`
                    // `a` in `({ a = 1 } = ...)`
                    return bindingElement.Name();
                case Kind.SpreadAssignment:
                    // `a` in `({ ...a } = ...)`
                    return GetTargetOfBindingOrAssignmentElement(bindingElement.Expression()!);
            }

            // no target
            return null;
        }

        if (IsAssignmentExpression(bindingElement /*excludeCompoundAssignment*/, true))
        {
            // `a` in `[a = 1] = ...`
            // `{a}` in `[{a} = 1] = ...`
            // `[a]` in `[[a] = 1] = ...`
            // `a.b` in `[a.b = 1] = ...`
            // `a[0]` in `[a[0] = 1] = ...`
            return GetTargetOfBindingOrAssignmentElement(bindingElement.AsBinaryExpression()!.Left!);
        }

        if (IsSpreadElement(bindingElement))
        {
            // `a` in `[...a] = ...`
            return GetTargetOfBindingOrAssignmentElement(bindingElement.Expression()!);
        }

        // `a` in `[a] = ...`
        // `{a}` in `[{a}] = ...`
        // `[a]` in `[[a]] = ...`
        // `a.b` in `[a.b] = ...`
        // `a[0]` in `[a[0]] = ...`
        return bindingElement;
    }

    /// <summary>Go: <c>func TryGetPropertyNameOfBindingOrAssignmentElement(bindingElement *Node) *Node</c>。</summary>
    public static Node? TryGetPropertyNameOfBindingOrAssignmentElement(Node bindingElement)
    {
        switch (bindingElement.Kind)
        {
            case Kind.BindingElement:
                // `a` in `let { a: b } = ...`
                // `[a]` in `let { [a]: b } = ...`
                // `"a"` in `let { "a": b } = ...`
                // `1` in `let { 1: b } = ...`
                if (bindingElement.PropertyName() != null)
                {
                    var propertyName = bindingElement.PropertyName()!;
                    // if IsPrivateIdentifier(propertyName) {
                    // 	return Debug.failBadSyntaxKind(propertyName) // !!!
                    // }
                    if (IsComputedPropertyName(propertyName) && IsStringOrNumericLiteralLike(propertyName.Expression()!))
                    {
                        return propertyName.Expression();
                    }
                    return propertyName;
                }
                break;
            case Kind.PropertyAssignment:
                // `a` in `({ a: b } = ...)`
                // `[a]` in `({ [a]: b } = ...)`
                // `"a"` in `({ "a": b } = ...)`
                // `1` in `({ 1: b } = ...)`
                if (bindingElement.Name() != null)
                {
                    var propertyName = bindingElement.Name()!;
                    // if IsPrivateIdentifier(propertyName) {
                    // 	return Debug.failBadSyntaxKind(propertyName) // !!!
                    // }
                    if (IsComputedPropertyName(propertyName) && IsStringOrNumericLiteralLike(propertyName.Expression()!))
                    {
                        return propertyName.Expression();
                    }
                    return propertyName;
                }
                break;
            case Kind.SpreadAssignment:
                // `a` in `({ ...a } = ...)`
                // if IsPrivateIdentifier(bindingElement.Name()) {
                // 	return Debug.failBadSyntaxKind(bindingElement.Name()) // !!!
                // }
                return bindingElement.Name();
        }

        var target = GetTargetOfBindingOrAssignmentElement(bindingElement);
        if (target != null && IsPropertyName(target))
        {
            return target;
        }
        return null;
    }

    // Walk an AssignmentPattern to determine if it contains object rest (`...`) syntax. We cannot rely on
    // propagation of `TransformFlags.ContainsObjectRestOrSpread` since it isn't propagated by default in
    // ObjectLiteralExpression and ArrayLiteralExpression since we do not know whether they belong to an
    // AssignmentPattern at the time the nodes are parsed.
    /// <summary>Go: <c>func ContainsObjectRestOrSpread(node *Node) bool</c>。</summary>
    public static bool ContainsObjectRestOrSpread(Node node)
    {
        if ((node.SubtreeFacts & SubtreeFacts.ContainsObjectRestOrSpread) != 0)
        {
            return true;
        }
        if ((node.SubtreeFacts & SubtreeFacts.ContainsESObjectRestOrSpread) != 0)
        {
            // check for nested spread assignments, otherwise '{ x: { a, ...b } = foo } = c'
            // will not be correctly interpreted by the rest/spread transformer
            foreach (var element in GetElementsOfBindingOrAssignmentPattern(node))
            {
                var target = GetTargetOfBindingOrAssignmentElement(element);
                if (target != null && IsAssignmentPattern(target))
                {
                    if ((target.SubtreeFacts & SubtreeFacts.ContainsObjectRestOrSpread) != 0)
                    {
                        return true;
                    }
                    if ((target.SubtreeFacts & SubtreeFacts.ContainsESObjectRestOrSpread) != 0)
                    {
                        if (ContainsObjectRestOrSpread(target))
                        {
                            return true;
                        }
                    }
                }
            }
        }
        return false;
    }

    /// <summary>Go: <c>func IsEmptyObjectLiteral(expression *Node) bool</c>。</summary>
    public static bool IsEmptyObjectLiteral(Node expression) =>
        IsObjectLiteralExpression(expression) && (expression.Properties()?.Length ?? 0) == 0;

    /// <summary>Go: <c>func IsEmptyArrayLiteral(expression *Node) bool</c>。</summary>
    public static bool IsEmptyArrayLiteral(Node expression) =>
        IsArrayLiteralExpression(expression) && (expression.Elements()?.Length ?? 0) == 0;

    /// <summary>Go: <c>func GetRestIndicatorOfBindingOrAssignmentElement(bindingElement *Node) *Node</c>。</summary>
    public static Node? GetRestIndicatorOfBindingOrAssignmentElement(Node bindingElement)
    {
        switch (bindingElement.Kind)
        {
            case Kind.Parameter:
                return bindingElement.AsParameterDeclaration()!.DotDotDotToken;
            case Kind.BindingElement:
                return bindingElement.AsBindingElement()!.DotDotDotToken;
            case Kind.SpreadElement:
            case Kind.SpreadAssignment:
                return bindingElement;
        }
        return null;
    }

    /// <summary>Go: <c>func IsJSDocNameReferenceContext(node *Node) bool</c>。</summary>
    public static bool IsJSDocNameReferenceContext(Node node)
    {
        return (node.Flags & NodeFlags.JSDoc) != 0 && FindAncestor(node, n => IsJSDocNameReference(n) || IsJSDocLinkLike(n)) != null;
    }

    // GetJSDocRoot returns the containing JSDoc node for a node inside a JSDoc comment.
    /// <summary>Go: <c>func GetJSDocRoot(node *Node) *Node</c>。</summary>
    public static Node? GetJSDocRoot(Node node)
    {
        return FindAncestor(node.Parent, n => n.Kind == Kind.JSDoc);
    }

    // GetJSDocHost returns the declaration that the JSDoc comment containing the given node is attached to.
    /// <summary>Go: <c>func GetJSDocHost(node *Node) *Node</c>。</summary>
    public static Node? GetJSDocHost(Node node)
    {
        var jsDoc = GetJSDocRoot(node);
        if (jsDoc == null)
        {
            return null;
        }
        return jsDoc.Parent;
    }

    // GetHostSignatureFromJSDoc returns the function-like declaration that hosts the JSDoc comment
    // containing the given node. This is used to resolve @link references to parameters.
    /// <summary>Go: <c>func GetHostSignatureFromJSDoc(node *Node) *Node</c>。</summary>
    public static Node? GetHostSignatureFromJSDoc(Node node)
    {
        var host = GetJSDocHost(node);
        if (host == null)
        {
            return null;
        }
        // !!! Strada's getEffectiveJSDocHost applies JS assignment pattern transforms (getSourceOfAssignment, getSourceOfDefaultedAssignment, etc.) not yet ported
        if (IsPropertySignatureDeclaration(host) && host.Type() != null && IsFunctionLike(host.Type()!))
        {
            return host.Type();
        }
        if (IsFunctionLike(host))
        {
            return host;
        }
        return null;
    }

    // Finds the declaration that owns the JSDoc for a function-like node.
    // Keep these hosts aligned with JSDoc parameter reparsing so unmatched @param diagnostics use the same attachment rules.
    // Keep in sync with getNextJSDocCommentLocation in the API's src/ast/jsdoc.ts
    /// <summary>Go: <c>func GetNextJSDocCommentLocation(node *Node) *Node</c>。</summary>
    public static Node? GetNextJSDocCommentLocation(Node node)
    {
        if (node.Parent is { } parent)
        {
            switch (parent.Kind)
            {
                case Kind.PropertyAssignment:
                case Kind.ExportAssignment:
                case Kind.PropertyDeclaration:
                case Kind.VariableDeclaration:
                case Kind.SatisfiesExpression:
                case Kind.ReturnStatement:
                case Kind.VariableStatement:
                case Kind.ExpressionStatement:
                    return parent;
                case Kind.VariableDeclarationList:
                    if (parent.AsVariableDeclarationList()!.Declarations!.Nodes![0] == node)
                    {
                        return parent;
                    }
                    break;
            }
        }
        return null;
    }

    // 未移植：Go:3760 IsJsxOpeningLikeElement —— 已由 Utilities.cs 移植（Go utilities.go:3760）。


    /// <summary>Go: <c>func IsImportOrImportEqualsDeclaration(node *Node) bool</c>。</summary>
    public static bool IsImportOrImportEqualsDeclaration(Node? node)
    {
        var n = node!;
        return IsImportDeclaration(n) || IsImportEqualsDeclaration(n);
    }

    /// <summary>Go: <c>func IsPrimitiveLiteralValue(node *Node, includeBigInt bool) bool</c>。</summary>
    public static bool IsPrimitiveLiteralValue(Node? node, bool includeBigInt)
    {
        var n = node!;
        switch (n.Kind)
        {
            case Kind.TrueKeyword:
            case Kind.FalseKeyword:
            case Kind.NumericLiteral:
            case Kind.StringLiteral:
            case Kind.NoSubstitutionTemplateLiteral:
                return true;
            case Kind.BigIntLiteral:
                return includeBigInt;
            case Kind.PrefixUnaryExpression:
                if (n.AsPrefixUnaryExpression()!.Operator == Kind.MinusToken)
                {
                    return IsNumericLiteral(n.AsPrefixUnaryExpression()!.Operand!) ||
                        (includeBigInt && IsBigIntLiteral(n.AsPrefixUnaryExpression()!.Operand!));
                }

                if (n.AsPrefixUnaryExpression()!.Operator == Kind.PlusToken)
                {
                    return IsNumericLiteral(n.AsPrefixUnaryExpression()!.Operand!);
                }

                return false;
            default:
                return false;
        }
    }

    /// <summary>Go: <c>func HasInferredType(node *Node) bool</c>。</summary>
    public static bool HasInferredType(Node? node)
    {
        var n = node!;
        // Debug.type<HasInferredType>(node); // !!!
        switch (n.Kind)
        {
            case Kind.Parameter:
            case Kind.PropertySignature:
            case Kind.PropertyDeclaration:
            case Kind.BindingElement:
            case Kind.PropertyAccessExpression:
            case Kind.ElementAccessExpression:
            case Kind.BinaryExpression:
            case Kind.CallExpression:
            case Kind.VariableDeclaration:
            case Kind.ExportAssignment:
            case Kind.PropertyAssignment:
            case Kind.ShorthandPropertyAssignment:
            case Kind.JSDocParameterTag:
            case Kind.JSDocPropertyTag:
                return true;
            default:
                // assertType<never>(node); // !!!
                return false;
        }
    }

    /// <summary>Go: <c>func IsKeyword(token Kind) bool</c>。</summary>
    public static bool IsKeyword(Kind token)
    {
        return Kind.FirstKeyword <= token && token <= Kind.LastKeyword;
    }

    /// <summary>Go: <c>func IsNonContextualKeyword(token Kind) bool</c>。</summary>
    public static bool IsNonContextualKeyword(Kind token)
    {
        return IsKeyword(token) && !IsContextualKeyword(token);
    }

    /// <summary>Go: <c>func HasModifier(node *Node, flags ModifierFlags) bool</c>。</summary>
    public static bool HasModifier(Node? node, ModifierFlags flags)
    {
        return (node!.ModifierFlags & flags) != 0;
    }

    /// <summary>Go: <c>func IsExpandoInitializer(declaration *Node, initializer *Node) bool</c>。</summary>
    public static bool IsExpandoInitializer(Node? declaration, Node? initializer)
    {
        if (initializer is null)
        {
            return false;
        }

        if (IsFunctionExpressionOrArrowFunction(initializer))
        {
            return true;
        }

        if (IsInJSFile(initializer))
        {
            return IsClassExpression(initializer) ||
                (IsObjectLiteralExpression(initializer) &&
                    initializer.Properties() is null or { Length: 0 } &&
                    declaration!.Type() is null);
        }

        return false;
    }

    /// <summary>Go: <c>func GetContainingFunction(node *Node) *Node</c>。</summary>
    public static Node? GetContainingFunction(Node? node)
    {
        return FindAncestor(node!.Parent, n => IsFunctionLike(n));
    }

    /// <summary>Go: <c>func ImportFromModuleSpecifier(node *Node) *Node</c>。</summary>
    public static Node? ImportFromModuleSpecifier(Node? node)
    {
        var n = node!;
        if (TryGetImportFromModuleSpecifier(n) is { } result)
        {
            return result;
        }

        Debug.FailBadSyntaxKind(n.Parent!);
        return null;
    }

    /// <summary>Go: <c>func TryGetImportFromModuleSpecifier(node *StringLiteralLike) *Node</c>。</summary>
    public static Node? TryGetImportFromModuleSpecifier(Node? node)
    {
        var n = node!;
        switch (n.Parent!.Kind)
        {
            case Kind.ImportDeclaration:
            case Kind.JSImportDeclaration:
            case Kind.ExportDeclaration:
                return n.Parent;
            case Kind.ExternalModuleReference:
                return n.Parent.Parent;
            case Kind.CallExpression:
                if (IsImportCall(n.Parent!) || IsRequireCall(n.Parent!, false /*requireStringLiteralLikeArgument*/))
                {
                    return n.Parent;
                }

                return null;
            case Kind.LiteralType:
                if (!IsStringLiteral(n))
                {
                    return null;
                }

                if (IsImportTypeNode(n.Parent.Parent!))
                {
                    return n.Parent.Parent;
                }

                return null;
        }

        return null;
    }

    /// <summary>Go: <c>func IsImplicitlyExportedJSDocDeclaration(node *Node) bool</c>。</summary>
    public static bool IsImplicitlyExportedJSDocDeclaration(Node? node)
    {
        var n = node!;
        if (!IsSourceFile(n.Parent!) || !IsExternalOrCommonJSModule(n.Parent!.AsSourceFile()!))
        {
            return false;
        }

        if (IsJSTypeAliasDeclaration(n))
        {
            return true;
        }

        // A reparsed ModuleDeclaration synthesized from a JSDoc @typedef/@callback
        // dotted name should also be treated as implicitly exported in modules.
        return IsModuleDeclaration(n) && (n.Flags & NodeFlags.Reparsed) != 0;
    }

    /// <summary>Go: <c>func HasContextSensitiveParameters(node *Node) bool</c>。</summary>
    public static bool HasContextSensitiveParameters(Node? node)
    {
        var n = node!;
        // Functions with type parameters are not context sensitive.
        if (n.TypeParameters() is null)
        {
            // Functions with any parameters that lack type annotations are context sensitive.
            if (Some(n.Parameters(), p => p.Type() is null))
            {
                return true;
            }

            if (!IsArrowFunction(n))
            {
                // If the first parameter is not an explicit 'this' parameter, then the function has
                // an implicit 'this' parameter which is subject to contextual typing.
                var parameter = FirstOrNil(n.Parameters());
                if (parameter is null || !IsThisParameter(parameter))
                {
                    return (n.Flags & NodeFlags.ContainsThis) != 0;
                }
            }
        }

        return false;
    }

    /// <summary>Go: <c>func IsInfinityOrNaNString(name string) bool</c>。</summary>
    public static bool IsInfinityOrNaNString(string name)
    {
        return name == "Infinity" || name == "-Infinity" || name == "NaN";
    }

    /// <summary>Go: <c>func GetFirstConstructorWithBody(node *Node) *Node</c>。</summary>
    public static Node? GetFirstConstructorWithBody(Node? node)
    {
        var n = node!;
        foreach (var member in n.Members().AsSpan())
        {
            if (IsConstructorDeclaration(member) && NodeIsPresent(member.Body()))
            {
                return member;
            }
        }

        return null;
    }

    // Returns true for nodes that are considered executable for the purposes of unreachable code detection.
    /// <summary>Go: <c>func IsPotentiallyExecutableNode(node *Node) bool</c>。</summary>
    public static bool IsPotentiallyExecutableNode(Node? node)
    {
        var n = node!;
        if (Kind.FirstStatement <= n.Kind && n.Kind <= Kind.LastStatement)
        {
            if (IsVariableStatement(n))
            {
                var declarationList = n.AsVariableStatement()!.DeclarationList!;
                if ((GetCombinedNodeFlags(declarationList!) & NodeFlags.BlockScoped) != 0)
                {
                    return true;
                }

                var declarations = declarationList.AsVariableDeclarationList()!.Declarations!.Nodes;
                return Some(declarations, d => d.Initializer() is not null);
            }

            return true;
        }

        return IsClassDeclaration(n) || IsEnumDeclaration(n) || IsModuleDeclaration(n);
    }

    /// <summary>Go: <c>func HasAbstractModifier(node *Node) bool</c>。</summary>
    public static bool HasAbstractModifier(Node? node)
    {
        return HasSyntacticModifier(node!, ModifierFlags.Abstract);
    }

    /// <summary>Go: <c>func HasAmbientModifier(node *Node) bool</c>。</summary>
    public static bool HasAmbientModifier(Node? node)
    {
        return HasSyntacticModifier(node!, ModifierFlags.Ambient);
    }

    /// <summary>Go: <c>func NodeCanBeDecorated(useLegacyDecorators bool, node *Node, parent *Node, grandparent *Node) bool</c>。</summary>
    public static bool NodeCanBeDecorated(bool useLegacyDecorators, Node? node, Node? parent, Node? grandparent)
    {
        var n = node!;
        // private names cannot be used with decorators yet
        if (useLegacyDecorators && n.Name() is { } name && IsPrivateIdentifier(name))
        {
            return false;
        }

        switch (n.Kind)
        {
            // class declarations are valid targets
            case Kind.ClassDeclaration:
                return true;
            // class expressions are valid targets for native decorators
            case Kind.ClassExpression:
                return !useLegacyDecorators;
            // property declarations are valid if their parent is a class declaration.
            case Kind.PropertyDeclaration:
                return parent is not null && (useLegacyDecorators && IsClassDeclaration(parent) ||
                    !useLegacyDecorators && IsClassLike(parent) && !HasAbstractModifier(n) && !HasAmbientModifier(n));
            // if this method has a body and its parent is a class declaration, this is a valid target.
            case Kind.GetAccessor:
            case Kind.SetAccessor:
            case Kind.MethodDeclaration:
                return parent is not null && n.Body() is not null && (useLegacyDecorators && IsClassDeclaration(parent) ||
                    !useLegacyDecorators && IsClassLike(parent));
            // TODO(rbuckton): ParameterDeclaration decorator support for ES decorators must wait until it is standardized
            case Kind.Parameter:
                if (!useLegacyDecorators)
                {
                    return false;
                }

                // if the parameter's parent has a body and its grandparent is a class declaration, this is a valid target.
                return parent is not null && parent.Body() is not null &&
                    (parent.Kind == Kind.Constructor || parent.Kind == Kind.MethodDeclaration || parent.Kind == Kind.SetAccessor) &&
                    GetThisParameter(parent) != n && grandparent is not null && grandparent.Kind == Kind.ClassDeclaration;
        }

        return false;
    }

    /// <summary>Go: <c>func ClassOrConstructorParameterIsDecorated(useLegacyDecorators bool, node *Node) bool</c>。</summary>
    public static bool ClassOrConstructorParameterIsDecorated(bool useLegacyDecorators, Node? node)
    {
        var n = node!;
        if (NodeIsDecorated(useLegacyDecorators, n, null, null))
        {
            return true;
        }

        var constructor = GetFirstConstructorWithBody(n);
        return constructor is not null && ChildIsDecorated(useLegacyDecorators, constructor, n);
    }

    /// <summary>Go: <c>func ClassElementOrClassElementParameterIsDecorated(useLegacyDecorators bool, node *Node, parent *Node) bool</c>。</summary>
    public static bool ClassElementOrClassElementParameterIsDecorated(bool useLegacyDecorators, Node? node, Node? parent)
    {
        var n = node!;
        var p = parent!;

        NodeList? parameters = null;
        if (IsAccessor(n))
        {
            var decls = GetAllAccessorDeclarations(p.Members().AsSpan(), n);
            Node? firstAccessorWithDecorators = null;
            if (HasDecorators(decls.FirstAccessor!))
            {
                firstAccessorWithDecorators = decls.FirstAccessor;
            }
            else if (decls.SecondAccessor is not null && HasDecorators(decls.SecondAccessor!))
            {
                firstAccessorWithDecorators = decls.SecondAccessor;
            }

            if (firstAccessorWithDecorators is null || n != firstAccessorWithDecorators)
            {
                return false;
            }

            if (decls.SetAccessor is not null)
            {
                // Go 里是字段 `decls.SetAccessor.Parameters`，两者取值相同（FunctionLikeData().Parameters）。
                parameters = decls.SetAccessor.ParameterList();
            }
        }
        else if (IsMethodDeclaration(n))
        {
            parameters = n.ParameterList();
        }

        if (NodeIsDecorated(useLegacyDecorators, n, p, null))
        {
            return true;
        }

        if (parameters is not null && parameters.Nodes is { Length: > 0 } parameterNodes)
        {
            foreach (var parameter in parameterNodes)
            {
                if (IsThisParameter(parameter))
                {
                    continue;
                }

                if (NodeIsDecorated(useLegacyDecorators, parameter, n, p))
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>Go: <c>func NodeIsDecorated(useLegacyDecorators bool, node *Node, parent *Node, grandparent *Node) bool</c>。</summary>
    public static bool NodeIsDecorated(bool useLegacyDecorators, Node? node, Node? parent, Node? grandparent)
    {
        return HasDecorators(node!) && NodeCanBeDecorated(useLegacyDecorators, node, parent, grandparent);
    }

    /// <summary>Go: <c>func NodeOrChildIsDecorated(useLegacyDecorators bool, node *Node, parent *Node, grandparent *Node) bool</c>。</summary>
    public static bool NodeOrChildIsDecorated(bool useLegacyDecorators, Node? node, Node? parent, Node? grandparent)
    {
        return NodeIsDecorated(useLegacyDecorators, node, parent, grandparent) ||
            ChildIsDecorated(useLegacyDecorators, node, parent);
    }

    /// <summary>Go: <c>func ChildIsDecorated(useLegacyDecorators bool, node *Node, parent *Node) bool</c>。</summary>
    public static bool ChildIsDecorated(bool useLegacyDecorators, Node? node, Node? parent)
    {
        var n = node!;
        switch (n.Kind)
        {
            case Kind.ClassDeclaration:
            case Kind.ClassExpression:
                return Some(n.Members(), m => NodeOrChildIsDecorated(useLegacyDecorators, m, n, parent));
            case Kind.MethodDeclaration:
            case Kind.SetAccessor:
            case Kind.Constructor:
                return Some(n.Parameters(), p => NodeIsDecorated(useLegacyDecorators, p, n, parent));
            default:
                return false;
        }
    }

    /// <summary>Go: <c>func GetAllAccessorDeclarationsForDeclaration(accessor *AccessorDeclaration, declarationsOfSymbol []*Node) AllAccessorDeclarations</c>。</summary>
    public static AllAccessorDeclarations GetAllAccessorDeclarationsForDeclaration(Node? accessor, ReadOnlySpan<Node> declarationsOfSymbol)
    {
        var a = accessor!;

        Kind otherKind;
        if (a.Kind == Kind.SetAccessor)
        {
            otherKind = Kind.GetAccessor;
        }
        else if (a.Kind == Kind.GetAccessor)
        {
            otherKind = Kind.SetAccessor;
        }
        else
        {
            throw new DebugFailureException($"Unexpected node kind \"{a.Kind.String()}\"");
        }

        // otherAccessor := GetDeclarationOfKind(c.getSymbolOfDeclaration(accessor), otherKind)
        Node? otherAccessor = null;
        foreach (var d in declarationsOfSymbol)
        {
            if (d.Kind == otherKind)
            {
                otherAccessor = d;
                break;
            }
        }

        Node? firstAccessor;
        Node? secondAccessor;
        if (otherAccessor is not null && otherAccessor.Pos() < a.Pos())
        {
            firstAccessor = otherAccessor;
            secondAccessor = a;
        }
        else
        {
            firstAccessor = a;
            secondAccessor = otherAccessor;
        }

        Node? setAccessor;
        Node? getAccessor;
        if (a.Kind == Kind.SetAccessor)
        {
            setAccessor = a.AsSetAccessorDeclaration();
            if (otherAccessor is not null)
            {
                getAccessor = otherAccessor.AsGetAccessorDeclaration();
            }
            else
            {
                getAccessor = null;
            }
        }
        else
        {
            getAccessor = a.AsGetAccessorDeclaration();
            if (otherAccessor is not null)
            {
                setAccessor = otherAccessor.AsSetAccessorDeclaration();
            }
            else
            {
                setAccessor = null;
            }
        }

        return new AllAccessorDeclarations
        {
            FirstAccessor = firstAccessor,
            SecondAccessor = secondAccessor,
            SetAccessor = setAccessor,
            GetAccessor = getAccessor,
        };
    }

    /// <summary>Go: <c>func GetAllAccessorDeclarations(parentDeclarations []*Node, accessor *AccessorDeclaration) AllAccessorDeclarations</c>。</summary>
    public static AllAccessorDeclarations GetAllAccessorDeclarations(ReadOnlySpan<Node> parentDeclarations, Node? accessor)
    {
        var a = accessor!;
        if (HasDynamicName(a))
        {
            // dynamic names can only be match up via checker symbol lookup, just return an object with just this accessor
            return GetAllAccessorDeclarationsForDeclaration(a, [a]);
        }

        var accessorName = GetPropertyNameForPropertyNameNode(a.Name()!);
        var accessorStatic = IsStatic(a);
        var matches = new List<Node>();
        foreach (var member in parentDeclarations)
        {
            if (!IsAccessor(member) || IsStatic(member) != accessorStatic)
            {
                continue;
            }

            var memberName = GetPropertyNameForPropertyNameNode(member.Name()!);
            if (memberName == accessorName)
            {
                matches.Add(member);
            }
        }

        // Go 的 `matches` 直接以切片形式传下去；C# 侧用零拷贝视图（docs/architecture.md §3.8）。
        return GetAllAccessorDeclarationsForDeclaration(a, CollectionsMarshal.AsSpan(matches));
    }

    /// <summary>Go: <c>func IsAsyncFunction(node *Node) bool</c>。</summary>
    public static bool IsAsyncFunction(Node? node)
    {
        var n = node!;
        switch (n.Kind)
        {
            case Kind.FunctionDeclaration:
            case Kind.FunctionExpression:
            case Kind.ArrowFunction:
            case Kind.MethodDeclaration:
                var data = n.BodyData()!;
                return data.Body is not null && data.AsteriskToken is null && HasSyntacticModifier(n, ModifierFlags.Async);
        }

        return false;
    }

    /**
     * Gets the most likely element type for a TypeNode. This is not an exhaustive test
     * as it assumes a rest argument can only be an array type (either T[], or Array<T>).
     *
     * @param node The type node.
     *
     * @internal
     */
    /// <summary>Go: <c>func GetRestParameterElementType(node *ParameterDeclarationNode) *Node</c>。</summary>
    public static Node? GetRestParameterElementType(Node? node)
    {
        if (node is null)
        {
            return node;
        }

        if (node.Kind == Kind.ArrayType)
        {
            return node.AsArrayTypeNode()!.ElementType;
        }

        if (node.Kind == Kind.TypeReference && node.AsTypeReferenceNode()!.TypeArguments is not null)
        {
            return FirstOrNil(node.AsTypeReferenceNode()!.TypeArguments!.Nodes);
        }

        return null;
    }

    /// <summary>Go: <c>func TagNamesAreEquivalent(lhs *Expression, rhs *Expression) bool</c>。</summary>
    public static bool TagNamesAreEquivalent(Node? lhs, Node? rhs)
    {
        var l = lhs!;
        var r = rhs!;
        if (l.Kind != r.Kind)
        {
            return false;
        }

        switch (l.Kind)
        {
            case Kind.Identifier:
                return l.Text() == r.Text();
            case Kind.ThisKeyword:
                return true;
            case Kind.JsxNamespacedName:
                return l.AsJsxNamespacedName()!.Namespace!.Text() == r.AsJsxNamespacedName()!.Namespace!.Text() &&
                    l.AsJsxNamespacedName()!.Name()!.Text() == r.AsJsxNamespacedName()!.Name()!.Text();
            case Kind.PropertyAccessExpression:
                return l.AsPropertyAccessExpression()!.Name()!.Text() == r.AsPropertyAccessExpression()!.Name()!.Text() &&
                    TagNamesAreEquivalent(l.Expression(), r.Expression());
        }

        throw new DebugFailureException("Unhandled case in TagNamesAreEquivalent");
    }

    /// <summary>Go: <c>func IsTagName(node *Node) bool</c>。</summary>
    public static bool IsTagName(Node? node)
    {
        var n = node!;
        var parent = n.Parent;
        return parent is not null && IsJSDocTag(parent) && parent.TagName() == n;
    }

    // We want to store any numbers/strings if they were a name that could be
    // related to a declaration.  So, if we have 'import x = require("something")'
    // then we want 'something' to be in the name table.  Similarly, if we have
    // "a['propname']" then we want to store "propname" in the name table.
    /// <summary>Go: <c>func literalIsName(node *Node) bool</c>。</summary>
    internal static bool LiteralIsName(Node? node)
    {
        return IsDeclarationName(node!) ||
            node!.Parent!.Kind == Kind.ExternalModuleReference ||
            IsArgumentOfElementAccessExpression(node) ||
            IsLiteralComputedPropertyDeclarationName(node!);
    }

    /// <summary>Go: <c>func isArgumentOfElementAccessExpression(node *Node) bool</c>。</summary>
    internal static bool IsArgumentOfElementAccessExpression(Node? node)
    {
        return node is not null && node.Parent is not null &&
            node.Parent.Kind == Kind.ElementAccessExpression &&
            node.Parent.AsElementAccessExpression()!.ArgumentExpression == node;
    }

    // If the given node is part of a subtree of JSDoc nodes that have been cloned into a reparsed construct,
    // return the corresponding reparsed clone in the subtree. Otherwise, just return the node.
    /// <summary>Go: <c>func GetReparsedNodeForNode(node *Node) *Node</c>。</summary>
    public static Node? GetReparsedNodeForNode(Node? node)
    {
        if (node is not null && (node.Flags & NodeFlags.JSDoc) != 0 && (node.Flags & NodeFlags.Reparsed) == 0)
        {
            var file = GetSourceFileOfNode(node!);
            if (file is not null && file.ReparsedClones is { Length: > 0 } clones)
            {
                // Go: pos, found := slices.BinarySearchFunc(file.ReparsedClones, node, CompareNodePositions)
                // C# 无对应公开 API，就地展开：low 收敛到第一个 cmp(a[i], target) >= 0 的下标（找不到时是插入点）。
                var low = 0;
                var high = clones.Length;
                while (low < high)
                {
                    var mid = low + ((high - low) >> 1);
                    if (CompareNodePositions(clones[mid]!, node) < 0)
                    {
                        low = mid + 1;
                    }
                    else
                    {
                        high = mid;
                    }
                }

                var pos = low;
                var found = pos < clones.Length && CompareNodePositions(clones[pos]!, node) == 0;
                if (!found && pos > 0)
                {
                    pos--;
                }

                var candidate = clones[pos]!;
                if (node.Loc.ContainedBy(candidate.Loc))
                {
                    if (FindCloneInNode(candidate, node) is { } reparsed)
                    {
                        return reparsed;
                    }
                }
            }
        }

        return node;
    }

    /// <summary>Go: <c>func findCloneInNode(node *Node, original *Node) *Node</c>。</summary>
    internal static Node? FindCloneInNode(Node? node, Node? original)
    {
        var current = node!;
        var target = original!;
        while (true)
        {
            if (current.Kind == target.Kind && current.Loc == target.Loc)
            {
                return current;
            }

            // Go 的闭包在这里直接回写形参（`node = n`）；C# 用 next 承接，语义相同。
            var next = current;
            var foundContainingChild = current.ForEachChild(n =>
            {
                if (target.Loc.ContainedBy(n.Loc))
                {
                    next = n;
                    return true;
                }

                return false;
            });
            current = next;
            if (!foundContainingChild)
            {
                return null;
            }
        }
    }

    /// <summary>Go: <c>func IsExpandoPropertyDeclaration(node *Node) bool</c>。</summary>
    public static bool IsExpandoPropertyDeclaration(Node? node)
    {
        return node is not null && IsBinaryExpression(node);
    }

    // IsSuperProperty checks if a node is super.x or super[x].
    /// <summary>Go: <c>func IsSuperProperty(node *Node) bool</c>。</summary>
    public static bool IsSuperProperty(Node? node)
    {
        var n = node!;
        return (IsPropertyAccessExpression(n) || IsElementAccessExpression(n)) &&
            n.Expression()!.Kind == Kind.SuperKeyword;
    }

    // Indicates whether a node is a potential source of an assigned name for a class, function, or arrow function.
    /// <summary>Go: <c>func IsNamedEvaluationSource(node *Node) bool</c>。</summary>
    public static bool IsNamedEvaluationSource(Node? node)
    {
        var n = node!;
        switch (n.Kind)
        {
            case Kind.PropertyAssignment:
                return !IsProtoSetter(n.AsPropertyAssignment()!.Name());
            case Kind.ShorthandPropertyAssignment:
                return n.AsShorthandPropertyAssignment()!.ObjectAssignmentInitializer is not null;
            case Kind.VariableDeclaration:
                return IsIdentifier(n.AsVariableDeclaration()!.Name()!) && n.Initializer() is not null;
            case Kind.Parameter:
                return IsIdentifier(n.AsParameterDeclaration()!.Name()!) && n.Initializer() is not null &&
                    n.AsParameterDeclaration()!.DotDotDotToken is null;
            case Kind.BindingElement:
                return IsIdentifier(n.AsBindingElement()!.Name()!) && n.Initializer() is not null &&
                    n.AsBindingElement()!.DotDotDotToken is null;
            case Kind.PropertyDeclaration:
                return n.Initializer() is not null;
            case Kind.BinaryExpression:
                switch (n.AsBinaryExpression()!.OperatorToken!.Kind)
                {
                    case Kind.EqualsToken:
                    case Kind.AmpersandAmpersandEqualsToken:
                    case Kind.BarBarEqualsToken:
                    case Kind.QuestionQuestionEqualsToken:
                        return IsIdentifier(n.AsBinaryExpression()!.Left!);
                }

                break;
            case Kind.ExportAssignment:
                return true;
        }

        return false;
    }

    // Indicates whether a property name is the special `__proto__` property.
    // Per the ECMA-262 spec, this only matters for property assignments whose name is
    // the Identifier `__proto__`, or the string literal `"__proto__"`, but not for
    // computed property names.
    /// <summary>Go: <c>func IsProtoSetter(node *Node) bool</c>。</summary>
    public static bool IsProtoSetter(Node? node)
    {
        var n = node!;
        return (IsIdentifier(n) || IsStringLiteral(n)) && n.Text() == "__proto__";
    }

    /// <summary>Go: <c>func IsStringLiteralLikeType(node *Node) bool</c>。</summary>
    public static bool IsStringLiteralLikeType(Node? node)
    {
        var n = node!;
        return n.Kind == Kind.LiteralType &&
            IsStringLiteralLike(n.AsLiteralTypeNode()!.Literal!);
    }

}
