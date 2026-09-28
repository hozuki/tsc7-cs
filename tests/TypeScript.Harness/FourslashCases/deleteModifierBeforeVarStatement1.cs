// ported from: internal/fourslash/tests/deleteModifierBeforeVarStatement1_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class DeleteModifierBeforeVarStatement1Case
{
    public static void TestDeleteModifierBeforeVarStatement1(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("\n\n/////////////////////////////\n/// Windows Script Host APIS\n/////////////////////////////\n\ndeclare var ActiveXObject: { new (s: string): any; };\n\ninterface ITextWriter {\n    WriteLine(s): void;\n}\n\ndeclare var WScript: {\n    Echo(s): void;\n    StdErr: ITextWriter;\n    Arguments: { length: number; Item(): string; };\n    ScriptFullName: string;\n    Quit(): number;\n}\n"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.GoToFileNumber(t, 0);
                f.GoToPosition(t, 0);
                f.DeleteAtCaret(t, 100);
                f.GoToPosition(t, 198);
                f.DeleteAtCaret(t, 16);
                f.GoToPosition(t, 198);
                f.Insert(t, "Item(): string; ");
            }
            finally
            {
                done();
            }
        });
    }

}
