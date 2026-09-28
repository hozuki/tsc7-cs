// ported from: internal/fourslash/tests/organizeImports_removeUnused_preservesMultiline_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using LSP = TypeScript.LsProto;
using LSU = TypeScript.LsUtil.LsUtil;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class OrganizeImports_removeUnused_preservesMultilineCase
{
    public static void TestOrganizeImports_removeUnused_preservesMultiline(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("import {\n    a,\n    b,\n    c,\n} from \"module\";\n\nexport { a, b, c };"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyOrganizeImports(t, "import {\n    a,\n    b,\n    c,\n} from \"module\";\n\nexport { a, b, c };", LSP.CodeActionKind.SourceRemoveUnusedImportsTs, null!);
            }
            finally
            {
                done();
            }
        });
    }

    public static void TestOrganizeImports_removeUnused_preservesMultilineWithRemoval(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("import {\n    a,\n    b,\n    c,\n} from \"module\";\n\nexport { a, c };"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyOrganizeImports(t, "import {\n    a,\n    c\n} from \"module\";\n\nexport { a, c };", LSP.CodeActionKind.SourceRemoveUnusedImportsTs, null!);
            }
            finally
            {
                done();
            }
        });
    }

    public static void TestOrganizeImports_removeUnusedUsesLanguageServiceFormatOptions(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("import {\n    a,\n    b,\n    c,\n} from \"module\";\n\nexport { a, c };"u8);
            var preferences = LSU.ParseUserPreferences(new Dictionary<string, object?> { ["editor"] = new Dictionary<string, object?> { ["tabSize"] = 2, ["insertSpaces"] = false } });
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyOrganizeImports(t, "import {\n\ta,\n\tc\n} from \"module\";\n\nexport { a, c };", LSP.CodeActionKind.SourceRemoveUnusedImportsTs, preferences);
            }
            finally
            {
                done();
            }
        });
    }

}
