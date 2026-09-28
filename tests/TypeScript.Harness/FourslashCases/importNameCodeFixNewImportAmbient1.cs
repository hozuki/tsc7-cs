// ported from: internal/fourslash/tests/importNameCodeFixNewImportAmbient1_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class ImportNameCodeFixNewImportAmbient1Case
{
    public static void TestImportNameCodeFixNewImportAmbient1(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("import d from \"other-ambient-module\";\nimport * as ns from \"yet-another-ambient-module\";\nvar x = v1/*0*/ + 5;\n// @Filename: ambientModule.ts\ndeclare module \"ambient-module\" {\n   export function f1();\n   export var v1;\n}\n// @Filename: otherAmbientModule.ts\ndeclare module \"other-ambient-module\" {\n   export default function f2();\n}\n// @Filename: yetAnotherAmbientModule.ts\ndeclare module \"yet-another-ambient-module\" {\n   export function f3();\n   export var v3;\n}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyImportFixAtPosition(t, ["import { v1 } from \"ambient-module\";\nimport d from \"other-ambient-module\";\nimport * as ns from \"yet-another-ambient-module\";\nvar x = v1 + 5;"], null!);
            }
            finally
            {
                done();
            }
        });
    }

}
