// ported from: internal/fourslash/tests/issue57429_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using LSP = TypeScript.LsProto;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class Issue57429Case
{
    public static void TestIssue57429(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @strict: true\nfunction Builder<I>(def: I) {\n  return def;\n}\n\ninterface IThing {\n  doThing: (args: { value: object }) => string\n  doAnotherThing: () => void\n}\n\nBuilder<IThing>({\n  doThing(args: { value: object }) {\n    const { v/*1*/alue } = this.[|args|]\n    return `${value}`\n  },\n  doAnotherThing() { },\n})"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyQuickInfoAt(t, "1", "const value: any", "");
                f.VerifyNonSuggestionDiagnostics(t, [new LSP.Diagnostic { Message = new LSP.StringOrMarkupContent { String = "Property 'args' does not exist on type 'IThing'." }, Code = new LSP.IntegerOrString { Integer = (int)(2339) } }]);
            }
            finally
            {
                done();
            }
        });
    }

}
