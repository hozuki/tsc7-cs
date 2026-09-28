// ported from: internal/fourslash/tests/contextuallyTypedObjectLiteralMethodDeclarationParam01_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class ContextuallyTypedObjectLiteralMethodDeclarationParam01Case
{
    public static void TestContextuallyTypedObjectLiteralMethodDeclarationParam01(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @noImplicitAny: true\ninterface A {\n    numProp: number;\n}\n\ninterface B  {\n    strProp: string;\n}\n\ninterface Foo {\n    method1(arg: A): void;\n    method2(arg: B): void;\n}\n\nfunction getFoo1(): Foo {\n    return {\n        method1(/*param1*/arg) {\n            arg.numProp = 10;\n        },\n        method2(/*param2*/arg) {\n            arg.strProp = \"hello\";\n        }\n    }\n}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyQuickInfoAt(t, "param1", "(parameter) arg: A", "");
                f.VerifyQuickInfoAt(t, "param2", "(parameter) arg: B", "");
            }
            finally
            {
                done();
            }
        });
    }

}
