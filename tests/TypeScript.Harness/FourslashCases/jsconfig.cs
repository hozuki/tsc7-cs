// ported from: internal/fourslash/tests/jsconfig_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class JsconfigCase
{
    public static void TestJsconfig(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @Filename: /a.js\nfunction f(/**/x) {\n}\n// @Filename: /jsconfig.json\n{\n    \"compilerOptions\": {\n        \"checkJs\": true,\n        \"noImplicitAny\": true\n    }\n}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.GoToFile(t, "/a.js");
                f.VerifyErrorExistsAfterMarker(t, "");
            }
            finally
            {
                done();
            }
        });
    }

}
