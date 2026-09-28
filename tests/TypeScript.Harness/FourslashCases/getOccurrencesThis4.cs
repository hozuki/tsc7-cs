// ported from: internal/fourslash/tests/getOccurrencesThis4_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;
using U = TypeScript.Harness.FourslashCases.FourslashTestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class GetOccurrencesThis4Case
{
    public static void TestGetOccurrencesThis4(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("this;\nthis;\n\nfunction f() {\n    this;\n    this;\n    () => this;\n    () => {\n        if (this) {\n            this;\n        }\n        else {\n            this.this;\n        }\n    }\n    function inside() {\n        this;\n        (function (_) {\n            this;\n        })(this);\n    }\n}\n\nnamespace m {\n    function f() {\n        this;\n        this;\n        () => this;\n        () => {\n            if (this) {\n                this;\n            }\n            else {\n                this.this;\n            }\n        }\n        function inside() {\n            this;\n            (function (_) {\n                this;\n            })(this);\n        }\n    }\n}\n\nclass A {\n    public b = [|this|].method1;\n\n    public method1() {\n        [|this|];\n        [|this|];\n        () => [|this|];\n        () => {\n            if ([|this|]) {\n                [|this|];\n            }\n            else {\n                [|this|].this;\n            }\n        }\n        function inside() {\n            this;\n            (function (_) {\n                this;\n            })(this);\n        }\n    }\n\n    private method2() {\n        [|this|];\n        [|this|];\n        () => [|t/**/his|];\n        () => {\n            if ([|this|]) {\n                [|this|];\n            }\n            else {\n                [|this|].this;\n            }\n        }\n        function inside() {\n            this;\n            (function (_) {\n                this;\n            })(this);\n        }\n    }\n\n    public static staticB = this.staticMethod1;\n\n    public static staticMethod1() {\n        this;\n        this;\n        () => this;\n        () => {\n            if (this) {\n                this;\n            }\n            else {\n                this.this;\n            }\n        }\n        function inside() {\n            this;\n            (function (_) {\n                this;\n            })(this);\n        }\n    }\n\n    private static staticMethod2() {\n        this;\n        this;\n        () => this;\n        () => {\n            if (this) {\n                this;\n            }\n            else {\n                this.this;\n            }\n        }\n        function inside() {\n            this;\n            (function (_) {\n                this;\n            })(this);\n        }\n    }\n}\n\nvar x = {\n    f() {\n        this;\n    },\n    g() {\n        this;\n    }\n}"u8);
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
