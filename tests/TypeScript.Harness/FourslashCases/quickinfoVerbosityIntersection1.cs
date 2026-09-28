// ported from: internal/fourslash/tests/quickinfoVerbosityIntersection1_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class QuickinfoVerbosityIntersection1Case
{
    public static void TestQuickinfoVerbosityIntersection1(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("{\n    type Foo = { a: \"a\" | \"c\" };\n    type Bar = { a: \"a\" | \"b\" };\n    const obj/*o1*/: Foo & Bar = { a: \"a\" };\n}\n{\n    type Foo = { a: \"c\" };\n    type Bar = { a: \"b\" };\n    const obj/*o2*/: Foo & Bar = { a: \"\" };\n}\n{\n    type Foo = { a: \"c\" };\n    type Bar = { a: \"b\" };\n    type Never = Foo & Bar;\n    const obj/*o3*/: Never = { a: \"\" };\n}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyBaselineHoverWithVerbosity(t, new Dictionary<string, int[]> { ["o1"] = [0, 1], ["o2"] = [0], ["o3"] = [0] });
            }
            finally
            {
                done();
            }
        });
    }

}
