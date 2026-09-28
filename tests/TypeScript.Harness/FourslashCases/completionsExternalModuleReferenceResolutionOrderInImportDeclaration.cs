// ported from: internal/fourslash/tests/completionsExternalModuleReferenceResolutionOrderInImportDeclaration_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;
using U = TypeScript.Harness.FourslashCases.FourslashTestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class CompletionsExternalModuleReferenceResolutionOrderInImportDeclarationCase
{
    public static void TestCompletionsExternalModuleReferenceResolutionOrderInImportDeclaration(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @Filename: externalModuleRefernceResolutionOrderInImportDeclaration_file1.ts\nexport function foo() { };\n// @Filename: externalModuleRefernceResolutionOrderInImportDeclaration_file2.ts\ndeclare module \"externalModuleRefernceResolutionOrderInImportDeclaration_file1\" {\n    export function bar();\n}\n// @Filename: externalModuleRefernceResolutionOrderInImportDeclaration_file3.ts\n///<reference path='externalModuleRefernceResolutionOrderInImportDeclaration_file2.ts'/>\nimport file1 = require('externalModuleRefernceResolutionOrderInImportDeclaration_file1');\n/*1*/"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.GoToMarker(t, "1");
                f.Insert(t, "file1.");
                f.VerifyCompletions(t, null!, new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Includes = ["bar"], Excludes = ["foo"] } });
            }
            finally
            {
                done();
            }
        });
    }

}
