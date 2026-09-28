// ported from: internal/fourslash/tests/completionListOnAliases2_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using LS = TypeScript.Ls.Ls;
using LSP = TypeScript.LsProto;
using TSU = TypeScript.TestUtil;
using U = TypeScript.Harness.FourslashCases.FourslashTestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class CompletionListOnAliases2Case
{
    public static void TestCompletionListOnAliases2(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @lib: es5\nnamespace M {\n    export interface I { }\n    export class C {\n        static property;\n    }\n    export enum E {\n        value = 0\n    }\n    export namespace N {\n        export var v;\n    }\n    export var V = 0;\n    export function F() { }\n    export import A = M;\n}\n\nimport m = M;\nimport c = M.C;\nimport e = M.E;\nimport n = M.N;\nimport v = M.V;\nimport f = M.F;\nimport a = M.A;\n\nm./*1*/;\nvar tmp: m./*1Type*/;\nc./*2*/;\ne./*3*/;\nn./*4*/;\nv./*5*/;\nf./*6*/;\na./*7*/;\nvar tmp2: a./*7Type*/;"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyCompletions(t, new F.MarkerInput { MarkerNames = ["1", "7"] }, new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Unsorted = ["F", "C", "E", "N", "V", "A"] } });
                f.VerifyCompletions(t, new F.MarkerInput { MarkerNames = ["1Type", "7Type"] }, new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Unsorted = ["I", "C", "E", "A"] } });
                f.VerifyCompletions(t, "2", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Exact = U.CompletionFunctionMembersPlus([new LSP.CompletionItem { Label = "property", SortText = LS.SortTextLocalDeclarationPriority }, new LSP.CompletionItem { Label = "prototype", SortText = LS.SortTextLocationPriority }]) } });
                f.VerifyCompletions(t, "3", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Exact = ["value"] } });
                f.VerifyCompletions(t, "4", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Exact = ["v"] } });
                f.VerifyCompletions(t, "5", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Includes = ["toFixed"] } });
                f.VerifyCompletions(t, "6", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Includes = ["call"] } });
            }
            finally
            {
                done();
            }
        });
    }

}
