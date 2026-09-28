// ported from: internal/fourslash/tests/getEditsForFileRename_renameFromIndex_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class GetEditsForFileRename_renameFromIndexCase
{
    public static void TestGetEditsForFileRename_renameFromIndex(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @Filename: /a.ts\n/// <reference path=\"./src/index.ts\" />\nimport old from \"./src\";\nimport old2 from \"./src/index\";\n// @Filename: /src/a.ts\n/// <reference path=\"./index.ts\" />\nimport old from \".\";\nimport old2 from \"./index\";\n// @Filename: /src/foo/a.ts\n/// <reference path=\"../index.ts\" />\nimport old from \"..\";\nimport old2 from \"../index\";\n// @Filename: /src/index.ts\n\n// @Filename: /tsconfig.json\n{ \"files\": [\"a.ts\", \"src/a.ts\", \"src/foo/a.ts\", \"src/index.ts\"] }"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyWillRenameFilesEdits(t, "/src/index.ts", "/src/new.ts", new Dictionary<string, string> { ["/a.ts"] = "/// <reference path=\"./src/new.ts\" />\nimport old from \"./src/new\";\nimport old2 from \"./src/new\";", ["/src/a.ts"] = "/// <reference path=\"./new.ts\" />\nimport old from \"./new\";\nimport old2 from \"./new\";", ["/src/foo/a.ts"] = "/// <reference path=\"../new.ts\" />\nimport old from \"../new\";\nimport old2 from \"../new\";", ["/tsconfig.json"] = "{ \"files\": [\"a.ts\", \"src/a.ts\", \"src/foo/a.ts\", \"src/new.ts\"] }" }, null!);
            }
            finally
            {
                done();
            }
        });
    }

}
