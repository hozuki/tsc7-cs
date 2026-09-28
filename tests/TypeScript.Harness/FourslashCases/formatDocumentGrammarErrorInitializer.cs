// ported from: internal/fourslash/tests/formatDocumentGrammarErrorInitializer_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class FormatDocumentGrammarErrorInitializerCase
{
    public static void TestFormatDocumentGrammarErrorInitializer(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @Filename: /a.ts\nconst f = () => {\n  const v = x as A & {\n    a: { b: C\n  }\n  const m: T[] = [\n    { g: () => { nav(`${z}`) } },\n  ]\n  const n: T[] =\n}\n"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.FormatDocument(t, "/a.ts");
            }
            finally
            {
                done();
            }
        });
    }

}
