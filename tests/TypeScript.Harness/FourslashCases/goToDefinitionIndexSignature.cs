// ported from: internal/fourslash/tests/goToDefinitionIndexSignature_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class GoToDefinitionIndexSignatureCase
{
    public static void TestGoToDefinitionIndexSignature(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("interface I {\n    /*defI*/[x: string]: boolean;\n}\ninterface J {\n    /*defJ*/[x: string]: number;\n}\ninterface K {\n    /*defa*/[x: `a${string}`]: string;\n    /*defb*/[x: `${string}b`]: string;\n}\ndeclare const i: I;\ni.[|/*useI*/foo|];\ndeclare const ij: I | J;\nij.[|/*useIJ*/foo|];\ndeclare const k: K;\nk.[|/*usea*/a|];\nk.[|/*useb*/b|];\nk.[|/*useab*/ab|];"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyBaselineGoToDefinition(t, true, "useI", "useIJ", "usea", "useb", "useab");
            }
            finally
            {
                done();
            }
        });
    }

}
