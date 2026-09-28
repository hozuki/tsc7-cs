// ported from: internal/fourslash/tests/quickInfoDisplayPartsExternalModules_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class QuickInfoDisplayPartsExternalModulesCase
{
    public static void TestQuickInfoDisplayPartsExternalModules(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("export namespace /*1*/m {\n    var /*2*/namespaceElemWithoutExport = 10;\n    export var /*3*/namespaceElemWithExport = 10;\n}\nexport var /*4*/a = /*5*/m;\nexport var /*6*/b: typeof /*7*/m;\nexport namespace /*8*/m1./*9*/m2 {\n    var /*10*/namespaceElemWithoutExport = 10;\n    export var /*11*/namespaceElemWithExport = 10;\n}\nexport var /*12*/x = /*13*/m1./*14*/m2;\nexport var /*15*/y: typeof /*16*/m1./*17*/m2;"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyBaselineHover(t);
            }
            finally
            {
                done();
            }
        });
    }

}
