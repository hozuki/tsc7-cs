// ported from: internal/stringutil/js_case.go
//
// 偏离 Go 的地方（docs/architecture.md §3.1）：
//
// 1) Go 的循环体用 DecodeJSStringRune 从（哨兵编码的）UTF-8 里读“下一个 JS 字符”，并且要把孤立代理项
//    重新编码成哨兵字节写回去。C# 的 string 已是 UTF-16，所以按代码单元读即可（见 DecodeJsStringRune）：
//    合法代理对合成一个码点，孤立代理项原样保留为它自己的代码单元；写回时直接 Append(char)，
//    不需要 EncodeJSStringRune，也不需要 surrogateUTF8* 哨兵常量。
// 2) Go 的 `func toLowerASCII(str string) (string, bool)` 用 C# 惯用的 `bool TryToLowerASCII(string, out string)`
//    形状（返回值语义完全相同：false 表示“不是纯 ASCII，需要走完整映射”）。
// 3) 判定函数的参数是 int（Go 的 rune）：System.Text.Rune 无法表示代理项，见 Util.cs 文件头。
// 4) 新增了两个私有辅助函数（Go 用的是标准库/直接方法调用）：DecodeJsStringRune（在 UTF-16 代码单元串上
//    读下一个 JS 字符，孤立代理项原样保留）与 AppendRune（把一个码点写成 1–2 个 UTF-16 代码单元，避免
//    char.ConvertFromUtf32 的字符串分配）。
using System.Text;

namespace TypeScript.StringUtil;

public static partial class StringUtil
{
    public static string ToLowerJS(string str)
    {
        if (TryToLowerASCII(str, out var ascii))
        {
            return ascii;
        }

        var builder = new StringBuilder(str.Length);
        // casedBefore tracks whether the most recent non-Case_Ignorable code point is
        // "cased", which is the backward half of the Final_Sigma context. We
        // accumulate it as we stream so we never have to scan (or decode) backwards.
        var casedBefore = false;
        for (var i = 0; i < str.Length;)
        {
            var r = DecodeJsStringRune(str, i, out var size);
            i += size;
            if (IsSurrogate(r))
            {
                // A lone surrogate has no case mapping; preserve it verbatim, matching
                // String.prototype.toLowerCase.
                // C# 的 string 是 UTF-16，孤立代理项就是合法代码单元，直接 Append 即可
                // （Go 必须用 EncodeJSStringRune 把哨兵字节写回去，因为 WriteRune 会把代理项写成 U+FFFD）。
                builder.Append((char)r);
            }
            else if (SpecialCasingMappings.TryGetValue(r, out var mapping))
            {
                if (mapping.Condition == SpecialCasingCondition.FinalSigma && IsFinalSigmaContext(casedBefore, str, i))
                {
                    builder.Append(mapping.ConditionalLower);
                }
                else
                {
                    builder.Append(mapping.Lower);
                }
            }
            else
            {
                AppendRune(builder, r);
            }

            if (!IsUnicodeCaseIgnorable(r))
            {
                casedBefore = IsSigmaCased(r);
            }
        }

        return builder.ToString();
    }

    public static string ToUpperJS(string str)
    {
        if (TryToUpperASCII(str, out var ascii))
        {
            return ascii;
        }

        var builder = new StringBuilder(str.Length);
        for (var i = 0; i < str.Length;)
        {
            var r = DecodeJsStringRune(str, i, out var size);
            if (IsSurrogate(r))
            {
                // A lone surrogate has no case mapping; preserve it verbatim, matching
                // String.prototype.toUpperCase.
                // C# 侧直接 Append 该代码单元（Go 用 EncodeJSStringRune/WriteString 是为了保住哨兵字节）。
                builder.Append((char)r);
            }
            else if (SpecialCasingMappings.TryGetValue(r, out var mapping))
            {
                builder.Append(mapping.Upper);
            }
            else
            {
                AppendRune(builder, r);
            }

            i += size;
        }

        return builder.ToString();
    }

    private static bool TryToLowerASCII(string str, out string ascii)
    {
        var needsMapping = false;
        foreach (var ch in str)
        {
            // Go: utf8.RuneSelf == 0x80；只要出现任何非 ASCII 字节就不是纯 ASCII 快路径。
            if (ch >= 0x80)
            {
                ascii = "";
                return false;
            }

            needsMapping = needsMapping || ('A' <= ch && ch <= 'Z');
        }

        if (!needsMapping)
        {
            ascii = str;
            return true;
        }

        var buf = str.ToCharArray();
        for (var i = 0; i < buf.Length; i++)
        {
            var ch = buf[i];
            if ('A' <= ch && ch <= 'Z')
            {
                buf[i] = (char)(ch + ('a' - 'A'));
            }
        }

        ascii = new string(buf);
        return true;
    }

    private static bool TryToUpperASCII(string str, out string ascii)
    {
        var needsMapping = false;
        foreach (var ch in str)
        {
            if (ch >= 0x80)
            {
                ascii = "";
                return false;
            }

            needsMapping = needsMapping || ('a' <= ch && ch <= 'z');
        }

        if (!needsMapping)
        {
            ascii = str;
            return true;
        }

        var buf = str.ToCharArray();
        for (var i = 0; i < buf.Length; i++)
        {
            var ch = buf[i];
            if ('a' <= ch && ch <= 'z')
            {
                buf[i] = (char)(ch - ('a' - 'A'));
            }
        }

        ascii = new string(buf);
        return true;
    }

    // isFinalSigmaContext reports whether a sigma at the current position is in
    // Final_Sigma context: it is preceded by a cased code point and not followed by
    // one. casedBefore carries the backward half (tracked incrementally by the
    // caller so we never scan backwards); afterOffset is the byte offset just past
    // the sigma, from which we scan forward.
    //
    // ECMAScript points at Unicode Default Case Conversion for toLowerCase, and
    // modern V8 reaches that behavior through Intl::ConvertToLower, which uses
    // ICU root-locale lowercasing for non-Latin1 strings like Greek sigma.
    // We intentionally do not delegate this to golang.org/x/text/cases: x/text
    // is a general Unicode casing library, but its root-locale behavior is not
    // an exact match for the JS semantics exercised by String.prototype
    // .toLowerCase(), especially around Final_Sigma context. TypeScript needs the
    // JS behavior itself here, so we keep the context-sensitive part explicit.
    // SpiderMonkey models Final_Sigma with a more explicit context walk, while
    // Unicode Table 3-17 describes it in terms of Cased and Case_Ignorable.
    // We model the exposed V8/ICU behavior directly here: skip Case_Ignorable code
    // points and then look for a Cased code point, exactly as Unicode Table 3-17
    // defines the Final_Sigma condition. The Cased property already subsumes
    // lowercase, uppercase, and titlecase letters, including the
    // DerivedCoreProperties Lowercase/Uppercase extras such as ª, º, and Roman
    // numerals.
    private static bool IsFinalSigmaContext(bool casedBefore, string str, int afterOffset)
    {
        return casedBefore && !HasSigmaCasedAfter(str, afterOffset);
    }

    private static bool HasSigmaCasedAfter(string str, int start)
    {
        for (var i = start; i < str.Length;)
        {
            var r = DecodeJsStringRune(str, i, out var size);
            i += size;
            if (IsUnicodeCaseIgnorable(r))
            {
                continue;
            }

            return IsSigmaCased(r);
        }

        return false;
    }

    private static bool IsSigmaCased(int r)
    {
        return UnicodeCasedRanges.Contains(r);
    }

    private static bool IsUnicodeCaseIgnorable(int r)
    {
        return UnicodeCaseIgnorableRanges.Contains(r);
    }

    // Go: DecodeJSStringRune —— 从字符串里读“下一个 JS 字符”并给出消耗的单元数。
    // Go 的 string 是哨兵编码的 UTF-8（合法序列 → 码点；哨兵 3 字节 → 孤立代理项值；
    // 其他非法字节 → U+FFFD、size 1），C# 的 string 是 UTF-16 代码单元序列，因此这里按代码单元读：
    //   合法代理对   → 合成码点，size 2
    //   孤立代理项   → 该代码单元的值（U+D800–U+DFFF），size 1
    //   其他         → 该代码单元的值，size 1
    // 返回值用 int 而不是 Rune，因为孤立代理项无法用 Rune 表示（new Rune(0xD800) 会抛异常）。
    private static int DecodeJsStringRune(string str, int index, out int size)
    {
        var ch = str[index];
        if (char.IsHighSurrogate(ch) && index + 1 < str.Length && char.IsLowSurrogate(str[index + 1]))
        {
            size = 2;
            return char.ConvertToUtf32(ch, str[index + 1]);
        }

        size = 1;
        return ch;
    }

    // Go: builder.WriteRune(r)。大小写表里有 astral 条目（如 U+1E921），所以不能只 Append((char)r)；
    // 这里用 stackalloc + Rune.EncodeToUtf16 写出 1–2 个代码单元，避免 char.ConvertFromUtf32 的字符串分配。
    private static void AppendRune(StringBuilder builder, int r)
    {
        if (r <= 0xFFFF)
        {
            builder.Append((char)r);
            return;
        }

        Span<char> chars = stackalloc char[2];
        var length = new Rune(r).EncodeToUtf16(chars);
        builder.Append(chars[..length]);
    }
}
