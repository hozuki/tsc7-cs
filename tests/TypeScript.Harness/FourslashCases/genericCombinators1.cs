// ported from: internal/fourslash/tests/genericCombinators1_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class GenericCombinators1Case
{
    public static void TestGenericCombinators1(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("interface Collection<T> {\n    length: number;\n    add(x: T): void;\n    remove(x: T): boolean;\n}\ninterface Combinators {\n    map<T, U>(c: Collection<T>, f: (x: T) => U): Collection<U>;\n    map<T>(c: Collection<T>, f: (x: T) => any): Collection<any>;\n}\nclass A {\n    foo<T>() { return this; }\n}\nclass B<T> {\n    foo(x: T): T { return null; }\n}\nvar c2: Collection<number>;\nvar c3: Collection<Collection<number>>;\nvar c4: Collection<A>;\nvar c5: Collection<B<any>>;\nvar _: Combinators;\nvar rf1 = (x: number) => { return x.toFixed() };\nvar rf2 = (x: Collection<number>) => { return x.length };\nvar rf3 = (x: A) => { return x.foo() };\nvar /*9*/r1a = _.map(c2, (/*1*/x) => { return x.toFixed() });\nvar /*10*/r1b = _.map(c2, rf1);\nvar /*11*/r2a = _.map(c3, (/*2*/x: Collection<number>) => { return x.length });\nvar /*12*/r2b = _.map(c3, rf2);\nvar /*13*/r3a = _.map(c4, (/*3*/x) => { return x.foo() });\nvar /*14*/r3b = _.map(c4, rf3);\nvar /*15*/r4a = _.map(c5, (/*4*/x) => { return x.foo(1) });\nvar /*17*/r5a = _.map<number, string>(c2, (/*5*/x) => { return x.toFixed() });\nvar /*18*/r5b = _.map<number, string>(c2, rf1);\nvar /*19*/r6a = _.map<Collection<number>, number>(/*6*/c3, (x: Collection<number>) => { return x.length });\nvar /*20*/r6b = _.map<Collection<number>, number>(c3, rf2);\nvar /*21*/r7a = _.map<A, A>(c4, (/*7*/x: A) => { return x.foo() });\nvar /*22*/r7b = _.map<A, A>(c4, rf3);\nvar /*23*/r8a = _.map</*error1*/B/*error2*/, string>(c5, (/*8*/x) => { return x.foo() });"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyQuickInfoAt(t, "1", "(parameter) x: number", "");
                f.VerifyQuickInfoAt(t, "2", "(parameter) x: Collection<number>", "");
                f.VerifyQuickInfoAt(t, "3", "(parameter) x: A", "");
                f.VerifyQuickInfoAt(t, "4", "(parameter) x: B<any>", "");
                f.VerifyQuickInfoAt(t, "5", "(parameter) x: number", "");
                f.VerifyQuickInfoAt(t, "6", "var c3: Collection<Collection<number>>", "");
                f.VerifyQuickInfoAt(t, "7", "(parameter) x: A", "");
                f.VerifyQuickInfoAt(t, "8", "(parameter) x: any", "");
                f.VerifyQuickInfoAt(t, "9", "var r1a: Collection<string>", "");
                f.VerifyQuickInfoAt(t, "10", "var r1b: Collection<string>", "");
                f.VerifyQuickInfoAt(t, "11", "var r2a: Collection<number>", "");
                f.VerifyQuickInfoAt(t, "12", "var r2b: Collection<number>", "");
                f.VerifyQuickInfoAt(t, "13", "var r3a: Collection<A>", "");
                f.VerifyQuickInfoAt(t, "14", "var r3b: Collection<A>", "");
                f.VerifyQuickInfoAt(t, "15", "var r4a: Collection<any>", "");
                f.VerifyQuickInfoAt(t, "17", "var r5a: Collection<string>", "");
                f.VerifyQuickInfoAt(t, "18", "var r5b: Collection<string>", "");
                f.VerifyQuickInfoAt(t, "19", "var r6a: Collection<number>", "");
                f.VerifyQuickInfoAt(t, "20", "var r6b: Collection<number>", "");
                f.VerifyQuickInfoAt(t, "21", "var r7a: Collection<A>", "");
                f.VerifyQuickInfoAt(t, "22", "var r7b: Collection<A>", "");
                f.VerifyQuickInfoAt(t, "23", "var r8a: Collection<string>", "");
                f.VerifyErrorExistsBetweenMarkers(t, "error1", "error2");
            }
            finally
            {
                done();
            }
        });
    }

}
