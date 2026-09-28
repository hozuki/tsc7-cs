// ported from: internal/fourslash/tests/referencesForGlobalsInExternalModule_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class ReferencesForGlobalsInExternalModuleCase
{
    public static void TestReferencesForGlobalsInExternalModule(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("/*1*/var /*2*/topLevelVar = 2;\nvar topLevelVar2 = /*3*/topLevelVar;\n\n/*4*/class /*5*/topLevelClass { }\nvar c = new /*6*/topLevelClass();\n\n/*7*/interface /*8*/topLevelInterface { }\nvar i: /*9*/topLevelInterface;\n\n/*10*/module /*11*/topLevelModule {\n    export var x;\n}\nvar x = /*12*/topLevelModule.x;\n\nexport = x;"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyBaselineFindAllReferences(t, "1", "2", "3", "4", "5", "6", "7", "8", "9", "10", "11", "12");
            }
            finally
            {
                done();
            }
        });
    }

}
