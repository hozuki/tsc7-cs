// ported from: internal/testutil/tsbaseline/sourcemap_record_baseline.go
//
// 与 Go 的差异：`*testing.T` → `IHarnessTest`（这里只用来上报 baseline 差异）。

namespace TypeScript.TestUtil.TsBaseline;

using TypeScript.Core;
using TypeScript.TestUtil.Baseline;
using TypeScript.TestUtil.HarnessUtil;
using static TypeScript.TSPath.TSPath;

public static partial class TsBaseline
{
    /// <summary>Go: <c>func DoSourcemapRecordBaseline(...)</c>。</summary>
    public static void DoSourcemapRecordBaseline(
        IHarnessTest t,
        string baselinePath,
        string header,
        CompilerOptions options,
        CompilationResult result,
        HarnessOptions harnessSettings,
        BaselineOptions opts)
    {
        var actual = Baseline.NoContent;
        if (options.SourceMap.IsTrue() || options.InlineSourceMap.IsTrue() || options.DeclarationMap.IsTrue())
        {
            var record = RemoveTestPathPrefixes(result.GetSourceMapRecord(), false /*retainTrailingDirectorySeparator*/);
            if (!(options.NoEmitOnError.IsTrue() && result.Diagnostics.Length > 0) && record.Length > 0)
            {
                actual = record;
            }
        }

        if (FileExtensionIsOneOf(baselinePath, [ExtensionTs, ExtensionTsx]))
        {
            baselinePath = ChangeExtension(baselinePath, ".sourcemap.txt");
        }

        Baseline.Run(t, baselinePath, actual, opts);
    }
}
