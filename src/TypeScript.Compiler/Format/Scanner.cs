// ported from: internal/format/scanner.go
//
// 与 Go 的差异（C# 语言特性；依据 docs/architecture.md §2/§3.2/§3.3）：
// - `TextRangeWithKind`（Go 导出类型 + 导出字段）→ C# readonly struct + 相等运算符（Go 用 `==` 比较整个结构体，
//   span.go 里 `w.previousRange != NewTextRangeWithKind(0, 0, 0)` 逐字对应）；`NewTextRangeWithKind` 是包级函数，
//   落在静态类 `Format` 里。
// - 未导出类型 `tokenInfo`/`formattingScanner`/`scanAction` → `TokenInfo`/`FormattingScanner`/`ScanAction`；
//   未导出字段 → `_camelCase`（internal）。`tokenInfo` 是 struct（Go 的值语义：`fixTokenKind` 返回副本）。
// - `slices.Clone(x)` → `(T[])x.Clone()`。
// - Go 的 `panic`/`debug.Assert` → `Debug.Fail`/`Debug.Assert`（§3.2）。
// - `scanner` 包（Go）→ `TypeScript.Scanner.Scanner`；因为在 `namespace TypeScript.Format` 里裸写 `Scanner` 会
//   解析到命名空间 `TypeScript.Scanner`，本文件用 `using Scan = TypeScript.Scanner.Scanner;`。
using TypeScript.Ast;
using TypeScript.Core;
using static TypeScript.Ast.Ast;
using static TypeScript.Core.Core;
using Scan = TypeScript.Scanner.Scanner;

namespace TypeScript.Format;

/// <summary>Go 的 <c>type TextRangeWithKind struct { Loc core.TextRange; Kind ast.Kind }</c>。</summary>
public readonly struct TextRangeWithKind : IEquatable<TextRangeWithKind>
{
    public readonly TextRange Loc;
    public readonly Kind Kind;

    public TextRangeWithKind(TextRange loc, Kind kind)
    {
        Loc = loc;
        Kind = kind;
    }

    public bool Equals(TextRangeWithKind other) => Loc == other.Loc && Kind == other.Kind;

    public override bool Equals(object? obj) => obj is TextRangeWithKind other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(Loc, Kind);

    public static bool operator ==(TextRangeWithKind left, TextRangeWithKind right) => left.Equals(right);

    public static bool operator !=(TextRangeWithKind left, TextRangeWithKind right) => !left.Equals(right);
}

/// <summary>Go 的 <c>type tokenInfo struct</c>（未导出类型；值语义 struct）。</summary>
internal struct TokenInfo
{
    internal TextRangeWithKind[] _leadingTrivia;
    internal TextRangeWithKind _token;
    internal TextRangeWithKind[] _trailingTrivia;
}

internal enum ScanAction
{
    Scan = 0,
    RescanGreaterThanToken = 1,
    RescanSlashToken = 2,
    RescanTemplateToken = 3,
    RescanJsxIdentifier = 4,
    RescanJsxText = 5,
    RescanJsxAttributeValue = 6,
}

internal sealed class FormattingScanner
{
    internal Scan _s;
    internal int _startPos;
    internal int _endPos;
    internal int _savedPos;
    internal bool _hasLastTokenInfo;
    internal TokenInfo _lastTokenInfo;
    internal ScanAction _lastScanAction;
    internal TextRangeWithKind[] _leadingTrivia = [];
    internal TextRangeWithKind[] _trailingTrivia = [];
    internal bool _wasNewLine;

    internal FormattingScanner(Scan s, int startPos, int endPos)
    {
        _s = s;
        _startPos = startPos;
        _endPos = endPos;
        _wasNewLine = true;
    }

    internal void Advance()
    {
        _hasLastTokenInfo = false;
        var isStarted = _s.TokenFullStart() != _startPos;

        if (isStarted)
        {
            _wasNewLine = _trailingTrivia.Length > 0 && LastOrNil(_trailingTrivia).Kind == Kind.NewLineTrivia;
        }
        else
        {
            _s.Scan();
        }

        _leadingTrivia = [];
        _trailingTrivia = [];

        var pos = _s.TokenFullStart();

        // Read leading trivia and token
        while (pos < _endPos)
        {
            var t = _s.Token();
            if (!IsTrivia(t))
            {
                break;
            }

            // consume leading trivia
            _s.Scan();
            var item = Format.NewTextRangeWithKind(pos, _s.TokenFullStart(), t);

            pos = _s.TokenFullStart();

            _leadingTrivia = [.. _leadingTrivia, item];
        }

        _savedPos = _s.TokenFullStart();
    }

    internal bool ShouldRescanJsxText(Node node)
    {
        if (IsJsxText(node))
        {
            return true;
        }

        if (!IsJsxElement(node) || !_hasLastTokenInfo)
        {
            return false;
        }

        return _lastTokenInfo._token.Kind == Kind.JsxText;
    }

    internal TokenInfo ReadTokenInfo(Node n)
    {
        Debug.Assert(IsOnToken());

        // normally scanner returns the smallest available token
        // check the kind of context node to determine if scanner should have more greedy behavior and consume more text.

        ScanAction expectedScanAction;
        if (Format.ShouldRescanGreaterThanToken(n))
        {
            expectedScanAction = ScanAction.RescanGreaterThanToken;
        }
        else if (Format.ShouldRescanSlashToken(n))
        {
            expectedScanAction = ScanAction.RescanSlashToken;
        }
        else if (Format.ShouldRescanTemplateToken(n))
        {
            expectedScanAction = ScanAction.RescanTemplateToken;
        }
        else if (Format.ShouldRescanJsxIdentifier(n))
        {
            expectedScanAction = ScanAction.RescanJsxIdentifier;
        }
        else if (ShouldRescanJsxText(n))
        {
            expectedScanAction = ScanAction.RescanJsxText;
        }
        else if (Format.ShouldRescanJsxAttributeValue(n))
        {
            expectedScanAction = ScanAction.RescanJsxAttributeValue;
        }
        else
        {
            expectedScanAction = ScanAction.Scan;
        }

        if (_hasLastTokenInfo && expectedScanAction == _lastScanAction)
        {
            // readTokenInfo was called before with the same expected scan action.
            // No need to re-scan text, return existing 'lastTokenInfo'
            // it is ok to call fixTokenKind here since it does not affect
            // what portion of text is consumed. In contrast rescanning can change it,
            // i.e. for '>=' when originally scanner eats just one character
            // and rescanning forces it to consume more.
            _lastTokenInfo = Format.FixTokenKind(_lastTokenInfo, n);
            return _lastTokenInfo;
        }

        if (_s.TokenFullStart() != _savedPos)
        {
            // readTokenInfo was called before but scan action differs - rescan text
            _s.ResetTokenState(_savedPos);
            _s.Scan();
        }

        var currentToken = GetNextToken(n, expectedScanAction);

        var token = new TextRangeWithKind(
            NewTextRange(_s.TokenFullStart(), _s.TokenEnd()),
            currentToken);

        // consume trailing trivia
        _trailingTrivia = [];
        while (_s.TokenFullStart() < _endPos)
        {
            currentToken = _s.Scan();
            if (!IsTrivia(currentToken))
            {
                break;
            }

            var trivia = new TextRangeWithKind(
                NewTextRange(_s.TokenFullStart(), _s.TokenEnd()),
                currentToken);

            _trailingTrivia = [.. _trailingTrivia, trivia];

            if (currentToken == Kind.NewLineTrivia)
            {
                // move past new line
                _s.Scan();
                break;
            }
        }

        _hasLastTokenInfo = true;
        _lastTokenInfo = new TokenInfo
        {
            _leadingTrivia = (TextRangeWithKind[])_leadingTrivia.Clone(),
            _token = token,
            _trailingTrivia = (TextRangeWithKind[])_trailingTrivia.Clone(),
        };
        _lastTokenInfo = Format.FixTokenKind(_lastTokenInfo, n);

        return _lastTokenInfo;
    }

    private Kind GetNextToken(Node n, ScanAction expectedScanAction)
    {
        var token = _s.Token();
        _lastScanAction = ScanAction.Scan;
        switch (expectedScanAction)
        {
            case ScanAction.RescanGreaterThanToken:
                if (token == Kind.GreaterThanToken)
                {
                    _lastScanAction = ScanAction.RescanGreaterThanToken;
                    var newToken = _s.ReScanGreaterThanToken();
                    Debug.Assert(n.Kind == newToken);
                    return newToken;
                }

                break;
            case ScanAction.RescanSlashToken:
                if (Format.StartsWithSlashToken(token))
                {
                    _lastScanAction = ScanAction.RescanSlashToken;
                    var newToken = _s.ReScanSlashToken();
                    Debug.Assert(n.Kind == newToken);
                    return newToken;
                }

                break;
            case ScanAction.RescanTemplateToken:
                if (token == Kind.CloseBraceToken)
                {
                    _lastScanAction = ScanAction.RescanTemplateToken;
                    return _s.ReScanTemplateToken(/*isTaggedTemplate*/ false);
                }

                break;
            case ScanAction.RescanJsxIdentifier:
                _lastScanAction = ScanAction.RescanJsxIdentifier;
                return _s.ScanJsxIdentifier();
            case ScanAction.RescanJsxText:
                _lastScanAction = ScanAction.RescanJsxText;
                return _s.ReScanJsxToken(/*allowMultilineJsxText*/ false);
            case ScanAction.RescanJsxAttributeValue:
                _lastScanAction = ScanAction.RescanJsxAttributeValue;
                return _s.ReScanJsxAttributeValue();
            case ScanAction.Scan:
                // no rescan needed; the token was already produced by the normal scan
                break;
            default:
                Debug.AssertNever(expectedScanAction, "unhandled scan action kind");
                break;
        }

        return token;
    }

    internal TextRangeWithKind ReadEOFTokenRange()
    {
        Debug.Assert(IsOnEOF());
        return Format.NewTextRangeWithKind(
            _s.TokenFullStart(),
            _s.TokenEnd(),
            Kind.EndOfFile);
    }

    internal bool IsOnToken()
    {
        var current = _s.Token();
        if (_hasLastTokenInfo)
        {
            current = _lastTokenInfo._token.Kind;
        }

        return current != Kind.EndOfFile && !IsTrivia(current);
    }

    internal bool IsOnEOF()
    {
        var current = _s.Token();
        if (_hasLastTokenInfo)
        {
            current = _lastTokenInfo._token.Kind;
        }

        return current == Kind.EndOfFile;
    }

    internal void SkipToEndOf(TextRange r)
    {
        _s.ResetTokenState(r.End());
        _savedPos = _s.TokenFullStart();
        _lastScanAction = ScanAction.Scan;
        _hasLastTokenInfo = false;
        _wasNewLine = false;
        _leadingTrivia = [];
        _trailingTrivia = [];
    }

    internal void SkipToStartOf(TextRange r)
    {
        _s.ResetTokenState(r.Pos());
        _savedPos = _s.TokenFullStart();
        _lastScanAction = ScanAction.Scan;
        _hasLastTokenInfo = false;
        _wasNewLine = false;
        _leadingTrivia = [];
        _trailingTrivia = [];
    }

    internal TextRangeWithKind[] GetCurrentLeadingTrivia() => _leadingTrivia;

    internal bool LastTrailingTriviaWasNewLine() => _wasNewLine;

    internal int GetTokenFullStart()
    {
        if (_hasLastTokenInfo)
        {
            return _lastTokenInfo._token.Loc.Pos();
        }

        return _s.TokenFullStart();
    }

    // TODO: redundant?
    internal int GetStartPos() => GetTokenFullStart();
}

public static partial class Format
{
    internal static TextRangeWithKind NewTextRangeWithKind(int pos, int end, Kind kind)
    {
        return new TextRangeWithKind(NewTextRange(pos, end), kind);
    }

    internal static bool ShouldRescanGreaterThanToken(Node node)
    {
        switch (node.Kind)
        {
            case Kind.GreaterThanEqualsToken:
            case Kind.GreaterThanGreaterThanEqualsToken:
            case Kind.GreaterThanGreaterThanGreaterThanEqualsToken:
            case Kind.GreaterThanGreaterThanGreaterThanToken:
            case Kind.GreaterThanGreaterThanToken:
                return true;
        }

        return false;
    }

    internal static bool ShouldRescanJsxIdentifier(Node node)
    {
        if (node.Parent is not null)
        {
            switch (node.Parent.Kind)
            {
                case Kind.JsxAttribute:
                case Kind.JsxOpeningElement:
                case Kind.JsxClosingElement:
                case Kind.JsxSelfClosingElement:
                case Kind.JsxNamespacedName:
                    // May parse an identifier like `module-layout`; that will be scanned as a keyword at first, but we should parse the whole thing to get an identifier.
                    return IsKeywordKind(node.Kind) || node.Kind == Kind.Identifier;
                case Kind.PropertyAccessExpression:
                    // The leftmost name of a dotted JSX tag name (e.g. `a-b` in `<a-b.c>`) may contain hyphens, so rescan it as a JSX identifier.
                    return (IsKeywordKind(node.Kind) || node.Kind == Kind.Identifier) && IsLeftmostJsxTagName(node);
            }
        }

        return false;
    }

    internal static bool IsLeftmostJsxTagName(Node node)
    {
        return FindAncestorOrQuit(node, n =>
        {
            if (n.Parent is null)
            {
                return FindAncestorResult.Quit;
            }

            if (IsJsxTagName(n))
            {
                return FindAncestorResult.True;
            }

            if (IsPropertyAccessExpression(n.Parent) && n.Parent.Expression() == n)
            {
                return FindAncestorResult.False;
            }

            return FindAncestorResult.Quit;
        }) is not null;
    }

    internal static bool ShouldRescanSlashToken(Node container)
    {
        return container.Kind == Kind.RegularExpressionLiteral;
    }

    internal static bool ShouldRescanTemplateToken(Node container)
    {
        return container.Kind == Kind.TemplateMiddle ||
            container.Kind == Kind.TemplateTail;
    }

    internal static bool ShouldRescanJsxAttributeValue(Node node)
    {
        return node.Parent is not null && IsJsxAttribute(node.Parent) && node.Parent.Initializer() == node;
    }

    internal static bool StartsWithSlashToken(Kind t)
    {
        return t == Kind.SlashToken || t == Kind.SlashEqualsToken;
    }

    internal static TokenInfo FixTokenKind(TokenInfo tokenInfo, Node container)
    {
        if (IsTokenKind(container.Kind) && tokenInfo._token.Kind != container.Kind)
        {
            tokenInfo._token = new TextRangeWithKind(tokenInfo._token.Loc, container.Kind);
        }

        return tokenInfo;
    }

    /// <summary>Go 的 <c>func newFormattingScanner(text string, languageVariant core.LanguageVariant, startPos int, endPos int, worker *formatSpanWorker) []core.TextChange</c>。</summary>
    internal static TextChange[] NewFormattingScanner(string text, LanguageVariant languageVariant, int startPos, int endPos, FormatSpanWorker worker)
    {
        var scan = Scan.NewScanner();
        scan.SetSkipTrivia(false);
        scan.SetLanguageVariant(languageVariant);
        scan.SetText(text);
        scan.ResetTokenState(startPos);

        var fmtScn = new FormattingScanner(scan, startPos, endPos);

        var res = worker.Execute(fmtScn);

        fmtScn._hasLastTokenInfo = false;
        scan.Reset();

        return res;
    }
}
