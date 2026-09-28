// ported from: internal/fourslash/tests/findAllRefsInheritedProperties3_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class FindAllRefsInheritedProperties3Case
{
    public static void TestFindAllRefsInheritedProperties3(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("class class1 extends class1 {\n    [|/*0*/doStuff() { }|]\n    [|/*1*/propName: string;|]\n}\ninterface interface1 extends interface1 {\n    [|/*2*/doStuff(): void;|]\n    [|/*3*/propName: string;|]\n}\nclass class2 extends class1 implements interface1 {\n    [|/*4*/doStuff() { }|]\n    [|/*5*/propName: string;|]\n}\n\nvar v: class2;\nv./*6*/doStuff();\nv./*7*/propName;"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyBaselineFindAllReferences(t, "0", "1", "2", "3", "4", "6", "5", "7");
            }
            finally
            {
                done();
            }
        });
    }

}
