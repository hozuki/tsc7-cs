// ported from: internal/fourslash/tests/genericsFormatting_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class GenericsFormattingCase
{
    public static void TestGenericsFormatting(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("/*inClassDeclaration*/class Foo   <    T1   ,  T2    >  {\n/*inMethodDeclaration*/    public method    <   T3,    T4   >   ( a: T1,   b: Array    < T4 > ):   Map < T1  ,   T2, Array < T3    >    > {\n    }\n}\n/*typeArguments*/var foo = new Foo   <  number, Array <   number  >   >  (  );\n/*typeArgumentsWithTypeLiterals*/foo = new Foo  <  {   bar  :  number }, Array   < {   baz :  string   }  >  >  (  );\n\ninterface IFoo {\n/*inNewSignature*/new < T  > ( a: T);\n/*inOptionalMethodSignature*/op?< T , M > (a: T, b : M );\n}\n\nfoo()<number, string, T >();\n(a + b)<number, string, T >();\n\n/*inFunctionDeclaration*/function bar <T> () {\n/*inClassExpression*/    return class  <  T2 > {\n    }\n}\n/*expressionWithTypeArguments*/class A < T > extends bar <  T >( )  <  T > {\n}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.FormatDocument(t, "");
                f.GoToMarker(t, "inClassDeclaration");
                f.VerifyCurrentLineContent(t, "class Foo<T1, T2> {");
                f.GoToMarker(t, "inMethodDeclaration");
                f.VerifyCurrentLineContent(t, "    public method<T3, T4>(a: T1, b: Array<T4>): Map<T1, T2, Array<T3>> {");
                f.GoToMarker(t, "typeArguments");
                f.VerifyCurrentLineContent(t, "var foo = new Foo<number, Array<number>>();");
                f.GoToMarker(t, "typeArgumentsWithTypeLiterals");
                f.VerifyCurrentLineContent(t, "foo = new Foo<{ bar: number }, Array<{ baz: string }>>();");
                f.GoToMarker(t, "inNewSignature");
                f.VerifyCurrentLineContent(t, "    new <T>(a: T);");
                f.GoToMarker(t, "inOptionalMethodSignature");
                f.VerifyCurrentLineContent(t, "    op?<T, M>(a: T, b: M);");
                f.GoToMarker(t, "inFunctionDeclaration");
                f.VerifyCurrentLineContent(t, "function bar<T>() {");
                f.GoToMarker(t, "inClassExpression");
                f.VerifyCurrentLineContent(t, "    return class <T2> {");
                f.GoToMarker(t, "expressionWithTypeArguments");
                f.VerifyCurrentLineContent(t, "class A<T> extends bar<T>()<T> {");
            }
            finally
            {
                done();
            }
        });
    }

}
