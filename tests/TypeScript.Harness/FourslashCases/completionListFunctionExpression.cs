// ported from: internal/fourslash/tests/completionListFunctionExpression_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;
using U = TypeScript.Harness.FourslashCases.FourslashTestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class CompletionListFunctionExpressionCase
{
    public static void TestCompletionListFunctionExpression(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @lib: es5\nclass DataHandler {\n    dataArray: Uint8Array;\n    loadData(filename) {\n        var xmlReq = new XMLHttpRequest();\n        xmlReq.open(\"GET\", \"/\" + filename, true);\n        xmlReq.responseType = \"arraybuffer\";\n        xmlReq.onload = function(xmlEvent) {\n            /*local*/\n            this./*this*/;\n        }\n    }\n}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.GoToMarker(t, "local");
                f.InsertLine(t, "");
                f.VerifyCompletions(t, null!, new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Includes = ["xmlEvent"] } });
                f.VerifyCompletions(t, "this", null!);
            }
            finally
            {
                done();
            }
        });
    }

}
