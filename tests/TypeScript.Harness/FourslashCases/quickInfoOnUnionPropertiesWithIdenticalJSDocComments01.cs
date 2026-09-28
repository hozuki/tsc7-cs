// ported from: internal/fourslash/tests/quickInfoOnUnionPropertiesWithIdenticalJSDocComments01_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class QuickInfoOnUnionPropertiesWithIdenticalJSDocComments01Case
{
    public static void TestQuickInfoOnUnionPropertiesWithIdenticalJSDocComments01(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("export type DocumentFilter = {\n    /** A language id, like `typescript`. */\n    language: string;\n    /** A Uri [scheme](#Uri.scheme), like `file` or `untitled`. */\n    scheme?: string;\n    /** A glob pattern, like `*.{ts,js}`. */\n    pattern?: string;\n} | {\n    /** A language id, like `typescript`. */\n    language?: string;\n    /** A Uri [scheme](#Uri.scheme), like `file` or `untitled`. */\n    scheme: string;\n    /** A glob pattern, like `*.{ts,js}`. */\n    pattern?: string;\n} | {\n    /** A language id, like `typescript`. */\n    language?: string;\n    /** A Uri [scheme](#Uri.scheme), like `file` or `untitled`. */\n    scheme?: string;\n    /** A glob pattern, like `*.{ts,js}`. */\n    pattern: string;\n};\n\ndeclare let x: DocumentFilter;\nx./**/language"u8);
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
