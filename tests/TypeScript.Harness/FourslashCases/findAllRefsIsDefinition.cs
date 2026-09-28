// ported from: internal/fourslash/tests/findAllRefsIsDefinition_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class FindAllRefsIsDefinitionCase
{
    public static void TestFindAllRefsIsDefinition(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("declare function foo(a: number): number;\ndeclare function foo(a: string): string;\ndeclare function foo/*1*/(a: string | number): string | number;\n\nfunction foon(a: number): number;\nfunction foon(a: string): string;\nfunction foon/*2*/(a: string | number): string | number {\n    return a\n}\n\nfoo; foon;\n\nexport const bar/*3*/ = 123;\nconsole.log({ bar });\n\ninterface IFoo {\n    foo/*4*/(): void;\n}\nclass Foo implements IFoo {\n    constructor(n: number)\n    constructor()\n    /*5*/constructor(n: number?) { }\n    foo/*6*/(): void { }\n    static init() { return new this() }\n}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyBaselineFindAllReferences(t, "1", "2", "3", "4", "5", "6");
            }
            finally
            {
                done();
            }
        });
    }

}
