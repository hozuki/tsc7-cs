// ported from: internal/fourslash/tests/getEditsForFileRename_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class GetEditsForFileRenameCase
{
    public static void TestGetEditsForFileRename(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @Filename: /a.ts\n/// <reference path=\"./src/old.ts\" />\nimport old from \"./src/old\";\n// @Filename: /src/a.ts\n/// <reference path=\"./old.ts\" />\nimport old from \"./old\";\n// @Filename: /src/foo/a.ts\n/// <reference path=\"../old.ts\" />\nimport old from \"../old\";\n// @Filename: /unrelated.ts\nimport { x } from \"././src/./foo/./a\";\n// @Filename: /src/old.ts\nexport default 0;\n// @Filename: /tsconfig.json\n{ \"files\": [\"a.ts\", \"src/a.ts\", \"src/foo/a.ts\", \"src/old.ts\"] }"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyWillRenameFilesEdits(t, "/src/old.ts", "/src/new.ts", new Dictionary<string, string> { ["/a.ts"] = "/// <reference path=\"./src/new.ts\" />\nimport old from \"./src/new\";", ["/src/a.ts"] = "/// <reference path=\"./new.ts\" />\nimport old from \"./new\";", ["/src/foo/a.ts"] = "/// <reference path=\"../new.ts\" />\nimport old from \"../new\";", ["/tsconfig.json"] = "{ \"files\": [\"a.ts\", \"src/a.ts\", \"src/foo/a.ts\", \"src/new.ts\"] }" }, null!);
            }
            finally
            {
                done();
            }
        });
    }

}
