// ported from: internal/fourslash/tests/goToDefinitionModifiers_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class GoToDefinitionModifiersCase
{
    public static void TestGoToDefinitionModifiers(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @Filename: /a.ts\n/*export*/export class A/*A*/ {\n\n    /*private*/private z/*z*/: string;\n\n    /*readonly*/readonly x/*x*/: string;\n\n    /*async*/async a/*a*/() {  }\n\n    /*override*/override b/*b*/() {}\n\n    /*public1*/public/*public2*/ as/*multipleModifiers*/ync c/*c*/() { }\n}\n\nexp/*exportFunction*/ort function foo/*foo*/() { }"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyBaselineGoToDefinition(t, true, "export", "A", "private", "z", "readonly", "x", "async", "a", "override", "b", "public1", "public2", "multipleModifiers", "c", "exportFunction", "foo");
            }
            finally
            {
                done();
            }
        });
    }

}
