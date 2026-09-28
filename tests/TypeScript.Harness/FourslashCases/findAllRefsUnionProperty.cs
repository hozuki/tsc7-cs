// ported from: internal/fourslash/tests/findAllRefsUnionProperty_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class FindAllRefsUnionPropertyCase
{
    public static void TestFindAllRefsUnionProperty(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("type T =\n    | { /*t0*/type: \"a\", /*p0*/prop: number }\n    | { /*t1*/type: \"b\", /*p1*/prop: string };\nconst tt: T = {\n    /*t2*/type: \"a\",\n    /*p2*/prop: 0,\n};\ndeclare const t: T;\nif (t./*t3*/type === \"a\") {\n    t./*t4*/type;\n} else {\n    t./*t5*/type;\n}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyBaselineFindAllReferences(t, "t0", "t1", "t3", "t4", "t5", "t2", "p0", "p1", "p2");
            }
            finally
            {
                done();
            }
        });
    }

}
