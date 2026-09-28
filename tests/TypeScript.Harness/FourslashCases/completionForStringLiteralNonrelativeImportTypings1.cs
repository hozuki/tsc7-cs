// ported from: internal/fourslash/tests/completionForStringLiteralNonrelativeImportTypings1_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class CompletionForStringLiteralNonrelativeImportTypings1Case
{
    public static void TestCompletionForStringLiteralNonrelativeImportTypings1(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @typeRoots: my_typings,my_other_typings\n// @Filename: tests/test0.ts\n/// <reference types=\"m/*types_ref0*/\" />\nimport * as foo1 from \"m/*import_as0*/\nimport foo2 = require(\"m/*import_equals0*/\nvar foo3 = require(\"m/*require0*/\n// @Filename: my_typings/module-x/index.d.ts\nexport var x = 9;\n// @Filename: my_typings/module-x/whatever.d.ts\nexport var w = 9;\n// @Filename: my_typings/module-y/index.d.ts\nexport var y = 9;\n// @Filename: my_other_typings/module-z/index.d.ts\nexport var z = 9;"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyCompletions(t, f.Markers(), new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = [], EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Exact = ["module-x", "module-y", "module-z"] } });
            }
            finally
            {
                done();
            }
        });
    }

}
