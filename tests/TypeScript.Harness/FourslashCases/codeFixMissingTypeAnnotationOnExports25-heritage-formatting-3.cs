// ported from: internal/fourslash/tests/codeFixMissingTypeAnnotationOnExports25-heritage-formatting-3_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class CodeFixMissingTypeAnnotationOnExports25_heritage_formatting_3Case
{
    public static void TestCodeFixMissingTypeAnnotationOnExports25_heritage_formatting_3(TSU.IHarnessTest t)
    {
        t.Skip("Known failing fourslash test");
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @isolatedDeclarations: true\n// @declaration: true\nfunction mixin<T extends new (...a: any) => any>(ctor: T): T {\n    return ctor;\n}\nclass Point2D { x = 0; y = 0; }\nexport class Point3D3 extends mixin(Point2D) /* DD*/ {\n    z = 0;\n}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyCodeFixAvailable(t, ["Extract base class to variable"]);
                f.VerifyCodeFix(t, new F.VerifyCodeFixOptions { Description = "Extract base class to variable", NewFileContent = "function mixin<T extends new (...a: any) => any>(ctor: T): T {\n    return ctor;\n}\nclass Point2D { x = 0; y = 0; }\nconst Point3D3Base: typeof Point2D = mixin(Point2D) /* DD*/;\nexport class Point3D3 extends Point3D3Base {\n    z = 0;\n}", Index = 0 });
            }
            finally
            {
                done();
            }
        });
    }

}
