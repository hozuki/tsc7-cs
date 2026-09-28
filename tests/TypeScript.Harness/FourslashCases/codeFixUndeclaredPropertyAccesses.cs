// ported from: internal/fourslash/tests/codeFixUndeclaredPropertyAccesses_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class CodeFixUndeclaredPropertyAccessesCase
{
    public static void TestCodeFixUndeclaredPropertyAccesses(TSU.IHarnessTest t)
    {
        t.Skip("Known failing fourslash test");
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("interface I { x: number; }\nlet i: I;\ni.y;\ni.foo();\nenum E { a,b }\nlet e: typeof E;\ne.a;\ne.c;\nlet obj = { a: 1, b: \"asdf\"};\nobj.c;\ntype T<U> = I | U;\nlet t: T<number>;\nt.x;"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyCodeFixAvailable(t, null!);
            }
            finally
            {
                done();
            }
        });
    }

}
