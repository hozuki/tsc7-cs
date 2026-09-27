// ported from: internal/scanner/scanner.go:1484-1584（标识符：scanIdentifier/scanIdentifierParts/scanIdentifierEscape）
//             + internal/scanner/scanner.go:2213-2282（GetIdentifierToken/IsValidIdentifier/isWordCharacter/IsIdentifier*、
//               textToKeyword / textToToken / tokenToText 表与 TokenToString / StringToToken / GetViableKeywordSuggestions）
//             + internal/scanner/scanner.go:1228-1244（ReScanHashToken / ReScanQuestionToken）
//
// 与 Go 的偏离（其余逐行对应）：
// - Go 的 `map[string]ast.Kind` → `Dictionary<string, Kind>` + `StringComparer.Ordinal`（Go 的字符串比较就是
//   序数比较）。`textToKeyword`/`textToToken`/`tokenToText` 之间的初始化顺序在 C# 里不能靠"字段书写顺序"
//   保证（partial 类型分成多个文件后，静态字段初始化器的顺序由编译器决定），所以三者都在**静态构造器**里
//   赋值 —— 静态字段初始化器一定先于静态构造器体执行（见静态构造器里的注释）。
// - Go 的 `textToToken` 用 `maps.Copy` 把关键字表并进来；C# 用显式 foreach 拷贝（语义相同）。
// - `tokenToText` 在 Go 里是 `[ast.KindCount]string` 数组，未命中的槽位是 ""（Go 的零值）；C# 的
//   `new string[n]` 槽位默认是 null，所以建表后显式填 ""。
// - `GetViableKeywordSuggestions` 的返回类型：Go 的 `[]string` → C# `string[]`。
using TypeScript.Ast;
using TypeScript.Core;
using static TypeScript.StringUtil.StringUtil;

namespace TypeScript.Scanner;

public sealed partial class Scanner
{
    // Go: var textToKeyword = map[string]ast.Kind{...}（在静态构造器之前初始化，CreateTextToToken 读它）
    internal static readonly Dictionary<string, Kind> _textToKeyword = new(StringComparer.Ordinal)
    {
        ["abstract"] = Kind.AbstractKeyword,
        ["accessor"] = Kind.AccessorKeyword,
        ["any"] = Kind.AnyKeyword,
        ["as"] = Kind.AsKeyword,
        ["asserts"] = Kind.AssertsKeyword,
        ["assert"] = Kind.AssertKeyword,
        ["bigint"] = Kind.BigIntKeyword,
        ["boolean"] = Kind.BooleanKeyword,
        ["break"] = Kind.BreakKeyword,
        ["case"] = Kind.CaseKeyword,
        ["catch"] = Kind.CatchKeyword,
        ["class"] = Kind.ClassKeyword,
        ["continue"] = Kind.ContinueKeyword,
        ["const"] = Kind.ConstKeyword,
        ["constructor"] = Kind.ConstructorKeyword,
        ["debugger"] = Kind.DebuggerKeyword,
        ["declare"] = Kind.DeclareKeyword,
        ["default"] = Kind.DefaultKeyword,
        ["defer"] = Kind.DeferKeyword,
        ["delete"] = Kind.DeleteKeyword,
        ["do"] = Kind.DoKeyword,
        ["else"] = Kind.ElseKeyword,
        ["enum"] = Kind.EnumKeyword,
        ["export"] = Kind.ExportKeyword,
        ["extends"] = Kind.ExtendsKeyword,
        ["false"] = Kind.FalseKeyword,
        ["finally"] = Kind.FinallyKeyword,
        ["for"] = Kind.ForKeyword,
        ["from"] = Kind.FromKeyword,
        ["function"] = Kind.FunctionKeyword,
        ["get"] = Kind.GetKeyword,
        ["if"] = Kind.IfKeyword,
        ["immediate"] = Kind.ImmediateKeyword,
        ["implements"] = Kind.ImplementsKeyword,
        ["import"] = Kind.ImportKeyword,
        ["in"] = Kind.InKeyword,
        ["infer"] = Kind.InferKeyword,
        ["instanceof"] = Kind.InstanceOfKeyword,
        ["interface"] = Kind.InterfaceKeyword,
        ["intrinsic"] = Kind.IntrinsicKeyword,
        ["is"] = Kind.IsKeyword,
        ["keyof"] = Kind.KeyOfKeyword,
        ["let"] = Kind.LetKeyword,
        ["module"] = Kind.ModuleKeyword,
        ["namespace"] = Kind.NamespaceKeyword,
        ["never"] = Kind.NeverKeyword,
        ["new"] = Kind.NewKeyword,
        ["null"] = Kind.NullKeyword,
        ["number"] = Kind.NumberKeyword,
        ["object"] = Kind.ObjectKeyword,
        ["package"] = Kind.PackageKeyword,
        ["private"] = Kind.PrivateKeyword,
        ["protected"] = Kind.ProtectedKeyword,
        ["public"] = Kind.PublicKeyword,
        ["override"] = Kind.OverrideKeyword,
        ["out"] = Kind.OutKeyword,
        ["readonly"] = Kind.ReadonlyKeyword,
        ["require"] = Kind.RequireKeyword,
        ["global"] = Kind.GlobalKeyword,
        ["return"] = Kind.ReturnKeyword,
        ["satisfies"] = Kind.SatisfiesKeyword,
        ["set"] = Kind.SetKeyword,
        ["static"] = Kind.StaticKeyword,
        ["string"] = Kind.StringKeyword,
        ["super"] = Kind.SuperKeyword,
        ["switch"] = Kind.SwitchKeyword,
        ["symbol"] = Kind.SymbolKeyword,
        ["this"] = Kind.ThisKeyword,
        ["throw"] = Kind.ThrowKeyword,
        ["true"] = Kind.TrueKeyword,
        ["try"] = Kind.TryKeyword,
        ["type"] = Kind.TypeKeyword,
        ["typeof"] = Kind.TypeOfKeyword,
        ["undefined"] = Kind.UndefinedKeyword,
        ["unique"] = Kind.UniqueKeyword,
        ["unknown"] = Kind.UnknownKeyword,
        ["using"] = Kind.UsingKeyword,
        ["var"] = Kind.VarKeyword,
        ["void"] = Kind.VoidKeyword,
        ["while"] = Kind.WhileKeyword,
        ["with"] = Kind.WithKeyword,
        ["yield"] = Kind.YieldKeyword,
        ["async"] = Kind.AsyncKeyword,
        ["await"] = Kind.AwaitKeyword,
        ["of"] = Kind.OfKeyword,
    };

    internal static readonly Dictionary<string, Kind> _textToToken;

    private static readonly string[] _tokenToText;

    static Scanner()
    {
        // 静态字段初始化器（上面的 _textToKeyword）一定先于静态构造器体执行，所以这里能安全地依赖它。
        _textToToken = CreateTextToToken();
        _tokenToText = CreateTokenToText();
    }

    // Go: var textToToken = func() map[string]ast.Kind { m := map[string]ast.Kind{...}; maps.Copy(m, textToKeyword); return m }()
    private static Dictionary<string, Kind> CreateTextToToken()
    {
        var m = new Dictionary<string, Kind>(StringComparer.Ordinal)
        {
            ["{"] = Kind.OpenBraceToken,
            ["}"] = Kind.CloseBraceToken,
            ["("] = Kind.OpenParenToken,
            [")"] = Kind.CloseParenToken,
            ["["] = Kind.OpenBracketToken,
            ["]"] = Kind.CloseBracketToken,
            ["."] = Kind.DotToken,
            ["..."] = Kind.DotDotDotToken,
            [";"] = Kind.SemicolonToken,
            [","] = Kind.CommaToken,
            ["<"] = Kind.LessThanToken,
            [">"] = Kind.GreaterThanToken,
            ["<="] = Kind.LessThanEqualsToken,
            [">="] = Kind.GreaterThanEqualsToken,
            ["=="] = Kind.EqualsEqualsToken,
            ["!="] = Kind.ExclamationEqualsToken,
            ["==="] = Kind.EqualsEqualsEqualsToken,
            ["!=="] = Kind.ExclamationEqualsEqualsToken,
            ["=>"] = Kind.EqualsGreaterThanToken,
            ["+"] = Kind.PlusToken,
            ["-"] = Kind.MinusToken,
            ["**"] = Kind.AsteriskAsteriskToken,
            ["*"] = Kind.AsteriskToken,
            ["/"] = Kind.SlashToken,
            ["%"] = Kind.PercentToken,
            ["++"] = Kind.PlusPlusToken,
            ["--"] = Kind.MinusMinusToken,
            ["<<"] = Kind.LessThanLessThanToken,
            ["</"] = Kind.LessThanSlashToken,
            [">>"] = Kind.GreaterThanGreaterThanToken,
            [">>>"] = Kind.GreaterThanGreaterThanGreaterThanToken,
            ["&"] = Kind.AmpersandToken,
            ["|"] = Kind.BarToken,
            ["^"] = Kind.CaretToken,
            ["!"] = Kind.ExclamationToken,
            ["~"] = Kind.TildeToken,
            ["&&"] = Kind.AmpersandAmpersandToken,
            ["||"] = Kind.BarBarToken,
            ["?"] = Kind.QuestionToken,
            ["??"] = Kind.QuestionQuestionToken,
            ["?."] = Kind.QuestionDotToken,
            [":"] = Kind.ColonToken,
            ["="] = Kind.EqualsToken,
            ["+="] = Kind.PlusEqualsToken,
            ["-="] = Kind.MinusEqualsToken,
            ["*="] = Kind.AsteriskEqualsToken,
            ["**="] = Kind.AsteriskAsteriskEqualsToken,
            ["/="] = Kind.SlashEqualsToken,
            ["%="] = Kind.PercentEqualsToken,
            ["<<="] = Kind.LessThanLessThanEqualsToken,
            [">>="] = Kind.GreaterThanGreaterThanEqualsToken,
            [">>>="] = Kind.GreaterThanGreaterThanGreaterThanEqualsToken,
            ["&="] = Kind.AmpersandEqualsToken,
            ["|="] = Kind.BarEqualsToken,
            ["^="] = Kind.CaretEqualsToken,
            ["||="] = Kind.BarBarEqualsToken,
            ["&&="] = Kind.AmpersandAmpersandEqualsToken,
            ["??="] = Kind.QuestionQuestionEqualsToken,
            ["@"] = Kind.AtToken,
            ["#"] = Kind.HashToken,
            ["`"] = Kind.BacktickToken,
        };
        foreach (var (text, kind) in _textToKeyword)
        {
            m[text] = kind;
        }

        return m;
    }

    // Go: var tokenToText = func() [ast.KindCount]string { var result [ast.KindCount]string; for text, kind := range textToToken { result[kind] = text }; return result }()
    private static string[] CreateTokenToText()
    {
        var result = new string[(int)Kind.Count];
        Array.Fill(result, "");
        foreach (var (text, kind) in _textToToken)
        {
            result[(int)kind] = text;
        }

        return result;
    }

    internal bool ScanIdentifier(int prefixLength, IdentifierVariant variant)
    {
        var start = _pos;
        _pos += prefixLength;
        var identifierStart = _pos;
        var ch = Char();

        // Fast path for simple ASCII identifiers
        if (variant != IdentifierVariant.JSX && (IsASCIILetter(ch) || ch == '_' || ch == '$'))
        {
            _pos++;
            ScanASCIIWhile(default(IdentifierPartPredicate));
            ch = Char();
            if (ch < 0x80 && ch != '\\')
            {
                _tokenValue = _text[start.._pos];
                return true;
            }

            _pos = identifierStart;
        }

        var (ch2, size) = CharAndSize();
        if (IsIdentifierStart(ch2))
        {
            var languageVariant = LanguageVariant.Standard;
            if (variant == IdentifierVariant.JSX)
            {
                languageVariant = LanguageVariant.JSX;
            }

            while (true)
            {
                _pos += size;
                (ch2, size) = CharAndSize();
                if (!IsIdentifierPartEx(ch2, languageVariant))
                {
                    break;
                }
            }

            _tokenValue = _text[start.._pos];
            if (ch2 == '\\')
            {
                _tokenValue += ScanIdentifierParts(variant);
            }

            return true;
        }

        if (ch2 == '\\')
        {
            var (escaped, ok) = ScanIdentifierEscape(IsIdentifierStart, variant == IdentifierVariant.RegExpGroupName);
            if (ok)
            {
                _tokenValue = _text[start..identifierStart] + CodePointToString(escaped) + ScanIdentifierParts(variant);
                return true;
            }
        }

        return false;
    }

    private string ScanIdentifierParts(IdentifierVariant variant)
    {
        var sb = new System.Text.StringBuilder();
        var start = _pos;
        var languageVariant = LanguageVariant.Standard;
        if (variant == IdentifierVariant.JSX)
        {
            languageVariant = LanguageVariant.JSX;
        }

        while (true)
        {
            var (ch, size) = CharAndSize();
            if (IsIdentifierPartEx(ch, languageVariant))
            {
                _pos += size;
                continue;
            }

            if (ch == '\\')
            {
                var escapeStart = _pos;
                var (escaped, ok) = ScanIdentifierEscape(ch2 => IsIdentifierPartEx(ch2, languageVariant), variant == IdentifierVariant.RegExpGroupName);
                if (ok)
                {
                    sb.Append(_text, start, escapeStart - start);
                    // Go: sb.WriteRune(escaped)（按码点写）；C# 侧按码点写入（astral 码点 2 个代码单元）。
                    sb.Append(CodePointToString(escaped));
                    start = _pos;
                    continue;
                }
            }

            break;
        }

        sb.Append(_text, start, _pos - start);
        return sb.ToString();
    }

    // Go: func (s *Scanner) scanIdentifierEscape(isValid func(rune) bool, allowSurrogatePairEscape bool) (rune, bool)
    // 冷路径（只在标识符里出现反斜杠时走到），保留 Go 的谓词参数。
    private (int Value, bool Ok) ScanIdentifierEscape(Func<int, bool> isValid, bool allowSurrogatePairEscape)
    {
        var escaped = PeekUnicodeEscape();
        if (escaped >= 0 && isValid(escaped))
        {
            return (ScanUnicodeEscape(true), true);
        }

        if (allowSurrogatePairEscape && CharAt(2) != '{' && IsHighSurrogate(escaped))
        {
            // Unlike normal identifiers, group names in regular expressions, whether in Unicode mode or not,
            // accept \u HexLeadSurrogate \u HexTrailSurrogate as part of RegExpIdentifierName.
            // See https://github.com/tc39/ecma262/pull/1869 for the change.
            var savedPos = _pos;
            var savedTokenFlags = _tokenFlags;
            ScanUnicodeEscape(false);

            // scanLowSurrogateEscape also accepts the braced form used in string literals,
            // but RegExpIdentifierName does not allow it.
            if (CharAt(2) != '{')
            {
                var (codePoint, ok) = ScanLowSurrogateEscape(escaped);
                if (ok && isValid(codePoint))
                {
                    return (codePoint, true);
                }
            }

            _pos = savedPos;
            _tokenFlags = savedTokenFlags;
        }

        return (0, false);
    }

    public Kind ReScanHashToken()
    {
        if (_token == Kind.PrivateIdentifier)
        {
            _pos = _tokenStart + 1;
            _token = Kind.HashToken;
        }

        return _token;
    }

    public Kind ReScanQuestionToken()
    {
        if (_token != Kind.QuestionQuestionToken)
        {
            Debug.Fail("'reScanQuestionToken' should only be called on a '??'");
        }

        _pos = _tokenStart + 1;
        _token = Kind.QuestionToken;
        return _token;
    }

    public static Kind GetIdentifierToken(string str)
    {
        if (str.Length >= 2 && str.Length <= 12 && str[0] >= 'a' && str[0] <= 'z')
        {
            if (_textToKeyword.TryGetValue(str, out var keyword) && keyword != Kind.Unknown)
            {
                return keyword;
            }
        }

        return Kind.Identifier;
    }

    public static bool IsValidIdentifier(string s)
    {
        if (s.Length == 0)
        {
            return false;
        }

        var i = 0;
        while (i < s.Length)
        {
            var ch = DecodeRuneAt(s, i, out var size);
            if (i == 0 && !IsIdentifierStart(ch) || i != 0 && !IsIdentifierPart(ch))
            {
                return false;
            }

            i += size;
        }

        return true;
    }

    // Section 6.1.4
    internal static bool IsWordCharacter(int ch)
    {
        return IsASCIILetter(ch) || IsDigit(ch) || ch == '_';
    }

    public static bool IsIdentifierStart(int ch)
    {
        return IsASCIILetter(ch) || ch == '_' || ch == '$' || ch >= 0x80 && IsUnicodeIdentifierStart(ch);
    }

    public static bool IsIdentifierPart(int ch)
    {
        return IsIdentifierPartEx(ch, LanguageVariant.Standard);
    }

    public static bool IsIdentifierPartEx(int ch, LanguageVariant languageVariant)
    {
        return IsWordCharacter(ch) || ch == '$' ||
            ch >= 0x80 && IsUnicodeIdentifierPart(ch) ||
            languageVariant == LanguageVariant.JSX && ch == '-' // ":" is part of JSXNamespacedName, but not JSXIdentifier.
            ;
    }

    public static string TokenToString(Kind token)
    {
        return _tokenToText[(int)token];
    }

    public static Kind StringToToken(string s)
    {
        return _textToToken.TryGetValue(s, out var kind) ? kind : Kind.Unknown;
    }

    public static string[] GetViableKeywordSuggestions()
    {
        var result = new List<string>(_textToKeyword.Count);
        foreach (var text in _textToKeyword.Keys)
        {
            if (text.Length > 2)
            {
                result.Add(text);
            }
        }

        return result.ToArray();
    }
}
