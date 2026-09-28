// ported from: internal/fourslash/tests/missingMethodAfterEditAfterImport_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class MissingMethodAfterEditAfterImportCase
{
    public static void TestMissingMethodAfterEditAfterImport(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("namespace foo {\n    export namespace bar { namespace baz { export class boo { } } }\n}\n\nimport f = /*foo*/foo;\n\n/*delete*/var x;"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyQuickInfoAt(t, "foo", "namespace foo", "");
                f.GoToMarker(t, "delete");
                f.DeleteAtCaret(t, 6);
                f.VerifyQuickInfoAt(t, "foo", "namespace foo", "");
            }
            finally
            {
                done();
            }
        });
    }

}
