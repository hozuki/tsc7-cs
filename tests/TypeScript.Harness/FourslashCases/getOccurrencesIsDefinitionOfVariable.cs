// ported from: internal/fourslash/tests/getOccurrencesIsDefinitionOfVariable_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class GetOccurrencesIsDefinitionOfVariableCase
{
    public static void TestGetOccurrencesIsDefinitionOfVariable(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("/*1*/var /*2*/x = 0;\nvar assignmentRightHandSide = /*3*/x;\nvar assignmentRightHandSide2 = 1 + /*4*/x;\n\n/*5*/x = 1;\n/*6*/x = /*7*/x + /*8*/x;\n\n/*9*/x == 1;\n/*10*/x <= 1;\n\nvar preIncrement = ++/*11*/x;\nvar postIncrement = /*12*/x++;\nvar preDecrement = --/*13*/x;\nvar postDecrement = /*14*/x--;\n\n/*15*/x += 1;\n/*16*/x <<= 1;"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyBaselineFindAllReferences(t, "1", "2", "3", "4", "5", "6", "7", "8", "9", "10", "11", "12", "13", "14", "15", "16");
            }
            finally
            {
                done();
            }
        });
    }

}
