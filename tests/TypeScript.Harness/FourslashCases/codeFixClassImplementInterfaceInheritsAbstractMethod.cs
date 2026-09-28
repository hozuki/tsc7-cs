// ported from: internal/fourslash/tests/codeFixClassImplementInterfaceInheritsAbstractMethod_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class CodeFixClassImplementInterfaceInheritsAbstractMethodCase
{
    public static void TestCodeFixClassImplementInterfaceInheritsAbstractMethod(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("abstract class C1 { }\nabstract class C2 {\n    abstract f\uFF21<T extends number>(): T;\n}\ninterface I1 extends C1, C2 { }\nclass C3 implements I1 {[| |]}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyCodeFix(t, new F.VerifyCodeFixOptions { Description = "Implement interface 'I1'", NewFileContent = "abstract class C1 { }\nabstract class C2 {\n    abstract f\uff21<T extends number>(): T;\n}\ninterface I1 extends C1, C2 { }\nclass C3 implements I1 {\n    f\uff21<T extends number>(): T {\n        throw new Error(\"Method not implemented.\");\n    }\n}", Index = 0 });
            }
            finally
            {
                done();
            }
        });
    }

}
