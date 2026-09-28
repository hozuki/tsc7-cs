// ported from: internal/fourslash/tests/hoverMixinOverrideDocumentation_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class HoverMixinOverrideDocumentationCase
{
    public static void TestHoverMixinOverrideDocumentation(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("\n// @strict: true\n// @filename: main.ts\n\ndeclare class BaseClass {\n    /** some documentation */\n    static method(): number;\n}\n\ntype AnyConstructor = abstract new (...args: any[]) => object\n\nclass MixinClass {}\ndeclare function Mix<T extends AnyConstructor>(BaseClass: T): typeof MixinClass & T;\n\ndeclare class Mixed extends Mix(BaseClass) {\n    static method(): number;\n}\n\nMixed./*1*/method;\n"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyQuickInfoAt(t, "1", "(method) Mixed.method(): number", "some documentation");
            }
            finally
            {
                done();
            }
        });
    }

}
