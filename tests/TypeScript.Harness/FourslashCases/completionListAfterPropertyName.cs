// ported from: internal/fourslash/tests/completionListAfterPropertyName_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;
using U = TypeScript.Harness.FourslashCases.FourslashTestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class CompletionListAfterPropertyNameCase
{
    public static void TestCompletionListAfterPropertyName(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @Filename: a.ts\nclass Test1 {\n\tpublic some /*afterPropertyName*/\n}\n// @Filename: b.ts\nclass Test2 {\n\tpublic some(/*inMethodParameter*/\n}\n// @Filename: c.ts\nclass Test3 {\n\tpublic some(a/*atMethodParameter*/\n}\n// @Filename: d.ts\nclass Test4 {\n\tpublic some(a /*afterMethodParameter*/\n}\n// @Filename: e.ts\nclass Test5 {\n\tpublic some(a /*afterMethodParameterBeforeComma*/,\n}\n// @Filename: f.ts\nclass Test6 {\n\tpublic some(a, /*afterMethodParameterComma*/\n}\n// @Filename: g.ts\nclass Test7 {\n\tconstructor(/*inConstructorParameter*/\n}\n// @Filename: h.ts\nclass Test8 {\n\tconstructor(public /*inConstructorParameterAfterModifier*/\n}\n// @Filename: i.ts\nclass Test9 {\n\tconstructor(a/*atConstructorParameter*/\n}\n// @Filename: j.ts\nclass Test10 {\n\tconstructor(public/*atConstructorParameterModifier*/\n}\n// @Filename: k.ts\nclass Test11 {\n\tconstructor(public a/*atConstructorParameterAfterModifier*/\n}\n// @Filename: l.ts\nclass Test12 {\n\tconstructor(a /*afterConstructorParameter*/\n}\n// @Filename: m.ts\nclass Test13 {\n\tconstructor(a /*afterConstructorParameterBeforeComma*/,\n}\n// @Filename: n.ts\nclass Test14 {\n\tconstructor(public a, /*afterConstructorParameterComma*/\n}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyCompletions(t, new F.MarkerInput { MarkerNames = ["afterPropertyName", "inMethodParameter", "atMethodParameter", "afterMethodParameter", "afterMethodParameterBeforeComma", "afterMethodParameterComma", "afterConstructorParameter"] }, null!);
                f.VerifyCompletions(t, new F.MarkerInput { MarkerNames = ["inConstructorParameter", "inConstructorParameterAfterModifier", "atConstructorParameter", "atConstructorParameterModifier", "atConstructorParameterAfterModifier", "afterConstructorParameterComma"] }, new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = [], EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Exact = U.CompletionConstructorParameterKeywords } });
            }
            finally
            {
                done();
            }
        });
    }

}
