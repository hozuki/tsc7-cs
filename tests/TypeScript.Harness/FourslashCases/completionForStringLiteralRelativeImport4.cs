// ported from: internal/fourslash/tests/completionForStringLiteralRelativeImport4_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class CompletionForStringLiteralRelativeImport4Case
{
    public static void TestCompletionForStringLiteralRelativeImport4(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @rootDirs: /sub/src1,/src2\n// @Filename: /src2/test0.ts\n import * as foo1 from \"./mo/*import_as0*/\n import foo2 = require(\"./mo/*import_equals0*/\n var foo3 = require(\"./mo/*require0*/\n// @Filename: /src2/inner/inner0.ts\nimport * as s from \".//*inner*/\";\n// @Filename: /src2/inner/inner1.ts\nexport const x = 0;\n// @Filename: /src2/module0.ts\n export var w = 0;\n// @Filename: /sub/src1/module1.ts\n export var x = 0;\n// @Filename: /sub/src1/module2.ts\n export var y = 0;\n// @Filename: /sub/src1/more/module3.ts\n export var z = 0;\n// @Filename: f1.ts\n\n// @Filename: f2.tsx\n\n// @Filename: folder/f1.ts\n\n// @Filename: f3.js\n\n// @Filename: f4.jsx\n\n// @Filename: e1.ts\n\n// @Filename: e2.js\n"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyCompletions(t, new F.MarkerInput { MarkerNames = ["import_as0", "import_equals0", "require0"] }, new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = [], EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Unsorted = ["module1", "module2", "more", "module0", "inner"] } });
                f.VerifyCompletions(t, "inner", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = [], EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Unsorted = ["inner1"] } });
            }
            finally
            {
                done();
            }
        });
    }

}
