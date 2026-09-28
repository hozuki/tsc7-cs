// ported from: internal/fourslash/tests/quickInfoGenerics_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class QuickInfoGenericsCase
{
    public static void TestQuickInfoGenerics(TSU.IHarnessTest t)
    {
        t.Skip("Known failing fourslash test");
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("class Con/*1*/tainer<T> {\n    x: T;\n}\ninterface IList</*2*/T> {\n    getItem(i: number): /*3*/T;\n}\nclass List2</*4*/T extends IList<number>> implements IList<T> {\n    private __it/*6*/em: /*5*/T[];\n    public get/*7*/Item(i: number) {\n        return this.__item[i];\n    }\n    public /*8*/method</*9*/S extends IList<T>>(s: S, p: /*10*/T[]) {\n        return s;\n    }\n}\nfunction foo4</*11*/T extends Date>(test: T): T;\nfunction foo4</*12*/S extends string>(test: S): S;\nfunction foo4(test: any): any;\nfunction foo4</*13*/T extends Date>(test: any): any { return null; }\nvar x: List2<IList<number>>;\nvar y = x./*14*/getItem(10);\nvar x2: IList<IList<number>>;\nvar x3: IList<number>;\nvar y2 = x./*15*/method(x2, [x3, x3]);"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyQuickInfoAt(t, "1", "class Container<T>", "");
                f.VerifyQuickInfoAt(t, "2", "(type parameter) T in IList<T>", "");
                f.VerifyQuickInfoAt(t, "3", "(type parameter) T in IList<T>", "");
                f.VerifyQuickInfoAt(t, "4", "(type parameter) T in List2<T extends IList<number>>", "");
                f.VerifyQuickInfoAt(t, "5", "(type parameter) T in List2<T extends IList<number>>", "");
                f.VerifyQuickInfoAt(t, "6", "(property) List2<T extends IList<number>>.__item: T[]", "");
                f.VerifyQuickInfoAt(t, "7", "(method) List2<T extends IList<number>>.getItem(i: number): T", "");
                f.VerifyQuickInfoAt(t, "8", "(method) List2<T extends IList<number>>.method<S extends IList<T>>(s: S, p: T[]): S", "");
                f.VerifyQuickInfoAt(t, "9", "(type parameter) S in List2<T extends IList<number>>.method<S extends IList<T>>(s: S, p: T[]): S", "");
                f.VerifyQuickInfoAt(t, "10", "(type parameter) T in List2<T extends IList<number>>", "");
                f.VerifyQuickInfoAt(t, "11", "(type parameter) T in foo4<T extends Date>(test: T): T", "");
                f.VerifyQuickInfoAt(t, "12", "(type parameter) S in foo4<S extends string>(test: S): S", "");
                f.VerifyQuickInfoAt(t, "13", "(type parameter) T in foo4<T extends Date>(test: any): any", "");
                f.VerifyQuickInfoAt(t, "14", "(method) List2<IList<number>>.getItem(i: number): IList<number>", "");
                f.VerifyQuickInfoAt(t, "15", "(method) List2<IList<number>>.method<IList<IList<number>>>(s: IList<IList<number>>, p: IList<number>[]): IList<IList<number>>", "");
            }
            finally
            {
                done();
            }
        });
    }

}
