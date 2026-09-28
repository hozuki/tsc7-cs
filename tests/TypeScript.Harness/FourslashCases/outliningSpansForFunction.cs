// ported from: internal/fourslash/tests/outliningSpansForFunction_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class OutliningSpansForFunctionCase
{
    public static void TestOutliningSpansForFunction(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("[|(\n    a: number,\n    b: number\n) => {\n    return a + b;\n}|];\n\n(a: number, b: number) =>[| {\n    return a + b;\n}|]\n\nconst f1 = function[| (\n    a: number\n    b: number\n) {\n    return a + b;\n}|]\n\nconst f2 = function (a: number, b: number)[| {\n    return a + b;\n}|]\n\nfunction f3[| (\n    a: number\n    b: number\n) {\n    return a + b;\n}|]\n\nfunction f4(a: number, b: number)[| {\n    return a + b;\n}|]\n\nclass Foo[| {\n    constructor[|(\n        a: number,\n        b: number\n    ) {\n        this.a = a;\n        this.b = b;\n    }|]\n\n    m1[|(\n        a: number,\n        b: number\n    ) {\n        return a + b;\n    }|]\n\n    m1(a: number, b: number)[| {\n        return a + b;\n    }|]\n}|]\n\ndeclare function foo(props: any): void;\nfoo[|(\n    a =>[| {\n\n    }|]\n)|]\n\nfoo[|(\n    (a) =>[| {\n\n    }|]\n)|]\n\nfoo[|(\n    (a, b, c) =>[| {\n\n    }|]\n)|]\n\nfoo[|([|\n    (a,\n     b,\n     c) => {\n\n    }|]\n)|]"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyOutliningSpans(t);
            }
            finally
            {
                done();
            }
        });
    }

}
