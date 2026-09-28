// ported from: internal/fourslash/tests/referencesForInheritedProperties10_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class ReferencesForInheritedProperties10Case
{
    public static void TestReferencesForInheritedProperties10(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("interface IFeedbackHandler {\n  /*1*/handleAccept?(): void;\n  handleReject?(): void;\n}\n\nabstract class AbstractFeedbackHandler implements IFeedbackHandler {}\n\nclass FeedbackHandler extends AbstractFeedbackHandler {\n  /*2*/handleAccept(): void {\n    console.log(\"Feedback accepted\");\n  }\n\n  handleReject(): void {\n    console.log(\"Feedback rejected\");\n  }\n}\n\nfunction foo(handler: IFeedbackHandler) {\n  handler./*3*/handleAccept?.();\n  handler.handleReject?.();\n}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyBaselineFindAllReferences(t, "1", "2", "3");
            }
            finally
            {
                done();
            }
        });
    }

}
