// ported from: internal/fourslash/tests/autoCloseFragment_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class AutoCloseFragmentCase
{
    public static void TestAutoCloseFragment(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @noLib: true\n// @Filename: /0.tsx\nconst x = <>/*0*/;\n\n// @Filename: /1.tsx\nconst x = <> foo/*1*/ </>;\n\n// @Filename: /2.tsx\nconst x = <></>/*2*/;\n\n// @Filename: /3.tsx\nconst x = </>/*3*/;\n\n// @Filename: /4.tsx\nconst x = <div>\n    <>/*4*/\n    </div>\n</>;\n\n// @Filename: /5.tsx\nconst x = <> text /*5*/;\n\n// @Filename: /6.tsx\nconst x = <>\n    <>/*6*/\n</>;\n\n// @Filename: /7.tsx\nconst x = <div>\n    <>/*7*/\n</div>;\n\n// @Filename: /8.tsx\nconst x = <div>\n    <>/*8*/</>\n</div>;\n\n// @Filename: /9.tsx\nconst x = <p>\n    <>\n        <>/*9*/\n    </>\n</p>"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyJsxClosingTag(t, new Dictionary<string, string?> { ["0"] = "</>", ["1"] = null!, ["2"] = null!, ["3"] = null!, ["4"] = "</>", ["5"] = "</>", ["6"] = "</>", ["7"] = "</>", ["8"] = null!, ["9"] = "</>" });
            }
            finally
            {
                done();
            }
        });
    }

}
