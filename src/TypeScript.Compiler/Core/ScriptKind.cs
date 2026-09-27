// ported from: internal/core/scriptkind.go
//
// 偏离 Go 的地方（C# 语言特性）：
// 1) `type ScriptKind int32` + 包级常量 → C# `enum ScriptKind : int`，成员名 = Go 常量名去掉类型前缀。
//    注意：Go 的 scriptkind stringer **没有** `-trimprefix`，输出是 "ScriptKindJSON" 这种带前缀的名字，
//    所以 `ScriptKind.JSON.ToString()` 是 "JSON" 而 `ScriptKind.JSON.String()` 是 "ScriptKindJSON"
//    （凡进诊断/JSON 的地方用 `.String()`，见 ScriptKindStringer.Generated.cs）。
// 2) 5 号与 7 号值在 Go 里只有注释（formerly ScriptKindExternal / ScriptKindDeferred），保留原注释。
namespace TypeScript.Core;

public enum ScriptKind : int
{
    Unknown = 0,
    JS = 1,
    JSX = 2,
    TS = 3,
    TSX = 4,

    // Value 5 is reserved (formerly ScriptKindExternal).

    JSON = 6,

    // Value 7 is reserved (formerly ScriptKindDeferred).
}
