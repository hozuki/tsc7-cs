// ported from: internal/core/tristate_stringer_generated.go
//
// Go 侧由 `stringer -type=Tristate` 生成；C# 的 enum 不能覆写 ToString，所以 1:1 落成 `Core` 静态类里的
// 扩展方法 `String(this Tristate)`，调用点保留 Go 的 `x.String()` 写法。
//
// 输出与 Go 逐字一致：_Tristate_name = "TSUnknownTSFalseTSTrue"、_Tristate_index = {0,9,16,22}，
// 合法值 → 常量名；其余 → "Tristate(" + strconv.FormatInt(int64(i), 10) + ")"。
namespace TypeScript.Core;

public static partial class Core
{
    public static string String(this Tristate i)
    {
        switch (i)
        {
            case Tristate.TSUnknown:
                return "TSUnknown";
            case Tristate.TSFalse:
                return "TSFalse";
            case Tristate.TSTrue:
                return "TSTrue";
            default:
                return $"Tristate({(int)i})";
        }
    }
}
