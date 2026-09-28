// ported from: internal/fourslash/tests/codeFixSpellingJs6_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class CodeFixSpellingJs6Case
{
    public static void TestCodeFixSpellingJs6(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @allowjs: true\n// @checkjs: false\n// @noEmit: true\n// @filename: spellingUncheckedJS.js\nexport var inModule = 1\ninmodule.toFixed()\n\nfunction f() {\n    var locals = 2 + true\n    locale.toFixed()\n}\nclass Classe {\n    non = 'oui'\n    methode() {\n        // no error on 'this' references\n        return this.none\n    }\n}\nclass Derivee extends Classe {\n    methode() {\n        // no error on 'super' references\n        return super.none\n    }\n}\n\n\nvar object = {\n    spaaace: 3\n}\nobject.spaaaace // error on read\nobject.spaace = 12 // error on write\nobject.fresh = 12 // OK\nother.puuuce // OK, from another file\nnew Date().getGMTDate() // OK, from another file\n\n// No suggestions for globals from other files\nconst atoc = setIntegral(() => console.log('ok'), 500)\nAudioBuffin // etc\nJimmy\nJon\nwindow.argle\nself.blargle\n// @filename: other.js\nvar Jimmy = 1\nvar John = 2\nJon // error, it's from the same file\nvar other = {\n    puuce: 4\n}\nwindow.argle\nself.blargle"u8);
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
