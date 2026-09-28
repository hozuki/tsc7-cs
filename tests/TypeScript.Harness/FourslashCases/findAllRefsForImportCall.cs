// ported from: internal/fourslash/tests/findAllRefsForImportCall_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class FindAllRefsForImportCallCase
{
    public static void TestFindAllRefsForImportCall(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @Filename: /app.ts\nexport function he/**/llo() {};\n// @Filename: /re-export.ts\nexport const services = { app: setup(() => import('./app')) }\nfunction setup<T>(importee: () => Promise<T>): T { return {} as any }\n// @Filename: /indirect-use.ts\nimport(\"./re-export\").then(mod => mod.services.app.hello());\n// @Filename: /direct-use.ts\nasync function main() {\n    const mod = await import(\"./app\")\n    mod.hello();\n}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyBaselineFindAllReferences(t, "");
            }
            finally
            {
                done();
            }
        });
    }

}
