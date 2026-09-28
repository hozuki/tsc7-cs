// ported from: internal/fourslash/tests/outliningSpansForImportAndExportAttributes_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class OutliningSpansForImportAndExportAttributesCase
{
    public static void TestOutliningSpansForImportAndExportAttributes(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("import { a1, a2 } from \"a\";\n;\nimport {\n} from \"a\";\n;\nimport [|{\n  b1,\n  b2,\n}|] from \"b\";\n;\nimport j1 from \"./j\" with { type: \"json\" };\n;\nimport j2 from \"./j\" with {\n};\n;\nimport j3 from \"./j\" with [|{\n  type: \"json\"\n}|];\n;\n[|import { a5, a6 } from \"a\";\nimport [|{\n  a7,\n  a8,\n}|] from \"a\";|]\nexport { a1, a2 };\n;\nexport { a3, a4 } from \"a\";\n;\nexport {\n};\n;\nexport [|{\n  b1,\n  b2,\n}|];\n;\nexport {\n} from \"b\";\n;\nexport [|{\n  b3,\n  b4,\n}|] from \"b\";\n;"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyOutliningSpans(t);
            }
            finally
            {
                done();
            }
        });
    }

}
