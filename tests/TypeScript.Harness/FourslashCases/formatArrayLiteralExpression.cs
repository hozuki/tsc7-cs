// ported from: internal/fourslash/tests/formatArrayLiteralExpression_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class FormatArrayLiteralExpressionCase
{
    public static void TestFormatArrayLiteralExpression(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("export let Things = [{\n    Hat: 'hat', /*1*/\n    Glove: 'glove',\n    Umbrella: 'umbrella'\n},{/*2*/\n        Salad: 'salad', /*3*/\n        Burrito: 'burrito',\n        Pie: 'pie'\n    }];/*4*/\n\nexport let Things2 = [\n{\n    Hat: 'hat', /*5*/\n    Glove: 'glove',\n    Umbrella: 'umbrella'\n}/*6*/,\n    {\n        Salad: 'salad', /*7*/\n        Burrito: ['burrito', 'carne asada', 'tinga de res', 'tinga de pollo'], /*8*/\n        Pie: 'pie'\n    }];/*9*/"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.FormatDocument(t, "");
                f.GoToMarker(t, "1");
                f.VerifyCurrentLineContent(t, "    Hat: 'hat',");
                f.GoToMarker(t, "2");
                f.VerifyCurrentLineContent(t, "}, {");
                f.GoToMarker(t, "3");
                f.VerifyCurrentLineContent(t, "    Salad: 'salad',");
                f.GoToMarker(t, "4");
                f.VerifyCurrentLineContent(t, "}];");
                f.GoToMarker(t, "5");
                f.VerifyCurrentLineContent(t, "        Hat: 'hat',");
                f.GoToMarker(t, "6");
                f.VerifyCurrentLineContent(t, "    },");
                f.GoToMarker(t, "7");
                f.VerifyCurrentLineContent(t, "        Salad: 'salad',");
                f.GoToMarker(t, "8");
                f.VerifyCurrentLineContent(t, "        Burrito: ['burrito', 'carne asada', 'tinga de res', 'tinga de pollo'],");
                f.GoToMarker(t, "9");
                f.VerifyCurrentLineContent(t, "    }];");
            }
            finally
            {
                done();
            }
        });
    }

}
