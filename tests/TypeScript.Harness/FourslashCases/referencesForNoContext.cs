// ported from: internal/fourslash/tests/referencesForNoContext_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class ReferencesForNoContextCase
{
    public static void TestReferencesForNoContext(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("namespace modTest {\n    //Declare\n    export var modVar:number;\n    /*1*/\n\n    //Increments\n    modVar++;\n\n    class testCls{\n        /*2*/\n    }\n\n    function testFn(){\n        //Increments\n        modVar++;\n    }  /*3*/\n/*4*/\n    namespace testMod {\n    }\n}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyBaselineFindAllReferences(t, "1", "2", "3", "4");
            }
            finally
            {
                done();
            }
        });
    }

}
