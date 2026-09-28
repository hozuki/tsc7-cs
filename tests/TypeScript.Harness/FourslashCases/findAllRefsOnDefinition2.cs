// ported from: internal/fourslash/tests/findAllRefsOnDefinition2_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class FindAllRefsOnDefinition2Case
{
    public static void TestFindAllRefsOnDefinition2(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("//@Filename: findAllRefsOnDefinition2-import.ts\nexport module Test{\n\n    /*1*/export interface /*2*/start { }\n\n    export interface stop { }\n}\n//@Filename: findAllRefsOnDefinition2.ts\nimport Second = require(\"./findAllRefsOnDefinition2-import\");\n\nvar start: Second.Test./*3*/start;\nvar stop: Second.Test.stop;"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyBaselineFindAllReferences(t, "1", "2", "3");
            }
            finally
            {
                done();
            }
        });
    }

}
