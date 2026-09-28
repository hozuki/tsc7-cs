// ported from: internal/fourslash/tests/jsDocInheritDoc_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class JsDocInheritDocCase
{
    public static void TestJsDocInheritDoc(TSU.IHarnessTest t)
    {
        t.Skip("Known failing fourslash test");
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @Filename: inheritDoc.ts\nclass Foo {\n    /**\n     * Foo constructor documentation\n     */\n    constructor(value: number) {}\n    /**\n     * Foo#method1 documentation\n     */\n    static method1() {}\n    /**\n     * Foo#method2 documentation\n     */\n    method2() {}\n    /**\n     * Foo#property1 documentation\n     */\n    property1: string;\n    /**\n     * Foo#property3 documentation\n     */\n    property3 = \"instance prop\";\n}\ninterface Baz {\n    /** Baz#property1 documentation */\n    property1: string;\n    /**\n     * Baz#property2 documentation\n     */\n    property2: object;\n}\nclass Bar extends Foo implements Baz {\n    ctorValue: number;\n    /** @inheritDoc */\n    constructor(value: number) {\n        super(value);\n        this.ctorValue = value;\n    }\n    /** @inheritDoc */\n    static method1() {}\n    method2() {}\n    /** @inheritDoc */\n    property1: string;\n    /**\n     * Bar#property2\n     * @inheritDoc\n     */\n    property2: object;\n\n    static /*6*/property3 = \"class prop\";\n}\nconst b = new Bar/*1*/(5);\nb.method2/*2*/();\nBar.method1/*3*/();\nconst p1 = b.property1/*4*/;\nconst p2 = b.property2/*5*/;"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyQuickInfoAt(t, "1", "constructor Bar(value: number): Bar", "");
                f.VerifyQuickInfoAt(t, "2", "(method) Bar.method2(): void", "Foo#method2 documentation");
                f.VerifyQuickInfoAt(t, "3", "(method) Bar.method1(): void", "Foo#method1 documentation");
                f.VerifyQuickInfoAt(t, "4", "(property) Bar.property1: string", "Foo#property1 documentation");
                f.VerifyQuickInfoAt(t, "5", "(property) Bar.property2: object", "Baz#property2 documentation\nBar#property2");
                f.VerifyQuickInfoAt(t, "6", "(property) Bar.property3: string", "");
            }
            finally
            {
                done();
            }
        });
    }

}
