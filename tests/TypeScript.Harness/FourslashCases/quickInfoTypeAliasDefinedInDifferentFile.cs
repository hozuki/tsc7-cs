// ported from: internal/fourslash/tests/quickInfoTypeAliasDefinedInDifferentFile_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class QuickInfoTypeAliasDefinedInDifferentFileCase
{
    public static void TestQuickInfoTypeAliasDefinedInDifferentFile(TSU.IHarnessTest t)
    {
        t.Skip("Known failing fourslash test");
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @Filename: /a.ts\nexport type X = { x: number };\nexport function f(x: X): void {}\n// @Filename: /b.ts\nimport { f } from \"./a\";\n/**/f({ x: 1 });"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyQuickInfoAt(t, "", "(alias) f(x: X): void\nimport f", "");
            }
            finally
            {
                done();
            }
        });
    }

}
