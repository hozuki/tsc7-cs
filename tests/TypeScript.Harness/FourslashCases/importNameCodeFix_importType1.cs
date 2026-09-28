// ported from: internal/fourslash/tests/importNameCodeFix_importType1_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class ImportNameCodeFix_importType1Case
{
    public static void TestImportNameCodeFix_importType1(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @verbatimModuleSyntax: true\n// @module: es2015\n// @Filename: /exports.ts\nexport default someValue = 0;\nexport function Component() {}\nexport interface ComponentProps {}\n// @Filename: /a.ts\nimport { Component } from \"./exports.js\";\ninterface MoreProps extends /*a*/ComponentProps {}\n// @Filename: /b.ts\nimport someValue from \"./exports.js\";\ninterface MoreProps extends /*b*/ComponentProps {}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.GoToMarker(t, "a");
                f.VerifyImportFixAtPosition(t, ["import { Component, type ComponentProps } from \"./exports.js\";\ninterface MoreProps extends ComponentProps {}"], null!);
                f.GoToMarker(t, "b");
                f.VerifyImportFixAtPosition(t, ["import someValue, { type ComponentProps } from \"./exports.js\";\ninterface MoreProps extends ComponentProps {}"], null!);
            }
            finally
            {
                done();
            }
        });
    }

}
