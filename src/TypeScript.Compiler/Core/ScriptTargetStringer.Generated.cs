// ported from: internal/core/scripttarget_stringer_generated.go
//
// Go 侧由 `stringer -type=ScriptTarget -trimprefix=ScriptTarget` 生成；C# 的 enum 不能覆写 ToString，
// 所以 1:1 落成 `Core` 静态类里的扩展方法 `String(this ScriptTarget)`，调用点保留 Go 的 `x.String()` 写法。
//
// 输出与 Go 逐字一致（名字已去掉 ScriptTarget 前缀）：
//   0..12   → "None" "ES5" "ES2015" … "ES2025"
//   99..100 → "ESNext" "JSON"
//   其余    → "ScriptTarget(" + strconv.FormatInt(int64(i), 10) + ")"
// 注意 Go 的别名常量（ScriptTargetLatest = ESNext、ScriptTargetLatestStandard = ES2025）不是 stringer 的
// 独立分支：99 → "ESNext"、12 → "ES2025"（C# 侧别名是 Core 的 const，见 CompilerOptions.cs）。
namespace TypeScript.Core;

public static partial class Core
{
    public static string String(this ScriptTarget i)
    {
        switch (i)
        {
            case ScriptTarget.None:
                return "None";
            case ScriptTarget.ES5:
                return "ES5";
            case ScriptTarget.ES2015:
                return "ES2015";
            case ScriptTarget.ES2016:
                return "ES2016";
            case ScriptTarget.ES2017:
                return "ES2017";
            case ScriptTarget.ES2018:
                return "ES2018";
            case ScriptTarget.ES2019:
                return "ES2019";
            case ScriptTarget.ES2020:
                return "ES2020";
            case ScriptTarget.ES2021:
                return "ES2021";
            case ScriptTarget.ES2022:
                return "ES2022";
            case ScriptTarget.ES2023:
                return "ES2023";
            case ScriptTarget.ES2024:
                return "ES2024";
            case ScriptTarget.ES2025:
                return "ES2025";
            case ScriptTarget.ESNext:
                return "ESNext";
            case ScriptTarget.JSON:
                return "JSON";
            default:
                return $"ScriptTarget({(int)i})";
        }
    }
}
