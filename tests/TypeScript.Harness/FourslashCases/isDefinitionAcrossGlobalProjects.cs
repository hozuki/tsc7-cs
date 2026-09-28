// ported from: internal/fourslash/tests/isDefinitionAcrossGlobalProjects_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class IsDefinitionAcrossGlobalProjectsCase
{
    public static void TestIsDefinitionAcrossGlobalProjects(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @Filename: /home/src/workspaces/project/a/index.ts\n/// <reference path=\"../b/index.ts\" />\n/// <reference path=\"../c/index.ts\" />\nnamespace NS {\n    export function /*1*/FA() {\n        FB();\n    }\n}\n\ninterface /*2*/I {\n    /*3*/FA();\n}\n\nconst ia: I = {\n    FA() { },\n    FB() { },\n    FC() { },\n };\n// @Filename: /home/src/workspaces/project/a/tsconfig.json\n{\n    \"extends\": \"../tsconfig.settings.json\",\n    \"references\": [\n        { \"path\": \"../b\" },\n        { \"path\": \"../c\" },\n    ],\n    \"files\": [\n        \"index.ts\",\n    ],\n}\n// @Filename: /home/src/workspaces/project/b/index.ts\nnamespace NS {\n    export function /*4*/FB() {}\n}\n\ninterface /*5*/I {\n    /*6*/FB();\n}\n\nconst ib: I = { FB() {} };\n// @Filename: /home/src/workspaces/project/b/tsconfig.json\n{\n    \"extends\": \"../tsconfig.settings.json\",\n    \"files\": [\n        \"index.ts\",\n    ],\n}\n// @Filename: /home/src/workspaces/project/c/index.ts\nnamespace NS {\n    export function /*7*/FC() {}\n}\n\ninterface /*8*/I {\n    /*9*/FC();\n}\n\nconst ic: I = { FC() {} };\n// @Filename: /home/src/workspaces/project/c/tsconfig.json\n{\n    \"extends\": \"../tsconfig.settings.json\",\n    \"files\": [\n        \"index.ts\",\n    ],\n}\n// @Filename: /home/src/workspaces/project/tsconfig.json\n{\n    \"compilerOptions\": {\n        \"composite\": true,\n        \"lib\": [\"es5\"],\n    },\n    \"references\": [\n        { \"path\": \"a\" },\n    ],\n    \"files\": []\n}\n// @Filename: /home/src/workspaces/project/tsconfig.settings.json\n{\n    \"compilerOptions\": {\n        \"composite\": true,\n        \"skipLibCheck\": true,\n        \"declarationMap\": true,\n        \"emitDeclarationOnly\": true,\n        \"lib\": [\"es5\"],\n    }\n}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.MarkTestAsStradaServer();
                f.VerifyBaselineFindAllReferences(t, "1", "2", "3", "4", "5", "6", "7", "8", "9");
            }
            finally
            {
                done();
            }
        });
    }

}
