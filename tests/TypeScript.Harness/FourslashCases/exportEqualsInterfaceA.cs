// ported from: internal/fourslash/tests/exportEqualsInterfaceA_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class ExportEqualsInterfaceACase
{
    public static void TestExportEqualsInterfaceA(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @Filename: exportEqualsInterface_A.ts\ninterface A {\n    p1: number;\n}\nexport = A;\n/**/\nvar i: I1;\nvar n: number = i.p1;"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.GoToMarker(t, "");
                f.Insert(t, "import I1 = require(\"exportEqualsInterface_A\");");
            }
            finally
            {
                done();
            }
        });
    }

}
