// ported from: internal/fourslash/tests/getEditsForFileRename_tsconfig_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class GetEditsForFileRename_tsconfigCase
{
    public static void TestGetEditsForFileRename_tsconfig(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @Filename: /src/tsconfig.json\n{\n    \"compilerOptions\": {\n        \"baseUrl\": \"./old\",\n        \"paths\": {\n            \"foo\": [\"old\"],\n        },\n        \"rootDir\": \"old\",\n        \"rootDirs\": [\"old\"],\n        \"typeRoots\": [\"old\"],\n    },\n    \"files\": [\"old/a.ts\"],\n    \"include\": [\"old/*.ts\"],\n    \"exclude\": [\"old\"],\n}\n// @Filename: /src/old/someFile.ts\n"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyWillRenameFilesEdits(t, "/src/old", "/src/new", new Dictionary<string, string> { ["/src/tsconfig.json"] = "{\n    \"compilerOptions\": {\n        \"baseUrl\": \"new\",\n        \"paths\": {\n            \"foo\": [\"new\"],\n        },\n        \"rootDir\": \"new\",\n        \"rootDirs\": [\"new\"],\n        \"typeRoots\": [\"new\"],\n    },\n    \"files\": [\"new/a.ts\"],\n    \"include\": [\"new/*.ts\"],\n    \"exclude\": [\"new\"],\n}" }, null!);
            }
            finally
            {
                done();
            }
        });
    }

}
