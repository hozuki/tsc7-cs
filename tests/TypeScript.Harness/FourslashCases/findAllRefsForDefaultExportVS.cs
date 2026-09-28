// ported from: internal/fourslash/tests/findAllRefsForDefaultExportVS_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using LSP = TypeScript.LsProto;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class FindAllRefsForDefaultExportVSCase
{
    public static void TestFindAllRefsForDefaultExportVS(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @Filename: a.ts\nexport default function /*def*/f() {}\n// @Filename: b.ts\nimport /*deg*/g from \"./a\";\n[|/*ref*/g|]();\n// @Filename: c.ts\nimport { f } from \"./a\";"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, new LSP.ClientCapabilities { VSSupportsVisualStudioExtensions = true }, content);
            try
            {
                f.VerifyBaselineVSFindAllReferences(t, "def", "deg");
            }
            finally
            {
                done();
            }
        });
    }

}
