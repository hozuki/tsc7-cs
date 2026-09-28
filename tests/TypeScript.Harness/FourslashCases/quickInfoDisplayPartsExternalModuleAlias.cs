// ported from: internal/fourslash/tests/quickInfoDisplayPartsExternalModuleAlias_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class QuickInfoDisplayPartsExternalModuleAliasCase
{
    public static void TestQuickInfoDisplayPartsExternalModuleAlias(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @Filename: quickInfoDisplayPartsExternalModuleAlias_file0.ts\nexport namespace m1 {\n    export class c {\n    }\n}\n// @Filename: quickInfoDisplayPartsExternalModuleAlias_file1.ts\nimport /*1*/a1 = require(/*mod1*/\"./quickInfoDisplayPartsExternalModuleAlias_file0\");\nnew /*2*/a1.m1.c();\nexport import /*3*/a2 = require(/*mod2*/\"./quickInfoDisplayPartsExternalModuleAlias_file0\");\nnew /*4*/a2.m1.c();"u8);
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
