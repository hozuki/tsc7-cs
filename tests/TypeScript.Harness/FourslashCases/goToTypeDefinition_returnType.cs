// ported from: internal/fourslash/tests/goToTypeDefinition_returnType_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class GoToTypeDefinition_returnTypeCase
{
    public static void TestGoToTypeDefinition_returnType(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("interface /*I*/I { x: number; }\ninterface /*J*/J { y: number; }\n\nfunction f0(): I { return { x: 0 }; }\n\ntype T = /*T*/(i: I) => I;\nconst f1: T = i => ({ x: i.x + 1 });\n\nconst f2 = (i: I): I => ({ x: i.x + 1 });\n\nconst f3 = (i: I) => (/*f3Def*/{ x: i.x + 1 });\n\nconst f4 = (i: I) => i;\n\nconst f5 = /*f5Def*/(i: I): I | J => ({ x: i.x + 1 });\n\nconst f6 = (i: I, j: J, b: boolean) => b ? i : j;\n\nconst /*f7Def*/f7 = (i: I) => {};\n\nfunction f8(i: I): I;\nfunction f8(j: J): J;\nfunction /*f8Def*/f8(ij: any): any { return ij; }\n\n/*f0*/f0();\n/*f1*/f1();\n/*f2*/f2();\n/*f3*/f3();\n/*f4*/f4();\n/*f5*/f5();\n/*f6*/f6();\n/*f7*/f7();\n/*f8*/f8();"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyBaselineGoToTypeDefinition(t, "f0", "f1", "f2", "f3", "f4", "f5", "f6", "f7", "f8");
            }
            finally
            {
                done();
            }
        });
    }

}
