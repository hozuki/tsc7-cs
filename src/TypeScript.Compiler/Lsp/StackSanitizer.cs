// ported from: internal/lsp/stack_sanitizer.go:1-96（整文件）
//
// 与 Go 的偏离（逐条说明）：
//
// 1) **两个标记常量换成 C# 的对应物**（算法本身逐行照搬）：
//      * Go 用 `strings.Index(stack, "runtime/debug.Stack()")` 定位"我们自己栈帧的起点"；
//        C# 的栈文本来自 `Environment.StackTrace`（Go 的 `runtime/debug.Stack()` 在 C# 的对应物，
//        见 docs/architecture.md 的平台点清单），它的第一帧就是 `at System.Environment.get_StackTrace()`，
//        所以常量取它（`StackMarker`）。
//      * Go 用 `strings.Index(line, "TypeScript/tsc/")` 判断"这一帧是不是我们的模块"（模块根后面的部分保留，
//        其余帧写 `(REDACTED FRAME)`）；C# 的帧文本里没有模块路径，模块标识就是命名空间前缀，所以常量取
//        `"TypeScript."`（`OurModuleMarker`，本仓所有产品命名空间都是 `TypeScript.*`）。
//    这两处是**唯一**的语义替换；分帧、清参数、`|>` 拼接、关键词打散全部与 Go 逐行一致。
//
// 2) `strings.Lines(stack)` + `core.Enumerate(...)` → 本文件的私有 `Lines(string)` + `Core.Enumerate`。
//    Go 的 `strings.Lines` 会**带上**行尾换行符，但 Go 代码在此把它当"行"用：先写前导空白（`line[:i]`）、
//    再把余下的交给 `strings.TrimSpace`（于是行尾的 `\n` 被吃掉），最后用 `lineNum > 0` 自己补 `\n`。
//    ⇒ 去掉行尾 `\n`/`\r` 后逐行迭代的输出与 Go 完全相同（空行同样落到 `(REDACTED FRAME)`），
//    因此 C# 侧直接按"不含行尾符"的行迭代，省掉这份无意义的空白搬运。
//
// 3) C# 的帧文本比 Go 多一段 ` in <绝对路径>:line N`（同一行，不是 Go 的两行）。为了让输出保持 Go 的
//    "没有机器相关路径、只有模块相对位置"这一不变量，`WriteSanitizedModuleOrPath` 多了一步
//    （文件头第 3 条对应的实际代码处有注释）：把 ` in ` 之后的部分只留**文件名**（丢掉目录），
//    再把两份用 `/` 连起来，于是后续 `strings.Split(line, "/")` + `|>` 拼接的处理流程可以完全照搬。
//    同时把 `\` 归一成 `/`（Go 的路径本来就是 `/`，而 .NET 在 Windows 上打印 `\`）。
//
// 4) `strings.LastIndexByte(segment, '(')` → `segment.LastIndexOf('(')`；`strings.TrimSpace` →
//    `Trim()`（两者都按 Unicode 空白裁剪）；`regexp.ReplaceAllString` → `Regex.Replace`。
//
// 5) Go 的 `writeSanitizedModuleOrPath` 是包级未导出函数 → 静态类 `Lsp` 的 `internal static` 方法。
using System.Text;
using System.Text.RegularExpressions;
using TypeScript.Core;
using static TypeScript.Core.Core;

namespace TypeScript.Lsp;

public static partial class Lsp
{
    /// <summary>
    /// Go: <c>var genericSecretKeywordRegex</c>（stack_sanitizer.go:19）。
    ///
    /// VS Code 的 telemetry 管线会把任何匹配
    /// <c>/(key|token|sig|secret|signature|password|passwd|pwd|android:value)[^a-zA-Z0-9]/i</c>
    /// 的字符串整段替换成 <c>&lt;REDACTED: Generic Secret&gt;</c>，于是像 `getSignatureHelp(` 这种无害的
    /// Go 栈帧也会被误伤。做法是在我们真正会输出的、紧跟标点（`(`、`[`、`.`、`|`）的关键词后面插入 `X_X`
    /// 标记（在遥测看板上再把 `X_X` 删掉即还原）。
    /// </summary>
    private static readonly Regex _genericSecretKeywordRegex =
        new(@"(?i)(key|token|signature|sig|pwd)([(\[.|])");

    /// <summary>
    /// Go 的 <c>"runtime/debug.Stack()"</c>（stack_sanitizer.go:34）：`Environment.StackTrace` 的第一帧。
    /// 见文件头第 1 条。
    /// </summary>
    internal const string StackMarker = "at System.Environment.get_StackTrace()";

    /// <summary>Go 的 <c>"TypeScript/tsc/"</c>（stack_sanitizer.go:50）：本仓所有产品命名空间的前缀。见文件头第 1 条。</summary>
    internal const string OurModuleMarker = "TypeScript.";

    /// <summary>Go: <c>func defeatGenericSecretRegex(s string) string</c>（stack_sanitizer.go:23-25）。</summary>
    internal static string DefeatGenericSecretRegex(string s) =>
        _genericSecretKeywordRegex.Replace(s, "${1}X_X${2}");

    /// <summary>
    /// Go: <c>func sanitizeStackTrace(stack string) string</c>（stack_sanitizer.go:27-62）。
    /// 见文件头第 1 条：两个标记常量换成了 C# 的对应物。
    /// </summary>
    internal static string SanitizeStackTrace(string stack)
    {
        // TODO: should we just look for the first '(' and
        // just strip everything before the prior newline?
        var startIndex = stack.IndexOf(StackMarker, StringComparison.Ordinal);
        if (startIndex < 0)
        {
            return "";
        }

        stack = stack[startIndex..];

        var result = new StringBuilder();

        foreach (var (lineNum, rawLine) in Enumerate(Lines(stack)))
        {
            if (lineNum > 0)
            {
                result.Append('\n');
            }

            var line = rawLine;
            var i = 0;
            // Skip whitespace
            while (i < line.Length)
            {
                if (line[i] != ' ' && line[i] != '\t')
                {
                    break;
                }

                i++;
            }

            result.Append(line[..i]);

            line = line[i..];

            var ourModuleIndex = line.IndexOf(OurModuleMarker, StringComparison.Ordinal);
            if (ourModuleIndex >= 0)
            {
                line = line[ourModuleIndex..];
                WriteSanitizedModuleOrPath(line, result);
            }
            else
            {
                result.Append("(REDACTED FRAME)");
            }
        }

        return DefeatGenericSecretRegex(result.ToString());
    }

    /// <summary>Go: <c>func writeSanitizedModuleOrPath(line string, result *strings.Builder)</c>（stack_sanitizer.go:64-96）。</summary>
    private static void WriteSanitizedModuleOrPath(string line, StringBuilder result)
    {
        // We don't expect things like \r, but it doesn't hurt to trim just in case.
        line = line.Trim();

        // 见文件头第 3 条：C# 的帧把文件位置塞在同一行的 ` in <绝对路径>:line N` 里，Go 的帧则把它放在
        // 下一行、且已经是模块相对路径。为了不把机器路径带进遥测，这里只保留文件名（丢掉目录），
        // 并用 `/` 把"方法"和"文件"接起来，之后就能完全照搬 Go 的 split('|>') 处理。
        var inIndex = line.IndexOf(" in ", StringComparison.Ordinal);
        if (inIndex >= 0)
        {
            var location = line[(inIndex + " in ".Length)..].Replace('\\', '/');
            var lastSlash = location.LastIndexOf('/');
            line = line[..inIndex] + "/" + (lastSlash >= 0 ? location[(lastSlash + 1)..] : location);
        }
        else
        {
            line = line.Replace('\\', '/');
        }

        var plusHex = line.IndexOf(" +0x", StringComparison.Ordinal);
        if (plusHex >= 0)
        {
            line = line[..plusHex];
        }
        else
        {
            var inGoroutine = line.LastIndexOf(" in goroutine ", StringComparison.Ordinal);
            if (inGoroutine >= 0)
            {
                line = line[..inGoroutine];
            }
        }

        var segments = line.Split('/');
        for (var segmentIndex = 0; segmentIndex < segments.Length; segmentIndex++)
        {
            if (segmentIndex > 0)
            {
                result.Append("|>");
            }

            var segment = segments[segmentIndex];

            // See if the string ends with ), and strip out all the arguments.
            if (segment.EndsWith(')'))
            {
                var openParenIndex = segment.LastIndexOf('(');
                if (openParenIndex < 0)
                {
                    // Closing parenthesis, but no opening - bail out.
                    result.Append("???");
                    continue;
                }

                segment = segment[..openParenIndex];
                result.Append(segment);
                result.Append("()");
                continue;
            }

            result.Append(segment);
        }
    }

    /// <summary>Go: <c>strings.Lines(s)</c> 的等价物（不含行尾符，见文件头第 2 条）。</summary>
    private static IEnumerable<string> Lines(string s)
    {
        var start = 0;
        while (start < s.Length)
        {
            var newline = s.IndexOf('\n', start);
            if (newline < 0)
            {
                yield return TrimTrailingCr(s[start..]);
                yield break;
            }

            yield return TrimTrailingCr(s[start..newline]);
            start = newline + 1;
        }
    }

    private static string TrimTrailingCr(string line) =>
        line.Length > 0 && line[^1] == '\r' ? line[..^1] : line;
}
