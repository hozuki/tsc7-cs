// ported from: internal/fourslash/tests/genericsFormattingMultiline_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class GenericsFormattingMultilineCase
{
    public static void TestGenericsFormattingMultiline(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("\nclass Foo   <   \n T1   extends unknown,\n  T2   \n    > {\n    public method    <  \n T3,\n    >   (a: T1,   b: Array    < \n     string \n     > ):   Map <\n          T1 ,\n      Array < T3    >  \n          > { throw new Error(); } \n}\n\ninterface IFoo<\n       T, \n  > {\n    new < T\n      > ( a: T);\n    op?< \n   T,\n      M\n    > (a: T, b : M );\n    <\n     T,\n      >(x: T): T;\n}\n\ntype foo<\n  T\n   > = Foo   <\n  number, Array <   number  >  > ;\n\nfunction bar <\nT, U extends T\n >  () {\n    return class  < \n       T2,\n  > {\n    }\n}\n\nbar<\nstring, \n     \"s\"\n     > ();\n\ndeclare const func: <\nT   extends number[], \n                       > (x: T) => new <\n       U\n                          > () => U;\n\nclass A < T > extends bar <  \n        T,number\n >( )  <  T\n     > {\n}\n\nfunction s<T, U>(x: TemplateStringsArray, ...args: any[]) { return x.join(); }\n\nconst t = s<\n      number , \n  string[] & ArrayLike<any>\n      >`abc${1}def` ;\n"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.FormatDocument(t, "");
                f.VerifyCurrentFileContent(t, "\nclass Foo<\n    T1 extends unknown,\n    T2\n> {\n    public method<\n        T3,\n    >(a: T1, b: Array<\n        string\n    >): Map<\n        T1,\n        Array<T3>\n    > { throw new Error(); }\n}\n\ninterface IFoo<\n    T,\n> {\n    new <T\n    >(a: T);\n    op?<\n        T,\n        M\n    >(a: T, b: M);\n    <\n        T,\n    >(x: T): T;\n}\n\ntype foo<\n    T\n> = Foo<\n    number, Array<number>>;\n\nfunction bar<\n    T, U extends T\n>() {\n    return class <\n        T2,\n    > {\n    }\n}\n\nbar<\n    string,\n    \"s\"\n>();\n\ndeclare const func: <\n    T extends number[],\n> (x: T) => new <\n    U\n> () => U;\n\nclass A<T> extends bar<\n    T, number\n>()<T\n> {\n}\n\nfunction s<T, U>(x: TemplateStringsArray, ...args: any[]) { return x.join(); }\n\nconst t = s<\n    number,\n    string[] & ArrayLike<any>\n>`abc${1}def`;\n");
            }
            finally
            {
                done();
            }
        });
    }

}
