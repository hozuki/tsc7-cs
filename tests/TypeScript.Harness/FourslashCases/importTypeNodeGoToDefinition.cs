// ported from: internal/fourslash/tests/importTypeNodeGoToDefinition_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class ImportTypeNodeGoToDefinitionCase
{
    public static void TestImportTypeNodeGoToDefinition(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @Filename: /ns.ts\n/*refFile*/export namespace /*refFoo*/Foo {\n    export namespace /*refBar*/Bar {\n        export class /*refBaz*/Baz {}\n    }\n}\n// @Filename: /usage.ts\ntype A = typeof import([|/*1*/\"./ns\"|]).[|/*2*/Foo|].[|/*3*/Bar|];\ntype B = import([|/*4*/\"./ns\"|]).[|/*5*/Foo|].[|/*6*/Bar|].[|/*7*/Baz|];"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyBaselineGoToDefinition(t, true, "1", "2", "3", "4", "5", "6", "7");
            }
            finally
            {
                done();
            }
        });
    }

}
