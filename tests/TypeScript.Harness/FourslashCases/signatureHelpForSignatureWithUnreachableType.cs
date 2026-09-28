// ported from: internal/fourslash/tests/signatureHelpForSignatureWithUnreachableType_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class SignatureHelpForSignatureWithUnreachableTypeCase
{
    public static void TestSignatureHelpForSignatureWithUnreachableType(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @Filename: /node_modules/foo/node_modules/bar/index.d.ts\nexport interface SomeType {\n    x?: number;\n}\n// @Filename: /node_modules/foo/index.d.ts\nimport { SomeType } from \"bar\";\nexport function func<T extends SomeType>(param: T): void;\nexport function func<T extends SomeType>(param: T, other: T): void;\n// @Filename: /usage.ts\nimport { func } from \"foo\";\nfunc({/*1*/});"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.GoToMarker(t, "1");
                f.VerifySignatureHelp(t, new F.VerifySignatureHelpOptions { Text = "func(param: {}): void", OverloadsCount = 2 });
            }
            finally
            {
                done();
            }
        });
    }

}
