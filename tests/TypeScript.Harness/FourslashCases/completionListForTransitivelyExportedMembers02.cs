// ported from: internal/fourslash/tests/completionListForTransitivelyExportedMembers02_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;
using U = TypeScript.Harness.FourslashCases.FourslashTestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class CompletionListForTransitivelyExportedMembers02Case
{
    public static void TestCompletionListForTransitivelyExportedMembers02(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @Filename: A.ts\nexport interface I1 { one: number }\nexport interface I2 { two: string }\nexport type I1_OR_I2 = I1 | I2;\n\nexport class C1 {\n    one: string;\n}\n\nexport namespace Inner {\n    export interface I3 {\n        three: boolean\n    }\n\n    export var varVar = 100;\n    export let letVar = 200;\n    export const constVar = 300;\n}\n// @Filename: B.ts\nexport var bVar = \"bee!\";\n// @Filename: C.ts\nexport var cVar = \"see!\";\nexport * from \"./A\";\nexport * from \"./B\"\n// @Filename: D.ts\nimport * as c from \"./C\";\nvar x = c.Inner./**/"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyCompletions(t, "", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Exact = ["constVar", "letVar", "varVar"] } });
            }
            finally
            {
                done();
            }
        });
    }

}
