// ported from: internal/fourslash/tests/codeFixAddMissingAttributes5_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class CodeFixAddMissingAttributes5Case
{
    public static void TestCodeFixAddMissingAttributes5(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @jsx: preserve\n// @filename: foo.tsx\ninterface P {\n    a: number;\n    b: string;\n    c: number[];\n    d: any;\n}\n\nconst A = ({ a, b, c, d }: P) =>\n    <div>{a}{b}{c}{d}</div>;\n\nconst Bar = () =>\n    [|<A a={100} b={\"\"} c={[]} d={undefined}></A>|]"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyCodeFixNotAvailable(t, "fixMissingAttributes");
            }
            finally
            {
                done();
            }
        });
    }

}
