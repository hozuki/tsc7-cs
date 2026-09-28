// ported from: internal/fourslash/tests/referencesForAmbients_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class ReferencesForAmbientsCase
{
    public static void TestReferencesForAmbients(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("/*1*/declare module \"/*2*/foo\" {\n    /*3*/var /*4*/f: number;\n}\n\n/*5*/declare module \"/*6*/bar\" {\n    /*7*/export import /*8*/foo = require(\"/*9*/foo\");\n    var f2: typeof /*10*/foo./*11*/f;\n}\n\ndeclare module \"baz\" {\n    /*12*/import bar = require(\"/*13*/bar\");\n    var f2: typeof bar./*14*/foo;\n}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyBaselineFindAllReferences(t, "1", "2", "3", "4", "5", "6", "7", "8", "9", "10", "11", "12", "13", "14");
            }
            finally
            {
                done();
            }
        });
    }

}
