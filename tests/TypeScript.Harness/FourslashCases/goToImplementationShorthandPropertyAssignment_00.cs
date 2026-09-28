// ported from: internal/fourslash/tests/goToImplementationShorthandPropertyAssignment_00_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class GoToImplementationShorthandPropertyAssignment_00Case
{
    public static void TestGoToImplementationShorthandPropertyAssignment_00(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("interface Foo {\n    someFunction(): void;\n}\n\ninterface FooConstructor {\n    new (): Foo\n}\n\ninterface Bar {\n    Foo: FooConstructor;\n}\n\nvar x = class /*classExpression*/Foo {\n    createBarInClassExpression(): Bar {\n        return {\n            Fo/*classExpressionRef*/o\n        };\n    }\n\n    someFunction() {}\n}\n\nclass /*declaredClass*/Foo {\n\n}\n\nfunction createBarUsingClassDeclaration(): Bar {\n    return {\n        Fo/*declaredClassRef*/o\n    };\n}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyBaselineGoToImplementation(t, "classExpressionRef", "declaredClassRef");
            }
            finally
            {
                done();
            }
        });
    }

}
