// ported from: internal/scanner/regexp.go（1105 行）
//
// 本文件是 regexp.go 的完整移植：文件里的每一个顶层类型/常量组/函数都有对应物，顺序与 Go 一致。
// `Scanner` 部分（partial）只放 regexp.go 声明的成员（两张表 + checkRegularExpressionFlagAvailability）；
// Go 的 `regExpParser` 在 C# 里是独立类 `RegExpParser`（Go 的包内类型 → internal）。
//
// 与 Go 的差异（C# 语言特性 + UTF-16 语义；逐条依据 docs/architecture.md §2/§3.1/§3.3/§3.8）：
//
// 1) scanSourceCharacter：
//    * Go 的 text 是 UTF-8，非 unicode 模式下非 BMP 字符必须拆成两个 UTF-16 代理项，所以 Go 用
//      `pendingLowSurrogate` 记账（第一次调用只发高代理项且**不前进**，第二次调用才前进）。C# 的 string
//      本身就是 UTF-16，因此**删除 `pendingLowSurrogate` 字段**（scanAlternative/scanClassRanges 里对它的
//      复位语句一并删除）：非 unicode 模式只读一个代码单元（`_text[_pos]`）并前进 1，返回它的 1 字符
//      字符串——这正是 Strada/TS 的行为，产生的字符序列与 Go 的哨兵编码一致。
//    * unicode 模式用 `Scanner.DecodeRuneAt` 解码完整码点；孤立代理项（0xD800-0xDFFF）是 Go
//      `utf8.RuneError` 分支的 C# 对应物 → 前进 size 并返回 ""。
//    * Go 的 `utf8.RuneError` 常量在 C# 里落成本文件的 `_runeError`（Scanner.DecodeRuneAt 在 EOF 与孤立
//      代理项上也返回它）。
// 2) `stringutil.EncodeJSStringRune(cp)` → `Scanner.CodePointToString(cp)`；`utf16.RuneLen`/
//    `utf16.DecodeRune`/`stringutil.CodePointToSurrogatePair`/`stringutil.SurrogatePairToCodePoint` 的那套
//    拆合代理项代码出现在第 1 条删掉的分支里，故本文件**完全不出现**；`stringutil.DecodeJSStringRune(s)`
//    → `Scanner.DecodeJSStringRune(s)`（返回 `(Value, Size)`，Size 是 UTF-16 代码单元数）。Go 的
//    `len(minCharacter) == minSize` 在 C# 里写 `minCharacter.Length == minSize`，语义相同（都是"整个
//    字符串恰好是一个码点"）。哨兵编码本身（EncodeJSStringRune/CombineSurrogatePairs）整体不移植，§3.1。
// 3) Go 的 `string(ch)`（rune → string）→ 本文件的 `RuneToString(ch)`：Go 里 `string(rune(-1))`（EOF 的
//    char() 返回 -1）是 "\uFFFD"，而 `ConvertFromUtf32` 不接受负值，故负值单独处理，其余走
//    `Scanner.CodePointToString`。本文件所有 `string(ch)` 调用点的 ch 都是 ASCII 或 -1。
// 4) `strconv.Atoi` 失败（Go 的 `math.MaxInt`）→ `int.TryParse(value, out var val) ? val : int.MaxValue`。
// 5) `core.GetSpellingSuggestionForStrings(name, maps.Keys(x))` → `GetSpellingSuggestionForStrings(name, x.Keys())`
//    （`HashSet<string>` 就是 `IEnumerable<string>`）；`core.ConcatenateSeq(maps.Keys(a), maps.Keys(b), maps.Keys(c))`
//    → `ConcatenateSeq(a.Keys(), b.Keys(), c.Keys())`（`Core.ConcatenateSeq` 接受 `IEnumerable<T>?[]` 且跳过
//    null）。`valuesOfNonBinaryUnicodeProperties["General_Category"]` 在 Go 里是"可能为 nil 的 *Set"
//    （nil 接收者的 Has 返回 false，见 set.go 文件头第 3 条），C# 用 `TryGetValue` + `&&` 短路（
//    `IsGeneralCategoryValue`）表达同样语义。
// 6) `strings.TrimLeft(a, "0")` → `a.TrimStart('0')`；`strings.Compare(a, b)` → `string.CompareOrdinal(a, b)`
//    只取符号（`Math.Sign`，返回 -1/0/1，与 Go 一致）。
// 7) `strings.ToLower(x)` → `StringUtil.ToLowerJS(x)`（JS 的多字符大小写映射；**不能**用 `string.ToLower`，
//    它跟随当前区域且表不同，§3.4）。
// 8) Go 的 `p.scanner.tokenValue`/`tokenStart`/`pos` → `_scanner._tokenValue`/`_scanner._tokenStart`/
//    `_scanner._pos`（`RegExpParser` 与 `Scanner` 是不同的类，这些字段是 internal，同程序集可见）；
//    `p.scanner.scanIdentifier` → `_scanner.ScanIdentifier`、`errorAt` → `ErrorAt`、`scanEscapeSequence` →
//    `ScanEscapeSequence`、`languageVersion` → `LanguageVersion`、`char`/`charAt` → `_scanner.Char()`/
//    `_scanner.CharAt(offset)`（`CharAt` 与 Go 一样收"相对 pos 的偏移"，故 `CharAt(pos)` 先算 `pos - Pos()`）。
// 9) Go 的 `map[K]V` 索引（键缺失返回零值 ""/nil）→ `TryGetValue` + 零值回退，不用会抛异常的索引器；
//    命名捕获组的作用域查表用 `Dictionary.ContainsKey`（Go 里读 `group[name]` 取 bool）。
// 10) `debug.Fail`/`panic` → `throw new DebugFailureException(...)`；`debug.Assert` → `Debug.Assert(...)`。
// 11) 包级 var/const → `Scanner` partial 类上的 internal static readonly 成员；未导出名按 C# 习惯
//    `_camelCase`（§2 命名规则；.editorconfig 的 IDE1006 在 build 时是 error，含 static/const）；`iota`
//    枚举的数值与 Go 一致，成员名去类型前缀。
// 12) `collections.Set[string]` 的 Go 零值（nil map 的 `Set[string]{}`）→ `new Set<string>()`（set.go 文件头第 2 条）。
// 13) Go 的 `p.text()[i:j]`（零拷贝 string 切片）→ `_scanner._text.Substring(i, j - i)`：Go 在这里返回的就是
//    string（2 字符前瞻、operand/tokenValue 片段），C# 只能拷贝（§3.8 允许）。
// 14) Go 的 `scanner.go` 构造 `&regExpParser{…, groupSpecifiers: make(map[string]bool)}` → 本文件构造器里
//    `_groupSpecifiers = new Dictionary<string, bool>()`（Go 的 map 零值在 C# 里必须显式 new）。
// 15) `regExpFlags` 字段在 Go 里只被构造者赋值、从未读取（本文件保留该字段以对齐 Go 的形状；声明为
//    internal 避免 CS0414——私有字段"赋值但未使用"在 TreatWarningsAsErrors 下会变成错误）。

using TypeScript.Collections;
using TypeScript.Core;
using TypeScript.Diagnostics;
using static TypeScript.Core.Core;
using static TypeScript.Diagnostics.Diagnostics;
using static TypeScript.StringUtil.StringUtil;

namespace TypeScript.Scanner;

/// <summary>Go: <c>type regularExpressionFlags int32</c>（位掩码，成员名去类型前缀；数值与 Go 一致）。</summary>
[Flags]
internal enum RegularExpressionFlags : int
{
    None = 0,
    HasIndices = 1 << 0, // d
    Global = 1 << 1, // g
    IgnoreCase = 1 << 2, // i
    Multiline = 1 << 3, // m
    DotAll = 1 << 4, // s
    Unicode = 1 << 5, // u
    UnicodeSets = 1 << 6, // v
    Sticky = 1 << 7, // y
    AnyUnicodeMode = Unicode | UnicodeSets,
    Modifiers = IgnoreCase | Multiline | DotAll,
}

public sealed partial class Scanner
{
    /// <summary>Go: <c>var charCodeToRegExpFlag = map[rune]regularExpressionFlags{…}</c>。</summary>
    internal static readonly Dictionary<int, RegularExpressionFlags> _charCodeToRegExpFlag = new()
    {
        ['d'] = RegularExpressionFlags.HasIndices,
        ['g'] = RegularExpressionFlags.Global,
        ['i'] = RegularExpressionFlags.IgnoreCase,
        ['m'] = RegularExpressionFlags.Multiline,
        ['s'] = RegularExpressionFlags.DotAll,
        ['u'] = RegularExpressionFlags.Unicode,
        ['v'] = RegularExpressionFlags.UnicodeSets,
        ['y'] = RegularExpressionFlags.Sticky,
    };

    /// <summary>Go: <c>var regExpFlagToFirstAvailableLanguageVersion = map[regularExpressionFlags]core.ScriptTarget{…}</c>。</summary>
    internal static readonly Dictionary<RegularExpressionFlags, ScriptTarget> _regExpFlagToFirstAvailableLanguageVersion = new()
    {
        [RegularExpressionFlags.HasIndices] = ScriptTarget.ES2022,
        [RegularExpressionFlags.DotAll] = ScriptTarget.ES2018,
        [RegularExpressionFlags.UnicodeSets] = ScriptTarget.ES2024,
    };

    /// <summary>Go: <c>func (s *Scanner) checkRegularExpressionFlagAvailability(flag regularExpressionFlags, pos int, size int)</c>。</summary>
    internal void CheckRegularExpressionFlagAvailability(RegularExpressionFlags flag, int pos, int size)
    {
        if (_regExpFlagToFirstAvailableLanguageVersion.TryGetValue(flag, out var availableFrom) && LanguageVersion() < availableFrom)
        {
            ErrorAt(This_regular_expression_flag_is_only_available_when_targeting_0_or_later, pos, size, ToLowerJS(availableFrom.String()));
        }
    }
}

/// <summary>Go: <c>type classSetExpressionType int</c>（iota，数值与 Go 一致）。</summary>
internal enum ClassSetExpressionType
{
    Unknown = 0,
    ClassUnion = 1,
    ClassIntersection = 2,
    ClassSubtraction = 3,
}

/// <summary>Go: <c>type groupNameReference struct { pos int; end int; name string }</c>。</summary>
internal struct GroupNameReference
{
    public int Pos;
    public int End;
    public string Name;
}

/// <summary>Go: <c>type decimalEscapeValue struct { pos int; end int; value int }</c>。</summary>
internal struct DecimalEscapeValue
{
    public int Pos;
    public int End;
    public int Value;
}

internal sealed class RegExpParser
{
    private readonly Scanner _scanner;
    private readonly int _end;
    internal RegularExpressionFlags _regExpFlags; // Go: regExpFlags（只被构造者赋值）
    private readonly bool _anyUnicodeMode;
    private readonly bool _unicodeSetsMode;
    private readonly bool _annexB;

    private bool _anyUnicodeModeOrNonAnnexB;
    private readonly bool _namedCaptureGroups;

    // See scanClassSetExpression.
    private bool _mayContainStrings;
    // The number of all (named and unnamed) capturing groups defined in the regex.
    private int _numberOfCapturingGroups;
    // All named capturing groups defined in the regex.
    private readonly Dictionary<string, bool> _groupSpecifiers;
    // All references to named capturing groups in the regex.
    private readonly List<GroupNameReference> _groupNameReferences = [];
    // All numeric backreferences within the regex.
    private readonly List<DecimalEscapeValue> _decimalEscapes = [];
    // A stack of scopes for named capturing groups. See scanGroupName.
    private readonly List<Dictionary<string, bool>> _namedCapturingGroups = [];

    // Go 的 utf8.RuneError：Scanner.DecodeRuneAt 在 EOF（size 0）与孤立代理项上也返回它。
    private const int _runeError = 0xFFFD;

    internal RegExpParser(
        Scanner scanner,
        int end,
        RegularExpressionFlags regExpFlags,
        bool anyUnicodeMode,
        bool unicodeSetsMode,
        bool annexB,
        bool namedCaptureGroups)
    {
        _scanner = scanner;
        _end = end;
        _regExpFlags = regExpFlags;
        _anyUnicodeMode = anyUnicodeMode;
        _unicodeSetsMode = unicodeSetsMode;
        _annexB = annexB;
        _namedCaptureGroups = namedCaptureGroups;
        // Go: scanner.go 的 `groupSpecifiers: make(map[string]bool)`
        _groupSpecifiers = new Dictionary<string, bool>();
    }

    /// <summary>Go: <c>func (p *regExpParser) pos() int</c>。</summary>
    private int Pos()
    {
        return _scanner._pos;
    }

    /// <summary>Go: <c>func (p *regExpParser) setPos(v int)</c>。</summary>
    private void SetPos(int v)
    {
        _scanner._pos = v;
    }

    /// <summary>Go: <c>func (p *regExpParser) incPos(n int)</c>。</summary>
    private void IncPos(int n)
    {
        _scanner._pos += n;
    }

    /// <summary>Go: <c>func (p *regExpParser) char() rune</c>（只解码当前代码单元；EOF → -1）。</summary>
    private int Char()
    {
        return _scanner.Char();
    }

    /// <summary>Go: <c>func (p *regExpParser) charAt(pos int) rune</c>：Go 内部转成"相对 pos 的偏移"。</summary>
    private int CharAt(int pos)
    {
        return _scanner.CharAt(pos - Pos());
    }

    /// <summary>Go: <c>func (p *regExpParser) error(msg *diagnostics.Message, pos int, length int, args ...any)</c>。</summary>
    private void Error(Message msg, int pos, int length, params object?[] args)
    {
        _scanner.ErrorAt(msg, pos, length, args);
    }

    /// <summary>Go: <c>func (p *regExpParser) text() string</c>。</summary>
    private string Text()
    {
        return _scanner._text;
    }

    // Go 的 `string(ch)`（rune → string）：负值（EOF 的 char() = -1）在 Go 里是 U+FFFD。
    private static string RuneToString(int ch)
    {
        return ch < 0 ? "\uFFFD" : Scanner.CodePointToString(ch);
    }

    /// <summary>Go: <c>func compareDecimalStrings(a string, b string) int</c>。</summary>
    private static int CompareDecimalStrings(string a, string b)
    {
        a = a.TrimStart('0');
        b = b.TrimStart('0');
        if (a == "")
        {
            a = "0";
        }
        if (b == "")
        {
            b = "0";
        }
        if (a.Length != b.Length)
        {
            if (a.Length < b.Length)
            {
                return -1;
            }
            return 1;
        }
        return Math.Sign(string.CompareOrdinal(a, b));
    }

    // Disjunction ::= Alternative ('|' Alternative)*
    /// <summary>Go: <c>func (p *regExpParser) scanDisjunction(isInGroup bool)</c>。</summary>
    private void ScanDisjunction(bool isInGroup)
    {
        // Names defined by any of this disjunction's alternatives. Since exactly one
        // alternative is chosen at runtime, these names are unioned together (rather
        // than intersected) and, when this disjunction is nested inside a group,
        // bubbled up into the enclosing alternative's scope once the group closes.
        // This ensures a name defined inside a nested group (e.g. `(?:(?<a>x))`) is
        // still visible to a duplicate check for a sibling group later in the same
        // enclosing alternative (e.g. `(?:(?<a>x))(?<a>z)`).
        var disjunctionNames = new Set<string>();
        while (true)
        {
            _namedCapturingGroups.Add(new Dictionary<string, bool>());
            ScanAlternative(isInGroup);
            var alternativeNames = _namedCapturingGroups[_namedCapturingGroups.Count - 1];
            _namedCapturingGroups.RemoveAt(_namedCapturingGroups.Count - 1);
            foreach (var name in alternativeNames.Keys)
            {
                disjunctionNames.Add(name);
            }
            if (Char() != '|')
            {
                break;
            }
            IncPos(1);
        }
        if (isInGroup && _namedCapturingGroups.Count > 0)
        {
            var parentScope = _namedCapturingGroups[_namedCapturingGroups.Count - 1];
            foreach (var name in disjunctionNames.Keys())
            {
                parentScope[name] = true;
            }
        }
    }

    // Alternative ::= Term*
    // Term ::=
    //   | Assertion
    //   | Atom Quantifier?
    //
    // Assertion ::=
    //   | '^'
    //   | '$'
    //   | '\b'
    //   | '\B'
    //   | '(?=' Disjunction ')'
    //   | '(?!' Disjunction ')'
    //   | '(?<=' Disjunction ')'
    //   | '(?<!' Disjunction ')'
    //
    // Quantifier ::= QuantifierPrefix '?'?
    // QuantifierPrefix ::=
    //   | '*'
    //   | '+'
    //   | '?'
    //   | '{' DecimalDigits (',' DecimalDigits?)? '}'
    //
    // Atom ::=
    //   | PatternCharacter
    //   | '.'
    //   | '\' AtomEscape
    //   | CharacterClass
    //   | '(?<' RegExpIdentifierName '>' Disjunction ')'
    //   | '(?' RegularExpressionFlags ('-' RegularExpressionFlags)? ':' Disjunction ')'
    //
    // CharacterClass ::= unicodeMode
    //   ? '[' ClassRanges ']'
    //   : '[' ClassSetExpression ']'
    /// <summary>Go: <c>func (p *regExpParser) scanAlternative(isInGroup bool)</c>。</summary>
    private void ScanAlternative(bool isInGroup)
    {
        var isPreviousTermQuantifiable = false;
        while (Pos() < _end)
        {
            var start = Pos();
            var ch = Char();
            switch (ch)
            {
                case '^':
                case '$':
                    IncPos(1);
                    isPreviousTermQuantifiable = false;
                    break;
                case '\\':
                    IncPos(1);
                    switch (Char())
                    {
                        case 'b':
                        case 'B':
                            IncPos(1);
                            isPreviousTermQuantifiable = false;
                            break;
                        default:
                            ScanAtomEscape();
                            isPreviousTermQuantifiable = true;
                            break;
                    }
                    break;
                case '(':
                    IncPos(1);
                    if (Char() == '?')
                    {
                        IncPos(1);
                        switch (Char())
                        {
                            case '=':
                            case '!':
                                IncPos(1);
                                // In Annex B, `(?=Disjunction)` and `(?!Disjunction)` are quantifiable
                                isPreviousTermQuantifiable = !_anyUnicodeModeOrNonAnnexB;
                                break;
                            case '<':
                                var groupNameStart = Pos();
                                IncPos(1);
                                switch (Char())
                                {
                                    case '=':
                                    case '!':
                                        IncPos(1);
                                        isPreviousTermQuantifiable = false;
                                        break;
                                    default:
                                        ScanGroupName(false /*isReference*/);
                                        ScanExpectedChar('>');
                                        if (_scanner.LanguageVersion() < ScriptTarget.ES2018)
                                        {
                                            Error(Named_capturing_groups_are_only_available_when_targeting_ES2018_or_later, groupNameStart, Pos() - groupNameStart);
                                        }
                                        _numberOfCapturingGroups++;
                                        isPreviousTermQuantifiable = true;
                                        break;
                                }
                                break;
                            default:
                                var flagsStart = Pos();
                                var setFlags = ScanPatternModifiers(RegularExpressionFlags.None);
                                if (Char() == '-')
                                {
                                    IncPos(1);
                                    ScanPatternModifiers(setFlags);
                                    if (Pos() == flagsStart + 1)
                                    {
                                        Error(Subpattern_flags_must_be_present_when_there_is_a_minus_sign, flagsStart, Pos() - flagsStart);
                                    }
                                }
                                // Modifier characters were consumed, so this is `(?flags:` rather than a plain `(?:` group.
                                if (Pos() != flagsStart && _scanner.LanguageVersion() < ScriptTarget.ES2025)
                                {
                                    Error(Regular_expression_pattern_modifiers_are_only_available_when_targeting_0_or_later, flagsStart, Pos() - flagsStart, ToLowerJS(ScriptTarget.ES2025.String()));
                                }
                                ScanExpectedChar(':');
                                isPreviousTermQuantifiable = true;
                                break;
                        }
                    }
                    else
                    {
                        _numberOfCapturingGroups++;
                        isPreviousTermQuantifiable = true;
                    }
                    ScanDisjunction(true /*isInGroup*/);
                    ScanExpectedChar(')');
                    break;
                case '{':
                    IncPos(1);
                    var digitsStart = Pos();
                    ScanDigits();
                    var minStr = _scanner._tokenValue;
                    if (!_anyUnicodeModeOrNonAnnexB && minStr == "")
                    {
                        isPreviousTermQuantifiable = true;
                        continue;
                    }
                    if (Char() == ',')
                    {
                        IncPos(1);
                        ScanDigits();
                        var maxStr = _scanner._tokenValue;
                        if (minStr == "")
                        {
                            if (maxStr != "" || Char() == '}')
                            {
                                Error(Incomplete_quantifier_Digit_expected, digitsStart, 0);
                            }
                            else
                            {
                                Error(Unexpected_0_Did_you_mean_to_escape_it_with_backslash, start, 1, RuneToString(ch));
                                isPreviousTermQuantifiable = true;
                                continue;
                            }
                        }
                        else if (maxStr != "")
                        {
                            if (CompareDecimalStrings(minStr, maxStr) > 0 && (_anyUnicodeModeOrNonAnnexB || Char() == '}'))
                            {
                                Error(Numbers_out_of_order_in_quantifier, digitsStart, Pos() - digitsStart);
                            }
                        }
                    }
                    else if (minStr == "")
                    {
                        if (_anyUnicodeModeOrNonAnnexB)
                        {
                            Error(Unexpected_0_Did_you_mean_to_escape_it_with_backslash, start, 1, RuneToString(ch));
                        }
                        isPreviousTermQuantifiable = true;
                        continue;
                    }
                    if (Char() != '}')
                    {
                        if (_anyUnicodeModeOrNonAnnexB)
                        {
                            Error(X_0_expected, Pos(), 0, "}");
                            IncPos(-1);
                        }
                        else
                        {
                            isPreviousTermQuantifiable = true;
                            continue;
                        }
                    }
                    goto case '*';
                case '*':
                case '+':
                case '?':
                    IncPos(1);
                    if (Char() == '?')
                    {
                        // Non-greedy
                        IncPos(1);
                    }
                    if (!isPreviousTermQuantifiable)
                    {
                        Error(There_is_nothing_available_for_repetition, start, Pos() - start);
                    }
                    isPreviousTermQuantifiable = false;
                    break;
                case '.':
                    IncPos(1);
                    isPreviousTermQuantifiable = true;
                    break;
                case '[':
                    IncPos(1);
                    if (_unicodeSetsMode)
                    {
                        ScanClassSetExpression();
                    }
                    else
                    {
                        ScanClassRanges();
                    }
                    ScanExpectedChar(']');
                    isPreviousTermQuantifiable = true;
                    break;
                case ')':
                    if (isInGroup)
                    {
                        return;
                    }
                    goto case ']';
                case ']':
                case '}':
                    if (_anyUnicodeModeOrNonAnnexB || ch == ')')
                    {
                        Error(Unexpected_0_Did_you_mean_to_escape_it_with_backslash, Pos(), 1, RuneToString(ch));
                    }
                    IncPos(1);
                    isPreviousTermQuantifiable = true;
                    break;
                case '/':
                case '|':
                    return;
                default:
                    ScanSourceCharacter();
                    isPreviousTermQuantifiable = true;
                    break;
            }
        }
    }

    /// <summary>Go: <c>func (p *regExpParser) scanPatternModifiers(currFlags regularExpressionFlags) regularExpressionFlags</c>。</summary>
    private RegularExpressionFlags ScanPatternModifiers(RegularExpressionFlags currFlags)
    {
        while (Pos() < _end)
        {
            var ch = Scanner.DecodeRuneAt(Text(), Pos(), out var size);
            if (ch == _runeError || !Scanner.IsIdentifierPart(ch))
            {
                break;
            }
            if (!Scanner._charCodeToRegExpFlag.TryGetValue(ch, out var flag))
            {
                Error(Unknown_regular_expression_flag, Pos(), size);
            }
            else if ((currFlags & flag) != 0)
            {
                Error(Duplicate_regular_expression_flag, Pos(), size);
            }
            else if ((flag & RegularExpressionFlags.Modifiers) == 0)
            {
                Error(This_regular_expression_flag_cannot_be_toggled_within_a_subpattern, Pos(), size);
            }
            else
            {
                // Modifier syntax itself requires ES2025, which is later than any flag that can appear
                // here, so the group's own diagnostic already covers availability.
                currFlags |= flag;
            }
            IncPos(size);
        }
        return currFlags;
    }

    // AtomEscape ::=
    //   | DecimalEscape
    //   | CharacterClassEscape
    //   | CharacterEscape
    //   | 'k<' RegExpIdentifierName '>'
    /// <summary>Go: <c>func (p *regExpParser) scanAtomEscape()</c>。</summary>
    private void ScanAtomEscape()
    {
        Debug.Assert(Pos() > 0 && Text()[Pos() - 1] == '\\');
        switch (Char())
        {
            case 'k':
                IncPos(1);
                if (Char() == '<')
                {
                    IncPos(1);
                    ScanGroupName(true /*isReference*/);
                    ScanExpectedChar('>');
                }
                else if (_anyUnicodeModeOrNonAnnexB || _namedCaptureGroups)
                {
                    Error(X_k_must_be_followed_by_a_capturing_group_name_enclosed_in_angle_brackets, Pos() - 2, 2);
                }
                break;
            case 'q':
                if (_unicodeSetsMode)
                {
                    IncPos(1);
                    Error(X_q_is_only_available_inside_character_class, Pos() - 2, 2);
                    return;
                }
                goto default;
            default:
                if (!ScanCharacterClassEscape() && !ScanDecimalEscape())
                {
                    // Regex literals cannot contain line breaks here, so a character escape must consume something.
                    Debug.Assert(ScanCharacterEscape(true /*atomEscape*/) != "");
                }
                break;
        }
    }

    // DecimalEscape ::= [1-9] [0-9]*
    /// <summary>Go: <c>func (p *regExpParser) scanDecimalEscape() bool</c>。</summary>
    private bool ScanDecimalEscape()
    {
        Debug.Assert(Pos() > 0 && Text()[Pos() - 1] == '\\');
        var ch = Char();
        if (ch >= '1' && ch <= '9')
        {
            var start = Pos();
            ScanDigits();
            var val = int.TryParse(_scanner._tokenValue, out var parsed) ? parsed : int.MaxValue;
            _decimalEscapes.Add(new DecimalEscapeValue { Pos = start, End = Pos(), Value = val });
            return true;
        }
        return false;
    }

    // CharacterEscape ::=
    //   | `c` ControlLetter
    //   | IdentityEscape
    //   | (Other sequences handled by `scanEscapeSequence`)
    //
    // IdentityEscape ::=
    //   | '^' | '$' | '/' | '\' | '.' | '*' | '+' | '?' | '(' | ')' | '[' | ']' | '{' | '}' | '|'
    //   | [~AnyUnicodeMode] (any other non-identifier characters)
    /// <summary>Go: <c>func (p *regExpParser) scanCharacterEscape(atomEscape bool) string</c>。</summary>
    private string ScanCharacterEscape(bool atomEscape)
    {
        Debug.Assert(Pos() > 0 && Text()[Pos() - 1] == '\\');
        var ch = Char();
        switch (ch)
        {
            case -1:
                Error(Undetermined_character_escape, Pos() - 1, 1);
                return "\\";
            case 'c':
                IncPos(1);
                ch = Char();
                if (IsASCIILetter(ch))
                {
                    IncPos(1);
                    return RuneToString(ch & 0x1f);
                }
                if (_anyUnicodeModeOrNonAnnexB)
                {
                    Error(X_c_must_be_followed_by_an_ASCII_letter, Pos() - 2, 2);
                }
                else if (atomEscape)
                {
                    IncPos(-1);
                    return "\\";
                }
                return RuneToString(ch);
            case '^':
            case '$':
            case '/':
            case '\\':
            case '.':
            case '*':
            case '+':
            case '?':
            case '(':
            case ')':
            case '[':
            case ']':
            case '{':
            case '}':
            case '|':
                IncPos(1);
                return RuneToString(ch);
            default:
                IncPos(-1); // back up to include the backslash for scanEscapeSequence
                var flags = EscapeSequenceScanningFlags.RegularExpression;
                if (_annexB)
                {
                    flags |= EscapeSequenceScanningFlags.AnnexB;
                }
                if (_anyUnicodeMode)
                {
                    flags |= EscapeSequenceScanningFlags.AnyUnicodeMode;
                }
                if (atomEscape)
                {
                    flags |= EscapeSequenceScanningFlags.AtomEscape;
                }
                return _scanner.ScanEscapeSequence(flags);
        }
    }

    /// <summary>Go: <c>func (p *regExpParser) scanGroupName(isReference bool)</c>。</summary>
    private void ScanGroupName(bool isReference)
    {
        Debug.Assert(Pos() > 0 && Text()[Pos() - 1] == '<');
        _scanner._tokenStart = Pos();
        if (!_scanner.ScanIdentifier(0, IdentifierVariant.RegExpGroupName))
        {
            Error(Expected_a_capturing_group_name, Pos(), 0);
        }
        else if (isReference)
        {
            _groupNameReferences.Add(new GroupNameReference { Pos = _scanner._tokenStart, End = Pos(), Name = _scanner._tokenValue });
        }
        else if (NamedCapturingGroupsContains(_scanner._tokenValue))
        {
            Error(Named_capturing_groups_with_the_same_name_must_be_mutually_exclusive_to_each_other, _scanner._tokenStart, Pos() - _scanner._tokenStart);
        }
        else
        {
            // A previous definition can only have come from a mutually exclusive alternative.
            // Below ES2018 the group itself is already reported, so don't stack a second error on it.
            if (_groupSpecifiers.ContainsKey(_scanner._tokenValue) &&
                _scanner.LanguageVersion() >= ScriptTarget.ES2018 &&
                _scanner.LanguageVersion() < ScriptTarget.ES2025)
            {
                Error(Duplicate_named_capturing_groups_are_only_available_when_targeting_0_or_later, _scanner._tokenStart, Pos() - _scanner._tokenStart, ToLowerJS(ScriptTarget.ES2025.String()));
            }
            if (_namedCapturingGroups.Count > 0)
            {
                _namedCapturingGroups[_namedCapturingGroups.Count - 1][_scanner._tokenValue] = true;
            }
            _groupSpecifiers[_scanner._tokenValue] = true;
        }
    }

    /// <summary>Go: <c>func (p *regExpParser) namedCapturingGroupsContains(name string) bool</c>。</summary>
    private bool NamedCapturingGroupsContains(string name)
    {
        foreach (var group in _namedCapturingGroups)
        {
            if (group.ContainsKey(name))
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>Go: <c>func (p *regExpParser) isClassContentExit(ch rune) bool</c>。</summary>
    private bool IsClassContentExit(int ch)
    {
        return ch == ']' || Pos() >= _end;
    }

    // ClassRanges ::= '^'? (ClassAtom ('-' ClassAtom)?)*
    /// <summary>Go: <c>func (p *regExpParser) scanClassRanges()</c>。</summary>
    private void ScanClassRanges()
    {
        Debug.Assert(Pos() > 0 && Text()[Pos() - 1] == '[');
        if (Char() == '^')
        {
            IncPos(1);
        }
        while (Pos() < _end)
        {
            var ch = Char();
            if (IsClassContentExit(ch))
            {
                return;
            }
            var minStart = Pos();
            var minCharacter = ScanClassAtom();
            if (Char() == '-')
            {
                IncPos(1);
                ch = Char();
                if (IsClassContentExit(ch))
                {
                    return;
                }
                if (minCharacter == "" && _anyUnicodeModeOrNonAnnexB)
                {
                    Error(A_character_class_range_must_not_be_bounded_by_another_character_class, minStart, Pos() - 1 - minStart);
                }
                var maxStart = Pos();
                var maxCharacter = ScanClassAtom();
                if (maxCharacter == "" && _anyUnicodeModeOrNonAnnexB)
                {
                    Error(A_character_class_range_must_not_be_bounded_by_another_character_class, maxStart, Pos() - maxStart);
                    continue;
                }
                if (minCharacter == "")
                {
                    continue;
                }
                var (minCharacterValue, minSize) = Scanner.DecodeJSStringRune(minCharacter);
                var (maxCharacterValue, maxSize) = Scanner.DecodeJSStringRune(maxCharacter);
                if (minCharacter.Length == minSize && maxCharacter.Length == maxSize && minCharacterValue > maxCharacterValue)
                {
                    Error(Range_out_of_order_in_character_class, minStart, Pos() - minStart);
                }
            }
        }
    }

    // Static Semantics: MayContainStrings
    //     ClassUnion: ClassSetOperands.some(ClassSetOperand => ClassSetOperand.MayContainStrings)
    //     ClassIntersection: ClassSetOperands.every(ClassSetOperand => ClassSetOperand.MayContainStrings)
    //     ClassSubtraction: ClassSetOperands[0].MayContainStrings
    //     ClassSetOperand:
    //         || ClassStringDisjunctionContents.MayContainStrings
    //         || CharacterClassEscape.UnicodePropertyValueExpression.LoneUnicodePropertyNameOrValue.MayContainStrings
    //     ClassStringDisjunctionContents: ClassStrings.some(ClassString => ClassString.ClassSetCharacters.length !== 1)
    //     LoneUnicodePropertyNameOrValue: isBinaryUnicodePropertyOfStrings(LoneUnicodePropertyNameOrValue)

    // ClassSetExpression ::= '^'? (ClassUnion | ClassIntersection | ClassSubtraction)
    // ClassUnion ::= (ClassSetRange | ClassSetOperand)*
    // ClassIntersection ::= ClassSetOperand ('&&' ClassSetOperand)+
    // ClassSubtraction ::= ClassSetOperand ('--' ClassSetOperand)+
    // ClassSetRange ::= ClassSetCharacter '-' ClassSetCharacter
    /// <summary>Go: <c>func (p *regExpParser) scanClassSetExpression()</c>。</summary>
    private void ScanClassSetExpression()
    {
        Debug.Assert(Pos() > 0 && Text()[Pos() - 1] == '[');
        var isCharacterComplement = false;
        if (Char() == '^')
        {
            IncPos(1);
            isCharacterComplement = true;
        }
        var expressionMayContainStrings = false;
        var ch = Char();
        if (IsClassContentExit(ch))
        {
            return;
        }
        var start = Pos();
        var operand = "";
        var twoChars = "";
        if (Pos() + 1 < _end)
        {
            twoChars = Text().Substring(Pos(), 2);
        }
        switch (twoChars)
        {
            case "--":
            case "&&":
                Error(Expected_a_class_set_operand, Pos(), 0);
                _mayContainStrings = false;
                break;
            default:
                operand = ScanClassSetOperand();
                break;
        }
        switch (Char())
        {
            case '-':
                if (Pos() + 1 < _end && CharAt(Pos() + 1) == '-')
                {
                    if (isCharacterComplement && _mayContainStrings)
                    {
                        Error(Anything_that_would_possibly_match_more_than_a_single_character_is_invalid_inside_a_negated_character_class, start, Pos() - start);
                    }
                    expressionMayContainStrings = _mayContainStrings;
                    ScanClassSetSubExpression(ClassSetExpressionType.ClassSubtraction);
                    _mayContainStrings = !isCharacterComplement && expressionMayContainStrings;
                    return;
                }
                break;
            case '&':
                if (Pos() + 1 < _end && CharAt(Pos() + 1) == '&')
                {
                    ScanClassSetSubExpression(ClassSetExpressionType.ClassIntersection);
                    if (isCharacterComplement && _mayContainStrings)
                    {
                        Error(Anything_that_would_possibly_match_more_than_a_single_character_is_invalid_inside_a_negated_character_class, start, Pos() - start);
                    }
                    expressionMayContainStrings = _mayContainStrings;
                    _mayContainStrings = !isCharacterComplement && expressionMayContainStrings;
                    return;
                }
                break;
            default:
                if (isCharacterComplement && _mayContainStrings)
                {
                    Error(Anything_that_would_possibly_match_more_than_a_single_character_is_invalid_inside_a_negated_character_class, start, Pos() - start);
                }
                expressionMayContainStrings = _mayContainStrings;
                break;
        }
        for (; Pos() < _end;)
        {
            ch = Char();
            switch (ch)
            {
                case '-':
                    IncPos(1);
                    ch = Char();
                    if (IsClassContentExit(ch))
                    {
                        _mayContainStrings = !isCharacterComplement && expressionMayContainStrings;
                        return;
                    }
                    if (ch == '-')
                    {
                        IncPos(1);
                        Error(Operators_must_not_be_mixed_within_a_character_class_Wrap_it_in_a_nested_class_instead, Pos() - 2, 2);
                        start = Pos() - 2;
                        operand = Text().Substring(start, Pos() - start);
                        continue;
                    }
                    else
                    {
                        if (operand == "")
                        {
                            Error(A_character_class_range_must_not_be_bounded_by_another_character_class, start, Pos() - 1 - start);
                        }
                        var secondStart = Pos();
                        var secondOperand = ScanClassSetOperand();
                        if (isCharacterComplement && _mayContainStrings)
                        {
                            Error(Anything_that_would_possibly_match_more_than_a_single_character_is_invalid_inside_a_negated_character_class, secondStart, Pos() - secondStart);
                        }
                        expressionMayContainStrings = expressionMayContainStrings || _mayContainStrings;
                        if (secondOperand == "")
                        {
                            Error(A_character_class_range_must_not_be_bounded_by_another_character_class, secondStart, Pos() - secondStart);
                        }
                        else if (operand != "")
                        {
                            var (minCharacterValue, minSize) = Scanner.DecodeJSStringRune(operand);
                            var (maxCharacterValue, maxSize) = Scanner.DecodeJSStringRune(secondOperand);
                            if (operand.Length == minSize && secondOperand.Length == maxSize && minCharacterValue > maxCharacterValue)
                            {
                                Error(Range_out_of_order_in_character_class, start, Pos() - start);
                            }
                        }
                    }
                    break;
                case '&':
                    if (Pos() + 1 < _end && CharAt(Pos() + 1) == '&')
                    {
                        start = Pos();
                        IncPos(2);
                        Error(Operators_must_not_be_mixed_within_a_character_class_Wrap_it_in_a_nested_class_instead, Pos() - 2, 2);
                        if (Char() == '&')
                        {
                            Error(Unexpected_0_Did_you_mean_to_escape_it_with_backslash, Pos(), 1, RuneToString(ch));
                            IncPos(1);
                        }
                        operand = Text().Substring(start, Pos() - start);
                        continue;
                    }
                    break;
            }
            if (IsClassContentExit(Char()))
            {
                break;
            }
            start = Pos();
            twoChars = "";
            if (Pos() + 1 < _end)
            {
                twoChars = Text().Substring(Pos(), 2);
            }
            switch (twoChars)
            {
                case "--":
                case "&&":
                    Error(Operators_must_not_be_mixed_within_a_character_class_Wrap_it_in_a_nested_class_instead, Pos(), 2);
                    IncPos(2);
                    operand = Text().Substring(start, Pos() - start);
                    break;
                default:
                    operand = ScanClassSetOperand();
                    if (isCharacterComplement && _mayContainStrings)
                    {
                        Error(Anything_that_would_possibly_match_more_than_a_single_character_is_invalid_inside_a_negated_character_class, start, Pos() - start);
                    }
                    expressionMayContainStrings = expressionMayContainStrings || _mayContainStrings;
                    break;
            }
        }
        _mayContainStrings = !isCharacterComplement && expressionMayContainStrings;
    }

    /// <summary>Go: <c>func (p *regExpParser) scanClassSetSubExpression(expressionType classSetExpressionType)</c>。</summary>
    private void ScanClassSetSubExpression(ClassSetExpressionType expressionType)
    {
        var expressionMayContainStrings = _mayContainStrings;
        while (Pos() < _end)
        {
            var ch = Char();
            if (IsClassContentExit(ch))
            {
                break;
            }
            switch (ch)
            {
                case '-':
                    IncPos(1);
                    if (Char() == '-')
                    {
                        IncPos(1);
                        if (expressionType != ClassSetExpressionType.ClassSubtraction)
                        {
                            Error(Operators_must_not_be_mixed_within_a_character_class_Wrap_it_in_a_nested_class_instead, Pos() - 2, 2);
                        }
                    }
                    else
                    {
                        Error(Operators_must_not_be_mixed_within_a_character_class_Wrap_it_in_a_nested_class_instead, Pos() - 1, 1);
                    }
                    break;
                case '&':
                    IncPos(1);
                    if (Char() == '&')
                    {
                        IncPos(1);
                        if (expressionType != ClassSetExpressionType.ClassIntersection)
                        {
                            Error(Operators_must_not_be_mixed_within_a_character_class_Wrap_it_in_a_nested_class_instead, Pos() - 2, 2);
                        }
                        if (Char() == '&')
                        {
                            Error(Unexpected_0_Did_you_mean_to_escape_it_with_backslash, Pos(), 1, RuneToString(ch));
                            IncPos(1);
                        }
                    }
                    else
                    {
                        Error(Unexpected_0_Did_you_mean_to_escape_it_with_backslash, Pos() - 1, 1, RuneToString(ch));
                    }
                    break;
                default:
                    switch (expressionType)
                    {
                        case ClassSetExpressionType.ClassSubtraction:
                            Error(X_0_expected, Pos(), 0, "--");
                            break;
                        case ClassSetExpressionType.ClassIntersection:
                            Error(X_0_expected, Pos(), 0, "&&");
                            break;
                    }
                    break;
            }
            ch = Char();
            if (IsClassContentExit(ch))
            {
                Error(Expected_a_class_set_operand, Pos(), 0);
                break;
            }
            ScanClassSetOperand();
            if (expressionType == ClassSetExpressionType.ClassIntersection)
            {
                expressionMayContainStrings = expressionMayContainStrings && _mayContainStrings;
            }
        }
        _mayContainStrings = expressionMayContainStrings;
    }

    // ClassSetOperand ::=
    //   | '[' ClassSetExpression ']'
    //   | '\' CharacterClassEscape
    //   | '\q{' ClassStringDisjunctionContents '}'
    //   | ClassSetCharacter
    /// <summary>Go: <c>func (p *regExpParser) scanClassSetOperand() string</c>。</summary>
    private string ScanClassSetOperand()
    {
        _mayContainStrings = false;
        switch (Char())
        {
            case '[':
                IncPos(1);
                ScanClassSetExpression();
                ScanExpectedChar(']');
                return "";
            case '\\':
                IncPos(1);
                if (ScanCharacterClassEscape())
                {
                    return "";
                }
                else if (Char() == 'q')
                {
                    IncPos(1);
                    if (Char() == '{')
                    {
                        IncPos(1);
                        ScanClassStringDisjunctionContents();
                        ScanExpectedChar('}');
                        return "";
                    }
                    else
                    {
                        Error(X_q_must_be_followed_by_string_alternatives_enclosed_in_braces, Pos() - 2, 2);
                        return "q";
                    }
                }
                IncPos(-1);
                goto default;
            default:
                return ScanClassSetCharacter();
        }
    }

    // ClassStringDisjunctionContents ::= ClassSetCharacter* ('|' ClassSetCharacter*)*
    /// <summary>Go: <c>func (p *regExpParser) scanClassStringDisjunctionContents()</c>。</summary>
    private void ScanClassStringDisjunctionContents()
    {
        Debug.Assert(Pos() > 0 && Text()[Pos() - 1] == '{');
        var characterCount = 0;
        while (Pos() < _end)
        {
            var ch = Char();
            switch (ch)
            {
                case '}':
                    if (characterCount != 1)
                    {
                        _mayContainStrings = true;
                    }
                    return;
                case '|':
                    if (characterCount != 1)
                    {
                        _mayContainStrings = true;
                    }
                    IncPos(1);
                    characterCount = 0;
                    break;
                default:
                    ScanClassSetCharacter();
                    characterCount++;
                    break;
            }
        }
    }

    // ClassSetCharacter ::=
    //   | SourceCharacter -- ClassSetSyntaxCharacter -- ClassSetReservedDoublePunctuator
    //   | '\' (CharacterEscape | ClassSetReservedPunctuator | 'b')
    /// <summary>Go: <c>func (p *regExpParser) scanClassSetCharacter() string</c>。</summary>
    private string ScanClassSetCharacter()
    {
        var ch = Char();
        if (ch == '\\')
        {
            IncPos(1);
            var innerCh = Char();
            switch (innerCh)
            {
                case 'b':
                    IncPos(1);
                    return "\b";
                case '&':
                case '-':
                case '!':
                case '#':
                case '%':
                case ',':
                case ':':
                case ';':
                case '<':
                case '=':
                case '>':
                case '@':
                case '`':
                case '~':
                    IncPos(1);
                    return RuneToString(innerCh);
                default:
                    return ScanCharacterEscape(false /*atomEscape*/);
            }
        }
        else if (Pos() + 1 < _end && ch == CharAt(Pos() + 1))
        {
            switch (ch)
            {
                case '&':
                case '!':
                case '#':
                case '%':
                case '*':
                case '+':
                case ',':
                case '.':
                case ':':
                case ';':
                case '<':
                case '=':
                case '>':
                case '?':
                case '@':
                case '`':
                case '~':
                    Error(A_character_class_must_not_contain_a_reserved_double_punctuator_Did_you_mean_to_escape_it_with_backslash, Pos(), 2);
                    IncPos(2);
                    return Text().Substring(Pos() - 2, 2);
            }
        }
        switch (ch)
        {
            case '/':
            case '(':
            case ')':
            case '[':
            case ']':
            case '{':
            case '}':
            case '-':
            case '|':
                Error(Unexpected_0_Did_you_mean_to_escape_it_with_backslash, Pos(), 1, RuneToString(ch));
                IncPos(1);
                return RuneToString(ch);
        }
        return ScanSourceCharacter();
    }

    // ClassAtom ::=
    //   | SourceCharacter but not one of '\' or ']'
    //   | '\' ClassEscape
    //
    // ClassEscape ::=
    //   | 'b'
    //   | '-'
    //   | CharacterClassEscape
    //   | CharacterEscape
    /// <summary>Go: <c>func (p *regExpParser) scanClassAtom() string</c>。</summary>
    private string ScanClassAtom()
    {
        if (Char() == '\\')
        {
            IncPos(1);
            var ch = Char();
            switch (ch)
            {
                case 'b':
                    IncPos(1);
                    return "\b";
                case '-':
                    IncPos(1);
                    return RuneToString(ch);
                default:
                    if (ScanCharacterClassEscape())
                    {
                        return "";
                    }
                    return ScanCharacterEscape(false /*atomEscape*/);
            }
        }
        else
        {
            return ScanSourceCharacter();
        }
    }

    // CharacterClassEscape ::=
    //   | 'd' | 'D' | 's' | 'S' | 'w' | 'W'
    //   | [+AnyUnicodeMode] ('P' | 'p') '{' UnicodePropertyValueExpression '}'
    /// <summary>Go: <c>func (p *regExpParser) scanCharacterClassEscape() bool</c>。</summary>
    private bool ScanCharacterClassEscape()
    {
        Debug.Assert(Pos() > 0 && Text()[Pos() - 1] == '\\');
        var isCharacterComplement = false;
        var start = Pos() - 1;
        var ch = Char();
        switch (ch)
        {
            case 'd':
            case 'D':
            case 's':
            case 'S':
            case 'w':
            case 'W':
                IncPos(1);
                return true;
            case 'P':
                isCharacterComplement = true;
                goto case 'p';
            case 'p':
                IncPos(1);
                if (Char() == '{')
                {
                    IncPos(1);
                    var propertyNameOrValueStart = Pos();
                    var propertyNameOrValue = ScanWordCharacters();
                    if (Char() == '=')
                    {
                        var propertyName = Scanner._nonBinaryUnicodeProperties.TryGetValue(propertyNameOrValue, out var foundPropertyName) ? foundPropertyName : "";
                        if (Pos() == propertyNameOrValueStart)
                        {
                            Error(Expected_a_Unicode_property_name, Pos(), 0);
                        }
                        else if (propertyName == "")
                        {
                            Error(Unknown_Unicode_property_name, propertyNameOrValueStart, Pos() - propertyNameOrValueStart);
                            var suggestion = GetSpellingSuggestionForUnicodePropertyName(propertyNameOrValue);
                            if (suggestion != "")
                            {
                                Error(Did_you_mean_0, propertyNameOrValueStart, Pos() - propertyNameOrValueStart, suggestion);
                            }
                        }
                        IncPos(1);
                        var propertyValueStart = Pos();
                        var propertyValue = ScanWordCharacters();
                        if (Pos() == propertyValueStart)
                        {
                            Error(Expected_a_Unicode_property_value, Pos(), 0);
                        }
                        else if (propertyName != "")
                        {
                            var values = Scanner._valuesOfNonBinaryUnicodeProperties.TryGetValue(propertyName, out var foundValues) ? foundValues : null;
                            if (values != null && !values.Has(propertyValue))
                            {
                                Error(Unknown_Unicode_property_value, propertyValueStart, Pos() - propertyValueStart);
                                var suggestion = GetSpellingSuggestionForUnicodePropertyValue(propertyName, propertyValue);
                                if (suggestion != "")
                                {
                                    Error(Did_you_mean_0, propertyValueStart, Pos() - propertyValueStart, suggestion);
                                }
                            }
                        }
                    }
                    else
                    {
                        if (Pos() == propertyNameOrValueStart)
                        {
                            Error(Expected_a_Unicode_property_name_or_value, Pos(), 0);
                        }
                        else if (Scanner._binaryUnicodePropertiesOfStrings.Has(propertyNameOrValue))
                        {
                            if (!_unicodeSetsMode)
                            {
                                Error(Any_Unicode_property_that_would_possibly_match_more_than_a_single_character_is_only_available_when_the_Unicode_Sets_v_flag_is_set, propertyNameOrValueStart, Pos() - propertyNameOrValueStart);
                            }
                            else if (isCharacterComplement)
                            {
                                Error(Anything_that_would_possibly_match_more_than_a_single_character_is_invalid_inside_a_negated_character_class, propertyNameOrValueStart, Pos() - propertyNameOrValueStart);
                            }
                            else
                            {
                                _mayContainStrings = true;
                            }
                        }
                        else if (!IsGeneralCategoryValue(propertyNameOrValue) && !Scanner._binaryUnicodeProperties.Has(propertyNameOrValue))
                        {
                            Error(Unknown_Unicode_property_name_or_value, propertyNameOrValueStart, Pos() - propertyNameOrValueStart);
                            var suggestion = GetSpellingSuggestionForUnicodePropertyNameOrValue(propertyNameOrValue);
                            if (suggestion != "")
                            {
                                Error(Did_you_mean_0, propertyNameOrValueStart, Pos() - propertyNameOrValueStart, suggestion);
                            }
                        }
                    }
                    ScanExpectedChar('}');
                    if (!_anyUnicodeMode)
                    {
                        Error(Unicode_property_value_expressions_are_only_available_when_the_Unicode_u_flag_or_the_Unicode_Sets_v_flag_is_set, start, Pos() - start);
                    }
                }
                else if (_anyUnicodeModeOrNonAnnexB)
                {
                    Error(X_0_must_be_followed_by_a_Unicode_property_value_expression_enclosed_in_braces, Pos() - 2, 2, RuneToString(ch));
                }
                else
                {
                    IncPos(-1);
                    return false;
                }
                return true;
        }
        return false;
    }

    // Go: valuesOfNonBinaryUnicodeProperties["General_Category"].Has(value)
    //（键缺失时 Go 的零值是 nil *Set，nil 接收者的 Has 返回 false，见 set.go 文件头第 3 条）
    private static bool IsGeneralCategoryValue(string value)
    {
        return Scanner._valuesOfNonBinaryUnicodeProperties.TryGetValue("General_Category", out var generalCategory) && generalCategory.Has(value);
    }

    /// <summary>Go: <c>func (p *regExpParser) getSpellingSuggestionForUnicodePropertyName(name string) string</c>。</summary>
    private string GetSpellingSuggestionForUnicodePropertyName(string name)
    {
        return GetSpellingSuggestionForStrings(name, Scanner._nonBinaryUnicodeProperties.Keys);
    }

    /// <summary>Go: <c>func (p *regExpParser) getSpellingSuggestionForUnicodePropertyValue(propertyName string, value string) string</c>。</summary>
    private string GetSpellingSuggestionForUnicodePropertyValue(string propertyName, string value)
    {
        var values = Scanner._valuesOfNonBinaryUnicodeProperties.TryGetValue(propertyName, out var foundValues) ? foundValues : null;
        if (values == null)
        {
            return "";
        }
        return GetSpellingSuggestionForStrings(value, values.Keys());
    }

    /// <summary>Go: <c>func (p *regExpParser) getSpellingSuggestionForUnicodePropertyNameOrValue(name string) string</c>。</summary>
    private string GetSpellingSuggestionForUnicodePropertyNameOrValue(string name)
    {
        var generalCategoryKeys = Scanner._valuesOfNonBinaryUnicodeProperties.TryGetValue("General_Category", out var generalCategory) ? generalCategory.Keys() : null;
        return GetSpellingSuggestionForStrings(name, ConcatenateSeq(
            generalCategoryKeys,
            Scanner._binaryUnicodeProperties.Keys(),
            Scanner._binaryUnicodePropertiesOfStrings.Keys()));
    }

    /// <summary>Go: <c>func (p *regExpParser) scanWordCharacters() string</c>。</summary>
    private string ScanWordCharacters()
    {
        var start = Pos();
        while (Pos() < _end)
        {
            var ch = Char();
            if (!Scanner.IsWordCharacter(ch))
            {
                break;
            }
            IncPos(1);
        }
        return Text().Substring(start, Pos() - start);
    }

    /// <summary>Go: <c>func (p *regExpParser) scanSourceCharacter() string</c>（见文件头第 1 条）。</summary>
    private string ScanSourceCharacter()
    {
        if (Pos() >= _end)
        {
            return "";
        }
        if (!_anyUnicodeMode)
        {
            // 非 unicode 模式：C# 的 text 已是 UTF-16，只读一个代码单元并前进 1
            //（Go 的 pendingLowSurrogate 记账本在 C# 里不存在）。
            var ch = _scanner._text[Pos()];
            IncPos(1);
            return ch.ToString();
        }
        var cp = Scanner.DecodeRuneAt(_scanner._text, Pos(), out var size);
        if (size == 0)
        {
            return "";
        }
        if (cp == _runeError || IsHighSurrogate(cp) || IsLowSurrogate(cp))
        {
            // 孤立代理项（Invalid UTF-16）在 Go 侧是 utf8.RuneError：前进 size 并返回 ""，避免死循环。
            IncPos(size);
            return "";
        }
        IncPos(size);
        return Scanner.CodePointToString(cp);
    }

    /// <summary>Go: <c>func (p *regExpParser) scanExpectedChar(ch rune)</c>。</summary>
    private void ScanExpectedChar(int ch)
    {
        if (Char() == ch)
        {
            IncPos(1);
        }
        else
        {
            Error(X_0_expected, Pos(), 0, RuneToString(ch));
        }
    }

    /// <summary>Go: <c>func (p *regExpParser) scanDigits()</c>。</summary>
    private void ScanDigits()
    {
        var start = Pos();
        while (Pos() < _end && IsDigit(Char()))
        {
            IncPos(1);
        }
        _scanner._tokenValue = Text().Substring(start, Pos() - start);
    }

    /// <summary>Go: <c>func (p *regExpParser) run()</c>。</summary>
    internal void Run()
    {
        // Regular expressions are checked more strictly when either in 'u' or 'v' mode, or
        // when not using the looser interpretation of the syntax from ECMA-262 Annex B.
        _anyUnicodeModeOrNonAnnexB = _anyUnicodeMode || !_annexB;

        ScanDisjunction(false /*isInGroup*/);

        foreach (var reference in _groupNameReferences)
        {
            if (!_groupSpecifiers.ContainsKey(reference.Name))
            {
                Error(There_is_no_capturing_group_named_0_in_this_regular_expression, reference.Pos, reference.End - reference.Pos, reference.Name);
                if (_groupSpecifiers.Count > 0)
                {
                    var suggestion = GetSpellingSuggestionForStrings(reference.Name, _groupSpecifiers.Keys);
                    if (suggestion != "")
                    {
                        Error(Did_you_mean_0, reference.Pos, reference.End - reference.Pos, suggestion);
                    }
                }
            }
        }
        foreach (var escape in _decimalEscapes)
        {
            // Although a DecimalEscape with a value greater than the number of capturing groups
            // is treated as either a LegacyOctalEscapeSequence or an IdentityEscape in Annex B,
            // an error is nevertheless reported since it's most likely a mistake.
            if (escape.Value > _numberOfCapturingGroups)
            {
                if (_numberOfCapturingGroups > 0)
                {
                    Error(This_backreference_refers_to_a_group_that_does_not_exist_There_are_only_0_capturing_groups_in_this_regular_expression, escape.Pos, escape.End - escape.Pos, _numberOfCapturingGroups);
                }
                else
                {
                    Error(This_backreference_refers_to_a_group_that_does_not_exist_There_are_no_capturing_groups_in_this_regular_expression, escape.Pos, escape.End - escape.Pos);
                }
            }
        }
    }
}
