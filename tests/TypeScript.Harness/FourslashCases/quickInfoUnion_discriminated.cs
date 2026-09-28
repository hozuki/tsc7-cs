// ported from: internal/fourslash/tests/quickInfoUnion_discriminated_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class QuickInfoUnion_discriminatedCase
{
    public static void TestQuickInfoUnion_discriminated(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @Filename: quickInfoJsDocTags.ts\ntype U = A | B;\n\ninterface A {\n    /** Kind A */\n    kind: \"a\";\n    /** Prop A */\n    prop: number;\n}\n\ninterface B {\n    /** Kind B */\n    kind: \"b\";\n    /** Prop B */\n    prop: string;\n}\n\nconst u: U = {\n    /*uKind*/kind: \"a\",\n    /*uProp*/prop: 0,\n}\nconst u2: U = {\n    /*u2Kind*/kind: \"bogus\",\n    /*u2Prop*/prop: 1,\n};"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyQuickInfoAt(t, "uKind", "(property) A.kind: \"a\"", "Kind A");
                f.VerifyQuickInfoAt(t, "uProp", "(property) A.prop: number", "Prop A");
                f.VerifyQuickInfoAt(t, "u2Kind", "(property) kind: \"bogus\"", "");
                f.VerifyQuickInfoAt(t, "u2Prop", "(property) prop: number", "");
            }
            finally
            {
                done();
            }
        });
    }

}
