// ported from: internal/fourslash/tests/organizeImports10_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using LSP = TypeScript.LsProto;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class OrganizeImports10Case
{
    public static void TestOrganizeImports10(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = "// @Filename: /module.ts\nimport type { ZodType } from './declaration';\n\n/** Intended to be used in combination with {@link ZodType} */\nexport function fun() { /* ... */ }\n// @Filename: /declaration.ts\n type ZodType = {};\n export type { ZodType }";
            var (f, done) = F.Fourslash.NewFourslash(t, null, content);
            try
            {
                f.VerifyOrganizeImports(t, "import type { ZodType } from './declaration';\n\n/** Intended to be used in combination with {@link ZodType} */\nexport function fun() { /* ... */ }", LSP.CodeActionKind.SourceOrganizeImportsTs, null);
            }
            finally
            {
                done();
            }
        });
    }

}
