// ported from: internal/fourslash/tests/completionForStringLiteralRelativeImportAllowJSTrue_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class CompletionForStringLiteralRelativeImportAllowJSTrueCase
{
    public static void TestCompletionForStringLiteralRelativeImportAllowJSTrue(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @allowJs: true\n// @Filename: test0.ts\nimport * as foo1 from \".//*import_as0*/\nimport * as foo2 from \"./f/*import_as1*/\nimport foo3 = require(\".//*import_equals0*/\nimport foo4 = require(\"./f/*import_equals1*/\nvar foo5 = require(\".//*require0*/\nvar foo6 = require(\"./f/*require1*/\n// @Filename: f1.ts\n\n// @Filename: f2.js\n\n// @Filename: f3.d.ts\n\n// @Filename: f4.tsx\n\n// @Filename: f5.js\n\n// @Filename: f6.jsx\n\n// @Filename: g1.ts\n\n// @Filename: g2.js\n"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyCompletions(t, f.Markers(), new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = [], EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Exact = ["f1", "f2", "f3", "f4", "f5", "f6", "g1", "g2"] } });
            }
            finally
            {
                done();
            }
        });
    }

}
