// ported from: internal/fourslash/tests/codeFixSpellingJs5_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class CodeFixSpellingJs5Case
{
    public static void TestCodeFixSpellingJs5(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @allowjs: true\n// @noEmit: true\n// @filename: a.js\nvar other = {\n    puuce: 4\n}\nvar Jimmy = 1\nvar John = 2\n// @filename: b.js\nother.puuuce // OK, from another file\nnew Date().getGMTDate() // OK, from another file\nwindow.argle // OK, from globalThis\nself.blargle // OK, from globalThis\n\n// No suggestions for globals from other files\nconst atoc = setIntegral(() => console.log('ok'), 500)\nAudioBuffin // etc\nJimmy\nJon"u8);
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
