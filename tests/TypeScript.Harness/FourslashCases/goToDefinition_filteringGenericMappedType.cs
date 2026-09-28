// ported from: internal/fourslash/tests/goToDefinition_filteringGenericMappedType_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class GoToDefinition_filteringGenericMappedTypeCase
{
    public static void TestGoToDefinition_filteringGenericMappedType(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("const obj = {\n  get /*def*/id() {\n    return 1;\n  },\n  name: \"test\",\n};\n\ntype Omit2<T, DroppedKeys extends PropertyKey> = {\n  [K in keyof T as Exclude<K, DroppedKeys>]: T[K];\n};\n\ndeclare function omit2<O, Mask extends { [K in keyof O]?: true }>(\n  obj: O,\n  mask: Mask\n): Omit2<O, keyof Mask>;\n\nconst obj2 = omit2(obj, {\n  name: true,\n});\n\nobj2.[|/*ref*/id|];"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyBaselineGoToDefinition(t, true, "ref");
            }
            finally
            {
                done();
            }
        });
    }

}
