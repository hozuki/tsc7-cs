// ported from: internal/fourslash/tests/quickInfoGenericCombinators2_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class QuickInfoGenericCombinators2Case
{
    public static void TestQuickInfoGenericCombinators2(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("interface Collection<T, U> {\n   length: number;\n   add(x: T, y: U): void ;\n   remove(x: T, y: U): boolean;\n}\n\ninterface Combinators {\n   map<T, U, V>(c: Collection<T, U>, f: (x: T, y: U) => V): Collection<T, V>;\n   map<T, U>(c: Collection<T, U>, f: (x: T, y: U) => any): Collection<any, any>;\n}\n\nclass A {\n   foo<T>(): T { return null; }\n}\n\nclass B<T> {\n   foo(x: T): T { return null; }\n}\n\nvar c1: Collection<any, any>;\nvar c2: Collection<number, string>;\nvar c3: Collection<Collection<number, number>, string>;\nvar c4: Collection<number, A>;\nvar c5: Collection<number, B<any>>;\n\nvar _: Combinators;\n// param help on open paren for arg 2 should show 'number' not T or 'any'\n// x should be contextually typed to number\nvar rf1 = (x: number, y: string) => { return x.toFixed() };\nvar rf2 = (x: Collection<number, number>, y: string) => { return x.length };\nvar rf3 = (x: number, y: A) => { return y.foo() };\n\nvar /*9*/r1a  = _.map/*1c*/(c2, (/*1a*/x, /*1b*/y) => { return x.toFixed() });\nvar /*10*/r1b = _.map(c2, rf1);\n\nvar /*11*/r2a = _.map(c3, (/*2a*/x, /*2b*/y) => { return x.length });\nvar /*12*/r2b = _.map(c3, rf2);\n\nvar /*13*/r3a = _.map(c4, (/*3a*/x, /*3b*/y) => { return y.foo() });\nvar /*14*/r3b = _.map(c4, rf3);\n\nvar /*15*/r4a = _.map(c5, (/*4a*/x, /*4b*/y) => { return y.foo() });\n\nvar /*17*/r5a = _.map<number, string, Date>(c2, /*17error1*/(/*5a*/x, /*5b*/y) => { return x.toFixed() }/*17error2*/); \nvar rf1b = (x: number, y: string) => { return new Date() };\nvar /*18*/r5b = _.map<number, string, Date>(c2, rf1b);\n\nvar /*19*/r6a = _.map<Collection<number, number>, string, Date>(c3, (/*6a*/x,/*6b*/y) => { return new Date(); });\nvar rf2b = (x: Collection<number, number>, y: string) => { return new Date(); };\nvar /*20*/r6b = _.map<Collection<number, number>, string, Date>(c3, rf2b);\n\nvar /*21*/r7a = _.map<number, A, string>(c4, (/*7a*/x,/*7b*/y) => { return y.foo() });\nvar /*22*/r7b = _.map<number, A, string>(c4, /*22error1*/rf3/*22error2*/);\n\nvar /*23*/r8a = _.map<number, /*error1*/B/*error2*/, string>(c5, (/*8a*/x,/*8b*/y) => { return y.foo() }); "u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyQuickInfoAt(t, "2a", "(parameter) x: Collection<number, number>", "");
                f.VerifyQuickInfoAt(t, "2b", "(parameter) y: string", "");
                f.VerifyQuickInfoAt(t, "3a", "(parameter) x: number", "");
                f.VerifyQuickInfoAt(t, "3b", "(parameter) y: A", "");
                f.VerifyQuickInfoAt(t, "4a", "(parameter) x: number", "");
                f.VerifyQuickInfoAt(t, "4b", "(parameter) y: B<any>", "");
                f.VerifyQuickInfoAt(t, "5a", "(parameter) x: number", "");
                f.VerifyQuickInfoAt(t, "5b", "(parameter) y: string", "");
                f.VerifyQuickInfoAt(t, "6a", "(parameter) x: Collection<number, number>", "");
                f.VerifyQuickInfoAt(t, "6b", "(parameter) y: string", "");
                f.VerifyQuickInfoAt(t, "7a", "(parameter) x: number", "");
                f.VerifyQuickInfoAt(t, "7b", "(parameter) y: A", "");
                f.VerifyQuickInfoAt(t, "8a", "(parameter) x: number", "");
                f.VerifyQuickInfoAt(t, "8b", "(parameter) y: any", "");
                f.VerifyQuickInfoAt(t, "9", "var r1a: Collection<number, string>", "");
                f.VerifyQuickInfoAt(t, "10", "var r1b: Collection<number, string>", "");
                f.VerifyQuickInfoAt(t, "11", "var r2a: Collection<Collection<number, number>, number>", "");
                f.VerifyQuickInfoAt(t, "12", "var r2b: Collection<Collection<number, number>, number>", "");
                f.VerifyQuickInfoAt(t, "13", "var r3a: Collection<number, unknown>", "");
                f.VerifyQuickInfoAt(t, "14", "var r3b: Collection<number, unknown>", "");
                f.VerifyQuickInfoAt(t, "15", "var r4a: Collection<number, any>", "");
                f.VerifyQuickInfoAt(t, "17", "var r5a: Collection<number, Date>", "");
                f.VerifyQuickInfoAt(t, "18", "var r5b: Collection<number, Date>", "");
                f.VerifyQuickInfoAt(t, "19", "var r6a: Collection<Collection<number, number>, Date>", "");
                f.VerifyQuickInfoAt(t, "20", "var r6b: Collection<Collection<number, number>, Date>", "");
                f.VerifyQuickInfoAt(t, "21", "var r7a: Collection<number, string>", "");
                f.VerifyQuickInfoAt(t, "22", "var r7b: Collection<number, string>", "");
                f.VerifyQuickInfoAt(t, "23", "var r8a: Collection<number, string>", "");
                f.VerifyErrorExistsBetweenMarkers(t, "error1", "error2");
                f.VerifyErrorExistsBetweenMarkers(t, "17error1", "17error2");
                f.VerifyErrorExistsBetweenMarkers(t, "22error1", "22error2");
            }
            finally
            {
                done();
            }
        });
    }

}
