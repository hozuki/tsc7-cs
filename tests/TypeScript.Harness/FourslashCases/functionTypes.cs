// ported from: internal/fourslash/tests/functionTypes_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;
using U = TypeScript.Harness.FourslashCases.FourslashTestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class FunctionTypesCase
{
    public static void TestFunctionTypes(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @lib: es5\n// @strict: false\nvar f: Function;\nfunction g() { }\n\nclass C {\n    h: () => void ;\n    i(): number { return 5; }\n    static j = (e) => e;\n    static k() { return 'hi';}\n}\nvar l = () => void 0;\nvar z = new C;\n\nf./*1*/apply(this, [1]);\ng./*2*/arguments;\nz.h./*3*/bind(undefined, 1, 2);\nz.i./*4*/call(null)\nC.j./*5*/length === 1;\ntypeof C.k./*6*/caller === 'function';\nl./*7*/prototype = Object.prototype;"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyNoErrors(t);
                f.VerifyCompletions(t, new F.MarkerInput { MarkerNames = ["1", "2", "3", "4", "5", "6"] }, new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Exact = U.CompletionFunctionMembersWithPrototype } });
                f.VerifyCompletions(t, "7", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Exact = U.CompletionFunctionMembersPlus(["prototype"]) } });
            }
            finally
            {
                done();
            }
        });
    }

}
