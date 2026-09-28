// ported from: internal/fourslash/tests/referencesForOverrides_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class ReferencesForOverridesCase
{
    public static void TestReferencesForOverrides(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("namespace FindRef3 {\n\tnamespace SimpleClassTest {\n\t\texport class Foo {\n\t\t\tpublic /*foo*/foo(): void {\n\t\t\t}\n\t\t}\n\t\texport class Bar extends Foo {\n\t\t\tpublic foo(): void {\n\t\t\t}\n\t\t}\n\t}\n\n\tnamespace SimpleInterfaceTest {\n\t\texport interface IFoo {\n\t\t\t/*ifoo*/ifoo(): void;\n\t\t}\n\t\texport interface IBar extends IFoo {\n\t\t\tifoo(): void;\n\t\t}\n\t}\n\n\tnamespace SimpleClassInterfaceTest {\n\t\texport interface IFoo {\n\t\t\t/*icfoo*/icfoo(): void;\n\t\t}\n\t\texport class Bar implements IFoo {\n\t\t\tpublic icfoo(): void {\n\t\t\t}\n\t\t}\n\t}\n\n\tnamespace Test {\n\t\texport interface IBase {\n\t\t\t/*field*/field: string;\n\t\t\t/*method*/method(): void;\n\t\t}\n\n\t\texport interface IBlah extends IBase {\n\t\t\tfield: string;\n\t\t}\n\n\t\texport interface IBlah2 extends IBlah {\n\t\t\tfield: string;\n\t\t}\n\n\t\texport interface IDerived extends IBlah2 {\n\t\t\tmethod(): void;\n\t\t}\n\n\t\texport class Bar implements IDerived {\n\t\t\tpublic field: string;\n\t\t\tpublic method(): void { }\n\t\t}\n\n\t\texport class BarBlah extends Bar {\n\t\t\tpublic field: string;\n\t\t}\n\t}\n\n\tfunction test() {\n\t\tvar x = new SimpleClassTest.Bar();\n\t\tx.foo();\n\n\t\tvar y: SimpleInterfaceTest.IBar = null;\n\t\ty.ifoo();\n\n        var w: SimpleClassInterfaceTest.Bar = null;\n        w.icfoo();\n\n\t\tvar z = new Test.BarBlah();\n\t\tz.field = \"\";\n        z.method();\n\t}\n}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyBaselineFindAllReferences(t, "foo", "ifoo", "icfoo", "field", "method");
            }
            finally
            {
                done();
            }
        });
    }

}
