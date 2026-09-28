// ported from: internal/fourslash/tests/quickInfoFromContextualUnionType3_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class QuickInfoFromContextualUnionType3Case
{
    public static void TestQuickInfoFromContextualUnionType3(TSU.IHarnessTest t)
    {
        t.Skip("Known failing fourslash test");
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @strict: true\ndeclare const foo1: <D extends Foo1<D>>(definition: D) => D;\n\ntype Foo1<D, Bar = Prop<D, \"bar\">> = {\n  bar: {\n    [K in keyof Bar]: Bar[K] extends boolean\n      ? Bar[K]\n      : \"Error: bar should be boolean\";\n  };\n};\n\ndeclare const foo2: <D extends Foo2<D>>(definition: D) => D;\n\ntype Foo2<D, Bar = Prop<D, \"bar\">> = {\n  bar?: {\n    [K in keyof Bar]: Bar[K] extends boolean\n      ? Bar[K]\n      : \"Error: bar should be boolean\";\n  };\n};\n\ntype Prop<T, K> = K extends keyof T ? T[K] : never;\n\nfoo1({ bar: { /*1*/X: \"test\" } });\n\nfoo2({ bar: { /*2*/X: \"test\" } });"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyQuickInfoAt(t, "1", "(property) X: \"Error: bar should be boolean\"", "");
                f.VerifyQuickInfoAt(t, "2", "(property) X: \"Error: bar should be boolean\"", "");
            }
            finally
            {
                done();
            }
        });
    }

}
