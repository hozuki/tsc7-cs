// ported from: internal/core/languagevariant.go
//
// 偏离 Go 的地方（C# 语言特性）：`type LanguageVariant int32` + 包级常量 → C# `enum LanguageVariant : int`，
// 成员名 = Go 常量名去掉类型前缀。Go 的 languagevariant stringer **没有** `-trimprefix`
// （输出 "LanguageVariantStandard"），见 LanguageVariantStringer.Generated.cs。
namespace TypeScript.Core;

public enum LanguageVariant : int
{
    Standard = 0,
    JSX = 1,
}
