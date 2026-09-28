// ported from: internal/fourslash/tests/goToImplementationShorthandPropertyAssignment_01_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class GoToImplementationShorthandPropertyAssignment_01Case
{
    public static void TestGoToImplementationShorthandPropertyAssignment_01(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("interface Foo {\n    someFunction(): void;\n}\n\ninterface FooConstructor {\n    new (): Foo\n}\n\ninterface Bar {\n    Foo: FooConstructor;\n}\n\n// Class expression that gets used in a bar implementation\nvar x = class [|Foo|] {\n    createBarInClassExpression(): Bar {\n        return {\n            Foo\n        };\n    }\n\n    someFunction() {}\n};\n\n// Class declaration that gets used in a bar implementation. This class has multiple definitions\n// (the class declaration and the interface above), but we only want the class returned\nclass [|Foo|] {\n\n}\n\nfunction createBarUsingClassDeclaration(): Bar {\n    return {\n        Foo\n    };\n}\n\n// Class expression that does not get used in a bar implementation\nvar y = class Foo {\n    someFunction() {}\n};\n\ncreateBarUsingClassDeclaration().Fo/*reference*/o;"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyBaselineGoToImplementation(t, "reference");
            }
            finally
            {
                done();
            }
        });
    }

}
