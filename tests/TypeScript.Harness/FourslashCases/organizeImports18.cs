// ported from: internal/fourslash/tests/organizeImports18_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using LSP = TypeScript.LsProto;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class OrganizeImports18Case
{
    public static void TestOrganizeImports18(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @filename: /A.ts\nexport interface A {}\nexport function bFuncA(a: A) {}\n// @filename: /B.ts\nexport interface B {}\nexport function bFuncB(b: B) {}\n// @filename: /C.ts\nexport interface C {}\nexport function bFuncC(c: C) {}\n// @filename: /test.ts\nexport { C } from \"./C\";\nexport { B } from \"./B\";\nexport { A } from \"./A\";\n\nexport { bFuncC } from \"./C\";\nexport { bFuncB } from \"./B\";\nexport { bFuncA } from \"./A\";"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.GoToFile(t, "/test.ts");
                f.VerifyOrganizeImports(t, "export { A } from \"./A\";\nexport { B } from \"./B\";\nexport { C } from \"./C\";\n\nexport { bFuncA } from \"./A\";\nexport { bFuncB } from \"./B\";\nexport { bFuncC } from \"./C\";\n", LSP.CodeActionKind.SourceOrganizeImportsTs, null!);
            }
            finally
            {
                done();
            }
        });
    }

}
