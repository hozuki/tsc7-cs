// ported from: internal/fourslash/tests/quickInfoGenericTypePath_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class QuickInfoGenericTypePathCase
{
    public static void TestQuickInfoGenericTypePath(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("\nfunction f<T>(x: T) {\n  class C {\n    value = x\n  }\n  return new C()\n}\n\nclass Box<T> {\n  public value: T;\n  constructor(value: T) {\n    this.value = value;\n  }\n}\n\nconst instance = f/*callF*/(\"hello\");\nconst b1/*b1*/ = new Box/*newBox*/(instance);\ndeclare const b2/*b2*/: Box<typeof instance>;\n"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyBaselineHover(t);
            }
            finally
            {
                done();
            }
        });
    }

}
