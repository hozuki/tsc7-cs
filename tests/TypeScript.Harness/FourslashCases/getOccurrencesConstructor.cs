// ported from: internal/fourslash/tests/getOccurrencesConstructor_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;
using U = TypeScript.Harness.FourslashCases.FourslashTestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class GetOccurrencesConstructorCase
{
    public static void TestGetOccurrencesConstructor(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("class C {\n    [|const/**/ructor|]();\n    [|constructor|](x: number);\n    [|constructor|](y: string, x: number);\n    [|constructor|](a?: any, ...r: any[]) {\n        if (a === undefined && r.length === 0) {\n            return;\n        }\n\n        return;\n    }\n}\n\nclass D {\n    constructor(public x: number, public y: number) {\n    }\n}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyBaselineDocumentHighlights(t, null!, U.ToAny(f.Ranges()));
            }
            finally
            {
                done();
            }
        });
    }

}
