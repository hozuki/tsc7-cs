// ported from: internal/fourslash/tests/codeFixClassImplementInterfaceAutoImports_typeOnly_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class CodeFixClassImplementInterfaceAutoImports_typeOnlyCase
{
    public static void TestCodeFixClassImplementInterfaceAutoImports_typeOnly(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @module: esnext\n// @verbatimModuleSyntax: true\n// @Filename: types1.ts\ntype A = {};\nexport default A;\n// @Filename: types2.ts\nexport type B = {};\nexport type C = {};\nexport type D<T> = {};\n// @Filename: interface.ts\nimport type A from './types1';\nimport type { B, C, D } from './types2';\n\nexport interface Base {\n  a: A;\n  b<T extends B = B>(p1: C): D<C>;\n}\n// @Filename: index.ts\nimport type { Base } from './interface';\n\nexport class C implements Base {[| |]}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.GoToFile(t, "index.ts");
                f.VerifyCodeFix(t, new F.VerifyCodeFixOptions { Description = "Implement interface 'Base'", NewFileContent = "import type { Base } from './interface';\nimport type A from './types1';\nimport type { B, C, D } from './types2';\n\nexport class C implements Base {\n    a: A;\n    b<T extends B = B>(p1: C): D<C> {\n        throw new Error('Method not implemented.');\n    }\n}", Index = 0 });
            }
            finally
            {
                done();
            }
        });
    }

}
