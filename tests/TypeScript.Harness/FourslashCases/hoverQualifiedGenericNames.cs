// ported from: internal/fourslash/tests/hoverQualifiedGenericNames_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class HoverQualifiedGenericNamesCase
{
    public static void TestHoverQualifiedGenericNames(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("\nfunction f<T>(x: T) {\n    class C {\n        value = x\n    }\n    return new C()\n}\n\nclass A<T> {\n    foo() {}\n}\nclass B extends A<string> {}\n\nlet t1/*1*/ = f(\"hello\")\nconst t2/*2*/ = new B()\nt2./*3*/foo()\n"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyQuickInfoAt(t, "1", "let t1: f<string>.C", "");
                f.VerifyQuickInfoAt(t, "2", "const t2: B", "");
                f.VerifyQuickInfoAt(t, "3", "(method) A<string>.foo(): void", "");
            }
            finally
            {
                done();
            }
        });
    }

}
