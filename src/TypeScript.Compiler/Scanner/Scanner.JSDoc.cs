// ported from: internal/scanner/scanner.go:316-395（SetSkipJSDocLeadingAsterisks / scanJSDocCommentForTags / hasJSDocTag）
//             + internal/scanner/scanner.go:1356-1483（ScanJSDocCommentTextToken / CanFollowJSDocAt / ScanJSDocToken）
//
// 与 Go 的偏离（逐条）：
// - `hasJSDocTag(text, tags ...string)` 是 Go 的包级未导出函数 → C# `internal static bool HasJSDocTag(string text, params string[] tags)`。
// - `utf8.DecodeLastRuneInString` / `utf8.DecodeRuneInString` → `DecodeLastRuneAt` / `DecodeRuneAt`（UTF-16 代码单元下标，
//   见 Scanner.Escapes.cs 文件头）。
using TypeScript.Ast;
using TypeScript.Core;
using static TypeScript.StringUtil.StringUtil;

namespace TypeScript.Scanner;

public sealed partial class Scanner
{
    public void SetSkipJSDocLeadingAsterisks(bool skip)
    {
        if (skip)
        {
            _skipJSDocLeadingAsterisks += 1;
        }
        else
        {
            _skipJSDocLeadingAsterisks += -1;
        }
    }

    // scanJSDocCommentForTags scans a JSDoc comment for @deprecated, @see, and @link tags,
    // setting the appropriate token flags. Called during scanning when a JSDoc comment is detected.
    //
    // 与 Go 的对应：Go 的 `scanJSDocCommentForTags(commentText string)` 收到的是 `s.text[s.tokenStart:s.pos]`
    // **切片**（scanner.go:679），循环里 `commentText = commentText[i+1:]` 也是切片（scanner.go:364）——全是零拷贝。
    // C# 的 `string` 不能做视图，若照抄 `string` 参数则每次调用复制整段注释、每个 '@' 再复制一次后缀。
    // 所以这里（与 Scanner.SetText(string,int)/TextFrom 同一套思路）把参数收成 `ReadOnlySpan<char>`：
    // 切片就是 Go 的切片，`_text` 始终是全文、越界由调用方切出的长度挡住。
    private void ScanJSDocCommentForTags(ReadOnlySpan<char> commentText)
    {
        while (true)
        {
            var i = commentText.IndexOf('@');
            if (i < 0)
            {
                return;
            }

            commentText = commentText[(i + 1)..];
            if ((_tokenFlags & TokenFlags.PrecedingJSDocWithDeprecated) == 0 && HasJSDocTag(commentText, "deprecated"))
            {
                _tokenFlags |= TokenFlags.PrecedingJSDocWithDeprecated;
            }

            if ((_tokenFlags & TokenFlags.PrecedingJSDocWithSeeOrLink) == 0 && HasJSDocTag(commentText, "see", "link", "linkcode", "linkplain"))
            {
                _tokenFlags |= TokenFlags.PrecedingJSDocWithSeeOrLink;
            }

            if ((_tokenFlags & (TokenFlags.PrecedingJSDocWithDeprecated | TokenFlags.PrecedingJSDocWithSeeOrLink)) ==
                (TokenFlags.PrecedingJSDocWithDeprecated | TokenFlags.PrecedingJSDocWithSeeOrLink))
            {
                return;
            }
        }
    }

    // hasJSDocTag reports whether text starts with one of the given tag names followed
    // by a valid JSDoc tag terminator (whitespace, '}', '*', or end-of-string).
    //
    // 参数 `text` 是 span（见 ScanJSDocCommentForTags 的说明）；`tags` 是 `params ReadOnlySpan&lt;string&gt;`：
    // Go 的变参 `tags ...string` 每次调用分配一个切片，而它在 ScanJSDocCommentForTags 的循环里**每个 '@' 都要
    // 调两次**（1 元素 + 4 元素各一次）⇒ 这是扫描相位里纯为变参服务的分配。C# 13 的 `params ReadOnlySpan&lt;string&gt;`
    // 由编译器在调用点 `stackalloc`（先例 `Ast/Utilities.cs` 的 `NodeKindIs`），这里是有实测依据的刻意偏离。
    internal static bool HasJSDocTag(ReadOnlySpan<char> text, params ReadOnlySpan<string> tags)
    {
        foreach (var tag in tags)
        {
            if (!text.StartsWith(tag, StringComparison.Ordinal))
            {
                continue;
            }

            if (text.Length == tag.Length)
            {
                return true;
            }

            var ch = text[tag.Length];
            if (ch == ' ' || ch == '\t' || ch == '\n' || ch == '\r' || ch == '}' || ch == '*')
            {
                return true;
            }
        }

        return false;
    }

    /** In addition to the usual JSDoc Kinds, can also return Kind.JSDocCommentTextToken */
    public Kind ScanJSDocCommentTextToken(bool inBackticks)
    {
        _fullStartPos = _pos;
        _tokenFlags = TokenFlags.None;
        // Go: `s.pos >= len(s.text)`（scanner.go:1359）——Go 里 `len(s.text) == s.end`（SetText），C# 的
        // `_text` 可以是全文而 `_end` 被调用方收窄（见 Scanner.SetText(string, int)）⇒ 边界一律用 `_end`。
        if (_pos >= _end)
        {
            _token = Kind.EndOfFile;
            return _token;
        }

        _tokenStart = _pos;
        var (ch, size) = CharAndSize();
        while (_pos < _end && !IsLineBreak(ch) && ch != '`')
        {
            if (!inBackticks)
            {
                if (ch == '{')
                {
                    break;
                }
                else if (ch == '@')
                {
                    // @ doesn't start a new tag inside ``, and elsewhere, only after whitespace and before identifier
                    var (previous, _) = DecodeLastRuneAt(_text, _pos);
                    if (IsWhiteSpaceSingleLine(previous))
                    {
                        var next = DecodeRuneAt(_text, _pos + size, out _);
                        if (IsIdentifierStart(next))
                        {
                            break;
                        }
                    }
                }
            }

            _pos += size;
            (ch, size) = CharAndSize();
        }

        if (_pos == _tokenStart)
        {
            return ScanJSDocToken();
        }

        _tokenValue = _text[_tokenStart.._pos];
        _token = Kind.JSDocCommentTextToken;
        return _token;
    }

    // Peek at the character at the current scanner position (expected to be right after '@')
    // and return true if a JSDoc tag can follow. Identifier starts indicate a tag name.
    // Whitespace, newlines, and EOF are also accepted to support incomplete tags for code completion.
    public bool CanFollowJSDocAt()
    {
        // Go: `s.pos >= len(s.text)`（scanner.go:1393），见 ScanJSDocCommentTextToken 的说明。
        if (_pos >= _end)
        {
            return true;
        }

        var ch = DecodeRuneAt(_text, _pos, out _);
        return IsIdentifierStart(ch) || IsWhiteSpaceSingleLine(ch) || IsLineBreak(ch);
    }

    public Kind ScanJSDocToken()
    {
        _fullStartPos = _pos;
        _tokenFlags = TokenFlags.None;
        // Go: `s.pos >= len(s.text)`（scanner.go:1403），见 ScanJSDocCommentTextToken 的说明。
        if (_pos >= _end)
        {
            _token = Kind.EndOfFile;
            return _token;
        }

        _tokenStart = _pos;
        var (ch, size) = CharAndSize();
        _pos += size;
        switch (ch)
        {
            case '\t':
            case '\v':
            case '\f':
            case ' ':
                while (true)
                {
                    var (ch2, size2) = CharAndSize();
                    if (size2 <= 0 || !IsWhiteSpaceSingleLine(ch2))
                    {
                        break;
                    }

                    _pos += size2;
                }

                _token = Kind.WhitespaceTrivia;
                return _token;
            case '@':
                _token = Kind.AtToken;
                return _token;
            case '\r':
                if (Char() == '\n')
                {
                    _pos++;
                }

                goto case '\n';
            case '\n':
                _tokenFlags |= TokenFlags.PrecedingLineBreak;
                _token = Kind.NewLineTrivia;
                return _token;
            case '*':
                _token = Kind.AsteriskToken;
                return _token;
            case '{':
                _token = Kind.OpenBraceToken;
                return _token;
            case '}':
                _token = Kind.CloseBraceToken;
                return _token;
            case '[':
                _token = Kind.OpenBracketToken;
                return _token;
            case ']':
                _token = Kind.CloseBracketToken;
                return _token;
            case '(':
                _token = Kind.OpenParenToken;
                return _token;
            case ')':
                _token = Kind.CloseParenToken;
                return _token;
            case '<':
                _token = Kind.LessThanToken;
                return _token;
            case '>':
                _token = Kind.GreaterThanToken;
                return _token;
            case '=':
                _token = Kind.EqualsToken;
                return _token;
            case ',':
                _token = Kind.CommaToken;
                return _token;
            case '.':
                _token = Kind.DotToken;
                return _token;
            case '`':
                _token = Kind.BacktickToken;
                return _token;
            case '#':
                _token = Kind.HashToken;
                return _token;
        }

        _pos = _tokenStart;
        if (ScanIdentifier(0, IdentifierVariant.JSX))
        {
            _token = GetIdentifierToken(_tokenValue);
            return _token;
        }

        _pos = _tokenStart + size;
        _token = Kind.Unknown;
        return _token;
    }
}
