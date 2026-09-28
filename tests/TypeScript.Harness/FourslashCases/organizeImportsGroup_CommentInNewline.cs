// ported from: internal/fourslash/tests/organizeImportsGroup_CommentInNewline_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using LSP = TypeScript.LsProto;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class OrganizeImportsGroup_CommentInNewlineCase
{
    public static void TestOrganizeImportsGroup_CommentInNewline(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// polyfill\nimport c from \"C\";\n// not polyfill\nimport d from \"D\";\nimport a from \"A\";\nimport b from \"B\";\n\nconsole.log(a, b, c, d)"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyOrganizeImports(t, "// polyfill\nimport c from \"C\";\n// not polyfill\nimport a from \"A\";\nimport b from \"B\";\nimport d from \"D\";\n\nconsole.log(a, b, c, d)", LSP.CodeActionKind.SourceOrganizeImportsTs, null!);
            }
            finally
            {
                done();
            }
        });
    }

}
