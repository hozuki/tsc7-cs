// ported from: internal/sourcemap/lineinfo.go
//
// 与 Go 的差异（逐条）：
// - 未导出字段 `text`/`lineStarts` → `internal` 的 `_text`/`_lineStarts`（source_mapper.go 同包读取，docs/architecture.md §2）。
// - `CreateECMALineInfo` 是包级导出函数 → 放在静态类 `SourceMap` 上（docs/architecture.md §2：包级函数放与 package
//   同名的静态类；调用点用 `using static TypeScript.SourceMap.SourceMap;` 后与 Go 的裸函数名一致）。
// - Go 的 `li.text[pos:end]` 是零拷贝子串；C# 的 `string` 切片必然分配（§3.8 的例外：Go 的 string 切片本身不分配，
//   C# 没有对应物）。`LineText` 不是热路径（只在找 sourceMappingURL 与错误报告时调用）。
namespace TypeScript.SourceMap;

using TypeScript.Core;

public sealed class ECMALineInfo
{
    internal readonly string _text;

    internal readonly ECMALineStarts _lineStarts;

    internal ECMALineInfo(string text, ECMALineStarts lineStarts)
    {
        _text = text;
        _lineStarts = lineStarts;
    }

    public int LineCount() => _lineStarts.Length;

    public string LineText(int line)
    {
        var lineStarts = _lineStarts;
        var pos = lineStarts[line];
        TextPos end;
        if (line + 1 < lineStarts.Length)
        {
            end = lineStarts[line + 1];
        }
        else
        {
            end = new TextPos(_text.Length);
        }

        return _text[(int)pos..(int)end];
    }
}

public static partial class SourceMap
{
    public static ECMALineInfo CreateECMALineInfo(string text, ECMALineStarts lineStarts) =>
        new(text, lineStarts);
}
