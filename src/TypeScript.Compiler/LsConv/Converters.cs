// ported from: tsc/internal/ls/lsconv/converters.go:1-635（MappedSpan/MappedPosition:24-34、Converters:20-449、
//               LanguageKindToScriptKind:312-327、FileNameToDocumentURI:329-379、Diagnostic 转换:451-614、
//               OriginalTextScript:573-585。Script 接口在 Script.cs。）
//
// 与 Go 的偏离（逐条）：
// 1) **`Script` 接口**放在 Script.cs（含 `SourceFile` 的 partial 接口声明）；本文件只有转换逻辑。
// 2) 包级函数 → 静态类 `LsConv`（docs/architecture.md §2）：`NewConverters` / `FileNameToDocumentURI` /
//    `LanguageKindToScriptKind` / `DiagnosticToLSPPull` / `DiagnosticToLSPPush`。未导出的包级函数
//    （`sourceFileProjections`/`virtualRangeToOriginal`/`virtualPositionToOriginal`/`diagnosticToLSP`/
//    `diagnosticScriptAndRange`/`diagnosticSeverity`/`messageChainToString`/`ptrToSliceIfNonEmpty`）
//    → 本文件的 private static + PascalCase。
// 3) **命名空间遮蔽同名类型**（本仓硬规则）：`SpanMap` 既是命名空间又是类型 → 类型用别名 `SpanMapType`，
//    命名空间用别名 `SpanMapNs`；`Range` 与 `System.Range` 同名（ImplicitUsings 里的 `global using System;`）
//    → 别名 `Range = TypeScript.LsProto.Range`；`Diagnostic` 在 `TypeScript.Ast` 与 `TypeScript.LsProto` 都有
//    → 别名 `AstDiag` / `LspDiag`；`Locale` 既是命名空间又是类型 → 别名 `Loc`；`Diagnostics` / `Bundled` /
//    `TSPath` / `DiagnosticWriter` 都是"命名空间 + 同名静态类" → 用 `using static` 直接调函数名。
// 4) **位置编码**：Go 的 `url.PathEscape` 在 C# 无对应物（`Uri.EscapeDataString` 的转义集与 Go 不同：
//    Go 保留 `$&+:=@`，.NET 会转义它们）→ 本文件按 Go `net/url` 的 `shouldEscape(encodePathSegment)` 逐条实现
//    `PathEscape`（按 UTF-8 字节处理）。`extraEscapeReplacer`（strings.Replacer）→ `ExtraEscape` 的顺序
//    `string.Replace`：替换键都是单个非字母数字字符，替换值只含 `%`/数字/大写字母，互不包含，逐个替换与
//    Go 的单遍替换等价。
// 5) **字节 vs UTF-16**：本仓内部坐标是 UTF-16 代码单元（Core/Text.cs 文件头）。`lineAndCharacterToPosition` /
//    `positionToLineAndCharacter` 里的 Go 代码在 UTF-8 字节上走位，这里改写成在 UTF-16 串上走位：
//      * `utf8.DecodeRuneInString` → `Scanner.DecodeRuneAt`（返回码点，`size` 就是消耗的 UTF-16 代码单元数），
//        于是 Go 的 `utf16.RuneLen(r)` 与 `size` 相等；
//      * UTF-16 position encoding 的"重扫 UTF-8 字节串累加 RuneLen"分支退化成 `position - start`；
//      * UTF-8 position encoding 的"字符偏移 = 字节偏移"分支在 C# 里必须真的按码点累加 UTF-8 字节数
//        （`Utf8LineOffsetToCodeUnit` / `CodeUnitToUtf8LineOffset`），否则非 ASCII 行会算错。
//    两处 Go 的哨兵语义（不切开代理对：`utf16Char + u16Len > char` 就停下）保留。
// 6) `slices.Contains(opts.tagValueSet, …)` → `Array.IndexOf(...) >= 0`；`strings.Cut` → `IndexOf` + 切片
//    （不移植 Go 的 `ok` 三元组）。`panic` → `Debug.Fail(...)`（其后无返回值需求）。
// 7) `Diagnostic` 相关：`&severity` → `Severity = severity`（C# 是 `DiagnosticSeverity?` 字段）；
//    `ptrToSliceIfNonEmpty` → `T[]?`（空数组 → null，与 Go 的 nil 切片一致）。
//    `fmt.Sprintf("TS%d", code)` → `"TS" + code.ToString(CultureInfo.InvariantCulture)`。
// 8) `func (lm *LSPLineMap)` 的调用来自本文件时用 `BinarySearchLineStarts`（见 Linemap.cs 文件头第 6 条）。
// 9) `debug.Assert` → `Debug.Assert`；`_getLineMap(fileName)` 在 Go 里可能返回 nil 指针并在下一行崩溃，
//    C# 里用 `!` 表达同样的"不预期为 null"（不额外做容错，保持与 Go 一致）。

using System.Globalization;
using System.Text;
using TypeScript.Ast;
using TypeScript.Collections;
using TypeScript.Core;
using TypeScript.Diagnostics;
using TypeScript.LsProto;
using TypeScript.SpanMap;
using static TypeScript.Bundled.Bundled;
using static TypeScript.Collections.Collections;
using static TypeScript.Core.Core;
using static TypeScript.DiagnosticWriter.DiagnosticWriter;
using static TypeScript.Diagnostics.Diagnostics;
using static TypeScript.TSPath.TSPath;
using AstDiag = TypeScript.Ast.Diagnostic;
using Loc = TypeScript.Locale.Locale;
using LspDiag = TypeScript.LsProto.Diagnostic;
using Range = TypeScript.LsProto.Range;
using Scan = TypeScript.Scanner.Scanner;
using SpanMapNs = TypeScript.SpanMap;
using SpanMapType = TypeScript.SpanMap.SpanMap;

namespace TypeScript.LsConv;

/// <summary>
/// Go: <c>type MappedSpan[T Script] struct</c>（converters.go:24）：原文区间的一个虚拟投影（<see cref="Span"/> /
/// <see cref="Fidelity"/> 来自嵌入的 <c>spanmap.MappedSpan</c>）。
/// </summary>
public readonly struct MappedSpan<T> where T : Script
{
    public readonly T Script;
    public readonly TextRange Span;
    public readonly Fidelity Fidelity;

    public MappedSpan(T script, TextRange span, Fidelity fidelity)
    {
        Script = script;
        Span = span;
        Fidelity = fidelity;
    }

    /// <summary>Go 里用嵌入字段写的 `MappedSpan[T]{Script: s, MappedSpan: m}` 的对应构造。</summary>
    public MappedSpan(T script, SpanMapNs.MappedSpan mapped)
        : this(script, mapped.Span, mapped.Fidelity)
    {
    }
}

/// <summary>Go: <c>type MappedPosition[T Script] struct</c>（converters.go:30）。</summary>
public readonly struct MappedPosition<T> where T : Script
{
    public readonly T Script;
    public readonly TextPos Position;
    public readonly Fidelity Fidelity;

    public MappedPosition(T script, TextPos position, Fidelity fidelity)
    {
        Script = script;
        Position = position;
        Fidelity = fidelity;
    }

    /// <summary>Go 里用嵌入字段写的 `MappedPosition[T]{Script: s, MappedPosition: m}` 的对应构造。</summary>
    public MappedPosition(T script, SpanMapNs.MappedPosition mapped)
        : this(script, mapped.Position, mapped.Fidelity)
    {
    }
}

/// <summary>Go: <c>type Converters struct</c>（converters.go:20）。</summary>
public sealed class Converters
{
    private readonly Func<string, LSPLineMap?> _getLineMap;
    private readonly PositionEncodingKind _positionEncoding;

    public Converters(PositionEncodingKind positionEncoding, Func<string, LSPLineMap?> getLineMap)
    {
        _getLineMap = getLineMap;
        _positionEncoding = positionEncoding;
    }

    /// <summary>
    /// Go: <c>func (c *Converters) ToLSPRange(script Script, textRange core.TextRange) (lsproto.Range, spanmap.Fidelity)</c>
    /// （converters.go:62-67）。内容映射过的虚拟 SourceFile 会先过 span map（并报告映射忠实度）；普通文件返回
    /// <c>FidelityExact</c>。
    /// </summary>
    public (Range Range, Fidelity Fidelity) ToLSPRange(Script script, TextRange textRange)
    {
        var (mappedScript, mappedRange, fidelity) = LsConv.VirtualRangeToOriginal(script, textRange, null);
        return (new Range
        {
            Start = PositionToLineAndCharacter(mappedScript, new TextPos(mappedRange.Pos())),
            End = PositionToLineAndCharacter(mappedScript, new TextPos(mappedRange.End())),
        }, fidelity);
    }

    /// <summary>
    /// Go: <c>func (c *Converters) ToLSPRangeForFeature(script Script, textRange core.TextRange, feature spanmap.Feature)</c>
    /// （converters.go:72-79）。内容映射文件：只有当整个虚拟区间都被参与 <paramref name="feature"/> 的连续段覆盖时才返回
    /// <c>FidelityExact</c>，否则返回尽力映射的结果 + 更低的忠实度。
    /// </summary>
    public (Range Range, Fidelity Fidelity) ToLSPRangeForFeature(Script script, TextRange textRange, Feature feature)
    {
        var (mappedScript, mappedRange, fidelity) = LsConv.VirtualRangeToOriginal(script, textRange, feature);
        return (new Range
        {
            Start = PositionToLineAndCharacter(mappedScript, new TextPos(mappedRange.Pos())),
            End = PositionToLineAndCharacter(mappedScript, new TextPos(mappedRange.End())),
        }, fidelity);
    }

    /// <summary>Go: <c>func (c *Converters) ToLSPPosition(script Script, position core.TextPos) (lsproto.Position, spanmap.Fidelity)</c>（converters.go:84-87）。</summary>
    public (Position Position, Fidelity Fidelity) ToLSPPosition(Script script, TextPos position)
    {
        var (mappedScript, mappedPosition, fidelity) = LsConv.VirtualPositionToOriginal(script, position, null);
        return (PositionToLineAndCharacter(mappedScript, mappedPosition), fidelity);
    }

    /// <summary>Go: <c>func (c *Converters) ToLSPPositionForFeature(script Script, position core.TextPos, feature spanmap.Feature)</c>（converters.go:92-95）。</summary>
    public (Position Position, Fidelity Fidelity) ToLSPPositionForFeature(Script script, TextPos position, Feature feature)
    {
        var (mappedScript, mappedPosition, fidelity) = LsConv.VirtualPositionToOriginal(script, position, feature);
        return (PositionToLineAndCharacter(mappedScript, mappedPosition), fidelity);
    }

    /// <summary>
    /// Go: <c>func (c *Converters) ToLSPLocation(script Script, rng core.TextRange) (lsproto.Location, spanmap.Fidelity)</c>
    /// （converters.go:101-107）。内容映射器的补充输出文件用**原文文件名**做 URI（App.astro.0.ts → App.astro）。
    /// </summary>
    public (Location Location, Fidelity Fidelity) ToLSPLocation(Script script, TextRange rng)
    {
        var (lspRange, fidelity) = ToLSPRange(script, rng);
        return (new Location
        {
            Uri = LsConv.FileNameToDocumentURI(script.OriginalFileName()),
            Range = lspRange,
        }, fidelity);
    }

    /// <summary>Go: <c>func (c *Converters) ToLSPLocationForFeature(script Script, rng core.TextRange, feature spanmap.Feature)</c>（converters.go:113-116）。</summary>
    public (Location Location, Fidelity Fidelity) ToLSPLocationForFeature(Script script, TextRange rng, Feature feature)
    {
        var (lspRange, fidelity) = ToLSPRangeForFeature(script, rng, feature);
        return (new Location
        {
            Uri = LsConv.FileNameToDocumentURI(script.OriginalFileName()),
            Range = lspRange,
        }, fidelity);
    }

    /// <summary>
    /// Go: <c>func (c *Converters) FromLSPRange[T Script](script T, textRange lsproto.Range, feature spanmap.Feature) []MappedSpan[T]</c>
    /// （converters.go:122-124）。
    /// </summary>
    public MappedSpan<T>[] FromLSPRange<T>(T script, Range textRange, Feature feature) where T : Script
        => LspRangeToVirtualForScripts([script], textRange, feature);

    /// <summary>
    /// Go: <c>func (c *Converters) FromLSPRangeForSourceFile(file *ast.SourceFile, textRange lsproto.Range, feature spanmap.Feature) []MappedSpan[*ast.SourceFile]</c>
    /// （converters.go:130-133）：跨"规范文件 + 全部补充文件"投影。
    /// </summary>
    public MappedSpan<SourceFile>[] FromLSPRangeForSourceFile(SourceFile file, Range textRange, Feature feature)
        => LspRangeToVirtualForScripts(LsConv.SourceFileProjections(file), textRange, feature);

    /// <summary>
    /// Go: <c>func (c *Converters) FromLSPRangeIntersectingForSourceFile(...)</c>（converters.go:149-175）：
    /// 取与 <paramref name="textRange"/> **相交**的、参与 <paramref name="feature"/> 的所有投影（只读范围请求用，
    /// 例如语义高亮 / inlay hints：视口两端可能都落在脚本段之外）。
    /// </summary>
    public MappedSpan<SourceFile>[] FromLSPRangeIntersectingForSourceFile(SourceFile file, Range textRange, Feature feature)
    {
        var files = LsConv.SourceFileProjections(file);
        var result = new List<MappedSpan<SourceFile>>(files.Length);
        foreach (var script in files)
        {
            var spans = script.SpanMap();
            if (spans is null)
            {
                result.Add(new MappedSpan<SourceFile>(
                    script,
                    NewTextRange(
                        LineAndCharacterToPosition(script, textRange.Start),
                        LineAndCharacterToPosition(script, textRange.End)),
                    Fidelity.Exact));
                continue;
            }

            var original = new OriginalTextScript(script.OriginalFileName(), script.OriginalText());
            var originalRange = NewTextRange(
                LineAndCharacterToPosition(original, textRange.Start),
                LineAndCharacterToPosition(original, textRange.End));
            foreach (var mapped in spans.OriginalToVirtualIntersectingSpans(originalRange, feature))
            {
                result.Add(new MappedSpan<SourceFile>(script, mapped));
            }
        }

        return result.ToArray();
    }

    private MappedSpan<T>[] LspRangeToVirtualForScripts<T>(T[] scripts, Range textRange, Feature feature) where T : Script
    {
        var result = new List<MappedSpan<T>>(scripts.Length);
        foreach (var script in scripts)
        {
            foreach (var mapped in LspRangeToVirtual(script, textRange, feature))
            {
                result.Add(new MappedSpan<T>(script, mapped));
            }
        }

        return result.ToArray();
    }

    private SpanMapNs.MappedSpan[] LspRangeToVirtual(Script script, Range textRange, Feature feature)
    {
        var spans = script.SpanMap();
        if (spans is null)
        {
            return
            [
                new SpanMapNs.MappedSpan(
                    NewTextRange(
                        LineAndCharacterToPosition(script, textRange.Start),
                        LineAndCharacterToPosition(script, textRange.End)),
                    Fidelity.Exact),
            ];
        }

        // 内容映射文件的 line map 是**原文**的，所以先在原文上换算，再把原文区间正向映射回虚拟文本。
        var original = new OriginalTextScript(script.OriginalFileName(), script.OriginalText());
        var origRange = NewTextRange(
            LineAndCharacterToPosition(original, textRange.Start),
            LineAndCharacterToPosition(original, textRange.End));
        return spans.OriginalToVirtualSpans(origRange, feature);
    }

    /// <summary>
    /// Go: <c>func (c *Converters) FromLSPPosition[T Script](script T, position lsproto.Position, feature spanmap.Feature) []MappedPosition[T]</c>
    /// （converters.go:181-183）。
    /// </summary>
    public MappedPosition<T>[] FromLSPPosition<T>(T script, Position position, Feature feature) where T : Script
        => LspPositionToVirtualForScripts([script], position, feature);

    /// <summary>Go: <c>func (c *Converters) FromLSPPositionForSourceFile(...)</c>（converters.go:189-192）。</summary>
    public MappedPosition<SourceFile>[] FromLSPPositionForSourceFile(SourceFile file, Position position, Feature feature)
        => LspPositionToVirtualForScripts(LsConv.SourceFileProjections(file), position, feature);

    /// <summary>Go: <c>func (c *Converters) FromLSPRangeToOriginal(script Script, textRange lsproto.Range) core.TextRange</c>（converters.go:196-202）：把内容映射文档里的 LSP 区间直接换算成**原文**偏移。</summary>
    public TextRange FromLSPRangeToOriginal(Script script, Range textRange)
    {
        var original = new OriginalTextScript(script.OriginalFileName(), script.OriginalText());
        return NewTextRange(
            LineAndCharacterToPosition(original, textRange.Start),
            LineAndCharacterToPosition(original, textRange.End));
    }

    private MappedPosition<T>[] LspPositionToVirtualForScripts<T>(T[] scripts, Position position, Feature feature) where T : Script
    {
        var result = new List<MappedPosition<T>>(scripts.Length);
        foreach (var script in scripts)
        {
            foreach (var mapped in LspPositionToVirtual(script, position, feature))
            {
                result.Add(new MappedPosition<T>(script, mapped));
            }
        }

        return result.ToArray();
    }

    private SpanMapNs.MappedPosition[] LspPositionToVirtual(Script script, Position position, Feature feature)
    {
        var spans = script.SpanMap();
        if (spans is null)
        {
            return [new SpanMapNs.MappedPosition(LineAndCharacterToPosition(script, position), Fidelity.Exact)];
        }

        var original = new OriginalTextScript(script.OriginalFileName(), script.OriginalText());
        var origOffset = LineAndCharacterToPosition(original, position);
        return spans.OriginalToVirtualPositions(origOffset, feature);
    }

    /// <summary>
    /// Go: <c>func (c *Converters) lineAndCharacterToPosition(script Script, lineAndCharacter lsproto.Position) core.TextPos</c>
    /// （converters.go:363-412）。UTF-8/16 的 0-indexed 行列 → 本仓内部坐标（UTF-16 代码单元）。
    /// </summary>
    private TextPos LineAndCharacterToPosition(Script script, Position lineAndCharacter)
    {
        // Go: raw coordinate conversion requires a non-content-mapped script
        Debug.Assert(script.SpanMap() is null, "raw coordinate conversion requires a non-content-mapped script");

        var lineMap = _getLineMap(script.FileName())!;

        var line = (int)lineAndCharacter.Line;
        var character = (int)lineAndCharacter.Character;
        var textLen = script.Text().Length;

        // 行号超界 → 文本末尾（Go 的同一钳制）。
        if (line >= lineMap.LineStarts.Length)
        {
            return new TextPos(textLen);
        }

        var start = (int)lineMap.LineStarts[line];

        // 本行末尾（下一行行首，或文本末尾）。
        var lineEnd = line + 1 < lineMap.LineStarts.Length ? (int)lineMap.LineStarts[line + 1] : textLen;

        if (lineMap.AsciiOnly)
        {
            // 纯 ASCII：字节偏移 == 代码单元偏移，直接钳制。
            return new TextPos(Math.Max(start, Math.Min(start + character, lineEnd)));
        }

        var text = script.Text();
        if (_positionEncoding == PositionEncodingKind.UTF8)
        {
            // 客户端按 UTF-8 字节计偏移：按码点累加 UTF-8 字节数走到目标字节处。
            return new TextPos(Utf8LineOffsetToCodeUnit(text, start, lineEnd, character));
        }

        // UTF-16 position encoding：按码点累加 UTF-16 长度走到 character 处（不切进代理对中间）。
        // Go 用的是 DecodeRuneInString + RuneLen；C# 的 DecodeRuneAt 的 size 就是 UTF-16 长度。
        var utf16Char = 0;
        var pos = start;
        while (pos < lineEnd)
        {
            Scan.DecodeRuneAt(text, pos, out var size);
            if (utf16Char + size > character)
            {
                break;
            }

            utf16Char += size;
            pos += size;
        }

        return new TextPos(pos);
    }

    /// <summary>
    /// Go: <c>func (c *Converters) positionToLineAndCharacter(script Script, position core.TextPos) lsproto.Position</c>
    /// （converters.go:414-449）。本仓内部坐标（UTF-16 代码单元）→ UTF-8/16 的 0-indexed 行列。
    /// </summary>
    private Position PositionToLineAndCharacter(Script script, TextPos position)
    {
        // Go: raw coordinate conversion requires a non-content-mapped script
        Debug.Assert(script.SpanMap() is null, "raw coordinate conversion requires a non-content-mapped script");

        var clamped = Math.Max(0, Math.Min((int)position, script.Text().Length));

        var lineMap = _getLineMap(script.FileName())!;
        var (line, isLineStart) = LsConv.BinarySearchLineStarts(lineMap.LineStarts.Items, new TextPos(clamped));
        if (!isLineStart)
        {
            line--;
        }

        line = Math.Max(0, Math.Min(line, lineMap.LineStarts.Length - 1));

        // 本行范围是 lineMap.LineStarts[line] … LineStarts[line+1]（或文本末尾）。
        var start = (int)lineMap.LineStarts[line];

        int character;
        if (lineMap.AsciiOnly)
        {
            character = clamped - start;
        }
        else if (_positionEncoding == PositionEncodingKind.UTF8)
        {
            // Go 里 position/start 都是 UTF-8 字节下标，相减即字节数；C# 里两者是 UTF-16 下标，
            // 必须按码点累加 UTF-8 字节长度。
            character = CodeUnitToUtf8LineOffset(script.Text(), start, clamped);
        }
        else
        {
            // Go 的 UTF-16 分支要在 UTF-8 字节串上重扫累加 RuneLen；C# 的 string 本身就是 UTF-16 代码单元
            // 序列，`clamped - start` 就是 UTF-16 长度，两个分支因此等价。
            character = clamped - start;
        }

        return new Position { Line = (uint)line, Character = (uint)character };
    }

    /// <summary>行内 UTF-8 字节偏移（客户端坐标）→ 内部 UTF-16 代码单元下标。</summary>
    private static int Utf8LineOffsetToCodeUnit(string text, int lineStart, int lineEnd, int utf8Offset)
    {
        var bytes = 0;
        var pos = lineStart;
        while (pos < lineEnd)
        {
            var rune = Scan.DecodeRuneAt(text, pos, out var size);
            var byteLen = Utf8ByteLength(rune, size);
            if (bytes + byteLen > utf8Offset)
            {
                break;
            }

            bytes += byteLen;
            pos += size;
        }

        return pos;
    }

    /// <summary>内部 UTF-16 代码单元下标 → 行内 UTF-8 字节偏移（客户端坐标）。</summary>
    private static int CodeUnitToUtf8LineOffset(string text, int lineStart, int position)
    {
        var bytes = 0;
        var pos = lineStart;
        while (pos < position)
        {
            var rune = Scan.DecodeRuneAt(text, pos, out var size);
            if (pos + size > position)
            {
                // Go 用 `for _, r := range text[start:position]`，落在码点中间的端点不会被计入。
                break;
            }

            bytes += Utf8ByteLength(rune, size);
            pos += size;
        }

        return bytes;
    }

    /// <summary>Go 的 <c>utf8.RuneLen(r)</c>（这里是"该码点在 UTF-8 下占几字节"）。</summary>
    private static int Utf8ByteLength(int rune, int size)
    {
        // Go 的源文本是 UTF-8：非法字节（这里对应孤立代理项）DecodeRuneInString 返回 (RuneError, 1)，
        // 即按 1 字节前进；C# 的 string 可以把孤立代理项原样存下来，所以照 1 字节算。
        if (size == 1 && rune is >= 0xD800 and <= 0xDFFF)
        {
            return 1;
        }

        return rune switch
        {
            < 0x80 => 1,
            < 0x800 => 2,
            < 0x10000 => 3,
            _ => 4,
        };
    }
}

/// <summary>
/// Go: <c>type originalTextScript struct</c>（converters.go:573-585，未导出 → C# 内部类型、PascalCase）：
/// 把内容映射文件的**原文**当作一个 Script，这样"已经映射到原文里的区间"能换算成正确的行列。
/// </summary>
internal sealed class OriginalTextScript(string fileName, string text) : Script
{
    public string FileName() => fileName;

    public string OriginalFileName() => fileName;

    public string Text() => text;

    public string OriginalText() => text;

    public SpanMapType? SpanMap() => null;
}

public static partial class LsConv
{
    /// <summary>Go: <c>func NewConverters(positionEncoding lsproto.PositionEncodingKind, getLineMap func(fileName string) *LSPLineMap) *Converters</c>（converters.go:52-60）。</summary>
    public static Converters NewConverters(PositionEncodingKind positionEncoding, Func<string, LSPLineMap?> getLineMap)
        => new(positionEncoding, getLineMap);

    /// <summary>Go: <c>func sourceFileProjections(file *ast.SourceFile) []*ast.SourceFile</c>（converters.go:204-209）。</summary>
    internal static SourceFile[] SourceFileProjections(SourceFile file)
    {
        var supplemental = file.SupplementalSourceFiles();
        var files = new SourceFile[1 + supplemental.Length];
        files[0] = file;
        supplemental.CopyTo(files, 1);
        return files;
    }

    /// <summary>
    /// Go: <c>func virtualRangeToOriginal(script Script, textRange core.TextRange, feature *spanmap.Feature) (Script, core.TextRange, spanmap.Fidelity)</c>
    /// （converters.go:282-295）。feature 为 nil 表示绕过特征过滤（诊断与编辑用）。
    /// </summary>
    internal static (Script Script, TextRange TextRange, Fidelity Fidelity) VirtualRangeToOriginal(Script script, TextRange textRange, Feature? feature)
    {
        if (script.SpanMap() is null)
        {
            return (script, textRange, Fidelity.Exact);
        }

        TextRange mapped;
        Fidelity fidelity;
        if (feature is null)
        {
            (mapped, fidelity) = script.SpanMap()!.VirtualToOriginalSpan(textRange);
        }
        else
        {
            (mapped, fidelity) = script.SpanMap()!.VirtualToOriginalSpanForFeature(textRange, feature.Value);
        }

        return (new OriginalTextScript(script.OriginalFileName(), script.OriginalText()), mapped, fidelity);
    }

    /// <summary>Go: <c>func virtualPositionToOriginal(script Script, position core.TextPos, feature *spanmap.Feature) (Script, core.TextPos, spanmap.Fidelity)</c>（converters.go:297-310）。</summary>
    internal static (Script Script, TextPos Position, Fidelity Fidelity) VirtualPositionToOriginal(Script script, TextPos position, Feature? feature)
    {
        if (script.SpanMap() is null)
        {
            return (script, position, Fidelity.Exact);
        }

        TextPos mapped;
        Fidelity fidelity;
        if (feature is null)
        {
            (mapped, fidelity) = script.SpanMap()!.VirtualToOriginalPosition(position);
        }
        else
        {
            (mapped, fidelity) = script.SpanMap()!.VirtualToOriginalPositionForFeature(position, feature.Value);
        }

        return (new OriginalTextScript(script.OriginalFileName(), script.OriginalText()), mapped, fidelity);
    }

    /// <summary>Go: <c>func LanguageKindToScriptKind(languageID lsproto.LanguageKind) core.ScriptKind</c>（converters.go:312-327）。</summary>
    public static ScriptKind LanguageKindToScriptKind(LanguageKind languageID)
    {
        if (languageID == LanguageKind.TypeScript)
        {
            return ScriptKind.TS;
        }

        if (languageID == LanguageKind.TypeScriptReact)
        {
            return ScriptKind.TSX;
        }

        if (languageID == LanguageKind.JavaScript)
        {
            return ScriptKind.JS;
        }

        if (languageID == LanguageKind.JavaScriptReact)
        {
            return ScriptKind.JSX;
        }

        if (languageID == LanguageKind.JSON)
        {
            return ScriptKind.JSON;
        }

        return ScriptKind.Unknown;
    }

    /// <summary>
    /// Go: <c>func FileNameToDocumentURI(fileName string) lsproto.DocumentUri</c>（converters.go:352-379）。
    /// 转义规则照 vscode-uri（见 Go 里的注释链接）。
    /// </summary>
    public static DocumentUri FileNameToDocumentURI(string fileName)
    {
        if (IsBundled(fileName))
        {
            return new DocumentUri(fileName);
        }

        if (IsDynamicFileName(fileName))
        {
            var rest0 = fileName[2..];
            var slash0 = rest0.IndexOf('/');
            if (slash0 < 0)
            {
                Debug.Fail("invalid file name: " + fileName);
            }

            var scheme = rest0[..slash0];
            var rest = rest0[(slash0 + 1)..];
            var slash1 = rest.IndexOf('/');
            if (slash1 < 0)
            {
                Debug.Fail("invalid file name: " + fileName);
            }

            var authority = rest[..slash1];
            var path = rest[(slash1 + 1)..];
            if (authority == "ts-nul-authority")
            {
                return new DocumentUri(scheme + ":" + path);
            }

            return new DocumentUri(scheme + "://" + authority + "/" + path);
        }

        TrySplitVolumePath(fileName, out var volume, out fileName);
        if (volume != "")
        {
            volume = "/" + ExtraEscape(volume);
        }

        if (fileName.StartsWith("//", StringComparison.Ordinal))
        {
            fileName = fileName[2..];
        }

        var parts = fileName.Split('/');
        for (var i = 0; i < parts.Length; i++)
        {
            parts[i] = ExtraEscape(PathEscape(parts[i]));
        }

        return new DocumentUri("file://" + volume + string.Join('/', parts));
    }

    /// <summary>
    /// Go 的 <c>extraEscapeReplacer</c>（<c>strings.NewReplacer</c>，converters.go:330-350）：
    /// 逐个 <c>string.Replace</c> 等价（替换键是单个非字母数字字符，替换值只含 <c>%</c>/数字/大写字母，互不包含）。
    /// </summary>
    private static string ExtraEscape(string s)
    {
        return s
            .Replace(":", "%3A", StringComparison.Ordinal)
            .Replace("/", "%2F", StringComparison.Ordinal)
            .Replace("?", "%3F", StringComparison.Ordinal)
            .Replace("#", "%23", StringComparison.Ordinal)
            .Replace("[", "%5B", StringComparison.Ordinal)
            .Replace("]", "%5D", StringComparison.Ordinal)
            .Replace("@", "%40", StringComparison.Ordinal)
            .Replace("!", "%21", StringComparison.Ordinal)
            .Replace("$", "%24", StringComparison.Ordinal)
            .Replace("&", "%26", StringComparison.Ordinal)
            .Replace("'", "%27", StringComparison.Ordinal)
            .Replace("(", "%28", StringComparison.Ordinal)
            .Replace(")", "%29", StringComparison.Ordinal)
            .Replace("*", "%2A", StringComparison.Ordinal)
            .Replace("+", "%2B", StringComparison.Ordinal)
            .Replace(",", "%2C", StringComparison.Ordinal)
            .Replace(";", "%3B", StringComparison.Ordinal)
            .Replace("=", "%3D", StringComparison.Ordinal)
            .Replace(" ", "%20", StringComparison.Ordinal);
    }

    private const string _upperHex = "0123456789ABCDEF";

    /// <summary>
    /// Go 的 <c>url.PathEscape</c>（<c>escape(s, encodePathSegment)</c>）：按 UTF-8 字节处理，
    /// 只保留 §2.3 的非保留字符与 §2.2 里 path segment 允许的保留字符（<c>$&amp;+: =@</c>），其余逐字节 <c>%XX</c>。
    /// </summary>
    private static string PathEscape(string s)
    {
        var bytes = Encoding.UTF8.GetBytes(s);
        var needsEscape = false;
        foreach (var b in bytes)
        {
            if (ShouldEscapePathSegment(b))
            {
                needsEscape = true;
                break;
            }
        }

        if (!needsEscape)
        {
            return s;
        }

        var sb = new StringBuilder(bytes.Length);
        foreach (var b in bytes)
        {
            if (ShouldEscapePathSegment(b))
            {
                sb.Append('%').Append(_upperHex[b >> 4]).Append(_upperHex[b & 0xF]);
            }
            else
            {
                sb.Append((char)b);
            }
        }

        return sb.ToString();
    }

    private static bool ShouldEscapePathSegment(byte c)
    {
        // §2.3 Unreserved characters（字母数字）。
        if (c is >= (byte)'a' and <= (byte)'z' or >= (byte)'A' and <= (byte)'Z' or >= (byte)'0' and <= (byte)'9')
        {
            return false;
        }

        // §2.3 Unreserved characters（mark）。
        if (c is (byte)'-' or (byte)'_' or (byte)'.' or (byte)'~')
        {
            return false;
        }

        // §2.2 Reserved characters：encodePathSegment 下只转义 / ; , ?。
        if (c is (byte)'$' or (byte)'&' or (byte)'+'
            or (byte)',' or (byte)'/' or (byte)':'
            or (byte)';' or (byte)'=' or (byte)'?'
            or (byte)'@')
        {
            return c is (byte)'/' or (byte)';' or (byte)',' or (byte)'?';
        }

        return true;
    }

    /// <summary>Go: <c>func DiagnosticToLSPPull(ctx context.Context, converters *Converters, diagnostic *ast.Diagnostic, reportStyleChecksAsWarnings bool) *lsproto.Diagnostic</c>（converters.go:459-468）。<c>textDocument/diagnostic</c> 用。</summary>
    public static LspDiag DiagnosticToLSPPull(Context ctx, Converters converters, AstDiag diagnostic, bool reportStyleChecksAsWarnings)
    {
        // C# 的 GetClientCapabilities 不带 ctx（LsProto 用 AsyncLocal，见其文件头第 4 条）。
        var clientCaps = LsProto.LsProto.GetClientCapabilities();
        var clientDiagnosticCaps = clientCaps.TextDocument.Diagnostic;
        return DiagnosticToLSP(ctx, converters, diagnostic, new DiagnosticOptions
        {
            _reportStyleChecksAsWarnings = reportStyleChecksAsWarnings, // !!! get through context UserPreferences
            _relatedInformation = clientDiagnosticCaps.RelatedInformation,
            _tagValueSet = clientDiagnosticCaps.TagSupport.ValueSet,
            _visualStudio = clientCaps.VSSupportsVisualStudioExtensions,
        });
    }

    /// <summary>Go: <c>func DiagnosticToLSPPush(ctx context.Context, converters *Converters, diagnostic *ast.Diagnostic) *lsproto.Diagnostic</c>（converters.go:471-478）。<c>textDocument/publishDiagnostics</c> 用。</summary>
    public static LspDiag DiagnosticToLSPPush(Context ctx, Converters converters, AstDiag diagnostic)
    {
        var clientCaps = LsProto.LsProto.GetClientCapabilities();
        var clientDiagnosticCaps = clientCaps.TextDocument.PublishDiagnostics;
        return DiagnosticToLSP(ctx, converters, diagnostic, new DiagnosticOptions
        {
            _relatedInformation = clientDiagnosticCaps.RelatedInformation,
            _tagValueSet = clientDiagnosticCaps.TagSupport.ValueSet,
            _visualStudio = clientCaps.VSSupportsVisualStudioExtensions,
        });
    }

    /// <summary>Go: <c>type diagnosticOptions struct</c>（converters.go:451-456，未导出 → private + `_camelCase` 字段）。</summary>
    private sealed class DiagnosticOptions
    {
        internal bool _reportStyleChecksAsWarnings;
        internal bool _relatedInformation;
        internal DiagnosticTag[]? _tagValueSet;
        internal bool _visualStudio;
    }

    /// <summary>
    /// Go: <c>var styleCheckDiagnostics = collections.NewSetFromItems(...)</c>（converters.go:483-492）：
    /// vscode 把这几条诊断当"风格检查"处理（可被 reportStyleChecksAsWarnings 降级成 warning）。
    /// </summary>
    private static readonly Set<int> _styleCheckDiagnostics = NewSetFromItems(
        X_0_is_declared_but_never_used.Code(),
        X_0_is_declared_but_its_value_is_never_read.Code(),
        Property_0_is_declared_but_its_value_is_never_read.Code(),
        All_imports_in_import_declaration_are_unused.Code(),
        Unreachable_code_detected.Code(),
        Unused_label.Code(),
        Fallthrough_case_in_switch.Code(),
        Not_all_code_paths_return_a_value.Code());

    /// <summary>Go: <c>func diagnosticToLSP(ctx context.Context, converters *Converters, diagnostic *ast.Diagnostic, opts diagnosticOptions) *lsproto.Diagnostic</c>（converters.go:494-563）。</summary>
    private static LspDiag DiagnosticToLSP(Context ctx, Converters converters, AstDiag diagnostic, DiagnosticOptions opts)
    {
        var locale = Loc.FromContext(ctx);
        var severity = DiagnosticSeverityFor(diagnostic.Category());

        if (opts._reportStyleChecksAsWarnings && severity == DiagnosticSeverity.Error && _styleCheckDiagnostics.Has(diagnostic.Code()))
        {
            severity = DiagnosticSeverity.Warning;
        }

        var relatedInformation = new List<DiagnosticRelatedInformation>();
        if (opts._relatedInformation)
        {
            foreach (var related in diagnostic.RelatedInformation() ?? [])
            {
                var (script, loc) = DiagnosticScriptAndRange(related.File(), related.Loc(), related.Source());
                var (relatedRange, fidelity) = converters.ToLSPRange(script!, loc);
                if (fidelity.IsNone())
                {
                    // 关联诊断信息不能省略位置：显式给"整个文件"（零区间），而不是把合成区间的插入点当作关联源码。
                    relatedRange = new Range();
                }

                relatedInformation.Add(new DiagnosticRelatedInformation
                {
                    Location = new Location
                    {
                        Uri = FileNameToDocumentURI(related.File()!.OriginalFileName()),
                        Range = relatedRange,
                    },
                    Message = related.Localize(locale),
                });
            }
        }

        var tags = new List<DiagnosticTag>();
        if (opts._tagValueSet is { Length: > 0 } && (diagnostic.ReportsUnnecessary() || diagnostic.ReportsDeprecated()))
        {
            if (diagnostic.ReportsUnnecessary() && Array.IndexOf(opts._tagValueSet, DiagnosticTag.Unnecessary) >= 0)
            {
                tags.Add(DiagnosticTag.Unnecessary);
            }

            if (diagnostic.ReportsDeprecated() && Array.IndexOf(opts._tagValueSet, DiagnosticTag.Deprecated) >= 0)
            {
                tags.Add(DiagnosticTag.Deprecated);
            }
        }

        // 没有文件的诊断（例如程序级诊断）用零区间。
        var lspRange = new Range();
        if (diagnostic.File() is not null)
        {
            var (script, loc) = DiagnosticScriptAndRange(diagnostic.File(), diagnostic.Loc(), diagnostic.Source());
            var (mappedRange, fidelity) = converters.ToLSPRange(script!, loc);
            lspRange = mappedRange;
            if (fidelity.IsNone())
            {
                // 诊断必须带区间。整段都出自合成代码、没有原文来源时，零区间诚实地表示"就是这个文件"。
                lspRange = new Range();
            }
        }

        IntegerOrString code;
        var sourceText = diagnostic.Source();
        if (sourceText == "")
        {
            sourceText = "ts";
        }

        if (opts._visualStudio)
        {
            code = new IntegerOrString
            {
                String = "TS" + diagnostic.Code().ToString(CultureInfo.InvariantCulture),
            };
        }
        else
        {
            code = new IntegerOrString { Integer = diagnostic.Code() };
        }

        return new LspDiag
        {
            Range = lspRange,
            Code = code,
            Severity = severity,
            Message = new StringOrMarkupContent { String = MessageChainToString(diagnostic, locale) },
            Source = sourceText,
            RelatedInformation = PtrToSliceIfNonEmpty(relatedInformation.ToArray()),
            Tags = PtrToSliceIfNonEmpty(tags.ToArray()),
        };
    }

    /// <summary>
    /// Go: <c>func diagnosticScriptAndRange(file *ast.SourceFile, loc core.TextRange, source string) (Script, core.TextRange)</c>
    /// （converters.go:565-583）。内容映射文件先把虚拟区间映射回原文（编辑器看到的就是原文）；
    /// 合成代码里的区间没有原文对应物，落到文件顶部。非映射文件原样返回。
    /// 返回的 <see cref="Script"/> 在 <paramref name="file"/> 为 null 时也是 null（Go 返回 nil 的 *SourceFile 接口值，
    /// 调用点会解引用它 —— 与 Go 一样不做容错）。
    /// </summary>
    private static (Script? Script, TextRange Loc) DiagnosticScriptAndRange(SourceFile? file, TextRange loc, string source)
    {
        if (file is null || file.SpanMap() is null)
        {
            return (file, loc);
        }

        var original = new OriginalTextScript(file.OriginalFileName(), file.OriginalText());
        if (source != "")
        {
            // 内容映射器自己的诊断带的就是原文区间。
            return (original, loc);
        }

        var (mapped, fidelity) = file.SpanMap()!.VirtualToOriginalSpan(loc);
        if (fidelity == Fidelity.None)
        {
            // 整段都是合成代码：没有原文位置，落到文件顶部。
            return (original, NewTextRange(0, 0));
        }

        return (original, mapped);
    }

    /// <summary>
    /// Go: <c>func diagnosticSeverity(category diagnostics.Category) lsproto.DiagnosticSeverity</c>（converters.go:587-598）。
    /// C# 里不能叫 `DiagnosticSeverity`（与返回类型同名会让 `DiagnosticSeverity.Error` 解析到方法组），故加 `For` 后缀。
    /// </summary>
    private static DiagnosticSeverity DiagnosticSeverityFor(Category category)
    {
        if (category == Category.Suggestion)
        {
            return DiagnosticSeverity.Hint;
        }

        if (category == Category.Message)
        {
            return DiagnosticSeverity.Information;
        }

        if (category == Category.Warning)
        {
            return DiagnosticSeverity.Warning;
        }

        return DiagnosticSeverity.Error;
    }

    /// <summary>Go: <c>func messageChainToString(diagnostic *ast.Diagnostic, locale locale.Locale) string</c>（converters.go:600-607）。</summary>
    private static string MessageChainToString(AstDiag diagnostic, Loc locale)
    {
        if (diagnostic.MessageChain() is not { Length: > 0 })
        {
            return diagnostic.Localize(locale);
        }

        var b = new StringWriter();
        WriteFlattenedASTDiagnosticMessage(b, diagnostic, "\n", locale);
        return b.ToString();
    }

    /// <summary>Go: <c>func ptrToSliceIfNonEmpty[T any](s []T) *[]T</c>（converters.go:609-614）：空切片 → nil。</summary>
    private static T[]? PtrToSliceIfNonEmpty<T>(T[]? s)
        => s is { Length: > 0 } ? s : null;
}
