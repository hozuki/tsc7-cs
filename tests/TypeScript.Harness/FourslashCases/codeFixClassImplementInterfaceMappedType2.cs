// ported from: internal/fourslash/tests/codeFixClassImplementInterfaceMappedType2_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class CodeFixClassImplementInterfaceMappedType2Case
{
    public static void TestCodeFixClassImplementInterfaceMappedType2(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("type ListenerTemplate<T, S extends string, I extends string = \"${1}\"> = {\n    [K in keyof T as K extends string\n        ? S extends `${infer F}${I}${infer R}` ? `${F}${K}${R}` : K : K]\n        : (listener: (payload: T[K]) => void) => void;\n};\ntype ListenActionable<E> = ListenerTemplate<E, \"add*Listener\" | \"remove*Listener\", \"*\">;\ntype ClickEventSupport = ListenActionable<{ Click: 'some-click-event-payload' }>;\n\n[|class C implements ClickEventSupport { }|]"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyCodeFix(t, new F.VerifyCodeFixOptions { Description = "Implement interface 'ClickEventSupport'", NewRangeContent = "class C implements ClickEventSupport {\n    addClickListener: (listener: (payload: \"some-click-event-payload\") => void) => void;\n    removeClickListener: (listener: (payload: \"some-click-event-payload\") => void) => void;\n}", Index = 0 });
            }
            finally
            {
                done();
            }
        });
    }

}
