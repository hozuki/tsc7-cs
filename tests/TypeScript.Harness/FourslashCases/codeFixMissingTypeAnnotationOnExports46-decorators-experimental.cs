// ported from: internal/fourslash/tests/codeFixMissingTypeAnnotationOnExports46-decorators-experimental_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class CodeFixMissingTypeAnnotationOnExports46_decorators_experimentalCase
{
    public static void TestCodeFixMissingTypeAnnotationOnExports46_decorators_experimental(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @isolatedDeclarations: true\n// @declaration: true\n// @experimentalDecorators: true\n// @Filename: /code.ts\nfunction classDecorator<T extends Function>() { return (target: T) => target; }\nfunction methodDecorator() { return (target: any, key: string, descriptor: PropertyDescriptor) => descriptor;}\nfunction parameterDecorator() { return (target: any, key: string, idx: number) => {};}\nfunction getterDecorator() { return (target: any, key: string) => {}; }\nfunction setterDecorator() { return (target: any, key: string) => {}; }\nfunction fieldDecorator()  { return (target: any, key: string) => {}; }\nfunction foo() { return 42; }\n\n@classDecorator()\nexport class A {\n  @methodDecorator()\n  sum(...args: number[]) {\n    return args.reduce((a, b) => a + b, 0);\n  }\n  getSelf() {\n    return this;\n  }\n  passParameter(@parameterDecorator() param = foo()) {}\n  @getterDecorator()\n  get a() {\n    return foo();\n  }\n  @setterDecorator()\n  set a(value) {}\n  @fieldDecorator() classProp = foo();\n}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyCodeFixAll(t, new F.VerifyCodeFixAllOptions { FixID = "fixMissingTypeAnnotationOnExports", NewFileContent = "function classDecorator<T extends Function>() { return (target: T) => target; }\nfunction methodDecorator() { return (target: any, key: string, descriptor: PropertyDescriptor) => descriptor;}\nfunction parameterDecorator() { return (target: any, key: string, idx: number) => {};}\nfunction getterDecorator() { return (target: any, key: string) => {}; }\nfunction setterDecorator() { return (target: any, key: string) => {}; }\nfunction fieldDecorator()  { return (target: any, key: string) => {}; }\nfunction foo() { return 42; }\n\n@classDecorator()\nexport class A {\n  @methodDecorator()\n  sum(...args: number[]): number {\n    return args.reduce((a, b) => a + b, 0);\n  }\n  getSelf(): this {\n    return this;\n  }\n  passParameter(@parameterDecorator() param: number = foo()): void {}\n  @getterDecorator()\n  get a(): number {\n    return foo();\n  }\n  @setterDecorator()\n  set a(value: number) {}\n  @fieldDecorator() classProp: number = foo();\n}" });
            }
            finally
            {
                done();
            }
        });
    }

}
