// ported from: internal/fourslash/tests/codeFixConvertToTypeOnlyImport2_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class CodeFixConvertToTypeOnlyImport2Case
{
    public static void TestCodeFixConvertToTypeOnlyImport2(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @module: esnext\n// @verbatimModuleSyntax: true\n// @Filename: exports.ts\nexport default class A {}\nexport class B {}\nexport class C {}\n// @Filename: imports.ts\nimport A, { B, C } from './exports';\n\ndeclare const a: A;\ndeclare const b: B;\ndeclare const c: C;\nconsole.log(a, b, c);"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.GoToFile(t, "imports.ts");
                f.VerifyCodeFixNotAvailable(t);
            }
            finally
            {
                done();
            }
        });
    }

}
