// ported from: internal/fourslash/tests/quickInfoAssertionNodeNotReusedWhenTypeNotEquivalent1_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class QuickInfoAssertionNodeNotReusedWhenTypeNotEquivalent1Case
{
    public static void TestQuickInfoAssertionNodeNotReusedWhenTypeNotEquivalent1(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @strict: true\ntype Wrapper<T> = {\n  _type: T;\n};\n\nfunction stringWrapper(): Wrapper<string> {\n  return { _type: \"\" };\n}\n\nfunction objWrapper<T extends Record<string, Wrapper<any>>>(\n  obj: T,\n): Wrapper<T> {\n  return { _type: obj };\n}\n\nconst value = objWrapper({\n  prop1: stringWrapper() as Wrapper<\"hello\">,\n});\n\ntype Unwrap<T extends Wrapper<any>> = T[\"_type\"] extends Record<\n  string,\n  Wrapper<any>\n>\n  ? { [Key in keyof T[\"_type\"]]: Unwrap<T[\"_type\"][Key]> }\n  : T[\"_type\"];\n\ntype Test/*1*/ = Unwrap<typeof value>;"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyQuickInfoAt(t, "1", "type Test = {\n    prop1: \"hello\";\n}", "");
            }
            finally
            {
                done();
            }
        });
    }

}
