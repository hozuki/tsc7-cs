// ported from: internal/fourslash/tests/findAllRefsOfConstructor_multipleFiles_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class FindAllRefsOfConstructor_multipleFilesCase
{
    public static void TestFindAllRefsOfConstructor_multipleFiles(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @Filename: f.ts\nclass A {\n    /*aCtr*/constructor(s: string) {}\n}\nclass B extends A { }\nexport { A, B };\n// @Filename: a.ts\nimport { A as A1 } from \"./f\";\nconst a1 = new A1(\"a1\");\nexport default class extends A1 { }\nexport { B as B1 } from \"./f\";\n// @Filename: b.ts\nimport B, { B1 } from \"./a\";\nconst d = new B(\"b\");\nconst d1 = new B1(\"b1\");"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyNoErrors(t);
                f.VerifyBaselineFindAllReferences(t, "aCtr");
            }
            finally
            {
                done();
            }
        });
    }

}
