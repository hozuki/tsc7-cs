// ported from: internal/fourslash/tests/javascriptModules24_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class JavascriptModules24Case
{
    public static void TestJavascriptModules24(TSU.IHarnessTest t)
    {
        t.Skip("Known failing fourslash test");
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @Filename: mod.ts\nfunction foo() { return 42; }\nnamespace foo {\n  export function bar (a: string) { return a; }\n}\nexport = foo;\n// @Filename: app.ts\nimport * as foo from \"./mod\"\nfoo/*1*/();\nfoo.bar(/*2*/\"test\");"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.GoToMarker(t, "1");
                f.VerifyErrorExistsBeforeMarker(t, "1");
                f.VerifyQuickInfoIs(t, "(alias) function foo(): number\n(alias) namespace foo\nimport foo", "");
                f.GoToMarker(t, "2");
                f.VerifySignatureHelp(t, new F.VerifySignatureHelpOptions {  });
            }
            finally
            {
                done();
            }
        });
    }

}
