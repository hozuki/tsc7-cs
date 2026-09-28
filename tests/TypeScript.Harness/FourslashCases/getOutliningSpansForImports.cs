// ported from: internal/fourslash/tests/getOutliningSpansForImports_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using LSP = TypeScript.LsProto;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class GetOutliningSpansForImportsCase
{
    public static void TestGetOutliningSpansForImports(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("[|import * as ns from \"mod\";\n\nimport d from \"mod\";\nimport { a, b, c } from \"mod\";\n\nimport r = require(\"mod\");|]\n\n// statement\nvar x = 0;\n\n// another set of imports\n[|import * as ns from \"mod\";\nimport d from \"mod\";\nimport { a, b, c } from \"mod\";\nimport r = require(\"mod\");|]"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyOutliningSpans(t, LSP.FoldingRangeKind.Imports);
            }
            finally
            {
                done();
            }
        });
    }

}
