// ported from: internal/fourslash/tests/codeFixClassSuperMustPrecedeThisAccess_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class CodeFixClassSuperMustPrecedeThisAccessCase
{
    public static void TestCodeFixClassSuperMustPrecedeThisAccess(TSU.IHarnessTest t)
    {
        t.Skip("Known failing fourslash test");
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("class Base{\n}\nclass C extends Base{\n    private a:number;\n    constructor() {[|\n        this.a = 12;\n        super();\n    |]}\n    m() { this.a; } // avoid unused 'a'\n}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyRangeAfterCodeFix(t, "\n        super();\n        this.a = 12;\n    ", true, 0, 0);
            }
            finally
            {
                done();
            }
        });
    }

}
