// ported from: internal/fourslash/tests/inlayHintsVariableTypes1_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using LSUP = TypeScript.LsUtil;
using TRI = TypeScript.Core.Tristate;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class InlayHintsVariableTypes1Case
{
    public static void TestInlayHintsVariableTypes1(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("class C {}\nnamespace N { export class Foo {} }\ninterface Foo {}\nconst a = \"a\";\nconst b = 1;\nconst c = true;\nconst d = {} as Foo;\nconst e = <Foo>{};\nconst f = {} as const;\nconst g = (({} as const));\nconst h = new C();\nconst i = new N.C();\nconst j = ((((new C()))));\nconst k = { a: 1, b: 1 };\nconst l = ((({ a: 1, b: 1 })));\n const m = () => 123;\n const n;"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyBaselineInlayHints(t, null!, new LSUP.UserPreferences { InlayHints = new LSUP.InlayHintsPreferences { IncludeInlayVariableTypeHints = TRI.TSTrue } });
            }
            finally
            {
                done();
            }
        });
    }

}
