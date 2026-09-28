// ported from: internal/fourslash/tests/findAllReferencesOfConstructor_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class FindAllReferencesOfConstructorCase
{
    public static void TestFindAllReferencesOfConstructor(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @Filename: a.ts\nexport class C {\n    /*0*/constructor(n: number);\n    /*1*/constructor();\n    /*2*/constructor(n?: number){}\n    static f() {\n        this.f();\n        new this();\n    }\n}\nnew C();\nconst D = C;\nnew D();\n// @Filename: b.ts\nimport { C } from \"./a\";\nnew C();\n// @Filename: c.ts\nimport { C } from \"./a\";\nclass D extends C {\n    constructor() {\n        super();\n        super.method();\n    }\n    method() { super(); }\n}\nclass E implements C {\n    constructor() { super(); }\n}\n// @Filename: d.ts\nimport * as a from \"./a\";\nnew a.C();\nclass d extends a.C { constructor() { super(); }"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyBaselineFindAllReferences(t, "0", "1", "2");
            }
            finally
            {
                done();
            }
        });
    }

}
