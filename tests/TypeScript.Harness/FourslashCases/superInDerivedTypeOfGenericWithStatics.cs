// ported from: internal/fourslash/tests/superInDerivedTypeOfGenericWithStatics_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class SuperInDerivedTypeOfGenericWithStaticsCase
{
    public static void TestSuperInDerivedTypeOfGenericWithStatics(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @strict: false\nnamespace M {\n   export class C<T extends Date> {\n      static foo(): C<Date> {\n          return null;\n           }\n     }\n}\nclass D extends M.C<Date> {\n    constructor() {\n        /**/ // was an error appearing on super in editing scenarios\n       }\n}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.GoToMarker(t, "");
                f.Insert(t, "super();");
                f.VerifyNoErrors(t);
            }
            finally
            {
                done();
            }
        });
    }

}
