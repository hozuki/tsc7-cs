// ported from: internal/fourslash/tests/organizeImports_coalesceImports_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using LSP = TypeScript.LsProto;
using LSUP = TypeScript.LsUtil;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class OrganizeImports_coalesceImportsCase
{
    public static void TestOrganizeImports_coalesceImports_sortSpecifiersCaseInsensitive(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("import { default as M, a as n, B, y, Z as O } from \"lib\";\nM; n; B; y; O;"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyOrganizeImports(t, "import { B, default as M, a as n, Z as O, y } from \"lib\";\nM; n; B; y; O;", LSP.CodeActionKind.SourceSortImportsTs, new LSUP.UserPreferences { OrganizeImportsSort = LSUP.OrganizeImportsSort.OrdinalIgnoreCase });
            }
            finally
            {
                done();
            }
        });
    }

    public static void TestOrganizeImports_coalesceImports_combineSideEffectOnly(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("import \"lib\";\nimport \"lib\";\nvoid 0;"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyOrganizeImports(t, "import \"lib\";\nvoid 0;", LSP.CodeActionKind.SourceSortImportsTs, new LSUP.UserPreferences { OrganizeImportsSort = LSUP.OrganizeImportsSort.OrdinalIgnoreCase });
            }
            finally
            {
                done();
            }
        });
    }

    public static void TestOrganizeImports_coalesceImports_combineNamespaceImportsNotMerged(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("import * as x from \"lib\";\nimport * as y from \"lib\";\nimport { z } from \"aaa\";\nx; y; z;"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyOrganizeImports(t, "import { z } from \"aaa\";\nimport * as x from \"lib\";\nimport * as y from \"lib\";\nx; y; z;", LSP.CodeActionKind.SourceSortImportsTs, new LSUP.UserPreferences { OrganizeImportsSort = LSUP.OrganizeImportsSort.OrdinalIgnoreCase });
            }
            finally
            {
                done();
            }
        });
    }

    public static void TestOrganizeImports_coalesceImports_combineDefaultImports(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("import x from \"lib\";\nimport y from \"lib\";\nx; y;"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyOrganizeImports(t, "import { default as x, default as y } from \"lib\";\nx; y;", LSP.CodeActionKind.SourceSortImportsTs, new LSUP.UserPreferences { OrganizeImportsSort = LSUP.OrganizeImportsSort.OrdinalIgnoreCase });
            }
            finally
            {
                done();
            }
        });
    }

    public static void TestOrganizeImports_coalesceImports_combinePropertyImports(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("import { x } from \"lib\";\nimport { y as z } from \"lib\";\nx; z;"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyOrganizeImports(t, "import { x, y as z } from \"lib\";\nx; z;", LSP.CodeActionKind.SourceSortImportsTs, new LSUP.UserPreferences { OrganizeImportsSort = LSUP.OrganizeImportsSort.OrdinalIgnoreCase });
            }
            finally
            {
                done();
            }
        });
    }

    public static void TestOrganizeImports_coalesceImports_sideEffectWithNamespace(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("import \"lib\";\nimport * as x from \"lib\";\nimport { z } from \"aaa\";\nx; z;"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyOrganizeImports(t, "import { z } from \"aaa\";\nimport \"lib\";\nimport * as x from \"lib\";\nx; z;", LSP.CodeActionKind.SourceSortImportsTs, new LSUP.UserPreferences { OrganizeImportsSort = LSUP.OrganizeImportsSort.OrdinalIgnoreCase });
            }
            finally
            {
                done();
            }
        });
    }

    public static void TestOrganizeImports_coalesceImports_sideEffectWithDefault(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("import \"lib\";\nimport x from \"lib\";\nimport { z } from \"aaa\";\nx; z;"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyOrganizeImports(t, "import { z } from \"aaa\";\nimport \"lib\";\nimport x from \"lib\";\nx; z;", LSP.CodeActionKind.SourceSortImportsTs, new LSUP.UserPreferences { OrganizeImportsSort = LSUP.OrganizeImportsSort.OrdinalIgnoreCase });
            }
            finally
            {
                done();
            }
        });
    }

    public static void TestOrganizeImports_coalesceImports_sideEffectWithProperty(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("import \"lib\";\nimport { x } from \"lib\";\nimport { z } from \"aaa\";\nx; z;"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyOrganizeImports(t, "import { z } from \"aaa\";\nimport \"lib\";\nimport { x } from \"lib\";\nx; z;", LSP.CodeActionKind.SourceSortImportsTs, new LSUP.UserPreferences { OrganizeImportsSort = LSUP.OrganizeImportsSort.OrdinalIgnoreCase });
            }
            finally
            {
                done();
            }
        });
    }

    public static void TestOrganizeImports_coalesceImports_namespaceWithDefault(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("import * as x from \"lib\";\nimport y from \"lib\";\nx; y;"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyOrganizeImports(t, "import y, * as x from \"lib\";\nx; y;", LSP.CodeActionKind.SourceSortImportsTs, new LSUP.UserPreferences { OrganizeImportsSort = LSUP.OrganizeImportsSort.OrdinalIgnoreCase });
            }
            finally
            {
                done();
            }
        });
    }

    public static void TestOrganizeImports_coalesceImports_namespaceWithProperty(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("import * as x from \"lib\";\nimport { y } from \"lib\";\nimport { z } from \"aaa\";\nx; y; z;"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyOrganizeImports(t, "import { z } from \"aaa\";\nimport * as x from \"lib\";\nimport { y } from \"lib\";\nx; y; z;", LSP.CodeActionKind.SourceSortImportsTs, new LSUP.UserPreferences { OrganizeImportsSort = LSUP.OrganizeImportsSort.OrdinalIgnoreCase });
            }
            finally
            {
                done();
            }
        });
    }

    public static void TestOrganizeImports_coalesceImports_defaultWithProperty(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("import x from \"lib\";\nimport { y } from \"lib\";\nx; y;"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyOrganizeImports(t, "import x, { y } from \"lib\";\nx; y;", LSP.CodeActionKind.SourceSortImportsTs, new LSUP.UserPreferences { OrganizeImportsSort = LSUP.OrganizeImportsSort.OrdinalIgnoreCase });
            }
            finally
            {
                done();
            }
        });
    }

    public static void TestOrganizeImports_coalesceImports_combineMany(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("import \"lib\";\nimport * as y from \"lib\";\nimport w from \"lib\";\nimport { b } from \"lib\";\nimport \"lib\";\nimport * as x from \"lib\";\nimport z from \"lib\";\nimport { a } from \"lib\";\nw; x; y; z; a; b;"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyOrganizeImports(t, "import \"lib\";\nimport * as x from \"lib\";\nimport * as y from \"lib\";\nimport { a, b, default as w, default as z } from \"lib\";\nw; x; y; z; a; b;", LSP.CodeActionKind.SourceSortImportsTs, new LSUP.UserPreferences { OrganizeImportsSort = LSUP.OrganizeImportsSort.OrdinalIgnoreCase });
            }
            finally
            {
                done();
            }
        });
    }

    public static void TestOrganizeImports_coalesceImports_twoNamespacesOneDefault(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("import * as x from \"lib\";\nimport * as y from \"lib\";\nimport z from \"lib\";\nimport { w } from \"aaa\";\nx; y; z; w;"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyOrganizeImports(t, "import { w } from \"aaa\";\nimport * as x from \"lib\";\nimport * as y from \"lib\";\nimport z from \"lib\";\nx; y; z; w;", LSP.CodeActionKind.SourceSortImportsTs, new LSUP.UserPreferences { OrganizeImportsSort = LSUP.OrganizeImportsSort.OrdinalIgnoreCase });
            }
            finally
            {
                done();
            }
        });
    }

    public static void TestOrganizeImports_coalesceImports_typeOnlySeparate(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("import type { x } from \"lib\";\nimport type { y } from \"lib\";\nimport { z } from \"lib\";\nx; y; z;"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyOrganizeImports(t, "import type { x, y } from \"lib\";\nimport { z } from \"lib\";\nx; y; z;", LSP.CodeActionKind.SourceSortImportsTs, new LSUP.UserPreferences { OrganizeImportsSort = LSUP.OrganizeImportsSort.OrdinalIgnoreCase });
            }
            finally
            {
                done();
            }
        });
    }

    public static void TestOrganizeImports_coalesceImports_typeOnlyKindsNotCombined(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("import type { x } from \"lib\";\nimport type * as y from \"lib\";\nimport type z from \"lib\";\nx; y; z;"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyOrganizeImports(t, "import type * as y from \"lib\";\nimport type z from \"lib\";\nimport type { x } from \"lib\";\nx; y; z;", LSP.CodeActionKind.SourceSortImportsTs, new LSUP.UserPreferences { OrganizeImportsSort = LSUP.OrganizeImportsSort.OrdinalIgnoreCase });
            }
            finally
            {
                done();
            }
        });
    }

    public static void TestOrganizeImports_coalesceImports_sortSpecifiersTypeOnlyInline(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("import { type z, y, type x, c, type b, a } from \"lib\";\nz; y; x; c; b; a;"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyOrganizeImports(t, "import { a, type b, c, type x, y, type z } from \"lib\";\nz; y; x; c; b; a;", LSP.CodeActionKind.SourceSortImportsTs, new LSUP.UserPreferences { OrganizeImportsSort = LSUP.OrganizeImportsSort.OrdinalIgnoreCase, OrganizeImportsTypeOrder = LSUP.OrganizeImportsTypeOrder.Inline });
            }
            finally
            {
                done();
            }
        });
    }

}
