// ported from: internal/fourslash/tests/findAllRefsParameterPropertyWithConflictingMember_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class FindAllRefsParameterPropertyWithConflictingMemberCase
{
    public static void TestFindAllRefsParameterPropertyWithConflictingMember(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("\n// @filename: c1.ts\nclass C1 {\n  [|x|]() {}\n  constructor(public [|x|]: number) {\n    [|x|]++;\n  }\n}\nnew C1(1).[|x|];\n\n// @filename: c2.ts\ninterface C2 {\n  get [|x|](): void\n}\nclass C2 {\n  constructor(public [|x|]: number) {\n    [|x|]++;\n  }\n}\nnew C2(1).[|x|];\n"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyBaselineFindAllReferences(t);
            }
            finally
            {
                done();
            }
        });
    }

}
