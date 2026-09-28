// ported from: internal/fourslash/tests/chineseCharacterDisplay_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class ChineseCharacterDisplayCase
{
    public static void TestChineseCharacterDisplayInHover(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("\ninterface \u4E2D\u6587\u754C\u9762 {\n    \u4E0A\u5C45\u4E2D: string;\n    \u4E0B\u5C45\u4E2D: string;\n}\n\nclass \u4E2D\u6587\u7C7B {\n    \u83B7\u53D6\u4E2D\u6587\u5C5E\u6027(): \u4E2D\u6587\u754C\u9762 {\n        return {\n            \u4E0A\u5C45\u4E2D: \"\u4E0A\u5C45\u4E2D\",\n            \u4E0B\u5C45\u4E2D: \"\u4E0B\u5C45\u4E2D\"\n        };\n    }\n}\n\nlet /*instanceHover*/\u5B9E\u4F8B = new \u4E2D\u6587\u7C7B();\nlet \u5C5E\u6027\u5BF9\u8C61 = \u5B9E\u4F8B./*methodHover*/\u83B7\u53D6\u4E2D\u6587\u5C5E\u6027();"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyQuickInfoAt(t, "instanceHover", "let \u5b9e\u4f8b: \u4e2d\u6587\u7c7b", "");
                f.VerifyQuickInfoAt(t, "methodHover", "(method) \u4e2d\u6587\u7c7b.\u83b7\u53d6\u4e2d\u6587\u5c5e\u6027(): \u4e2d\u6587\u754c\u9762", "");
            }
            finally
            {
                done();
            }
        });
    }

    public static void TestChineseCharacterDisplayInUnionTypes(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("\n// Test the original issue: Chinese characters in method parameters should display correctly\nclass TSLine {\n    setLengthTextPositionPreset(/*methodParam*/preset: \"\u4E0A\u5C45\u4E2D\" | \"\u4E0B\u5C45\u4E2D\" | \"\u53F3\u5C45\u4E2D\" | \"\u5DE6\u5C45\u4E2D\"): void {}\n}\n\nlet lines = new TSLine();\nlines./*method*/setLengthTextPositionPreset;"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyQuickInfoAt(t, "method", "(method) TSLine.setLengthTextPositionPreset(preset: \"\u4e0a\u5c45\u4e2d\" | \"\u4e0b\u5c45\u4e2d\" | \"\u53f3\u5c45\u4e2d\" | \"\u5de6\u5c45\u4e2d\"): void", "");
            }
            finally
            {
                done();
            }
        });
    }

}
