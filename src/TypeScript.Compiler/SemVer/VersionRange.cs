// ported from: internal/semver/version_range.go
//
// 与 Go 不一致的决定（docs/architecture.md §3.2、§3.3、§3.8）：
//
// 1) `TryParseVersionRange(text string) (VersionRange, bool)` → `bool TryParseVersionRange(string text, out VersionRange range)`
//    （与 Go 一样，失败时 out 里是空 alternatives 的 VersionRange，`Test` 对空集恒为 true = Go 的 "*"）。
// 2) `VersionRange` 是 `struct`（Go 是值类型），内部 `[][]versionComparator` → `VersionComparator[][]?`
//    （Go 的 nil 切片 = C# 的 null；Go 的"len==0"判定在 C# 侧统一写成 `is null or { Length: 0 }` / `is null || ...`）。
// 3) Go 的未导出类型 `versionComparator` / `partialVersion` → `internal struct VersionComparator` / `PartialVersion`
//    （未导出类型按 C# 习惯命名，字段仍是 `_camelCase`；测试与被测代码靠 InternalsVisibleTo 保持 Go 同 package 的可见性）。
// 4) Go 的 `type comparatorOperator string` + 5 个 typed const 在 C# 里没有对应物（C# 不能给 string 起别名），
//    落成 `internal const string _rangeLessThan…`；这个类型只提供类型安全，取值本身就是 String() 输出的一部分。
// 5) `panic("Unexpected operator: " + op)` 的实参是 string（不是 error 值），正是 Go 里 debug.Fail 的场景 → `Debug.Fail(...)`。
// 6) 正则：与 version.go 同一套映射（`\s` → `[\t\n\f\r ]`、`\d` → `[0-9]`、非 multiline 的 `$` → `\z`、
//    `(?i)` → `RegexOptions.IgnoreCase | RegexOptions.CultureInvariant`；不用 RegexOptions.Compiled，AOT 不友好）。
// 7) `strings.TrimSpace` → `string.Trim()`：Go 的 unicode.IsSpace 与 .NET 的 char.IsWhiteSpace 都覆盖
//    ASCII 空白 + U+0085 + U+00A0 + Unicode Zs，对版本串没有实际差异。
// 8) `strings.Builder` → `StringBuilder`；`append(comparators, parsed...)` → `List<T>.AddRange`。
using System.Text;
using System.Text.RegularExpressions;
// `VersionRange` 在本文件里是独立类型，调用包级（= `SemVer` 静态类）的 internal 辅助函数需要 using static
// （不能写 `SemVer.X`：那会先解析到命名空间，见 docs/architecture.md §2）。
using static TypeScript.SemVer.SemVer;

namespace TypeScript.SemVer;

public static partial class SemVer
{
    // https://github.com/npm/node-semver#range-grammar
    //
    // range-set    ::= range ( logical-or range ) *
    // range        ::= hyphen | simple ( ' ' simple ) * | ”
    // logical-or   ::= ( ' ' ) * '||' ( ' ' ) *
    internal static readonly Regex _logicalOrRegExp = new(@"\|\|", RegexOptions.CultureInvariant);

    internal static readonly Regex _whitespaceRegExp = new(@"[\t\n\f\r ]+", RegexOptions.CultureInvariant);

    // https://github.com/npm/node-semver#range-grammar
    //
    // partial      ::= xr ( '.' xr ( '.' xr qualifier ? )? )?
    // xr           ::= 'x' | 'X' | '*' | nr
    // nr           ::= '0' | ['1'-'9'] ( ['0'-'9'] ) *
    // qualifier    ::= ( '-' pre )? ( '+' build )?
    // pre          ::= parts
    // build        ::= parts
    // parts        ::= part ( '.' part ) *
    // part         ::= nr | [-0-9A-Za-z]+
    internal static readonly Regex _partialRegExp = new(
        @"^([x*0]|[1-9][0-9]*)(?:\.([x*0]|[1-9][0-9]*)(?:\.([x*0]|[1-9][0-9]*)(?:-([a-z0-9-.]+))?(?:\+([a-z0-9-.]+))?)?)?\z",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    // https://github.com/npm/node-semver#range-grammar
    //
    // hyphen       ::= partial ' - ' partial
    internal static readonly Regex _hyphenRegExp = new(
        @"^[\t\n\f\r ]*([a-z0-9-+.*]+)[\t\n\f\r ]+-[\t\n\f\r ]+([a-z0-9-+.*]+)[\t\n\f\r ]*\z",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    // https://github.com/npm/node-semver#range-grammar
    //
    // simple       ::= primitive | partial | tilde | caret
    // primitive    ::= ( '<' | '>' | '>=' | '<=' | '=' ) partial
    // tilde        ::= '~' partial
    // caret        ::= '^' partial
    internal static readonly Regex _rangeRegExp = new(
        @"^([~^<>=]|<=|>=)?[\t\n\f\r ]*([a-z0-9-+.*]+)\z",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    // Go: type comparatorOperator string + 5 个 typed const（见文件头第 4 条）
    internal const string _rangeLessThan = "<";
    internal const string _rangeLessThanEqual = "<=";
    internal const string _rangeEqual = "=";
    internal const string _rangeGreaterThanEqual = ">=";
    internal const string _rangeGreaterThan = ">";

    public static bool TryParseVersionRange(string text, out VersionRange range)
    {
        var ok = TryParseAlternatives(text, out var alternatives);
        range = new VersionRange(alternatives);
        return ok;
    }

    private static bool TryParseAlternatives(string text, out VersionComparator[][]? alternatives)
    {
        alternatives = null; // Go: var alternatives [][]versionComparator
        var result = new List<VersionComparator[]>();

        var trimmed = text.Trim();
        var ranges = _logicalOrRegExp.Split(trimmed);
        foreach (var rangeText in ranges)
        {
            var r = rangeText.Trim();
            if (r.Length == 0)
            {
                continue;
            }

            var comparators = new List<VersionComparator>();

            var hyphenMatch = _hyphenRegExp.Match(r);
            if (hyphenMatch.Success)
            {
                if (TryParseHyphen(hyphenMatch.Groups[1].Value, hyphenMatch.Groups[2].Value, out var parsedComparators))
                {
                    comparators.AddRange(parsedComparators);
                }
                else
                {
                    return false;
                }
            }
            else
            {
                foreach (var simple in _whitespaceRegExp.Split(r))
                {
                    var match = _rangeRegExp.Match(simple.Trim());
                    if (!match.Success)
                    {
                        return false;
                    }

                    if (TryParseComparator(match.Groups[1].Value, match.Groups[2].Value, out var parsedComparators))
                    {
                        comparators.AddRange(parsedComparators);
                    }
                    else
                    {
                        return false;
                    }
                }
            }

            result.Add(comparators.ToArray());
        }

        alternatives = result.ToArray();
        return true;
    }

    private static bool TryParseHyphen(string left, string right, out VersionComparator[] comparators)
    {
        comparators = [];
        if (!TryParsePartial(left, out var leftResult))
        {
            return false;
        }

        if (!TryParsePartial(right, out var rightResult))
        {
            return false;
        }

        var result = new List<VersionComparator>();
        if (!IsWildcard(leftResult._majorStr))
        {
            // `MAJOR.*.*-...` gives us `>=MAJOR.0.0 ...`
            result.Add(new VersionComparator(_rangeGreaterThanEqual, leftResult._version));
        }

        if (!IsWildcard(rightResult._majorStr))
        {
            string op;
            var operand = rightResult._version;

            if (IsWildcard(rightResult._minorStr))
            {
                // `...-MAJOR.*.*` gives us `... <(MAJOR+1).0.0`
                operand = operand.IncrementMajor();
                op = _rangeLessThan;
            }
            else if (IsWildcard(rightResult._patchStr))
            {
                // `...-MAJOR.MINOR.*` gives us `... <MAJOR.(MINOR+1).0`
                operand = operand.IncrementMinor();
                op = _rangeLessThan;
            }
            else
            {
                // `...-MAJOR.MINOR.PATCH` gives us `... <=MAJOR.MINOR.PATCH`
                op = _rangeLessThanEqual;
            }

            result.Add(new VersionComparator(op, operand));
        }

        comparators = result.ToArray();
        return true;
    }

    // Produces a "partial" version
    private static bool TryParsePartial(string text, out PartialVersion result)
    {
        result = default;

        var match = _partialRegExp.Match(text);
        if (!match.Success)
        {
            return false;
        }

        var majorStr = match.Groups[1].Value;
        var minorStr = match.Groups[2].Value;
        var patchStr = match.Groups[3].Value;
        var prereleaseStr = match.Groups[4].Value;
        var buildStr = match.Groups[5].Value;

        if (minorStr.Length == 0)
        {
            minorStr = "*";
        }

        if (patchStr.Length == 0)
        {
            patchStr = "*";
        }

        uint majorNumeric = 0, minorNumeric = 0, patchNumeric = 0;

        if (IsWildcard(majorStr))
        {
            majorNumeric = 0;
            minorNumeric = 0;
            patchNumeric = 0;
        }
        else
        {
            if (!GetUintComponent(majorStr, out majorNumeric))
            {
                return false;
            }

            if (IsWildcard(minorStr))
            {
                minorNumeric = 0;
                patchNumeric = 0;
            }
            else
            {
                if (!GetUintComponent(minorStr, out minorNumeric))
                {
                    return false;
                }

                if (IsWildcard(patchStr))
                {
                    patchNumeric = 0;
                }
                else if (!GetUintComponent(patchStr, out patchNumeric))
                {
                    return false;
                }
            }
        }

        var prerelease = prereleaseStr.Length != 0 ? prereleaseStr.Split('.') : null;
        var build = buildStr.Length != 0 ? buildStr.Split('.') : null;

        result = new PartialVersion
        {
            _version = new Version(majorNumeric, minorNumeric, patchNumeric, prerelease, build),
            _majorStr = majorStr,
            _minorStr = minorStr,
            _patchStr = patchStr,
        };

        return true;
    }

    private static bool TryParseComparator(string op, string text, out VersionComparator[] comparators)
    {
        comparators = [];

        if (!TryParsePartial(text, out var result))
        {
            return false;
        }

        var comparatorsResult = new List<VersionComparator>();

        if (!IsWildcard(result._majorStr))
        {
            switch (op)
            {
                case "~":
                    {
                        var first = new VersionComparator(_rangeGreaterThanEqual, result._version);

                        Version secondVersion;
                        if (IsWildcard(result._minorStr))
                        {
                            secondVersion = result._version.IncrementMajor();
                        }
                        else
                        {
                            secondVersion = result._version.IncrementMinor();
                        }

                        var second = new VersionComparator(_rangeLessThan, secondVersion);
                        comparatorsResult.Add(first);
                        comparatorsResult.Add(second);
                        break;
                    }

                case "^":
                    {
                        var first = new VersionComparator(_rangeGreaterThanEqual, result._version);

                        Version secondVersion;
                        if (result._version._major > 0 || IsWildcard(result._minorStr))
                        {
                            secondVersion = result._version.IncrementMajor();
                        }
                        else if (result._version._minor > 0 || IsWildcard(result._patchStr))
                        {
                            secondVersion = result._version.IncrementMinor();
                        }
                        else
                        {
                            secondVersion = result._version.IncrementPatch();
                        }

                        var second = new VersionComparator(_rangeLessThan, secondVersion);
                        comparatorsResult.Add(first);
                        comparatorsResult.Add(second);
                        break;
                    }

                case "<":
                case ">=":
                    {
                        var version = result._version;
                        if (IsWildcard(result._minorStr) || IsWildcard(result._patchStr))
                        {
                            version._prerelease = ["0"];
                        }

                        comparatorsResult.Add(new VersionComparator(op, version));
                        break;
                    }

                case "<=":
                case ">":
                    {
                        var version = result._version;
                        var finalOp = op;
                        if (IsWildcard(result._minorStr))
                        {
                            if (finalOp == _rangeLessThanEqual)
                            {
                                finalOp = _rangeLessThan;
                            }
                            else
                            {
                                finalOp = _rangeGreaterThanEqual;
                            }

                            version = version.IncrementMajor();
                            version._prerelease = ["0"];
                        }
                        else if (IsWildcard(result._patchStr))
                        {
                            if (finalOp == _rangeLessThanEqual)
                            {
                                finalOp = _rangeLessThan;
                            }
                            else
                            {
                                finalOp = _rangeGreaterThanEqual;
                            }

                            version = version.IncrementMinor();
                            version._prerelease = ["0"];
                        }

                        comparatorsResult.Add(new VersionComparator(finalOp, version));
                        break;
                    }

                case "=":
                case "":
                    {
                        // normalize empty string to `=`
                        if (IsWildcard(result._minorStr) || IsWildcard(result._patchStr))
                        {
                            var originalVersion = result._version;

                            var firstVersion = originalVersion;
                            firstVersion._prerelease = ["0"];

                            var secondVersion = IsWildcard(result._minorStr)
                                ? originalVersion.IncrementMajor()
                                : originalVersion.IncrementMinor();
                            secondVersion._prerelease = ["0"];

                            comparatorsResult.Add(new VersionComparator(_rangeGreaterThanEqual, firstVersion));
                            comparatorsResult.Add(new VersionComparator(_rangeLessThan, secondVersion));
                        }
                        else
                        {
                            comparatorsResult.Add(new VersionComparator(_rangeEqual, result._version));
                        }

                        break;
                    }

                default:
                    // Go: panic("Unexpected operator: " + operator)
                    Debug.Fail("Unexpected operator: " + op);
                    break;
            }
        }
        else
        {
            if (op is _rangeLessThan or _rangeGreaterThan)
            {
                comparatorsResult.Add(
                    // < 0.0.0-0
                    new VersionComparator(_rangeLessThan, _versionZero));
            }
        }

        comparators = comparatorsResult.ToArray();
        return true;
    }

    internal static void FormatDisjunction(StringBuilder sb, VersionComparator[][]? alternatives)
    {
        var origLen = sb.Length;

        if (alternatives is not null)
        {
            for (var i = 0; i < alternatives.Length; i++)
            {
                if (i > 0)
                {
                    sb.Append(" || ");
                }

                FormatAlternative(sb, alternatives[i]);
            }
        }

        if (sb.Length == origLen)
        {
            sb.Append('*');
        }
    }

    internal static void FormatAlternative(StringBuilder sb, VersionComparator[] comparators)
    {
        for (var i = 0; i < comparators.Length; i++)
        {
            if (i > 0)
            {
                sb.Append(' ');
            }

            FormatComparator(sb, comparators[i]);
        }
    }

    internal static void FormatComparator(StringBuilder sb, in VersionComparator comparator)
    {
        sb.Append(comparator._operator);
        sb.Append(comparator._operand.ToString()); // Go: comparator.operand.String()
    }

    internal static bool TestDisjunction(VersionComparator[][]? alternatives, in Version version)
    {
        // an empty disjunction is treated as "*" (all versions)
        if (alternatives is null || alternatives.Length == 0)
        {
            return true;
        }

        foreach (var alternative in alternatives)
        {
            if (TestAlternative(alternative, version))
            {
                return true;
            }
        }

        return false;
    }

    internal static bool TestAlternative(VersionComparator[] alternative, in Version version)
    {
        foreach (var comparator in alternative)
        {
            if (!TestComparator(comparator, version))
            {
                return false;
            }
        }

        return true;
    }

    internal static bool TestComparator(in VersionComparator comparator, in Version version)
    {
        var cmp = version.Compare(comparator._operand);
        switch (comparator._operator)
        {
            case _rangeLessThan:
                return cmp < 0;
            case _rangeLessThanEqual:
                return cmp <= 0;
            case _rangeEqual:
                return cmp == 0;
            case _rangeGreaterThanEqual:
                return cmp >= 0;
            case _rangeGreaterThan:
                return cmp > 0;
            default:
                // Go: panic("Unexpected operator: " + comparator.operator)
                Debug.Fail("Unexpected operator: " + comparator._operator);
                return false;
        }
    }

    private static bool IsWildcard(string text)
    {
        return text is "*" or "x" or "X";
    }

    // Go 的 `String() string` stringer → `ToString()` 覆写（见下方）+ 这个扩展方法，
    // 让调用点保留 Go 的写法 `range.String()`（docs/architecture.md §3.3）。
    public static string String(this VersionRange range)
    {
        return range.ToString();
    }
}

public struct VersionRange
{
    internal VersionComparator[][]? _alternatives; // Go 的 nil 切片 = null

    internal VersionRange(VersionComparator[][]? alternatives)
    {
        _alternatives = alternatives;
    }

    public bool Test(in Version version)
    {
        return TestDisjunction(_alternatives, version);
    }

    public override string ToString()
    {
        var sb = new StringBuilder();
        FormatDisjunction(sb, _alternatives);
        return sb.ToString();
    }
}

internal struct VersionComparator
{
    internal string _operator; // Go: type comparatorOperator string（见文件头第 4 条）
    internal Version _operand;

    internal VersionComparator(string op, Version operand)
    {
        _operator = op;
        _operand = operand;
    }
}

internal struct PartialVersion
{
    internal Version _version;
    internal string _majorStr;
    internal string _minorStr;
    internal string _patchStr;
}
