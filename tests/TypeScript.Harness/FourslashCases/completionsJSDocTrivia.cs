// ported from: internal/fourslash/tests/completionsJSDocTrivia_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class CompletionsJSDocTriviaCase
{
    public static void TestCompletionsJSDocTrivia(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @noLib: true\n/**\n * @type {{\n * 'string-property': boolean;\n */*$*/ identifierProperty: boolean;\n * }}\n */\nvar someVariable;"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.GoToMarker(t, "$");
                f.VerifyCompletions(t, null!, new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = [".", ",", ";"], EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems {  } });
            }
            finally
            {
                done();
            }
        });
    }

}
