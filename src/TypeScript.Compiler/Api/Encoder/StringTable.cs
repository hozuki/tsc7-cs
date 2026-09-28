// ported from: internal/api/encoder/stringtable.go:1-68
//
// 与 Go 的偏离（逐条说明）：
//
// 1) **字节语义 vs 字符语义（本文件最关键的偏离）**：Go 的 `string` 是 UTF-8 字节序列，
//    `len(s)`/`s[i:j]`/`strings.Builder.Len()` 全是字节语义，而字符串偏移表记录的正是
//    **字符串数据段的 UTF-8 字节偏移**（协议规定，解码端按字节切片读回字符串）。
//    C# 的 `string` 是 UTF-16 代码单元序列，所以：
//    - `len(text)` → `Wtf8.ByteCount(text)`（WTF-8 字节数，见本文件底部的 `Wtf8`）；
//    - Go 直接用字节下标的 `t.fileText[start:end]` → 先在 char 域定位（`e - text.Length`）再按字节换算偏移；
//    - 相等比较在 char 域做（`string.CompareOrdinal`）：相同字符序列的 WTF-8 字节序列必然相同，语义等价，
//      且省掉一次编码。
//    另外 Go 允许"字节下标切在字符中间"（切片不 panic），C# 的 char 下标不可能切在字符中间——
//    但节点 pos/end 本来就落在字符边界上，所以这是纯理论差异。
//    唯一的行为级差异：Go 在 `start < 0`（节点位置与文件文本不自洽）时切片越界 panic，C# 判定为"不匹配"
//    并落到 otherStrings 分支（编码仍然合法）。
// 2) `strings.Builder` → `ByteBuffer`（`_otherStrings`）：Go 的 Builder 攒的是字节，C# 的 `StringBuilder`
//    攒的是 char，而偏移量必须按字节算，所以直接攒字节；`Len()` → `_otherStrings.Length`。
// 3) `offsets []uint32` → `List<uint>`：`encode()` 之外还有 `len(t.offsets)` 的读取（Encoder.cs 里算
//    string-offsets 段长度），所以它不是纯内部状态。
// 4) 本文件底部的 `Wtf8` 是 **C# 专用基础设施**（Go 没有对应物）：Go 的字符串本身就是字节序列，
//    JS 字符串里的孤立代理项在 Go 的 string 里以 WTF-8 形式存在；C# 要写出与 Go 逐字节相同的
//    数据段，就不能用 `Encoding.UTF8`（它会把孤立代理项替换成 U+FFFD），必须自己编码 WTF-8：
//    孤立代理项 → 3 字节，合法代理对 → 4 字节，其余按标准 UTF-8。
// 5) Go 的 `newStringTable(...)` 未导出构造函数 → `internal static NewStringTable(...)`（保留 Go 名）
//    + `internal` 构造函数；`add`/`encode`/`stringLength`/`encodedLength` → `Add`/`Encode`/`StringLength`/`EncodedLength`。
using System.Buffers.Binary;
using TypeScript.Ast;

namespace TypeScript.Api.Encoder;

/// <summary>
/// Go: <c>type stringTable struct { fileText string; otherStrings *strings.Builder; offsets []uint32 }</c>
/// （stringtable.go:9-13）。把节点字符串映射到字符串数据段里的 (start, end) 字节偏移对。
/// </summary>
internal sealed class StringTable
{
    private readonly string _fileText;

    /// <summary>Go: <c>otherStrings *strings.Builder</c> —— 与文件文本不匹配的字符串按出现顺序追加在这里。</summary>
    private readonly ByteBuffer _otherStrings = new(0);

    /// <summary>Go: <c>offsets []uint32</c>（pos/end 字节偏移对，`Encoder.EncodeTree` 要读 Count 算段长度）。</summary>
    internal readonly List<uint> Offsets;

    // Go 里 `len(t.fileText)` 是 O(1)；C# 需要"char 下标 → 字节偏移"的换算，这里用检查点把总成本压到 O(fileText)：
    // 节点按源码顺序访问，所以 char 下标单调递增（只有访问根节点后回头编码子节点时会回退一次）。
    private int _checkpointChar;
    private int _checkpointByte;

    internal StringTable(string fileText, int stringCount)
    {
        _fileText = fileText;
        Offsets = new List<uint>(stringCount * 2);
    }

    /// <summary>Go: <c>func newStringTable(fileText string, stringCount int) *stringTable</c>（stringtable.go:15-22）。</summary>
    internal static StringTable NewStringTable(string fileText, int stringCount) => new(fileText, stringCount);

    /// <summary>Go: <c>func (t *stringTable) add(text string, kind ast.Kind, pos int, end int) uint32</c>（stringtable.go:24-52）。</summary>
    internal uint Add(string text, Kind kind, int pos, int end)
    {
        var index = (uint)Offsets.Count;
        if (kind == Kind.SourceFile)
        {
            Offsets.Add((uint)Utf8OffsetOf(pos));
            Offsets.Add((uint)Utf8OffsetOf(end));
            return index;
        }

        var length = Wtf8.ByteCount(text);
        if (end - pos > 0 && end <= _fileText.Length)
        {
            // Go 的注释：pos includes leading trivia, but we can usually infer the actual start of the
            // string from the kind and end
            var endOffset = 0;
            if (kind is Kind.StringLiteral or Kind.TemplateTail or Kind.NoSubstitutionTemplateLiteral)
            {
                endOffset = 1;
            }

            var e = end - endOffset;
            var s = e - text.Length; // Go 的 `start := end - length`（字节域）在 char 域即此式
            // 偏离：Go 在 `start < 0`（位置与文本不自洽时）会以切片越界 panic；C# 这里判定为"不匹配"，
            // 落到下面的 otherStrings 分支（结果仍是合法编码，只是该字符串不再零拷贝复用文件文本）。
            if (s >= 0 && string.CompareOrdinal(_fileText, s, text, 0, text.Length) == 0)
            {
                Offsets.Add((uint)Utf8OffsetOf(s));
                Offsets.Add((uint)Utf8OffsetOf(e));
                return index;
            }
        }

        // no exact match, so we need to add it to the string table
        var offset = Wtf8.ByteCount(_fileText) + _otherStrings.Length;
        Wtf8.Write(_otherStrings, text, length);
        Offsets.Add((uint)offset);
        Offsets.Add((uint)(offset + length));
        return index;
    }

    /// <summary>Go: <c>func (t *stringTable) encode() []byte</c>（stringtable.go:54-60）。</summary>
    internal byte[] Encode()
    {
        var result = new byte[EncodedLength()];
        var span = result.AsSpan();
        var pos = 0;
        foreach (var offset in Offsets)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(span[pos..], offset);
            pos += 4;
        }

        pos += Wtf8.Write(span[pos..], _fileText);
        _otherStrings.AsSpan().CopyTo(span[pos..]);
        return result;
    }

    /// <summary>Go: <c>func (t *stringTable) stringLength() int</c>（stringtable.go:62-64）：文件文本 + 其它字符串的**字节**总长。</summary>
    internal int StringLength() => Wtf8.ByteCount(_fileText) + _otherStrings.Length;

    /// <summary>Go: <c>func (t *stringTable) encodedLength() int</c>（stringtable.go:66-68）。</summary>
    internal int EncodedLength() => Offsets.Count * 4 + StringLength();

    /// <summary>
    /// C# 专用助手（Go 无对应物）：把 `_fileText` 的 char 下标换算成 UTF-8/WTF-8 字节偏移。
    /// Go 里 `t.fileText` 本身就是字节串，下标即字节偏移，不需要这一步。
    /// 越界时钳制到文本末尾（Go 里 `t.fileText[start:end]` 越界会 panic；本仓约定 panic → 异常，
    /// 但这里只在 SourceFile 分支可能被调用，节点自身的 pos/end 恒在范围内，所以钳制只是防御）。
    /// 落在代理对中间的 char 下标会吸附到代理对的起始处（Go 的字节下标无法表达"码点中间"，节点位置也
    /// 从不会切在代理对中间）。
    /// </summary>
    private int Utf8OffsetOf(int charIndex)
    {
        if (charIndex > _fileText.Length)
        {
            charIndex = _fileText.Length;
        }

        if (charIndex > 0 && charIndex < _fileText.Length
            && char.IsLowSurrogate(_fileText[charIndex]) && char.IsHighSurrogate(_fileText[charIndex - 1]))
        {
            charIndex--;
        }

        var charPos = _checkpointChar;
        var bytePos = _checkpointByte;
        if (charIndex < charPos)
        {
            // 回退（encodeTree 先编码根节点再回头编码子节点；JSDoc 字符串的位置也在其节点之前）：
            // 直接减去 [charIndex, charPos) 区间的字节数——回退距离通常很短，最坏也只是"检查点走到文件末尾后
            // 一次性回退到首个节点"，整体仍是 O(fileText)。
            bytePos -= Wtf8.ByteCount(_fileText, charIndex, charPos);
            charPos = charIndex;
        }

        while (charPos < charIndex)
        {
            var c = _fileText[charPos];
            if (char.IsHighSurrogate(c) && charPos + 1 < _fileText.Length && char.IsLowSurrogate(_fileText[charPos + 1]))
            {
                charPos += 2;
                bytePos += 4;
            }
            else if (c < 0x80)
            {
                charPos += 1;
                bytePos += 1;
            }
            else if (c < 0x800)
            {
                charPos += 1;
                bytePos += 2;
            }
            else
            {
                charPos += 1;
                bytePos += 3;
            }
        }

        _checkpointChar = charPos;
        _checkpointByte = bytePos;
        return bytePos;
    }
}

/// <summary>
/// C# 专用基础设施（Go 无对应物，见文件头 4）：WTF-8 编解码的编码侧。
/// Go 的 `string` 就是字节序列，`append(buf, s...)` 写的是它原本的字节；C# 必须显式产出同样的字节，
/// 且**不能**用 `Encoding.UTF8`（它把孤立代理项换成 U+FFFD，会让孤立代理项所在的字符串偏移错位）。
/// </summary>
internal static class Wtf8
{
    /// <summary>Go 的 <c>len(s)</c>（UTF-8/WTF-8 字节数）。</summary>
    internal static int ByteCount(string s)
    {
        var count = 0;
        for (var i = 0; i < s.Length; i++)
        {
            var c = s[i];
            if (char.IsHighSurrogate(c) && i + 1 < s.Length && char.IsLowSurrogate(s[i + 1]))
            {
                count += 4;
                i++;
            }
            else if (c < 0x80)
            {
                count += 1;
            }
            else if (c < 0x800)
            {
                count += 2;
            }
            else
            {
                count += 3;
            }
        }

        return count;
    }

    /// <summary>
    /// Go 的 <c>len(s[start:end])</c>（不分配子串的字节计数；StringTable 的回退换算用它，
    /// 见 <c>Utf8OffsetOf</c>）。
    /// </summary>
    internal static int ByteCount(string s, int start, int end)
    {
        var count = 0;
        for (var i = start; i < end; i++)
        {
            var c = s[i];
            if (char.IsHighSurrogate(c) && i + 1 < s.Length && char.IsLowSurrogate(s[i + 1]))
            {
                count += 4;
                i++;
            }
            else if (c < 0x80)
            {
                count += 1;
            }
            else if (c < 0x800)
            {
                count += 2;
            }
            else
            {
                count += 3;
            }
        }

        return count;
    }

    /// <summary>把 <paramref name="s"/> 编码进 <paramref name="dest"/>，返回写入的字节数（调用点必须先按 <see cref="ByteCount"/> 备好空间）。</summary>
    internal static int Write(Span<byte> dest, string s)
    {
        var pos = 0;
        for (var i = 0; i < s.Length; i++)
        {
            var c = s[i];
            if (char.IsHighSurrogate(c) && i + 1 < s.Length && char.IsLowSurrogate(s[i + 1]))
            {
                var codePoint = 0x10000 + ((c - 0xD800) << 10) + (s[i + 1] - 0xDC00);
                dest[pos++] = (byte)(0xF0 | (codePoint >> 18));
                dest[pos++] = (byte)(0x80 | ((codePoint >> 12) & 0x3F));
                dest[pos++] = (byte)(0x80 | ((codePoint >> 6) & 0x3F));
                dest[pos++] = (byte)(0x80 | (codePoint & 0x3F));
                i++;
            }
            else if (c < 0x80)
            {
                dest[pos++] = (byte)c;
            }
            else if (c < 0x800)
            {
                dest[pos++] = (byte)(0xC0 | (c >> 6));
                dest[pos++] = (byte)(0x80 | (c & 0x3F));
            }
            else
            {
                dest[pos++] = (byte)(0xE0 | (c >> 12));
                dest[pos++] = (byte)(0x80 | ((c >> 6) & 0x3F));
                dest[pos++] = (byte)(0x80 | (c & 0x3F));
            }
        }

        return pos;
    }

    /// <summary>追加到 <paramref name="buf"/>（<paramref name="byteCount"/> 必须是 <see cref="ByteCount"/> 的结果，避免重复计数）。</summary>
    internal static void Write(ByteBuffer buf, string s, int byteCount)
    {
        Write(buf.GetWriteSpan(byteCount), s);
    }
}
