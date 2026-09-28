// ported from: internal/fourslash/tests/getOutliningForArrayDestructuring_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class GetOutliningForArrayDestructuringCase
{
    public static void TestGetOutliningForArrayDestructuring(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("const[| [\n    a,\n    b,\n    c\n]|] =[| [\n    1,\n    2,\n    3\n]|];\nconst[| [\n    [|[\n        [|[\n            [|[\n                a,\n                b,\n                c\n            ]|]\n        ]|]\n    ]|],\n    [|[\n        a1,\n        b1,\n        c1\n    ]|]\n]|] =[| [\n    [|[\n        [|[\n            [|[\n                1,\n                2,\n                3\n            ]|]\n        ]|]\n    ]|],\n    [|[\n        1,\n        2,\n        3\n    ]|]\n]|]"u8);
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
