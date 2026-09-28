// ported from: internal/fourslash/tests/completionsJSDocNoCrash1_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class CompletionsJSDocNoCrash1Case
{
    public static void TestCompletionsJSDocNoCrash1(TSU.IHarnessTest t)
    {
        t.Skip("Known failing fourslash test");
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @strict: true\n// @checkJs: true\n// @allowJs: true\n// @filename: index.js\n/**\n * @example\n  <file name=\"glyphicons.css\">\n    @import url(//netdna.bootstrapcdn.com/bootstrap/3.0.0/css/bootstrap-glyphicons.css);\n  </file>\n  <example module=\"ngAnimate\" deps=\"angular-animate.js\" animations=\"true\">\n    <file name=\"animations.css\">\n      .animate-show.ng-hide-add.ng-hide-add-active,\n      .animate-show.ng-hide-remove.ng-hide-remove-active {\n        transition:all linear 0./**/5s;\n      }\n    </file>\n  </example>\n */\nvar ngShowDirective = ['$animate', function($animate) {}];"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyCompletions(t, "", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = [], EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Includes = ["url"] } });
            }
            finally
            {
                done();
            }
        });
    }

}
