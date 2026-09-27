// ported from: internal/core/languagevariant_stringer_generated.go
//
// Go 侧由 `stringer -type=LanguageVariant` 生成（**没有** -trimprefix）；C# 的 enum 不能覆写 ToString，
// 所以 1:1 落成 `Core` 静态类里的扩展方法 `String(this LanguageVariant)`。
//
// 输出与 Go 逐字一致：_LanguageVariant_name = "LanguageVariantStandardLanguageVariantJSX"、
// _LanguageVariant_index = {0,23,41}，合法值 → 常量名；其余 → "LanguageVariant(" + 十进制 + ")"。
namespace TypeScript.Core;

public static partial class Core
{
    public static string String(this LanguageVariant i)
    {
        switch (i)
        {
            case LanguageVariant.Standard:
                return "LanguageVariantStandard";
            case LanguageVariant.JSX:
                return "LanguageVariantJSX";
            default:
                return $"LanguageVariant({(int)i})";
        }
    }
}
