// ported from: internal/fourslash/tests/unusedImports8FS_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class UnusedImports8FSCase
{
    public static void TestUnusedImports8FS(TSU.IHarnessTest t)
    {
        t.Skip("Known failing fourslash test");
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @noUnusedLocals: true\n// @Filename: file2.ts\n[|import {Calculator as calc, test as t1, test2 as t2} from \"./file1\"|]\n\nvar x = new calc();\nx.handleChar();\nt1();\n// @Filename: file1.ts\nexport class Calculator {\n    handleChar() { }\n}\nexport function test() {\n\n}\nexport function test2() {\n\n}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyRangeAfterCodeFix(t, "import {Calculator as calc, test as t1} from \"./file1\"", false, 0, 0);
            }
            finally
            {
                done();
            }
        });
    }

}
