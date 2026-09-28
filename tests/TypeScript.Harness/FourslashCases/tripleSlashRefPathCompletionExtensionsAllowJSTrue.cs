// ported from: internal/fourslash/tests/tripleSlashRefPathCompletionExtensionsAllowJSTrue_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class TripleSlashRefPathCompletionExtensionsAllowJSTrueCase
{
    public static void TestTripleSlashRefPathCompletionExtensionsAllowJSTrue(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @allowJs: true\n// @Filename: test0.ts\n/// <reference path=\"/*0*/\n/// <reference path=\".//*1*/\n/// <reference path=\"./f/*2*/\n// @Filename: f1.ts\n\n// @Filename: f1.js\n\n// @Filename: f1.d.ts\n\n// @Filename: f1.tsx\n\n// @Filename: f1.js\n\n// @Filename: f1.jsx\n\n// @Filename: f1.cs\n"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyCompletions(t, f.Markers(), new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = [], EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Exact = ["f1.d.ts", "f1.js", "f1.jsx", "f1.ts", "f1.tsx"] } });
            }
            finally
            {
                done();
            }
        });
    }

}
