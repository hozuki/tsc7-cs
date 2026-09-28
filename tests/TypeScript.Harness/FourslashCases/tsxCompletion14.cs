// ported from: internal/fourslash/tests/tsxCompletion14_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;
using U = TypeScript.Harness.FourslashCases.FourslashTestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class TsxCompletion14Case
{
    public static void TestTsxCompletion14(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("//@module: commonjs\n//@jsx: preserve\ndeclare namespace JSX {\n    interface Element { }\n    interface IntrinsicElements {\n    }\n    interface ElementAttributesProperty { props; }\n}\n//@Filename: exporter.tsx\nexport class Thing { props: { ONE: string; TWO: number } }\nexport namespace M {\n   export declare function SFCComp(props: { Three: number; Four: string }): JSX.Element;\n}\n//@Filename: file.tsx\nimport * as Exp from './exporter';\nvar x1 = <Exp.Thing /*1*/ />;\nvar x2 = <Exp.M.SFCComp /*2*/ />;\nvar x3 = <Exp.Thing /*3*/ ></Exp.Thing>;\nvar x4 = <Exp.M.SFCComp /*4*/ ></Exp.M.SFCComp>;"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyCompletions(t, new F.MarkerInput { MarkerNames = ["1", "3"] }, new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Exact = ["ONE", "TWO"] } });
                f.VerifyCompletions(t, new F.MarkerInput { MarkerNames = ["2", "4"] }, new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Exact = ["Four", "Three"] } });
            }
            finally
            {
                done();
            }
        });
    }

}
