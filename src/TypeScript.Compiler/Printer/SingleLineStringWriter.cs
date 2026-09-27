// ported from: internal/printer/singlelinestringwriter.go
//
// 偏离 Go 的地方：
// 1) Go 的 `sync.Pool`（每个 P 一个本地池）→ C# 的 `[ThreadStatic]` 单槽（每个线程一个可复用实例，Get 时取走、
//    Cleanup 时放回）。语义与 Go 一致：Get 取到的是已 Clear 的实例；重入时槽为空 → 分配新实例（Go 的 Pool 同理）。
//    没有用 `ConcurrentBag`/锁，避免在热路径（每个 identifier 的临时打印）上加同步。
// 2) Go 的 `GetSingleLineStringWriter() (EmitTextWriter, func())` → C# 返回元组 `(EmitTextWriter Writer, Action Cleanup)`
//    （与 Go 的形状逐位对应；Cleanup 是等价的闭包）。
// 3) `HasTrailingWhitespace`：Go 用 `utf8.DecodeLastRuneInString`；C# 的 string 是 UTF-16，末位 char 即最后一个代码单元
//    （空白类码点全在 BMP，判定结果与 Go 相同；空串/无效编码在 Go 里是 RuneError → false，C# 用长度判断）。
using System.Text;
using TypeScript.Core;
using static TypeScript.StringUtil.StringUtil;

namespace TypeScript.Printer;

internal sealed class SingleLineStringWriter : EmitTextWriter
{
    [ThreadStatic]
    private static SingleLineStringWriter? _pooled;

    private readonly StringBuilder _builder = new();
    private string _lastWritten = "";

    private SingleLineStringWriter()
    {
    }

    public static (EmitTextWriter Writer, Action Cleanup) Get()
    {
        var w = _pooled ?? new SingleLineStringWriter();
        _pooled = null;
        w.Clear();
        return (w, () => { _pooled = w; });
    }

    public void Clear()
    {
        _lastWritten = "";
        _builder.Clear();
    }

    public void DecreaseIndent()
    {
        // Do Nothing
    }

    public UTF16Offset GetColumn() => new(0);

    public int GetIndent() => 0;

    public int GetLine() => 0;

    public string String() => _builder.ToString();

    public int GetTextPos() => _builder.Length;

    public bool HasTrailingComment() => false;

    public bool HasTrailingWhitespace()
    {
        if (_builder.Length == 0)
        {
            return false;
        }
        if (_lastWritten.Length == 0)
        {
            return false;
        }
        return IsWhiteSpaceLike(_lastWritten[^1]);
    }

    public void IncreaseIndent()
    {
        // Do Nothing
    }

    public bool IsAtStartOfLine() => false;

    public void RawWrite(string s)
    {
        _lastWritten = s;
        _builder.Append(s);
    }

    public void Write(string s)
    {
        _lastWritten = s;
        _builder.Append(s);
    }

    public void WriteComment(string text)
    {
        _lastWritten = text;
        _builder.Append(text);
    }

    public void WriteKeyword(string text)
    {
        _lastWritten = text;
        _builder.Append(text);
    }

    public void WriteLine()
    {
        _lastWritten = " ";
        _builder.Append(' ');
    }

    public void WriteLineForce(bool force)
    {
        _lastWritten = " ";
        _builder.Append(' ');
    }

    public void WriteLiteral(string s)
    {
        _lastWritten = s;
        _builder.Append(s);
    }

    public void WriteOperator(string text)
    {
        _lastWritten = text;
        _builder.Append(text);
    }

    public void WriteParameter(string text)
    {
        _lastWritten = text;
        _builder.Append(text);
    }

    public void WriteProperty(string text)
    {
        _lastWritten = text;
        _builder.Append(text);
    }

    public void WritePunctuation(string text)
    {
        _lastWritten = text;
        _builder.Append(text);
    }

    public void WriteSpace(string text)
    {
        _lastWritten = text;
        _builder.Append(text);
    }

    public void WriteStringLiteral(string text)
    {
        _lastWritten = text;
        _builder.Append(text);
    }

    public void WriteSymbol(string text, Ast.Symbol? symbol)
    {
        _lastWritten = text;
        _builder.Append(text);
    }

    public void WriteTrailingSemicolon(string text)
    {
        _lastWritten = text;
        _builder.Append(text);
    }
}

public sealed partial class Printer
{
    /// <summary>Go: <c>func GetSingleLineStringWriter() (EmitTextWriter, func())</c>（池化见文件头第 1 条）。</summary>
    public static (EmitTextWriter Writer, Action Cleanup) GetSingleLineStringWriter() =>
        SingleLineStringWriter.Get();
}
