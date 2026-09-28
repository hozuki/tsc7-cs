// ported from: internal/scanner/scanner.go:1-477（类型/常量/状态/构造/访问器/错误回调/字符读取/scanASCIIWhile）
//             + internal/scanner/scanner.go:477-1008（Scan 主循环、processCommentDirective）
//             + internal/scanner/scanner.go:1009-1244（ReScan 系列：<、>、模板、*=、/、JSX、#、?）
//
// 与 Go 的偏离（逐条；其余地方与 Go 逐行对应）：
//
// 1) **位置语义**：Go 的 string 是 UTF-8 字节序列，scanner 的下标是*字节*下标；C# 的 string 是 UTF-16 代码
//    单元序列，所以本文件所有下标都是 **UTF-16 代码单元**下标（docs/architecture.md §3.1）。Go 里
//    `s.text[p]`（取一个字节）对应 C# 的 `_text[p]`（取一个代码单元）；需要"一个完整码点"时 Go 调
//    utf8.DecodeRuneInString，C# 对应 Scanner.DecodeRuneAt（见 Scanner.Escapes.cs 的文件头）。
//
// 2) **哨兵机制不移植**（§3.1）：Go 的 EncodeJSStringRune / DecodeJSStringRune(哨兵) / CombineSurrogatePairs
//    与 surrogateUTF8Lead 等常量在 C# 里**不存在**。C# 的 string 本身就能存孤立代理项，需要"原样保留孤立
//    代理项"的地方直接存 char。
//
// 3) **ScannerState 平铺**：Go 用内嵌结构体 + 字段提升（`s.pos`、`s.token`…）。C# 把状态字段平铺进
//    Scanner（internal `_camelCase`），同时保留 `public struct ScannerState`（同样字段、internal
//    `_camelCase`）供 `Mark()`/`Rewind()` 与 parser 存放状态（internal/parser/parser.go:342 就是这么用的）。
//    `Reset()` 里 Go 的 `*s = defaultScanner()` 是"整体重置"，C# 改**逐字段清空** —— C# 没法把结构体字面量
//    整体赋给一个已有对象；三个缓存照 Go 保留（Reset 只清空内容、不换对象）。
//
// 4) **ScannerState.commentDirectives**：Go 里是切片头（Mark 冻结长度），C# 用 List + 长度快照，
//    Rewind 时截掉多出来的元素 —— 与 Go"恢复切片长度"的可见行为一致（Go 恢复长度后，被隐藏的元素仍留在
//    底层数组里，再次 append 会覆盖它；C# 的截断是等价的）。
//
// 5) **包级函数挂到 Scanner 类的静态成员上**：Go 的 scanner 包名与包内类型名相同，C# 里一个类不可能既
//    是实例类又是静态类，所以 `scanner.GetIdentifierToken(s)` 落成 `Scanner.GetIdentifierToken(s)`
//    （docs/architecture.md §2 的"包级函数进同名静态类"在本题下只能与实例类合并）。调用点读起来仍与 Go 一致。
//
// 6) **ErrorCallback**：Go 的 `func(diagnostic *diagnostics.Message, start, length int, args ...any)` →
//    C# `public delegate void ErrorCallback(Message diagnostic, int start, int length, params object?[] args)`
//    （Message 是 class，对应 Go 的 *Message；C# 变长参数用 params 数组表达 Go 的 `...any`）。
//
// 7) **`TokenFlags()` 改成属性**：访问器名与类型名同为 `TokenFlags`，C# 的"Color Color"规则不覆盖方法
//    （方法会遮蔽类型名 → `TokenFlags.None` 报 CS0119），所以照 docs/ast.md 的先例落成
//    `public TokenFlags TokenFlags { get; }`，调用点由 `s.TokenFlags()` 变成 `s.TokenFlags`。
//
// 8) **`ReScanSlashToken(reportErrors ...bool)`** 的变长参数 → C# 可选参数 `bool shouldReportErrors = false`。
//
// 9) **scanASCIIWhile 的谓词**：Go 传闭包；C# 用委托时，捕获变量的闭包每次调用都会分配（热路径，每个
//    token 都会走到），故用"泛型 + 结构体谓词"（`IASCIIBytePredicate`）实现同一语义：仍然只扫 ASCII
//    （`char < 0x80 && pred(char)`），但零分配、可内联。
//
// 10) **词典查表**：Go 的 `m[k]` 未命中给零值，C# 的 Dictionary 索引器会抛异常，故一律 `TryGetValue`
//     （未命中取 Go 的零值 Kind.Unknown / null）。
//
// 11) **`charAndSize` 的单代理项**：返回该代理项自身的值、Size=1（TS 语义，见 §3.1）；Go 里孤立代理项
//     是 CESU-8 哨兵 → DecodeJSStringRune 也把它解回代理项值，语义一致。`RuneError`(U+FFFD) 在 C# 侧
//     只可能来自源文本里**字面量** U+FFFD（`string` 已经解码过，不存在"非法 UTF-8 字节"），
//     Scan 的二进制文件判定因此只对字面量 U+FFFD 生效（见该分支注释）。
using System.Text;
using TypeScript.Ast;
using TypeScript.Collections;
using TypeScript.Core;
using static TypeScript.Core.Core;
using TypeScript.Diagnostics;
using static TypeScript.Diagnostics.Diagnostics;
using static TypeScript.StringUtil.StringUtil;

namespace TypeScript.Scanner;

/// <summary>Go: <c>type ErrorCallback func(diagnostic *diagnostics.Message, start, length int, args ...any)</c>。</summary>
public delegate void ErrorCallback(Message diagnostic, int start, int length, params object?[] args);

/// <summary>Go: <c>type identifierVariant int32</c>（未导出；成员名去前缀，数值与 Go 一致）。</summary>
internal enum IdentifierVariant
{
    Standard = 0,
    JSX = 1,
    RegExpGroupName = 2,
}

/// <summary>Go: <c>type EscapeSequenceScanningFlags int32</c>（位标志；成员名去前缀，数值与 Go 一致）。</summary>
[Flags]
public enum EscapeSequenceScanningFlags : int
{
    None = 0,
    String = 1 << 0,
    ReportErrors = 1 << 1,
    RegularExpression = 1 << 2,
    AnnexB = 1 << 3,
    AnyUnicodeMode = 1 << 4,
    AtomEscape = 1 << 5,
    ReportInvalidEscapeErrors = RegularExpression | ReportErrors,
    AllowExtendedUnicodeEscape = String | AnyUnicodeMode,
}

/// <summary>
/// Go: <c>type ScannerState struct</c>（Go 里内嵌进 Scanner 做字段提升；C# 里同时作为 Mark/Rewind 的载荷，
/// 见文件头第 3 条）。字段与 Go 逐条对应。
/// </summary>
public struct ScannerState
{
    internal int _pos;                       // Current position in text (and ending position of current token)
    internal int _fullStartPos;              // Starting position of current token including preceding whitespace
    internal int _tokenStart;                // Starting position of non-whitespace part of current token
    internal Kind _token;                    // Kind of current token
    internal string _tokenValue;             // Parsed value of current token
    internal TokenFlags _tokenFlags;         // Flags for current token
    internal int _commentDirectivesCount;    // Go: len(commentDirectives)（切片头里的长度）
    internal int _skipJSDocLeadingAsterisks; // Leading asterisks to skip when scanning types inside JSDoc. Should be 0 outside JSDoc
}

/// <summary>
/// Go: <c>type Scanner struct</c>（包内唯一的状态载体；包级函数也挂在这个类的静态成员上，见文件头第 5 条）。
/// </summary>
public sealed partial class Scanner
{
    internal string _text = "";
    internal int _end;
    internal LanguageVariant _languageVariant;
    internal ScriptTarget _scriptTarget;
    internal ErrorCallback? _onError;
    internal bool _skipTrivia;

    // ── Go 的 ScannerState 字段（平铺，见文件头第 3 条）──
    internal int _pos;   // Current position in text (and ending position of current token)
    internal int _fullStartPos; // Starting position of current token including preceding whitespace
    internal int _tokenStart;   // Starting position of non-whitespace part of current token
    internal Kind _token;       // Kind of current token
    internal string _tokenValue = ""; // Parsed value of current token
    internal TokenFlags _tokenFlags;  // Flags for current token
    internal List<CommentDirective> _commentDirectives = new(); // Go: []ast.CommentDirective
    internal int _skipJSDocLeadingAsterisks; // Leading asterisks to skip when scanning types inside JSDoc. Should be 0 outside JSDoc

    private Dictionary<string, string> _numberCache = new(StringComparer.Ordinal);
    private Dictionary<string, string> _hexNumberCache = new(StringComparer.Ordinal);
    private Dictionary<string, string> _hexDigitCache = new(StringComparer.Ordinal);

    // Go: defaultScanner()。用方法而不是全局变量是刻意的（Go 注释：函数可被内联成纯代码，全局变量需要写屏障）；
    // C# 的字段初始化器与 Go 的零值 + skipTrivia=true 等价。
    private static Scanner DefaultScanner() => new() { _skipTrivia = true };

    public static Scanner NewScanner() => DefaultScanner();

    public void Reset()
    {
        // Go: numberCache := cleared(s.numberCache); …; *s = defaultScanner(); s.numberCache = numberCache; …
        var numberCache = Cleared(_numberCache);
        var hexNumberCache = Cleared(_hexNumberCache);
        var hexDigitCache = Cleared(_hexDigitCache);
        // Go 的 `*s = defaultScanner()` → 逐字段清空（见文件头第 3 条）。
        _text = "";
        _end = 0;
        _languageVariant = LanguageVariant.Standard;
        _scriptTarget = ScriptTarget.None;
        _onError = null;
        _skipTrivia = true;
        _pos = 0;
        _fullStartPos = 0;
        _tokenStart = 0;
        _token = Kind.Unknown;
        _tokenValue = "";
        _tokenFlags = TokenFlags.None;
        _commentDirectives.Clear();
        _skipJSDocLeadingAsterisks = 0;
        _numberCache = numberCache;
        _hexNumberCache = hexNumberCache;
        _hexDigitCache = hexDigitCache;
    }

    // Go: func cleared[M ~map[K]V, K comparable, V any](m M) M { clear(m); return m }
    private static Dictionary<string, string> Cleared(Dictionary<string, string> m)
    {
        m.Clear();
        return m;
    }

    public string Text() => _text;

    /// <summary>
    /// Go: <c>s.text[pos:]</c>（例如 scanner.go:999 的 <c>strings.HasPrefix(s.text[pos:], …)</c>）。C# 的
    /// <c>_text[pos..]</c> 既会复制、又不受 <c>_end</c> 收窄约束（Go 的 <c>s.text</c> 可能已被调用方截断）⇒
    /// 这里返回以 <c>_end</c> 为界的零拷贝视图，与 Go 的切片逐字相同。
    /// </summary>
    internal ReadOnlySpan<char> TextFrom(int pos) => _text.AsSpan(pos, _end - pos);

    public Kind Token() => _token;

    // Go: func (s *Scanner) TokenFlags() ast.TokenFlags → C# 属性（名字与类型名相同，见文件头第 7 条）。
    public TokenFlags TokenFlags => _tokenFlags;

    public int TokenFullStart() => _fullStartPos;

    public int TokenStart() => _tokenStart;

    public int TokenEnd() => _pos;

    public string TokenText() => _text[_tokenStart.._pos];

    public string TokenValue() => _tokenValue;

    public TextRange TokenRange() => new(_tokenStart, _pos);

    // Go: func (s *Scanner) CommentDirectives() []ast.CommentDirective
    // Go 返回切片本身（零拷贝）；C# 的 List 不能当数组返回，故拷贝一次（调用点每个文件只取一次）。
    public CommentDirective[] CommentDirectives() => _commentDirectives.ToArray();

    public ScannerState Mark() => new()
    {
        _pos = _pos,
        _fullStartPos = _fullStartPos,
        _tokenStart = _tokenStart,
        _token = _token,
        _tokenValue = _tokenValue,
        _tokenFlags = _tokenFlags,
        _commentDirectivesCount = _commentDirectives.Count,
        _skipJSDocLeadingAsterisks = _skipJSDocLeadingAsterisks,
    };

    public void Rewind(ScannerState state)
    {
        _pos = state._pos;
        _fullStartPos = state._fullStartPos;
        _tokenStart = state._tokenStart;
        _token = state._token;
        _tokenValue = state._tokenValue;
        _tokenFlags = state._tokenFlags;
        // Go 里 ScannerState 带的是切片头，赋值即恢复长度；C# 里对应"把多出来的指令截掉"（文件头第 4 条）。
        if (_commentDirectives.Count > state._commentDirectivesCount)
        {
            _commentDirectives.RemoveRange(state._commentDirectivesCount, _commentDirectives.Count - state._commentDirectivesCount);
        }

        _skipJSDocLeadingAsterisks = state._skipJSDocLeadingAsterisks;
    }

    public void ResetPos(int pos)
    {
        if (pos < 0)
        {
            Debug.Fail("Cannot reset token state to negative position");
        }

        _pos = pos;
        _fullStartPos = pos;
        _tokenStart = pos;
    }

    public void ResetTokenState(int pos)
    {
        ResetPos(pos);
        _token = Kind.Unknown;
        _tokenValue = "";
        _tokenFlags = TokenFlags.None;
    }

    public void SetSkipTrivia(bool skip) => _skipTrivia = skip;

    public bool HasUnicodeEscape() => (_tokenFlags & TokenFlags.UnicodeEscape) != 0;

    public bool HasExtendedUnicodeEscape() => (_tokenFlags & TokenFlags.ExtendedUnicodeEscape) != 0;

    public bool HasPrecedingLineBreak() => (_tokenFlags & TokenFlags.PrecedingLineBreak) != 0;

    public bool HasPrecedingJSDocComment() => (_tokenFlags & TokenFlags.PrecedingJSDocComment) != 0;

    public bool HasPrecedingJSDocLeadingAsterisks() => (_tokenFlags & TokenFlags.PrecedingJSDocLeadingAsterisks) != 0;

    public bool HasPrecedingJSDocWithDeprecatedTag() => (_tokenFlags & TokenFlags.PrecedingJSDocWithDeprecated) != 0;

    public bool HasPrecedingJSDocWithSeeOrLink() => (_tokenFlags & TokenFlags.PrecedingJSDocWithSeeOrLink) != 0;

    // Go: func (s *Scanner) SetText(text string)（scanner.go:396-400）。
    public void SetText(string text) => SetText(text, text.Length);

    /// <summary>
    /// Go: <c>s.SetText(text[:end])</c> 的零拷贝等价物：Go 的 <c>s.text</c> 与 <c>s.end</c> 是**两个**字段
    /// （scanner.go:398，以及 436/444 的 <c>s.pos &lt; s.end</c> 边界检查），而 C# 的 <c>string</c> 不能做视图，
    /// <c>text[..end]</c> 必然 O(end) 复制。调用方只想"把扫描限制在 [0, end)"时，把 <c>_end</c> 收窄即可得到与
    /// "传入截断后的字符串"逐字节相同的扫描结果（<c>_text</c> 保持全文，越界读取全由 <c>_end</c> 挡住）。
    /// 仓内先例：正则子解析的 <c>_end = endOfRegExpBody</c>（Scanner.cs 的 ReScanSlashToken，与 Go 的
    /// <c>s.end = endOfRegExpBody</c>（scanner.go:1193）逐行对应）。
    /// </summary>
    internal void SetText(string text, int end)
    {
        _text = text;
        _end = end;
        // Go: s.ScannerState = ScannerState{} → C# 逐字段清空（见文件头第 3 条）。
        _pos = 0;
        _fullStartPos = 0;
        _tokenStart = 0;
        _token = Kind.Unknown;
        _tokenValue = "";
        _tokenFlags = TokenFlags.None;
        _commentDirectives.Clear();
        _skipJSDocLeadingAsterisks = 0;
    }

    public void SetOnError(ErrorCallback? errorCallback) => _onError = errorCallback;

    public void SetLanguageVariant(LanguageVariant languageVariant) => _languageVariant = languageVariant;

    public void SetScriptTarget(ScriptTarget scriptTarget) => _scriptTarget = scriptTarget;

    // Go: func (s *Scanner) languageVersion() core.ScriptTarget
    internal ScriptTarget LanguageVersion() => _scriptTarget == ScriptTarget.None ? ScriptTargetLatest : _scriptTarget;

    // Go: func (s *Scanner) error(diagnostic *diagnostics.Message)
    internal void Error(Message diagnostic) => ErrorAt(diagnostic, _pos, 0);

    // Go: func (s *Scanner) errorAt(diagnostic *diagnostics.Message, pos int, length int, args ...any)
    internal void ErrorAt(Message diagnostic, int pos, int length, params object?[] args)
    {
        _onError?.Invoke(diagnostic, pos, length, args);
    }

    // NOTE: even though this returns a rune, it only decodes the current code unit.
    // It must be checked against 0x80 (utf8.RuneSelf) to verify that a call to charAndSize
    // is not needed.
    internal int Char() => _pos < _end ? _text[_pos] : -1;

    // NOTE: this returns a rune, but only decodes the code unit at the offset.
    internal int CharAt(int offset) => _pos + offset < _end ? _text[_pos + offset] : -1;

    internal (int Rune, int Size) CharAndSize()
    {
        // Fast path: a single ASCII code unit. The vast majority of source is ASCII; handling
        // it here avoids a full code-point decode on every code unit.
        if (_pos < _end)
        {
            var ch = _text[_pos];
            if (ch < 0x80)
            {
                return (ch, 1);
            }
        }

        return (DecodeRuneAt(_text, _pos, out var size), size);
    }

    /// <summary>Go 的 <c>scanASCIIWhile(pred func(byte) bool)</c> 的谓词（结构体形式，见文件头第 9 条）。</summary>
    private interface IASCIIBytePredicate
    {
        bool Matches(byte b);
    }

    /// <summary>Go: <c>b == ' ' || (b &gt;= '\t' &amp;&amp; b &lt;= '\r')</c>（Scan 里 "\n"/"\r" 分支的收起逻辑）。</summary>
    private readonly struct LineBreakFollowWhitespacePredicate : IASCIIBytePredicate
    {
        public bool Matches(byte b) => b == ' ' || (b >= '\t' && b <= '\r');
    }

    /// <summary>Go: <c>b != '\n' &amp;&amp; b != '\r'</c>（单行注释）。</summary>
    private readonly struct SingleLineCommentPredicate : IASCIIBytePredicate
    {
        public bool Matches(byte b) => b != '\n' && b != '\r';
    }

    /// <summary>Go: <c>b != '*' &amp;&amp; b != '\n' &amp;&amp; b != '\r'</c>（多行注释）。</summary>
    private readonly struct MultiLineCommentPredicate : IASCIIBytePredicate
    {
        public bool Matches(byte b) => b != '*' && b != '\n' && b != '\r';
    }

    /// <summary>Go: <c>(b &gt;= 'a' &amp;&amp; b &lt;= 'z') || (b &gt;= 'A' &amp;&amp; b &lt;= 'Z') || (b &gt;= '0' &amp;&amp; b &lt;= '9') || b == '_' || b == '$'</c>。</summary>
    private readonly struct IdentifierPartPredicate : IASCIIBytePredicate
    {
        public bool Matches(byte b) => (b >= 'a' && b <= 'z') || (b >= 'A' && b <= 'Z') || (b >= '0' && b <= '9') || b == '_' || b == '$';
    }

    /// <summary>Go: <c>b != '`' &amp;&amp; b != '$' &amp;&amp; b != '\\' &amp;&amp; b != '\r'</c>（模板字面量的普通片段）。</summary>
    private readonly struct TemplateCharPredicate : IASCIIBytePredicate
    {
        public bool Matches(byte b) => b != '`' && b != '$' && b != '\\' && b != '\r';
    }

    /// <summary>Go: <c>b &gt;= '0' &amp;&amp; b &lt;= '9'</c>（数字片段）。</summary>
    private readonly struct DecimalDigitPredicate : IASCIIBytePredicate
    {
        public bool Matches(byte b) => b >= '0' && b <= '9';
    }

    /// <summary>
    /// scanASCIIWhile advances _pos over the longest run of ASCII code units for which pred returns
    /// true. It stops at end-of-text, the first non-ASCII code unit, or the first code unit where pred
    /// is false.
    /// </summary>
    private void ScanASCIIWhile<T>(T pred)
        where T : struct, IASCIIBytePredicate
    {
        var text = _text;
        var i = _pos;
        while (i < _end)
        {
            var ch = text[i];
            // Go 的 `b >= utf8.RuneSelf`：非 ASCII 立即停（谓词只接受 ASCII 字节）。
            if (ch >= 0x80 || !pred.Matches((byte)ch))
            {
                break;
            }

            i++;
        }

        _pos = i;
    }

    public Kind Scan()
    {
        _fullStartPos = _pos;
        _tokenFlags = TokenFlags.None;
        while (true)
        {
            var ch = Char();
            _tokenStart = _pos;

            switch (ch)
            {
                case '\t':
                case '\v':
                case '\f':
                case ' ':
                    _pos++;
                    if (_skipTrivia)
                    {
                        continue;
                    }

                    while (true)
                    {
                        var (ch2, size) = CharAndSize();
                        if (!IsWhiteSpaceSingleLine(ch2))
                        {
                            break;
                        }

                        _pos += size;
                    }

                    _token = Kind.WhitespaceTrivia;
                    break;
                case '\n':
                case '\r':
                    _tokenFlags |= TokenFlags.PrecedingLineBreak;
                    if (_skipTrivia)
                    {
                        _pos++;
                        ScanASCIIWhile(default(LineBreakFollowWhitespacePredicate));
                        continue;
                    }

                    if (ch == '\r' && CharAt(1) == '\n')
                    {
                        _pos += 2;
                    }
                    else
                    {
                        _pos++;
                    }

                    _token = Kind.NewLineTrivia;
                    break;
                case '!':
                    if (CharAt(1) == '=')
                    {
                        if (CharAt(2) == '=')
                        {
                            _pos += 3;
                            _token = Kind.ExclamationEqualsEqualsToken;
                        }
                        else
                        {
                            _pos += 2;
                            _token = Kind.ExclamationEqualsToken;
                        }
                    }
                    else
                    {
                        _pos++;
                        _token = Kind.ExclamationToken;
                    }

                    break;
                case '"':
                case '\'':
                    _tokenValue = ScanString(false /*jsxAttributeString*/);
                    _token = Kind.StringLiteral;
                    break;
                case '`':
                    _token = ScanTemplateAndSetTokenValue(false /*shouldEmitInvalidEscapeError*/);
                    break;
                case '%':
                    if (CharAt(1) == '=')
                    {
                        _pos += 2;
                        _token = Kind.PercentEqualsToken;
                    }
                    else
                    {
                        _pos++;
                        _token = Kind.PercentToken;
                    }

                    break;
                case '&':
                    var next = CharAt(1);
                    if (next == '&')
                    {
                        if (CharAt(2) == '=')
                        {
                            _pos += 3;
                            _token = Kind.AmpersandAmpersandEqualsToken;
                        }
                        else
                        {
                            _pos += 2;
                            _token = Kind.AmpersandAmpersandToken;
                        }
                    }
                    else if (next == '=')
                    {
                        _pos += 2;
                        _token = Kind.AmpersandEqualsToken;
                    }
                    else
                    {
                        _pos++;
                        _token = Kind.AmpersandToken;
                    }

                    break;
                case '(':
                    _pos++;
                    _token = Kind.OpenParenToken;
                    break;
                case ')':
                    _pos++;
                    _token = Kind.CloseParenToken;
                    break;
                case '*':
                    next = CharAt(1);
                    if (next == '=')
                    {
                        _pos += 2;
                        _token = Kind.AsteriskEqualsToken;
                    }
                    else if (next == '*')
                    {
                        if (CharAt(2) == '=')
                        {
                            _pos += 3;
                            _token = Kind.AsteriskAsteriskEqualsToken;
                        }
                        else
                        {
                            _pos += 2;
                            _token = Kind.AsteriskAsteriskToken;
                        }
                    }
                    else
                    {
                        _pos++;
                        if (_skipJSDocLeadingAsterisks != 0 &&
                            (_tokenFlags & TokenFlags.PrecedingJSDocLeadingAsterisks) == 0 &&
                            (_tokenFlags & TokenFlags.PrecedingLineBreak) != 0)
                        {
                            _tokenFlags |= TokenFlags.PrecedingJSDocLeadingAsterisks;
                            continue;
                        }

                        _token = Kind.AsteriskToken;
                    }

                    break;
                case '+':
                    next = CharAt(1);
                    if (next == '=')
                    {
                        _pos += 2;
                        _token = Kind.PlusEqualsToken;
                    }
                    else if (next == '+')
                    {
                        _pos += 2;
                        _token = Kind.PlusPlusToken;
                    }
                    else
                    {
                        _pos++;
                        _token = Kind.PlusToken;
                    }

                    break;
                case ',':
                    _pos++;
                    _token = Kind.CommaToken;
                    break;
                case '-':
                    next = CharAt(1);
                    if (next == '=')
                    {
                        _pos += 2;
                        _token = Kind.MinusEqualsToken;
                    }
                    else if (next == '-')
                    {
                        _pos += 2;
                        _token = Kind.MinusMinusToken;
                    }
                    else
                    {
                        _pos++;
                        _token = Kind.MinusToken;
                    }

                    break;
                case '.':
                    next = CharAt(1);
                    if (IsDigit(next))
                    {
                        _token = ScanNumber();
                    }
                    else if (next == '.' && CharAt(2) == '.')
                    {
                        _pos += 3;
                        _token = Kind.DotDotDotToken;
                    }
                    else
                    {
                        _pos++;
                        _token = Kind.DotToken;
                    }

                    break;
                case '/':
                    // Single-line comment
                    if (CharAt(1) == '/')
                    {
                        _pos += 2;

                        while (true)
                        {
                            ScanASCIIWhile(default(SingleLineCommentPredicate));
                            var (ch1, size) = CharAndSize();
                            if (size == 0 || IsLineBreak(ch1))
                            {
                                break;
                            }

                            _pos += size;
                        }

                        ProcessCommentDirective(_tokenStart, _pos, false);

                        if (_skipTrivia)
                        {
                            continue;
                        }

                        _token = Kind.SingleLineCommentTrivia;
                        return _token;
                    }

                    // Multi-line comment
                    if (CharAt(1) == '*')
                    {
                        _pos += 2;
                        var isJSDoc = Char() == '*' && CharAt(1) != '/';

                        var commentClosed = false;
                        var lastLineStart = _tokenStart;
                        while (true)
                        {
                            ScanASCIIWhile(default(MultiLineCommentPredicate));
                            var (ch1, size) = CharAndSize();
                            if (size == 0)
                            {
                                break;
                            }

                            if (ch1 == '*' && CharAt(1) == '/')
                            {
                                _pos += 2;
                                commentClosed = true;
                                break;
                            }

                            _pos += size;

                            if (IsLineBreak(ch1))
                            {
                                lastLineStart = _pos;
                                _tokenFlags |= TokenFlags.PrecedingLineBreak;
                            }
                        }

                        if (isJSDoc)
                        {
                            _tokenFlags |= TokenFlags.PrecedingJSDocComment;
                            ScanJSDocCommentForTags(_text[_tokenStart.._pos]);
                        }

                        ProcessCommentDirective(lastLineStart, _pos, true);

                        if (!commentClosed)
                        {
                            Error(Asterisk_Slash_expected);
                        }

                        if (_skipTrivia)
                        {
                            continue;
                        }

                        if (!commentClosed)
                        {
                            _tokenFlags |= TokenFlags.Unterminated;
                        }

                        _token = Kind.MultiLineCommentTrivia;
                        return _token;
                    }

                    if (CharAt(1) == '=')
                    {
                        _pos += 2;
                        _token = Kind.SlashEqualsToken;
                    }
                    else
                    {
                        _pos++;
                        _token = Kind.SlashToken;
                    }

                    break;
                case '0':
                    if (CharAt(1) == 'X' || CharAt(1) == 'x')
                    {
                        var start = _pos;
                        _pos += 2;
                        var digits = ScanHexDigits(1, true, true);
                        if (digits == "")
                        {
                            Error(Hexadecimal_digit_expected);
                            digits = "0";
                        }

                        if (_hexNumberCache.TryGetValue(digits, out var cachedValue))
                        {
                            _tokenValue = cachedValue;
                        }
                        else
                        {
                            var rawText = _text[start.._pos];
                            if (rawText.StartsWith("0x", StringComparison.Ordinal) && rawText[2..] == digits)
                            {
                                _tokenValue = rawText;
                            }
                            else
                            {
                                _tokenValue = "0x" + digits;
                            }

                            _hexNumberCache[digits] = _tokenValue;
                        }

                        _tokenFlags |= TokenFlags.HexSpecifier;
                        _token = ScanBigIntSuffix();
                        break;
                    }

                    if (CharAt(1) == 'B' || CharAt(1) == 'b')
                    {
                        _pos += 2;
                        var digits = ScanBinaryOrOctalDigits(2);
                        if (digits == "")
                        {
                            Error(Binary_digit_expected);
                            digits = "0";
                        }

                        _tokenValue = "0b" + digits;
                        _tokenFlags |= TokenFlags.BinarySpecifier;
                        _token = ScanBigIntSuffix();
                        break;
                    }

                    if (CharAt(1) == 'O' || CharAt(1) == 'o')
                    {
                        _pos += 2;
                        var digits = ScanBinaryOrOctalDigits(8);
                        if (digits == "")
                        {
                            Error(Octal_digit_expected);
                            digits = "0";
                        }

                        _tokenValue = "0o" + digits;
                        _tokenFlags |= TokenFlags.OctalSpecifier;
                        _token = ScanBigIntSuffix();
                        break;
                    }

                    goto case '1';
                case '1':
                case '2':
                case '3':
                case '4':
                case '5':
                case '6':
                case '7':
                case '8':
                case '9':
                    _token = ScanNumber();
                    break;
                case ':':
                    _pos++;
                    _token = Kind.ColonToken;
                    break;
                case ';':
                    _pos++;
                    _token = Kind.SemicolonToken;
                    break;
                case '<':
                    if (CharAt(1) == '<' && IsConflictMarkerTrivia(_text, _pos))
                    {
                        _pos = ScanConflictMarkerTrivia(_text, _pos, ErrorAt);
                        if (_skipTrivia)
                        {
                            continue;
                        }

                        _token = Kind.ConflictMarkerTrivia;
                        return _token;
                    }

                    if (CharAt(1) == '<')
                    {
                        if (CharAt(2) == '=')
                        {
                            _pos += 3;
                            _token = Kind.LessThanLessThanEqualsToken;
                        }
                        else
                        {
                            _pos += 2;
                            _token = Kind.LessThanLessThanToken;
                        }
                    }
                    else if (CharAt(1) == '=')
                    {
                        _pos += 2;
                        _token = Kind.LessThanEqualsToken;
                    }
                    else if (_languageVariant == LanguageVariant.JSX && CharAt(1) == '/' && CharAt(2) != '*')
                    {
                        _pos += 2;
                        _token = Kind.LessThanSlashToken;
                    }
                    else
                    {
                        _pos++;
                        _token = Kind.LessThanToken;
                    }

                    break;
                case '=':
                    if (CharAt(1) == '=' && IsConflictMarkerTrivia(_text, _pos))
                    {
                        _pos = ScanConflictMarkerTrivia(_text, _pos, ErrorAt);
                        if (_skipTrivia)
                        {
                            continue;
                        }

                        _token = Kind.ConflictMarkerTrivia;
                        return _token;
                    }

                    if (CharAt(1) == '=')
                    {
                        if (CharAt(2) == '=')
                        {
                            _pos += 3;
                            _token = Kind.EqualsEqualsEqualsToken;
                        }
                        else
                        {
                            _pos += 2;
                            _token = Kind.EqualsEqualsToken;
                        }
                    }
                    else if (CharAt(1) == '>')
                    {
                        _pos += 2;
                        _token = Kind.EqualsGreaterThanToken;
                    }
                    else
                    {
                        _pos++;
                        _token = Kind.EqualsToken;
                    }

                    break;
                case '>':
                    if (CharAt(1) == '>' && IsConflictMarkerTrivia(_text, _pos))
                    {
                        _pos = ScanConflictMarkerTrivia(_text, _pos, ErrorAt);
                        if (_skipTrivia)
                        {
                            continue;
                        }

                        _token = Kind.ConflictMarkerTrivia;
                        return _token;
                    }

                    _pos++;
                    _token = Kind.GreaterThanToken;
                    break;
                case '?':
                    if (CharAt(1) == '.' && !IsDigit(CharAt(2)))
                    {
                        _pos += 2;
                        _token = Kind.QuestionDotToken;
                    }
                    else if (CharAt(1) == '?')
                    {
                        if (CharAt(2) == '=')
                        {
                            _pos += 3;
                            _token = Kind.QuestionQuestionEqualsToken;
                        }
                        else
                        {
                            _pos += 2;
                            _token = Kind.QuestionQuestionToken;
                        }
                    }
                    else
                    {
                        _pos++;
                        _token = Kind.QuestionToken;
                    }

                    break;
                case '[':
                    _pos++;
                    _token = Kind.OpenBracketToken;
                    break;
                case ']':
                    _pos++;
                    _token = Kind.CloseBracketToken;
                    break;
                case '^':
                    if (CharAt(1) == '=')
                    {
                        _pos += 2;
                        _token = Kind.CaretEqualsToken;
                    }
                    else
                    {
                        _pos++;
                        _token = Kind.CaretToken;
                    }

                    break;
                case '{':
                    _pos++;
                    _token = Kind.OpenBraceToken;
                    break;
                case '|':
                    if (CharAt(1) == '|' && IsConflictMarkerTrivia(_text, _pos))
                    {
                        _pos = ScanConflictMarkerTrivia(_text, _pos, ErrorAt);
                        if (_skipTrivia)
                        {
                            continue;
                        }

                        _token = Kind.ConflictMarkerTrivia;
                        return _token;
                    }

                    if (CharAt(1) == '|')
                    {
                        if (CharAt(2) == '=')
                        {
                            _pos += 3;
                            _token = Kind.BarBarEqualsToken;
                        }
                        else
                        {
                            _pos += 2;
                            _token = Kind.BarBarToken;
                        }
                    }
                    else if (CharAt(1) == '=')
                    {
                        _pos += 2;
                        _token = Kind.BarEqualsToken;
                    }
                    else
                    {
                        _pos++;
                        _token = Kind.BarToken;
                    }

                    break;
                case '}':
                    _pos++;
                    _token = Kind.CloseBraceToken;
                    break;
                case '~':
                    _pos++;
                    _token = Kind.TildeToken;
                    break;
                case '@':
                    _pos++;
                    _token = Kind.AtToken;
                    break;
                case '\\':
                    if (ScanIdentifier(0, IdentifierVariant.Standard))
                    {
                        _token = GetIdentifierToken(_tokenValue);
                    }
                    else
                    {
                        ScanInvalidCharacter();
                    }

                    break;
                case '#':
                    if (CharAt(1) == '!')
                    {
                        if (_pos == 0)
                        {
                            _pos += 2;
                            while (true)
                            {
                                var (ch2, size) = CharAndSize();
                                if (size <= 0 || IsLineBreak(ch2))
                                {
                                    break;
                                }

                                _pos += size;
                            }

                            continue;
                        }

                        ErrorAt(X_can_only_be_used_at_the_start_of_a_file, _pos, 2);
                        _pos += 2;
                        _token = Kind.Unknown;
                        break;
                    }

                    if (!ScanIdentifier(1, IdentifierVariant.Standard))
                    {
                        ErrorAt(Invalid_character, _pos - 1, 1);
                        _tokenValue = "#";
                    }

                    _token = Kind.PrivateIdentifier;
                    break;
                default:
                    if (ch < 0)
                    {
                        _token = Kind.EndOfFile;
                        break;
                    }

                    if (ScanIdentifier(0, IdentifierVariant.Standard))
                    {
                        _token = GetIdentifierToken(_tokenValue);
                        break;
                    }

                    var (ch3, size3) = CharAndSize();
                    if (ch3 == 0xFFFD)
                    {
                        // Go 在这里判定"文件看起来是二进制"：DecodeRuneInString 遇到非法 UTF-8 返回 RuneError。
                        // C# 的 string 已经是解码结果，不存在非法 UTF-8 字节，所以这条分支只对源文本里**字面量**
                        // U+FFFD 生效（文件头第 11 条）。
                        ErrorAt(File_appears_to_be_binary, 0, 0);
                        // Go: `s.pos = len(s.text)`（scanner.go:934）。C# 的 `_text` 始终是全文，而
                        // `len(s.text)` 在"调用方收窄了扫描范围"时等于收窄后的边界（正则子解析的 _end、
                        // JSDoc 子解析的 _end，见 SetText(string, int)）⇒ 用 _end，两种收窄下都不会越界。
                        _pos = _end;
                        _token = Kind.NonTextFileMarkerTrivia;
                        break;
                    }

                    if (IsWhiteSpaceSingleLine(ch3))
                    {
                        _pos += size3;

                        // If we get here and it's not 0x0085 (nextLine), then we're handling non-ASCII whitespace.
                        // Handle skipTrivia like we do in the space case above.
                        if (ch3 == 0x0085 || _skipTrivia)
                        {
                            continue;
                        }

                        while (true)
                        {
                            (ch3, size3) = CharAndSize();
                            if (!IsWhiteSpaceSingleLine(ch3))
                            {
                                break;
                            }

                            _pos += size3;
                        }

                        _token = Kind.WhitespaceTrivia;
                        return _token;
                    }

                    if (IsLineBreak(ch3))
                    {
                        _tokenFlags |= TokenFlags.PrecedingLineBreak;
                        _pos += size3;
                        continue;
                    }

                    ScanInvalidCharacter();
                    break;
            }

            return _token;
        }
    }

    private void ProcessCommentDirective(int start, int end, bool multiline)
    {
        // Skip starting slashes and whitespace
        var pos = start;
        if (multiline)
        {
            // Skip whitespace
            while (pos < end && (_text[pos] == ' ' || _text[pos] == '\t'))
            {
                pos++;
            }

            // Skip combinations of / and *
            while (pos < end && (_text[pos] == '/' || _text[pos] == '*'))
            {
                pos++;
            }
        }
        else
        {
            // Skip opening //
            pos += 2;

            // Skip another / if present
            while (pos < end && _text[pos] == '/')
            {
                pos++;
            }
        }

        // Skip whitespace
        while (pos < end && (_text[pos] == ' ' || _text[pos] == '\t'))
        {
            pos++;
        }

        // Directive must start with '@'
        if (!(pos < end && _text[pos] == '@'))
        {
            return;
        }

        pos++;
        CommentDirectiveKind kind;
        if (_text.AsSpan(pos).StartsWith("ts-expect-error", StringComparison.Ordinal))
        {
            kind = CommentDirectiveKind.ExpectError;
        }
        else if (_text.AsSpan(pos).StartsWith("ts-ignore", StringComparison.Ordinal))
        {
            kind = CommentDirectiveKind.Ignore;
        }
        else
        {
            return;
        }

        _commentDirectives.Add(new CommentDirective { Loc = new TextRange(start, end), Kind = kind });
    }

    public Kind ReScanLessThanToken()
    {
        if (_token == Kind.LessThanLessThanToken)
        {
            _pos = _tokenStart + 1;
            _token = Kind.LessThanToken;
        }

        return _token;
    }

    public Kind ReScanGreaterThanToken()
    {
        if (_token == Kind.GreaterThanToken)
        {
            ReScanGreaterThanTokenInner();
        }

        return _token;
    }

    private void ReScanGreaterThanTokenInner()
    {
        _pos = _tokenStart + 1;
        if (Char() == '>')
        {
            if (CharAt(1) == '>')
            {
                if (CharAt(2) == '=')
                {
                    _pos += 3;
                    _token = Kind.GreaterThanGreaterThanGreaterThanEqualsToken;
                }
                else
                {
                    _pos += 2;
                    _token = Kind.GreaterThanGreaterThanGreaterThanToken;
                }
            }
            else if (CharAt(1) == '=')
            {
                _pos += 2;
                _token = Kind.GreaterThanGreaterThanEqualsToken;
            }
            else
            {
                _pos++;
                _token = Kind.GreaterThanGreaterThanToken;
            }
        }
        else if (Char() == '=')
        {
            _pos++;
            _token = Kind.GreaterThanEqualsToken;
        }
    }

    public Kind ReScanTemplateToken(bool isTaggedTemplate)
    {
        _pos = _tokenStart;
        _token = ScanTemplateAndSetTokenValue(!isTaggedTemplate);
        return _token;
    }

    public Kind ReScanAsteriskEqualsToken()
    {
        if (_token != Kind.AsteriskEqualsToken)
        {
            Debug.Fail("'ReScanAsteriskEqualsToken' should only be called on a '*='");
        }

        _pos = _tokenStart + 1;
        _token = Kind.EqualsToken;
        return _token;
    }

    // Go: func (s *Scanner) ReScanSlashToken(reportErrors ...bool) ast.Kind
    // C# 用可选参数表达 Go 的变长参数（文件头第 8 条）。
    public Kind ReScanSlashToken(bool shouldReportErrors = false)
    {
        if (_token == Kind.SlashToken || _token == Kind.SlashEqualsToken)
        {
            // Quickly get to the end of regex such that we know the flags
            var startOfRegExpBody = _tokenStart + 1;
            var p = startOfRegExpBody;
            var inEscape = false;
            var namedCaptureGroups = false;

            // Although nested character classes are allowed in Unicode Sets mode,
            // an unescaped slash is nevertheless invalid even in a character class in any Unicode mode.
            // This is indicated by Section 12.9.5 Regular Expression Literals of the specification,
            // where nested character classes are not considered at all. (A `[` RegularExpressionClassChar
            // does nothing in a RegularExpressionClass, and a `]` always closes the class.)
            // Additionally, parsing nested character classes will misinterpret regexes like `/[[]/`
            // as unterminated, consuming characters beyond the slash. (This even applies to `/[[]/v`,
            // which should be parsed as a well-terminated regex with an incomplete character class.)
            // Thus we must not handle nested character classes in the first pass.
            var inCharacterClass = false;
            while (true)
            {
                // If we reach the end of a file, or hit a newline, then this is an unterminated
                // regex. Report error and return what we have so far.
                if (p >= _end)
                {
                    _tokenFlags |= TokenFlags.Unterminated;
                    break;
                }

                var ch = (int)_text[p];
                // Go 的这一遍是**逐字节**扫的（`ch := rune(s.text[p])` 取的是一个字节），所以
                // stringutil.IsLineBreak 只能认出 0x0A/0x0D：U+2028/U+2029 的 UTF-8 首字节（0xE2）不是换行，
                // Go 会把它们当成正则体的一部分继续扫。真 tsc 对 `/a<U+2028>b/` 也不报错（oracle 用例
                // regex_ls_in_body），所以这里**不能**用 rune 版的 IsLineBreak（否则会多报 1161）。
                if (ch == '\n' || ch == '\r')
                {
                    _tokenFlags |= TokenFlags.Unterminated;
                    break;
                }
                else if (inEscape)
                {
                    // Parsing an escape character;
                    // reset the flag and just advance to the next char.
                    inEscape = false;
                }
                else if (ch == '/' && !inCharacterClass)
                {
                    // A slash within a character class is permissible,
                    // but in general it signals the end of the regexp literal.
                    break;
                }
                else if (ch == '[')
                {
                    inCharacterClass = true;
                }
                else if (ch == '\\')
                {
                    inEscape = true;
                }
                else if (ch == ']')
                {
                    inCharacterClass = false;
                }
                else if (!inCharacterClass && ch == '(' &&
                    p + 1 < _end && _text[p + 1] == '?' &&
                    p + 2 < _end && _text[p + 2] == '<' &&
                    (p + 3 >= _end || (_text[p + 3] != '=' && _text[p + 3] != '!')))
                {
                    namedCaptureGroups = true;
                }

                p++;
            }

            var endOfRegExpBody = p;
            if ((_tokenFlags & TokenFlags.Unterminated) != 0)
            {
                // Search for the nearest unbalanced bracket for better recovery. Since the expression is
                // invalid anyways, we take nested square brackets into consideration for the best guess.
                p = startOfRegExpBody;
                inEscape = false;
                var characterClassDepth = 0;
                var inDecimalQuantifier = false;
                var groupDepth = 0;
                while (p < endOfRegExpBody)
                {
                    var ch = (int)_text[p];
                    if (inEscape)
                    {
                        inEscape = false;
                    }
                    else if (ch == '\\')
                    {
                        inEscape = true;
                    }
                    else if (ch == '[')
                    {
                        characterClassDepth++;
                    }
                    else if (ch == ']' && characterClassDepth != 0)
                    {
                        characterClassDepth--;
                    }
                    else if (characterClassDepth == 0)
                    {
                        if (ch == '{')
                        {
                            inDecimalQuantifier = true;
                        }
                        else if (ch == '}' && inDecimalQuantifier)
                        {
                            inDecimalQuantifier = false;
                        }
                        else if (!inDecimalQuantifier)
                        {
                            if (ch == '(')
                            {
                                groupDepth++;
                            }
                            else if (ch == ')' && groupDepth != 0)
                            {
                                groupDepth--;
                            }
                            else if (ch == ')' || ch == ']' || ch == '}')
                            {
                                // We encountered an unbalanced bracket outside a character class. Treat this position as the end of regex.
                                break;
                            }
                        }
                    }

                    p++;
                }

                // Whitespaces and semicolons at the end are not likely to be part of the regex
                while (p > startOfRegExpBody)
                {
                    var (ch, size) = DecodeLastRuneAt(_text, p);
                    if (IsWhiteSpaceLike(ch) || ch == ';')
                    {
                        p -= size;
                    }
                    else
                    {
                        break;
                    }
                }

                ErrorAt(Unterminated_regular_expression_literal, _tokenStart, p - _tokenStart);
            }
            else
            {
                // Consume the slash character
                p++;
                var regExpFlags = RegularExpressionFlags.None;
                while (p < _end)
                {
                    var ch = DecodeRuneAt(_text, p, out var size);
                    if (ch == 0xFFFD || !IsIdentifierPart(ch))
                    {
                        break;
                    }

                    if (shouldReportErrors)
                    {
                        if (!Scanner._charCodeToRegExpFlag.TryGetValue(ch, out var flag))
                        {
                            ErrorAt(Unknown_regular_expression_flag, p, size);
                        }
                        else if ((regExpFlags & flag) != 0)
                        {
                            ErrorAt(Duplicate_regular_expression_flag, p, size);
                        }
                        else if (((regExpFlags | flag) & RegularExpressionFlags.AnyUnicodeMode) == RegularExpressionFlags.AnyUnicodeMode)
                        {
                            ErrorAt(The_Unicode_u_flag_and_the_Unicode_Sets_v_flag_cannot_be_set_simultaneously, p, size);
                        }
                        else
                        {
                            regExpFlags |= flag;
                            CheckRegularExpressionFlagAvailability(flag, p, size);
                        }
                    }

                    p += size;
                }

                if (shouldReportErrors)
                {
                    _pos = startOfRegExpBody;
                    var saveEnd = _end;
                    var saveTokenPos = _tokenStart;
                    var saveTokenFlags = _tokenFlags;
                    _end = endOfRegExpBody;
                    var parser = new RegExpParser(
                        this,
                        endOfRegExpBody,
                        regExpFlags,
                        (regExpFlags & RegularExpressionFlags.AnyUnicodeMode) != 0,
                        (regExpFlags & RegularExpressionFlags.UnicodeSets) != 0,
                        annexB: true,
                        namedCaptureGroups);
                    parser.Run();
                    _end = saveEnd;
                    _pos = p;
                    _tokenStart = saveTokenPos;
                    _tokenFlags = saveTokenFlags;
                }
                else
                {
                    _pos = p;
                }
            }

            _pos = p;
            _tokenValue = _text[_tokenStart.._pos];
            _token = Kind.RegularExpressionLiteral;
        }

        return _token;
    }
}
