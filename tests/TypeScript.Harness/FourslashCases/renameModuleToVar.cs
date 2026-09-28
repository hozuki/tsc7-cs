// ported from: internal/fourslash/tests/renameModuleToVar_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class RenameModuleToVarCase
{
    public static void TestRenameModuleToVar(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("interface IMod {\n    y: number;\n}\ndeclare module/**/ X: IMod;// {\n//    export var y: numb;\nvar y: number;\nnamespace Y {\n    var z = y + 5;\n}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.GoToMarker(t, "");
                f.Backspace(t, 6);
                f.Insert(t, "var");
                f.VerifyNoErrors(t);
            }
            finally
            {
                done();
            }
        });
    }

}
