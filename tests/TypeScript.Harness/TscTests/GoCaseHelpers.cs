// 非 Go 源文件：`artifacts/gen-tsccases.js` 生成的用例文件所用的少量运行时助手。
//
// 这些函数逐条对应 Go 侧 testdata/harness 里用到的标准库/语言内建语义（stdlib 的 slices/core/fmt/strings 子集），
// 只是为了让机械转写出来的 C# 保持"一行对一行"。没有它们，生成器就得把 `slices.Concat`、`fmt.Sprintf` 之类
// 内联展开，反而更难与 Go 源对照。
//
// 逐条对应：
//   * `GoStr(a, b, …)`      ← Go 的 `a + b`（字符串拼接；bool 打印 "true"/"false"，int 十进制，nil → "<nil>"）
//   * `Concat<T>(…)`        ← `slices.Concat`
//   * `Append<T>(s, xs…)`   ← `append(s, xs…)`
//   * `FlatMap<TSrc,T>(…)`  ← `core.FlatMap`
//   * `Map<TSrc,T>(…)`      ← `core.Map`
//   * `Contains<T>(s, x)`   ← `slices.Contains`
//   * `Sprintf(fmt, …)`     ← `fmt.Sprintf`（只实现 %s %d %t %v %q %% 与 `%[n]` 显式下标）
//   * `StringsReplace(s,o,n,cnt)` ← `strings.Replace(s, o, n, cnt)`
//   * `Itoa(i)`             ← `strconv.Itoa`
namespace TypeScript.Harness.TscTests;

using global::System;
using global::System.Collections.Generic;
using global::System.Globalization;
using global::System.Linq;
using global::System.Text;

/// <summary>生成用例文件的运行时助手（详见文件头注释）。</summary>
internal static class GoCaseHelpers
{
    /// <summary>Go: <c>panic(v)</c>：C# 里以异常表达（表达式位置需要一个返回值）。</summary>
    public static string ThrowNew(Exception e) => throw e;

    /// <summary>Go 的 <c>fmt.Sprint</c> 口径：nil → <c>&lt;nil&gt;</c>，bool → 小写，其余不变文化。</summary>
    public static string FormatValue(object? value) => value switch
    {
        null => "<nil>",
        bool b => b ? "true" : "false",
        string s => s,
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString() ?? "",
    };

    /// <summary>Go 的字符串拼接：非字符串操作数按 <see cref="FormatValue"/> 打印。</summary>
    public static string GoStr(params object?[] parts)
    {
        var sb = new StringBuilder();
        foreach (var part in parts)
        {
            sb.Append(part is string s ? s : FormatValue(part));
        }

        return sb.ToString();
    }

    /// <summary>Go: <c>len(x)</c>（nil 切片/映射 → 0）。</summary>
    public static int GoLen<T>(IEnumerable<T>? x) => x is null ? 0 : x.Count();

    /// <summary>Go 的 range：nil 切片/映射迭代零次（C# 的 foreach 会抛）。</summary>
    public static IEnumerable<T> GoIter<T>(IEnumerable<T>? x) => x ?? [];

    /// <summary>Go: <c>slices.Concat(parts...)</c>。</summary>
    public static T[] Concat<T>(params IEnumerable<T>[] parts)
    {
        var list = new List<T>();
        foreach (var part in parts)
        {
            if (part is null)
            {
                continue;
            }

            list.AddRange(part);
        }

        return list.ToArray();
    }

    /// <summary>Go: <c>append(slice, xs...)</c>。</summary>
    public static T[] Append<T>(T[]? slice, params T[] xs)
    {
        var list = new List<T>();
        if (slice is not null)
        {
            list.AddRange(slice);
        }

        list.AddRange(xs);
        return list.ToArray();
    }

    /// <summary>Go: <c>append(slice, other...)</c>（展开形式）。</summary>
    public static T[] AppendRange<T>(T[]? slice, IEnumerable<T> other)
    {
        var list = new List<T>();
        if (slice is not null)
        {
            list.AddRange(slice);
        }

        list.AddRange(other);
        return list.ToArray();
    }

    /// <summary>Go: <c>core.FlatMap(slice, f)</c>。</summary>
    public static TOut[] FlatMap<TIn, TOut>(IEnumerable<TIn>? slice, Func<TIn, TOut[]> f)
    {
        var list = new List<TOut>();
        foreach (var item in GoIter(slice))
        {
            list.AddRange(f(item));
        }

        return list.ToArray();
    }

    /// <summary>Go: <c>core.Map(slice, f)</c>。</summary>
    public static TOut[] Map<TIn, TOut>(IEnumerable<TIn>? slice, Func<TIn, TOut> f) =>
        GoIter(slice).Select(f).ToArray();

    /// <summary>Go: <c>slices.Contains(slice, value)</c>。</summary>
    public static bool Contains<T>(IEnumerable<T>? slice, T value) => GoIter(slice).Contains(value);

    /// <summary>Go: <c>strconv.Itoa(i)</c>。</summary>
    public static string Itoa(int i) => i.ToString(CultureInfo.InvariantCulture);

    /// <summary>Go: <c>strings.Join(parts, sep)</c>（Go 的 nil 切片等价于空串，C# 的 <c>string.Join</c> 会抛）。</summary>
    public static string GoJoin(string sep, IEnumerable<string>? parts) =>
        parts is null ? "" : string.Join(sep, parts);

    /// <summary>Go: <c>strings.Replace(s, old, new, n)</c>（n &lt; 0 表示全部）。</summary>
    public static string StringsReplace(string s, string oldValue, string newValue, int n)
    {
        if (n == 0 || oldValue.Length == 0)
        {
            return s;
        }

        if (n < 0)
        {
            return s.Replace(oldValue, newValue, StringComparison.Ordinal);
        }

        var sb = new StringBuilder();
        var index = 0;
        var replaced = 0;
        while (replaced < n)
        {
            var next = s.IndexOf(oldValue, index, StringComparison.Ordinal);
            if (next < 0)
            {
                break;
            }

            sb.Append(s, index, next - index).Append(newValue);
            index = next + oldValue.Length;
            replaced++;
        }

        sb.Append(s, index, s.Length - index);
        return sb.ToString();
    }

    /// <summary>
    /// Go: <c>fmt.Sprintf(format, args...)</c> 的子集：<c>%%</c>、<c>%s</c>、<c>%d</c>、<c>%t</c>、<c>%v</c>、
    /// <c>%q</c>，以及 <c>%[n]</c> 显式下标。
    /// </summary>
    public static string Sprintf(string format, params object?[] args)
    {
        var sb = new StringBuilder();
        var argIndex = 0;
        for (var i = 0; i < format.Length; i++)
        {
            if (format[i] != '%')
            {
                sb.Append(format[i]);
                continue;
            }

            i++;
            if (i >= format.Length)
            {
                sb.Append('%');
                break;
            }

            if (format[i] == '%')
            {
                sb.Append('%');
                continue;
            }

            var explicitIndex = -1;
            if (format[i] == '[')
            {
                var close = format.IndexOf(']', i);
                if (close < 0)
                {
                    sb.Append(format, i - 1, format.Length - i + 1);
                    break;
                }

                explicitIndex = int.Parse(format[(i + 1)..close], CultureInfo.InvariantCulture) - 1;
                i = close + 1;
            }

            while (i < format.Length && !char.IsLetter(format[i]))
            {
                i++;
            }

            if (i >= format.Length)
            {
                break;
            }

            var verb = format[i];
            object? arg;
            if (explicitIndex >= 0)
            {
                arg = explicitIndex < args.Length ? args[explicitIndex] : null;
                argIndex = explicitIndex + 1;
            }
            else
            {
                arg = argIndex < args.Length ? args[argIndex] : null;
                argIndex++;
            }

            switch (verb)
            {
                case 't':
                    sb.Append(arg is bool b ? (b ? "true" : "false") : FormatValue(arg));
                    break;
                case 'd':
                    sb.Append(Convert.ToInt64(arg, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture));
                    break;
                case 'q':
                    sb.Append('"').Append(FormatValue(arg)).Append('"');
                    break;
                case 's':
                    sb.Append(arg is string s ? s : FormatValue(arg));
                    break;
                default:
                    sb.Append(FormatValue(arg));
                    break;
            }
        }

        return sb.ToString();
    }
}
