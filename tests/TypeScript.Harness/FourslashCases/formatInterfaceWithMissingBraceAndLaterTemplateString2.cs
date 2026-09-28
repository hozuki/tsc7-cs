// ported from: internal/fourslash/tests/formatInterfaceWithMissingBraceAndLaterTemplateString2_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class FormatInterfaceWithMissingBraceAndLaterTemplateString2Case
{
    public static void TestFormatInterfaceWithMissingBraceAndLaterTemplateString2(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("\n// @Filename: /FormCheck.tsx\ninterface FormCheckProps {\n\nconst FormCheck: DynamicRefForwardingComponent<'input', FormCheckProps> =\n  React.forwardRef(\n\t    () => {\n\t      return <div className={`${bsPrefix}-reverse`} />;\n    },\n  );\n\nFormCheck.displayName = 'FormCheck';\n"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.FormatDocument(t, "");
                f.VerifyCurrentFileContent(t, "interface FormCheckProps {\n\nconst FormCheck: DynamicRefForwardingComponent<'input', FormCheckProps> =\n    React.forwardRef(\n        () => {\n            return <div className={" + "`" + "${bsPrefix}-reverse" + "`" + "} />;\n        },\n    );\n\nFormCheck.displayName = 'FormCheck';\n");
            }
            finally
            {
                done();
            }
        });
    }

}
