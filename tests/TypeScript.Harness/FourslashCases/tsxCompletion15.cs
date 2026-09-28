// ported from: internal/fourslash/tests/tsxCompletion15_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;
using U = TypeScript.Harness.FourslashCases.FourslashTestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class TsxCompletion15Case
{
    public static void TestTsxCompletion15(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("//@module: commonjs\n//@jsx: preserve\ndeclare namespace JSX {\n    interface Element { }\n    interface IntrinsicElements {\n    }\n    interface ElementAttributesProperty { props; }\n}\n//@Filename: exporter.tsx\nexport namespace M {\n   export declare function SFCComp(props: { Three: number; Four: string }): JSX.Element;\n}\n//@Filename: file.tsx\nimport * as Exp from './exporter';\nvar x1  = <Exp.M.SFCComp></[|/*1*/|]>;\nvar x2  = <Exp.M.SFCComp></[|Exp./*2*/|]>;\nvar x3  = <Exp.M.SFCComp></[|Exp.M./*3*/|]>;\nvar x4  = <Exp.M.SFCComp></[|Exp.M.SFCComp/*4*/|]\nvar x5  = <Exp.M.SFCComp></[|Exp.M.SFCComp/*5*/|]>;\nvar x6  = <Exp.M.SFCComp></      [|Exp./*6*/|]>;\nvar x7  = <Exp.M.SFCComp></[|/*7*/Exp.M.SFCComp|]>;\nvar x8  = <Exp.M.SFCComp></[|Exp/*8*/|]>;\nvar x9  = <Exp.M.SFCComp></[|Exp.M./*9*/|]>;\nvar x10 = <Exp.M.SFCComp></      [|/*10*/Exp.M.Foo.Bar.Baz.Wut|]>;\nvar x11 = <Exp.M.SFCComp></[|Exp./*11*/M.SFCComp|]>;\nvar x12 = <Exp.M.SFCComp><div><span /></div></[|Exp.M./*12*/SFCComp|]>;"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyCompletions(t, "1", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Exact = ["Exp.M.SFCComp"] } });
                f.VerifyCompletions(t, "2", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Exact = ["Exp.M.SFCComp"] } });
                f.VerifyCompletions(t, "3", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Exact = ["Exp.M.SFCComp"] } });
                f.VerifyCompletions(t, "4", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Exact = ["Exp.M.SFCComp>"] } });
                f.VerifyCompletions(t, "5", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Exact = ["Exp.M.SFCComp"] } });
                f.VerifyCompletions(t, "6", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Exact = ["Exp.M.SFCComp"] } });
                f.VerifyCompletions(t, "7", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Exact = ["Exp.M.SFCComp"] } });
                f.VerifyCompletions(t, "8", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Exact = ["Exp.M.SFCComp"] } });
                f.VerifyCompletions(t, "9", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Exact = ["Exp.M.SFCComp"] } });
                f.VerifyCompletions(t, "10", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Exact = ["Exp.M.SFCComp"] } });
                f.VerifyCompletions(t, "11", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Exact = ["Exp.M.SFCComp"] } });
                f.VerifyCompletions(t, "12", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Exact = ["Exp.M.SFCComp"] } });
            }
            finally
            {
                done();
            }
        });
    }

}
