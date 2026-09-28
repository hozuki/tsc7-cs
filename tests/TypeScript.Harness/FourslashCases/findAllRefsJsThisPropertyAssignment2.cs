// ported from: internal/fourslash/tests/findAllRefsJsThisPropertyAssignment2_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class FindAllRefsJsThisPropertyAssignment2Case
{
    public static void TestFindAllRefsJsThisPropertyAssignment2(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @allowJs: true\n// @noImplicitThis: true\n// @Filename: infer.d.ts\nexport declare function infer(o: { m: Record<string, Function> } & ThisType<{ x: number }>): void;\n// @Filename: a.js\nimport { infer } from \"./infer\";\ninfer({\n    m: {\n        initData() {\n            this.x = 1;\n            this./*1*/x;\n        },\n    }\n});\n// @Filename: b.ts\nimport { infer } from \"./infer\";\ninfer({\n    m: {\n        initData() {\n            this.x = 1;\n            this./*2*/x;\n        },\n    }\n});"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyBaselineFindAllReferences(t, "1", "2");
            }
            finally
            {
                done();
            }
        });
    }

}
