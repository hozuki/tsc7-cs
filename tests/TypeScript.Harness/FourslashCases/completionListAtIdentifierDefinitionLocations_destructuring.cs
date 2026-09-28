// ported from: internal/fourslash/tests/completionListAtIdentifierDefinitionLocations_destructuring_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class CompletionListAtIdentifierDefinitionLocations_destructuringCase
{
    public static void TestCompletionListAtIdentifierDefinitionLocations_destructuring(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @Filename: a.ts\nvar [x/*variable1*/\n// @Filename: b.ts\nvar [x, y/*variable2*/\n// @Filename: c.ts\nvar [./*variable3*/\n// @Filename: d.ts\nvar [x, ...z/*variable4*/\n// @Filename: e.ts\nvar {x/*variable5*/\n// @Filename: f.ts\nvar {x, y/*variable6*/\n// @Filename: g.ts\nfunction func1({ a/*parameter1*/\n// @Filename: h.ts\nfunction func2({ a, b/*parameter2*/"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyCompletions(t, f.Markers(), null!);
            }
            finally
            {
                done();
            }
        });
    }

}
