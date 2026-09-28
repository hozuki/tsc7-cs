// ported from: internal/fourslash/tests/cloduleAsBaseClass2_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using LS = TypeScript.Ls.Ls;
using LSP = TypeScript.LsProto;
using TSU = TypeScript.TestUtil;
using U = TypeScript.Harness.FourslashCases.FourslashTestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class CloduleAsBaseClass2Case
{
    public static void TestCloduleAsBaseClass2(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @module: commonjs\n// @strict: false\n// @Filename: cloduleAsBaseClass2_0.ts\nclass A {\n    constructor(x: number) { }\n    foo() { }\n    static bar() { }\n}\n\nnamespace A {\n    export var x = 1;\n    export function baz() { }\n}\n\nexport = A;\n// @Filename: cloduleAsBaseClass2_1.ts\nimport B = require('./cloduleAsBaseClass2_0');\nclass D extends B {\n    constructor() {\n        super(1);\n    }\n    foo2() { }\n    static bar2() { }\n}\n\nvar d: D;\nd./*1*/\nD./*2*/"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyCompletions(t, "1", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Exact = ["foo", "foo2"] } });
                f.Insert(t, "foo()");
                f.VerifyCompletions(t, "2", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Includes = [new LSP.CompletionItem { Label = "bar", SortText = LS.SortTextLocalDeclarationPriority }, new LSP.CompletionItem { Label = "bar2", SortText = LS.SortTextLocalDeclarationPriority }, new LSP.CompletionItem { Label = "baz", SortText = LS.SortTextLocationPriority }, new LSP.CompletionItem { Label = "x", SortText = LS.SortTextLocationPriority }], Excludes = ["foo", "foo2"] } });
                f.Insert(t, "bar()");
                f.VerifyNoErrors(t);
            }
            finally
            {
                done();
            }
        });
    }

}
