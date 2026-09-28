// ported from: internal/fourslash/tests/getEditsForFileRename_jsRename_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class GetEditsForFileRename_jsRenameCase
{
    public static void TestGetEditsForFileRename_jsRename(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("\n// @Filename: /tsconfig.json\n{ \"compilerOptions\": { \"module\": \"nodenext\" } }\n// @Filename: /a.ts\nexport const a = 1;\n// @Filename: /b.ts\nimport { a } from \".//*rename*/a.js\";"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyRename(t, "rename", "c.js", new Dictionary<string, string> { ["/c.ts"] = "export const a = 1;", ["/b.ts"] = "import { a } from \"./c.js\";" });
            }
            finally
            {
                done();
            }
        });
    }

}
