// ported from: internal/fourslash/tests/hoverOptionalMembers_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class HoverOptionalMembersCase
{
    public static void TestHoverOptionalMembers(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("\ntype Foo1 = {\n    x?: string;\n    f?: (x: number) => void;\n    g?: { (x: number): void; (x: string): void; }\n    h?: ((x: number) => void) & ((x: string) => void);\n    m?(x: number): void;\n    m?(x: string): void;\n}\n\ninterface Foo2 {\n    x?: string;\n    f?: (x: number) => void;\n    g?: { (x: number): void; (x: string): void; }\n    h?: ((x: number) => void) & ((x: string) => void);\n    m?(x: number): void;\n    m?(x: string): void;\n}\n\nclass Foo3 {\n    x?: string;\n    f?: (x: number) => void;\n    g?: { (x: number): void; (x: string): void; }\n    h?: ((x: number) => void) & ((x: string) => void);\n    m?(x: number): void;\n    m?(x: string): void;\n}\n\ndeclare const foo1: Foo1;\ndeclare const foo2: Foo2;\ndeclare const foo3: Foo3;\n\nfoo1./*1*/x\nfoo1./*1a*/f\nfoo1./*1b*/f?.(42)\nfoo1./*1c*/g\nfoo1./*1d*/g?.(42)\nfoo1./*1e*/g?.(\"abc\")\nfoo1./*1f*/h\nfoo1./*1g*/h?.(42)\nfoo1./*1h*/h?.(\"abc\")\nfoo1./*1i*/m\nfoo1./*1j*/m?.(42)\nfoo1./*1k*/m?.(\"abc\")\n\nfoo2./*2*/x\nfoo2./*2a*/f\nfoo2./*2b*/f?.(42)\nfoo2./*2c*/g\nfoo2./*2d*/g?.(42)\nfoo2./*2e*/g?.(\"abc\")\nfoo2./*2f*/h\nfoo2./*2g*/h?.(42)\nfoo2./*2h*/h?.(\"abc\")\nfoo2./*2i*/m\nfoo2./*2j*/m?.(42)\nfoo2./*2k*/m?.(\"abc\")\n\nfoo3./*3*/x\nfoo3./*3a*/f\nfoo3./*3b*/f?.(42)\nfoo3./*3c*/g\nfoo3./*3d*/g?.(42)\nfoo3./*3e*/g?.(\"abc\")\nfoo3./*3f*/h\nfoo3./*3g*/h?.(42)\nfoo3./*3h*/h?.(\"abc\")\nfoo3./*3i*/m\nfoo3./*3j*/m?.(42)\nfoo3./*3k*/m?.(\"abc\")\n"u8);
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
