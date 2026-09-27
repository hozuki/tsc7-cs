// ported from: internal/transformers/tstransforms/utilities.go:1-29
//
// Go 的包级函数（`constantExpression`）落进与本包同名的静态类 `TsTransforms`
// （docs/architecture.md §2）。后续几个文件用 partial 把同一类补全。
//
// 偏离 Go 的地方：
// 1) `value any` → `object?`；Go 的 type switch 对 `jsnum.Number`（值类型）会装箱，
//    C# 的 `case JsNumber n` 同样要拆箱，语义一致。
// 2) `jsnum.Number.String()` 在 C# 里是 `JsNum` 静态类里的扩展方法（`using static TypeScript.JsNum.JsNum;`），
//    保持 Go 的 `n.String()` 写法。
using TypeScript.Ast;
using TypeScript.Printer;
using static TypeScript.JsNum.JsNum;
using JsNumber = TypeScript.JsNum.Number;
// Go: printer.NodeFactory（与 ast.NodeFactory 同名不同包 → 显式限定，避免 CS0104）。
using NodeFactory = TypeScript.Printer.NodeFactory;

namespace TypeScript.Transformers.TsTransforms;

public static partial class TsTransforms
{
    /// <summary>Go: <c>func constantExpression(value any, factory *printer.NodeFactory) *ast.Expression</c>。</summary>
    internal static Node? ConstantExpression(object? value, NodeFactory factory)
    {
        switch (value)
        {
            case string s:
                return factory.NewStringLiteral(s, TokenFlags.None);
            case JsNumber n:
                if (n.IsInf())
                {
                    if (n > 0)
                    {
                        return factory.NewIdentifier("Infinity");
                    }

                    return factory.NewPrefixUnaryExpression(Kind.MinusToken, factory.NewIdentifier("Infinity"));
                }

                if (n.IsNaN())
                {
                    return factory.NewIdentifier("NaN");
                }

                if (n < 0)
                {
                    return factory.NewPrefixUnaryExpression(Kind.MinusToken, ConstantExpression(-n, factory));
                }

                return factory.NewNumericLiteral(n.String(), TokenFlags.None);
        }

        return null;
    }
}
