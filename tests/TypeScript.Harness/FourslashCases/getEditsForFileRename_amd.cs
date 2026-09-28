// ported from: internal/fourslash/tests/getEditsForFileRename_amd_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class GetEditsForFileRename_amdCase
{
    public static void TestGetEditsForFileRename_amd(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @moduleResolution: classic\n// @Filename: /src/user.ts\nimport { x } from \"old\";\n// @Filename: /src/old.ts\nexport const x = 0;"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyWillRenameFilesEdits(t, "/src/old.ts", "/src/new.ts", new Dictionary<string, string> { ["/src/user.ts"] = "import { x } from \"./new\";" }, null!);
            }
            finally
            {
                done();
            }
        });
    }

}
