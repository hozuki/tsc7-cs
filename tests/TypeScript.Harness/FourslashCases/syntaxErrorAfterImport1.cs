// ported from: internal/fourslash/tests/syntaxErrorAfterImport1_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class SyntaxErrorAfterImport1Case
{
    public static void TestSyntaxErrorAfterImport1(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("declare module \"extmod\" {\n  namespace IntMod {\n    class Customer {\n      constructor(name: string);\n    }\n  }\n}\nimport ext = require('extmod');\nimport int = ext.IntMod;\nvar x = new int/*0*/"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.GoToMarker(t, "0");
                f.Insert(t, ".");
            }
            finally
            {
                done();
            }
        });
    }

}
