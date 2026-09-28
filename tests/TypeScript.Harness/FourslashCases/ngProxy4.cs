// ported from: internal/fourslash/tests/ngProxy4_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class NgProxy4Case
{
    public static void TestNgProxy4(TSU.IHarnessTest t)
    {
        t.Skip("Known failing fourslash test");
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @Filename: tsconfig.json\n{\n    \"compilerOptions\": {\n        \"lib\": [\"es5\"],\n        \"plugins\": [\n            { \"name\": \"diagnostic-adder\" }\n        ]\n    },\n    \"files\": [\"a.ts\"]\n}\n// @Filename: a.ts\nlet x = [1, 2];\nx/**/\n"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.MarkTestAsStradaServer();
                f.GoToMarker(t, "");
                f.VerifyNumberOfErrorsInCurrentFile(t, 1);
            }
            finally
            {
                done();
            }
        });
    }

}
