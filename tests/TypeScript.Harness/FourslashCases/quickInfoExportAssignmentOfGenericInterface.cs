// ported from: internal/fourslash/tests/quickInfoExportAssignmentOfGenericInterface_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class QuickInfoExportAssignmentOfGenericInterfaceCase
{
    public static void TestQuickInfoExportAssignmentOfGenericInterface(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @Filename: quickInfoExportAssignmentOfGenericInterface_0.ts\ninterface Foo<T> {\n    a: string;\n}\nexport = Foo;\n// @Filename: quickInfoExportAssignmentOfGenericInterface_1.ts\nimport a = require('./quickInfoExportAssignmentOfGenericInterface_0');\nexport var /*1*/x: a<a<string>>;\nx.a;"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyQuickInfoAt(t, "1", "var x: a<a<string>>", "");
            }
            finally
            {
                done();
            }
        });
    }

}
