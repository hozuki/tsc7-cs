// ported from: internal/fourslash/tests/crossFileQuickInfoExportedTypeDoesNotUseImportType_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class CrossFileQuickInfoExportedTypeDoesNotUseImportTypeCase
{
    public static void TestCrossFileQuickInfoExportedTypeDoesNotUseImportType(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @Filename: b.ts\nexport interface B {}\nexport function foob(): {\n    x: B,\n    y: B\n} {\n    return null as any;\n}\n// @Filename: a.ts\nimport { foob } from \"./b\";\nconst thing/*1*/ = foob(/*2*/);"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyQuickInfoAt(t, "1", "const thing: {\n    x: B;\n    y: B;\n}", "");
                f.GoToMarker(t, "2");
                f.VerifySignatureHelp(t, new F.VerifySignatureHelpOptions { Text = "foob(): { x: B; y: B; }" });
            }
            finally
            {
                done();
            }
        });
    }

}
