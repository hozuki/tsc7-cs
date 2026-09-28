// ported from: internal/fourslash/tests/importNameCodeFixUMDGlobalReact2_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class ImportNameCodeFixUMDGlobalReact2Case
{
    public static void TestImportNameCodeFixUMDGlobalReact2(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @jsx: react\n// @jsxFactory: factory\n// @Filename: /factory.ts\nexport function factory() { return {}; }\ndeclare global {\n    namespace JSX {\n        interface Element {}\n    }\n}\n// @Filename: /a.tsx\n[|<div/>|]"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.GoToFile(t, "/a.tsx");
                f.VerifyImportFixAtPosition(t, ["import { factory } from \"./factory\";\n\n<div/>"], null!);
            }
            finally
            {
                done();
            }
        });
    }

}
