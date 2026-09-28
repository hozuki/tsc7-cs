// ported from: internal/fourslash/tests/unreachableStatementNodeReuse_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class UnreachableStatementNodeReuseCase
{
    public static void TestUnreachableStatementNodeReuse(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("function test() {\n\treturn/*a*/abc();\n\treturn;\n}\nfunction abc() { }"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyNumberOfErrorsInCurrentFile(t, 1);
                f.GoToMarker(t, "a");
                f.Insert(t, " ");
                f.VerifyNoErrors(t);
            }
            finally
            {
                done();
            }
        });
    }

}
