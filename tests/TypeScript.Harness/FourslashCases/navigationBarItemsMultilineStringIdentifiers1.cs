// ported from: internal/fourslash/tests/navigationBarItemsMultilineStringIdentifiers1_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class NavigationBarItemsMultilineStringIdentifiers1Case
{
    public static void TestNavigationBarItemsMultilineStringIdentifiers1(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("declare module \"Multiline\\r\\nMadness\" {\n}\n\ndeclare module \"Multiline\\\nMadness\" {\n}\ndeclare module \"MultilineMadness\" {}\n\ndeclare module \"Multiline\\\nMadness2\" {\n}\n\ninterface Foo {\n    \"a1\\\\\\r\\nb\";\n    \"a2\\\n    \\\n    b\"(): Foo;\n}\n\nclass Bar implements Foo {\n    'a1\\\\\\r\\nb': Foo;\n\n    'a2\\\n    \\\n    b'(): Foo {\n        return this;\n    }\n}"u8);
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
