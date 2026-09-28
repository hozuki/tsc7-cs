// ported from: internal/fourslash/tests/getOutliningForObjectDestructuring_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class GetOutliningForObjectDestructuringCase
{
    public static void TestGetOutliningForObjectDestructuring(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("const[| {\n    a,\n    b,\n    c\n}|] =[| {\n    a: 1,\n    b: 2,\n    c: 3\n}|]\nconst[| {\n    a:[| {\n        a_1,\n        a_2,\n        a_3:[| {\n            a_3_1,\n            a_3_2,\n            a_3_3,\n        }|],\n    }|],\n    b,\n    c\n}|] =[| {\n    a:[| {\n        a_1: 1,\n        a_2: 2,\n        a_3:[| {\n            a_3_1: 1,\n            a_3_2: 1,\n            a_3_3: 1\n        }|],\n    }|],\n    b: 2,\n    c: 3\n}|]"u8);
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
