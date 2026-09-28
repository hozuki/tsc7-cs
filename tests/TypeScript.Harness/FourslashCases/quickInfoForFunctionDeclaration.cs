// ported from: internal/fourslash/tests/quickInfoForFunctionDeclaration_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class QuickInfoForFunctionDeclarationCase
{
    public static void TestQuickInfoForFunctionDeclaration(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("interface A<T> { }\n\nfunction ma/*makeA*/keA<T>(t: T): A<T> { return null; }\n\nfunction /*f*/f<T>(t: T) {\n    return makeA(t);\n}\n\nvar x = f(0);\nvar y = makeA(0);"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyQuickInfoAt(t, "makeA", "function makeA<T>(t: T): A<T>", "");
                f.VerifyQuickInfoAt(t, "f", "function f<T>(t: T): A<T>", "");
            }
            finally
            {
                done();
            }
        });
    }

}
