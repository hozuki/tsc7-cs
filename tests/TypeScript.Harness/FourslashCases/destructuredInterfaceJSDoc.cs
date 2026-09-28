// ported from: internal/fourslash/tests/destructuredInterfaceJSDoc_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class DestructuredInterfaceJSDocCase
{
    public static void TestDestructuredInterfaceJSDoc(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("\ninterface FooBar {\n    /** foo comment */\n    foo: number;\n    /** bar comment */\n    bar: string;\n    /** baz comment */\n    baz: string;\n}\n\ndeclare const fubar: FooBar;\n\nconst {/*1*/foo, /*2*/bar, /*3*/baz: /*4*/biz} = fubar;\n"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyBaselineHover(t);
            }
            finally
            {
                done();
            }
        });
    }

    public static void TestDestructuredInterfaceJSDocWithRename(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("\ninterface FooBar {\n    /** foo comment */\n    foo: number;\n    /** bar comment */\n    bar: string;\n}\n\ndeclare const fubar: FooBar;\n\nconst {foo: /*1*/myFoo, bar: /*2*/myBar} = fubar;\n"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyBaselineHover(t);
            }
            finally
            {
                done();
            }
        });
    }

    public static void TestDestructuredWithOwnJSDoc(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("\ninterface Foo {\n    /** This is bar from the interface */\n    bar: string;\n    /** This is baz from the interface */\n    baz: number;\n}\n\ndeclare var foo: Foo;\n\n/** Comment on the variable statement. */\nconst {\n    /** Comment on bar destructuring. */ /*1*/bar,\n    /** Comment on baz destructuring. */ /*2*/baz\n} = foo;\n"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyBaselineHover(t);
            }
            finally
            {
                done();
            }
        });
    }

}
