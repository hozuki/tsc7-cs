// ported from: internal/fourslash/tests/completionClassMemberAfterJSDocWithInvalidJSDocTagInTheComment1_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class CompletionClassMemberAfterJSDocWithInvalidJSDocTagInTheComment1Case
{
    public static void TestCompletionClassMemberAfterJSDocWithInvalidJSDocTagInTheComment1(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("export class NeedsPrefix {\n  private _prefixes: {\n    add: Record<string, any>;\n    browsers: {selected: string[]};\n  };\n\n  constructor(browsers: string[]) {\n  }\n\n  /** Checks whether an @-rule needs to be prefixed. */\n  /**/atRule(identifier: string): boolean {\n    return true;\n  }\n}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyCompletions(t, "", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = [], EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems {  } });
            }
            finally
            {
                done();
            }
        });
    }

}
