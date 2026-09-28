// ported from: internal/fourslash/tests/completionsTypeOnlyNamespace_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using LSP = TypeScript.LsProto;
using TSU = TypeScript.TestUtil;
using U = TypeScript.Harness.FourslashCases.FourslashTestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class CompletionsTypeOnlyNamespaceCase
{
    public static void TestCompletionsTypeOnlyNamespace(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @Filename: /a.ts\nexport namespace ns {\n  export class Box<T> {}\n  export type Type = {};\n  export const Value = {};\n}\n// @Filename: /b.ts\nimport type { ns } from './a';\nlet x: ns./**/"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyCompletions(t, "", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Exact = [new LSP.CompletionItem { Label = "Box", Detail = "class ns.Box<T>" }, new LSP.CompletionItem { Label = "Type", Detail = "type ns.Type = {}" }] } });
            }
            finally
            {
                done();
            }
        });
    }

}
