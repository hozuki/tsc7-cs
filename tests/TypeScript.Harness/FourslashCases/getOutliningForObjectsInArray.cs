// ported from: internal/fourslash/tests/getOutliningForObjectsInArray_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class GetOutliningForObjectsInArrayCase
{
    public static void TestGetOutliningForObjectsInArray(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("const x =[| [\n    [|{ a: 0 }|],\n    [|{ b: 1 }|],\n    [|{ c: 2 }|]\n]|];\n\nconst y =[| [\n    [|{\n        a: 0\n    }|],\n    [|{\n        b: 1\n    }|],\n    [|{\n        c: 2\n    }|]\n]|];\n\nconst w =[| [\n    [|[ 0 ]|],\n    [|[ 1 ]|],\n    [|[ 2 ]|]\n]|];\n\nconst z =[| [\n    [|[\n        0\n    ]|],\n    [|[\n        1\n    ]|],\n    [|[\n        2\n    ]|]\n]|];\n\nconst z =[| [\n    [|[\n        [|{ hello: 0 }|]\n    ]|],\n    [|[\n        [|{ hello: 3 }|]\n    ]|],\n    [|[\n        [|{ hello: 5 }|],\n        [|{ hello: 7 }|]\n    ]|]\n]|];"u8);
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
