// ported from: internal/testutil/tsbaseline/module_resolution_baseline.go
//
// 与 Go 的差异：`*testing.T` → `IHarnessTest`（这里只用来上报 baseline 差异）。

namespace TypeScript.TestUtil.TsBaseline;

using TypeScript.TestUtil.Baseline;

public static partial class TsBaseline
{
    /// <summary>Go: <c>func DoModuleResolutionBaseline(t *testing.T, baselinePath string, trace string, opts baseline.Options)</c>。</summary>
    public static void DoModuleResolutionBaseline(IHarnessTest t, string baselinePath, string trace, BaselineOptions opts)
    {
        baselinePath = _tsExtension.Replace(baselinePath, ".trace.json");
        string errorBaseline;
        if (trace != "")
        {
            errorBaseline = trace;
        }
        else
        {
            errorBaseline = Baseline.NoContent;
        }

        Baseline.Run(t, baselinePath, errorBaseline, opts);
    }
}
