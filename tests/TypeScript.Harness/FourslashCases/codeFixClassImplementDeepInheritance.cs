// ported from: internal/fourslash/tests/codeFixClassImplementDeepInheritance_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class CodeFixClassImplementDeepInheritanceCase
{
    public static void TestCodeFixClassImplementDeepInheritance(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @stableTypeOrdering: true\n// @strict: false\n// Referenced throughout the inheritance chain.\ninterface I0 { a: number }\n\nclass C1 implements I0 { a: number }\ninterface I1 { b: number }\ninterface I2 extends C1, I1 {}\n\nclass C2 { c: number }\ninterface I3 {d: number}\nclass C3 extends C2 implements I0, I2, I3 {\n    a: number;\n    b: number;\n    d: number;\n}\n\ninterface I4 { e: number }\ninterface I5 { f: number }\nclass C4 extends C3 implements I0, I4, I5 {\n    e: number;\n    f: number;\n}\n\ninterface I6 extends C4 {}\nclass C5 implements I6 {}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyCodeFix(t, new F.VerifyCodeFixOptions { Description = "Implement interface 'I6'", NewFileContent = "// Referenced throughout the inheritance chain.\ninterface I0 { a: number }\n\nclass C1 implements I0 { a: number }\ninterface I1 { b: number }\ninterface I2 extends C1, I1 {}\n\nclass C2 { c: number }\ninterface I3 {d: number}\nclass C3 extends C2 implements I0, I2, I3 {\n    a: number;\n    b: number;\n    d: number;\n}\n\ninterface I4 { e: number }\ninterface I5 { f: number }\nclass C4 extends C3 implements I0, I4, I5 {\n    e: number;\n    f: number;\n}\n\ninterface I6 extends C4 {}\nclass C5 implements I6 {\n    c: number;\n    a: number;\n    b: number;\n    d: number;\n    e: number;\n    f: number;\n}", Index = 0 });
            }
            finally
            {
                done();
            }
        });
    }

}
