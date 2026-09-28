// ported from: internal/fourslash/tests/getOutliningSpansForTemplateLiteral_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class GetOutliningSpansForTemplateLiteralCase
{
    public static void TestGetOutliningSpansForTemplateLiteral(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("declare function tag(...args: any[]): void\nconst a = [|`signal line`|]\nconst b = [|`multi\nline`|]\nconst c = tag[|`signal line`|]\nconst d = tag[|`multi\nline`|]\nconst e = [|`signal ${1} line`|]\nconst f = [|`multi\n${1}\nline`|]\nconst g = tag[|`signal ${1} line`|]\nconst h = tag[|`multi\n${1}\nline`|]\nconst i = ``"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyOutliningSpans(t);
            }
            finally
            {
                done();
            }
        });
    }

}
