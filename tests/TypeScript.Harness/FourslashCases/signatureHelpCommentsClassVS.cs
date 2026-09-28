// ported from: internal/fourslash/tests/signatureHelpCommentsClassVS_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using LSP = TypeScript.LsProto;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class SignatureHelpCommentsClassVSCase
{
    public static void TestSignatureHelpCommentsClassVS(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("/** This is class c2 without constructor*/\nclass c2 {\n}\nvar i2 = new c2(/*3*/);\nvar i2_c = c2;\nclass c3 {\n    /** Constructor comment*/\n    constructor() {\n    }\n}\nvar i3 = new c3(/*8*/);\nvar i3_c = c3;\n/** Class comment*/\nclass c4 {\n    /** Constructor comment*/\n    constructor() {\n    }\n}\nvar i4 = new c4(/*13*/);\nvar i4_c = c4;\n/** Class with statics*/\nclass c5 {\n    static s1: number;\n}\nvar i5 = new c5(/*18*/);\nvar i5_c = c5;\n/** class with statics and constructor*/\nclass c6 {\n    /** s1 comment*/\n    static s1: number;\n    /** constructor comment*/\n    constructor() {\n    }\n}\nvar i6 = new c6(/*23*/);\nvar i6_c = c6;\n\nclass a {\n    /**\n    constructor for a\n    @param a this is my a\n    */\n    constructor(a: string) {\n    }\n}\nnew a(/*27*/\"Hello\");\nnamespace m {\n    export namespace m2 {\n        /** class comment */\n        export class c1 {\n            /** constructor comment*/\n            constructor() {\n            }\n        }\n    }\n}\nvar myVar = new m.m2.c1();"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, new LSP.ClientCapabilities { VSSupportsVisualStudioExtensions = true }, content);
            try
            {
                f.VerifyBaselineSignatureHelp(t);
            }
            finally
            {
                done();
            }
        });
    }

}
