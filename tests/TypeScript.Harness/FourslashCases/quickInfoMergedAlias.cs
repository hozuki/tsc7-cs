// ported from: internal/fourslash/tests/quickInfoMergedAlias_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class QuickInfoMergedAliasCase
{
    public static void TestQuickInfoMergedAlias(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @filename: /a.ts\n/**\n * A function\n */\nexport function foo/*1*/() {}\n// @filename: /b.ts\nimport { foo/*2*/ } from './a';\nexport { foo/*3*/ };\n\n/**\n * A type\n */\ntype foo/*4*/ = number;\n\nfoo/*5*/()\nlet x1: foo/*6*/;\n// @filename: /c.ts\nimport { foo/*7*/ } from './b';\n\n/**\n * A namespace\n */\nnamespace foo/*8*/ {\n    export type bar = string[];\n}\n\nfoo/*9*/()\nlet x1: foo/*10*/;\nlet x2: foo/*11*/.bar;"u8);
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
