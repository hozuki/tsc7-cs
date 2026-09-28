// ported from: internal/fourslash/tests/codeFixMissingTypeAnnotationOnExports45-decorators_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class CodeFixMissingTypeAnnotationOnExports45_decoratorsCase
{
    public static void TestCodeFixMissingTypeAnnotationOnExports45_decorators(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @isolatedDeclarations: true\n// @declaration: true\n// @Filename: /code.ts\nfunction classDecorator<T extends Function> (value: T, context: ClassDecoratorContext) {}\nfunction methodDecorator<This> (\n  target: (...args: number[])=> number,\n  context: ClassMethodDecoratorContext<This, (this: This, ...args: number[]) => number>) {}\nfunction getterDecorator(value: Function, context: ClassGetterDecoratorContext) {}\nfunction setterDecorator(value: Function, context: ClassSetterDecoratorContext) {}\nfunction fieldDecorator(value: undefined, context: ClassFieldDecoratorContext) {}\nfunction foo() { return 42;}\n\n@classDecorator\nexport class A {\n  @methodDecorator\n  sum(...args: number[]) {\n    return args.reduce((a, b) => a + b, 0);\n  }\n  getSelf() {\n    return this;\n  }\n  @getterDecorator\n  get a() {\n    return foo();\n  }\n  @setterDecorator\n  set a(value) {}\n\n  @fieldDecorator classProp = foo();\n}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyCodeFixAll(t, new F.VerifyCodeFixAllOptions { FixID = "fixMissingTypeAnnotationOnExports", NewFileContent = "function classDecorator<T extends Function> (value: T, context: ClassDecoratorContext) {}\nfunction methodDecorator<This> (\n  target: (...args: number[])=> number,\n  context: ClassMethodDecoratorContext<This, (this: This, ...args: number[]) => number>) {}\nfunction getterDecorator(value: Function, context: ClassGetterDecoratorContext) {}\nfunction setterDecorator(value: Function, context: ClassSetterDecoratorContext) {}\nfunction fieldDecorator(value: undefined, context: ClassFieldDecoratorContext) {}\nfunction foo() { return 42;}\n\n@classDecorator\nexport class A {\n  @methodDecorator\n  sum(...args: number[]): number {\n    return args.reduce((a, b) => a + b, 0);\n  }\n  getSelf(): this {\n    return this;\n  }\n  @getterDecorator\n  get a(): number {\n    return foo();\n  }\n  @setterDecorator\n  set a(value: number) {}\n\n  @fieldDecorator classProp: number = foo();\n}" });
            }
            finally
            {
                done();
            }
        });
    }

}
