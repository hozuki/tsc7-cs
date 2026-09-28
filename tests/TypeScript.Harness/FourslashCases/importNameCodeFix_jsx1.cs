// ported from: internal/fourslash/tests/importNameCodeFix_jsx1_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class ImportNameCodeFix_jsx1Case
{
    public static void TestImportNameCodeFix_jsx1(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @jsx: react\n// @Filename: /node_modules/react/index.d.ts\nexport const React: any;\n// @Filename: /a.tsx\n[|<this>|]</this>\n// @Filename: /Foo.tsx\nexport const Foo = 0;\n// @Filename: /c.tsx\nimport { React } from \"react\";\n<Foo />;\n// @Filename: /d.tsx\nimport { Foo } from \"./Foo\";\n<Foo />;"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.GoToFile(t, "/a.tsx");
                f.VerifyImportFixAtPosition(t, [], null!);
                f.GoToFile(t, "/c.tsx");
                f.VerifyImportFixAtPosition(t, ["import { React } from \"react\";\nimport { Foo } from \"./Foo\";\n<Foo />;"], null!);
                f.GoToFile(t, "/d.tsx");
                f.VerifyImportFixAtPosition(t, ["import { React } from \"react\";\nimport { Foo } from \"./Foo\";\n<Foo />;"], null!);
            }
            finally
            {
                done();
            }
        });
    }

}
