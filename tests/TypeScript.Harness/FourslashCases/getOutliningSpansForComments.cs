// ported from: internal/fourslash/tests/getOutliningSpansForComments_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using LSP = TypeScript.LsProto;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class GetOutliningSpansForCommentsCase
{
    public static void TestGetOutliningSpansForComments(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @lib: es5\n[|/*\n    Block comment at the beginning of the file before module:\n        line one of the comment\n        line two of the comment\n        line three\n        line four\n        line five\n*/|]\ndeclare module \"m\";\n[|// Single line comments at the start of the file\n// line 2\n// line 3\n// line 4|]\ndeclare module \"n\";"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.MarkTestAsStradaServer();
                f.VerifyOutliningSpans(t, LSP.FoldingRangeKind.Comment);
            }
            finally
            {
                done();
            }
        });
    }

}
