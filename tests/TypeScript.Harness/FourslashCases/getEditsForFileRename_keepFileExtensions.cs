// ported from: internal/fourslash/tests/getEditsForFileRename_keepFileExtensions_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class GetEditsForFileRename_keepFileExtensionsCase
{
    public static void TestGetEditsForFileRename_keepFileExtensions(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @Filename: /tsconfig.json\n{\n  \"compilerOptions\": {\n    \"module\": \"Node16\",\n    \"rootDirs\": [\"src\"]\n  }\n}\n// @Filename: /src/person.ts\nexport const name = 0;\n// @Filename: /src/index.ts\nimport {name} from \"./person.js\";"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyWillRenameFilesEdits(t, "/src/person.ts", "/src/vip.ts", new Dictionary<string, string> { ["/src/index.ts"] = "import {name} from \"./vip.js\";" }, null!);
            }
            finally
            {
                done();
            }
        });
    }

}
