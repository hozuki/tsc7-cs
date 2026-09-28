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
// 5) `lastWritten` 字段删除：Go 只用它算 `HasTrailingWhitespace()`（`utf8.DecodeLastRuneInString(w.lastWritten)`）。
//    而 `_lastWritten` 永远等于"最后一次写进 builder 的非空文本"（`writeText`/`RawWrite`/`writeLineRaw` 三个写入点都
//    同时更新二者，`Clear` 同时清空二者）⇒ 只要 builder 非空，其末位代码单元就是 lastWritten 的末位代码单元。故
//    C# 直接读 `_builder[^1]`（等价、且让写路径不必为了这个字段物化 string；docs §3.8.1）。`Clear()` 后 builder 为空，
//    与 Go 的 `builder.Len() == 0` 早退同义。
// 6) `ISpanTextWriter`（internal，C# 专用）：Go 的注释文本是**切片**（`text[pos:end]`），可以直接喂给
//    `WriteComment(text string)`；C# 的 string 不能做视图，经公开接口 `EmitTextWriter` 传递必然物化。故按第 4 条同一
//    手法声明本接口接收 `ReadOnlySpan<char>`，不支持的写者回落 `WriteComment(string)`（行为不变，仅多一次物化）。
// 7) `getIndentString(indent, indentSize)`（Go 包级函数，`strings.Repeat` 每次分配）→ 本类的空格缓存
//    `_spaces`（按需增长）+ `StringBuilder.Append(string, int, int)`/`RawWriteSpaces(n)`：输出逐字节等价，去掉
//    热路径上"每次换行都分配一个缩进串"的浪费（Go 源码里那句 `TODO: This is cached in tsc`）。
using System;
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

/// <summary>
/// C# 专用（Go 无此接口，见 TextWriter.cs 文件头第 6 条）：接收 <c>ReadOnlySpan&lt;char&gt;</c> 视图的写入能力。
/// Go 的 printer 把 <c>text[pos:end]</c> 切片直接交给 <c>WriteComment(text string)</c>（零拷贝）；C# 的 string 不能做
/// 视图，经公开接口 <see cref="EmitTextWriter"/> 传递必然物化（docs §3.8.1）。调用点在发现写者不支持本接口时回落
/// <c>WriteComment(string)</c>，行为不变。
/// </summary>
internal interface ISpanTextWriter
{
    /// <summary>写入一段注释文本的视图（Go: <c>WriteComment(text string)</c>，实参是切片）。</summary>
    void WriteCommentSpan(ReadOnlySpan<char> text);

    /// <summary>写入 <paramref name="count"/> 个空格（Go: <c>RawWrite(getIndentString(n, indentSize))</c>）。</summary>
    void RawWriteSpaces(int count);
}

internal class TextWriter : EmitTextWriter, IGrowableTextWriter, ISpanTextWriter
{
    private string _newLine;
    private int _indentSize;
    private readonly StringBuilder _builder = new();
    private int _indent;
    private bool _lineStart;
    private int _lineCount;
    private int _linePos;
    private bool _hasTrailingCommentState;
    /// <summary>缩进空格缓存（文件头第 7 条）：按需增长，只增不减；`Clear()` 不清（与输出内容无关）。</summary>
    private string _spaces = "";

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
        // Go: utf8.DecodeLastRuneInString(w.lastWritten)（空串/无效编码得到 utf8.RuneError → false）。
        // 文件头第 5 条：builder 非空时其末位代码单元恒等于 lastWritten 的末位代码单元（且 0xD800-0xDFFF 这类
        // 孤立代理项在 Go 侧是 RuneError → false，C# 的 IsWhiteSpaceLike 同样为 false），故直接读 builder。
        return IsWhiteSpaceLike(_builder[_builder.Length - 1]);
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
            _hasTrailingCommentState = false;
        }
        UpdateLineCountAndPosFor(s.AsSpan());
    }

    /// <summary>见 <see cref="ISpanTextWriter.RawWriteSpaces"/>。</summary>
    public void RawWriteSpaces(int count)
    {
        // Go: `p.writer.RawWrite(getIndentString(n, indentSize))`（写入 n 个空格；n == 0 时等价于 RawWrite("")）。
        if (count > 0)
        {
            var spaces = GetSpaces(count);
            _builder.Append(spaces, 0, count);
            _hasTrailingCommentState = false;
            UpdateLineCountAndPosFor(spaces.AsSpan(0, count));
            return;
        }
        UpdateLineCountAndPosFor(ReadOnlySpan<char>.Empty);
    }

    /// <summary>
    /// 返回一个长度 ≥ <paramref name="count"/> 的纯空格串（文件头第 7 条）。
    /// </summary>
    private string GetSpaces(int count)
    {
        if (_spaces.Length < count)
        {
            _spaces = new string(' ', Math.Max(count, _spaces.Length * 2));
        }
        return _spaces;
    }

    /// <summary>
    /// 统计 <paramref name="s"/> 里的 ECMA 行首（见文件头第 3 条）。
    /// </summary>
    private static void CountECMALineStarts(ReadOnlySpan<char> s, out int count, out int lastLineStart)
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

    private void UpdateLineCountAndPosFor(ReadOnlySpan<char> s)
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

    private void WriteText(string s) => WriteText(s.AsSpan());

    private void WriteText(ReadOnlySpan<char> s)
    {
        if (s.Length != 0)
        {
            if (_lineStart)
            {
                // Go: w.builder.WriteString(getIndentString(w.indent, w.indentSize))（文件头第 7 条）。
                // indent == 0 时 Go 的 getIndentString 返回 ""（写空串是 no-op），这里同样直接跳过。
                var indentSpaces = _indent * _indentSize;
                if (indentSpaces != 0)
                {
                    _builder.Append(GetSpaces(indentSpaces), 0, indentSpaces);
                }
                _lineStart = false;
            }
            _builder.Append(s);
            UpdateLineCountAndPosFor(s);
        }
    }

    public void WriteComment(string text) => WriteCommentSpan(text);

    /// <summary>见 <see cref="ISpanTextWriter.WriteCommentSpan"/>（Go: <c>WriteComment(text string)</c> 的切片调用点）。</summary>
    public void WriteCommentSpan(ReadOnlySpan<char> text)
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

    /// <summary>
    /// Go: 包级 <c>getIndentString(indent, indentSize)</c>（<c>strings.Repeat(" ", n)</c>，每次都分配）。
    /// 本仓把它内化到 <see cref="TextWriter"/> 的空格缓存里（TextWriter.cs 文件头第 7 条）：热路径只写视图、
    /// 不物化缩进串。调用点因此改成 <see cref="WriteIndentSpaces"/>（写视图）——语义与 Go 逐字节相同。
    /// </summary>
    internal static void WriteIndentSpaces(EmitTextWriter writer, int count)
    {
        if (writer is ISpanTextWriter spanWriter)
        {
            spanWriter.RawWriteSpaces(count);
            return;
        }
        // 非本包写者（Ls 的 DisplayPartsWriter 等）：回落 Go 的原样写法（物化缩进串）。
        writer.RawWrite(count == 0 ? "" : new string(' ', count));
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
