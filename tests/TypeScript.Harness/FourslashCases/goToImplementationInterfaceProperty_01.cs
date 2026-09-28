// ported from: internal/fourslash/tests/goToImplementationInterfaceProperty_01_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class GoToImplementationInterfaceProperty_01Case
{
    public static void TestGoToImplementationInterfaceProperty_01(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("interface Foo { hello: number }\n\nclass Bar implements Foo {\n    [|hello|] = 5 * 9;\n}\n\nfunction whatever(foo: Foo) {\n    foo.he/*reference*/llo;\n}"u8);
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
