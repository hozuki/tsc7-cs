// ported from: internal/fourslash/tests/memberListInsideObjectLiterals_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using LSP = TypeScript.LsProto;
using TSU = TypeScript.TestUtil;
using U = TypeScript.Harness.FourslashCases.FourslashTestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class MemberListInsideObjectLiteralsCase
{
    public static void TestMemberListInsideObjectLiterals(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("namespace ObjectLiterals {\n    interface MyPoint {\n        x1: number;\n        y1: number;\n    }\n\n    var p1: MyPoint = {\n        /*1*/\n    };\n\n    var p2: MyPoint = {\n        x1: 5,\n        /*2*/\n    };\n\n    var p3: MyPoint = {\n        x1/*3*/:\n    };\n\n    var p4: MyPoint = {\n        /*4*/y1\n    };\n}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyCompletions(t, new F.MarkerInput { MarkerNames = ["1", "3", "4"] }, new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Exact = [new LSP.CompletionItem { Label = "x1", Detail = "(property) MyPoint.x1: number" }, new LSP.CompletionItem { Label = "y1", Detail = "(property) MyPoint.y1: number" }] } });
                f.VerifyCompletions(t, new F.MarkerInput { MarkerNames = ["2"] }, new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Exact = [new LSP.CompletionItem { Label = "y1", Detail = "(property) MyPoint.y1: number" }] } });
            }
            finally
            {
                done();
            }
        });
    }

}
