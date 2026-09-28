// ported from: internal/fourslash/tests/completionsWrappedClass_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;
using U = TypeScript.Harness.FourslashCases.FourslashTestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class CompletionsWrappedClassCase
{
    public static void TestCompletionsWrappedClass(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("class Client {\n    private close() { }\n    public open() { }\n}\ntype Wrap<T> = T &\n{\n    [K in Extract<keyof T, string> as `${K}Wrapped`]: T[K];\n};\nclass Service {\n    method() {\n        let service = undefined as unknown as Wrap<Client>;\n        const { /*a*/ } = service;\n    }\n}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyCompletions(t, "a", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Exact = ["open", "openWrapped"] } });
            }
            finally
            {
                done();
            }
        });
    }

}
