// ported from: internal/fourslash/tests/signatureHelpInRecursiveType_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class SignatureHelpInRecursiveTypeCase
{
    public static void TestSignatureHelpInRecursiveType(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("type Tail<T extends any[]> =\n\t((...args: T) => any) extends ((head: any, ...tail: infer R) => any) ? R : never;\n\ntype Reverse<List extends any[]> = _Reverse<List, []>;\n\ntype _Reverse<Source extends any[], Result extends any[] = []> = {\n\t1: Result,\n\t0: _Reverse<Tail<Source>, 0>,\n}[Source extends [] ? 1 : 0];\n\ntype Foo = Reverse<[0,/**/]>;"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.GoToMarker(t, "");
                f.VerifySignatureHelp(t, new F.VerifySignatureHelpOptions { Text = "Reverse<List extends any[]>" });
            }
            finally
            {
                done();
            }
        });
    }

}
