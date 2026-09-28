// ported from: internal/fourslash/tests/formatInterfaceWithMissingBraceAndLaterTemplateString1_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class FormatInterfaceWithMissingBraceAndLaterTemplateString1Case
{
    public static void TestFormatInterfaceWithMissingBraceAndLaterTemplateString1(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("\n// @Filename: /resource-card.tsx\ninterface Props {\n  iconOnly?: boolean\n\n\nconst ResourceCard: React.FC<Props> = (props) => {\n  return (\n    <IoLayersOutline\n      className={`${match ? 'text-primary-foreground' : 'text-foreground'}`}\n    />\n  )\n}\n\nexport default ResourceCard\n"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.FormatDocument(t, "");
                f.VerifyCurrentFileContent(t, "interface Props {\n    iconOnly?: boolean\n\n\nconst ResourceCard: React.FC<Props> = (props) => {\n    return (\n        <IoLayersOutline\n            className={" + "`" + "${match ? 'text-primary-foreground' : 'text-foreground'}" + "`" + "}\n        />\n    )\n}\n\nexport default ResourceCard\n");
            }
            finally
            {
                done();
            }
        });
    }

}
