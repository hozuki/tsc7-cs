// ported from: internal/fourslash/tests/completionsLiteralOverload_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;
using U = TypeScript.Harness.FourslashCases.FourslashTestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class CompletionsLiteralOverloadCase
{
    public static void TestCompletionsLiteralOverload(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @allowJs: true\n// @Filename: /a.tsx\ninterface Events {\n  \"\": any;\n  drag: any;\n  dragenter: any;\n}\ndeclare function addListener<K extends keyof Events>(type: K, listener: (ev: Events[K]) => any): void;\n\ndeclare function ListenerComponent<K extends keyof Events>(props: { type: K, onWhatever: (ev: Events[K]) => void }): JSX.Element;\n\naddListener(\"/*ts*/\");\n(<ListenerComponent type=\"/*tsx*/\" />);\n// @Filename: /b.js\naddListener(\"/*js*/\");"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyCompletions(t, new F.MarkerInput { MarkerNames = ["ts", "tsx", "js"] }, new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Exact = ["", "drag", "dragenter"] } });
                f.Insert(t, "drag");
                f.VerifyCompletions(t, new F.MarkerInput { MarkerNames = ["ts", "tsx", "js"] }, new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Exact = ["", "drag", "dragenter"] } });
            }
            finally
            {
                done();
            }
        });
    }

}
