// ported from: internal/fourslash/tests/classInterfaceInsert_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class ClassInterfaceInsertCase
{
    public static void TestClassInterfaceInsert(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("interface Intersection {\n    dist: number;\n}\n/*interfaceGoesHere*/\nclass /*className*/Sphere {\n    constructor(private center) {\n    }\n}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyQuickInfoAt(t, "className", "class Sphere", "");
                f.GoToMarker(t, "interfaceGoesHere");
                f.Insert(t, "\ninterface Surface {\n    reflect: () => number;\n}\n");
                f.VerifyQuickInfoAt(t, "className", "class Sphere", "");
            }
            finally
            {
                done();
            }
        });
    }

}
