// ported from: internal/testutil/harnessutil/harnessutil.go:1228-1234（本文件目前只落 TestRunner 需要的这一个函数）
//
// 说明（**M7 未完成**）：`internal/testutil/harnessutil`（1,682 行）依赖 `internal/compiler` 与
// `internal/execute`（tsc/watch/build），这些包在 M6/M7 才落地，所以本波只移植 `TestCaseParser`
// （`internal/testrunner/test_case_parser.go`）真正调用的这一个函数；harnessutil 的其余部分
// （`SkipUnsupportedCompilerOptions`/`GetHarnessOptions`/baseline 路径映射…）随 M7 的 harness
// 一起补，`docs/checker-plan.md` §3.1 已登记。
namespace TypeScript.TestUtil.HarnessUtil;

using static TypeScript.StringUtil.StringUtil;
using static TypeScript.TSPath.TSPath;

/// <summary>Go 的 <c>harnessutil</c> 包级函数（本波只有 <see cref="GetConfigNameFromFileName"/>）。</summary>
public static partial class HarnessUtil
{
    /// <summary>
    /// Go: <c>func GetConfigNameFromFileName(filename string) string</c>（harnessutil.go:1228-1234）。
    /// 只把 `tsconfig.json` / `jsconfig.json`（比较时小写化）识别为配置文件，其余返回空串。
    /// </summary>
    public static string GetConfigNameFromFileName(string filename)
    {
        // Go: basenameLower := strings.ToLower(tspath.GetBaseFileName(filename))
        var basenameLower = ToLowerJS(GetBaseFileName(filename));
        if (basenameLower == "tsconfig.json" || basenameLower == "jsconfig.json")
        {
            return basenameLower;
        }

        return "";
    }
}
