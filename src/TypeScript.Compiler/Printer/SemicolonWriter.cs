// ported from: internal/printer/semicolon_writer.go
//
// Go 的未导出类型 `trailingSemicolonDeferringWriter` → C# 的 internal `TrailingSemicolonDeferringWriter`
// （未导出类型按 C# 习惯命名）；未导出的构造函数 `getTrailingSemicolonDeferringWriter` →
// `Printer.GetTrailingSemicolonDeferringWriter`（internal static，调用点与 Go 同形）。
using TypeScript.Core;

namespace TypeScript.Printer;

internal sealed class TrailingSemicolonDeferringWriter : EmitTextWriter
{
    private readonly EmitTextWriter _inner;
    private bool _hasPendingSemicolon;

    public TrailingSemicolonDeferringWriter(EmitTextWriter writer)
    {
        _inner = writer;
    }

    private void CommitSemicolon()
    {
        if (_hasPendingSemicolon)
        {
            _inner.WriteTrailingSemicolon(";");
            _hasPendingSemicolon = false;
        }
    }

    public void Write(string s)
    {
        CommitSemicolon();
        _inner.Write(s);
    }

    public void WriteTrailingSemicolon(string _)
    {
        _hasPendingSemicolon = true;
    }

    public void WriteComment(string text)
    {
        CommitSemicolon();
        _inner.WriteComment(text);
    }

    public void WriteKeyword(string text)
    {
        CommitSemicolon();
        _inner.WriteKeyword(text);
    }

    public void WriteOperator(string text)
    {
        CommitSemicolon();
        _inner.WriteOperator(text);
    }

    public void WritePunctuation(string text)
    {
        CommitSemicolon();
        _inner.WritePunctuation(text);
    }

    public void WriteSpace(string text)
    {
        CommitSemicolon();
        _inner.WriteSpace(text);
    }

    public void WriteStringLiteral(string text)
    {
        CommitSemicolon();
        _inner.WriteStringLiteral(text);
    }

    public void WriteParameter(string text)
    {
        CommitSemicolon();
        _inner.WriteParameter(text);
    }

    public void WriteProperty(string text)
    {
        CommitSemicolon();
        _inner.WriteProperty(text);
    }

    public void WriteSymbol(string text, Ast.Symbol? symbol)
    {
        CommitSemicolon();
        _inner.WriteSymbol(text, symbol);
    }

    public void WriteLine()
    {
        CommitSemicolon();
        _inner.WriteLine();
    }

    public void WriteLineForce(bool force)
    {
        CommitSemicolon();
        _inner.WriteLineForce(force);
    }

    public void IncreaseIndent()
    {
        CommitSemicolon();
        _inner.IncreaseIndent();
    }

    public void DecreaseIndent()
    {
        CommitSemicolon();
        _inner.DecreaseIndent();
    }

    public void Clear()
    {
        _hasPendingSemicolon = false;
        _inner.Clear();
    }

    public string String() => _inner.String();

    public void RawWrite(string s)
    {
        CommitSemicolon();
        _inner.RawWrite(s);
    }

    public void WriteLiteral(string s)
    {
        CommitSemicolon();
        _inner.WriteLiteral(s);
    }

    public int GetTextPos() => _inner.GetTextPos();

    public int GetLine() => _inner.GetLine();

    public UTF16Offset GetColumn() => _inner.GetColumn();

    public int GetIndent() => _inner.GetIndent();

    public bool IsAtStartOfLine() => _inner.IsAtStartOfLine();

    public bool HasTrailingComment() => _inner.HasTrailingComment();

    public bool HasTrailingWhitespace() => _inner.HasTrailingWhitespace();
}

public sealed partial class Printer
{
    /// <summary>Go: <c>func getTrailingSemicolonDeferringWriter(writer EmitTextWriter) EmitTextWriter</c>。</summary>
    internal static EmitTextWriter GetTrailingSemicolonDeferringWriter(EmitTextWriter writer) =>
        new TrailingSemicolonDeferringWriter(writer);
}
