// ported from: internal/fourslash/tests/goToTypeDefinitionModifiers_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class GoToTypeDefinitionModifiersCase
{
    public static void TestGoToTypeDefinitionModifiers(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = "// @lib: es5\n// @Filename: /a.ts\n/*export*/export class A/*A*/ {\n\n    /*private*/private z/*z*/: string;\n\n    /*private2*/private y/*y*/: A;\n\n    /*readonly*/readonly x/*x*/: string;\n\n    /*async*/async a/*a*/() {  }\n\n    /*override*/override b/*b*/() {}\n\n    /*public1*/public/*public2*/ as/*multipleModifiers*/ync c/*c*/() { }\n}\n\nexp/*exportFunction*/ort function foo/*foo*/() { }";
            var (f, done) = F.Fourslash.NewFourslash(t, null, content);
            try
            {
                f.VerifyBaselineGoToTypeDefinition(t, "export", "A", "private", "z", "private2", "y", "readonly", "x", "async", "a", "override", "b", "public1", "public2", "multipleModifiers", "c", "exportFunction", "foo");
            }
            finally
            {
                done();
            }
        });
    }

}
