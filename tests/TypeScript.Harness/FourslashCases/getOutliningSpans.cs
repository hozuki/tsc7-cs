// ported from: internal/fourslash/tests/getOutliningSpans_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class GetOutliningSpansCase
{
    public static void TestGetOutliningSpans(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// interface\ninterface IFoo[| {\n    getDist(): number;\n}|]\n\n// class members\nclass Foo[| {\n    constructor()[| {\n    }|]\n\n    public foo(): number[| {\n        return 0;\n    }|]\n\n    public get X()[| {\n        return 1;\n    }|]\n\n    public set X(v: number)[| {\n    }|]\n\n    public member = function f()[| {\n\n    }|]\n}|]\n// class expressions\n [|(new class[| {\n     bla()[| {\n\n     }|]\n }|])|]\nswitch(1)[| {\n case 1:[| break;|]\n}|]\n\nvar array =[| [\n    1,\n    2\n]|]\n\n// modules\nmodule m1[| {\n    module m2[| { }|]\n    module m3[| {\n        function foo()[| {\n\n        }|]\n\n        interface IFoo2[| {\n\n        }|]\n\n        class foo2 implements IFoo2[| {\n\n        }|]\n    }|]\n}|]\n\n// function declaration\nfunction foo(): number[| {\n    return 0;\n}|]\n\n// function expressions\n[|(function f()[| {\n\n}|])|]\n\n// trivia handling\nclass ClassFooWithTrivia[| /*  some comments */\n   /* more trivia */ {\n\n\n    [|/*some trailing trivia */|]\n}|] /* even more */\n\n// object literals\nvar x =[|{\n  a:1,\n  b:2,\n  get foo()[| {\n    return 1;\n  }|]\n}|]\n//outline with deep nesting\nvar nest =[| [[|[[|[[|[[|[[|[[|[[|[[|[[|[[|[[|[[|[[|[[|[[|[[|[[|[[|[[|[[|[[|[[|[[|[[|[[|[[|[[|[[|[[|[[|[[|[[|[[|[[|[[|[\n  [|[\n      [\n        [\n          [\n            [\n              1,2,3\n            ]\n          ]\n        ]\n      ]\n  ]|]\n]|]]|]]|]]|]]|]]|]]|]]|]]|]]|]]|]]|]]|]]|]]|]]|]]|]]|]]|]]|]]|]]|]]|]]|]]|]]|]]|]]|]]|]]|]]|]]|]]|]]|]]|]]|];\n\n//outline after a deeply nested node\nclass AfterNestedNodes[| {\n}|]\n// function arguments\nfunction f(x: number[], y: number[])[| {\n    return 3;\n}|]\nf[|(\n//  single line array literal span won't render in VS\n    [|[0]|],\n    [|[\n        1,\n        2\n    ]|]\n)|];\n\nclass C<T>[| {\n    foo: T;\n}|]\n\nclass D<T> extends C<T>[| {\n    constructor(x)[| {\n        super<T>(x);\n    }|]\n}|]"u8);
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
