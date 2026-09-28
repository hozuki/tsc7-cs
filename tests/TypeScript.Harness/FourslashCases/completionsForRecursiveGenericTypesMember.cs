// ported from: internal/fourslash/tests/completionsForRecursiveGenericTypesMember_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;
using U = TypeScript.Harness.FourslashCases.FourslashTestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class CompletionsForRecursiveGenericTypesMemberCase
{
    public static void TestCompletionsForRecursiveGenericTypesMember(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("export class TestBase<T extends TestBase<T>>\n{\n    public publicMethod(p: any): void {}\n    private privateMethod(p: any): void {}\n    protected protectedMethod(p: any): void {}\n    public test(t: T): void\n    {\n        t./**/\n    }\n}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyCompletions(t, "", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Exact = ["privateMethod", "protectedMethod", "publicMethod", "test"] } });
            }
            finally
            {
                done();
            }
        });
    }

}
