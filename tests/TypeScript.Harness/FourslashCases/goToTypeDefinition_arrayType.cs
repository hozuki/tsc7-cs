// ported from: internal/fourslash/tests/goToTypeDefinition_arrayType_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class GoToTypeDefinition_arrayTypeCase
{
    public static void TestGoToTypeDefinition_arrayType(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @lib: es5\ntype User = { name: string };\ndeclare const users: User[]\n/*reference*/users\n\ntype UsersArr = Array<User>\ndeclare const users2: UsersArr\n/*reference2*/users2\n\nclass CustomArray<T> extends Array<T> { immutableReverse() { return [...this].reverse() } }\ndeclare const users3: CustomArray<User>\n/*reference3*/users3"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyBaselineGoToTypeDefinition(t, "reference", "reference2", "reference3");
            }
            finally
            {
                done();
            }
        });
    }

}
