// ported from: internal/fourslash/tests/completionClassMemberSnippetCrossFileNodeReuse1_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using LSP = TypeScript.LsProto;
using LSUP = TypeScript.LsUtil;
using TRI = TypeScript.Core.Tristate;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class CompletionClassMemberSnippetCrossFileNodeReuse1Case
{
    public static void TestCompletionClassMemberSnippetCrossFileNodeReuse1(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @strict: true\n// @filename: KlassConstructor.ts\ntype GenericConstructor<T> = new (...args: any[]) => T;\nexport type KlassConstructor<Cls extends GenericConstructor<any>> =\n  GenericConstructor<InstanceType<Cls>> & { [k in keyof Cls]: Cls[k] };\n// @filename: ElementNode.ts\nimport { KlassConstructor } from \"./KlassConstructor\";\n\nexport type NodeKey = string;\n\nexport class ElementNode {\n  [\"constructor\"]!: KlassConstructor<typeof ElementNode>;\n}\n// @filename: CollapsibleContainerNode.ts\nimport { ElementNode, NodeKey } from \"./ElementNode\";\n\nexport class CollapsibleContainerNode extends ElementNode {\n  __open: boolean;\n\n  /*1*/\n}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                var opts786 = f.GetOptions();
                opts786!.FormatCodeSettings!.InsertSpaceAfterConstructor = TRI.TSFalse;
                f.Configure(t, opts786);
                f.VerifyCompletions(t, "1", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = [], EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Includes = [new LSP.CompletionItem { Label = "[\"constructor\"]", InsertText = "[\"constructor\"]: KlassConstructor<typeof ElementNode>;", FilterText = "[\"constructor\"]", AdditionalTextEdits = F.FourslashTest.AnyTextEdits, Data = new LSP.CompletionItemData { Source = "ClassMemberSnippet/" } }] }, UserPreferences = new LSUP.UserPreferences { IncludeCompletionsWithClassMemberSnippets = TRI.TSTrue } });
            }
            finally
            {
                done();
            }
        });
    }

}
