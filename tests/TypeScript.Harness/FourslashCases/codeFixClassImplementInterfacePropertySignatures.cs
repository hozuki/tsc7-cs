// ported from: internal/fourslash/tests/codeFixClassImplementInterfacePropertySignatures_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class CodeFixClassImplementInterfacePropertySignaturesCase
{
    public static void TestCodeFixClassImplementInterfacePropertySignatures(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("interface I {\n    a0: {};\n    a1: { (b1: number, c1: string): number; };\n    a2: (b2: number, c2: string) => number;\n    a3: { (b3: number, c3: string): number, x: number };\n\n    a4: { new (b1: number, c1: string): number; };\n    a5: new (b2: number, c2: string) => number;\n    a6: { new (b3: number, c3: string): number, x: number };\n\n    a7: { foo(b7: number, c7: string): number };\n\n    a8: { (b81: number, c81: string): number, new (b82: number, c82: string): number; };\n\n    a9: { (b9: number, c9: string): number; [d9: number]: I };\n    a10: { (b10: number, c10: string): number; [d10: string]: I };\n}\nclass C implements I {}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyCodeFix(t, new F.VerifyCodeFixOptions { Description = "Implement interface 'I'", NewFileContent = "interface I {\n    a0: {};\n    a1: { (b1: number, c1: string): number; };\n    a2: (b2: number, c2: string) => number;\n    a3: { (b3: number, c3: string): number, x: number };\n\n    a4: { new (b1: number, c1: string): number; };\n    a5: new (b2: number, c2: string) => number;\n    a6: { new (b3: number, c3: string): number, x: number };\n\n    a7: { foo(b7: number, c7: string): number };\n\n    a8: { (b81: number, c81: string): number, new (b82: number, c82: string): number; };\n\n    a9: { (b9: number, c9: string): number; [d9: number]: I };\n    a10: { (b10: number, c10: string): number; [d10: string]: I };\n}\nclass C implements I {\n    a0: {};\n    a1: (b1: number, c1: string) => number;\n    a2: (b2: number, c2: string) => number;\n    a3: { (b3: number, c3: string): number; x: number; };\n    a4: new (b1: number, c1: string) => number;\n    a5: new (b2: number, c2: string) => number;\n    a6: { new(b3: number, c3: string): number; x: number; };\n    a7: { foo(b7: number, c7: string): number; };\n    a8: { (b81: number, c81: string): number; new(b82: number, c82: string): number; };\n    a9: { (b9: number, c9: string): number;[d9: number]: I; };\n    a10: { (b10: number, c10: string): number;[d10: string]: I; };\n}", Index = 0 });
            }
            finally
            {
                done();
            }
        });
    }

}
