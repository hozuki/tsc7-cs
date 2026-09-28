// ported from: internal/fourslash/tests/autoCloseTagsWithTriviaAndComplexNames_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class AutoCloseTagsWithTriviaAndComplexNamesCase
{
    public static void TestAutoCloseTagsWithTriviaAndComplexNames(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @noLib: true\n\n// @Filename: /0.tsx\n// JSDoc\nconst x = <\n\t/** hello world! */\n\tdiv /** hello world! */\n\t>/*0*/\n\n// @Filename: /1.tsx\n// Single-line comments\nconst x =\n\t<\n\t// hello world!\n\tdiv // hello world!\n\t>/*1*/\n\n// @Filename: /2.tsx\n// Namespaced tag\nconst x =\n\t<ns:sometag>/*2*/\n\n// @Filename: /3.tsx\n// Namespace with single-line comments\nconst x = <\n\t// pre-ns\t\n\tns\n\t// pre-colon\n\t:\n\t// post-colon\n\tsometag\n\t// post-id\n\t>/*3*/\n\n// @Filename: /4.tsx\n// UppercaseComponent-named tag\nconst x = <SomeComponent>/*4*/\n\n// @Filename: /5.tsx\n// propertyAccess.Component-named tag\nconst x = <\n\tsomeModule\n\t.\n\tSomeComponent\n>/*5*/\n\n// @Filename: /6.tsx\n// propertyAccess.Component-named tag with single-line comments\nconst x =\n\t<\n\t// pre-object\n\tsomeModule\n\t// pre-dot\n\t.\n\t// post-dot\n\tSomeComponent\n\t// post-id\n\t>/*6*/;\n\n// @Filename: /7.tsx\n// Generic propertyAccess.Component-named tag\nconst x =\n\t<\n\tsomeModule.SomeComponent<string>\n\tprop=\"stringValue\"\n\t>/*7*/;\n\n// @Filename: /8.tsx\n// Namespaced tag with hyphens\nconst x =\n\t<my-namespace:my-tag>/*8*/\n\n// @Filename: /9.tsx\n// Generic tag with no attributes\nconst x = <SomeComponent<number>>/*9*/\n\n// @Filename: /10.tsx\n// Tag name containing $ (must be snippet-escaped)\nconst x = <$Foo>/*10*/\n"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyBaselineClosingTags(t);
            }
            finally
            {
                done();
            }
        });
    }

}
