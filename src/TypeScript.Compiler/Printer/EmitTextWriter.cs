// ported from: internal/printer/emittextwriter.go
//
// Go 的 `*ast.Symbol` 参数 → C# `Ast.Symbol?`（Go 里调用点允许传 nil）。
using TypeScript.Ast;
using TypeScript.Core;

namespace TypeScript.Printer;

/// <summary>Go 的 <c>type EmitTextWriter interface</c>：对外不透明的文本写入抽象。</summary>
public interface EmitTextWriter
{
    void Write(string s);

    void WriteTrailingSemicolon(string text);

    void WriteComment(string text);

    void WriteKeyword(string text);

    void WriteOperator(string text);

    void WritePunctuation(string text);

    void WriteSpace(string text);

    void WriteStringLiteral(string text);

    void WriteParameter(string text);

    void WriteProperty(string text);

    void WriteSymbol(string text, Symbol? symbol);

    void WriteLine();

    void WriteLineForce(bool force);

    void IncreaseIndent();

    void DecreaseIndent();

    void Clear();

    /// <summary>Go 的 <c>String() string</c>（保留 Go 方法名；见 Locale 的先例）。</summary>
    string String();

    void RawWrite(string s);

    void WriteLiteral(string s);

    int GetTextPos();

    int GetLine();

    /// <summary>列位置，以 UTF-16 代码单元计（source map 兼容）。</summary>
    UTF16Offset GetColumn();

    int GetIndent();

    bool IsAtStartOfLine();

    bool HasTrailingComment();

    bool HasTrailingWhitespace();
}
