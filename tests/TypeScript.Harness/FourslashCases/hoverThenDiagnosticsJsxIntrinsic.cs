// ported from: internal/fourslash/tests/hoverThenDiagnosticsJsxIntrinsic_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class HoverThenDiagnosticsJsxIntrinsicCase
{
    public static void TestHoverThenDiagnosticsJsxIntrinsic(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @Filename: /tsconfig.json\n{ \"compilerOptions\": { \"strict\": true, \"jsx\": \"preserve\" } }\n// @Filename: /jsx.d.ts\ndeclare namespace JSX {\n    interface Element { }\n    interface IntrinsicElements {\n        div: any;\n    }\n}\n// @Filename: /file.tsx\nexport default function Home() {\n    return <di/*1*/v>hi</div>;\n}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyQuickInfoAt(t, "1", "(property) JSX.IntrinsicElements.div: any", "");
                f.VerifyNoErrors(t);
            }
            finally
            {
                done();
            }
        });
    }

}
