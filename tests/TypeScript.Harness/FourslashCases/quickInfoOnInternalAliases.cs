// ported from: internal/fourslash/tests/quickInfoOnInternalAliases_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class QuickInfoOnInternalAliasesCase
{
    public static void TestQuickInfoOnInternalAliases(TSU.IHarnessTest t)
    {
        t.Skip("Known failing fourslash test");
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("/** Module comment*/\nexport namespace m1 {\n    /** m2 comments*/\n    export namespace m2 {\n        /** class comment;*/\n        export class /*1*/c {\n        };\n    }\n    export function foo() {\n    }\n}\n/**This is on import declaration*/\nimport /*2*/internalAlias = m1.m2./*3*/c;\nvar /*4*/newVar = new /*5*/internalAlias();\nvar /*6*/anotherAliasVar = /*7*/internalAlias;\nimport /*8*/internalFoo = m1./*9*/foo;\nvar /*10*/callVar = /*11*/internalFoo();\nvar /*12*/anotherAliasFoo = /*13*/internalFoo;"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyQuickInfoAt(t, "1", "class m1.m2.c", "class comment;");
                f.VerifyQuickInfoAt(t, "2", "(alias) class internalAlias\nimport internalAlias = m1.m2.c", "This is on import declaration");
                f.VerifyQuickInfoAt(t, "3", "class m1.m2.c", "class comment;");
                f.VerifyQuickInfoAt(t, "4", "var newVar: internalAlias", "");
                f.VerifyQuickInfoAt(t, "5", "(alias) new internalAlias(): internalAlias\nimport internalAlias = m1.m2.c", "This is on import declaration");
                f.VerifyQuickInfoAt(t, "6", "var anotherAliasVar: typeof internalAlias", "");
                f.VerifyQuickInfoAt(t, "7", "(alias) class internalAlias\nimport internalAlias = m1.m2.c", "This is on import declaration");
                f.VerifyQuickInfoAt(t, "8", "(alias) function internalFoo(): void\nimport internalFoo = m1.foo", "");
                f.VerifyQuickInfoAt(t, "9", "function m1.foo(): void", "");
                f.VerifyQuickInfoAt(t, "10", "var callVar: void", "");
                f.VerifyQuickInfoAt(t, "11", "(alias) internalFoo(): void\nimport internalFoo = m1.foo", "");
                f.VerifyQuickInfoAt(t, "12", "var anotherAliasFoo: () => void", "");
                f.VerifyQuickInfoAt(t, "13", "(alias) function internalFoo(): void\nimport internalFoo = m1.foo", "");
            }
            finally
            {
                done();
            }
        });
    }

}
