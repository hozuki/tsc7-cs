// ported from: internal/fourslash/tests/memberOverloadEdits_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class MemberOverloadEditsCase
{
    public static void TestMemberOverloadEdits(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("namespace M {\n    export class A {\n        public m(n: number) {\n            return 0;\n        }\n        public n() {\n            return this.m(0);\n        }\n    }\n    export class B extends A { /*1*/ }\n}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyNoErrors(t);
                f.GoToMarker(t, "1");
                f.Insert(t, "public m(n: number) { return 0; }");
                f.VerifyNoErrors(t);
            }
            finally
            {
                done();
            }
        });
    }

}
