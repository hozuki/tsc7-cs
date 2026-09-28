// ported from: internal/fourslash/tests/codeFixClassImplementClassAbstractGettersAndSetters_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class CodeFixClassImplementClassAbstractGettersAndSettersCase
{
    public static void TestCodeFixClassImplementClassAbstractGettersAndSetters(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("abstract class A {\n    abstract get a(): string;\n    abstract set a(newName: string);\n\n    abstract get b(): number;\n\n    abstract set c(arg: number | string);\n\n    abstract accessor d: string;\n}\n\nclass C implements A {}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyCodeFix(t, new F.VerifyCodeFixOptions { Description = "Implement interface 'A'", NewFileContent = "abstract class A {\n    abstract get a(): string;\n    abstract set a(newName: string);\n\n    abstract get b(): number;\n\n    abstract set c(arg: number | string);\n\n    abstract accessor d: string;\n}\n\nclass C implements A {\n    get a(): string {\n        throw new Error(\"Method not implemented.\");\n    }\n    set a(newName: string) {\n        throw new Error(\"Method not implemented.\");\n    }\n    get b(): number {\n        throw new Error(\"Method not implemented.\");\n    }\n    set c(arg: string | number) {\n        throw new Error(\"Method not implemented.\");\n    }\n    accessor d: string;\n}", Index = 0 });
            }
            finally
            {
                done();
            }
        });
    }

}
