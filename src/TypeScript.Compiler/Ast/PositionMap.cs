// ported from: internal/ast/positionmap.go
//
// **重要偏离**（C# 语言特性，依据 docs/architecture.md §3.1）：
// Go 的 PositionMap 提供 "UTF-8 字节偏移 ↔ UTF-16 代码单元偏移" 的双向映射，因为 Go 的 string 是 UTF-8
// 字节序列，而 TS 的 Pos 语义是 UTF-16 代码单元。C# 的 string 本身就是 UTF-16 代码单元序列（与 JS 字符串
// 同构），位置就是 UTF-16 下标，两种偏移**恒等**：
// - `ComputePositionMap` 不再扫描文本（结果永远是"全 ASCII"的恒等映射）；
// - Go 依赖的 `stringutil.DecodeJSStringRune` / `EncodeJSStringRune` 不移植（§3.1 明确不移植哨兵机制）；
// - Go 的私有字段 `asciiOnly`/`entries` 与私有类型 `positionMapEntry` 只有"字节↔代码单元换算"一个用途，
//   随之删除（不再有需要查表的下标）。
// 保留本类型的唯一目的：让 M4+ 的调用点（`SourceFile.GetPositionMap()`、`file.GetPositionOfLineAndCharacter`
// 等）能逐字移植；它们是**恒等**方法，行为与 Go 的 asciiOnly 快路径完全一致。
namespace TypeScript.Ast;

public sealed class PositionMap
{
    public bool IsAsciiOnly() => true;

    public int UTF8ToUTF16(int utf8Offset) => utf8Offset;

    public int UTF16ToUTF8(int utf16Offset) => utf16Offset;
}

public static partial class Ast
{
    public static PositionMap ComputePositionMap(string text) => new();
}
