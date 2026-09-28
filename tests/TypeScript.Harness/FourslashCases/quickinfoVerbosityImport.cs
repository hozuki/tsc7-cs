// ported from: internal/fourslash/tests/quickinfoVerbosityImport_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class QuickinfoVerbosityImportCase
{
    public static void TestQuickinfoVerbosityImport(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @module: esnext\n// @filename: /0.ts\nexport type Apple = {\n    a: number;\n    b: string;\n}\nexport const a: Apple = { a: 1, b: \"2\"};\nexport enum Color {\n    Red,\n    Green,\n    Blue,\n}\n// @filename: /1.ts\nimport * as zero from \"./0\";\nconst b/*b*/ = zero;\n// @filename: /2.ts\nimport { a/*a*/ } from \"./0\";\nimport { Color/*c*/ } from \"./0\";\n// @filename: /3.ts\nexport default class {\n    a: boolean;\n}\n// @filename: /4.ts\nimport Foo/*d*/ from \"./3\";\nconst f/*e*/ = new Foo/*f*/();"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyBaselineHoverWithVerbosity(t, new Dictionary<string, int[]> { ["b"] = [0, 1, 2], ["a"] = [0, 1], ["c"] = [0, 1], ["d"] = [0], ["e"] = [0, 1], ["f"] = [0, 1] });
            }
            finally
            {
                done();
            }
        });
    }

}
