// ported from: internal/fourslash/tests/codeFixConvertToTypeOnlyImport3_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class CodeFixConvertToTypeOnlyImport3Case
{
    public static void TestCodeFixConvertToTypeOnlyImport3(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @module: esnext\n// @verbatimModuleSyntax: true\n// @Filename: exports1.ts\nexport default class A {}\nexport class B {}\nexport class C {}\n// @Filename: exports2.ts\nexport default class D {}\nexport class E {}\nexport class F {}\n// @Filename: imports.ts\nimport A, { B, C } from './exports1';\nimport D, * as others from \"./exports2\";\n\ndeclare const a: A;\ndeclare const b: B;\ndeclare const c: C;\ndeclare const d: D;\ndeclare const o: typeof others;\nconsole.log(a, b, c, d, o);"u8);
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
