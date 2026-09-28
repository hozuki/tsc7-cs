// ported from: internal/fourslash/tests/findAllReferencesLinkTag1_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class FindAllReferencesLinkTag1Case
{
    public static void TestFindAllReferencesLinkTag1(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("class C/*7*/ {\n    m/*1*/() { }\n    n/*2*/ = 1\n    static s/*3*/() { }\n    /**\n     * {@link m}\n     * @see {m}\n     * {@link C.m}\n     * @see {C.m}\n     * {@link C#m}\n     * @see {C#m}\n     * {@link C.prototype.m}\n     * @see {C.prototype.m}\n     */\n    p() { }\n    /**\n     * {@link n}\n     * @see {n}\n     * {@link C.n}\n     * @see {C.n}\n     * {@link C#n}\n     * @see {C#n}\n     * {@link C.prototype.n}\n     * @see {C.prototype.n}\n     */\n    q() { }\n    /**\n     * {@link s}\n     * @see {s}\n     * {@link C.s}\n     * @see {C.s}\n     */\n    r() { }\n}\n\ninterface I/*8*/ {\n    a/*4*/()\n    b/*5*/: 1\n    /**\n     * {@link a}\n     * @see {a}\n     * {@link I.a}\n     * @see {I.a}\n     * {@link I#a}\n     * @see {I#a}\n     */\n    c()\n    /**\n     * {@link b}\n     * @see {b}\n     * {@link I.b}\n     * @see {I.b}\n     */\n    d()\n}\n\nfunction nestor() {\n    /** {@link r2} */\n    function ref() { }\n    /** @see {r2} */\n    function d3() { }\n    function r2/*6*/() { }\n}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyBaselineFindAllReferences(t, "1", "2", "3", "4", "5", "6", "7", "8");
            }
            finally
            {
                done();
            }
        });
    }

}
