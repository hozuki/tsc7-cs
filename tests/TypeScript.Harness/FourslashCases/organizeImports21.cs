// ported from: internal/fourslash/tests/organizeImports21_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using LSP = TypeScript.LsProto;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class OrganizeImports21Case
{
    public static void TestOrganizeImports21(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @filename: /a.ts\nexport interface LocationDefinitions {}\nexport interface PersonDefinitions {}\n// @filename: /b.ts\nexport {\n    /** @deprecated Use LocationDefinitions instead */\n    LocationDefinitions as AddressDefinitions,\n    LocationDefinitions,\n    /** @deprecated Use PersonDefinitions instead */\n    PersonDefinitions as NameDefinitions,\n    PersonDefinitions,\n} from './a';"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.GoToFile(t, "/b.ts");
                f.VerifyOrganizeImports(t, "export {\n    /** @deprecated Use LocationDefinitions instead */\n    LocationDefinitions as AddressDefinitions,\n    LocationDefinitions,\n    /** @deprecated Use PersonDefinitions instead */\n    PersonDefinitions as NameDefinitions,\n    PersonDefinitions\n} from './a';\n", LSP.CodeActionKind.SourceOrganizeImportsTs, null!);
            }
            finally
            {
                done();
            }
        });
    }

}
