// ported from: internal/fourslash/tests/importNameCodeFixDefaultExport1_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class ImportNameCodeFixDefaultExport1Case
{
    public static void TestImportNameCodeFixDefaultExport1(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @Filename: /foo-bar.ts\nexport default function fooBar();\n// @Filename: /b.ts\n[|import * as fb from \"./foo-bar\";\nfoo/**/Bar|]"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.GoToFile(t, "/b.ts");
                f.VerifyImportFixAtPosition(t, ["import fooBar, * as fb from \"./foo-bar\";\nfooBar"], null!);
            }
            finally
            {
                done();
            }
        });
    }

}
