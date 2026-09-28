// ported from: internal/fourslash/tests/importNameCodeFix_order2_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class ImportNameCodeFix_order2Case
{
    public static void TestImportNameCodeFix_order2(TSU.IHarnessTest t)
    {
        t.Skip("Known failing fourslash test");
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @Filename: /a.ts\nexport const _aB: number;\nexport const _Ab: number;\nexport const aB: number;\nexport const Ab: number;\n// @Filename: /b.ts\n[|import {\n    _aB,\n    _Ab,\n    Ab,\n} from \"./a\";\naB;|]\n// @Filename: /c.ts\n[|import {\n    _aB,\n    _Ab,\n    Ab,\n} from \"./a\";\naB;|]"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.GoToFile(t, "/b.ts");
                f.VerifyImportFixAtPosition(t, ["import {\n    _aB,\n    _Ab,\n    Ab,\n    aB,\n} from \"./a\";\naB;"], null!);
                f.GoToFile(t, "/c.ts");
                f.VerifyImportFixAtPosition(t, ["import {\n    _aB,\n    _Ab,\n    aB,\n    Ab,\n} from \"./a\";\naB;"], null!);
            }
            finally
            {
                done();
            }
        });
    }

}
