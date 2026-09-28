// ported from: internal/fourslash/tests/goToImplementationInterface_09_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class GoToImplementationInterface_09Case
{
    public static void TestGoToImplementationInterface_09(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @Filename: def.d.ts\nexport interface Interface { P: number }\n// @Filename: ref.ts\nimport { Interface } from \"./def\";\nconst c: I/*ref*/nterface = [|{ P: 2 }|];"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyBaselineGoToImplementation(t, "ref");
            }
            finally
            {
                done();
            }
        });
    }

}
