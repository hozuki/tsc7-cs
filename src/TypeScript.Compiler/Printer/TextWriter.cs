// ported from: internal/printer/textwriter.go
//
// 偏离 Go 的地方：
// 1) Go 的未导出类型 `textWriter` → C# 的 internal `TextWriter`（名字与 System.IO.TextWriter 撞名，本 package 的
//    文件不 `using System.IO`，调用点一律用接口 `EmitTextWriter`，见 `NewTextWriter`）。
// 2) `GetColumn()`：Go 写的是 `core.UTF16Len(w.builder.String()[w.linePos:])`（切片 + UTF-16 计数）。C# 的
//    StringBuilder 内容本来就是 UTF-16 代码单元序列，`UTF16Len` 退化成"代码单元数"（docs §3.1），所以直接
//    `_builder.Length - _linePos`：语义等价且不分配（热路径，docs §3.8）。
// 3) `updateLineCountAndPosFor`：Go 用 `core.ComputeECMALineStartsSeq`（range-over-func 迭代器）算行首。
//    C# 里 `ComputeECMALineStartsSeq` 是 `IEnumerable`（每次 Write 都分配枚举器），所以这里内联同一套扫描逻辑
//    （`CountECMALineStarts`，逐条照抄 core.go:433-463 的判定），只按 `char` 处理：C# 的 string 是 UTF-16，
//    `\r`/`\n` 与 BMP 的 `IsLineBreak` 码点都落在单个 char 上，与 Go 按字节/码点判断的结果一致。
// 4) `Grow(n)`：Go 的 `strings.Builder.Grow` → `StringBuilder.EnsureCapacity(Length + n)`（同样的"预留 n 个
//    代码单元"语义）。Go 里 printer.go:5099 用**匿名结构化接口** `interface{ Grow(n int) }` 探测，C# 没有结构类型，
//    显式声明 `IGrowableTextWriter`（internal）。
using System.Text;
using TypeScript.Core;
using static TypeScript.StringUtil.StringUtil;

namespace TypeScript.Printer;

/// <summary>
/// Go: <c>printer.go:5099</c> 的匿名结构化接口 <c>interface{ Grow(n int) }</c>：带容量预留能力的文本写入器。
/// </summary>
internal interface IGrowableTextWriter
{
    void Grow(int n);
}

internal class TextWriter : EmitTextWriter, IGrowableTextWriter
{
    private string _newLine;
    private int _indentSize;
    private readonly StringBuilder _builder = new();
    private string _lastWritten = "";
    private int _indent;
    private bool _lineStart;
    private int _lineCount;
    private int _linePos;
    private bool _hasTrailingCommentState;

    public TextWriter(string newLine, int indentSize)
    {
        _newLine = newLine;
        _indentSize = indentSize;
        _lineStart = true;
    }

    public void Clear()
    {
        // Go: *w = textWriter{newLine: w.newLine, indentSize: w.indentSize, lineStart: true}
        _builder.Clear();
        _lastWritten = "";
        _indent = 0;
        _lineStart = true;
        _lineCount = 0;
        _linePos = 0;
        _hasTrailingCommentState = false;
    }

    public void Grow(int n)
    {
        _builder.EnsureCapacity(_builder.Length + n);
    }

    public void DecreaseIndent()
    {
        _indent--;
    }

    /// <summary>
    /// GetColumn returns the column position measured in UTF-16 code units
    /// for source map compatibility.
    /// </summary>
    public UTF16Offset GetColumn()
    {
        if (_lineStart)
        {
            return new UTF16Offset(_indent * _indentSize);
        }
        // Count UTF-16 code units from the last line start. (Go: core.UTF16Len(w.builder.String()[w.linePos:]))
        return new UTF16Offset(_builder.Length - _linePos);
    }

    public int GetIndent() => _indent;

    public int GetLine() => _lineCount;

    public string String() => _builder.ToString();

    public int GetTextPos() => _builder.Length;

    public bool HasTrailingComment() => _hasTrailingCommentState;

    public bool HasTrailingWhitespace()
    {
        if (_builder.Length == 0)
        {
            return false;
        }
        // Go: utf8.DecodeLastRuneInString(w.lastWritten)；空串/无效编码得到 utf8.RuneError → false。
        if (_lastWritten.Length == 0)
        {
            return false;
        }
        return IsWhiteSpaceLike(_lastWritten[^1]);
    }

    public void IncreaseIndent()
    {
        _indent++;
    }

    public bool IsAtStartOfLine() => _lineStart;

    public void RawWrite(string s)
    {
        if (s.Length != 0)
        {
            _builder.Append(s);
            _lastWritten = s;
            _hasTrailingCommentState = false;
        }
        UpdateLineCountAndPosFor(s);
    }

    /// <summary>
    /// 统计 <paramref name="s"/> 里的 ECMA 行首（见文件头第 3 条）。
    /// </summary>
    private static void CountECMALineStarts(string s, out int count, out int lastLineStart)
    {
        var textLen = s.Length;
        var pos = 0;
        var lineStart = 0;
        var lineCount = 0;
        while (pos < textLen)
        {
            var b = s[pos];
            if (b < 0x80)
            {
                pos++;
                if (b == '\r')
                {
                    if (pos < textLen && s[pos] == '\n')
                    {
                        pos++;
                    }
                    lineCount++;
                    lineStart = pos;
                }
                else if (b == '\n')
                {
                    lineCount++;
                    lineStart = pos;
                }
            }
            else
            {
                pos++;
                if (IsLineBreak(b))
                {
                    lineCount++;
                    lineStart = pos;
                }
            }
        }
        count = lineCount + 1; // Go 结尾还有一次 `yield(lineStart)`
        lastLineStart = lineStart;
    }

    private void UpdateLineCountAndPosFor(string s)
    {
        CountECMALineStarts(s, out var count, out var lastLineStart);

        if (count > 1)
        {
            _lineCount += count - 1;
            var curLen = _builder.Length;
            _linePos = curLen - s.Length + lastLineStart;
            _lineStart = (_linePos - curLen) == 0;
            return;
        }
        _lineStart = false;
    }

    public void Write(string s)
    {
        if (s.Length != 0)
        {
            _hasTrailingCommentState = false;
        }
        WriteText(s);
    }

    private void WriteText(string s)
    {
        if (s.Length != 0)
        {
            if (_lineStart)
            {
                _builder.Append(Printer.GetIndentString(_indent, _indentSize));
                _lineStart = false;
            }
            _builder.Append(s);
            _lastWritten = s;
            UpdateLineCountAndPosFor(s);
        }
    }

    public void WriteComment(string text)
    {
        if (text.Length != 0)
        {
            _hasTrailingCommentState = true;
        }
        WriteText(text);
    }

    public void WriteKeyword(string text) => Write(text);

    private void WriteLineRaw()
    {
        _builder.Append(_newLine);
        _lastWritten = _newLine;
        _lineCount++;
        _linePos = _builder.Length;
        _lineStart = true;
        _hasTrailingCommentState = false;
    }

    public void WriteLine()
    {
        if (!_lineStart)
        {
            WriteLineRaw();
        }
    }

    public void WriteLineForce(bool force)
    {
        if (!_lineStart || force)
        {
            WriteLineRaw();
        }
    }

    public void WriteLiteral(string s) => Write(s);

    public void WriteOperator(string text) => Write(text);

    public void WriteParameter(string text) => Write(text);

    public void WriteProperty(string text) => Write(text);

    public void WritePunctuation(string text) => Write(text);

    public void WriteSpace(string text) => Write(text);

    public void WriteStringLiteral(string text) => Write(text);

    public void WriteSymbol(string text, Ast.Symbol? symbol) => Write(text);

    public void WriteTrailingSemicolon(string text) => Write(text);
}

public sealed partial class Printer
{
    private const int _defaultIndentSize = 4;

    /// <summary>
    /// GetDefaultIndentSize returns the default indent size (4 spaces) used when no specific indent size is configured.
    /// </summary>
    public static int GetDefaultIndentSize() => _defaultIndentSize;

    internal static string GetIndentString(int indent, int indentSize)
    {
        if (indent == 0)
        {
            return "";
        }
        // TODO: This is cached in tsc - should it be cached here?
        return new string(' ', indent * indentSize);
    }

    public static EmitTextWriter NewTextWriter(string newLine, int indentSize)
    {
        if (indentSize <= 0)
        {
            indentSize = 4;
        }
        var w = new TextWriter(newLine, indentSize);
        w.Clear();
        return w;
    }
}
