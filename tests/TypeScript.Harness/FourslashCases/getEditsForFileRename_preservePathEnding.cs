// ported from: internal/fourslash/tests/getEditsForFileRename_preservePathEnding_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class GetEditsForFileRename_preservePathEndingCase
{
    public static void TestGetEditsForFileRename_preservePathEnding(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @allowJs: true\n// @checkJs: true\n// @strict: true\n// @jsx: preserve\n// @resolveJsonModule: true\n// @Filename: /index.js\nexport const x = 0;\n// @Filename: /jsx.jsx\nexport const y = 0;\n// @Filename: /j.jonah.json\n{ \"j\": 0 }\n// @Filename: /a.js\nimport { x as x0 } from \".\";\nimport { x as x1 } from \"./index\";\nimport { x as x2 } from \"./index.js\";\nimport { y } from \"./jsx.jsx\";\nimport { j } from \"./j.jonah.json\";"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyNoErrors(t);
                f.VerifyWillRenameFilesEdits(t, "/a.js", "/b.js", new Dictionary<string, string> {  }, null!);
                f.VerifyWillRenameFilesEdits(t, "/b.js", "/src/b.js", new Dictionary<string, string> { ["/src/b.js"] = "import { x as x0 } from \"..\";\nimport { x as x1 } from \"../index\";\nimport { x as x2 } from \"../index.js\";\nimport { y } from \"../jsx.jsx\";\nimport { j } from \"../j.jonah.json\";" }, null!);
            }
            finally
            {
                done();
            }
        });
    }

}
