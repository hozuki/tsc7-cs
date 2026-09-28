// ported from: internal/fourslash/tests/quickInfoCloduleWithRecursiveReference_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class QuickInfoCloduleWithRecursiveReferenceCase
{
    public static void TestQuickInfoCloduleWithRecursiveReference(TSU.IHarnessTest t)
    {
        t.Skip("Known failing fourslash test");
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("namespace M {\n    export class C {\n        foo() { }\n    }\n    export namespace C {\n    export var /**/C = M.C\n  }\n}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyQuickInfoAt(t, "", "var M.C.C: typeof M.C", "");
                f.VerifyNoErrors(t);
            }
            finally
            {
                done();
            }
        });
    }

}
