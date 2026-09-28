// ported from: internal/fourslash/tests/codeFixClassImplementInterfaceWithAmbientSignatures1_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class CodeFixClassImplementInterfaceWithAmbientSignatures1Case
{
    public static void TestCodeFixClassImplementInterfaceWithAmbientSignatures1(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @lib: esnext\n// @target: esnext\n// @Filename: /node_modules/@types/node/globals.d.ts\nexport {};\ndeclare global {\n    interface SymbolConstructor {\n        readonly dispose: unique symbol;\n    }\n    interface Disposable {\n        [Symbol.dispose](): void;\n    }\n}\n// @Filename: /node_modules/@types/node/index.d.ts\n/// <reference path=\"globals.d.ts\" />\n// @Filename: a.ts\nclass Foo implements Disposable {}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.GoToFile(t, "a.ts");
                f.VerifyCodeFix(t, new F.VerifyCodeFixOptions { Description = "Implement interface 'Disposable'", NewFileContent = "class Foo implements Disposable {\n    [Symbol.dispose](): void {\n        throw new Error(\"Method not implemented.\");\n    }\n}", Index = 0 });
            }
            finally
            {
                done();
            }
        });
    }

}
