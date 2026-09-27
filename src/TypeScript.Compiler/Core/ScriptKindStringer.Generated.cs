// ported from: internal/core/scriptkind_stringer_generated.go
//
// Go 侧由 `stringer -type=ScriptKind` 生成（**没有** -trimprefix）；C# 的 enum 不能覆写 ToString，
// 所以 1:1 落成 `Core` 静态类里的扩展方法 `String(this ScriptKind)`。
//
// 输出与 Go 逐字一致：_ScriptKind_name_0 = "ScriptKindUnknownScriptKindJSScriptKindJSXScriptKindTSScriptKindTSX"
// （index {0,17,29,42,54,67}）、_ScriptKind_name_1 = "ScriptKindJSON"（i == 6）；
// 其余 → "ScriptKind(" + strconv.FormatInt(int64(i), 10) + ")"（含保留值 5、7）。
namespace TypeScript.Core;

public static partial class Core
{
    public static string String(this ScriptKind i)
    {
        switch (i)
        {
            case ScriptKind.Unknown:
                return "ScriptKindUnknown";
            case ScriptKind.JS:
                return "ScriptKindJS";
            case ScriptKind.JSX:
                return "ScriptKindJSX";
            case ScriptKind.TS:
                return "ScriptKindTS";
            case ScriptKind.TSX:
                return "ScriptKindTSX";
            case ScriptKind.JSON:
                return "ScriptKindJSON";
            default:
                return $"ScriptKind({(int)i})";
        }
    }
}
