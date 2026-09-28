// ported from: (Go 标准库) flag/flag.go —— 只移植 cmd/tsc 的 lsp.go/api.go 用到的那一部分
//              （NewFlagSet(name, ContinueOnError) + Bool/String/Int + Parse + 默认用法输出）
//
// 为什么需要它：Go 的 `flag` 的**命令行语法与错误输出**是 CLI 对外契约的一部分（`--lsp -h`、`--api -unknown` 都要
// 与 Go 逐字一致），而 BCL 里没有任何等价物（System.CommandLine 是另一个库、语法也不同）。本文件放在 CLI 工程里而
// 不是库里：全仓只有 cmd/tsc 用它（Go 侧也是标准库，没有对应的 internal package）。
//
// 与 Go 的偏离（逐条给依据）：
// 1) `flag.NewFlagSet(name, flag.ContinueOnError)` → `new FlagSet(name)`：全仓只用 ContinueOnError 这一种
//    ErrorHandling（两个调用点都在解析失败时 `return 2`），其它两种（PanicOnError/ExitOnError）不移植——留着就是
//    没有调用点的死代码。Output 默认 `os.Stderr` → `Sys.StdErr`（可覆盖，同 Go 的 SetOutput）。
// 2) `flag.Bool/String/Int(name, def, usage) *bool/*string/*int` → 返回 `BoolFlag/StringFlag/IntFlag` 对象，
//    调用点在 `TryParse` 之后读 `.Value`（Go 传的是指针）。`*Var` 系列（`BoolVar(&field, …)`）因此**不需要**——
//    api.go 的 `result.async` 这类字段改成"解析后从 Flag 对象拷贝进结构体"（见 Api.cs）。
// 3) `Parse(arguments []string) error` 的错误是**预期分支**（用法错误）→ `bool TryParse(arguments)`（§3.2 的
//    `(T, error)` 约定）；与 Go 一样，失败时错误文本 + 用法已经写到 Output 上（Go 的 `failf` 行为），调用点只负责
//    返回退出码 2。
// 4) 只支持三种 Value 类型（bool/string/int）—— Go 的 flag 还有 Duration/Float64/Uint/Var/Func 等，本仓的
//    cmd/tsc 一个都没用到（`-clientProcessId` 是 int、`-cwd`/`-pipe`/`-socket`/`-pprofDir`/`-callbacks` 是 string、
//    其余是 bool），按"没有调用点就不移植"处理。
// 5) `strconv.ParseInt(s, 0, strconv.IntSize)`（基数 0：十进制 / 0x / 0b / 0o / 前导 0 八进制 / 数字下划线）在 BCL
//    里没有直接对应物（`int.TryParse` 只认十进制）→ 本文件实现同样的**前缀识别**；差异：Go 允许数字间下划线
//    （`1_000`），这里不接受（CLI 的 PID 参数不会带下划线，且下划线支持会让错误文本更复杂）。
// 6) `strconv.ParseBool` 接受 "1"/"t"/"T"/"TRUE"/"true"/"True"/"0"/"f"/"F"/"FALSE"/"false"/"False" → 逐字保留。
// 7) `%q`（Go 的带引号字符串）→ `GoQuote`：转义 `"` 与 `\`、非可打印字符走 `\xNN`/`\uNNNN`（Go 的 %q 对
//    ASCII 可打印字符原样输出；本仓的用法里只有 cwd 这条默认值会被引号包住）。
// 8) `errors.Is(err, flag.ErrHelp)` → 不移植：两个调用点对 `-h`/`-help` 与用法错误都返回 2（Go 侧亦然，只是
//    `err == flag.ErrHelp` 可以区分——没有调用点区分它）。
using global::System;
using global::System.Collections.Generic;
using global::System.Globalization;
using global::System.IO;
using global::System.Text;

namespace TypeScript.Tsc;

/// <summary>Go: <c>type FlagSet struct</c>（flag.go）—— 只保留 cmd/tsc 用到的部分。</summary>
internal sealed class FlagSet(string name)
{
    /// <summary>Go: <c>f.name</c>（默认用法输出里的 "Usage of %s:"）。</summary>
    private readonly string _name = name;

    /// <summary>Go: <c>f.formal map[string]*Flag</c>（按名字查找）。</summary>
    private readonly Dictionary<string, Flag> _formal = new(StringComparer.Ordinal);

    /// <summary>Go: <c>f.actual</c> 的等价物（VisitAll 按名字排序，见 <see cref="PrintDefaults"/>）。</summary>
    private readonly List<Flag> _ordered = [];

    /// <summary>Go: <c>f.Output()</c>（默认 os.Stderr → <see cref="Sys.StdErr"/>）。</summary>
    internal TextWriter Output { get; set; } = Sys.StdErr;

    /// <summary>Go: <c>func (f *FlagSet) Bool(name string, value bool, usage string) *bool</c>。</summary>
    internal BoolFlag Bool(string name, bool value, string usage)
    {
        var flag = new BoolFlag(name, value, usage);
        Add(flag);
        return flag;
    }

    /// <summary>Go: <c>func (f *FlagSet) String(name string, value string, usage string) *string</c>。</summary>
    internal StringFlag String(string name, string value, string usage)
    {
        var flag = new StringFlag(name, value, usage);
        Add(flag);
        return flag;
    }

    /// <summary>Go: <c>func (f *FlagSet) Int(name string, value int, usage string) *int</c>。</summary>
    internal IntFlag Int(string name, int value, string usage)
    {
        var flag = new IntFlag(name, value, usage);
        Add(flag);
        return flag;
    }

    private void Add(Flag flag)
    {
        // Go: panic(fmt.Sprintf("flag redefined: %s", name))。
        if (!_formal.TryAdd(flag.Name, flag))
        {
            throw new InvalidOperationException("flag redefined: " + flag.Name);
        }
        _ordered.Add(flag);
    }

    /// <summary>Go: <c>func (f *FlagSet) Parse(arguments []string) error</c>（ContinueOnError，见文件头偏离 3）。</summary>
    internal bool TryParse(string[] arguments)
    {
        // Go: f.args = arguments; for { seen, err := f.parseOne(); ... }（见 parseOne 的行内注释）。
        var args = arguments;
        var index = 0;
        while (true)
        {
            var (seen, ok) = ParseOne(args, ref index);
            if (!ok)
            {
                return false;
            }
            if (!seen)
            {
                return true;
            }
        }
    }

    /// <summary>Go: <c>func (f *FlagSet) parseOne() (bool, error)</c>（flag.go 的同名函数，逐分支对照）。</summary>
    private (bool Seen, bool Ok) ParseOne(string[] args, ref int index)
    {
        if (index >= args.Length)
        {
            return (false, true);
        }

        var s = args[index];
        if (s.Length < 2 || s[0] != '-')
        {
            return (false, true);
        }

        var numMinuses = 1;
        if (s[1] == '-')
        {
            numMinuses++;
            if (s.Length == 2)
            {
                // "--" terminates the flags.
                index++;
                return (false, true);
            }
        }

        var name = s[numMinuses..];
        if (name.Length == 0 || name[0] == '-' || name[0] == '=')
        {
            Fail("bad flag syntax: " + s);
            return (false, false);
        }

        // It's a flag. Does it have an argument?
        index++;
        var hasValue = false;
        var value = "";
        for (var i = 1; i < name.Length; i++)
        {
            // Equals cannot be first.
            if (name[i] == '=')
            {
                value = name[(i + 1)..];
                hasValue = true;
                name = name[..i];
                break;
            }
        }

        if (!_formal.TryGetValue(name, out var flag))
        {
            if (name is "help" or "h")
            {
                // Special case for a nice help message.
                Usage();
                return (false, false);
            }
            Fail("flag provided but not defined: -" + name);
            return (false, false);
        }

        if (flag.IsBool)
        {
            // Special case: a bool flag doesn't need an argument.
            if (hasValue)
            {
                if (!flag.TrySet(value, out var err))
                {
                    Fail("invalid boolean value " + Flag.GoQuote(value) + " for -" + name + ": " + err);
                    return (false, false);
                }
            }
            else
            {
                // Go 的 `fv.Set("true")` 对 bool 不可能失败，这里保留同样的调用形状。
                flag.TrySet("true", out _);
            }
        }
        else
        {
            // It must have a value, which might be the next argument.
            if (!hasValue && index < args.Length)
            {
                hasValue = true;
                value = args[index];
                index++;
            }
            if (!hasValue)
            {
                Fail("flag needs an argument: -" + name);
                return (false, false);
            }
            if (!flag.TrySet(value, out var err))
            {
                Fail("invalid value " + Flag.GoQuote(value) + " for flag -" + name + ": " + err);
                return (false, false);
            }
        }

        return (true, true);
    }

    /// <summary>Go: <c>func (f *FlagSet) failf(format string, a ...any) error</c>：错误文本 + 用法都写到 Output。</summary>
    private void Fail(string message)
    {
        Output.Write(message);
        Output.Write("\n");
        Usage();
    }

    /// <summary>Go: <c>func (f *FlagSet) usage()</c> → <c>defaultUsage()</c>。</summary>
    private void Usage()
    {
        Output.Write("Usage of " + _name + ":\n");
        PrintDefaults();
    }

    /// <summary>Go: <c>func (f *FlagSet) PrintDefaults()</c>（逐行对照 flag.go，含"4 字符以内用制表符对齐"的特殊情形）。</summary>
    private void PrintDefaults()
    {
        var flags = new List<Flag>(_ordered);
        // Go: sortFlags(flags) —— 按名字做字节序排序。
        flags.Sort(static (a, b) => string.CompareOrdinal(a.Name, b.Name));

        foreach (var flag in flags)
        {
            var b = new StringBuilder();
            b.Append("  -").Append(flag.Name);
            var placeholder = flag.Placeholder;
            if (placeholder.Length > 0)
            {
                b.Append(' ').Append(placeholder);
            }
            if (b.Length <= 4)
            {
                // space, space, '-', 'x'.
                b.Append('\t');
            }
            else
            {
                // Four spaces before the tab triggers good alignment for both 4- and 8-space tab stops.
                b.Append("\n    \t");
            }
            b.Append(flag.Usage.Replace("\n", "\n    \t"));
            if (!flag.IsZeroDefault)
            {
                b.Append(" (default ").Append(flag.IsString ? Flag.GoQuote(flag.DefValue) : flag.DefValue).Append(')');
            }
            Output.Write(b.ToString());
            Output.Write("\n");
        }
    }
}

/// <summary>Go: <c>type Flag struct</c>（flag.go:142-178）—— 只保留 cmd/tsc 用到的部分。</summary>
internal abstract class Flag(string name, string usage)
{
    /// <summary>Go: <c>Name string</c>（flag.go:145）。</summary>
    internal readonly string Name = name;

    /// <summary>Go: <c>Usage string</c>（flag.go:148）。</summary>
    internal readonly string Usage = usage;

    /// <summary>Go: <c>DefValue string</c>（默认值的**文本**形式，flag.go:157）。</summary>
    internal abstract string DefValue { get; }

    /// <summary>Go: <c>boolFlag.IsBoolFlag()</c>（决定该 flag 是否吃下一个参数）。</summary>
    internal abstract bool IsBool { get; }

    /// <summary>Go: <c>isZeroValue</c>/<c>PrintDefaults</c> 里"是不是 stringValue"（决定默认值要不要加引号）。</summary>
    internal abstract bool IsString { get; }

    /// <summary>Go: <c>UnquoteUsage</c> 从 usage 里反引号抽出的占位符（这里按 Value 类型定死）。</summary>
    internal abstract string Placeholder { get; }

    /// <summary>Go: <c>isZeroValue(flag, flag.DefValue)</c>（决定是否打印 "(default X)"）。</summary>
    internal abstract bool IsZeroDefault { get; }

    /// <summary>Go: <c>Flag.Value.Set(s) error</c>（失败返回错误文本，见各自实现）。</summary>
    internal abstract bool TrySet(string value, out string error);

    /// <summary>Go: <c>fmt.Sprintf("%q", s)</c>（见 Flag.cs 文件头偏离 7）。</summary>
    internal static string GoQuote(string s)
    {
        var b = new StringBuilder("\"");
        foreach (var c in s)
        {
            switch (c)
            {
                case '"':
                    b.Append("\\\"");
                    break;
                case '\\':
                    b.Append("\\\\");
                    break;
                case '\n':
                    b.Append("\\n");
                    break;
                case '\r':
                    b.Append("\\r");
                    break;
                case '\t':
                    b.Append("\\t");
                    break;
                default:
                    if (c < 0x20 || c == 0x7f)
                    {
                        b.Append("\\x").Append(((int)c).ToString("x2", CultureInfo.InvariantCulture));
                    }
                    else
                    {
                        b.Append(c);
                    }
                    break;
            }
        }
        return b.Append('"').ToString();
    }
}

/// <summary>Go: <c>type boolValue bool</c>（flag.go）—— 实现 <c>boolFlag</c>。</summary>
internal sealed class BoolFlag : Flag
{
    private readonly bool _defValue;

    internal BoolFlag(string name, bool value, string usage) : base(name, usage)
    {
        _defValue = value;
        Value = value;
    }

    /// <summary>Go: <c>*bool</c> 指向的值（见文件头偏离 2）。</summary>
    internal bool Value { get; private set; }

    internal override string DefValue => _defValue ? "true" : "false";

    internal override bool IsBool => true;

    internal override bool IsString => false;

    internal override string Placeholder => "";

    internal override bool IsZeroDefault => !_defValue;

    /// <summary>Go: <c>func (b *boolValue) Set(s string) error</c> → <c>strconv.ParseBool</c>（见文件头偏离 6）。</summary>
    internal override bool TrySet(string value, out string error)
    {
        switch (value)
        {
            case "1" or "t" or "T" or "true" or "TRUE" or "True":
                Value = true;
                error = "";
                return true;
            case "0" or "f" or "F" or "false" or "FALSE" or "False":
                Value = false;
                error = "";
                return true;
            default:
                error = "strconv.ParseBool: parsing " + GoQuote(value) + ": invalid syntax";
                return false;
        }
    }
}

/// <summary>Go: <c>type stringValue string</c>（flag.go）。</summary>
internal sealed class StringFlag : Flag
{
    private readonly string _defValue;

    internal StringFlag(string name, string value, string usage) : base(name, usage)
    {
        _defValue = value;
        Value = value;
    }

    /// <summary>Go: <c>*string</c> 指向的值（见文件头偏离 2）。</summary>
    internal string Value { get; private set; }

    internal override string DefValue => _defValue;

    internal override bool IsBool => false;

    internal override bool IsString => true;

    internal override string Placeholder => "string";

    internal override bool IsZeroDefault => _defValue.Length == 0;

    internal override bool TrySet(string value, out string error)
    {
        Value = value;
        error = "";
        return true;
    }
}

/// <summary>Go: <c>type intValue int</c>（flag.go）。</summary>
internal sealed class IntFlag : Flag
{
    private readonly int _defValue;

    internal IntFlag(string name, int value, string usage) : base(name, usage)
    {
        _defValue = value;
        Value = value;
    }

    /// <summary>Go: <c>*int</c> 指向的值（见文件头偏离 2）。</summary>
    internal int Value { get; private set; }

    internal override string DefValue => _defValue.ToString(CultureInfo.InvariantCulture);

    internal override bool IsBool => false;

    internal override bool IsString => false;

    internal override string Placeholder => "int";

    internal override bool IsZeroDefault => _defValue == 0;

    /// <summary>Go: <c>func (i *intValue) Set(s string) error</c> → <c>strconv.ParseInt(s, 0, strconv.IntSize)</c>（见文件头偏离 5）。</summary>
    internal override bool TrySet(string value, out string error)
    {
        if (!TryParseIntGo(value, out var parsed))
        {
            error = "strconv.ParseInt: parsing " + GoQuote(value) + ": invalid syntax";
            return false;
        }
        if (parsed is < int.MinValue or > int.MaxValue)
        {
            error = "strconv.ParseInt: parsing " + GoQuote(value) + ": value out of range";
            return false;
        }
        Value = (int)parsed;
        error = "";
        return true;
    }

    /// <summary>Go: <c>strconv.ParseInt(s, 0, 64)</c> 的基数识别（见文件头偏离 5）。</summary>
    private static bool TryParseIntGo(string s, out long value)
    {
        value = 0;
        if (s.Length == 0)
        {
            return false;
        }

        var i = 0;
        var negative = false;
        if (s[0] is '+' or '-')
        {
            negative = s[0] == '-';
            i++;
        }
        if (i >= s.Length)
        {
            return false;
        }

        var radix = 10;
        if (s.Length - i > 1 && s[i] == '0')
        {
            switch (s[i + 1])
            {
                case 'x' or 'X':
                    radix = 16;
                    i += 2;
                    break;
                case 'b' or 'B':
                    radix = 2;
                    i += 2;
                    break;
                case 'o' or 'O':
                    radix = 8;
                    i += 2;
                    break;
                default:
                    // Go: 前导 0（且后面还有数字）是八进制。
                    radix = 8;
                    i += 1;
                    break;
            }
        }

        long result = 0;
        var anyDigit = false;
        for (; i < s.Length; i++)
        {
            var c = s[i];
            int digit;
            if (c is >= '0' and <= '9')
            {
                digit = c - '0';
            }
            else if (c is >= 'a' and <= 'z')
            {
                digit = c - 'a' + 10;
            }
            else if (c is >= 'A' and <= 'Z')
            {
                digit = c - 'A' + 10;
            }
            else
            {
                return false;
            }
            if (digit >= radix)
            {
                return false;
            }
            result = (result * radix) + digit;
            anyDigit = true;
        }
        if (!anyDigit)
        {
            return false;
        }

        value = negative ? -result : result;
        return true;
    }
}
