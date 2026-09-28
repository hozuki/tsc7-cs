// ported from: internal/fourslash/tests/hoverCircularInheritedDocumentation_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class HoverCircularInheritedDocumentationCase
{
    public static void TestHoverCircularInheritedDocumentation(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @filename: base.ts\nexport interface Options {}\n// @filename: bridge.ts\nimport type { Options as _Options } from \"./base\";\nexport * from \"./base\";\ndeclare module \"./bridge\" {\n    interface Options extends _Options { hooks: {} }\n}\ndeclare const v: Options;\nv.hooks/*1*/;"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyQuickInfoAt(t, "1", "(property) Options.hooks: {}", "");
            }
            finally
            {
                done();
            }
        });
    }

}
