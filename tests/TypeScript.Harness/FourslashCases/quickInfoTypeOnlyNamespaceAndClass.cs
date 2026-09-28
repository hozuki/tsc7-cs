// ported from: internal/fourslash/tests/quickInfoTypeOnlyNamespaceAndClass_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class QuickInfoTypeOnlyNamespaceAndClassCase
{
    public static void TestQuickInfoTypeOnlyNamespaceAndClass(TSU.IHarnessTest t)
    {
        t.Skip("Known failing fourslash test");
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @Filename: /a.ts\nexport namespace ns {\n  export class Box<T> {}\n}\n// @Filename: /b.ts\nimport type { ns } from './a';\nlet x: /*1*/ns./*2*/Box<string>;"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyQuickInfoAt(t, "1", "(alias) namespace ns\nimport ns", "");
                f.VerifyQuickInfoAt(t, "2", "class ns.Box<T>", "");
            }
            finally
            {
                done();
            }
        });
    }

}
