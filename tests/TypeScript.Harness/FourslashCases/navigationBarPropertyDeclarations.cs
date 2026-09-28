// ported from: internal/fourslash/tests/navigationBarPropertyDeclarations_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class NavigationBarPropertyDeclarationsCase
{
    public static void TestNavigationBarPropertyDeclarations(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("class A {\n    public A1 = class {\n        public x = 1;\n        private y() {}\n        protected z() {}\n    }\n\n    public A2 = {\n        x: 1,\n        y() {},\n        z() {}\n    }\n\n    public A3 = function () {}\n    public A4 = () => {}\n    public A5 = 1;\n    public A6 = \"A6\";\n\n    public [\"A7\"] = class {\n        public x = 1;\n        private y() {}\n        protected z() {}\n    }\n\n    public [1] = {\n        x: 1,\n        y() {},\n        z() {}\n    }\n\n    public [1 + 1] = 1;\n}"u8);
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
