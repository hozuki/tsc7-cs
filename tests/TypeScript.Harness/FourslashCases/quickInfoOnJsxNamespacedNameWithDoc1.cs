// ported from: internal/fourslash/tests/quickInfoOnJsxNamespacedNameWithDoc1_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class QuickInfoOnJsxNamespacedNameWithDoc1Case
{
    public static void TestQuickInfoOnJsxNamespacedNameWithDoc1(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @jsx: react\n// @Filename: /types.d.ts\ndeclare namespace JSX {\n  interface IntrinsicElements {\n    'my-el': {\n      /** This appears */\n      foo: string;\n\n      /** This also appears */\n      'prop:foo': string;\n    };\n  }\n}\n// @filename: /a.tsx\n<my-el /*1*/prop:foo=\"bar\" /*2*/foo=\"baz\" />"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyQuickInfoAt(t, "1", "(property) 'prop:foo': string", "This also appears");
                f.VerifyQuickInfoAt(t, "2", "(property) foo: string", "This appears");
            }
            finally
            {
                done();
            }
        });
    }

}
