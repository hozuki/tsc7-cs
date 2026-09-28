// ported from: internal/fourslash/tests/formatTypeAnnotation1_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TRI = TypeScript.Core.Tristate;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class FormatTypeAnnotation1Case
{
    public static void TestFormatTypeAnnotation1(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("function foo(x: number, y?: string): number {}\ninterface Foo {\n    x: number;\n    y?: number;\n}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                var opts207 = f.GetOptions();
                opts207!.FormatCodeSettings!.InsertSpaceBeforeTypeAnnotation = TRI.TSTrue;
                f.Configure(t, opts207);
                f.FormatDocument(t, "");
                f.VerifyCurrentFileContent(t, "function foo(x : number, y ?: string) : number { }\ninterface Foo {\n    x : number;\n    y ?: number;\n}");
            }
            finally
            {
                done();
            }
        });
    }

}
