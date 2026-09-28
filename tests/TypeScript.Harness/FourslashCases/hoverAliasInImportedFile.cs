// ported from: internal/fourslash/tests/hoverAliasInImportedFile_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class HoverAliasInImportedFileCase
{
    public static void TestHoverAliasInImportedFile(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("\n// @filename: other2.ts\nexport type SomeAliasType<T> = { value: T };\n\n// @filename: other.ts\nimport { SomeAliasType } from './other2';\n\ndeclare function isSomeAliasType(x: any): x is SomeAliasType<any>;\n\nexport { isSomeAliasType };\n\n// @filename: main.ts\nimport { isSomeAliasType } from './other';\n\nexport function processValue(value: any) {\n  if (/*1*/isSomeAliasType(value)) {\n    console.log(\"ok\");\n  }\n}\n"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyQuickInfoAt(t, "1", "(alias) function isSomeAliasType(x: any): x is SomeAliasType<any>", "");
            }
            finally
            {
                done();
            }
        });
    }

}
