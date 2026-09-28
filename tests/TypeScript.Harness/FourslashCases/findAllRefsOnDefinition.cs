// ported from: internal/fourslash/tests/findAllRefsOnDefinition_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class FindAllRefsOnDefinitionCase
{
    public static void TestFindAllRefsOnDefinition(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("//@Filename: findAllRefsOnDefinition-import.ts\nexport class Test{\n\n    constructor(){\n\n    }\n\n    /*1*/public /*2*/start(){\n        return this;\n    }\n\n    public stop(){\n        return this;\n    }\n}\n//@Filename: findAllRefsOnDefinition.ts\nimport Second = require(\"./findAllRefsOnDefinition-import\");\n\nvar second = new Second.Test()\nsecond./*3*/start();\nsecond.stop();"u8);
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
