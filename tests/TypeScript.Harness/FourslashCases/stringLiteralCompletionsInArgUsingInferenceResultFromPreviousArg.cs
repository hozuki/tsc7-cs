// ported from: internal/fourslash/tests/stringLiteralCompletionsInArgUsingInferenceResultFromPreviousArg_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;
using U = TypeScript.Harness.FourslashCases.FourslashTestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class StringLiteralCompletionsInArgUsingInferenceResultFromPreviousArgCase
{
    public static void TestStringLiteralCompletionsInArgUsingInferenceResultFromPreviousArg(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @strict: true\n// https://github.com/microsoft/TypeScript/issues/55545\nenum myEnum {\n  valA = \"valA\",\n  valB = \"valB\",\n}\n\ninterface myEnumParamMapping {\n  [\"valA\"]: \"1\" | \"2\";\n  [\"valB\"]: \"3\" | \"4\";\n}\n\nfunction myFunction<K extends keyof typeof myEnum>(\n  a: K,\n  b: myEnumParamMapping[K],\n) {}\n\nmyFunction(\"valA\", \"/*ts1*/\");\nmyFunction(\"valA\", `/*ts2*/`);\n\nfunction myFunction2<K extends keyof typeof myEnum>(\n  a: K,\n  { b }: { b: myEnumParamMapping[K] },\n) {}\n\nmyFunction2(\"valA\", { b: \"/*ts3*/\" });\nmyFunction2(\"valA\", { b: `/*ts4*/` });"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyCompletions(t, new F.MarkerInput { MarkerNames = ["ts1", "ts2", "ts3", "ts4"] }, new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Exact = ["1", "2"] } });
            }
            finally
            {
                done();
            }
        });
    }

}
