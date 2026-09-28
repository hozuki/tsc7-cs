// ported from: internal/fourslash/tests/autoImportCJSWithNodeModuleKind_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class AutoImportCJSWithNodeModuleKindCase
{
    public static void TestAutoImportCJSWithNodeModuleKind(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @Filename: /tsconfig.json\n{\n  \"compilerOptions\": {\n    \"allowJs\": true,\n    \"module\": \"node20\",\n    \"checkJs\": true,\n    \"noEmit\": true\n  }\n}\n// @Filename: /package.json\n{ \"type\": \"commonjs\" }\n// @Filename: /lib.js\nmodule.exports = { LIB_VERSION: 1 };\n// @Filename: /main.js\nmodule.exports.foo = 0;\nLIB_VERSION/**/"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.GoToMarker(t, "");
                f.VerifyImportFixAtPosition(t, ["const { LIB_VERSION } = require(\"./lib\");\n\nmodule.exports.foo = 0;\nLIB_VERSION"], null!);
            }
            finally
            {
                done();
            }
        });
    }

    public static void TestAutoImportCJSWithNodeModuleKindEmptyFile(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @Filename: /tsconfig.json\n{\n  \"compilerOptions\": {\n    \"allowJs\": true,\n    \"module\": \"node20\",\n    \"checkJs\": true,\n    \"noEmit\": true\n  }\n}\n// @Filename: /package.json\n{ \"type\": \"commonjs\" }\n// @Filename: /lib.js\nmodule.exports = { LIB_VERSION: 1 };\n// @Filename: /main.js\nLIB_VERSION/**/"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.GoToMarker(t, "");
                f.VerifyImportFixAtPosition(t, ["const { LIB_VERSION } = require(\"./lib\");\n\nLIB_VERSION"], null!);
            }
            finally
            {
                done();
            }
        });
    }

    public static void TestAutoImportCJSWithModuleDetectionForce(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @Filename: /tsconfig.json\n{\n  \"compilerOptions\": {\n    \"allowJs\": true,\n    \"module\": \"preserve\",\n    \"moduleDetection\": \"force\",\n    \"checkJs\": true,\n    \"noEmit\": true\n  }\n}\n// @Filename: /lib.js\nexport const LIB_VERSION = 1;\n// @Filename: /main.js\nconst path = require(\"path\");\nLIB_VERSION/**/"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.GoToMarker(t, "");
                f.VerifyImportFixAtPosition(t, ["const path = require(\"path\");\nconst { LIB_VERSION } = require(\"./lib\");\nLIB_VERSION"], null!);
            }
            finally
            {
                done();
            }
        });
    }

}
