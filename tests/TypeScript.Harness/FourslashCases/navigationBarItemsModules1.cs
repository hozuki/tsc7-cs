// ported from: internal/fourslash/tests/navigationBarItemsModules1_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class NavigationBarItemsModules1Case
{
    public static void TestNavigationBarItemsModules1(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("declare module \"X.Y.Z\" {}\n\ndeclare module 'X2.Y2.Z2' {}\n\ndeclare module \"foo\";\n\nnamespace A.B.C {\n    export var x;\n}\n\nnamespace A.B {\n    export var y;\n}\n\nnamespace A {\n    export var z;\n}\n\nnamespace A {\n    namespace B {\n        namespace C {\n            declare var x;\n        }\n    }\n}"u8);
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
