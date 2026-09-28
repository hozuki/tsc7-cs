// ported from: internal/fourslash/tests/formatSpaceAfterImplementsExtends_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class FormatSpaceAfterImplementsExtendsCase
{
    public static void TestFormatSpaceAfterImplementsExtends(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("class C1 implements Array<string>{\n}\n\nclass C2 implements Number{\n}\n\nclass C3 extends Array<string>{\n}\n\nclass C4 extends Number{\n}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.FormatDocument(t, "");
                f.VerifyCurrentFileContent(t, "class C1 implements Array<string> {\n}\n\nclass C2 implements Number {\n}\n\nclass C3 extends Array<string> {\n}\n\nclass C4 extends Number {\n}");
            }
            finally
            {
                done();
            }
        });
    }

}
