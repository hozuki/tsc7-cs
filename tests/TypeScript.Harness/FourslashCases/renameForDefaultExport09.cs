// ported from: internal/fourslash/tests/renameForDefaultExport09_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class RenameForDefaultExport09Case
{
    public static void TestRenameForDefaultExport09(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @Filename: foo.ts\nfunction /**/[|f|]() {\n    return 100;\n}\n\nexport default f;\n\nvar x: typeof f;\n\nvar y = f();\n\n/**\n *  Commenting f\n */\nnamespace f {\n    var local = 100;\n}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.GoToMarker(t, "");
                f.VerifyRenameSucceeded(t, null!);
            }
            finally
            {
                done();
            }
        });
    }

}
