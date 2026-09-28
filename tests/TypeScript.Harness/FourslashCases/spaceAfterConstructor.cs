// ported from: internal/fourslash/tests/spaceAfterConstructor_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class SpaceAfterConstructorCase
{
    public static void TestSpaceAfterConstructor(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("export class myController {\n    private _processId;\n    constructor (processId: number) {/*1*/\n        this._processId = processId;\n    }/*2*/"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.GoToMarker(t, "2");
                f.Insert(t, "}");
                f.GoToMarker(t, "1");
                f.VerifyCurrentLineContent(t, "    constructor(processId: number) {");
            }
            finally
            {
                done();
            }
        });
    }

}
