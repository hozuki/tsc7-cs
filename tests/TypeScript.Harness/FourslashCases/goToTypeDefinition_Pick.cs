// ported from: internal/fourslash/tests/goToTypeDefinition_Pick_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class GoToTypeDefinition_PickCase
{
    public static void TestGoToTypeDefinition_Pick(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = "// @lib: es5\ntype User = { id: number; name: string; };\ndeclare const user: Pick<User, \"name\">\n/*reference*/user\n\ntype PickedUser = Pick<User, \"name\">\ndeclare const user2: PickedUser\n/*reference2*/user2";
            var (f, done) = F.Fourslash.NewFourslash(t, null, content);
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
