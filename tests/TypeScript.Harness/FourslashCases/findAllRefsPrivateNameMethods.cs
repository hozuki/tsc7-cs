// ported from: internal/fourslash/tests/findAllRefsPrivateNameMethods_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class FindAllRefsPrivateNameMethodsCase
{
    public static void TestFindAllRefsPrivateNameMethods(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("class C {\n    /*1*/#foo(){ }\n    constructor() {\n        this./*2*/#foo();\n    }\n}\nclass D extends C {\n    constructor() {\n        super()\n        this.#foo = 20;\n    }\n}\nclass E {\n    /*3*/#foo(){ }\n    constructor() {\n        this./*4*/#foo();\n    }\n}"u8);
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
