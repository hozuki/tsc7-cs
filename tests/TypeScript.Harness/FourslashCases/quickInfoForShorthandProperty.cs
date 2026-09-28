// ported from: internal/fourslash/tests/quickInfoForShorthandProperty_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class QuickInfoForShorthandPropertyCase
{
    public static void TestQuickInfoForShorthandProperty(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @strict: false\nvar name1 = undefined, id1 = undefined;\nvar /*obj1*/obj1 = {/*name1*/name1, /*id1*/id1};\nvar name2 = \"Hello\";\nvar id2 = 10000;\nvar /*obj2*/obj2 = {/*name2*/name2, /*id2*/id2};"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyQuickInfoAt(t, "obj1", "var obj1: {\n    name1: any;\n    id1: any;\n}", "");
                f.VerifyQuickInfoAt(t, "name1", "(property) name1: any", "");
                f.VerifyQuickInfoAt(t, "id1", "(property) id1: any", "");
                f.VerifyQuickInfoAt(t, "obj2", "var obj2: {\n    name2: string;\n    id2: number;\n}", "");
                f.VerifyQuickInfoAt(t, "name2", "(property) name2: string", "");
                f.VerifyQuickInfoAt(t, "id2", "(property) id2: number", "");
            }
            finally
            {
                done();
            }
        });
    }

}
