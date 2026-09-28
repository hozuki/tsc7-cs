// ported from: internal/fourslash/tests/multiModuleFundule_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;
using U = TypeScript.Harness.FourslashCases.FourslashTestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class MultiModuleFunduleCase
{
    public static void TestMultiModuleFundule(TSU.IHarnessTest t)
    {
        t.Skip("Known failing fourslash test");
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @strict: false\nfunction C(x: number) { }\n\nnamespace C {\n    export var x = 1;\n}\nnamespace C {\n    export function foo() { }\n}\n\nvar /*2*/r = C(/*1*/\nvar /*4*/r2 = new C(/*3*/ // using void returning function as constructor\nvar r3 = C./*5*/"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyCompletions(t, "1", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = [], EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Includes = ["C"] } });
                f.Insert(t, "C.x);");
                f.VerifyQuickInfoAt(t, "2", "var r: void", "");
                f.VerifyCompletions(t, "3", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = [], EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Includes = ["C"] } });
                f.Insert(t, "C.x);");
                f.VerifyQuickInfoAt(t, "4", "var r2: any", "");
                f.VerifyCompletions(t, "5", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Includes = ["x", "foo"] } });
                f.Insert(t, "x;");
                f.VerifyNoErrors(t);
            }
            finally
            {
                done();
            }
        });
    }

}
