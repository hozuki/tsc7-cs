// ported from: internal/fourslash/tests/goToDefinitionImports_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class GoToDefinitionImportsCase
{
    public static void TestGoToDefinitionImports(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @Filename: /a.ts\nexport default function /*fDef*/f() {}\nexport const /*xDef*/x = 0;\n// @Filename: /b.ts\n/*bDef*/declare const b: number;\nexport = b;\n// @Filename: /b.ts\nimport f, { x } from \"./a\";\nimport * as /*aDef*/a from \"./a\";\nimport b = require(\"./b\");\n[|/*fUse*/f|];\n[|/*xUse*/x|];\n[|/*aUse*/a|];\n[|/*bUse*/b|];"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyBaselineGoToDefinition(t, true, "aUse", "fUse", "xUse", "bUse");
            }
            finally
            {
                done();
            }
        });
    }

}
