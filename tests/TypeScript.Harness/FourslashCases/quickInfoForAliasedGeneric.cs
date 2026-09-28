// ported from: internal/fourslash/tests/quickInfoForAliasedGeneric_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class QuickInfoForAliasedGenericCase
{
    public static void TestQuickInfoForAliasedGeneric(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("namespace M {\n    export namespace N {\n        export class C<T> { }\n        export class D { }\n    }\n}\nimport d = M.N;\nvar /*1*/aa: d.C<number>;\nvar /*2*/bb: d.D;"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyQuickInfoAt(t, "1", "var aa: d.C<number>", "");
                f.VerifyQuickInfoAt(t, "2", "var bb: d.D", "");
            }
            finally
            {
                done();
            }
        });
    }

}
