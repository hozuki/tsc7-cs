// ported from: internal/fourslash/tests/codeFixClassSuperMustPrecedeThisAccess_callWithThisInside_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class CodeFixClassSuperMustPrecedeThisAccess_callWithThisInsideCase
{
    public static void TestCodeFixClassSuperMustPrecedeThisAccess_callWithThisInside(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("class Base{\n    constructor(id: number) { id; }\n}\nclass C extends Base{\n    constructor(private a:number) {\n        super(this.a);\n    }\n}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyCodeFixNotAvailable(t);
            }
            finally
            {
                done();
            }
        });
    }

}
