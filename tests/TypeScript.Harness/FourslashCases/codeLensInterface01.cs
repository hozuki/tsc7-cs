// ported from: internal/fourslash/tests/codeLensInterface01_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using LSUP = TypeScript.LsUtil;
using TRI = TypeScript.Core.Tristate;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class CodeLensInterface01Case
{
    public static void TestCodeLensInterface01(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("\n// @module: preserve\n\n// @filename: ./pointable.ts\nexport interface Pointable {\n  getX(): number;\n  getY(): number;\n}\n\n// @filename: ./classPointable.ts\nimport { Pointable } from \"./pointable\";\n\nclass Point implements Pointable {\n  getX(): number {\n    return 0;\n  }\n  getY(): number {\n    return 0;\n  }\n}\n\n// @filename: ./objectPointable.ts\nimport { Pointable } from \"./pointable\";\n\nlet x = 0;\nlet y = 0;\nconst p: Pointable = {\n  getX(): number {\n\treturn x;\n  },\n  getY(): number {\n\treturn y;\n  },\n};\n"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyBaselineCodeLens(t, new LSUP.UserPreferences { CodeLens = new LSUP.CodeLensUserPreferences { ReferencesCodeLensEnabled = TRI.TSTrue, ReferencesCodeLensShowOnAllFunctions = TRI.TSTrue, ImplementationsCodeLensEnabled = TRI.TSTrue, ImplementationsCodeLensShowOnInterfaceMethods = TRI.TSTrue, ImplementationsCodeLensShowOnAllClassMethods = TRI.TSTrue } });
            }
            finally
            {
                done();
            }
        });
    }

}
