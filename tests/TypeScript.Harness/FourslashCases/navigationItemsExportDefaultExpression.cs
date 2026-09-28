// ported from: internal/fourslash/tests/navigationItemsExportDefaultExpression_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class NavigationItemsExportDefaultExpressionCase
{
    public static void TestNavigationItemsExportDefaultExpression(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("export default function () {}\nexport default function () {\n    return class Foo {\n    }\n}\n\nexport default () => \"\"\nexport default () => {\n    return class Foo {\n    }\n}\n\nexport default function f1() {}\nexport default function f2() {\n    return class Foo {\n    }\n}\n\nconst abc = 12;\nexport default abc;\nexport default class AB {}\nexport default {\n    a: 1,\n    b: 1,\n    c: {\n        d: 1\n    }\n}\n\nfunction foo(props: { x: number; y: number }) {}\nexport default foo({ x: 1, y: 1 });"u8);
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
