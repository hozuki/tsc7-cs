// ported from: internal/fourslash/tests/codeFixMissingTypeAnnotationOnExports23-heritage-formatting_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class CodeFixMissingTypeAnnotationOnExports23_heritage_formattingCase
{
    public static void TestCodeFixMissingTypeAnnotationOnExports23_heritage_formatting(TSU.IHarnessTest t)
    {
        t.Skip("Known failing fourslash test");
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @isolatedDeclarations: true\n// @declaration: true\nfunction mixin<T extends new (...a: any) => any>(ctor: T): T {\n    return ctor;\n}\nclass Point2D { x = 0; y = 0; }\ninterface I{}\nexport class Point3D extends\n    /** Base class */\n    mixin(Point2D)\n    // Test\n    implements I\n    {\n              z = 0;\n}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyCodeFixAvailable(t, ["Extract base class to variable"]);
                f.VerifyCodeFix(t, new F.VerifyCodeFixOptions { Description = "Extract base class to variable", NewFileContent = "function mixin<T extends new (...a: any) => any>(ctor: T): T {\n    return ctor;\n}\nclass Point2D { x = 0; y = 0; }\ninterface I{}\nconst Point3DBase: typeof Point2D =\n    /** Base class */\n    mixin(Point2D);\nexport class Point3D extends Point3DBase\n    // Test\n    implements I\n    {\n              z = 0;\n}", Index = 0 });
            }
            finally
            {
                done();
            }
        });
    }

}
