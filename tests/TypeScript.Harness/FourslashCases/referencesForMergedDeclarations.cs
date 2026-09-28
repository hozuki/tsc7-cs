// ported from: internal/fourslash/tests/referencesForMergedDeclarations_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class ReferencesForMergedDeclarationsCase
{
    public static void TestReferencesForMergedDeclarations(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("/*1*/interface /*2*/Foo {\n}\n\n/*3*/module /*4*/Foo {\n    export interface Bar { }\n}\n\n/*5*/function /*6*/Foo(): void {\n}\n\nvar f1: /*7*/Foo.Bar;\nvar f2: /*8*/Foo;\n/*9*/Foo.bind(this);"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyBaselineFindAllReferences(t, "1", "2", "3", "4", "5", "6", "7", "8", "9");
            }
            finally
            {
                done();
            }
        });
    }

}
