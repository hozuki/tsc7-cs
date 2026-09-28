// ported from: internal/fourslash/tests/importTypeMemberCompletions_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;
using U = TypeScript.Harness.FourslashCases.FourslashTestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class ImportTypeMemberCompletionsCase
{
    public static void TestImportTypeMemberCompletions(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @Filename: /ns.ts\nexport namespace Foo {\n    export namespace Bar {\n        export class Baz {}\n        export interface Bat {}\n        export const a: number;\n        const b: string;\n    }\n}\n// @Filename: /top.ts\nexport interface Bat {}\nexport const a: number;\n// @Filename: /equals.ts\nclass Foo {\n public static bar: string;\n private static baz: number;\n}\nexport = Foo;\n// @Filename: /usage1.ts\ntype A = typeof import(\"./ns\")./*1*/\n// @Filename: /usage2.ts\ntype B = typeof import(\"./ns\").Foo./*2*/\n// @Filename: /usage3.ts\ntype C = typeof import(\"./ns\").Foo.Bar./*3*/\n// @Filename: /usage4.ts\ntype D = import(\"./ns\")./*4*/\n// @Filename: /usage5.ts\ntype E = import(\"./ns\").Foo./*5*/\n// @Filename: /usage6.ts\ntype F = import(\"./ns\").Foo.Bar./*6*/\n// @Filename: /usage7.ts\ntype G = typeof import(\"./top\")./*7*/\n// @Filename: /usage8.ts\ntype H = import(\"./top\")./*8*/\n// @Filename: /usage9.ts\ntype H = typeof import(\"./equals\")./*9*/"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyCompletions(t, "1", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Exact = ["Foo"] } });
                f.VerifyCompletions(t, "2", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Exact = ["Bar"] } });
                f.VerifyCompletions(t, "3", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Exact = ["a", "Baz"] } });
                f.VerifyCompletions(t, "4", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Exact = ["Foo"] } });
                f.VerifyCompletions(t, "5", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Exact = ["Bar"] } });
                f.VerifyCompletions(t, "6", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Exact = ["Bat", "Baz"] } });
                f.VerifyCompletions(t, "7", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Exact = ["a"] } });
                f.VerifyCompletions(t, "8", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Exact = ["Bat"] } });
                f.VerifyCompletions(t, "9", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Exact = ["bar", "prototype"] } });
            }
            finally
            {
                done();
            }
        });
    }

}
