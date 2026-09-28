// ported from: internal/fourslash/tests/findAllRefsThisKeyword_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class FindAllRefsThisKeywordCase
{
    public static void TestFindAllRefsThisKeyword(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @noLib: true\n/*1*/this;\nfunction f(/*2*/this) {\n    return /*3*/this;\n    function g(/*4*/this) { return /*5*/this; }\n}\nclass C {\n    static x() {\n        /*6*/this;\n    }\n    static y() {\n        () => /*7*/this;\n    }\n    constructor() {\n        /*8*/this;\n    }\n    method() {\n        () => /*9*/this;\n    }\n}\n// These are *not* real uses of the 'this' keyword, they are identifiers.\nconst x = { /*10*/this: 0 }\nx./*11*/this;"u8);
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
