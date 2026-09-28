// ported from: internal/fourslash/tests/quickInfoStaticPrototypePropertyOnClass_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class QuickInfoStaticPrototypePropertyOnClassCase
{
    public static void TestQuickInfoStaticPrototypePropertyOnClass(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("class c1 {\n}\nclass c2<T> {\n}\nclass c3 {\n    constructor() {\n    }\n}\nclass c4 {\n    constructor(param: string);\n    constructor(param: number);\n    constructor(param: any) {\n    }\n}\nc1./*1*/prototype;\nc2./*2*/prototype;\nc3./*3*/prototype;\nc4./*4*/prototype;"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyQuickInfoAt(t, "1", "(property) c1.prototype: c1", "");
                f.VerifyQuickInfoAt(t, "2", "(property) c2<T>.prototype: c2<any>", "");
                f.VerifyQuickInfoAt(t, "3", "(property) c3.prototype: c3", "");
                f.VerifyQuickInfoAt(t, "4", "(property) c4.prototype: c4", "");
            }
            finally
            {
                done();
            }
        });
    }

}
