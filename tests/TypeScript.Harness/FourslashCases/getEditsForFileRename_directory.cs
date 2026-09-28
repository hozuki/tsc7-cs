// ported from: internal/fourslash/tests/getEditsForFileRename_directory_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class GetEditsForFileRename_directoryCase
{
    public static void TestGetEditsForFileRename_directory(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @Filename: /a.ts\n/// <reference path=\"./src/old/file.ts\" />\nimport old from \"./src/old\";\nimport old2 from \"./src/old/file\";\nexport default 0;\n// @Filename: /src/b.ts\n/// <reference path=\"./old/file.ts\" />\nimport old from \"./old\";\nimport old2 from \"./old/file\";\nexport default 0;\n// @Filename: /src/foo/c.ts\n/// <reference path=\"../old/file.ts\" />\nimport old from \"../old\";\nimport old2 from \"../old/file\";\nexport default 0;\n// @Filename: /src/old/index.ts\nimport a from \"../../a\";\nimport a2 from \"../b\";\nimport a3 from \"../foo/c\";\nimport f from \"./file\";\nexport default 0;\n// @Filename: /src/old/file.ts\nexport default 0;\n// @Filename: /tsconfig.json\n{ \"files\": [\"a.ts\", \"src/b.ts\", \"src/foo/c.ts\", \"src/old/index.ts\", \"src/old/file.ts\"] }"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyWillRenameFilesEdits(t, "/src/old", "/src/new", new Dictionary<string, string> { ["/a.ts"] = "/// <reference path=\"./src/new/file.ts\" />\nimport old from \"./src/new\";\nimport old2 from \"./src/new/file\";\nexport default 0;", ["/src/b.ts"] = "/// <reference path=\"./new/file.ts\" />\nimport old from \"./new\";\nimport old2 from \"./new/file\";\nexport default 0;", ["/src/foo/c.ts"] = "/// <reference path=\"../new/file.ts\" />\nimport old from \"../new\";\nimport old2 from \"../new/file\";\nexport default 0;", ["/tsconfig.json"] = "{ \"files\": [\"a.ts\", \"src/b.ts\", \"src/foo/c.ts\", \"src/new/index.ts\", \"src/new/file.ts\"] }" }, null!);
            }
            finally
            {
                done();
            }
        });
    }

}
