// ported from: internal/fourslash/tests/addMemberNotInNodeModulesDeclarationFile_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class AddMemberNotInNodeModulesDeclarationFileCase
{
    public static void TestAddMemberNotInNodeModulesDeclarationFile(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @noImplicitReferences: true\n// @traceResolution: true\n// @Filename: /node_modules/foo/types.d.ts\ninterface Response {}\n// @Filename: /node_modules/foo/package.json\n{ \"types\": \"types.d.ts\" }\n// @Filename: /foo.ts\nimport { Response } from 'foo'\ndeclare const resp: Response\nresp.test()"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.GoToFile(t, "/foo.ts");
                f.VerifyCodeFixNotAvailable(t);
            }
            finally
            {
                done();
            }
        });
    }

}
