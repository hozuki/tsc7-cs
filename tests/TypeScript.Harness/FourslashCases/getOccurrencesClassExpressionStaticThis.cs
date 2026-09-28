// ported from: internal/fourslash/tests/getOccurrencesClassExpressionStaticThis_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;
using U = TypeScript.Harness.FourslashCases.FourslashTestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class GetOccurrencesClassExpressionStaticThisCase
{
    public static void TestGetOccurrencesClassExpressionStaticThis(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("var x = class C {\n    public x;\n    public y;\n    public z;\n    public staticX;\n    constructor() {\n        this;\n        this.x;\n        this.y;\n        this.z;\n    }\n    foo() {\n        this;\n        () => this;\n        () => {\n            if (this) {\n                this;\n            }\n        }\n        function inside() {\n            this;\n            (function (_) {\n                this;\n            })(this);\n        }\n        return this.x;\n    }\n\n    static bar() {\n        [|this|];\n        [|this|].staticX;\n        () => [|this|];\n        () => {\n            if ([|this|]) {\n                [|this|];\n            }\n        }\n        function inside() {\n            this;\n            (function (_) {\n                this;\n            })(this);\n        }\n    }\n}"u8);
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
