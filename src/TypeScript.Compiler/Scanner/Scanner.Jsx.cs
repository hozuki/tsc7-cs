// ported from: internal/scanner/scanner.go:1221-1355（ReScanJsxToken / ScanJsxToken / ScanJsxTokenEx /
//               ScanJsxIdentifier / ScanJsxAttributeValue / ReScanJsxAttributeValue）
//
// 与 Go 的偏离：无（逐行对应）。注释风格照 Go 原文保留，便于并排对照。
using TypeScript.Ast;
using static TypeScript.Diagnostics.Diagnostics;
using static TypeScript.StringUtil.StringUtil;

namespace TypeScript.Scanner;

public sealed partial class Scanner
{
    public Kind ReScanJsxToken(bool allowMultilineJsxText)
    {
        _pos = _fullStartPos;
        _tokenStart = _fullStartPos;
        _token = ScanJsxTokenEx(allowMultilineJsxText);
        return _token;
    }

    public Kind ScanJsxToken()
    {
        return ScanJsxTokenEx(true /*allowMultilineJsxText*/);
    }

    public Kind ScanJsxTokenEx(bool allowMultilineJsxText)
    {
        _fullStartPos = _pos;
        _tokenStart = _pos;
        var ch = Char();
        if (ch < 0)
        {
            _token = Kind.EndOfFile;
        }
        else if (ch == '<')
        {
            if (CharAt(1) == '/')
            {
                _pos += 2;
                _token = Kind.LessThanSlashToken;
            }
            else
            {
                _pos++;
                _token = Kind.LessThanToken;
            }
        }
        else if (ch == '{')
        {
            _pos++;
            _token = Kind.OpenBraceToken;
        }
        else
        {
            // First non-whitespace character on this line.
            var firstNonWhitespace = 0;
            // These initial values are special because the first line is:
            // firstNonWhitespace = 0 to indicate that we want leading whitespace
            while (true)
            {
                var (ch2, size) = CharAndSize();
                if (size == 0 || ch2 == '{')
                {
                    break;
                }

                if (ch2 == '<')
                {
                    if (IsConflictMarkerTrivia(_text, _pos))
                    {
                        _pos = ScanConflictMarkerTrivia(_text, _pos, ErrorAt);
                        _token = Kind.ConflictMarkerTrivia;
                        return _token;
                    }

                    break;
                }

                if (ch2 == '>')
                {
                    ErrorAt(Unexpected_token_Did_you_mean_or_gt, _pos, 1);
                }
                else if (ch2 == '}')
                {
                    ErrorAt(Unexpected_token_Did_you_mean_or_rbrace, _pos, 1);
                }

                // FirstNonWhitespace is 0, then we only see whitespaces so far. If we see a linebreak, we want to ignore that whitespaces.
                // i.e (- : whitespace)
                //      <div>----
                //      </div> becomes <div></div>
                //
                //      <div>----</div> becomes <div>----</div>
                if (IsLineBreak(ch2) && firstNonWhitespace == 0)
                {
                    firstNonWhitespace = -1;
                }
                else if (!allowMultilineJsxText && IsLineBreak(ch2) && firstNonWhitespace > 0)
                {
                    // Stop JsxText on each line during formatting. This allows the formatter to
                    // indent each line correctly.
                    break;
                }
                else if (!IsWhiteSpaceLike(ch2))
                {
                    firstNonWhitespace = _pos;
                }

                _pos += size;
            }

            _tokenValue = _text[_fullStartPos.._pos];
            _token = Kind.JsxText;
            if (firstNonWhitespace == -1)
            {
                _token = Kind.JsxTextAllWhiteSpaces;
            }
        }

        return _token;
    }

    // Scans a JSX identifier; these differ from normal identifiers in that they allow dashes
    public Kind ScanJsxIdentifier()
    {
        if (TokenIsIdentifierOrKeyword(_token))
        {
            // An identifier or keyword has already been parsed - check for a `-` or a single instance of `:` and then append it and
            // everything after it to the token
            // Do note that this means that `scanJsxIdentifier` effectively _mutates_ the visible token without advancing to a new token
            // Any caller should be expecting this behavior and should only read the pos or token value after calling it.
            // Here scanIdentifierParts is reused to ensure Unicode escapes are handled.
            _tokenValue += ScanIdentifierParts(IdentifierVariant.JSX);
            _token = GetIdentifierToken(_tokenValue);
        }

        return _token;
    }

    public Kind ScanJsxAttributeValue()
    {
        _fullStartPos = _pos;
        // Skip whitespace between '=' and the value so tokenStart lands on the
        // opening quote, not on trivia.
        while (true)
        {
            var (ch, size) = CharAndSize();
            if (size <= 0 || !IsWhiteSpaceLike(ch))
            {
                break;
            }

            _pos += size;
        }

        _tokenStart = _pos;
        switch (Char())
        {
            case '"':
            case '\'':
                _tokenValue = ScanString(true /*jsxAttributeString*/);
                _token = Kind.StringLiteral;
                return _token;
            default:
                // If this scans anything other than `{`, it's a parse error.
                return Scan();
        }
    }

    public Kind ReScanJsxAttributeValue()
    {
        _pos = _fullStartPos;
        _tokenStart = _fullStartPos;
        return ScanJsxAttributeValue();
    }
}
