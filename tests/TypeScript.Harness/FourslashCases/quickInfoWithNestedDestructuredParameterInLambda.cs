// ported from: internal/fourslash/tests/quickInfoWithNestedDestructuredParameterInLambda_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class QuickInfoWithNestedDestructuredParameterInLambdaCase
{
    public static void TestQuickInfoWithNestedDestructuredParameterInLambda(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @filename: a.tsx\nimport * as React from 'react';\ninterface SomeInterface {\n    someBoolean: boolean,\n    someString: string;\n}\ninterface SomeProps {\n    someProp: SomeInterface;\n}\nexport const /*1*/SomeStatelessComponent = ({someProp: { someBoolean, someString}}: SomeProps) => (<div>{`${someBoolean}${someString}`});"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.GoToMarker(t, "1");
                f.VerifyQuickInfoExists(t);
            }
            finally
            {
                done();
            }
        });
    }

}
