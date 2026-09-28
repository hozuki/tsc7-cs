// ported from: internal/fourslash/tests/findAllRefsOfConstructor2_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class FindAllRefsOfConstructor2Case
{
    public static void TestFindAllRefsOfConstructor2(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("class A {\n    /*a*/constructor(s: string) {}\n}\nclass B extends A {\n    /*b*/constructor() { super(\"\"); }\n}\nclass C extends B {\n    /*c*/constructor() {\n        super();\n    }\n}\nclass D extends B { }\nconst a = new A(\"a\");\nconst b = new B();\nconst c = new C();\nconst d = new D();"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyNoErrors(t);
                f.VerifyBaselineFindAllReferences(t, "a", "b", "c");
            }
            finally
            {
                done();
            }
        });
    }

}
