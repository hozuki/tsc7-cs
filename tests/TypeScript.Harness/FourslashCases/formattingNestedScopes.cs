// ported from: internal/fourslash/tests/formattingNestedScopes_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class FormattingNestedScopesCase
{
    public static void TestFormattingNestedScopes(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("/*1*/        namespace      My.App      {\n/*2*/export      var appModule =      angular.module(\"app\", [\n/*3*/            ]).config([() =>            {\n/*4*/                        configureStates\n/*5*/($stateProvider);\n/*6*/}]).run(My.App.setup);\n/*7*/      }"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.FormatDocument(t, "");
                f.GoToMarker(t, "1");
                f.VerifyCurrentLineContent(t, "namespace My.App {");
                f.GoToMarker(t, "2");
                f.VerifyCurrentLineContent(t, "    export var appModule = angular.module(\"app\", [");
                f.GoToMarker(t, "3");
                f.VerifyCurrentLineContent(t, "    ]).config([() => {");
                f.GoToMarker(t, "4");
                f.VerifyCurrentLineContent(t, "        configureStates");
                f.GoToMarker(t, "5");
                f.VerifyCurrentLineContent(t, "            ($stateProvider);");
                f.GoToMarker(t, "6");
                f.VerifyCurrentLineContent(t, "    }]).run(My.App.setup);");
                f.GoToMarker(t, "7");
                f.VerifyCurrentLineContent(t, "}");
            }
            finally
            {
                done();
            }
        });
    }

}
