// ported from: internal/fourslash/tests/quickInfoJsxNamespacedIntrinsic_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class QuickInfoJsxNamespacedIntrinsicCase
{
    public static void TestQuickInfoJsxNamespacedIntrinsic(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @jsx: react\n// @Filename: /a.tsx\ndeclare const React: any;\ndeclare namespace JSX {\n    interface Element {}\n    interface IntrinsicElements {\n        /** Element docs */\n        \"foo:bar\": {\n            /** Foo docs */\n            foo: boolean\n            /** Bar docs */\n            bar: string\n        }\n    }\n}\n<foo:ba/*tag*/r fo/*attr*/o />"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyQuickInfoAt(t, "tag", "(property) JSX.IntrinsicElements[\"foo:bar\"]: {\n    foo: boolean;\n    bar: string;\n}", "Element docs");
                f.VerifyQuickInfoAt(t, "attr", "(property) foo: boolean", "Foo docs");
            }
            finally
            {
                done();
            }
        });
    }

}
