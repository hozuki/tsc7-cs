// ported from: internal/fourslash/tests/codeFixClassImplementInterfaceInNamespace_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class CodeFixClassImplementInterfaceInNamespaceCase
{
    public static void TestCodeFixClassImplementInterfaceInNamespace(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("namespace N1 {\n    export interface I1 {\n        f1():string;\n    }\n}\ninterface I1 {\n    f1();\n}\n\nclass C1 implements N1.I1 {}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyCodeFix(t, new F.VerifyCodeFixOptions { Description = "Implement interface 'N1.I1'", NewFileContent = "namespace N1 {\n    export interface I1 {\n        f1():string;\n    }\n}\ninterface I1 {\n    f1();\n}\n\nclass C1 implements N1.I1 {\n    f1(): string {\n        throw new Error(\"Method not implemented.\");\n    }\n}", Index = 0 });
            }
            finally
            {
                done();
            }
        });
    }

}
