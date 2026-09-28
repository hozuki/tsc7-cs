// ported from: internal/fourslash/tests/navbar_contains-no-duplicates_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class Navbar_contains_no_duplicatesCase
{
    public static void TestNavbar_contains_no_duplicates(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("declare namespace Windows {\n    export namespace Foundation {\n        export var A;\n        export class Test {\n            public wow();\n        }\n    }\n}\n\ndeclare namespace Windows {\n    export namespace Foundation {\n        export var B;\n        export namespace Test {\n            export function Boom(): number;\n        }\n    }\n}\n\nclass ABC {\n    public foo() {\n        return 3;\n    }\n}\n\nnamespace ABC {\n    export var x = 3;\n}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyBaselineDocumentSymbol(t);
            }
            finally
            {
                done();
            }
        });
    }

}
