// ported from: internal/fourslash/tests/updateToClassStatics_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class UpdateToClassStaticsCase
{
    public static void TestUpdateToClassStatics(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("namespace TypeScript {\n    export class PullSymbol {}\n    export class Diagnostic {}\n    export class SymbolAndDiagnostics<TSymbol extends PullSymbol> {\n        constructor(public symbol: TSymbol,\n            public diagnostics: Diagnostic) {\n        }\n        /**/\n        public static create<TSymbol extends PullSymbol>(symbol: TSymbol, diagnostics: Diagnostic): SymbolAndDiagnostics<TSymbol> {\n            return new SymbolAndDiagnostics<TSymbol>(symbol, diagnostics);\n        }\n    }\n}\nnamespace TypeScript {\n    var x : TypeScript.SymbolAndDiagnostics;\n}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.GoToMarker(t, "");
                f.Insert(t, "someNewProperty = 0;");
            }
            finally
            {
                done();
            }
        });
    }

}
