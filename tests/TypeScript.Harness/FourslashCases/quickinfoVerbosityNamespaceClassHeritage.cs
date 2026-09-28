// ported from: internal/fourslash/tests/quickinfoVerbosityNamespaceClassHeritage_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class QuickinfoVerbosityNamespaceClassHeritageCase
{
    public static void TestQuickinfoVerbosityNamespaceClassHeritage(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("\ndeclare class Base {\n    id: number;\n}\n\ndeclare namespace Shapes/*1*/ {\n    class Circle extends Base {\n        radius: number;\n    }\n    class Square extends Base {\n        side: number;\n    }\n    interface Drawable {\n        draw(): void;\n    }\n}\n"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyBaselineHoverWithVerbosity(t, new Dictionary<string, int[]> { ["1"] = [0, 1] });
            }
            finally
            {
                done();
            }
        });
    }

}
