// ported from: internal/fourslash/tests/codeFixCorrectQualifiedNameToIndexedAccessType01_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class CodeFixCorrectQualifiedNameToIndexedAccessType01Case
{
    public static void TestCodeFixCorrectQualifiedNameToIndexedAccessType01(TSU.IHarnessTest t)
    {
        t.Skip("Known failing fourslash test");
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("export interface Foo {\n  bar: string;\n}\nexport const x: [|Foo.bar|] = \"\""u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyRangeAfterCodeFix(t, "Foo[\"bar\"]", false, 0, 0);
            }
            finally
            {
                done();
            }
        });
    }

}
