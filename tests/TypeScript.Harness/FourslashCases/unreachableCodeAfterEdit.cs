// ported from: internal/fourslash/tests/unreachableCodeAfterEdit_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class UnreachableCodeAfterEditCase
{
    public static void TestUnreachableCodeAfterEdit(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @allowUnreachableCode: false\n// @lib: es2015\n// @Filename: /base/browser/browser.ts\nexport const isStandalone = true;\n// @Filename: /base/browser/dom.ts\nexport function addDisposableListener() {}\n// @Filename: /base/browser/window.ts\nexport const mainWindow = {} as Window;\n// @Filename: /workbench.ts\n/*before*/import { isStandalone } from './base/browser/browser';\nimport { addDisposableListener } from './base/browser/dom';\nimport { mainWindow } from './base/browser/window';\n\ninterface ISecretStorageCrypto {\n    seal(data: string): Promise<string>;\n    unseal(data: string): Promise<string>;\n}\n\nexport class TransparentCrypto implements ISecretStorageCrypto {\n    async seal(data: string): Promise<string> {\n        return data;\n    }\n    async unseal(data: string): Promise<string> {\n        return data;\n    }\n}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyNumberOfErrorsInCurrentFile(t, 0);
                f.GoToMarker(t, "before");
                f.Insert(t, "throw new Error('foo');\n");
                f.VerifyNumberOfErrorsInCurrentFile(t, 1);
                f.GoToMarker(t, "before");
                f.DeleteAtCaret(t, 24);
                f.VerifyNumberOfErrorsInCurrentFile(t, 0);
            }
            finally
            {
                done();
            }
        });
    }

}
