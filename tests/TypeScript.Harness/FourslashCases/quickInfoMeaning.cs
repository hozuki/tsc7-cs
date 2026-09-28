// ported from: internal/fourslash/tests/quickInfoMeaning_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using LSP = TypeScript.LsProto;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class QuickInfoMeaningCase
{
    public static void TestQuickInfoMeaning(TSU.IHarnessTest t)
    {
        t.Skip("Known failing fourslash test");
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @lib: es5\n// @module: commonjs\n// @Filename: foo.d.ts\ndeclare const [|/*foo_value_declaration*/foo: number|];\n[|declare module \"foo_module\" {\n    interface /*foo_type_declaration*/I { x: number; y: number }\n    export = I;\n}|]\n// @Filename: foo_user.ts\n///<reference path=\"foo.d.ts\" />\n[|import foo = require(\"foo_module\");|]\nconst x = foo/*foo_value*/;\nconst i: foo/*foo_type*/ = { x: 1, y: 2 };\n// @Filename: bar.d.ts\n[|declare interface /*bar_type_declaration*/bar { x: number; y: number }|]\n[|declare module \"bar_module\" {\n    const /*bar_value_declaration*/x: number;\n    export = x;\n}|]\n// @Filename: bar_user.ts\n///<reference path=\"bar.d.ts\" />\n[|import bar = require(\"bar_module\");|]\nconst x = bar/*bar_value*/;\nconst i: bar/*bar_type*/ = { x: 1, y: 2 };"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyNoErrors(t);
                f.VerifyWorkspaceSymbol(t, [new F.VerifyWorkspaceSymbolCase { Pattern = "foo", Preferences = null!, Exact = [new LSP.SymbolInformation { Name = "foo", Kind = LSP.SymbolKind.Variable, Location = f.Ranges()[0].LSLocation() }, new LSP.SymbolInformation { Name = "foo", Kind = LSP.SymbolKind.Variable, Location = f.Ranges()[2].LSLocation() }, new LSP.SymbolInformation { Name = "foo_module", Kind = LSP.SymbolKind.Namespace, Location = f.Ranges()[1].LSLocation() }] }]);
                f.GoToMarker(t, "foo_value");
                f.VerifyQuickInfoIs(t, "const foo: number", "");
                f.GoToMarker(t, "foo_type");
                f.VerifyQuickInfoIs(t, "(alias) interface foo\nimport foo = require(\"foo_module\")", "");
                f.VerifyWorkspaceSymbol(t, [new F.VerifyWorkspaceSymbolCase { Pattern = "bar", Preferences = null!, Exact = [new LSP.SymbolInformation { Name = "bar", Kind = LSP.SymbolKind.Interface, Location = f.Ranges()[3].LSLocation() }, new LSP.SymbolInformation { Name = "bar", Kind = LSP.SymbolKind.Variable, Location = f.Ranges()[5].LSLocation() }, new LSP.SymbolInformation { Name = "bar_module", Kind = LSP.SymbolKind.Namespace, Location = f.Ranges()[4].LSLocation() }] }]);
                f.GoToMarker(t, "bar_value");
                f.VerifyQuickInfoIs(t, "(alias) const bar: number\nimport bar = require(\"bar_module\")", "");
                f.GoToMarker(t, "bar_type");
                f.VerifyQuickInfoIs(t, "interface bar", "");
                f.VerifyBaselineGoToDefinition(t, false, "foo_value", "foo_type", "bar_value", "bar_type");
            }
            finally
            {
                done();
            }
        });
    }

}
