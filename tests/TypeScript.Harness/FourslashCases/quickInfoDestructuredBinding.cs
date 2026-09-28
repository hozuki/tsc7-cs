// ported from: internal/fourslash/tests/quickInfoDestructuredBinding_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class QuickInfoDestructuredBindingCase
{
    public static void TestQuickInfoDestructuredBinding(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("\nfunction f({ /*1*/x }: { x: number }) {}\nfunction g([/*2*/y]: number[]) {}\nfunction h({ a: { /*3*/b } }: { a: { b: string } }) {}\nconst { /*4*/c } = { c: 42 };\nlet { /*5*/d } = { d: \"hello\" };\nvar { /*6*/e } = { e: true };\n"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyQuickInfoAt(t, "1", "(parameter) x: number", "");
                f.VerifyQuickInfoAt(t, "2", "(parameter) y: number", "");
                f.VerifyQuickInfoAt(t, "3", "(parameter) b: string", "");
                f.VerifyQuickInfoAt(t, "4", "const c: number", "");
                f.VerifyQuickInfoAt(t, "5", "let d: string", "");
                f.VerifyQuickInfoAt(t, "6", "var e: boolean", "");
            }
            finally
            {
                done();
            }
        });
    }

}
