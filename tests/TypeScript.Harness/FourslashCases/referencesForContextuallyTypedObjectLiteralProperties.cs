// ported from: internal/fourslash/tests/referencesForContextuallyTypedObjectLiteralProperties_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class ReferencesForContextuallyTypedObjectLiteralPropertiesCase
{
    public static void TestReferencesForContextuallyTypedObjectLiteralProperties(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("interface IFoo { /*xy*/xy: number; }\n\n// Assignment\nvar a1: IFoo = { xy: 0 };\nvar a2: IFoo = { xy: 0 };\n\n// Function call\nfunction consumer(f: IFoo) { }\nconsumer({ xy: 1 });\n\n// Type cast\nvar c = <IFoo>{ xy: 0 };\n\n// Array literal\nvar ar: IFoo[] = [{ xy: 1 }, { xy: 2 }];\n\n// Nested object literal\nvar ob: { ifoo: IFoo } = { ifoo: { xy: 0 } };\n\n// Widened type\nvar w: IFoo = { xy: undefined };\n\n// Untped -- should not be included\nvar u = { xy: 0 };"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyBaselineFindAllReferences(t, "xy");
            }
            finally
            {
                done();
            }
        });
    }

}
