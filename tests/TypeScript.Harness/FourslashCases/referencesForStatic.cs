// ported from: internal/fourslash/tests/referencesForStatic_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class ReferencesForStaticCase
{
    public static void TestReferencesForStatic(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @Filename: referencesOnStatic_1.ts\nvar n = 43;\n\nclass foo {\n    /*1*/static /*2*/n = '';\n\n    public bar() {\n        foo./*3*/n = \"'\";\n        if(foo./*4*/n) {\n            var x = foo./*5*/n;\n        }\n    }\n}\n\nclass foo2 {\n    private x = foo./*6*/n;\n    constructor() {\n        foo./*7*/n = x;\n    }\n\n    function b(n) {\n        n = foo./*8*/n;\n    }\n}\n// @Filename: referencesOnStatic_2.ts\nvar q = foo./*9*/n;"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyBaselineFindAllReferences(t, "1", "2", "3", "4", "5", "6", "7", "8", "9");
            }
            finally
            {
                done();
            }
        });
    }

}
