// ported from: internal/fourslash/tests/navigationBarItemsInsideMethodsAndConstructors_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class NavigationBarItemsInsideMethodsAndConstructorsCase
{
    public static void TestNavigationBarItemsInsideMethodsAndConstructors(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("class Class {\n    constructor() {\n        function LocalFunctionInConstructor() {}\n        interface LocalInterfaceInConstrcutor {}\n        enum LocalEnumInConstructor { LocalEnumMemberInConstructor }\n    }\n\n    method() {\n        function LocalFunctionInMethod() {\n            function LocalFunctionInLocalFunctionInMethod() {}\n        }\n        interface LocalInterfaceInMethod {}\n        enum LocalEnumInMethod { LocalEnumMemberInMethod }\n    }\n\n    emptyMethod() { } // Non child functions method should not be duplicated\n}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyBaselineDocumentSymbol(t);
            }
            finally
            {
                done();
            }
        });
    }

}
