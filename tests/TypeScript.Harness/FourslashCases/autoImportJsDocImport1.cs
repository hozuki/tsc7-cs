// ported from: internal/fourslash/tests/autoImportJsDocImport1_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class AutoImportJsDocImport1Case
{
    public static void TestAutoImportJsDocImport1(TSU.IHarnessTest t)
    {
        t.Skip("Known failing fourslash test");
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @verbatimModuleSyntax: true\n// @target: esnext\n// @allowJs: true\n// @checkJs: true\n// @Filename: /foo.ts\n export const A = 1;\n export type B = { x: number };\n export type C = 1;\n export class D { y: string }\n// @Filename: /test.js\n/**\n * @import { A, D, C } from \"./foo\"\n */\n\n/**\n * @param { typeof A } a\n * @param { B/**/ | C } b\n * @param { C } c\n * @param { D } d\n */\nexport function f(a, b, c, d) { }"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.GoToMarker(t, "");
                f.VerifyImportFixAtPosition(t, ["/**\n * @import { A, D, C, B } from \"./foo\"\n */\n\n/**\n * @param { typeof A } a\n * @param { B | C } b\n * @param { C } c\n * @param { D } d\n */\nexport function f(a, b, c, d) { }"], null!);
            }
            finally
            {
                done();
            }
        });
    }

}
