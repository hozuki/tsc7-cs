// ported from: internal/fourslash/tests/renameContextuallyTypedProperties2_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class RenameContextuallyTypedProperties2Case
{
    public static void TestRenameContextuallyTypedProperties2(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("interface I {\n    prop1: () => void;\n    [|[|{| \"contextRangeIndex\": 0 |}prop2|](): void;|]\n}\n\nvar o1: I = {\n    prop1() { },\n    [|[|{| \"contextRangeIndex\": 2 |}prop2|]() { }|]\n};\n\nvar o2: I = {\n    prop1: () => { },\n    [|[|{| \"contextRangeIndex\": 4 |}prop2|]: () => { }|]\n};\n\nvar o3: I = {\n    get prop1() { return () => { }; },\n    [|get [|{| \"contextRangeIndex\": 6 |}prop2|]() { return () => { }; }|]\n};\n\nvar o4: I = {\n    set prop1(v) { },\n    [|set [|{| \"contextRangeIndex\": 8 |}prop2|](v) { }|]\n};\n\nvar o5: I = {\n    \"prop1\"() { },\n    [|\"[|{| \"contextRangeIndex\": 10 |}prop2|]\"() { }|]\n};\n\nvar o6: I = {\n    \"prop1\": function () { },\n    [|\"[|{| \"contextRangeIndex\": 12 |}prop2|]\": function () { }|]\n};\n\nvar o7: I = {\n    [\"prop1\"]: function () { },\n    [|[\"[|{| \"contextRangeIndex\": 14 |}prop2|]\"]: function () { }|]\n};\n\nvar o8: I = {\n    [\"prop1\"]() { },\n    [|[\"[|{| \"contextRangeIndex\": 16 |}prop2|]\"]() { }|]\n};\n\nvar o9: I = {\n    get [\"prop1\"]() { return () => { }; },\n    [|get [\"[|{| \"contextRangeIndex\": 18 |}prop2|]\"]() { return () => { }; }|]\n};\n\nvar o10: I = {\n    set [\"prop1\"](v) { },\n    [|set [\"[|{| \"contextRangeIndex\": 20 |}prop2|]\"](v) { }|]\n};"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyBaselineRenameAtRangesWithText(t, null!, "prop2");
            }
            finally
            {
                done();
            }
        });
    }

}
