// ported from: internal/fourslash/tests/spaceAfterStatementConditions_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class SpaceAfterStatementConditionsCase
{
    public static void TestSpaceAfterStatementConditions(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("let i = 0;\n\nif(i<0) ++i;\nif(i<0) --i;\n\nwhile(i<0) ++i;\nwhile(i<0) --i;\n\ndo ++i;\nwhile(i<0)\ndo --i;\nwhile(i<0)\n\nfor(let prop in { foo: 1 }) ++i;\nfor(let prop in { foo: 1 }) --i;\n\nfor(let foo of [1, 2]) ++i;\nfor(let foo of [1, 2]) --i;\n\nfor(let j = 0; j < 10; j++) ++i;\nfor(let j = 0; j < 10; j++) --i;\n"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.FormatDocument(t, "");
                f.VerifyCurrentFileContent(t, "let i = 0;\n\nif (i < 0) ++i;\nif (i < 0) --i;\n\nwhile (i < 0) ++i;\nwhile (i < 0) --i;\n\ndo ++i;\nwhile (i < 0)\ndo --i;\nwhile (i < 0)\n\nfor (let prop in { foo: 1 }) ++i;\nfor (let prop in { foo: 1 }) --i;\n\nfor (let foo of [1, 2]) ++i;\nfor (let foo of [1, 2]) --i;\n\nfor (let j = 0; j < 10; j++) ++i;\nfor (let j = 0; j < 10; j++) --i;\n");
            }
            finally
            {
                done();
            }
        });
    }

}
