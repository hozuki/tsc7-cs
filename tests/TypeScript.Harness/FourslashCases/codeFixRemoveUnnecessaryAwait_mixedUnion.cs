// ported from: internal/fourslash/tests/codeFixRemoveUnnecessaryAwait_mixedUnion_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class CodeFixRemoveUnnecessaryAwait_mixedUnionCase
{
    public static void TestCodeFixRemoveUnnecessaryAwait_mixedUnion(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @target: esnext\nasync function fn1(a: Promise<void> | void) {\n  await a;\n}\n\nasync function fn2<T extends Promise<void> | void>(a: T) {\n  await a;\n}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifySuggestionDiagnostics(t, null!);
            }
            finally
            {
                done();
            }
        });
    }

}
