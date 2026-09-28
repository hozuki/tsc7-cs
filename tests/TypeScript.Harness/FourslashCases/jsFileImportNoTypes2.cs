// ported from: internal/fourslash/tests/jsFileImportNoTypes2_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using LSP = TypeScript.LsProto;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class JsFileImportNoTypes2Case
{
    public static void TestJsFileImportNoTypes2(TSU.IHarnessTest t)
    {
        t.Skip("Known failing fourslash test");
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @allowJs: true\n// @Filename: /default.ts\nexport default class TestDefaultClass {}\n// @Filename: /defaultType.ts\nexport default interface TestDefaultInterface {}\n// @Filename: /reExport/toReExport.ts\nexport class TestClassReExport {}\nexport interface TestInterfaceReExport {}\n// @Filename: /reExport/index.ts\nexport { TestClassReExport, TestInterfaceReExport } from './toReExport';\n// @Filename: /exportList.ts\nclass TestClassExportList {};\ninterface TestInterfaceExportList {};\nexport { TestClassExportList, TestInterfaceExportList };\n// @Filename: /baseline.ts\nexport class TestClassBaseline {}\nexport interface TestInterfaceBaseline {}\n// @Filename: /a.js\nimport /**/"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyCompletions(t, "", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = [], EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Exact = [new LSP.CompletionItem { Label = "TestClassBaseline", InsertText = "import { TestClassBaseline } from \"./baseline\";", Data = new LSP.CompletionItemData { AutoImport = new LSP.AutoImportFix { ModuleSpecifier = "./baseline" } } }, new LSP.CompletionItem { Label = "TestClassExportList", InsertText = "import { TestClassExportList } from \"./exportList\";", Data = new LSP.CompletionItemData { AutoImport = new LSP.AutoImportFix { ModuleSpecifier = "./exportList" } } }, new LSP.CompletionItem { Label = "TestClassReExport", InsertText = "import { TestClassReExport } from \"./reExport\";", Data = new LSP.CompletionItemData { AutoImport = new LSP.AutoImportFix { ModuleSpecifier = "./reExport" } } }, new LSP.CompletionItem { Label = "TestDefaultClass", InsertText = "import TestDefaultClass from \"./default\";", Data = new LSP.CompletionItemData { AutoImport = new LSP.AutoImportFix { ModuleSpecifier = "./default" } } }] } });
            }
            finally
            {
                done();
            }
        });
    }

}
