// ported from: internal/fourslash/tests/formatMultilineTypesWithMapped_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class FormatMultilineTypesWithMappedCase
{
    public static void TestFormatMultilineTypesWithMapped(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("type Z = 'z'\ntype A = {\n  a: 'a'\n} | {\n      [index in Z]: string\n  }\ntype B = {\n  b: 'b'\n} & {\n      [index in Z]: string\n  }\n\nconst c = {\n  c: 'c'\n} as const satisfies {\n    [index in Z]: string\n  }\n\nconst d = {\n  d: 'd'\n} as const satisfies {\n  [index: string]: string\n}\n\nconst e = {\n  e: 'e'\n} satisfies {\n    [index in Z]: string\n  }\n\nconst f = {\n  f: 'f'\n} satisfies {\n  [index: string]: string\n}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.FormatDocument(t, "");
                f.VerifyCurrentFileContent(t, "type Z = 'z'\ntype A = {\n    a: 'a'\n} | {\n    [index in Z]: string\n}\ntype B = {\n    b: 'b'\n} & {\n    [index in Z]: string\n}\n\nconst c = {\n    c: 'c'\n} as const satisfies {\n    [index in Z]: string\n}\n\nconst d = {\n    d: 'd'\n} as const satisfies {\n    [index: string]: string\n}\n\nconst e = {\n    e: 'e'\n} satisfies {\n    [index in Z]: string\n}\n\nconst f = {\n    f: 'f'\n} satisfies {\n    [index: string]: string\n}");
            }
            finally
            {
                done();
            }
        });
    }

}
