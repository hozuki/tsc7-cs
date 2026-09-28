// ported from: internal/fourslash/tests/getOccurrencesThrow3_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;
using U = TypeScript.Harness.FourslashCases.FourslashTestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class GetOccurrencesThrow3Case
{
    public static void TestGetOccurrencesThrow3(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("function f(a: number) {\n    try {\n        [|throw|] \"Hello\";\n\n        try {\n            throw 10;\n        }\n        catch (x) {\n            return 100;\n        }\n        finally {\n            [|thr/**/ow|] 10;\n        }\n    }\n    catch (x) {\n        throw \"Something\";\n    }\n    finally {\n        throw \"Also something\";\n    }\n    if (a > 0) {\n        return (function () {\n            return;\n            return;\n            return;\n\n            if (false) {\n                return true;\n            }\n            throw \"Hello!\";\n        })() || true;\n    }\n\n    throw 10;\n\n    var unusued = [1, 2, 3, 4].map(x => { throw 4 })\n\n    return;\n    return true;\n    throw false;\n}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyBaselineDocumentHighlights(t, null!, U.ToAny(f.Ranges()));
            }
            finally
            {
                done();
            }
        });
    }

}
