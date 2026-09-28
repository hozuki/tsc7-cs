// ported from: internal/fourslash/tests/quickInfoOfStringPropertyNames1_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class QuickInfoOfStringPropertyNames1Case
{
    public static void TestQuickInfoOfStringPropertyNames1(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("interface foo {\n    \"foo bar\": string;\n}\nvar f: foo;\nvar /*1*/r = f['foo bar'];\nclass bar {\n    'hello world': number;\n    '1': string;\n    constructor() {\n        bar['hello world'] = 3;\n    }\n}\nvar b: bar;\nvar /*2*/r2 = b[\"hello world\"];\nvar /*3*/r4 = b['1'];\nvar /*4*/r5 = b[1];"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyQuickInfoAt(t, "1", "var r: string", "");
                f.VerifyQuickInfoAt(t, "2", "var r2: number", "");
                f.VerifyQuickInfoAt(t, "3", "var r4: string", "");
                f.VerifyQuickInfoAt(t, "4", "var r5: string", "");
            }
            finally
            {
                done();
            }
        });
    }

}
