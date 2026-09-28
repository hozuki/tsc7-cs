// ported from: internal/fourslash/tests/formattingChainingMethods_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class FormattingChainingMethodsCase
{
    public static void TestFormattingChainingMethods(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8(" z$ = this.store.select(this.fake())\n     .ofType(\n      'ACTION',\n      'ACTION-2'\n     )\n     .pipe(\n         filter(x => !!x),\n         switchMap(() =>\n          this.store.select(this.menuSelector.getAll('x'))\n           .pipe(\n             tap(x => {\n             this.x = !x;\n             })\n           )\n         )\n     );\n\n1\n    .toFixed(\n        2);"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.FormatDocument(t, "");
                f.VerifyCurrentFileContent(t, "z$ = this.store.select(this.fake())\n    .ofType(\n        'ACTION',\n        'ACTION-2'\n    )\n    .pipe(\n        filter(x => !!x),\n        switchMap(() =>\n            this.store.select(this.menuSelector.getAll('x'))\n                .pipe(\n                    tap(x => {\n                        this.x = !x;\n                    })\n                )\n        )\n    );\n\n1\n    .toFixed(\n        2);");
            }
            finally
            {
                done();
            }
        });
    }

}
