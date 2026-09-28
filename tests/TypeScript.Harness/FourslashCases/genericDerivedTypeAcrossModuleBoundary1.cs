// ported from: internal/fourslash/tests/genericDerivedTypeAcrossModuleBoundary1_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class GenericDerivedTypeAcrossModuleBoundary1Case
{
    public static void TestGenericDerivedTypeAcrossModuleBoundary1(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("namespace M {\n   export class C1 { }\n   export class C2<T> { }\n}\nvar c = new M.C2<number>();\nnamespace N {\n   export class D1 extends M.C1 { }\n   export class D2<T> extends M.C2<T> { }\n}\nvar n = new N.D1();\nvar /*1*/n2 = new N.D2<number>();\nvar /*2*/n3 = new N.D2();"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyQuickInfoAt(t, "1", "var n2: N.D2<number>", "");
                f.VerifyQuickInfoAt(t, "2", "var n3: N.D2<unknown>", "");
            }
            finally
            {
                done();
            }
        });
    }

}
