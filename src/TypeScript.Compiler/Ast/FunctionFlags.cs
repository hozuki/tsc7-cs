// ported from: internal/ast/functionflags.go
//
// 与 Go 的差异（C# 语言特性，依据 docs/architecture.md §3.3）：
// - `type FunctionFlags uint32` + 包级常量 → `enum FunctionFlags : uint`：成员名 = Go 常量名去掉
//   `FunctionFlags` 前缀，数值逐条与 Go 一致。
namespace TypeScript.Ast;

[Flags]
public enum FunctionFlags : uint
{
    Normal = 0,
    Generator = 1u << 0,
    Async = 1u << 1,
    Invalid = 1u << 2,
    AsyncGenerator = Async | Generator,
}

public static partial class Ast
{
    public static FunctionFlags GetFunctionFlags(Node? node)
    {
        if (node is null)
        {
            return FunctionFlags.Invalid;
        }

        var data = node.BodyData();
        if (data is null)
        {
            return FunctionFlags.Invalid;
        }

        var flags = FunctionFlags.Normal;
        switch (node.Kind)
        {
            case Kind.FunctionDeclaration:
            case Kind.FunctionExpression:
            case Kind.MethodDeclaration:
                if (data.AsteriskToken is not null)
                {
                    flags |= FunctionFlags.Generator;
                }

                // Go: fallthrough（FunctionDeclaration/FunctionExpression/MethodDeclaration 会继续执行
                // KindArrowFunction 分支的 async 检查）。
                goto case Kind.ArrowFunction;
            case Kind.ArrowFunction:
                if (HasSyntacticModifier(node, ModifierFlags.Async))
                {
                    flags |= FunctionFlags.Async;
                }

                break;
        }

        if (data.Body is null)
        {
            flags |= FunctionFlags.Invalid;
        }

        return flags;
    }
}
