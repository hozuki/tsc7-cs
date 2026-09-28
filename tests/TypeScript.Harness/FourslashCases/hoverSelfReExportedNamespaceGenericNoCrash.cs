// ported from: internal/fourslash/tests/hoverSelfReExportedNamespaceGenericNoCrash_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class HoverSelfReExportedNamespaceGenericNoCrashCase
{
    public static void TestHoverSelfReExportedNamespaceGenericNoCrash(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @filename: mod.ts\nexport interface Box<A> { content: Content<A> }\nexport type Content<A> = A\nexport * as Box from \"./mod\"\n// @filename: main.ts\nimport { Box } from \"./mod\"\ndeclare const b: Box<string>\nconst x = b./*1*/content\n"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyBaselineHover(t);
            }
            finally
            {
                done();
            }
        });
    }

    public static void TestHoverSelfReExportedNamespaceGenericClassNoCrash(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @filename: mod.ts\nexport class Box<A> { content!: A }\nexport * as Box from \"./mod\"\n// @filename: main.ts\nimport { Box } from \"./mod\"\ndeclare const b: Box<string>\nconst x = b./*1*/content\n"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyBaselineHover(t);
            }
            finally
            {
                done();
            }
        });
    }

    public static void TestHoverNamespaceExportGenericNonColliding(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @filename: mod.ts\nexport interface Box<A> { content: A }\nexport * as BoxNS from \"./mod\"\n// @filename: main.ts\nimport { Box } from \"./mod\"\ndeclare const b: Box<string>\nconst x = b./*1*/content\n"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyBaselineHover(t);
            }
            finally
            {
                done();
            }
        });
    }

}
