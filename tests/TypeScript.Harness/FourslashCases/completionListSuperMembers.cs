// ported from: internal/fourslash/tests/completionListSuperMembers_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;
using U = TypeScript.Harness.FourslashCases.FourslashTestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class CompletionListSuperMembersCase
{
    public static void TestCompletionListSuperMembers(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("class Base {\n    private privateInstanceMethod() { }\n    public publicInstanceMethod() { }\n\n    private privateProperty = 1;\n    public publicProperty = 1;\n\n    private static privateStaticProperty = 1;\n    public static publicStaticProperty = 1;\n\n    private static privateStaticMethod() { }\n    public static publicStaticMethod() {\n        Class./*staticsInsideClassScope*/publicStaticMethod();\n        var c = new Class();\n        c./*instanceMembersInsideClassScope*/privateProperty;\n    }\n}\nclass Class extends Base {\n    private test() {\n        super./**/\n    }\n}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyCompletions(t, "", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Includes = ["publicInstanceMethod"], Excludes = ["publicProperty", "publicStaticProperty", "publicStaticMethod", "privateProperty", "privateInstanceMethod"] } });
            }
            finally
            {
                done();
            }
        });
    }

}
