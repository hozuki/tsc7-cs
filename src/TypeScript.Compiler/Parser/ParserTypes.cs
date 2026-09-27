// ported from: internal/parser/types.go:1-14（ParseFlags；W6 分片，整文件）
//
// 与 Go 的偏离（逐条）：
// 1) Go 的 `type ParseFlags uint32` + 无类型常量 → C# `[Flags] enum ParseFlags : uint`（成员名去 `ParseFlags`
//    前缀，数值与 Go 逐位一致，见 docs/architecture.md §3.3）。位 3（1<<3）在 Go 里本来就没有常量，照留空。
// 2) 类型本身在 Go 里是导出的（`ParseFlags`），故 C# 保持 `public`（docs §2 的 API 对齐要求）。
using System;

namespace TypeScript.Parser;

/// <summary>Go: <c>type ParseFlags uint32</c>。</summary>
[Flags]
public enum ParseFlags : uint
{
    None = 0,
    Yield = 1 << 0,
    Await = 1 << 1,
    Type = 1 << 2,
    IgnoreMissingOpenBrace = 1 << 4,
    JSDoc = 1 << 5,
}
