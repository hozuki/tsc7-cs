// ported from: internal/fourslash/tests/inheritedModuleMembersForClodule2_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class InheritedModuleMembersForClodule2Case
{
    public static void TestInheritedModuleMembersForClodule2(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @strict: false\nnamespace M {\n    export namespace A {\n        var o;\n    }\n}\nnamespace M {\n    export class A { a = 1;}\n}\nnamespace M {\n    export class A { /**/b }\n}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.GoToMarker(t, "");
                f.VerifyQuickInfoExists(t);
                f.VerifyNumberOfErrorsInCurrentFile(t, 4);
            }
            finally
            {
                done();
            }
        });
    }

}
