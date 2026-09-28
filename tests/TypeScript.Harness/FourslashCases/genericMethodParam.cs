// ported from: internal/fourslash/tests/genericMethodParam_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class GenericMethodParamCase
{
    public static void TestGenericMethodParam(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("class C<T> {\n    /*1*/\n}\n/*2*/"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyNoErrors(t);
                f.GoToMarker(t, "1");
                f.InsertLine(t, "constructor(){}");
                f.InsertLine(t, "foo(a: T) {");
                f.InsertLine(t, "    return a;");
                f.InsertLine(t, "}");
                f.VerifyNoErrors(t);
                f.GoToMarker(t, "2");
                f.InsertLine(t, "var x = new C<number>();");
                f.InsertLine(t, "var y: number = x.foo(5);");
                f.VerifyNoErrors(t);
            }
            finally
            {
                done();
            }
        });
    }

}
