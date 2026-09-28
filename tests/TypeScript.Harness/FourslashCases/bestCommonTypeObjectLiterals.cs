// ported from: internal/fourslash/tests/bestCommonTypeObjectLiterals_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class BestCommonTypeObjectLiteralsCase
{
    public static void TestBestCommonTypeObjectLiterals(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @stableTypeOrdering: true\nvar a = { name: 'bob', age: 18 };\nvar b = { name: 'jim', age: 20 };\nvar /*1*/c = [a, b];\nvar a1 = { name: 'bob', age: 18 };\nvar b1 = { name: 'jim', age: 20, dob: new Date() };\nvar /*2*/c1 = [a1, b1];\nvar a2 = { name: 'bob', age: 18, address: 'springfield' };\nvar b2 = { name: 'jim', age: 20, dob: new Date() };\nvar /*3*/c2 = [a2, b2];\ninterface I {\n    name: string;\n    age: number;\n}\nvar i: I;\nvar /*4*/c3 = [i, a];"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyQuickInfoAt(t, "1", "var c: {\n    name: string;\n    age: number;\n}[]", "");
                f.VerifyQuickInfoAt(t, "2", "var c1: {\n    name: string;\n    age: number;\n}[]", "");
                f.VerifyQuickInfoAt(t, "3", "var c2: ({\n    name: string;\n    age: number;\n    address: string;\n} | {\n    name: string;\n    age: number;\n    dob: Date;\n})[]", "");
                f.VerifyQuickInfoAt(t, "4", "var c3: I[]", "");
            }
            finally
            {
                done();
            }
        });
    }

}
