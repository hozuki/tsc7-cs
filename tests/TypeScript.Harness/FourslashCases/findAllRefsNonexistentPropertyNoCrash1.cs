// ported from: internal/fourslash/tests/findAllRefsNonexistentPropertyNoCrash1_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class FindAllRefsNonexistentPropertyNoCrash1Case
{
    public static void TestFindAllRefsNonexistentPropertyNoCrash1(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @strict: true\n// @allowJs: true\n// @checkJs: true\n// @filename: ./src/parser-input.js\nexport default () => {\n  let input;\n\n  const parserInput = {};\n\n  parserInput.currentChar = () => input.charAt(parserInput.i);\n\n  parserInput.end = () => {\n    const isFinished = parserInput.i >= input.length;\n\n    return {\n      isFinished,\n      furthest: parserInput.i,\n    };\n  };\n\n  return parserInput;\n};\n// @filename: ./src/parser.js\nimport getParserInput from \"./parser-input\";\n\nconst Parser = function Parser(context, imports, fileInfo, currentIndex) {\n  currentIndex = currentIndex || 0;\n  let parsers;\n  const parserInput = getParserInput();\n\n  return {\n    parserInput,\n    parsers: (parsers = {\n      variable: function () {\n        let name;\n\n        if (parserInput.currentChar() === \"/*1*/@\") {\n          return name[1];\n        }\n      },\n    }),\n  };\n};\n\nexport default Parser;"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyBaselineFindAllReferences(t, "1");
            }
            finally
            {
                done();
            }
        });
    }

}
