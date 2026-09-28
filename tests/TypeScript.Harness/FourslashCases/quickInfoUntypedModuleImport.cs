// ported from: internal/fourslash/tests/quickInfoUntypedModuleImport_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class QuickInfoUntypedModuleImportCase
{
    public static void TestQuickInfoUntypedModuleImport(TSU.IHarnessTest t)
    {
        t.Skip("Known failing fourslash test");
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @strict: false\n// @Filename: node_modules/foo/index.js\n /*index*/{}\n// @Filename: a.ts\nimport /*foo*/foo from /*fooModule*/\"foo\";\n/*fooCall*/foo();"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.GoToFile(t, "a.ts");
                f.VerifyNumberOfErrorsInCurrentFile(t, 0);
                f.GoToMarker(t, "fooModule");
                f.VerifyQuickInfoIs(t, "", "");
                f.GoToMarker(t, "foo");
                f.VerifyQuickInfoIs(t, "import foo", "");
                f.VerifyBaselineFindAllReferences(t, "foo", "fooModule", "fooCall");
                f.VerifyBaselineGoToDefinition(t, false, "fooModule", "foo");
            }
            finally
            {
                done();
            }
        });
    }

}
