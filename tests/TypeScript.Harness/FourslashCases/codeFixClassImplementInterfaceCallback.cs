// ported from: internal/fourslash/tests/codeFixClassImplementInterfaceCallback_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class CodeFixClassImplementInterfaceCallbackCase
{
    public static void TestCodeFixClassImplementInterfaceCallback(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("interface IFoo1 {\n    parse(reviver: () => any): void;\n}\n\nclass Foo1 implements IFoo1 {\n}\n\ninterface IFoo2 {\n    parse(reviver: { (): any }): void;\n}\n\nclass Foo2 implements IFoo2 {\n}\n\ninterface IFoo3 {\n    parse(reviver: new () => any): void;\n}\n\nclass Foo3 implements IFoo3 {\n}\n\ninterface IFoo4 {\n    parse(reviver: { new (): any }): void;\n}\n\nclass Foo4 implements IFoo4 {\n}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyCodeFixAll(t, new F.VerifyCodeFixAllOptions { FixID = "fixClassIncorrectlyImplementsInterface", NewFileContent = "interface IFoo1 {\n    parse(reviver: () => any): void;\n}\n\nclass Foo1 implements IFoo1 {\n    parse(reviver: () => any): void {\n        throw new Error(\"Method not implemented.\");\n    }\n}\n\ninterface IFoo2 {\n    parse(reviver: { (): any }): void;\n}\n\nclass Foo2 implements IFoo2 {\n    parse(reviver: { (): any; }): void {\n        throw new Error(\"Method not implemented.\");\n    }\n}\n\ninterface IFoo3 {\n    parse(reviver: new () => any): void;\n}\n\nclass Foo3 implements IFoo3 {\n    parse(reviver: new () => any): void {\n        throw new Error(\"Method not implemented.\");\n    }\n}\n\ninterface IFoo4 {\n    parse(reviver: { new (): any }): void;\n}\n\nclass Foo4 implements IFoo4 {\n    parse(reviver: { new(): any; }): void {\n        throw new Error(\"Method not implemented.\");\n    }\n}" });
            }
            finally
            {
                done();
            }
        });
    }

}
