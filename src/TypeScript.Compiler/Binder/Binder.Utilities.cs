// ported from: internal/binder/binder.go:2560-2727（SetValueDeclaration / GetContainerFlags / isNarrowingExpression /
//   containsNarrowableReference / isNarrowableReference / hasNarrowableArgument / isNarrowingBinaryExpression /
//   isNarrowableOperand / isNarrowingTypeOfOperands）
//
// 与 Go 的偏离（C# 语言特性，逐条依据 docs/architecture.md §2/§3 与 docs/ast.md；其余与 Go 逐行对应）：
// 1) `ast.` 的包级函数按裸名调用（`using static TypeScript.Ast.Ast;`）。本文件在 `TypeScript.Binder` 下，
//    裸写 `Ast.Xxx` 会先解析到外层命名空间 `TypeScript.Ast`（docs/architecture.md §2 的"外层命名空间先被找到"
//    规则；Scanner/Utilities.cs 的文件头有同样说明）。
// 2) `node.Flags&ast.NodeFlagsOptionalChain != 0` → `(node.Flags & NodeFlags.OptionalChain) != 0`：C# 的 `&`
//    优先级低于 `!=`/`==`，必须加括号（Go 里 `&` 优先级更高）。逻辑 `&&`/`||` 的优先级两边一致，未改结构。
// 3) Go 的 `switch` + `fallthrough`（GetContainerFlags 里 GetAccessor/SetAccessor/MethodDeclaration 落到
//    Constructor/FunctionDeclaration/ClassStaticBlockDeclaration 的那一支）→ C# `goto case Kind.Constructor;`：
//    C# 没有 fallthrough，`goto case` 跳到同一个 switch 内那个 case body，语义与 Go 的"落到下一个 case body"相同。
// 4) `[]*ast.Node` 字段 `NodeList.Nodes` 在 C# 里是 `Node[]?`（ast 契约），Go 直接解引用的地方按 parser 的既有
//    不变量用 `!`（docs/architecture.md §3.5：`!` 只用在已证明非空处），例如 `call.Arguments!.Nodes!`。
//    其余 Go 里可能为 nil 的节点指针落成 `Node?`，同样在 Go 直接解引用处用 `!`（`expr.Expression()`、
//    `binary.OperatorToken`、`call.Expression` 等，依据是 kind 判定 + parser 保证，与 Parser 分片一致）。
// 5) `isNarrowingBinaryExpression(expr *ast.BinaryExpression)` 的参数是具体类型 → C# `BinaryExpression`
//    （Go 里就是 `*ast.BinaryExpression`，与它同级的 `isNarrowingExpression(expr *ast.Node)` 仍是 `Node`）。
// 6) 包级未导出函数 → `private static` + PascalCase（契约 §1.2）；Go 导出的 `SetValueDeclaration`/`GetContainerFlags`
//    → `public static`，签名照契约 §1.2。`IsNarrowingExpression` 跨分片被 W3 的 `bindCaseBlock` 调用、其余
//    `IsNarrowable*` 被 W4 的流分析调用，都是同一个 partial 类的 `private static`。
// 7) GetContainerFlags 上方的 `/** … */` 是 Go 源里就挂在那里的 doc comment（它描述的其实是 `declareSymbol`，
//    属于 Binder.cs 的范围）。1:1 保留；本工程 `GenerateDocumentationFile=false`，C# 不会把它当 XML 文档解析。
// 8) 热路径：`hasNarrowableArgument` 的 `for _, argument := range call.Arguments.Nodes` → 索引循环（契约 §2），
//    保留 Go 的 `//nolint:modernize` 注释。
namespace TypeScript.Binder;

using TypeScript.Ast;
using static TypeScript.Ast.Ast;

public sealed partial class Binder
{
    public static void SetValueDeclaration(Symbol symbol, Node node)
    {
        var valueDeclaration = symbol.ValueDeclaration;
        if (valueDeclaration is null ||
            IsAssignmentDeclaration(valueDeclaration) && !IsAssignmentDeclaration(node) ||
            valueDeclaration.Kind != node.Kind && IsEffectiveModuleDeclaration(valueDeclaration))
        {
            // Non-assignment declarations take precedence over assignment declarations and
            // non-namespace declarations take precedence over namespace declarations.
            symbol.ValueDeclaration = node;
        }
    }

    /**
     * Declares a Symbol for the node and adds it to symbols. Reports errors for conflicting identifier names.
     * @param symbolTable - The symbol table which node will be added to.
     * @param parent - node's parent declaration.
     * @param node - The declaration to be added to the symbol table
     * @param includes - The SymbolFlags that node has in addition to its declaration type (eg: export, ambient, etc.)
     * @param excludes - The flags which node cannot be declared alongside in a symbol table. Used to report forbidden declarations.
     */

    public static ContainerFlags GetContainerFlags(Node node)
    {
        switch (node.Kind)
        {
            case Kind.ClassExpression:
            case Kind.ClassDeclaration:
            case Kind.EnumDeclaration:
            case Kind.ObjectLiteralExpression:
            case Kind.TypeLiteral:
            case Kind.JsxAttributes:
                return ContainerFlags.IsContainer;
            case Kind.InterfaceDeclaration:
                return ContainerFlags.IsContainer | ContainerFlags.IsInterface;
            case Kind.ModuleDeclaration:
            case Kind.TypeAliasDeclaration:
            case Kind.JSTypeAliasDeclaration:
            case Kind.MappedType:
            case Kind.IndexSignature:
                return ContainerFlags.IsContainer | ContainerFlags.HasLocals;
            case Kind.SourceFile:
                return ContainerFlags.IsContainer | ContainerFlags.IsControlFlowContainer | ContainerFlags.HasLocals;
            case Kind.GetAccessor:
            case Kind.SetAccessor:
            case Kind.MethodDeclaration:
                if (IsObjectLiteralOrClassExpressionMethodOrAccessor(node))
                {
                    return ContainerFlags.IsContainer | ContainerFlags.IsControlFlowContainer | ContainerFlags.HasLocals | ContainerFlags.IsFunctionLike | ContainerFlags.IsObjectLiteralOrClassExpressionMethodOrAccessor | ContainerFlags.IsThisContainer;
                }
                goto case Kind.Constructor;
            case Kind.Constructor:
            case Kind.FunctionDeclaration:
            case Kind.ClassStaticBlockDeclaration:
                return ContainerFlags.IsContainer | ContainerFlags.IsControlFlowContainer | ContainerFlags.HasLocals | ContainerFlags.IsFunctionLike | ContainerFlags.IsThisContainer;
            case Kind.MethodSignature:
            case Kind.CallSignature:
            case Kind.FunctionType:
            case Kind.ConstructSignature:
            case Kind.ConstructorType:
                return ContainerFlags.IsContainer | ContainerFlags.IsControlFlowContainer | ContainerFlags.HasLocals | ContainerFlags.IsFunctionLike | ContainerFlags.PropagatesThisKeyword;
            case Kind.FunctionExpression:
                return ContainerFlags.IsContainer | ContainerFlags.IsControlFlowContainer | ContainerFlags.HasLocals | ContainerFlags.IsFunctionLike | ContainerFlags.IsFunctionExpression | ContainerFlags.IsThisContainer;
            case Kind.ArrowFunction:
                return ContainerFlags.IsContainer | ContainerFlags.IsControlFlowContainer | ContainerFlags.HasLocals | ContainerFlags.IsFunctionLike | ContainerFlags.IsFunctionExpression | ContainerFlags.PropagatesThisKeyword;
            case Kind.ModuleBlock:
                return ContainerFlags.IsControlFlowContainer;
            case Kind.PropertyDeclaration:
                if (node.Initializer() is not null)
                {
                    return ContainerFlags.IsControlFlowContainer | ContainerFlags.IsThisContainer;
                }
                else
                {
                    return ContainerFlags.None;
                }
            case Kind.CatchClause:
            case Kind.ForStatement:
            case Kind.ForInStatement:
            case Kind.ForOfStatement:
            case Kind.CaseBlock:
                return ContainerFlags.IsBlockScopedContainer | ContainerFlags.HasLocals;
            case Kind.Block:
                if (IsFunctionLike(node.Parent) || IsClassStaticBlockDeclaration(node.Parent!))
                {
                    return ContainerFlags.None;
                }
                else
                {
                    return ContainerFlags.IsBlockScopedContainer | ContainerFlags.HasLocals;
                }
        }
        return ContainerFlags.None;
    }

    private static bool IsNarrowingExpression(Node expr)
    {
        switch (expr.Kind)
        {
            case Kind.Identifier:
            case Kind.ThisKeyword:
                return true;
            case Kind.PropertyAccessExpression:
            case Kind.ElementAccessExpression:
                return ContainsNarrowableReference(expr);
            case Kind.CallExpression:
                return HasNarrowableArgument(expr);
            case Kind.ParenthesizedExpression:
            case Kind.NonNullExpression:
            case Kind.TypeOfExpression:
                return IsNarrowingExpression(expr.Expression()!);
            case Kind.BinaryExpression:
                return IsNarrowingBinaryExpression(expr.AsBinaryExpression()!);
            case Kind.PrefixUnaryExpression:
                return expr.AsPrefixUnaryExpression()!.Operator == Kind.ExclamationToken && IsNarrowingExpression(expr.AsPrefixUnaryExpression()!.Operand!);
        }
        return false;
    }

    private static bool ContainsNarrowableReference(Node expr)
    {
        if (IsNarrowableReference(expr))
        {
            return true;
        }
        if ((expr.Flags & NodeFlags.OptionalChain) != 0)
        {
            switch (expr.Kind)
            {
                case Kind.PropertyAccessExpression:
                case Kind.ElementAccessExpression:
                case Kind.CallExpression:
                case Kind.NonNullExpression:
                    return ContainsNarrowableReference(expr.Expression()!);
            }
        }
        return false;
    }

    private static bool IsNarrowableReference(Node node)
    {
        switch (node.Kind)
        {
            case Kind.Identifier:
            case Kind.ThisKeyword:
            case Kind.SuperKeyword:
            case Kind.MetaProperty:
                return true;
            case Kind.PropertyAccessExpression:
            case Kind.ParenthesizedExpression:
            case Kind.NonNullExpression:
                return IsNarrowableReference(node.Expression()!);
            case Kind.ElementAccessExpression:
                var expr = node.AsElementAccessExpression()!;
                return IsStringOrNumericLiteralLike(expr.ArgumentExpression) ||
                    IsEntityNameExpression(expr.ArgumentExpression) && IsNarrowableReference(expr.Expression!);
            case Kind.BinaryExpression:
                var binary = node.AsBinaryExpression()!;
                return binary.OperatorToken!.Kind == Kind.CommaToken && IsNarrowableReference(binary.Right!) ||
                    IsAssignmentOperator(binary.OperatorToken!.Kind) && IsLeftHandSideExpression(binary.Left);
        }
        return false;
    }

    private static bool HasNarrowableArgument(Node expr)
    {
        var call = expr.AsCallExpression()!;
        var arguments = call.Arguments!.Nodes ?? []; //nolint:modernize
        for (var i = 0; i < arguments.Length; i++)
        {
            if (ContainsNarrowableReference(arguments[i]))
            {
                return true;
            }
        }
        if (IsPropertyAccessExpression(call.Expression!))
        {
            if (ContainsNarrowableReference(call.Expression!.Expression()!))
            {
                return true;
            }
        }
        return false;
    }

    private static bool IsNarrowingBinaryExpression(BinaryExpression expr)
    {
        switch (expr.OperatorToken!.Kind)
        {
            case Kind.EqualsToken:
            case Kind.BarBarEqualsToken:
            case Kind.AmpersandAmpersandEqualsToken:
            case Kind.QuestionQuestionEqualsToken:
                return ContainsNarrowableReference(expr.Left!);
            case Kind.EqualsEqualsToken:
            case Kind.ExclamationEqualsToken:
            case Kind.EqualsEqualsEqualsToken:
            case Kind.ExclamationEqualsEqualsToken:
                var left = SkipParentheses(expr.Left)!;
                var right = SkipParentheses(expr.Right)!;
                return IsNarrowableOperand(left) || IsNarrowableOperand(right) ||
                    IsNarrowingTypeOfOperands(right, left) || IsNarrowingTypeOfOperands(left, right) ||
                    (IsBooleanLiteral(right) && IsNarrowingExpression(left) || IsBooleanLiteral(left) && IsNarrowingExpression(right));
            case Kind.InstanceOfKeyword:
                return IsNarrowableOperand(expr.Left!);
            case Kind.InKeyword:
                return IsNarrowingExpression(expr.Right!);
            case Kind.CommaToken:
                return IsNarrowingExpression(expr.Right!);
        }
        return false;
    }

    private static bool IsNarrowableOperand(Node expr)
    {
        switch (expr.Kind)
        {
            case Kind.ParenthesizedExpression:
                return IsNarrowableOperand(expr.Expression()!);
            case Kind.BinaryExpression:
                var binary = expr.AsBinaryExpression()!;
                switch (binary.OperatorToken!.Kind)
                {
                    case Kind.EqualsToken:
                        return IsNarrowableOperand(binary.Left!);
                    case Kind.CommaToken:
                        return IsNarrowableOperand(binary.Right!);
                }
                break;
        }
        return ContainsNarrowableReference(expr);
    }

    private static bool IsNarrowingTypeOfOperands(Node expr1, Node expr2)
    {
        return IsTypeOfExpression(expr1) && IsNarrowableOperand(expr1.Expression()!) && IsStringLiteralLike(expr2);
    }
}
