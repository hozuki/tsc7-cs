// ported from: internal/fourslash/tests/autoCloseTag_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class AutoCloseTagCase
{
    public static void TestAutoCloseTag(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @noLib: true\n\n// @Filename: /0.tsx\nconst x = <div>/*0*/;\n\n// @Filename: /1.tsx\nconst x = <div> foo/*1*/ </div>;\n\n// @Filename: /2.tsx\nconst x = <div></div>/*2*/;\n\n// @Filename: /3.tsx\nconst x = <div/>/*3*/;\n\n// @Filename: /4.tsx\nconst x = <div>\n    <p>/*4*/\n    </div>\n</p>;\n\n// @Filename: /5.tsx\nconst x = <div> text /*5*/;\n\n// @Filename: /6.tsx\nconst x = <div>\n    <div>/*6*/\n</div>;\n\n// @Filename: /7.tsx\nconst x = <div>\n    <p>/*7*/\n</div>;\n\n// @Filename: /8.tsx\nconst x = <div>\n    <div>/*8*/</div>\n</div>;\n\n// @Filename: /9.tsx\nconst x = <p>\n    <div>\n        <div>/*9*/\n    </div>\n</p>"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyJsxClosingTag(t, new Dictionary<string, string?> { ["0"] = "</div>", ["1"] = null!, ["2"] = null!, ["3"] = null!, ["4"] = "</p>", ["5"] = "</div>", ["6"] = "</div>", ["7"] = "</p>", ["8"] = null!, ["9"] = "</div>" });
            }
            finally
            {
                done();
            }
        });
    }

}
