// 生成器里拼 C# 源码用的缩进写入器（对应 TSGO 生成器里的 CodeWriter）。
using System.Text;

namespace TypeScript.CodeGen;

internal sealed class CodeWriter
{
    private readonly List<string> _lines = [];
    private int _indent;

    public void Write(string line = "")
    {
        _lines.Add(line.Length == 0 ? "" : new string(' ', _indent * 4) + line);
    }

    public void WriteLineComment(string comment)
    {
        Write(comment);
    }

    public void WriteRaw(string text)
    {
        _lines.Add(text.TrimEnd('\n'));
    }

    public void Push() => _indent++;

    public void Pop() => _indent--;

    public override string ToString()
    {
        var sb = new StringBuilder();
        foreach (var line in _lines)
        {
            sb.Append(line);
            sb.Append('\n');
        }

        return sb.ToString();
    }
}
