// ported from: internal/evaluator/evaluator.go
//
// 与 Go 的差异（逐条）：
// 1) **包级函数放哪里**：Go 的 `type Evaluator func(expr *ast.Node, location *ast.Node) Result` 是导出类型，
//    按 docs/architecture.md §2 的"包级函数放同名静态类"规则，那个静态类也叫 `Evaluator`，与委托类型同名会撞
//    （CS0101）。这里优先保住 Go 的**类型名**：委托就叫 `Evaluator`（调用点 `Evaluator` 作类型用），包级函数
//    （`NewResult`/`NewEvaluator`/`AnyToString`/`IsTruthy`）放进 `EvaluatorFunctions`。调用点用
//    `using static TypeScript.Evaluator.EvaluatorFunctions;` 后与 Go 的裸函数名逐字一致。
// 2) Go 的 `any` → C# `object?`：Go 里 `Value any` 可以装 nil，对应 C# 的 null。
//    `v.(jsnum.Number)` 这类类型断言 → `v is Number n`（装箱后的结构体按类型精确匹配，语义一致）。
// 3) Go 的 `Result{...}` 复合字面量 → C# 有参构造器（字段顺序与 Go 完全一致）。
// 4) `jsnum.Number.String()` / `jsnum.PseudoBigInt.String()` 在 C# 里是 `using static TypeScript.JsNum.JsNum`
//    导入的扩展方法（docs/architecture.md §3.3），调用点写法与 Go 相同。
// 5) `panic("Unhandled case in …")` → `throw new DebugFailureException("Unhandled case in …")`（与 Ast/Utilities.cs 同款）。
namespace TypeScript.Evaluator;

using System.Text;
using TypeScript.Ast;
using TypeScript.JsNum;
using static TypeScript.Ast.Ast;
using static TypeScript.Core.Core;
using static TypeScript.JsNum.JsNum;

public readonly struct Result
{
    public readonly object? Value;
    public readonly bool IsSyntacticallyString;
    public readonly bool ResolvedOtherFiles;
    public readonly bool HasExternalReferences;

    public Result(object? value, bool isSyntacticallyString, bool resolvedOtherFiles, bool hasExternalReferences)
    {
        Value = value;
        IsSyntacticallyString = isSyntacticallyString;
        ResolvedOtherFiles = resolvedOtherFiles;
        HasExternalReferences = hasExternalReferences;
    }
}

/// <summary>Go 的 <c>type Evaluator func(expr *ast.Node, location *ast.Node) Result</c>。</summary>
public delegate Result Evaluator(Node expr, Node? location);

/// <summary>Go 的 evaluator 包级函数（见文件头第 1 条）。</summary>
public static class EvaluatorFunctions
{
    public static Result NewResult(object? value, bool isSyntacticallyString, bool resolvedOtherFiles, bool hasExternalReferences)
    {
        return new Result(value, isSyntacticallyString, resolvedOtherFiles, hasExternalReferences);
    }

    public static Evaluator NewEvaluator(Evaluator evaluateEntity, OuterExpressionKinds outerExpressionsToSkip)
    {
        Result Evaluate(Node expr, Node? location)
        {
            var isSyntacticallyString = false;
            var resolvedOtherFiles = false;
            var hasExternalReferences = false;
            // It's unclear when/whether we should consider skipping other kinds of outer expressions.
            // Type assertions intentionally break evaluation when evaluating literal types, such as:
            //     type T = `one ${"two" as any} three`; // string
            // But it's less clear whether such an assertion should break enum member evaluation:
            //     enum E {
            //       A = "one" as any
            //     }
            // SatisfiesExpressions and non-null assertions seem to have even less reason to break
            // emitting enum members as literals. However, these expressions also break Babel's
            // evaluation (but not esbuild's), and the isolatedModules errors we give depend on
            // our evaluation results, so we're currently being conservative so as to issue errors
            // on code that might break Babel.
            expr = SkipOuterExpressions(expr, outerExpressionsToSkip | OuterExpressionKinds.Parentheses)!;
            switch (expr.Kind)
            {
                case Kind.PrefixUnaryExpression:
                    {
                        var result = Evaluate(expr.AsPrefixUnaryExpression()!.Operand!, location);
                        resolvedOtherFiles = result.ResolvedOtherFiles;
                        hasExternalReferences = result.HasExternalReferences;
                        if (result.Value is Number value)
                        {
                            switch (expr.AsPrefixUnaryExpression()!.Operator)
                            {
                                case Kind.PlusToken:
                                    return new Result(value, isSyntacticallyString, resolvedOtherFiles, hasExternalReferences);
                                case Kind.MinusToken:
                                    return new Result(-value, isSyntacticallyString, resolvedOtherFiles, hasExternalReferences);
                                case Kind.TildeToken:
                                    return new Result(value.BitwiseNOT(), isSyntacticallyString, resolvedOtherFiles, hasExternalReferences);
                            }
                        }

                        break;
                    }

                case Kind.BinaryExpression:
                    {
                        var bin = expr.AsBinaryExpression()!;
                        var left = Evaluate(bin.Left!, location);
                        var right = Evaluate(bin.Right!, location);
                        var @operator = bin.OperatorToken!.Kind;
                        isSyntacticallyString = (left.IsSyntacticallyString || right.IsSyntacticallyString) && bin.OperatorToken!.Kind == Kind.PlusToken;
                        resolvedOtherFiles = left.ResolvedOtherFiles || right.ResolvedOtherFiles;
                        hasExternalReferences = left.HasExternalReferences || right.HasExternalReferences;
                        var leftIsNum = TryNumber(left.Value, out var leftNum);
                        var rightIsNum = TryNumber(right.Value, out var rightNum);
                        if (leftIsNum && rightIsNum)
                        {
                            switch (@operator)
                            {
                                case Kind.BarToken:
                                    return new Result(leftNum.BitwiseOR(rightNum), isSyntacticallyString, resolvedOtherFiles, hasExternalReferences);
                                case Kind.AmpersandToken:
                                    return new Result(leftNum.BitwiseAND(rightNum), isSyntacticallyString, resolvedOtherFiles, hasExternalReferences);
                                case Kind.GreaterThanGreaterThanToken:
                                    return new Result(leftNum.SignedRightShift(rightNum), isSyntacticallyString, resolvedOtherFiles, hasExternalReferences);
                                case Kind.GreaterThanGreaterThanGreaterThanToken:
                                    return new Result(leftNum.UnsignedRightShift(rightNum), isSyntacticallyString, resolvedOtherFiles, hasExternalReferences);
                                case Kind.LessThanLessThanToken:
                                    return new Result(leftNum.LeftShift(rightNum), isSyntacticallyString, resolvedOtherFiles, hasExternalReferences);
                                case Kind.CaretToken:
                                    return new Result(leftNum.BitwiseXOR(rightNum), isSyntacticallyString, resolvedOtherFiles, hasExternalReferences);
                                case Kind.AsteriskToken:
                                    return new Result(leftNum * rightNum, isSyntacticallyString, resolvedOtherFiles, hasExternalReferences);
                                case Kind.SlashToken:
                                    return new Result(leftNum / rightNum, isSyntacticallyString, resolvedOtherFiles, hasExternalReferences);
                                case Kind.PlusToken:
                                    return new Result(leftNum + rightNum, isSyntacticallyString, resolvedOtherFiles, hasExternalReferences);
                                case Kind.MinusToken:
                                    return new Result(leftNum - rightNum, isSyntacticallyString, resolvedOtherFiles, hasExternalReferences);
                                case Kind.PercentToken:
                                    return new Result(leftNum.Remainder(rightNum), isSyntacticallyString, resolvedOtherFiles, hasExternalReferences);
                                case Kind.AsteriskAsteriskToken:
                                    return new Result(leftNum.Exponentiate(rightNum), isSyntacticallyString, resolvedOtherFiles, hasExternalReferences);
                            }
                        }

                        string? leftStr = left.Value as string;
                        var leftIsStr = leftStr is not null;
                        string? rightStr = right.Value as string;
                        var rightIsStr = rightStr is not null;
                        if ((leftIsStr || leftIsNum) && (rightIsStr || rightIsNum) && @operator == Kind.PlusToken)
                        {
                            if (leftIsNum)
                            {
                                leftStr = leftNum.String();
                            }

                            if (rightIsNum)
                            {
                                rightStr = rightNum.String();
                            }

                            return new Result(leftStr + rightStr, isSyntacticallyString, resolvedOtherFiles, hasExternalReferences);
                        }

                        break;
                    }

                case Kind.StringLiteral:
                case Kind.NoSubstitutionTemplateLiteral:
                    return new Result(expr.Text(), true /*isSyntacticallyString*/, false, false);
                case Kind.TemplateExpression:
                    return EvaluateTemplateExpression(expr, location, Evaluate);
                case Kind.NumericLiteral:
                    return new Result(FromString(expr.Text()), false, false, false);
                case Kind.Identifier:
                    return evaluateEntity(expr, location);
                case Kind.ElementAccessExpression:
                case Kind.PropertyAccessExpression:
                    if (IsEntityNameExpression(expr.Expression()!))
                    {
                        return evaluateEntity(expr, location);
                    }

                    break;
            }

            return new Result(null, isSyntacticallyString, resolvedOtherFiles, hasExternalReferences);
        }

        return Evaluate;
    }

    /// <summary>Go 的两值类型断言 <c>n, ok := v.(jsnum.Number)</c>（C# 里直接强制转换会在类型不符时抛异常）。</summary>
    private static bool TryNumber(object? value, out Number number)
    {
        if (value is Number n)
        {
            number = n;
            return true;
        }

        number = default;
        return false;
    }

    private static Result EvaluateTemplateExpression(Node expr, Node? location, Evaluator evaluate)
    {
        var template = expr.AsTemplateExpression()!;
        var sb = new StringBuilder();
        sb.Append(template.Head!.Text());
        var resolvedOtherFiles = false;
        var hasExternalReferences = false;
        foreach (var span in template.TemplateSpans!.Nodes!)
        {
            var spanResult = evaluate(span.Expression()!, location);
            if (spanResult.Value is null)
            {
                return new Result(null, true /*isSyntacticallyString*/, false, false);
            }

            sb.Append(AnyToString(spanResult.Value));
            sb.Append(span.AsTemplateSpan()!.Literal!.Text());
            resolvedOtherFiles = resolvedOtherFiles || spanResult.ResolvedOtherFiles;
            hasExternalReferences = hasExternalReferences || spanResult.HasExternalReferences;
        }

        return new Result(sb.ToString(), true, resolvedOtherFiles, hasExternalReferences);
    }

    public static string AnyToString(object? v)
    {
        switch (v)
        {
            case string s:
                return s;
            case Number n:
                return n.String();
            case bool b:
                return IfElse(b, "true", "false");
            case PseudoBigInt b:
                return b.String();
        }

        throw new DebugFailureException("Unhandled case in AnyToString");
    }

    public static bool IsTruthy(object? v)
    {
        switch (v)
        {
            case string s:
                return s.Length != 0;
            case Number n:
                return n != 0 && !n.IsNaN();
            case bool b:
                return b;
            case PseudoBigInt b:
                return b != default;
        }

        throw new DebugFailureException("Unhandled case in IsTruthy");
    }
}
