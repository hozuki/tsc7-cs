// ported from: internal/fourslash/tests/calledUnionsOfDissimilarTyeshaveGoodDisplay_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class CalledUnionsOfDissimilarTyeshaveGoodDisplayCase
{
    public static void TestCalledUnionsOfDissimilarTyeshaveGoodDisplay(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("declare const callableThing1:\n    | ((o1: {x: number}) => void)\n    | ((o1: {y: number}) => void)\n    ;\n\ncallableThing1(/*1*/);\n\ndeclare const callableThing2:\n    | ((o1: {x: number}) => void)\n    | ((o2: {y: number}) => void)\n    ;\n\ncallableThing2(/*2*/);\n\ndeclare const callableThing3:\n    | ((o1: {x: number}) => void)\n    | ((o2: {y: number}) => void)\n    | ((o3: {z: number}) => void)\n    | ((o4: {u: number}) => void)\n    | ((o5: {v: number}) => void)\n    ;\n\ncallableThing3(/*3*/);\n\ndeclare const callableThing4:\n    | ((o1: {x: number}) => void)\n    | ((o2: {y: number}) => void)\n    | ((o3: {z: number}) => void)\n    | ((o4: {u: number}) => void)\n    | ((o5: {v: number}) => void)\n    | ((o6: {w: number}) => void)\n    ;\n\ncallableThing4(/*4*/);\n\ndeclare const callableThing5: \n    | (<U>(a1: U) => void)\n    | (() => void) \n    ;\n\ncallableThing5(/*5*/1)\n"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.GoToMarker(t, "1");
                f.VerifySignatureHelp(t, new F.VerifySignatureHelpOptions { Text = "callableThing1(o1: { x: number; } & { y: number; }): void" });
                f.GoToMarker(t, "2");
                f.VerifySignatureHelp(t, new F.VerifySignatureHelpOptions { Text = "callableThing2(arg0: { x: number; } & { y: number; }): void" });
                f.GoToMarker(t, "3");
                f.VerifySignatureHelp(t, new F.VerifySignatureHelpOptions { Text = "callableThing3(arg0: { x: number; } & { y: number; } & { z: number; } & { u: number; } & { v: number; }): void" });
                f.GoToMarker(t, "4");
                f.VerifySignatureHelp(t, new F.VerifySignatureHelpOptions { Text = "callableThing4(arg0: { x: number; } & { y: number; } & { z: number; } & { u: number; } & { v: number; } & { w: number; }): void" });
                f.GoToMarker(t, "5");
                f.VerifySignatureHelp(t, new F.VerifySignatureHelpOptions { Text = "callableThing5(a1: number): void" });
            }
            finally
            {
                done();
            }
        });
    }

}
