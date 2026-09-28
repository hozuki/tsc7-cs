// ported from: internal/fourslash/tests/typeOperatorNodeBuilding_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class TypeOperatorNodeBuildingCase
{
    public static void TestTypeOperatorNodeBuilding(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @Filename: keyof.ts\nfunction doSomethingWithKeys<T>(...keys: (keyof T)[]) { }\n\nconst /*1*/utilityFunctions = {\n  doSomethingWithKeys\n};\n// @Filename: typeof.ts\nclass Foo { static a: number; }\nfunction doSomethingWithTypes(...statics: (typeof Foo)[]) {}\n\nconst /*2*/utilityFunctions = {\n  doSomethingWithTypes\n};"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyQuickInfoAt(t, "1", "const utilityFunctions: {\n    doSomethingWithKeys: <T>(...keys: (keyof T)[]) => void;\n}", "");
                f.VerifyQuickInfoAt(t, "2", "const utilityFunctions: {\n    doSomethingWithTypes: (...statics: (typeof Foo)[]) => void;\n}", "");
            }
            finally
            {
                done();
            }
        });
    }

}
