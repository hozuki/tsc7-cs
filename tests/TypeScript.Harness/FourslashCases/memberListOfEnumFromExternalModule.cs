// ported from: internal/fourslash/tests/memberListOfEnumFromExternalModule_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;
using U = TypeScript.Harness.FourslashCases.FourslashTestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class MemberListOfEnumFromExternalModuleCase
{
    public static void TestMemberListOfEnumFromExternalModule(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @Filename: memberListOfEnumFromExternalModule_file0.ts\nexport enum Topic{ One, Two }\nvar topic = Topic.One;\n// @Filename: memberListOfEnumFromExternalModule_file1.ts\nimport t = require('./memberListOfEnumFromExternalModule_file0');\nvar topic = t.Topic./*1*/"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyCompletions(t, "1", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Exact = ["One", "Two"] } });
            }
            finally
            {
                done();
            }
        });
    }

}
