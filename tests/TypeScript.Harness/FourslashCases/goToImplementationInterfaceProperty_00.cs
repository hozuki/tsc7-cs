// ported from: internal/fourslash/tests/goToImplementationInterfaceProperty_00_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class GoToImplementationInterfaceProperty_00Case
{
    public static void TestGoToImplementationInterfaceProperty_00(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("interface Foo {\n    hello: number\n}\n\nvar bar: Foo = { [|hello|]: 5 };\n\n\nfunction whatever(x: Foo = { [|hello|]: 5 * 9 }) {\n    x.he/*reference*/llo\n}\n\nclass Bar {\n    x: Foo = { [|hello|]: 6 }\n\n    constructor(public f: Foo = { [|hello|]: 7 } ) {}\n}"u8);
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
