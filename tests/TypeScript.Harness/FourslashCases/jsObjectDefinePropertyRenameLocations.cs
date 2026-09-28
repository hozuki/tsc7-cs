// ported from: internal/fourslash/tests/jsObjectDefinePropertyRenameLocations_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class JsObjectDefinePropertyRenameLocationsCase
{
    public static void TestJsObjectDefinePropertyRenameLocations(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @allowJs: true\n// @checkJs: true\n// @noEmit: true\n// @Filename: index.js\nvar CircularList = (function () {\n    var CircularList = function() {};\n    Object.defineProperty(CircularList.prototype, \"[|maxLength|]\", { value: 0, writable: true });\n    CircularList.prototype.push = function (value) {\n        // ...\n        this.[|maxLength|] + this.[|maxLength|]\n    }\n    return CircularList;\n})()"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyBaselineRenameAtRangesWithText(t, null!);
            }
            finally
            {
                done();
            }
        });
    }

}
