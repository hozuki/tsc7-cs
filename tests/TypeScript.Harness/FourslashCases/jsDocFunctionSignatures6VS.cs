// ported from: internal/fourslash/tests/jsDocFunctionSignatures6VS_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using LSP = TypeScript.LsProto;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class JsDocFunctionSignatures6VSCase
{
    public static void TestJsDocFunctionSignatures6VS(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @allowJs: true\n// @Filename: Foo.js\n/**\n * @param {string} p1 - A string param\n * @param {string?} p2 - An optional param\n * @param {string} [p3] - Another optional param\n * @param {string} [p4=\"test\"] - An optional param with a default value\n */\nfunction f1(p1, p2, p3, p4){}\nf1(/*1*/'foo', /*2*/'bar', /*3*/'baz', /*4*/'qux');"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, new LSP.ClientCapabilities { VSSupportsVisualStudioExtensions = true }, content);
            try
            {
                f.VerifyBaselineSignatureHelp(t);
            }
            finally
            {
                done();
            }
        });
    }

}
