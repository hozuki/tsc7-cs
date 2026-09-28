// ported from: internal/fourslash/tests/goToImplementationInterface_00_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class GoToImplementationInterface_00Case
{
    public static void TestGoToImplementationInterface_00(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("interface Fo/*interface_definition*/o {\n    hello: () => void\n}\n\ninterface Baz extends Foo {}\n\nvar bar: Foo = [|{|\"parts\": [\"(\",\"object literal\",\")\"], \"kind\": \"interface\"|}{ hello: helloImpl /**0*/ }|];\nvar baz: Foo[] = [|[{ hello: helloImpl /**4*/ }]|];\n\nfunction helloImpl () {}\n\nfunction whatever(x: Foo = [|{|\"parts\": [\"(\",\"object literal\",\")\"], \"kind\": \"interface\"|}{ hello() {/**1*/} }|] ) {\n}\n\nclass Bar {\n    x: Foo = [|{ hello() {/*2*/} }|]\n\n    constructor(public f: Foo = [|{ hello() {/**3*/} }|] ) {}\n}"u8);
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
