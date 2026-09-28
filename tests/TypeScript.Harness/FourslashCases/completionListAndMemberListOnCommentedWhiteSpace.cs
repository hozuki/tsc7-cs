// ported from: internal/fourslash/tests/completionListAndMemberListOnCommentedWhiteSpace_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class CompletionListAndMemberListOnCommentedWhiteSpaceCase
{
    public static void TestCompletionListAndMemberListOnCommentedWhiteSpace(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("namespace M {\n  export class C { public pub = 0; private priv = 1; }\n  export var V = 0;\n}\n\n\nvar c = new M.C();\n\nc. // test on c.\n\n//Test for comment\n//c. /**/"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyCompletions(t, "", null!);
            }
            finally
            {
                done();
            }
        });
    }

}
