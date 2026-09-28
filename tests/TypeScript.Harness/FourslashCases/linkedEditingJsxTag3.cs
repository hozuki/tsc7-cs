// ported from: internal/fourslash/tests/linkedEditingJsxTag3_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using LSP = TypeScript.LsProto;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class LinkedEditingJsxTag3Case
{
    public static void TestLinkedEditingJsxTag3(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @Filename: /selfClosing.tsx\n/*0*/const jsx = /*1*/(\n   <div> /*2*/\n      <p>/*3*/\n         No lin/*4*/ked cursors here!\n         /*5*/</*6*/img/*7*/ /*8*///*9*/>\n     /*10*/ </p>/*11*/\n   /*12*/</div>\n/*13*/)/*14*/;/*15*/"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyLinkedEditing(t, new Dictionary<string, LSP.Range[]> { ["0"] = [], ["1"] = [], ["2"] = [], ["3"] = [], ["4"] = [], ["5"] = [], ["6"] = [], ["7"] = [], ["8"] = [], ["9"] = [], ["10"] = [], ["11"] = [], ["12"] = [], ["13"] = [], ["14"] = [], ["15"] = [] });
            }
            finally
            {
                done();
            }
        });
    }

}
