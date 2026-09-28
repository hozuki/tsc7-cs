// ported from: internal/fourslash/tests/goToImplementationInterface_07_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class GoToImplementationInterface_07Case
{
    public static void TestGoToImplementationInterface_07(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("interface Fo/*interface_definition*/o {\n    hello (): void;\n}\n\ninterface Bar {\n    hello (): void;\n}\n\nlet x1: Foo            = [|{ hello ()          { /**typeReference*/ } }|];\nlet x2: () => Foo      = [|(() => { hello ()   { /**functionType*/} })|];\nlet x3: Foo | Bar      = [|{ hello ()          { /**unionType*/} }|];\nlet x4: Foo & (Foo & Bar)      = [|{ hello ()          { /**intersectionType*/} }|];\nlet x5: [Foo]          = [|[{ hello ()         { /**tupleType*/} }]|];\nlet x6: (Foo)          = [|{ hello ()          { /**parenthesizedType*/} }|];\nlet x7: (new() => Foo) = [|class { hello ()    { /**constructorType*/} }|];\nlet x8: Foo[]          = [|[{ hello ()         { /**arrayType*/} }]|];\nlet x9: { y: Foo }     = [|{ y: { hello ()     { /**typeLiteral*/} } }|];\nlet x10 = [|{|\"parts\": [\"(\",\"anonymous local class\",\")\"], \"kind\": \"local class\"|}class implements Foo { hello() {} }|]\nlet x11 = class [|{|\"parts\": [\"(\",\"local class\",\")\",\" \",\"C\"], \"kind\": \"local class\"|}C|] implements Foo { hello() {} }\n\n// Should not do anything for type predicates\nfunction isFoo(a: any): a is Foo {\n    return true;\n}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyBaselineGoToImplementation(t, "interface_definition");
            }
            finally
            {
                done();
            }
        });
    }

}
