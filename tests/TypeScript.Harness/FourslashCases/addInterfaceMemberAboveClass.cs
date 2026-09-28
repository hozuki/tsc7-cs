// ported from: internal/fourslash/tests/addInterfaceMemberAboveClass_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class AddInterfaceMemberAboveClassCase
{
    public static void TestAddInterfaceMemberAboveClass(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("\ninterface Intersection {\n    /*insertHere*/\n}\ninterface Scene { }\nclass /*className*/Sphere {\n    constructor() {\n    }\n}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyQuickInfoAt(t, "className", "class Sphere", "");
                f.GoToMarker(t, "insertHere");
                f.Insert(t, "ray: Ray;");
                f.VerifyQuickInfoAt(t, "className", "class Sphere", "");
            }
            finally
            {
                done();
            }
        });
    }

}
