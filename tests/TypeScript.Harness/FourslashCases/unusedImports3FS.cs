// ported from: internal/fourslash/tests/unusedImports3FS_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class UnusedImports3FSCase
{
    public static void TestUnusedImports3FS(TSU.IHarnessTest t)
    {
        t.Skip("Known failing fourslash test");
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @noUnusedLocals: true\n// @Filename: file2.ts\n[| import {Calculator, /*some comments*/ test, test2} from \"./file1\" |]\n test();\n test2();\n// @Filename: file1.ts\n export class Calculator {\n     handleChar() {}\n }\n export function test() {\n\n }\n export function test2() {\n\n }"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyRangeAfterCodeFix(t, "import {/*some comments*/ test, test2} from \"./file1\"", false, 0, 0);
            }
            finally
            {
                done();
            }
        });
    }

}
