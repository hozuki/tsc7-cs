// ported from: internal/fourslash/tests/quickinfoVerbosityClass2_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class QuickinfoVerbosityClass2Case
{
    public static void TestQuickinfoVerbosityClass2(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("interface Apple {\n    color: string;\n}\nclass Foo/*1*/<T> {\n    constructor(public x: T) { }\n    public y!: T;\n    static whatever(): void { }\n    private foo(): Apple { return { color: \"green\" }; }\n    static {\n        const a = class { x?: Apple; };\n    }\n    protected z = true;\n}\ntype Whatever/*2*/ = Foo<string>;\nconst a/*3*/ = Foo;\nconst c/*4*/ = Foo<string>;\n[1].forEach(class/*5*/ <T> {\n    constructor(public x: T) { }\n    public y!: T;\n    static whatever(): void { }\n    private foo(): Apple { return { color: \"green\" }; }\n    static {\n        const a = class { x?: Apple; };\n    }\n    protected z = true;\n});\nconst b/*6*/ = Bar<number>;\n@random()\nabstract class Animal/*7*/ {\n    name!: string;\n    abstract makeSound(): void;\n}\nclass Dog/*8*/ {\n    what(this: this, that: Dog) { }\n    #bones: string[];\n}\nconst d/*9*/ = new Dog();"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyBaselineHoverWithVerbosity(t, new Dictionary<string, int[]> { ["1"] = [0, 1, 2], ["2"] = [0, 1, 2], ["3"] = [0, 1], ["4"] = [0], ["5"] = [0, 1, 2], ["6"] = [0], ["7"] = [0, 1], ["8"] = [0, 1], ["9"] = [0, 1] });
            }
            finally
            {
                done();
            }
        });
    }

}
