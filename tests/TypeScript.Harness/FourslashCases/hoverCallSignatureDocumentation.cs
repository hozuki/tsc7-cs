// ported from: internal/fourslash/tests/hoverCallSignatureDocumentation_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class HoverCallSignatureDocumentationCase
{
    public static void TestHoverCallSignatureDocumentation(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("\ntype X = {\n    /** Description of invoking. */\n    (): string\n\n    /** Description of constructor. */\n    new (): number\n}\n\ndeclare const x: X\n\n/*1*/x()\nnew /*2*/x()\n"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyQuickInfoAt(t, "1", "const x: () => string", "Description of invoking.");
                f.VerifyQuickInfoAt(t, "2", "const x: new () => number", "Description of constructor.");
            }
            finally
            {
                done();
            }
        });
    }

}
