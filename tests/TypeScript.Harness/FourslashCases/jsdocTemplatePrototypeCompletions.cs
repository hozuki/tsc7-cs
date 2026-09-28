// ported from: internal/fourslash/tests/jsdocTemplatePrototypeCompletions_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;
using U = TypeScript.Harness.FourslashCases.FourslashTestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class JsdocTemplatePrototypeCompletionsCase
{
    public static void TestJsdocTemplatePrototypeCompletions(TSU.IHarnessTest t)
    {
        t.Skip("Known failing fourslash test");
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @checkJs: true\n// @filename: index.js\nhttps://github.com/microsoft/TypeScript/issues/11492\n/** @constructor */\nfunction Foo() {}\n/**\n * @template T\n * @param {T} bar\n * @returns {T}\n */\nFoo.prototype.foo = function (bar) {};\nnew Foo().foo({ id: 1234 })./**/"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyCompletions(t, "", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Exact = ["id"] } });
            }
            finally
            {
                done();
            }
        });
    }

}
