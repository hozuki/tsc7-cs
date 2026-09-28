// ported from: internal/fourslash/tests/smartIndentNamedImport_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class SmartIndentNamedImportCase
{
    public static void TestSmartIndentNamedImport(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("import {/*0*/\n    numbers as bn,/*1*/\n    list/*2*/\n} from '@bykov/basics';/*3*/"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.FormatDocument(t, "");
                f.GoToMarker(t, "0");
                f.VerifyCurrentLineContent(t, "import {");
                f.GoToMarker(t, "1");
                f.VerifyCurrentLineContent(t, "    numbers as bn,");
                f.GoToMarker(t, "2");
                f.VerifyCurrentLineContent(t, "    list");
                f.GoToMarker(t, "3");
                f.VerifyCurrentLineContent(t, "} from '@bykov/basics';");
            }
            finally
            {
                done();
            }
        });
    }

}
