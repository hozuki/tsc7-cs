// ported from: internal/fourslash/tests/referencesForContextuallyTypedUnionProperties2_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class ReferencesForContextuallyTypedUnionProperties2Case
{
    public static void TestReferencesForContextuallyTypedUnionProperties2(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("interface A {\n    a: number;\n    common: string;\n}\n\ninterface B {\n    /*1*/b: number;\n    common: number;\n}\n\n// Assignment\nvar v1: A | B = { a: 0, common: \"\" };\nvar v2: A | B = { b: 0, common: 3 };\n\n// Function call\nfunction consumer(f:  A | B) { }\nconsumer({ a: 0, b: 0, common: 1 });\n\n// Type cast\nvar c = <A | B> { common: 0, b: 0 };\n\n// Array literal\nvar ar: Array<A|B> = [{ a: 0, common: \"\" }, { b: 0, common: 0 }];\n\n// Nested object literal\nvar ob: { aorb: A|B } = { aorb: { b: 0, common: 0 } };\n\n// Widened type\nvar w: A|B = { b:undefined, common: undefined };\n\n// Untped -- should not be included\nvar u1 = { a: 0, b: 0, common: \"\" };\nvar u2 = { b: 0, common: 0 };"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyBaselineFindAllReferences(t, "1");
            }
            finally
            {
                done();
            }
        });
    }

}
