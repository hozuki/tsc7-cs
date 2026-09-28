// ported from: internal/fourslash/tests/javascriptModules22_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;
using U = TypeScript.Harness.FourslashCases.FourslashTestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class JavascriptModules22Case
{
    public static void TestJavascriptModules22(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @allowJs: true\n// @module: commonjs\n// @allowSyntheticDefaultImports: false\n// @esModuleInterop: false\n// @Filename: mod.js\nfunction foo() { return {a: \"hello, world\"}; }\nmodule.exports = foo();\n// @Filename: mod2.js\nvar x = {name: 'test'};\n(function createExport(obj){\n    module.exports = {\n        \"default\": x,\n        \"sausages\": {eggs: 2}\n    };\n})();\n// @Filename: app.js\nimport {a} from \"./mod\"\nimport def, {sausages} from \"./mod2\"\na./**/"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyCompletions(t, "", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Includes = ["toString"] } });
                f.Backspace(t, 2);
                f.Insert(t, "def.");
                f.VerifyCompletions(t, null!, new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Includes = ["name"] } });
                f.Insert(t, "name;\nsausages.");
                f.VerifyCompletions(t, null!, new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Includes = ["eggs"] } });
                f.Insert(t, "eggs;");
                f.VerifyNoErrors(t);
            }
            finally
            {
                done();
            }
        });
    }

}
