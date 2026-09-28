// ported from: internal/fourslash/tests/quickInfoOnParameterProperties_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class QuickInfoOnParameterPropertiesCase
{
    public static void TestQuickInfoOnParameterProperties(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("interface IFoo {\n  /** this is the name of blabla \n   *  - use blabla \n   *  @example blabla\n   */\n  name?: string;\n}\n\n// test1 should work\nclass Foo implements IFoo {\n  //public name: string = '';\n  constructor(\n    public na/*1*/me: string, // documentation should leech and work ! \n  ) {\n  }\n}\n\n// test2 work\nclass Foo2 implements IFoo {\n  public na/*2*/me: string = ''; // documentation leeched and work ! \n  constructor(\n    //public name: string,\n  ) {\n  }\n}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyBaselineHover(t);
            }
            finally
            {
                done();
            }
        });
    }

}
