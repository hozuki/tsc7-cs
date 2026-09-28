// ported from: internal/fourslash/tests/typeReferenceAndImportDeprecated_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using LSP = TypeScript.LsProto;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class TypeReferenceAndImportDeprecatedCase
{
    public static void TestTypeReferenceAndImportDeprecated(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @filename: types.ts\n/** @deprecated */\nexport type SelectorMap<T extends Record<string, (...params: unknown[]) => unknown>> = {\n    [key in keyof T]: T[key];\n};\n// @filename: index.ts\n/** @deprecated */\nexport type SelectorMap<T extends Record<string, (...params: unknown[]) => unknown>> = {\n    [key in keyof T]: T[key];\n};\n\nexport declare const value2: {\n    sliceSelectors: <FuncMap extends [|import('./types').SelectorMap<FuncMap>|]>(selectorsBySlice: FuncMap) => { [P in keyof FuncMap]: Parameters<FuncMap[P]> };\n};\n\nexport declare const value3: {\n    sliceSelectors: <FuncMap extends [|SelectorMap<FuncMap>|]>(selectorsBySlice: FuncMap) => { [P in keyof FuncMap]: Parameters<FuncMap[P]> };\n};"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.GoToFile(t, "index.ts");
                f.VerifySuggestionDiagnostics(t, [new LSP.Diagnostic { Code = new LSP.IntegerOrString { Integer = (int)(6385) }, Message = new LSP.StringOrMarkupContent { String = "'SelectorMap' is deprecated." }, Tags = [LSP.DiagnosticTag.Deprecated], Range = f.Ranges()[0].LSRange }, new LSP.Diagnostic { Code = new LSP.IntegerOrString { Integer = (int)(6385) }, Message = new LSP.StringOrMarkupContent { String = "'SelectorMap' is deprecated." }, Tags = [LSP.DiagnosticTag.Deprecated], Range = f.Ranges()[1].LSRange }]);
            }
            finally
            {
                done();
            }
        });
    }

}
