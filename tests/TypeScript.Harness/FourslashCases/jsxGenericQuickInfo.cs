// ported from: internal/fourslash/tests/jsxGenericQuickInfo_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class JsxGenericQuickInfoCase
{
    public static void TestJsxGenericQuickInfo(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("//@Filename: file.tsx\ndeclare namespace JSX {\n    interface Element { }\n    interface IntrinsicElements {\n    }\n    interface ElementAttributesProperty { props }\n}\ninterface PropsA<T> {\n    /** comments for A */\n    name: 'A',\n    items: T[];\n    renderItem: (item: T) => string;\n}\ninterface PropsB<T> {\n    /** comments for B */\n    name: 'B',\n    items: T[];\n    renderItem: (item: T) => string;\n}\nclass Component<T> {\n    constructor(props: PropsA<T> | PropsB<T>) {}\n    props: PropsA<T> | PropsB<T>;\n}   \nvar b = new Component({items: [0, 1, 2], render/*0*/Item: it/*1*/em => item.toFixed(), name/*2*/: 'A',});\nvar c = <Component items={[0, 1, 2]} render/*3*/Item={it/*4*/em => item.toFixed()} name/*5*/=\"A\" />"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyQuickInfoAt(t, "0", "(property) PropsA<number>.renderItem: (item: number) => string", "");
                f.VerifyQuickInfoAt(t, "1", "(parameter) item: number", "");
                f.VerifyQuickInfoAt(t, "2", "(property) PropsA<T>.name: \"A\"", "comments for A");
                f.VerifyQuickInfoAt(t, "3", "(property) PropsA<number>.renderItem: (item: number) => string", "");
                f.VerifyQuickInfoAt(t, "4", "(parameter) item: number", "");
                f.VerifyQuickInfoAt(t, "5", "(property) PropsA<T>.name: \"A\"", "comments for A");
            }
            finally
            {
                done();
            }
        });
    }

}
