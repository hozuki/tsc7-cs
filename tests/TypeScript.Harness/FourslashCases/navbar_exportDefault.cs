// ported from: internal/fourslash/tests/navbar_exportDefault_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class Navbar_exportDefaultCase
{
    public static void TestNavbar_exportDefault(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @Filename: a.ts\nexport default class { }\n// @Filename: b.ts\nexport default class C { }\n// @Filename: c.ts\nexport default function { }\n// @Filename: d.ts\nexport default function Func { }"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.GoToFile(t, "a.ts");
                f.VerifyBaselineDocumentSymbol(t);
                f.GoToFile(t, "b.ts");
                f.VerifyBaselineDocumentSymbol(t);
                f.GoToFile(t, "c.ts");
                f.VerifyBaselineDocumentSymbol(t);
                f.GoToFile(t, "d.ts");
                f.VerifyBaselineDocumentSymbol(t);
            }
            finally
            {
                done();
            }
        });
    }

}
