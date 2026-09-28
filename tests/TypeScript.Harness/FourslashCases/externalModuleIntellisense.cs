// ported from: internal/fourslash/tests/externalModuleIntellisense_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;
using U = TypeScript.Harness.FourslashCases.FourslashTestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class ExternalModuleIntellisenseCase
{
    public static void TestExternalModuleIntellisense(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @module: commonjs\n// @Filename: externalModuleIntellisense_file0.ts\nexport = express;\nfunction express(): express.ExpressServer;\nnamespace express {\n    export interface ExpressServer {\n        enable(name: string): ExpressServer;\n        post(path: RegExp, handler: (req: Function) => void): void;\n    }\n    export class ExpressServerRequest {\n    }\n}\n// @Filename: externalModuleIntellisense_file1.ts\n///<reference path='externalModuleIntellisense_file0.ts'/>\nimport express = require('./externalModuleIntellisense_file0');\nvar x = express();/*1*/"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.GoToMarker(t, "1");
                f.VerifyNumberOfErrorsInCurrentFile(t, 0);
                f.GoToEOF(t);
                f.Insert(t, "x.");
                f.VerifyCompletions(t, null!, new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Exact = ["enable", "post"] } });
            }
            finally
            {
                done();
            }
        });
    }

}
