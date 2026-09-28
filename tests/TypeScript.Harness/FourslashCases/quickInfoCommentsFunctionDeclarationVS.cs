// ported from: internal/fourslash/tests/quickInfoCommentsFunctionDeclarationVS_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using LSP = TypeScript.LsProto;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class QuickInfoCommentsFunctionDeclarationVSCase
{
    public static void TestQuickInfoCommentsFunctionDeclarationVS(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("/** This comment should appear for foo*/\nfunction f/*1*/oo() {\n}\nf/*2*/oo();\n/** This is comment for function signature*/\nfunction fo/*5*/oWithParameters(/** this is comment about a*/a: string,\n    /** this is comment for b*/\n    b: number) {\n    var /*6*/d = a;\n}\nfooWithParam/*8*/eters(\"a\",10);\n/**\n* Does something\n* @param a a string\n*/\ndeclare function fn(a: string);\nfn(\"hello\");"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, new LSP.ClientCapabilities { VSSupportsVisualStudioExtensions = true }, content);
            try
            {
                f.VerifyBaselineVSHover(t);
            }
            finally
            {
                done();
            }
        });
    }

}
