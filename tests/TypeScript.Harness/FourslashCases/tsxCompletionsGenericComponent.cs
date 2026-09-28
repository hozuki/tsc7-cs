// ported from: internal/fourslash/tests/tsxCompletionsGenericComponent_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;
using U = TypeScript.Harness.FourslashCases.FourslashTestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class TsxCompletionsGenericComponentCase
{
    public static void TestTsxCompletionsGenericComponent(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @jsx: preserve\n// @skipLibCheck: true\n// @Filename: file.tsx\n declare namespace JSX {\n     interface Element { }\n     interface IntrinsicElements {\n     }\n     interface ElementAttributesProperty { props; }\n }\n\nclass Table<P> {\n    constructor(public props: P) {}\n}\n\ntype Props = { widthInCol: number; text: string; };\n\n/**\n * @param width {number} Table width in px\n */\nfunction createTable(width) {\n    return <Table<Props> /*1*/ />\n}\n\ncreateTable(800);"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyCompletions(t, "1", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Includes = ["widthInCol", "text"] } });
            }
            finally
            {
                done();
            }
        });
    }

}
