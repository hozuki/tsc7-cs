// ported from: internal/fourslash/tests/referencesForGlobals_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class ReferencesForGlobalsCase
{
    public static void TestReferencesForGlobals(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @Filename: referencesForGlobals_1.ts\n/*1*/var /*2*/global = 2;\n\nclass foo {\n    constructor (public global) { }\n    public f(global) { }\n    public f2(global) { }\n}\n\nclass bar {\n    constructor () {\n        var n = /*3*/global;\n\n        var f = new foo('');\n        f.global = '';\n    }\n}\n\nvar k = /*4*/global;\n// @Filename: referencesForGlobals_2.ts\nvar m = /*5*/global;"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyBaselineFindAllReferences(t, "1", "2", "3", "4", "5");
            }
            finally
            {
                done();
            }
        });
    }

}
