// ported from: internal/fourslash/tests/incompatibleOverride_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class IncompatibleOverrideCase
{
    public static void TestIncompatibleOverride(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @strict: false\nclass Foo { xyz: string; }\nclass Bar extends Foo { /*1*/xyz/*2*/: number = 1; }\nclass Baz extends Foo { public /*3*/xyz/*4*/: number = 2; }\nclass /*5*/Baf/*6*/ extends Foo {\n   constructor(public xyz: number) {\n      super();\n   }\n}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyErrorExistsBetweenMarkers(t, "1", "2");
                f.VerifyErrorExistsBetweenMarkers(t, "3", "4");
                f.VerifyErrorExistsBetweenMarkers(t, "5", "6");
                f.VerifyNumberOfErrorsInCurrentFile(t, 3);
            }
            finally
            {
                done();
            }
        });
    }

}
