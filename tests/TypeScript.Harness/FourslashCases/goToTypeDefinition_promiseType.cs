// ported from: internal/fourslash/tests/goToTypeDefinition_promiseType_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class GoToTypeDefinition_promiseTypeCase
{
    public static void TestGoToTypeDefinition_promiseType(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @lib: es5,es2015.promise\ntype User = { name: string };\nasync function /*reference*/getUser() { return { name: \"Bob\" } satisfies User as User }\n\nconst /*reference2*/promisedBob = getUser() \n\nexport {}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyBaselineGoToTypeDefinition(t, "reference", "reference2");
            }
            finally
            {
                done();
            }
        });
    }

}
