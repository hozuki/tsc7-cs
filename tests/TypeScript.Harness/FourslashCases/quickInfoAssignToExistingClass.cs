// ported from: internal/fourslash/tests/quickInfoAssignToExistingClass_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class QuickInfoAssignToExistingClassCase
{
    public static void TestQuickInfoAssignToExistingClass(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("namespace Test {\n    class Mocked {\n        myProp: string;\n    }\n    class Tester {\n        willThrowError() {\n            Mocked = Mocked || function () { // => Error: Invalid left-hand side of assignment expression.\n                return { /**/myProp: \"test\" };\n            };\n        }\n    }\n}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.GoToMarker(t, "");
                f.VerifyQuickInfoExists(t);
            }
            finally
            {
                done();
            }
        });
    }

}
