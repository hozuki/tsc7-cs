// ported from: internal/ast/precedence.go
//
// 与 Go 的差异（C# 语言特性；依据 docs/architecture.md §2、§3.3）：
// - Go 的 `type Xxx = Node` 别名（`Expression`/`TypeNode`）在 C# 里就是 `Node`（docs/ast.md §2）。
// - 包级函数落在静态类 `Ast`；未导出函数（getOperator）→ private static `GetOperator`。
// - `Kind` 的枚举成员名按 §3.3 去 `Kind` 前缀（Go 的 `KindCommaToken` → `Kind.CommaToken`）；
//   `Kind.String()` 是 KindStringer.Generated.cs 里的扩展方法，Go 的 `%v` 打印 kind → `n.Kind.String()`。
// - 枚举别名（Go 的 `OperatorPrecedenceLowest = OperatorPrecedenceComma` 等）在 C# 的 enum 里不能做别名成员
//   （会污染 ToString），落成静态类 `Ast` 里的 `const`（§3.3）。`OperatorPrecedenceInvalid = -1` 同理。
// - Go 的 `*T` 参数保留 nil 语义 → `T?`；Go 里不判空就解引用的地方用 `param!`（nil 时 C# 抛 NRE，等价于 Go 的 panic）。
// - Go 的 `panic(fmt.Sprintf(...))` → `throw new DebugFailureException(...)`。
// - Kind 判定后的 `x.AsXxx()`（Go 的 type assertion，不匹配即 panic）→ C# 生成物的 `AsXxx()!`（本项目在
//   Kind 已判定的 switch 里统一这么写，见 Node.cs 的同类 switch）。
// - Go 源里大段的文法注释只保留摘要；完整推导见 Go 源文件。
using TypeScript.Core;

namespace TypeScript.Ast;

/// <summary>
/// Go: <c>type OperatorPrecedence int</c>。表达式优先级（数值与 Go 一致；每条的文法见 Go 源的同名注释）。
/// </summary>
public enum OperatorPrecedence
{
    /// <summary>Expression: AssignmentExpression | Expression `,` AssignmentExpression</summary>
    Comma = 0,

    /// <summary>SpreadElement: `...` AssignmentExpression（高于 Comma 是因为 |ElementList| 的解析方式）。</summary>
    Spread = 1,

    /// <summary>YieldExpression: `yield` / `yield` AssignmentExpression / `yield` `*` AssignmentExpression。</summary>
    Yield = 2,

    /// <summary>LeftHandSideExpression `=`/`*=`/…/`**=` AssignmentExpression。</summary>
    Assignment = 3,

    /// <summary>ConditionalExpression（实际上与 Assignment 同级，这里算更高）。</summary>
    Conditional = 4,

    /// <summary>LogicalORExpression。</summary>
    LogicalOR = 5,

    /// <summary>LogicalANDExpression。</summary>
    LogicalAND = 6,

    /// <summary>BitwiseORExpression。</summary>
    BitwiseOR = 7,

    /// <summary>BitwiseXORExpression。</summary>
    BitwiseXOR = 8,

    /// <summary>BitwiseANDExpression。</summary>
    BitwiseAND = 9,

    /// <summary>EqualityExpression（`==` `!=` `===` `!==`）。</summary>
    Equality = 10,

    /// <summary>RelationalExpression（`&lt;` `&gt;` `&lt;=` `&gt;=` `instanceof` `in`，以及 TS 的 `as`）。</summary>
    Relational = 11,

    /// <summary>ShiftExpression（`&lt;&lt;` `&gt;&gt;` `&gt;&gt;&gt;`）。</summary>
    Shift = 12,

    /// <summary>AdditiveExpression（`+` `-`）。</summary>
    Additive = 13,

    /// <summary>MultiplicativeExpression（`*` `/` `%`）。</summary>
    Multiplicative = 14,

    /// <summary>ExponentiationExpression（`**`）。</summary>
    Exponentiation = 15,

    /// <summary>UnaryExpression（`delete` `void` `typeof` `+` `-` `~` `!`、await，以及 `++`/`--` 前缀）。</summary>
    Unary = 16,

    /// <summary>UpdateExpression（`x++` / `x--`）。</summary>
    Update = 17,

    /// <summary>LeftHandSideExpression: NewExpression（`new` 不带参数的形态）。</summary>
    LeftHandSide = 18,

    /// <summary>OptionalExpression（成员/调用链上的 `?.`）。</summary>
    OptionalChain = 19,

    /// <summary>CallExpression/MemberExpression（成员访问、下标、调用、模板调用、`new X(...)`）。</summary>
    Member = 20,

    /// <summary>PrimaryExpression（字面量、标识符、数组/对象字面量、函数/类表达式等）。</summary>
    Primary = 21,

    /// <summary>CoverParenthesizedExpressionAndArrowParameterList。</summary>
    Parentheses = 22,
}

/// <summary>Go: <c>type OperatorPrecedenceFlags int</c>。</summary>
[Flags]
public enum OperatorPrecedenceFlags
{
    None = 0,

    /// <summary>Go: <c>OperatorPrecedenceFlagsNewWithoutArguments</c>。</summary>
    NewWithoutArguments = 1 << 0,

    /// <summary>Go: <c>OperatorPrecedenceFlagsOptionalChain</c>。</summary>
    OptionalChain = 1 << 1,
}

/// <summary>Go: <c>type TypePrecedence int32</c>。类型节点的优先级（数值与 Go 一致）。</summary>
public enum TypePrecedence
{
    /// <summary>ConditionalTypeNode（最低）。</summary>
    Conditional = 0,

    /// <summary>JSDoc 的可选/可变类型（`...`? Type `=`?）。</summary>
    JSDoc = 1,

    /// <summary>FunctionTypeNode / ConstructorTypeNode。</summary>
    Function = 2,

    /// <summary>UnionTypeNode。</summary>
    Union = 3,

    /// <summary>IntersectionTypeNode。</summary>
    Intersection = 4,

    /// <summary>TypeOperatorNode / InferTypeNode / `keyof` / `unique` / `readonly`。</summary>
    TypeOperator = 5,

    /// <summary>PostfixType：OptionalTypeNode / ArrayTypeNode / IndexedAccessTypeNode。</summary>
    Postfix = 6,

    /// <summary>NonArrayType（最高）：关键字类型、字面量类型、引用/字面量/元组/映射类型等。</summary>
    NonArray = 7,
}

public static partial class Ast
{
    // Go: OperatorPrecedenceLowest / OperatorPrecedenceHighest / OperatorPrecedenceDisallowComma /
    //     OperatorPrecedenceCoalesce / OperatorPrecedenceInvalid（enum 别名 → const，§3.3）。
    public const OperatorPrecedence OperatorPrecedenceLowest = OperatorPrecedence.Comma;
    public const OperatorPrecedence OperatorPrecedenceHighest = OperatorPrecedence.Parentheses;
    public const OperatorPrecedence OperatorPrecedenceDisallowComma = OperatorPrecedence.Yield;

    /// <summary>CoalesceExpressionHead（`??` 与 `||` 同级）。</summary>
    public const OperatorPrecedence OperatorPrecedenceCoalesce = OperatorPrecedence.LogicalOR;

    /// <summary>比所有其它优先级都低：返回它会让二元表达式解析停下来。</summary>
    public const OperatorPrecedence OperatorPrecedenceInvalid = (OperatorPrecedence)(-1);

    // Go: TypePrecedenceLowest / TypePrecedenceHighest。
    public const TypePrecedence TypePrecedenceLowest = TypePrecedence.Conditional;
    public const TypePrecedence TypePrecedenceHighest = TypePrecedence.NonArray;

    /// <summary>Go: <c>func getOperator(expression *Expression) Kind</c>。</summary>
    private static Kind GetOperator(Node expression)
    {
        switch (expression.Kind)
        {
            case Kind.BinaryExpression:
                return expression.AsBinaryExpression()!.OperatorToken!.Kind;
            case Kind.PrefixUnaryExpression:
                return expression.AsPrefixUnaryExpression()!.Operator;
            case Kind.PostfixUnaryExpression:
                return expression.AsPostfixUnaryExpression()!.Operator;
            default:
                return expression.Kind;
        }
    }

    /// <summary>Go: <c>func GetExpressionPrecedence(expression *Expression) OperatorPrecedence</c>。</summary>
    public static OperatorPrecedence GetExpressionPrecedence(Node? expression)
    {
        // Go 不判空：nil 会 panic；C# 侧对应 NRE。
        var node = expression!;
        var op = GetOperator(node);
        var flags = OperatorPrecedenceFlags.None;
        if (node.Kind == Kind.NewExpression && node.ArgumentList() is null)
        {
            flags = OperatorPrecedenceFlags.NewWithoutArguments;
        }
        else if (IsOptionalChain(node))
        {
            flags = OperatorPrecedenceFlags.OptionalChain;
        }

        return GetOperatorPrecedence(node.Kind, op, flags);
    }

    /// <summary>Go: <c>func GetOperatorPrecedence(nodeKind Kind, operatorKind Kind, flags OperatorPrecedenceFlags) OperatorPrecedence</c>。</summary>
    public static OperatorPrecedence GetOperatorPrecedence(Kind nodeKind, Kind operatorKind, OperatorPrecedenceFlags flags)
    {
        switch (nodeKind)
        {
            case Kind.SpreadElement:
                return OperatorPrecedence.Spread;
            case Kind.YieldExpression:
                return OperatorPrecedence.Yield;

            // !!! 与旧编译器不同（为了与 ParenthesizerRules 对齐），可考虑回移。
            case Kind.ArrowFunction:
                return OperatorPrecedence.Assignment;
            case Kind.ConditionalExpression:
                return OperatorPrecedence.Conditional;
            case Kind.BinaryExpression:
                switch (operatorKind)
                {
                    case Kind.CommaToken:
                        return OperatorPrecedence.Comma;

                    case Kind.EqualsToken:
                    case Kind.PlusEqualsToken:
                    case Kind.MinusEqualsToken:
                    case Kind.AsteriskAsteriskEqualsToken:
                    case Kind.AsteriskEqualsToken:
                    case Kind.SlashEqualsToken:
                    case Kind.PercentEqualsToken:
                    case Kind.LessThanLessThanEqualsToken:
                    case Kind.GreaterThanGreaterThanEqualsToken:
                    case Kind.GreaterThanGreaterThanGreaterThanEqualsToken:
                    case Kind.AmpersandEqualsToken:
                    case Kind.CaretEqualsToken:
                    case Kind.BarEqualsToken:
                    case Kind.BarBarEqualsToken:
                    case Kind.AmpersandAmpersandEqualsToken:
                    case Kind.QuestionQuestionEqualsToken:
                        return OperatorPrecedence.Assignment;

                    default:
                        return GetBinaryOperatorPrecedence(operatorKind);
                }

            // TODO: 前缀 `++`/`--` 是否该挪到 `Update` 优先级？
            case Kind.TypeAssertionExpression:
            case Kind.NonNullExpression:
            case Kind.PrefixUnaryExpression:
            case Kind.TypeOfExpression:
            case Kind.VoidExpression:
            case Kind.DeleteExpression:
            case Kind.AwaitExpression:
                return OperatorPrecedence.Unary;

            case Kind.PostfixUnaryExpression:
                return OperatorPrecedence.Update;

            // !!! 与旧编译器不同（为了与 ParenthesizerRules 对齐），可考虑回移。
            case Kind.PropertyAccessExpression:
            case Kind.ElementAccessExpression:
                if ((flags & OperatorPrecedenceFlags.OptionalChain) != 0)
                {
                    return OperatorPrecedence.OptionalChain;
                }

                return OperatorPrecedence.Member;

            case Kind.CallExpression:
                if ((flags & OperatorPrecedenceFlags.OptionalChain) != 0)
                {
                    return OperatorPrecedence.OptionalChain;
                }

                return OperatorPrecedence.Member;

            // !!! 与旧编译器不同（为了与 ParenthesizerRules 对齐），可考虑回移。
            case Kind.NewExpression:
                if ((flags & OperatorPrecedenceFlags.NewWithoutArguments) != 0)
                {
                    return OperatorPrecedence.LeftHandSide;
                }

                return OperatorPrecedence.Member;

            // !!! 与旧编译器不同（为了与 ParenthesizerRules 对齐），可考虑回移。
            case Kind.TaggedTemplateExpression:
            case Kind.MetaProperty:
            case Kind.ExpressionWithTypeArguments:
                return OperatorPrecedence.Member;

            case Kind.AsExpression:
            case Kind.SatisfiesExpression:
                return OperatorPrecedence.Relational;

            case Kind.ThisKeyword:
            case Kind.SuperKeyword:
            case Kind.ImportKeyword:
            case Kind.Identifier:
            case Kind.PrivateIdentifier:
            case Kind.NullKeyword:
            case Kind.TrueKeyword:
            case Kind.FalseKeyword:
            case Kind.NumericLiteral:
            case Kind.BigIntLiteral:
            case Kind.StringLiteral:
            case Kind.ArrayLiteralExpression:
            case Kind.ObjectLiteralExpression:
            case Kind.FunctionExpression:
            case Kind.ClassExpression:
            case Kind.RegularExpressionLiteral:
            case Kind.NoSubstitutionTemplateLiteral:
            case Kind.TemplateExpression:
            case Kind.OmittedExpression:
            case Kind.JsxElement:
            case Kind.JsxSelfClosingElement:
            case Kind.JsxFragment:
            case Kind.MissingDeclaration:
                return OperatorPrecedence.Primary;

            // !!! 与旧编译器不同（为了支持 emit），可考虑回移。
            case Kind.ParenthesizedExpression:
                return OperatorPrecedence.Parentheses;

            default:
                return OperatorPrecedenceInvalid;
        }
    }

    /// <summary>Go: <c>func GetBinaryOperatorPrecedence(operatorKind Kind) OperatorPrecedence</c>。</summary>
    public static OperatorPrecedence GetBinaryOperatorPrecedence(Kind operatorKind)
    {
        switch (operatorKind)
        {
            case Kind.QuestionQuestionToken:
                return OperatorPrecedenceCoalesce;
            case Kind.BarBarToken:
                return OperatorPrecedence.LogicalOR;
            case Kind.AmpersandAmpersandToken:
                return OperatorPrecedence.LogicalAND;
            case Kind.BarToken:
                return OperatorPrecedence.BitwiseOR;
            case Kind.CaretToken:
                return OperatorPrecedence.BitwiseXOR;
            case Kind.AmpersandToken:
                return OperatorPrecedence.BitwiseAND;
            case Kind.EqualsEqualsToken:
            case Kind.ExclamationEqualsToken:
            case Kind.EqualsEqualsEqualsToken:
            case Kind.ExclamationEqualsEqualsToken:
                return OperatorPrecedence.Equality;
            case Kind.LessThanToken:
            case Kind.GreaterThanToken:
            case Kind.LessThanEqualsToken:
            case Kind.GreaterThanEqualsToken:
            case Kind.InstanceOfKeyword:
            case Kind.InKeyword:
            case Kind.AsKeyword:
            case Kind.SatisfiesKeyword:
                return OperatorPrecedence.Relational;
            case Kind.LessThanLessThanToken:
            case Kind.GreaterThanGreaterThanToken:
            case Kind.GreaterThanGreaterThanGreaterThanToken:
                return OperatorPrecedence.Shift;
            case Kind.PlusToken:
            case Kind.MinusToken:
                return OperatorPrecedence.Additive;
            case Kind.AsteriskToken:
            case Kind.SlashToken:
            case Kind.PercentToken:
                return OperatorPrecedence.Multiplicative;
            case Kind.AsteriskAsteriskToken:
                return OperatorPrecedence.Exponentiation;
        }

        // 比所有其它优先级都低：返回它会让二元表达式解析停下来。
        return OperatorPrecedenceInvalid;
    }

    /// <summary>
    /// Go: <c>func GetLeftmostExpression(node *Expression, stopAtCallExpressions bool) *Expression</c>。
    /// 取表达式最左侧的子表达式（`a.b`、`a[b]`、`a++`、`a+b`、`a?b:c`、`a as B` 里的 `a` 等）。
    /// 循环里的 `!` 是"Kind 已判定 + Go 同样解引用（nil 即 panic）"的同一证明。
    /// </summary>
    public static Node GetLeftmostExpression(Node node, bool stopAtCallExpressions)
    {
        while (true)
        {
            switch (node.Kind)
            {
                case Kind.PostfixUnaryExpression:
                    node = node.AsPostfixUnaryExpression()!.Operand!;
                    continue;
                case Kind.BinaryExpression:
                    node = node.AsBinaryExpression()!.Left!;
                    continue;
                case Kind.ConditionalExpression:
                    node = node.AsConditionalExpression()!.Condition!;
                    continue;
                case Kind.TaggedTemplateExpression:
                    node = node.AsTaggedTemplateExpression()!.Tag!;
                    continue;
                case Kind.CallExpression:
                    if (stopAtCallExpressions)
                    {
                        return node;
                    }

                    // Go 的 fallthrough（落到下面一组解引用的 kind）。
                    goto case Kind.AsExpression;
                case Kind.AsExpression:
                case Kind.ElementAccessExpression:
                case Kind.PropertyAccessExpression:
                case Kind.NonNullExpression:
                case Kind.PartiallyEmittedExpression:
                case Kind.SatisfiesExpression:
                    node = node.Expression()!;
                    continue;
                default:
                    return node;
            }
        }
    }

    /// <summary>Go: <c>func GetTypeNodePrecedence(n *TypeNode) TypePrecedence</c>。</summary>
    public static TypePrecedence GetTypeNodePrecedence(Node? n)
    {
        // Go 不判空：nil 会 panic；C# 侧对应 NRE。
        var node = n!;
        switch (node.Kind)
        {
            case Kind.ConditionalType:
                return TypePrecedence.Conditional;
            case Kind.JSDocOptionalType:
            case Kind.JSDocVariadicType:
                return TypePrecedence.JSDoc;
            case Kind.FunctionType:
            case Kind.ConstructorType:
                return TypePrecedence.Function;
            case Kind.UnionType:
                return TypePrecedence.Union;
            case Kind.IntersectionType:
                return TypePrecedence.Intersection;
            case Kind.TypeOperator:
                return TypePrecedence.TypeOperator;
            case Kind.InferType:
                if (node.AsInferTypeNode()!.TypeParameter!.AsTypeParameterDeclaration()!.Constraint is not null)
                {
                    // `infer T extends U` 必须按 FunctionTypeNode 的优先级处理，因为 `extends` 会急切消费 TypeNode。
                    return TypePrecedence.Function;
                }

                return TypePrecedence.TypeOperator;
            case Kind.IndexedAccessType:
            case Kind.ArrayType:
            case Kind.OptionalType:
                return TypePrecedence.Postfix;
            case Kind.TypeQuery:
                // TypeQueryNode 其实是 NonArrayType，但按 TypeOperatorNode 的优先级处理，这样用在
                // PostfixType 上下文里会被加括号（`(typeof C)[]` 而不是 `typeof C[]`）。
                return TypePrecedence.TypeOperator;
            case Kind.AnyKeyword:
            case Kind.UnknownKeyword:
            case Kind.StringKeyword:
            case Kind.NumberKeyword:
            case Kind.BigIntKeyword:
            case Kind.SymbolKeyword:
            case Kind.BooleanKeyword:
            case Kind.UndefinedKeyword:
            case Kind.NeverKeyword:
            case Kind.ObjectKeyword:
            case Kind.IntrinsicKeyword:
            case Kind.VoidKeyword:
            case Kind.JSDocAllType:
            case Kind.JSDocNullableType:
            case Kind.JSDocNonNullableType:
            case Kind.LiteralType:
            case Kind.TypePredicate:
            case Kind.TypeReference:
            case Kind.TypeLiteral:
            case Kind.TupleType:
            case Kind.RestType:
            case Kind.ParenthesizedType:
            case Kind.ThisType:
            case Kind.MappedType:
            case Kind.NamedTupleMember:
            case Kind.TemplateLiteralType:
            case Kind.ImportType:

            // 这些出现在伪类型里，如 `f<T>.C`（f 是泛型函数、C 是局部类型）。
            case Kind.PropertyAccessExpression:
            case Kind.ExpressionWithTypeArguments:
                return TypePrecedence.NonArray;
            default:
                throw new DebugFailureException("unhandled TypeNode: " + node.Kind.String());
        }
    }
}
