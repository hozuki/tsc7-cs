// ported from: internal/format/context.go
//
// 与 Go 的差异（C# 语言特性）：
// - Go 未导出字段 → `_camelCase`（internal，同程序集可见；§2）；Go 导出字段 `SourceFile`/
//   `FormattingRequestKind`/`Options` 原名保留（public）。
// - 包级构造函数 `NewFormattingContext` → 静态类 `Format` 里的同名函数；构造器本身是 internal。
// - Go 未导出方法名字母大小写只差首字母，C# 里一律 PascalCase：`RangeIsOnOneLine`/`NodeIsOnOneLine`/
//   `BlockIsOnOneLine`（internal）。
// - `panic(...)` → `Debug.Fail(...)`（§3.2；文本逐字保留）。
using TypeScript.Ast;
using TypeScript.Core;
using TypeScript.LsUtil;
using static TypeScript.Ast.Ast;
using static TypeScript.AstNav.AstNav;
using static TypeScript.Core.Core;
using Scan = TypeScript.Scanner.Scanner;

namespace TypeScript.Format;

/// <summary>Go 的 <c>type FormattingContext struct</c>。</summary>
public sealed class FormattingContext
{
    internal TextRangeWithKind _currentTokenSpan;
    internal TextRangeWithKind _nextTokenSpan;
    internal Node? _contextNode;
    internal Node? _currentTokenParent;
    internal Node? _nextTokenParent;

    internal Tristate _contextNodeAllOnSameLine;
    internal Tristate _nextNodeAllOnSameLine;
    internal Tristate _tokensAreOnSameLine;
    internal Tristate _contextNodeBlockIsOnOneLine;
    internal Tristate _nextNodeBlockIsOnOneLine;

    public SourceFile SourceFile;
    public FormatRequestKind FormattingRequestKind;
    public FormatCodeSettings Options;

    internal FormattingContext(SourceFile file, FormatRequestKind kind, FormatCodeSettings options)
    {
        SourceFile = file;
        FormattingRequestKind = kind;
        Options = options;
    }

    public void UpdateContext(
        TextRangeWithKind cur,
        Node? curParent,
        TextRangeWithKind next,
        Node? nextParent,
        Node? commonParent)
    {
        if (curParent is null)
        {
            Debug.Fail("nil current range node parent in update context");
        }

        if (nextParent is null)
        {
            Debug.Fail("nil next range node parent in update context");
        }

        if (commonParent is null)
        {
            Debug.Fail("nil common parent node in update context");
        }

        _currentTokenSpan = cur;
        _currentTokenParent = curParent;
        _nextTokenSpan = next;
        _nextTokenParent = nextParent;
        _contextNode = commonParent;

        // drop cached results
        _contextNodeAllOnSameLine = Tristate.TSUnknown;
        _nextNodeAllOnSameLine = Tristate.TSUnknown;
        _tokensAreOnSameLine = Tristate.TSUnknown;
        _contextNodeBlockIsOnOneLine = Tristate.TSUnknown;
        _nextNodeBlockIsOnOneLine = Tristate.TSUnknown;
    }

    internal Tristate RangeIsOnOneLine(TextRange node)
    {
        if (Format.RangeIsOnOneLine(node, SourceFile))
        {
            return Tristate.TSTrue;
        }

        return Tristate.TSFalse;
    }

    internal Tristate NodeIsOnOneLine(Node node)
    {
        return RangeIsOnOneLine(Format.WithTokenStart(node, SourceFile));
    }

    internal Tristate BlockIsOnOneLine(Node node)
    {
        var openBrace = FindChildOfKind(node, Kind.OpenBraceToken, SourceFile);
        var closeBrace = FindChildOfKind(node, Kind.CloseBraceToken, SourceFile);
        if (openBrace is not null && closeBrace is not null)
        {
            var closeBraceStart = Scan.GetTokenPosOfNode(closeBrace, SourceFile, false);
            return RangeIsOnOneLine(NewTextRange(openBrace.End(), closeBraceStart));
        }

        return Tristate.TSFalse;
    }

    public bool ContextNodeAllOnSameLine()
    {
        if (_contextNodeAllOnSameLine == Tristate.TSUnknown)
        {
            _contextNodeAllOnSameLine = NodeIsOnOneLine(_contextNode!);
        }

        return _contextNodeAllOnSameLine == Tristate.TSTrue;
    }

    public bool NextNodeAllOnSameLine()
    {
        if (_nextNodeAllOnSameLine == Tristate.TSUnknown)
        {
            _nextNodeAllOnSameLine = NodeIsOnOneLine(_nextTokenParent!);
        }

        return _nextNodeAllOnSameLine == Tristate.TSTrue;
    }

    public bool TokensAreOnSameLine()
    {
        if (_tokensAreOnSameLine == Tristate.TSUnknown)
        {
            _tokensAreOnSameLine = RangeIsOnOneLine(NewTextRange(_currentTokenSpan.Loc.Pos(), _nextTokenSpan.Loc.End()));
        }

        return _tokensAreOnSameLine == Tristate.TSTrue;
    }

    public bool ContextNodeBlockIsOnOneLine()
    {
        if (_contextNodeBlockIsOnOneLine == Tristate.TSUnknown)
        {
            _contextNodeBlockIsOnOneLine = BlockIsOnOneLine(_contextNode!);
        }

        return _contextNodeBlockIsOnOneLine == Tristate.TSTrue;
    }

    public bool NextNodeBlockIsOnOneLine()
    {
        if (_nextNodeBlockIsOnOneLine == Tristate.TSUnknown)
        {
            _nextNodeBlockIsOnOneLine = BlockIsOnOneLine(_nextTokenParent!);
        }

        return _nextNodeBlockIsOnOneLine == Tristate.TSTrue;
    }
}

public static partial class Format
{
    /// <summary>Go 的 <c>func NewFormattingContext(file *ast.SourceFile, kind FormatRequestKind, options lsutil.FormatCodeSettings) *FormattingContext</c>。</summary>
    public static FormattingContext NewFormattingContext(SourceFile file, FormatRequestKind kind, FormatCodeSettings options)
    {
        return new FormattingContext(file, kind, options);
    }

    /// <summary>Go 的 <c>func withTokenStart(loc *ast.Node, file *ast.SourceFile) core.TextRange</c>。</summary>
    internal static TextRange WithTokenStart(Node loc, SourceFile file)
    {
        var startPos = Scan.GetTokenPosOfNode(loc, file, false);
        return NewTextRange(startPos, loc.End());
    }
}
