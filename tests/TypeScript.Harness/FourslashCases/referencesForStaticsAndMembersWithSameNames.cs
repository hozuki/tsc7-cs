// ported from: internal/fourslash/tests/referencesForStaticsAndMembersWithSameNames_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class ReferencesForStaticsAndMembersWithSameNamesCase
{
    public static void TestReferencesForStaticsAndMembersWithSameNames(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("namespace FindRef4 {\n\tnamespace MixedStaticsClassTest {\n\t\texport class Foo {\n\t\t\t/*1*/bar: Foo;\n\t\t\t/*2*/static /*3*/bar: Foo;\n\n\t\t\t/*4*/public /*5*/foo(): void {\n\t\t\t}\n\t\t\t/*6*/public static /*7*/foo(): void {\n\t\t\t}\n\t\t}\n\t}\n\n\tfunction test() {\n\t\t// instance function\n\t\tvar x = new MixedStaticsClassTest.Foo();\n\t\tx./*8*/foo();\n\t\tx./*9*/bar;\n\n\t\t// static function\n\t\tMixedStaticsClassTest.Foo./*10*/foo();\n\t\tMixedStaticsClassTest.Foo./*11*/bar;\n\t}\n}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyBaselineFindAllReferences(t, "1", "2", "3", "4", "5", "6", "7", "8", "9", "10", "11");
            }
            finally
            {
                done();
            }
        });
    }

}
