// ported from: internal/fourslash/tests/callHierarchyContainerName_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class CallHierarchyContainerNameCase
{
    public static void TestCallHierarchyContainerName(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("function /**/f() {}\n\nclass A {\n  static sameName() {\n    f();\n  }\n}\n\nclass B {\n  sameName() {\n    A.sameName();\n  }\n}\n\nconst Obj = {\n  get sameName() {\n    return new B().sameName;\n  }\n};\n\nnamespace Foo {\n  function sameName() {\n    return Obj.sameName;\n  }\n\n  export class C {\n    constructor() {\n      sameName();\n    }\n  }\n}\n\nnamespace Foo.Bar {\n  const sameName = () => new Foo.C();\n}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.GoToMarker(t, "");
                f.VerifyBaselineCallHierarchy(t);
            }
            finally
            {
                done();
            }
        });
    }

}
