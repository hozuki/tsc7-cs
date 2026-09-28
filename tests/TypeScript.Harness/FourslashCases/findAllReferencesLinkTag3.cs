// ported from: internal/fourslash/tests/findAllReferencesLinkTag3_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class FindAllReferencesLinkTag3Case
{
    public static void TestFindAllReferencesLinkTag3(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("namespace NPR/*5*/ {\n    export class Consider/*4*/ {\n        This/*3*/ = class {\n            show/*2*/() { }\n        }\n        m/*1*/() { }\n    }\n    /**\n     * {@linkcode Consider.prototype.m}\n     * {@linkplain Consider#m}\n     * {@linkcode Consider#This#show}\n     * {@linkplain Consider.This.show}\n     * {@linkcode NPR.Consider#This#show}\n     * {@linkplain NPR.Consider.This#show}\n     * {@linkcode NPR.Consider#This.show} # doesn't parse trailing .\n     * {@linkcode NPR.Consider.This.show}\n     */\n    export function ref() { }\n}\n/**\n * {@linkplain NPR.Consider#This#show hello hello}\n * {@linkplain NPR.Consider.This#show}\n * {@linkcode NPR.Consider#This.show} # doesn't parse trailing .\n * {@linkcode NPR.Consider.This.show}\n */\nexport function outerref() { }"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyBaselineFindAllReferences(t, "1", "2", "3", "4", "5");
            }
            finally
            {
                done();
            }
        });
    }

}
