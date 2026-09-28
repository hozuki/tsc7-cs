// ported from: internal/fourslash/tests/codeFixAmbientClassImplementClassAbstractGettersAndSetters_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class CodeFixAmbientClassImplementClassAbstractGettersAndSettersCase
{
    public static void TestCodeFixAmbientClassImplementClassAbstractGettersAndSetters(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("abstract class A {\n    abstract get a(): string;\n    abstract set a(newName: string);\n\n    abstract get b(): number;\n\n    abstract set c(arg: number | string);\n}\n\ndeclare class C implements A {}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyCodeFix(t, new F.VerifyCodeFixOptions { Description = "Implement interface 'A'", NewFileContent = "abstract class A {\n    abstract get a(): string;\n    abstract set a(newName: string);\n\n    abstract get b(): number;\n\n    abstract set c(arg: number | string);\n}\n\ndeclare class C implements A {\n    get a(): string;\n    set a(newName: string);\n    get b(): number;\n    set c(arg: string | number);\n}", Index = 0 });
            }
            finally
            {
                done();
            }
        });
    }

}
