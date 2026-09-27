// ported from: internal/debug/debug.go
//
// Go 的 debug 包用 panic(string) 表达"不该发生"的内部错误；C# 侧用 DebugFailureException。
// TSGO 只在 API 服务器的请求边界上 recover（internal/api/session.go），编译器主体不捕获，
// 所以移植时不要在这条路径上做静默恢复。
//
// 命名空间偏离：Go 的 internal/debug 包对应的是**根命名空间 TypeScript**，不是 TypeScript.Debug。
// 因为 "Debug" 一旦同时是命名空间名和类名，在任意 TypeScript.* 命名空间里 `Debug.Fail(...)`
// 都会优先解析到命名空间而编译失败。调用点仍与 debug.Fail 一一对应。
using System.Text;

namespace TypeScript;

/// <summary>Go 的 <c>panic(reason)</c> 在 C# 里的对应物。</summary>
public sealed class DebugFailureException(string message) : Exception(message);

/// <summary>Go 里内联的 <c>interface{ KindString() string }</c>。</summary>
public interface IKindString
{
    string KindString();
}

public static class Debug
{
    public static void Fail(string reason)
    {
        reason = reason.Length == 0 ? "Debug failure." : "Debug failure. " + reason;
        throw new DebugFailureException(reason);
    }

    public static void FailBadSyntaxKind(IKindString node, params object?[] message)
    {
        var msg = message.Length == 0 ? "Unexpected node." : Sprint(message);
        Fail($"{msg}\nNode {node.KindString()} was unexpected.");
    }

    public static void AssertNever(object? member, params object?[] message)
    {
        var msg = message.Length == 0 ? "Illegal value:" : Sprint(message);
        var detail = member switch
        {
            IKindString kindString => kindString.KindString(),
            // Go 在这里还会退到 fmt.Stringer 再到 fmt.Sprintf("%v", …)；C# 里
            // 所有对象都有 ToString，所以后两个分支合成一个。
            null => "<nil>",
            _ => member.ToString() ?? "<nil>",
        };
        Fail($"{msg} {detail}");
    }

    public static void Assert(bool value, params object?[] message)
    {
        if (value)
        {
            return;
        }

        AssertSlow(message);
    }

    private static void AssertSlow(object?[] message)
    {
        var msg = message.Length > 0 ? "False expression: " + Sprint(message) : "False expression.";
        Fail(msg);
    }

    /// <summary>复刻 fmt.Sprint 的默认格式（含"两个操作数都不是 string 时插入空格"的规则）。</summary>
    private static string Sprint(object?[] parts)
    {
        if (parts.Length == 1)
        {
            return Format(parts[0]);
        }

        var builder = new StringBuilder();
        for (var i = 0; i < parts.Length; i++)
        {
            if (i > 0 && parts[i - 1] is not string && parts[i] is not string)
            {
                builder.Append(' ');
            }

            builder.Append(Format(parts[i]));
        }

        return builder.ToString();
    }

    private static string Format(object? value) => value switch
    {
        null => "<nil>",
        string s => s,
        bool b => b ? "true" : "false",
        _ => value.ToString() ?? "<nil>",
    };
}
