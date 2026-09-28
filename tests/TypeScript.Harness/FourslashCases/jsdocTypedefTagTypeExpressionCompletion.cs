// ported from: internal/fourslash/tests/jsdocTypedefTagTypeExpressionCompletion_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using LS = TypeScript.Ls.Ls;
using LSP = TypeScript.LsProto;
using TSU = TypeScript.TestUtil;
using U = TypeScript.Harness.FourslashCases.FourslashTestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class JsdocTypedefTagTypeExpressionCompletionCase
{
    public static void TestJsdocTypedefTagTypeExpressionCompletion(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @lib: es5\ninterface I {\n    age: number;\n}\n class Foo {\n     property1: string;\n     constructor(value: number) { this.property1 = \"hello\"; }\n     static method1() {}\n     method3(): number { return 3; }\n     /**\n      * @param {string} foo A value.\n      * @returns {number} Another value\n      * @mytag\n      */\n     method4(foo: string) { return 3; }\n }\n namespace Foo.Namespace { export interface SomeType { age2: number } }\n /**\n  * @type { /*type1*/Foo./*typeFooMember*/Namespace./*NamespaceMember*/SomeType }\n  */\nvar x;\n/*globalValue*/\nx./*valueMemberOfSomeType*/\nvar x1: Foo;\nx1./*valueMemberOfFooInstance*/;\nFoo./*valueMemberOfFoo*/;\n /**\n  * @type { {/*propertyName*/ageX: number} }\n  */\nvar y;"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyCompletions(t, "type1", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Includes = [new LSP.CompletionItem { Label = "Foo", Kind = LSP.CompletionItemKind.Class }, new LSP.CompletionItem { Label = "I", Kind = LSP.CompletionItemKind.Interface }], Excludes = ["Namespace", "SomeType", "x", "x1", "y", "method1", "property1", "method3", "method4", "foo"] } });
                f.VerifyCompletions(t, "typeFooMember", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Exact = [new LSP.CompletionItem { Label = "Namespace", Kind = LSP.CompletionItemKind.Module }] } });
                f.VerifyCompletions(t, "NamespaceMember", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Exact = [new LSP.CompletionItem { Label = "SomeType", Kind = LSP.CompletionItemKind.Interface }] } });
                f.VerifyCompletions(t, "globalValue", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Includes = [new LSP.CompletionItem { Label = "Foo", Kind = LSP.CompletionItemKind.Class }, new LSP.CompletionItem { Label = "x", Kind = LSP.CompletionItemKind.Variable }, new LSP.CompletionItem { Label = "x1", Kind = LSP.CompletionItemKind.Variable }, new LSP.CompletionItem { Label = "y", Kind = LSP.CompletionItemKind.Variable }], Excludes = ["I", "Namespace", "SomeType", "method1", "property1", "method3", "method4", "foo"] } });
                f.VerifyCompletions(t, "valueMemberOfSomeType", null!);
                f.VerifyCompletions(t, "valueMemberOfFooInstance", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Exact = [new LSP.CompletionItem { Label = "method3", Kind = LSP.CompletionItemKind.Method }, new LSP.CompletionItem { Label = "method4", Kind = LSP.CompletionItemKind.Method }, new LSP.CompletionItem { Label = "property1", Kind = LSP.CompletionItemKind.Field }] } });
                f.VerifyCompletions(t, "valueMemberOfFoo", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Exact = U.CompletionFunctionMembersPlus([new LSP.CompletionItem { Label = "method1", Kind = LSP.CompletionItemKind.Method, SortText = LS.SortTextLocalDeclarationPriority }, new LSP.CompletionItem { Label = "prototype", SortText = LS.SortTextLocationPriority }]) } });
                f.VerifyCompletions(t, "propertyName", null!);
            }
            finally
            {
                done();
            }
        });
    }

}
