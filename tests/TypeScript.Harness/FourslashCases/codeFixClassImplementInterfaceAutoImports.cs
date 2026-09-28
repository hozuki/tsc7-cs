// ported from: internal/fourslash/tests/codeFixClassImplementInterfaceAutoImports_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class CodeFixClassImplementInterfaceAutoImportsCase
{
    public static void TestCodeFixClassImplementInterfaceAutoImports(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @Filename: types1.ts\ntype A = {};\nexport default A;\n// @Filename: types2.ts\nexport type B = {};\nexport type C = {};\nexport type D<T> = {};\n// @Filename: interface.ts\nimport A from './types1';\nimport { B, C, D } from './types2';\n\nexport interface Base {\n  a: Readonly<A> & { kind: \"a\"; };\n  b<T extends B = B>(p1: C): D<C>;\n}\n// @Filename: index.ts\nimport { Base } from './interface';\n\nexport class C implements Base {[| |]}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.GoToFile(t, "index.ts");
                f.VerifyCodeFix(t, new F.VerifyCodeFixOptions { Description = "Implement interface 'Base'", NewFileContent = "import { Base } from './interface';\nimport A from './types1';\nimport { B, C, D } from './types2';\n\nexport class C implements Base {\n    a: Readonly<A> & { kind: 'a'; };\n    b<T extends B = B>(p1: C): D<C> {\n        throw new Error('Method not implemented.');\n    }\n}", Index = 0 });
            }
            finally
            {
                done();
            }
        });
    }

}
