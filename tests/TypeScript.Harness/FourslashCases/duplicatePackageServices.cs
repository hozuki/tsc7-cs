// ported from: internal/fourslash/tests/duplicatePackageServices_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class DuplicatePackageServicesCase
{
    public static void TestDuplicatePackageServices(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @noImplicitReferences: true\n// @Filename: /node_modules/a/index.d.ts\nimport [|X/*useAX*/|] from \"x\";\nexport function a(x: X): void;\n// @Filename: /node_modules/a/node_modules/x/index.d.ts\nexport default class /*defAX*/X {\n    private x: number;\n}\n// @Filename: /node_modules/a/node_modules/x/package.json\n{ \"name\": \"x\", \"version\": \"1.2.3\" }\n// @Filename: /node_modules/b/index.d.ts\nimport [|X/*useBX*/|] from \"x\";\nexport const b: X;\n// @Filename: /node_modules/b/node_modules/x/index.d.ts\nexport default class /*defBX*/X {\n    private x: number;\n}\n// @Filename: /node_modules/b/node_modules/x/package.json\n{ \"name\": \"x\", \"version\": \"1.2.3\" }\n// @Filename: /src/a.ts\nimport { a } from \"a\";\nimport { b } from \"b\";\na(/*error*/b);"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.GoToFile(t, "/src/a.ts");
                f.VerifyNumberOfErrorsInCurrentFile(t, 0);
                f.VerifyBaselineFindAllReferences(t, "useAX", "defAX", "useBX");
                f.VerifyBaselineGoToDefinition(t, true, "useAX", "useBX");
            }
            finally
            {
                done();
            }
        });
    }

}
