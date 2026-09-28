// ported from: internal/fourslash/tests/ambientShorthandGotoDefinition_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class AmbientShorthandGotoDefinitionCase
{
    public static void TestAmbientShorthandGotoDefinition(TSU.IHarnessTest t)
    {
        t.Skip("Known failing fourslash test");
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @Filename: declarations.d.ts\ndeclare module /*module*/\"jquery\"\n// @Filename: user.ts\n///<reference path=\"declarations.d.ts\"/>\nimport [|/*importFoo*/foo|], {bar} from \"jquery\";\nimport * as [|/*importBaz*/baz|] from \"jquery\";\nimport [|/*importBang*/bang|] = require(\"jquery\");\n[|foo/*useFoo*/|]([|bar/*useBar*/|], [|baz/*useBaz*/|], [|bang/*useBang*/|]);"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyQuickInfoAt(t, "useFoo", "(alias) module \"jquery\"\nimport foo", "");
                f.VerifyQuickInfoAt(t, "useBar", "(alias) module \"jquery\"\nimport bar", "");
                f.VerifyQuickInfoAt(t, "useBaz", "(alias) module \"jquery\"\nimport baz", "");
                f.VerifyQuickInfoAt(t, "useBang", "(alias) module \"jquery\"\nimport bang = require(\"jquery\")", "");
                f.VerifyBaselineGoToDefinition(t, true, "useFoo", "importFoo", "useBar", "useBaz", "importBaz", "useBang", "importBang");
            }
            finally
            {
                done();
            }
        });
    }

}
