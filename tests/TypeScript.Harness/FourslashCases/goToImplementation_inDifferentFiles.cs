// ported from: internal/fourslash/tests/goToImplementation_inDifferentFiles_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class GoToImplementation_inDifferentFilesCase
{
    public static void TestGoToImplementation_inDifferentFiles(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @lib: es5\n// @Filename: /home/src/workspaces/project/bar.ts\nimport {Foo} from './foo'\n\nclass [|A|] implements Foo {\n    func() {}\n}\n\nclass [|B|] implements Foo {\n    func() {}\n}\n// @Filename: /home/src/workspaces/project/foo.ts\nexport interface /**/Foo {\n    func();\n}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.MarkTestAsStradaServer();
                f.VerifyBaselineGoToImplementation(t, "");
            }
            finally
            {
                done();
            }
        });
    }

}
