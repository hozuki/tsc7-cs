// ported from: internal/fourslash/tests/linkedEditingJsxTag8_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using LSP = TypeScript.LsProto;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class LinkedEditingJsxTag8Case
{
    public static void TestLinkedEditingJsxTag8(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @FileName: /mismatchedNames.tsx\nconst A = thing;\nconst B = thing;\nconst jsx = (\n    </*8*/A>\n    </B>\n);"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyLinkedEditing(t, new Dictionary<string, LSP.Range[]> { ["8"] = [] });
            }
            finally
            {
                done();
            }
        });
    }

}
