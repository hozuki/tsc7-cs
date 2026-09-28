// ported from: internal/fourslash/tests/codeFixClassImplementInterfaceComments_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class CodeFixClassImplementInterfaceCommentsCase
{
    public static void TestCodeFixClassImplementInterfaceComments(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @lib: es2017\nnamespace N {\n    /**enum prefix */\n    export enum /**enum identifier prefix */ E /**open-brace prefix*/ {\n    /* literal prefix */ a /** comma prefix */,\n    /* literal prefix */ b /** comma prefix */,\n    /* literal prefix */ c\n    /** close brace prefix */ }\n    /** interface prefix */\n    export interface /**interface name prefix */ I /**open-brace prefix*/ {\n    /** property prefix */ a /** colon prefix */: /** enum literal prefix 1*/ E /** dot prefix */. /** enum literal prefix 2*/a;\n    /** property prefix */ b /** colon prefix */: /** enum prefix */ E;\n    /**method signature prefix */foo /**open angle prefix */< /**type parameter name prefix */ X /** closing angle prefix */> /**open paren prefix */(/** parameter prefix */ a/** colon prefix */: /** parameter type prefix */ X /** close paren prefix */) /** colon prefix */: /** return type prefix */ string /** semicolon prefix */;\n        /**close-brace prefix*/ }\n/**close-brace prefix*/ }\nclass C implements N.I {}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyCodeFix(t, new F.VerifyCodeFixOptions { Description = "Implement interface 'N.I'", NewFileContent = "namespace N {\n    /**enum prefix */\n    export enum /**enum identifier prefix */ E /**open-brace prefix*/ {\n    /* literal prefix */ a /** comma prefix */,\n    /* literal prefix */ b /** comma prefix */,\n    /* literal prefix */ c\n    /** close brace prefix */ }\n    /** interface prefix */\n    export interface /**interface name prefix */ I /**open-brace prefix*/ {\n    /** property prefix */ a /** colon prefix */: /** enum literal prefix 1*/ E /** dot prefix */. /** enum literal prefix 2*/a;\n    /** property prefix */ b /** colon prefix */: /** enum prefix */ E;\n    /**method signature prefix */foo /**open angle prefix */< /**type parameter name prefix */ X /** closing angle prefix */> /**open paren prefix */(/** parameter prefix */ a/** colon prefix */: /** parameter type prefix */ X /** close paren prefix */) /** colon prefix */: /** return type prefix */ string /** semicolon prefix */;\n        /**close-brace prefix*/ }\n/**close-brace prefix*/ }\nclass C implements N.I {\n    a: N.E.a;\n    b: N.E;\n    foo<X /** closing angle prefix */>(a: X /** close paren prefix */): string /** semicolon prefix */ {\n        throw new Error(\"Method not implemented.\");\n    }\n}", Index = 0 });
            }
            finally
            {
                done();
            }
        });
    }

}
