// ported from: internal/fourslash/tests/linkedEditingJsxTag10_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class LinkedEditingJsxTag10Case
{
    public static void TestLinkedEditingJsxTag10(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = "// @Filename: /jsx0.tsx\nconst jsx = </*0*/>\n// @Filename: /jsx1.tsx\nconst jsx = <//*1*/>\n// @Filename: /jsx2.tsx\nconst jsx = </*2*/div>\n// @Filename: /jsx3.tsx\nconst jsx = <//*3*/div>\n// @Filename: /jsx4.tsx\nconst jsx = </*4*/div> <//*4a*/>;\n// @Filename: /jsx5.tsx\nconst jsx = </*5*/> <//*5a*/div>;\n// @Filename: /jsx6.tsx\nconst jsx = /*6*/div> <//*6a*/div>;\n// @Filename: /jsx7.tsx\nconst jsx = </*7*/div> //*7a*/div>;\n// @Filename: /jsx8.tsx\nconst jsx = </*8*/div <//*8a*/div>;\n// @Filename: /jsx9.tsx\nconst jsx = </*9*/div> <//*9a*/div;\n// @Filename: /jsx10.tsx\nconst jsx = </*10*/> <//*10a*/;\n// @Filename: /jsx11.tsx\nconst jsx = </*11*/ <//*11a*/>;\n// @Filename: /jsx12.tsx\nconst jsx = /*12*/> <//*12a*/>;\n// @Filename: /jsx13.tsx\nconst jsx = </*13*/> //*13a*/>;\n// @Filename: /jsx14.tsx\nconst jsx = </*14*/> </*14a*/div> <//*14b*/> <//*14c*/div>;\n// @Filename: /jsx15.tsx\nconst jsx = </*15*/div> </*15a*/> <//*15b*/div> <//*15c*/>;";
            var (f, done) = F.Fourslash.NewFourslash(t, null, content);
            try
            {
                f.VerifyBaselineLinkedEditing(t);
            }
            finally
            {
                done();
            }
        });
    }

}
