// ported from: internal/fourslash/tests/renameDestructuringClassProperty_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class RenameDestructuringClassPropertyCase
{
    public static void TestRenameDestructuringClassProperty(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("class A {\n    [|[|{| \"contextRangeIndex\": 0 |}foo|]: string;|]\n}\nclass B {\n    syntax1(a: A): void {\n        [|let { [|{| \"contextRangeIndex\": 2 |}foo|] } = a;|]\n    }\n    syntax2(a: A): void {\n        [|let { [|{| \"contextRangeIndex\": 4 |}foo|]: foo } = a;|]\n    }\n    syntax11(a: A): void {\n        [|let { [|{| \"contextRangeIndex\": 6 |}foo|] } = a;|]\n        [|foo|] = \"newString\";\n    }\n}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyBaselineRename(t, null!, f.Ranges()[1], f.Ranges()[5], f.Ranges()[3], f.Ranges()[7], f.Ranges()[8]);
            }
            finally
            {
                done();
            }
        });
    }

}
