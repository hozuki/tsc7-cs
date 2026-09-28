// ported from: internal/fourslash/tests/statecallhierarchy_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class StatecallhierarchyCase
{
    public static void TestCallHierarchyAcrossProject(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("\n// @stateBaseline: true\n// @Filename: /projects/temp/temp.ts\n/*temp*/let x = 10\n// @Filename: /projects/temp/tsconfig.json\n{}\n// @Filename: /projects/container/lib/tsconfig.json\n{\n\t\"compilerOptions\": {\n\t\t\"composite\": true,\n\t},\n\treferences: [],\n\tfiles: [\n\t\t\"index.ts\",\n\t\t\"bar.ts\",\n\t\t\"baz.ts\"\n\t],\n}\n// @Filename: /projects/container/lib/index.ts\nexport function /*call*/createModelReference() {}\n// @Filename: /projects/container/lib/bar.ts\nimport { createModelReference } from \"./index\";\nfunction openElementsAtEditor() {\n  createModelReference();\n}\n// @Filename: /projects/container/lib/baz.ts\nimport { createModelReference } from \"./index\";\nfunction registerDefaultLanguageCommand() {\n  createModelReference();\n}\n// @Filename: /projects/container/exec/tsconfig.json\n{\n\t\"files\": [\"./index.ts\"],\n\t\"references\": [\n\t\t{ \"path\": \"../lib\" },\n\t],\n}\n// @Filename: /projects/container/exec/index.ts\nimport { createModelReference } from \"../lib\";\nfunction openElementsAtEditor1() {\n  createModelReference();\n}\n// @Filename: /projects/container/compositeExec/tsconfig.json\n{\n\t\"compilerOptions\": {\n\t\t\"composite\": true,\n\t},\n\t\"files\": [\"./index.ts\"],\n\t\"references\": [\n\t\t{ \"path\": \"../lib\" },\n\t],\n}\n// @Filename: /projects/container/compositeExec/index.ts\nimport { createModelReference } from \"../lib\";\nfunction openElementsAtEditor2() {\n  createModelReference();\n}\n// @Filename: /projects/container/tsconfig.json\n{\n\t\"files\": [],\n\t\"include\": [],\n\t\"references\": [\n\t\t{ \"path\": \"./exec\" },\n\t\t{ \"path\": \"./compositeExec\" },\n\t],\n}\n// @Filename: /projects/container/tsconfig.json\n{\n\t\"files\": [],\n\t\"include\": [],\n\t\"references\": [\n\t\t{ \"path\": \"./exec\" },\n\t\t{ \"path\": \"./compositeExec\" },\n\t],\n}\n// @Filename: /projects/container/tsconfig.json\n{\n\t\"files\": [],\n\t\"include\": [],\n\t\"references\": [\n\t\t{ \"path\": \"./exec\" },\n\t\t{ \"path\": \"./compositeExec\" },\n\t],\n}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.GoToMarker(t, "call");
                f.GoToMarker(t, "temp");
                f.GoToMarker(t, "call");
                f.VerifyBaselineCallHierarchy(t);
                f.CloseFileOfMarker(t, "temp");
                f.GoToMarker(t, "temp");
                f.CloseFileOfMarker(t, "call");
                f.CloseFileOfMarker(t, "temp");
                f.GoToMarker(t, "temp");
            }
            finally
            {
                done();
            }
        });
    }

}
