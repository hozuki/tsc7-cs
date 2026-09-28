// ported from: internal/fourslash/tests/codeFixSpellingJs3_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class CodeFixSpellingJs3Case
{
    public static void TestCodeFixSpellingJs3(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @allowjs: true\n// @noEmit: true\n// @filename: a.js\nclass Classe {\n    non = 'oui'\n    methode() {\n        // no error on 'this' references\n        return this.none\n    }\n}\nclass Derivee extends Classe {\n    methode() {\n        // no error on 'super' references\n        return super.none\n    }\n}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyNoErrors(t);
            }
            finally
            {
                done();
            }
        });
    }

}
