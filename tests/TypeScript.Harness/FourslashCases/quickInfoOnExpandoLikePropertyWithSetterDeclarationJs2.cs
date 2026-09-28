// ported from: internal/fourslash/tests/quickInfoOnExpandoLikePropertyWithSetterDeclarationJs2_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class QuickInfoOnExpandoLikePropertyWithSetterDeclarationJs2Case
{
    public static void TestQuickInfoOnExpandoLikePropertyWithSetterDeclarationJs2(TSU.IHarnessTest t)
    {
        t.Skip("Known failing fourslash test");
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @strict: true\n// @checkJs: true\n// @filename: index.js\nconst obj = {};\nlet val = 10;\nObject.defineProperty(obj, \"a\", {\n  configurable: true,\n  enumerable: true,\n  set(v) {\n    val = v;\n  },\n});\n\nobj.a/**/ = 100;"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyQuickInfoAt(t, "", "(property) obj.a: any", "");
            }
            finally
            {
                done();
            }
        });
    }

}
