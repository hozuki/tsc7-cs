// ported from: internal/fourslash/tests/getOccurrencesSuper2_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;
using U = TypeScript.Harness.FourslashCases.FourslashTestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class GetOccurrencesSuper2Case
{
    public static void TestGetOccurrencesSuper2(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("class SuperType {\n    superMethod() {\n    }\n\n    static superStaticMethod() {\n        return 10;\n    }\n}\n\nclass SubType extends SuperType {\n    public  prop1 = super.superMethod;\n    private prop2 = super.superMethod;\n\n    constructor() {\n        super();\n    }\n\n    public method1() {\n        return super.superMethod();\n    }\n\n    private method2() {\n        return super.superMethod();\n    }\n\n    public method3() {\n        var x = () => super.superMethod();\n\n        // Bad but still gets highlighted\n        function f() {\n            super.superMethod();\n        }\n    }\n\n    // Bad but still gets highlighted.\n    public static statProp1 = [|super|].superStaticMethod;\n\n    public static staticMethod1() {\n        return [|super|].superStaticMethod();\n    }\n\n    private static staticMethod2() {\n        return [|supe/**/r|].superStaticMethod();\n    }\n\n    // Are not actually 'super' keywords.\n    super = 10;\n    static super = 20;\n}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyBaselineDocumentHighlights(t, null!, U.ToAny(f.Ranges()));
            }
            finally
            {
                done();
            }
        });
    }

}
