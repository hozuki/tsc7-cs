// ported from: internal/fourslash/tests/organizeImports_coalesceExports_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using LSP = TypeScript.LsProto;
using LSUP = TypeScript.LsUtil;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class OrganizeImports_coalesceExportsCase
{
    public static void TestOrganizeImports_coalesceExports_sortSpecifiersCaseInsensitive(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("export { default as M, a as n, B, y, Z as O } from \"lib\";\nvoid 0;"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyOrganizeImports(t, "export { B, default as M, a as n, Z as O, y } from \"lib\";\nvoid 0;", LSP.CodeActionKind.SourceSortImportsTs, new LSUP.UserPreferences { OrganizeImportsSort = LSUP.OrganizeImportsSort.OrdinalIgnoreCase });
            }
            finally
            {
                done();
            }
        });
    }

    public static void TestOrganizeImports_coalesceExports_combineNamespaceReExports(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("export * from \"lib\";\nexport * from \"lib\";\nvoid 0;"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyOrganizeImports(t, "export * from \"lib\";\nvoid 0;", LSP.CodeActionKind.SourceSortImportsTs, new LSUP.UserPreferences { OrganizeImportsSort = LSUP.OrganizeImportsSort.OrdinalIgnoreCase });
            }
            finally
            {
                done();
            }
        });
    }

    public static void TestOrganizeImports_coalesceExports_combinePropertyExports(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("const x = 1, z = 2;\nexport { x };\nexport { z as y };\nvoid 0;"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyOrganizeImports(t, "const x = 1, z = 2;\nexport { x, z as y };\nvoid 0;", LSP.CodeActionKind.SourceSortImportsTs, new LSUP.UserPreferences { OrganizeImportsSort = LSUP.OrganizeImportsSort.OrdinalIgnoreCase });
            }
            finally
            {
                done();
            }
        });
    }

    public static void TestOrganizeImports_coalesceExports_combinePropertyReExports(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("export { x } from \"lib\";\nexport { y as z } from \"lib\";\nvoid 0;"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyOrganizeImports(t, "export { x, y as z } from \"lib\";\nvoid 0;", LSP.CodeActionKind.SourceSortImportsTs, new LSUP.UserPreferences { OrganizeImportsSort = LSUP.OrganizeImportsSort.OrdinalIgnoreCase });
            }
            finally
            {
                done();
            }
        });
    }

    public static void TestOrganizeImports_coalesceExports_namespaceWithPropertyReExport(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("export * from \"lib\";\nexport { y } from \"lib\";\nexport { z } from \"aaa\";\nvoid 0;"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyOrganizeImports(t, "export { z } from \"aaa\";\nexport * from \"lib\";\nexport { y } from \"lib\";\nvoid 0;", LSP.CodeActionKind.SourceSortImportsTs, new LSUP.UserPreferences { OrganizeImportsSort = LSUP.OrganizeImportsSort.OrdinalIgnoreCase });
            }
            finally
            {
                done();
            }
        });
    }

    public static void TestOrganizeImports_coalesceExports_combineMany(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("const x = 1, w = 2, z = 3, q = 4;\nexport { x };\nexport { w as y, z as default };\nexport { q as w };\nvoid 0;"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyOrganizeImports(t, "const x = 1, w = 2, z = 3, q = 4;\nexport { z as default, q as w, x, w as y };\nvoid 0;", LSP.CodeActionKind.SourceSortImportsTs, new LSUP.UserPreferences { OrganizeImportsSort = LSUP.OrganizeImportsSort.OrdinalIgnoreCase });
            }
            finally
            {
                done();
            }
        });
    }

    public static void TestOrganizeImports_coalesceExports_combineManyReExports(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("export { x as a, y } from \"lib\";\nexport * from \"lib\";\nexport { z as b } from \"lib\";\nvoid 0;"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyOrganizeImports(t, "export * from \"lib\";\nexport { x as a, z as b, y } from \"lib\";\nvoid 0;", LSP.CodeActionKind.SourceSortImportsTs, new LSUP.UserPreferences { OrganizeImportsSort = LSUP.OrganizeImportsSort.OrdinalIgnoreCase });
            }
            finally
            {
                done();
            }
        });
    }

    public static void TestOrganizeImports_coalesceExports_keepTypeOnlySeparate(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("const x = 1;\ntype y = string;\nexport { x };\nexport type { y };\nexport { z } from \"aaa\";\nvoid 0;"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyOrganizeImports(t, "const x = 1;\ntype y = string;\nexport { z } from \"aaa\";\nexport { x };\nexport type { y };\nvoid 0;", LSP.CodeActionKind.SourceSortImportsTs, new LSUP.UserPreferences { OrganizeImportsSort = LSUP.OrganizeImportsSort.OrdinalIgnoreCase });
            }
            finally
            {
                done();
            }
        });
    }

    public static void TestOrganizeImports_coalesceExports_combineTypeOnly(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("type x = string;\ntype y = number;\nexport type { x };\nexport type { y };\nvoid 0;"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyOrganizeImports(t, "type x = string;\ntype y = number;\nexport type { x, y };\nvoid 0;", LSP.CodeActionKind.SourceSortImportsTs, new LSUP.UserPreferences { OrganizeImportsSort = LSUP.OrganizeImportsSort.OrdinalIgnoreCase });
            }
            finally
            {
                done();
            }
        });
    }

}
