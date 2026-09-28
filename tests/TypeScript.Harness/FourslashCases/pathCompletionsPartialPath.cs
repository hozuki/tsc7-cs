// ported from: internal/fourslash/tests/pathCompletionsPartialPath_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class PathCompletionsPartialPathCase
{
    public static void TestPathCompletionsPartialPathRelativeImport(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @Filename: /src/main.ts\nimport { } from \"./foo//*$*/\";\n// @Filename: /src/foo/async.ts\nexport const asyncApi = \"async\";\n// @Filename: /src/foo/fs.ts\nexport const fsApi = \"fs\";\n// @Filename: /src/foo/sync.ts\nexport const syncApi = \"sync\";"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyCompletions(t, "$", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = [], EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Exact = ["async", "fs", "sync"] } });
            }
            finally
            {
                done();
            }
        });
    }

    public static void TestPathCompletionsPartialPathPackageNoExports(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @moduleResolution: bundler\n// @Filename: /node_modules/@typescript/typescript/package.json\n{ \"name\": \"@typescript/typescript\", \"version\": \"0.0.0\" }\n// @Filename: /node_modules/@typescript/typescript/unstable/async.ts\nexport const asyncApi = \"async\";\n// @Filename: /node_modules/@typescript/typescript/unstable/fs.ts\nexport const fsApi = \"fs\";\n// @Filename: /node_modules/@typescript/typescript/unstable/sync.ts\nexport const syncApi = \"sync\";\n// @Filename: /package.json\n{ \"dependencies\": { \"@typescript/typescript\": \"0.0.0\" } }\n// @Filename: /src/main.ts\nimport { } from \"@typescript/typescript/unstable//*$*/\";"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyCompletions(t, "$", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = [], EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Exact = ["async", "fs", "sync"] } });
            }
            finally
            {
                done();
            }
        });
    }

    public static void TestPathCompletionsPartialPathPackageExports(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @moduleResolution: bundler\n// @Filename: /node_modules/@typescript/typescript/package.json\n{\n\t\"name\": \"@typescript/typescript\",\n\t\"version\": \"0.0.0\",\n\t\"exports\": {\n\t\t\"./unstable/sync\": \"./dist/api/sync/api.js\",\n\t\t\"./unstable/async\": \"./dist/api/async/api.js\",\n\t\t\"./unstable/fs\": \"./dist/api/fs.js\"\n\t}\n}\n// @Filename: /node_modules/@typescript/typescript/index.d.ts\nexport {};\n// @Filename: /node_modules/@typescript/typescript/dist/api/async/api.js\nexport const asyncApi = \"async\";\n// @Filename: /node_modules/@typescript/typescript/dist/api/fs.js\nexport const fsApi = \"fs\";\n// @Filename: /node_modules/@typescript/typescript/dist/api/sync/api.js\nexport const syncApi = \"sync\";\n// @Filename: /package.json\n{ \"dependencies\": { \"@typescript/typescript\": \"0.0.0\" } }\n// @Filename: /src/main.ts\nimport { } from \"@typescript/typescript/unstable//*$*/\";"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyCompletions(t, "$", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = [], EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Exact = ["async", "fs", "sync"] } });
            }
            finally
            {
                done();
            }
        });
    }

    public static void TestPathCompletionsPartialPathPackageExportsEndingStar(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @moduleResolution: bundler\n// @Filename: /node_modules/@typescript/typescript/package.json\n{\n\t\"name\": \"@typescript/typescript\",\n\t\"version\": \"0.0.0\",\n\t\"exports\": {\n\t\t\"./unstable/*\": \"./dist/unstable/*.d.ts\"\n\t}\n}\n// @Filename: /node_modules/@typescript/typescript/dist/unstable/async.d.ts\nexport declare const asyncApi: string;\n// @Filename: /node_modules/@typescript/typescript/dist/unstable/fs.d.ts\nexport declare const fsApi: string;\n// @Filename: /node_modules/@typescript/typescript/dist/unstable/sync.d.ts\nexport declare const syncApi: string;\n// @Filename: /package.json\n{ \"dependencies\": { \"@typescript/typescript\": \"0.0.0\" } }\n// @Filename: /src/main.ts\nimport { } from \"@typescript/typescript/unstable//*$*/\";"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyCompletions(t, "$", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = [], EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Exact = ["async", "fs", "sync"] } });
            }
            finally
            {
                done();
            }
        });
    }

    public static void TestPathCompletionsPartialPathPackageExportsMiddleStar(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @moduleResolution: bundler\n// @Filename: /node_modules/@typescript/typescript/package.json\n{\n\t\"name\": \"@typescript/typescript\",\n\t\"version\": \"0.0.0\",\n\t\"exports\": {\n\t\t\"./unstable/_*/api\": \"./dist/api/*.d.ts\"\n\t}\n}\n// @Filename: /node_modules/@typescript/typescript/dist/api/async.d.ts\nexport declare const asyncApi: string;\n// @Filename: /node_modules/@typescript/typescript/dist/api/fs.d.ts\nexport declare const fsApi: string;\n// @Filename: /node_modules/@typescript/typescript/dist/api/sync.d.ts\nexport declare const syncApi: string;\n// @Filename: /package.json\n{ \"dependencies\": { \"@typescript/typescript\": \"0.0.0\" } }\n// @Filename: /src/main.ts\nimport { } from \"@typescript/typescript/unstable//*$*/\";"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyCompletions(t, "$", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = [], EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Exact = ["_async/api", "_fs/api", "_sync/api"] } });
            }
            finally
            {
                done();
            }
        });
    }

}
