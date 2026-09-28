// ported from: internal/fourslash/tests/organizeImports_sortModuleSpecifiers_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using LSP = TypeScript.LsProto;
using LSUP = TypeScript.LsUtil;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class OrganizeImports_sortModuleSpecifiersCase
{
    public static void TestOrganizeImports_sortModuleSpecifiers_nonRelativeVsNonRelative(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("import x from \"lib2\";\nimport y from \"lib1\";\nx; y;"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyOrganizeImports(t, "import y from \"lib1\";\nimport x from \"lib2\";\nx; y;", LSP.CodeActionKind.SourceSortImportsTs, new LSUP.UserPreferences { OrganizeImportsSort = LSUP.OrganizeImportsSort.OrdinalIgnoreCase });
            }
            finally
            {
                done();
            }
        });
    }

    public static void TestOrganizeImports_sortModuleSpecifiers_relativeVsRelative(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("import x from \"./lib2\";\nimport y from \"./lib1\";\nx; y;"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyOrganizeImports(t, "import y from \"./lib1\";\nimport x from \"./lib2\";\nx; y;", LSP.CodeActionKind.SourceSortImportsTs, new LSUP.UserPreferences { OrganizeImportsSort = LSUP.OrganizeImportsSort.OrdinalIgnoreCase });
            }
            finally
            {
                done();
            }
        });
    }

    public static void TestOrganizeImports_sortModuleSpecifiers_relativeVsNonRelative(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("import x from \"./lib\";\nimport y from \"lib\";\nx; y;"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyOrganizeImports(t, "import y from \"lib\";\nimport x from \"./lib\";\nx; y;", LSP.CodeActionKind.SourceSortImportsTs, new LSUP.UserPreferences { OrganizeImportsSort = LSUP.OrganizeImportsSort.OrdinalIgnoreCase });
            }
            finally
            {
                done();
            }
        });
    }

    public static void TestOrganizeImports_sortModuleSpecifiers_caseInsensitive(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("import x from \"Z\";\nimport y from \"a\";\nx; y;"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyOrganizeImports(t, "import y from \"a\";\nimport x from \"Z\";\nx; y;", LSP.CodeActionKind.SourceSortImportsTs, new LSUP.UserPreferences { OrganizeImportsSort = LSUP.OrganizeImportsSort.OrdinalIgnoreCase });
            }
            finally
            {
                done();
            }
        });
    }

    public static void TestOrganizeImports_sortModuleSpecifiers_caseInsensitiveReverse(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("import x from \"z\";\nimport y from \"A\";\nx; y;"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyOrganizeImports(t, "import y from \"A\";\nimport x from \"z\";\nx; y;", LSP.CodeActionKind.SourceSortImportsTs, new LSUP.UserPreferences { OrganizeImportsSort = LSUP.OrganizeImportsSort.OrdinalIgnoreCase });
            }
            finally
            {
                done();
            }
        });
    }

}
