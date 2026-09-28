// ported from: internal/fourslash/tests/forceIndentAfterNewLineInsert_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class ForceIndentAfterNewLineInsertCase
{
    public static void TestForceIndentAfterNewLineInsert(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("function f1()\n{ return 0; }\nfunction f2()\n{\nreturn 0;\n}\nfunction g()\n{ function h() {\nreturn 0;\n}}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.FormatDocument(t, "");
                f.VerifyCurrentFileContent(t, "function f1() { return 0; }\nfunction f2() {\n    return 0;\n}\nfunction g() {\n    function h() {\n        return 0;\n    }\n}");
            }
            finally
            {
                done();
            }
        });
    }

}
