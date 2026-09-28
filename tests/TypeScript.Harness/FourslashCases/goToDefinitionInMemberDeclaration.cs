// ported from: internal/fourslash/tests/goToDefinitionInMemberDeclaration_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class GoToDefinitionInMemberDeclarationCase
{
    public static void TestGoToDefinitionInMemberDeclaration(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("interface /*interfaceDefinition*/IFoo { method1(): number; }\n\nclass /*classDefinition*/Foo implements IFoo {\n    public method1(): number { return 0; }\n}\n\nenum /*enumDefinition*/Enum { value1, value2 };\n\nclass /*selfDefinition*/Bar {\n    public _interface: [|IFo/*interfaceReference*/o|] = new [|Fo/*classReferenceInInitializer*/o|]();\n    public _class: [|Fo/*classReference*/o|] = new Foo();\n    public _list: [|IF/*interfaceReferenceInList*/oo|][]=[];\n    public _enum: [|E/*enumReference*/num|] = [|En/*enumReferenceInInitializer*/um|].value1;\n    public _self: [|Ba/*selfReference*/r|];\n\n    constructor(public _inConstructor: [|IFo/*interfaceReferenceInConstructor*/o|]) {\n    }\n}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyBaselineGoToDefinition(t, true, "interfaceReference", "interfaceReferenceInList", "interfaceReferenceInConstructor", "classReference", "classReferenceInInitializer", "enumReference", "enumReferenceInInitializer", "selfReference");
            }
            finally
            {
                done();
            }
        });
    }

}
