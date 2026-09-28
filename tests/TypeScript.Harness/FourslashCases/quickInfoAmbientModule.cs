// ported from: internal/fourslash/tests/quickInfoAmbientModule_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class QuickInfoAmbientModuleCase
{
    public static void TestQuickInfoAmbientModule(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("declare module \"*.css\"/*1*/;"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyQuickInfoAt(t, "1", "module \"*.css\"", "");
            }
            finally
            {
                done();
            }
        });
    }

    public static void TestQuickInfoPatternAmbientModuleWithImportAttributes(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("declare module \"*.css\"/*1*/ with { type: \"css\" } {\n    const styles: { readonly [className: string]: string };\n    export default styles;\n}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyBaselineHoverWithVerbosity(t, new Dictionary<string, int[]> { ["1"] = [0, 1] });
            }
            finally
            {
                done();
            }
        });
    }

    public static void TestQuickInfoMergedPatternAmbientModuleWithImportAttributes(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @Filename: /first.d.ts\ndeclare module \"*.asset\"/*css*/ with { type: \"css\" } {\n    export const cssOnly: \"css\";\n}\ndeclare module \"*.asset\"/*text*/ with { type: \"text\" } {\n    export const textOnly: \"text\";\n}\n\n// @Filename: /second.d.ts\ndeclare module \"*.asset\" with { type: \"css\" } {\n    export const cssAlso: \"css-also\";\n}\ndeclare module \"*.asset\" with { type: \"text\" } {\n    export const textAlso: \"text-also\";\n}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyBaselineHoverWithVerbosity(t, new Dictionary<string, int[]> { ["css"] = [0, 1], ["text"] = [0, 1] });
            }
            finally
            {
                done();
            }
        });
    }

}
