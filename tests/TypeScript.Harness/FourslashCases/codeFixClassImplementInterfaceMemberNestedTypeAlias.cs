// ported from: internal/fourslash/tests/codeFixClassImplementInterfaceMemberNestedTypeAlias_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class CodeFixClassImplementInterfaceMemberNestedTypeAliasCase
{
    public static void TestCodeFixClassImplementInterfaceMemberNestedTypeAlias(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("type Either<T> = { val: T } | Error;\ninterface I {\n    x: Either<Either<string>>;\n    foo(x: Either<Either<string>>): void;\n}\nclass C implements I {}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyCodeFix(t, new F.VerifyCodeFixOptions { Description = "Implement interface 'I'", NewFileContent = "type Either<T> = { val: T } | Error;\ninterface I {\n    x: Either<Either<string>>;\n    foo(x: Either<Either<string>>): void;\n}\nclass C implements I {\n    x: Either<Either<string>>;\n    foo(x: Either<Either<string>>): void {\n        throw new Error(\"Method not implemented.\");\n    }\n}", Index = 0 });
            }
            finally
            {
                done();
            }
        });
    }

}
