// ported from: internal/fourslash/tests/importNameCodeFix_noDestructureNonObjectLiteral_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class ImportNameCodeFix_noDestructureNonObjectLiteralCase
{
    public static void TestImportNameCodeFix_noDestructureNonObjectLiteral(TSU.IHarnessTest t)
    {
        t.Skip("Known failing fourslash test");
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @lib: es5\n// @target: es2015\n// @strict: true\n// @esModuleInterop: true\n// @Filename: /array.ts\ndeclare const arr: number[];\nexport = arr;\n// @Filename: /class-instance-member.ts\nclass C { filter() {} }\nexport = new C();\n// @Filename: /object-literal.ts\ndeclare function filter(): void;\nexport = { filter };\n// @Filename: /jquery.d.ts\ninterface JQueryStatic {\n  filter(): void;\n}\ndeclare const $: JQueryStatic;\nexport = $;\n// @Filename: /jquery.js\nmodule.exports = {};\n// @Filename: /index.ts\nfilter/**/"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyImportFixModuleSpecifiers(t, "", ["./object-literal", "./jquery"], null!);
            }
            finally
            {
                done();
            }
        });
    }

}
