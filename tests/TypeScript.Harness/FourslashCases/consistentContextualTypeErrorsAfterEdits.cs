// ported from: internal/fourslash/tests/consistentContextualTypeErrorsAfterEdits_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class ConsistentContextualTypeErrorsAfterEditsCase
{
    public static void TestConsistentContextualTypeErrorsAfterEdits(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @strict: false\nclass A {\n    foo: string;\n}\nclass C {\n    foo: string;\n}\nvar xs /*1*/ = [(x: A) => { return x.foo; }, (x: C) => { return x.foo; }];\nxs.forEach(y => y(new /*2*/A()));"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyNumberOfErrorsInCurrentFile(t, 0);
                f.GoToMarker(t, "1");
                f.Insert(t, ": {}[]");
                f.VerifyNumberOfErrorsInCurrentFile(t, 1);
                f.GoToMarker(t, "2");
                f.DeleteAtCaret(t, 1);
                f.Insert(t, "C");
                f.VerifyNumberOfErrorsInCurrentFile(t, 1);
            }
            finally
            {
                done();
            }
        });
    }

}
