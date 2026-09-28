// ported from: internal/fourslash/tests/quickfixImplementInterfaceUnreachableTypeUsesRelativeImport_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class QuickfixImplementInterfaceUnreachableTypeUsesRelativeImportCase
{
    public static void TestQuickfixImplementInterfaceUnreachableTypeUsesRelativeImport(TSU.IHarnessTest t)
    {
        t.Skip("Known failing fourslash test");
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @Filename: class.ts\nexport class Class { }\n// @Filename: interface.ts\nimport { Class } from './class';\n\nexport interface Foo {\n    x: Class;\n}\n// @Filename: index.ts\nimport { Foo } from './interface';\n\nclass /*1*/X implements Foo {}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.GoToMarker(t, "1");
                f.VerifyCodeFix(t, new F.VerifyCodeFixOptions { Description = "Implement interface 'Foo'", NewFileContent = "", Index = 0 });
            }
            finally
            {
                done();
            }
        });
    }

}
