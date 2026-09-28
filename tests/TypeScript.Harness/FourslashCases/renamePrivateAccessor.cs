// ported from: internal/fourslash/tests/renamePrivateAccessor_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;
using U = TypeScript.Harness.FourslashCases.FourslashTestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class RenamePrivateAccessorCase
{
    public static void TestRenamePrivateAccessor(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("class Foo {\n   [|get [|{| \"contextRangeIndex\": 0 |}#foo|]() { return 1 }|]\n   [|set [|{| \"contextRangeIndex\": 2 |}#foo|](value: number) { }|]\n   retFoo() {\n       return this.[|#foo|];\n   }\n}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyBaselineRename(t, null!, U.ToAny(f.GetRangesByText().Get("#foo")!));
            }
            finally
            {
                done();
            }
        });
    }

}
