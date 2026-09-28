// ported from: internal/fourslash/tests/callHierarchyCrossFile_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class CallHierarchyCrossFileCase
{
    public static void TestCallHierarchyCrossFile(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @filename: /a.ts\nexport function /**/createModelReference() {}\n// @filename: /b.ts\nimport { createModelReference } from \"./a\";\nfunction openElementsAtEditor() {\n  createModelReference();\n}\n// @filename: /c.ts\nimport { createModelReference } from \"./a\";\nfunction registerDefaultLanguageCommand() {\n  createModelReference();\n}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.GoToMarker(t, "");
                f.VerifyBaselineCallHierarchy(t);
            }
            finally
            {
                done();
            }
        });
    }

}
