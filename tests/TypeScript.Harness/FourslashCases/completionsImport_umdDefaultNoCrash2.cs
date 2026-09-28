// ported from: internal/fourslash/tests/completionsImport_umdDefaultNoCrash2_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using LSP = TypeScript.LsProto;
using TSU = TypeScript.TestUtil;
using U = TypeScript.Harness.FourslashCases.FourslashTestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class CompletionsImport_umdDefaultNoCrash2Case
{
    public static void TestCompletionsImport_umdDefaultNoCrash2(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @moduleResolution: bundler\n// @allowJs: true\n// @checkJs: true\n// @Filename: /node_modules/dottie/package.json\n{\n  \"name\": \"dottie\",\n  \"main\": \"dottie.js\"\n}\n// @Filename: /node_modules/dottie/dottie.js\n(function (undefined) {\n  var root = this;\n\n  var Dottie = function () {};\n\n  Dottie[\"default\"] = function (object, path, value) {};\n\n  if (typeof module !== \"undefined\" && module.exports) {\n    exports = module.exports = Dottie;\n  } else {\n    root[\"Dottie\"] = Dottie;\n    root[\"Dot\"] = Dottie;\n\n    if (typeof define === \"function\") {\n      define([], function () {\n        return Dottie;\n      });\n    }\n  }\n})();\n// @Filename: /src/index.js\nimport Dottie from 'dottie';\n/**/"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyCompletions(t, "", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Includes = [new LSP.CompletionItem { Label = "Dottie" }] } });
            }
            finally
            {
                done();
            }
        });
    }

}
