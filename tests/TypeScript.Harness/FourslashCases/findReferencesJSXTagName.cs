// ported from: internal/fourslash/tests/findReferencesJSXTagName_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class FindReferencesJSXTagNameCase
{
    public static void TestFindReferencesJSXTagName(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @Filename: index.tsx\nimport { /*1*/SubmissionComp } from \"./RedditSubmission\"\nfunction displaySubreddit(subreddit: string) {\n    let components = submissions\n        .map((value, index) => <SubmissionComp key={ index } elementPosition= { index } {...value.data} />);\n}\n// @Filename: RedditSubmission.ts\nexport const /*2*/SubmissionComp = (submission: SubmissionProps) =>\n    <div style={{ fontFamily: \"sans-serif\" }}></div>;"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyBaselineFindAllReferences(t, "1", "2");
            }
            finally
            {
                done();
            }
        });
    }

}
