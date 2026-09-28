// ported from: internal/fourslash/tests/codeFixClassImplementInterfaceGlobal_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class CodeFixClassImplementInterfaceGlobalCase
{
    public static void TestCodeFixClassImplementInterfaceGlobal(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @Filename: /src/globals.d.ts\nexport {}; // Make this a module\ndeclare global {\n    interface Disposable {\n        [Symbol.dispose](): void;\n    }\n}\n// @Filename: /src/test.ts\nimport { Service } from './lifecycle';\nexport class [|EditingService|] implements Service { }\n// @Filename: /src/lifecycle.ts\nexport interface Disposable {\n\t(): string;\n}\nexport interface Service {\n\td: Disposable;\n}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.GoToFile(t, "/src/test.ts");
                f.VerifyCodeFix(t, new F.VerifyCodeFixOptions { Description = "Implement interface 'Service'", NewFileContent = "import { Disposable, Service } from './lifecycle';\nexport class EditingService implements Service {\n    d: Disposable;\n}", Index = 0 });
            }
            finally
            {
                done();
            }
        });
    }

}
