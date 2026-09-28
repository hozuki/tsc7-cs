// ported from: internal/fourslash/tests/findAllRefsForDefaultExport_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class FindAllRefsForDefaultExportCase
{
    public static void TestFindAllRefsForDefaultExport(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @Filename: a.ts\nexport default function /*def*/f() {}\n// @Filename: b.ts\nimport /*deg*/g from \"./a\";\n[|/*ref*/g|]();\n// @Filename: c.ts\nimport { f } from \"./a\";"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyBaselineFindAllReferences(t, "def", "deg");
                f.VerifyBaselineGoToDefinition(t, true, "ref");
            }
            finally
            {
                done();
            }
        });
    }

}
