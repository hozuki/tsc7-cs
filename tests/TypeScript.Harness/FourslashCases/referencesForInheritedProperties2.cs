// ported from: internal/fourslash/tests/referencesForInheritedProperties2_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class ReferencesForInheritedProperties2Case
{
    public static void TestReferencesForInheritedProperties2(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("interface interface1 {\n    /*1*/doStuff(): void;\n}\n\ninterface interface2 {\n    doStuff(): void;\n}\n\ninterface interface2 extends interface1 {\n}\n\nclass class1 implements interface2 {\n    doStuff() {\n\n    }\n}\n\nclass class2 extends class1 {\n\n}\n\nvar v: class2;\nv.doStuff();"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyBaselineFindAllReferences(t, "1");
            }
            finally
            {
                done();
            }
        });
    }

}
