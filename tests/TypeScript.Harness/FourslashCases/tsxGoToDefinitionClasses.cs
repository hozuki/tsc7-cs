// ported from: internal/fourslash/tests/tsxGoToDefinitionClasses_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class TsxGoToDefinitionClassesCase
{
    public static void TestTsxGoToDefinitionClasses(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("//@Filename: file.tsx\ndeclare namespace JSX {\n    interface Element { }\n    interface IntrinsicElements { }\n    interface ElementAttributesProperty { props; }\n}\nclass /*ct*/MyClass {\n    props: {\n        /*pt*/foo: string;\n    }\n}\nvar x = <[|My/*c*/Class|] />;\nvar y = <MyClass [|f/*p*/oo|]= 'hello' />;\nvar z = <[|MyCl/*w*/ass|] wrong= 'hello' />;"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyBaselineGoToDefinition(t, true, "c", "p", "w");
            }
            finally
            {
                done();
            }
        });
    }

}
