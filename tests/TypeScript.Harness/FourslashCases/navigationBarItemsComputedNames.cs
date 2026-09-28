// ported from: internal/fourslash/tests/navigationBarItemsComputedNames_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class NavigationBarItemsComputedNamesCase
{
    public static void TestNavigationBarItemsComputedNames(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("const enum E {\n\tA = 'A',\n}\nconst a = '';\n\nclass C {\n    [a]() {\n        return 1;\n    }\n\n    [E.A]() {\n        return 1;\n    }\n\n    [1]() {\n        return 1;\n    },\n\n    [\"foo\"]() {\n        return 1;\n    },\n}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyBaselineDocumentSymbol(t);
            }
            finally
            {
                done();
            }
        });
    }

}
