// ported from: internal/fourslash/tests/formattingOnTabAfterCloseCurly_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class FormattingOnTabAfterCloseCurlyCase
{
    public static void TestFormattingOnTabAfterCloseCurly(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("namespace Tools {/*1*/\n    export enum NodeType {/*2*/\n        Error,/*3*/\n        Comment,/*4*/\n    }   /*5*/\n    export enum foob/*6*/\n    {\n        Blah=1, Bleah=2/*7*/\n    }/*8*/\n}/*9*/"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.FormatDocument(t, "");
                f.GoToMarker(t, "1");
                f.VerifyCurrentLineContent(t, "namespace Tools {");
                f.GoToMarker(t, "2");
                f.VerifyCurrentLineContent(t, "    export enum NodeType {");
                f.GoToMarker(t, "3");
                f.VerifyCurrentLineContent(t, "        Error,");
                f.GoToMarker(t, "4");
                f.VerifyCurrentLineContent(t, "        Comment,");
                f.GoToMarker(t, "5");
                f.VerifyCurrentLineContent(t, "    }");
                f.GoToMarker(t, "6");
                f.VerifyCurrentLineContent(t, "    export enum foob {");
                f.GoToMarker(t, "7");
                f.VerifyCurrentLineContent(t, "        Blah = 1, Bleah = 2");
                f.GoToMarker(t, "8");
                f.VerifyCurrentLineContent(t, "    }");
                f.GoToMarker(t, "9");
                f.VerifyCurrentLineContent(t, "}");
            }
            finally
            {
                done();
            }
        });
    }

}
