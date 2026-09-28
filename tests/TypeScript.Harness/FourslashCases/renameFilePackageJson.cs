// ported from: internal/fourslash/tests/renameFilePackageJson_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class RenameFilePackageJsonCase
{
    public static void TestRenameFilePackageJson(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @Filename: /src/example.ts\nimport brushPackageJson from './visx-brush//*rename*/package.json';\n// @Filename: /src/visx-brush/package.json\n{ \"name\": \"brush\" }"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyRename(t, "rename", "package2.json", new Dictionary<string, string> { ["/src/example.ts"] = "import brushPackageJson from './visx-brush/package2.json';", ["/src/visx-brush/package2.json"] = "{ \"name\": \"brush\" }" });
            }
            finally
            {
                done();
            }
        });
    }

}
