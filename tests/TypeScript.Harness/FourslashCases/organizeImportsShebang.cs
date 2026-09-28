// ported from: internal/fourslash/tests/organizeImportsShebang_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using LSP = TypeScript.LsProto;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class OrganizeImportsShebangCase
{
    public static void TestOrganizeImports_Shebang_PreserveAndSort(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("#!/usr/bin/env node\nimport Foo from \"foo\";\nimport Bar from \"bar\";\n\nimport Foobar from \"foobar\";\n\nconsole.log(Foo, Bar, Foobar);"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyOrganizeImports(t, "#!/usr/bin/env node\nimport Bar from \"bar\";\nimport Foo from \"foo\";\n\nimport Foobar from \"foobar\";\n\nconsole.log(Foo, Bar, Foobar);", LSP.CodeActionKind.SourceOrganizeImportsTs, null!);
            }
            finally
            {
                done();
            }
        });
    }

}
