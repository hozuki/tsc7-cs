// ported from: internal/fourslash/tests/inlayHintsParameterNames_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using LSU = TypeScript.LsUtil.LsUtil;
using LSUP = TypeScript.LsUtil;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class InlayHintsParameterNamesCase
{
    public static void TestInlayHintsParameterNames(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8(" function foo1 (a: number, b: number) {}\n foo1(1, 2);\n function foo2 (a: number, { c }: any) {}\n foo2(1, { c: 1 });\nfunction foo3(a: any, b: number) {}\nfoo3({}, 1);\nconst foo3 = (a = 1) => class { }\nconst C1 = class extends foo3(1) { }\nclass C2 extends foo3(1) { }\nfunction foo4(a: number, b: number, c: number, d: number) {}\nfoo4(1, +1, -1, +\"1\");\nfunction foo5(\n    a: string,\n    b: undefined,\n    c: null,\n    d: boolean,\n    e: boolean,\n    f: number,\n    g: number,\n    h: number,\n    i: RegExp,\n    j: bigint,\n) {\n}\nfoo5(\n    \"hello\",\n    undefined,\n    null,\n    true,\n    false,\n    Infinity,\n    -Infinity,\n    NaN,\n    /hello/g,\n    123n,\n);\n declare const unknownCall: any;\n unknownCall();\nfunction trace(message: string) {}\ntrace(`${1}`);\ntrace(``);\nfunction func(\n    param1: number,\n    param2: string,\n    param3: boolean,\n) {}\nconst param1 = 1;\nfunc(\n    param1,\n    'foo',\n    true,\n)"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyBaselineInlayHints(t, null!, new LSUP.UserPreferences { InlayHints = new LSUP.InlayHintsPreferences { IncludeInlayParameterNameHints = LSU.IncludeInlayParameterNameHintsLiterals } });
            }
            finally
            {
                done();
            }
        });
    }

}
