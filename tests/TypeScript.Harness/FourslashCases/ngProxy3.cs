// ported from: internal/fourslash/tests/ngProxy3_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class NgProxy3Case
{
    public static void TestNgProxy3(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @Filename: tsconfig.json\n{\n    \"compilerOptions\": {\n        \"lib\": [\"es5\"],\n        \"plugins\": [\n            { \"name\": \"create-thrower\" }\n        ]\n    },\n    \"files\": [\"a.ts\"]\n}\n// @Filename: a.ts\nlet x = [1, 2];\nx/**/\n"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.MarkTestAsStradaServer();
                f.GoToMarker(t, "");
                f.VerifyQuickInfoIs(t, "let x: number[]", "");
            }
            finally
            {
                done();
            }
        });
    }

}
