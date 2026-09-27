// ported from: internal/semver/version.go
//
// 与 Go 不一致的决定（docs/architecture.md §3.2、§3.3、§3.8）：
//
// 1) `TryParseVersion(text string) (Version, error)` 的 error 分支是**预期分支**（调用点
//    packagejson.Cache.GetVersionPaths、module.IsApplicableVersionedTypesKey 就是拿它判断 typesVersions 的键）
//    → `bool TryParseVersion(string text, out Version version)`。Go 在失败时返回的那个 error
//    （`*SemverParseError` 或 strconv 的 ErrRange）在 C# 侧不区分；out 参数仍与 Go 一样是被填到一半的值。
// 2) `MustParse` 的 `panic(err)` 抛的是 Go 的 panic 值本身（`*SemverParseError`），不是字符串，所以这里直接
//    `throw new SemverParseError(text)`，而不是 `Debug.Fail`（后者用于 Go 里 panic(string) 的"不该发生"分支）。
// 3) `Version` 是 `struct`（Go 是值类型）。Go 的未导出字段 → `internal`（`_camelCase`）：测试与被测代码不同程序集，
//    靠 InternalsVisibleTo（Properties/AssemblyInfo.cs）保留 Go 同 package 的可见性，测试里的 `assertVersion`
//    与 Go 一样直接比较 `major/minor/patch/prerelease/build` 字段。C# 没有 Go 的"复合字面量 + 未导出字段"
//    初始化方式，所以补一个 internal 构造器（包外只能得到 Go 的零值 `Version{}` = `default(Version)`）。
// 4) `Compare` 的 `a == b`（指针同一）与 `a == nil`/`b == nil` 三个 case 在 C# 里没有对应物：接收者是值、
//    `in Version` 参数不可为 null（docs/architecture.md §3.2 的"死代码直接删"）。Go 里 `a == nil` 时返回
//    LessThan 的行为对应 C# 的 `default(Version)`（0.0.0），调用点（ata/discovertypings）不会传 nil。
// 5) Go 的 `strings.Builder` + `fmt.Fprintf("%d.%d.%d")` → `StringBuilder.Append(uint)` +
//    `string.Join('.', …)`（uint 的 ToString 在任何区域下都是 ASCII 数字，与 Go 的 `%d` 一致）。
// 6) 正则（Go 的 `regexp` → `System.Text.RegularExpressions`，AOT 安全、不用 RegexOptions.Compiled）：
//    * Go 的 `\d` / `\s` 默认只匹配 ASCII（`[0-9]` / `[\t\n\f\r ]`），而 .NET 的 `\d` / `\s` 是 Unicode 类别
//      （`\d` 会匹配阿拉伯-印度数字等），所以 pattern 里显式写成 `[0-9]` / `[\t\n\f\r ]`。
//    * Go 在非 multiline 下的 `$` 等价于 .NET 的 `\z`（.NET 的 `$` 还会匹配"结尾换行之前"的位置，
//      于是 "1.2.3\n" 在 .NET 下会误判为合法），因此 pattern 结尾统一写 `\z`；开头的 `^` 两边语义相同，保持原样。
//    * `(?i)` → `RegexOptions.IgnoreCase`，另加 `RegexOptions.CultureInvariant`（Go 的 case folding 与区域无关）。
// 7) `strconv.ParseUint(text, 10, 32)` → `uint.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out _)`：
//    两者都只接受纯十进制数字（无符号、无空白、无下划线），且越界都算失败
//    （version_range 测试里 320 个 '1' 的用例走的就是"溢出 → 退化成比较长度"这条路径）。
// 8) `SemverParseError` 的 Go 字段 `origInput` 只用于拼错误消息，C# 侧在构造时拼好（primary constructor 的参数
//    不会被捕获成字段），因此没有单独的同名字段；`Error() string` → `Exception.Message`
//    （compare.mjs 的白名单已按此约定放行 `Error`，与 JsonException/MappingError 同一写法）。
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
// `Version` 在本文件里是独立类型，调用包级（= `SemVer` 静态类）的 internal 辅助函数需要 using static
// （不能写 `SemVer.X`：那会先解析到命名空间，见 docs/architecture.md §2）。
using static TypeScript.SemVer.SemVer;

namespace TypeScript.SemVer;

// https://semver.org/#spec-item-2
// > A normal version number MUST take the form X.Y.Z where X, Y, and Z are non-negative
// > integers, and MUST NOT contain leading zeroes. X is the major version, Y is the minor
// > version, and Z is the patch version. Each element MUST increase numerically.
//
// NOTE: We differ here in that we allow X and X.Y, with missing parts having the default
// value of `0`.
//
// Go 的包级 var/const → `SemVer` 静态类的 internal 成员（C# 没有 package 作用域；命名按 .editorconfig 的
// `_camelCase` 字段规则，与 Core/LinkStore.cs 的 `_pageShift` 同一约定）。
public static partial class SemVer
{
    internal static readonly Regex _versionRegexp = new(
        @"^(0|[1-9][0-9]*)(?:\.(0|[1-9][0-9]*)(?:\.(0|[1-9][0-9]*)(?:-([a-z0-9-.]+))?(?:\+([a-z0-9-.]+))?)?)?\z",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    // https://semver.org/#spec-item-9
    // > A pre-release version MAY be denoted by appending a hyphen and a series of dot separated
    // > identifiers immediately following the patch version. Identifiers MUST comprise only ASCII
    // > alphanumerics and hyphen [0-9A-Za-z-]. Identifiers MUST NOT be empty. Numeric identifiers
    // > MUST NOT include leading zeroes.
    internal static readonly Regex _prereleaseRegexp = new(
        @"^(?:0|[1-9][0-9]*|[a-z-][a-z0-9-]*)(?:\.(?:0|[1-9][0-9]*|[a-zA-Z-][a-zA-Z0-9-]*))*\z",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    internal static readonly Regex _prereleasePartRegexp = new(
        @"^(?:0|[1-9][0-9]*|[a-z-][a-z0-9-]*)\z",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    // https://semver.org/#spec-item-10
    // > Build metadata MAY be denoted by appending a plus sign and a series of dot separated
    // > identifiers immediately following the patch or pre-release version. Identifiers MUST
    // > comprise only ASCII alphanumerics and hyphen [0-9A-Za-z-]. Identifiers MUST NOT be empty.
    internal static readonly Regex _buildRegExp = new(
        @"^[a-z0-9-]+(?:\.[a-z0-9-]+)*\z",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    internal static readonly Regex _buildPartRegExp = new(
        @"^[a-z0-9-]+\z",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    // https://semver.org/#spec-item-9
    // > Numeric identifiers MUST NOT include leading zeroes.
    internal static readonly Regex _numericIdentifierRegExp = new(
        @"^(?:0|[1-9][0-9]*)\z",
        RegexOptions.CultureInvariant);

    internal static readonly Version _versionZero = new(0, 0, 0, ["0"]);

    internal const int _comparisonLessThan = -1;
    internal const int _comparisonEqualTo = 0;
    internal const int _comparisonGreaterThan = 1;

    public static bool TryParseVersion(string text, out Version version)
    {
        var result = default(Version); // Go: var result Version

        var match = _versionRegexp.Match(text);
        if (!match.Success)
        {
            // Go: return result, &SemverParseError{origInput: text}
            version = result;
            return false;
        }

        // Go 的 FindStringSubmatch 对未参与匹配的分组返回 ""，与 Match.Groups[i].Value 一致。
        var majorStr = match.Groups[1].Value;
        var minorStr = match.Groups[2].Value;
        var patchStr = match.Groups[3].Value;
        var prereleaseStr = match.Groups[4].Value;
        var buildStr = match.Groups[5].Value;

        // Go 的 getUintComponent 返回 (uint32, error)：这里用 out 参数，任一失败即整串解析失败
        // （失败时依然把已填好的部分写回 out，与 Go 返回半成品 Version 的行为一致）。
        if (!GetUintComponent(majorStr, out result._major))
        {
            version = result;
            return false;
        }

        if (minorStr.Length != 0)
        {
            if (!GetUintComponent(minorStr, out result._minor))
            {
                version = result;
                return false;
            }
        }

        if (patchStr.Length != 0)
        {
            if (!GetUintComponent(patchStr, out result._patch))
            {
                version = result;
                return false;
            }
        }

        if (prereleaseStr.Length != 0)
        {
            if (!_prereleaseRegexp.IsMatch(prereleaseStr))
            {
                version = result;
                return false;
            }

            result._prerelease = prereleaseStr.Split('.');
        }

        if (buildStr.Length != 0)
        {
            if (!_buildRegExp.IsMatch(buildStr))
            {
                version = result;
                return false;
            }

            result._build = buildStr.Split('.');
        }

        version = result;
        return true;
    }

    public static Version MustParse(string text)
    {
        if (!TryParseVersion(text, out var v))
        {
            // Go: panic(err)（err 是 *SemverParseError；调用点 ata/discovertypings 不 recover）
            throw new SemverParseError(text);
        }

        return v;
    }

    private static bool GetUintComponent(string text, out uint result)
    {
        // Go: strconv.ParseUint(text, 10, 32) → (uint32(r), err)
        return uint.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out result);
    }

    internal static int ComparePreReleaseIdentifiers(string[]? left, string[]? right)
    {
        // https://semver.org/#spec-item-11
        // > When major, minor, and patch are equal, a pre-release version has lower precedence
        // > than a normal version.
        // （Go 的 `len(left) == 0` 同时覆盖 nil 切片，C# 侧用 `is null or { Length: 0 }` 保持同一判定。）
        if (left is null || left.Length == 0)
        {
            return right is null || right.Length == 0 ? _comparisonEqualTo : _comparisonGreaterThan;
        }

        if (right is null || right.Length == 0)
        {
            return _comparisonLessThan;
        }

        // https://semver.org/#spec-item-11
        // > Precedence for two pre-release versions with the same major, minor, and patch version
        // > MUST be determined by comparing each dot separated identifier from left to right until
        // > a difference is found [...]
        // Go: slices.CompareFunc(left, right, comparePreReleaseIdentifier)
        var len = Math.Min(left.Length, right.Length);
        for (var i = 0; i < len; i++)
        {
            var r = ComparePreReleaseIdentifier(left[i], right[i]);
            if (r != 0)
            {
                return r;
            }
        }

        return left.Length.CompareTo(right.Length);
    }

    private static int ComparePreReleaseIdentifier(string left, string right)
    {
        // https://semver.org/#spec-item-11
        // > Precedence for two pre-release versions with the same major, minor, and patch version
        // > MUST be determined by comparing each dot separated identifier from left to right until
        // > a difference is found [...]
        // Go: strings.Compare → 序数（UTF-8 字节序）比较，**只返回 -1/0/1**；C# 的 string.CompareOrdinal
        // 返回的是首个不同字符的差值（'A' vs 'a' = -32），所以这里把符号归一化，保持与 Go 逐位一致。
        var ordinal = string.CompareOrdinal(left, right);
        var compareResult = ordinal < 0 ? -1 : ordinal > 0 ? 1 : 0;
        if (compareResult == 0)
        {
            return compareResult;
        }

        var leftIsNumeric = _numericIdentifierRegExp.IsMatch(left);
        var rightIsNumeric = _numericIdentifierRegExp.IsMatch(right);

        if (leftIsNumeric || rightIsNumeric)
        {
            // https://semver.org/#spec-item-11
            // > Numeric identifiers always have lower precedence than non-numeric identifiers.
            if (!rightIsNumeric)
            {
                return _comparisonLessThan;
            }

            if (!leftIsNumeric)
            {
                return _comparisonGreaterThan;
            }

            // https://semver.org/#spec-item-11
            // > identifiers consisting of only digits are compared numerically
            var leftOk = GetUintComponent(left, out var leftAsNumber);
            var rightOk = GetUintComponent(right, out var rightAsNumber);
            if (!leftOk || !rightOk)
            {
                // This should only happen in the event of an overflow.
                // If so, use the lengths or fall back to string comparison.
                // Go: len(left)/len(right) 是**字节**长度；标识符只含 ASCII，与 C# 的 char 长度相同。
                var lenCompare = left.Length.CompareTo(right.Length);
                return lenCompare != 0 ? lenCompare : compareResult;
            }

            return leftAsNumber.CompareTo(rightAsNumber);
        }

        // https://semver.org/#spec-item-11
        // > identifiers with letters or hyphens are compared lexically in ASCII sort order.
        return compareResult;
    }

    // Go 的 `String() string` stringer → `ToString()` 覆写（Version.cs 下方）+ 这个扩展方法，
    // 让调用点保留 Go 的写法 `v.String()`（与 JsNum_Number、Core 枚举同一约定，docs/architecture.md §3.3）。
    public static string String(this Version v)
    {
        return v.ToString();
    }
}

public struct Version
{
    internal uint _major;
    internal uint _minor;
    internal uint _patch;
    internal string[]? _prerelease; // Go 的 nil 切片 = C# 的 null
    internal string[]? _build;

    internal Version(uint major, uint minor = 0, uint patch = 0, string[]? prerelease = null, string[]? build = null)
    {
        _major = major;
        _minor = minor;
        _patch = patch;
        _prerelease = prerelease;
        _build = build;
    }

    internal Version IncrementMajor()
    {
        // Go: Version{major: v.major + 1}（minor/patch/prerelease/build 归零）
        return new Version(_major + 1);
    }

    internal Version IncrementMinor()
    {
        // Go: Version{major: v.major, minor: v.minor + 1}
        return new Version(_major, _minor + 1);
    }

    internal Version IncrementPatch()
    {
        // Go: Version{major: v.major, minor: v.minor, patch: v.patch + 1}
        return new Version(_major, _minor, _patch + 1);
    }

    public int Compare(in Version b)
    {
        // https://semver.org/#spec-item-11
        // > Precedence is determined by the first difference when comparing each of these
        // > identifiers from left to right as follows: Major, minor, and patch versions are
        // > always compared numerically.
        //
        // https://semver.org/#spec-item-11
        // > Precedence for two pre-release versions with the same major, minor, and patch version
        // > MUST be determined by comparing each dot separated identifier from left to right until
        // > a difference is found [...]
        //
        // https://semver.org/#spec-item-11
        // > Build metadata does not figure into precedence
        //
        // Go 的 `a == b` / `a == nil` / `b == nil` 三个 case 没有对应物（见文件头第 4 条）。
        // cmp.Compare(uint32, uint32) → uint.CompareTo（返回 -1/0/1，与 sig 一致）。
        var r = _major.CompareTo(b._major);
        if (r != 0)
        {
            return r;
        }

        r = _minor.CompareTo(b._minor);
        if (r != 0)
        {
            return r;
        }

        r = _patch.CompareTo(b._patch);
        if (r != 0)
        {
            return r;
        }

        return ComparePreReleaseIdentifiers(_prerelease, b._prerelease);
    }

    public override string ToString()
    {
        var sb = new StringBuilder();
        sb.Append(_major).Append('.').Append(_minor).Append('.').Append(_patch);
        if (_prerelease is { Length: > 0 })
        {
            sb.Append('-').Append(string.Join('.', _prerelease));
        }

        if (_build is { Length: > 0 })
        {
            sb.Append('+').Append(string.Join('.', _build));
        }

        return sb.ToString();
    }
}

/// <summary>
/// Go: <c>type SemverParseError struct</c>（实现 Go 的 error 接口）。
/// C# 侧照 JsonException 的写法落成异常类型：Go 的 <c>Error() string</c> → <c>Exception.Message</c>。
/// </summary>
public sealed class SemverParseError(string origInput)
    : Exception($"Could not parse version string from \"{origInput}\"")
{
    // Go 的 fmt.Sprintf("Could not parse version string from %q", e.origInput)：%q 会转义特殊字符，
    // 这里只加引号（与 Ast/SourceFile.cs、JsNum/PseudoBigInt.cs 对 %q 的处理一致；消息文本不参与任何判断）。
}
