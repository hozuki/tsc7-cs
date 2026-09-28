// ported from: internal/fourslash/tests/isDefinitionAcrossModuleProjects_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class IsDefinitionAcrossModuleProjectsCase
{
    public static void TestIsDefinitionAcrossModuleProjects(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @Filename: /home/src/workspaces/project/a/index.ts\nimport { NS } from \"../b\";\nimport { I } from \"../c\";\n\ndeclare module \"../b\" {\n    export namespace NS {\n        export function /*1*/FA();\n    }\n}\n\ndeclare module \"../c\" {\n    export interface /*2*/I {\n        /*3*/FA();\n    }\n}\n\nconst ia: I = {\n    FA: NS.FA,\n    FC() { },\n};\n// @Filename: /home/src/workspaces/project/a/tsconfig.json\n{\n    \"extends\": \"../tsconfig.settings.json\",\n    \"references\": [\n        { \"path\": \"../b\" },\n        { \"path\": \"../c\" },\n    ],\n    \"files\": [\n        \"index.ts\",\n    ],\n}\n// @Filename: /home/src/workspaces/project/a2/index.ts\nimport { NS } from \"../b\";\nimport { I } from \"../c\";\n\ndeclare module \"../b\" {\n    export namespace NS {\n        export function /*4*/FA();\n    }\n}\n\ndeclare module \"../c\" {\n    export interface /*5*/I {\n        /*6*/FA();\n    }\n}\n\nconst ia: I = {\n    FA: NS.FA,\n    FC() { },\n};\n// @Filename: /home/src/workspaces/project/a2/tsconfig.json\n{\n    \"extends\": \"../tsconfig.settings.json\",\n    \"references\": [\n        { \"path\": \"../b\" },\n        { \"path\": \"../c\" },\n    ],\n    \"files\": [\n        \"index.ts\",\n    ],\n}\n// @Filename: /home/src/workspaces/project/b/index.ts\nexport namespace NS {\n    export function /*7*/FB() {}\n}\n\nexport interface /*8*/I {\n    /*9*/FB();\n}\n\nconst ib: I = { FB() {} };\n// @Filename: /home/src/workspaces/project/b/other.ts\nexport const Other = 1;\n// @Filename: /home/src/workspaces/project/b/tsconfig.json\n{\n    \"extends\": \"../tsconfig.settings.json\",\n    \"files\": [\n        \"index.ts\",\n        \"other.ts\",\n    ],\n}\n// @Filename: /home/src/workspaces/project/c/index.ts\nexport namespace NS {\n    export function /*10*/FC() {}\n}\n\nexport interface /*11*/I {\n    /*12*/FC();\n}\n\nconst ic: I = { FC() {} };\n// @Filename: /home/src/workspaces/project/c/tsconfig.json\n{\n    \"extends\": \"../tsconfig.settings.json\",\n    \"files\": [\n        \"index.ts\",\n    ],\n}\n// @Filename: /home/src/workspaces/project/tsconfig.json\n{\n    \"compilerOptions\": {\n        \"composite\": true,\n        \"lib\": [\"es5\"],\n    },\n    \"references\": [\n        { \"path\": \"a\" },\n        { \"path\": \"a2\" },\n    ],\n    \"files\": []\n}\n// @Filename: /home/src/workspaces/project/tsconfig.settings.json\n{\n    \"compilerOptions\": {\n        \"composite\": true,\n        \"skipLibCheck\": true,\n        \"declarationMap\": true,\n        \"module\": \"CommonJS\",\n        \"emitDeclarationOnly\": true,\n        \"lib\": [\"es5\"],\n    }\n}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.MarkTestAsStradaServer();
                f.VerifyBaselineFindAllReferences(t, "1", "2", "3", "4", "5", "6", "7", "8", "9", "10", "11", "12");
            }
            finally
            {
                done();
            }
        });
    }

}
