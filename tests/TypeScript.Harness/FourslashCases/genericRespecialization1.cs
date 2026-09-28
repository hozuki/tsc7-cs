// ported from: internal/fourslash/tests/genericRespecialization1_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class GenericRespecialization1Case
{
    public static void TestGenericRespecialization1(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @strict: false\nclass Food {\n    private amount: number;\n    constructor(public name: string) {\n        this.amount = 100;\n    }\n    public eat(amountToEat: number): boolean {\n        this.amount -= amountToEat;\n        if (this.amount <= 0) {\n            this.amount = 0;\n            return false;\n        }\n        else {\n            return true;\n        }\n    }\n}\nclass IceCream extends Food {\n    private isDairyFree: boolean;\n    constructor(public flavor: string) {\n        super(\"Ice Cream\");\n    }\n}\nclass Cookie extends Food {\n    constructor(public flavor: string, public isGlutenFree: boolean) {\n        super(\"Cookie\");\n    }\n}\nclass Slug {\n    // This is NOT a food!!!\n}\nclass GenericMonster<T extends Food, V> {\n    private name: string;\n    private age: number;\n    private isFriendly: boolean;\n    constructor(name: string, age: number, isFriendly: boolean, private food: T, public variant: V) {\n        this.name = name;\n        this.age = age;\n        this.isFriendly = isFriendly;\n    }\n    public getFood(): T {\n        return this.food;\n    }\n    public getVariant(): V {\n        return this.variant;\n    }\n    public eatFood(amountToEat: number): boolean {\n        return this.food.eat(amountToEat);\n    }\n    public sayGreeting(): string {\n        return (\"My name is \" + this.name + \", and my age is \" + this.age + \".  I enjoy eating \" + this.food.name + \" and my variant is \" + this.variant);\n    }\n}\nclass GenericPlanet<T extends GenericMonster</*2*/Cookie, any>> {\n    constructor(public name: string, public solarSystem: string, public species: T) { }\n}\nvar cookie = new Cookie(\"Chocolate Chip\", false);\nvar cookieMonster = new GenericMonster<Cookie, string>(\"Cookie Monster\", 50, true, cookie, \"hello\");\nvar sesameStreet = new GenericPlanet<GenericMonster<Cookie, string>>(\"Sesame Street\", \"Alpha Centuri\", cookieMonster);\nclass GenericPlanet2<T extends Food, V>{\n    constructor(public name: string, public solarSystem: string, public species: GenericMonster<T, V>) { }\n}\n /*1*/"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyNoErrors(t);
                f.GoToMarker(t, "1");
                f.InsertLine(t, "");
                f.InsertLine(t, "");
                f.VerifyNoErrors(t);
                f.GoToMarker(t, "2");
                f.DeleteAtCaret(t, 6);
                f.Insert(t, "any");
                f.VerifyNoErrors(t);
                f.InsertLine(t, "var narnia = new GenericPlanet2<Cookie, string>(");
            }
            finally
            {
                done();
            }
        });
    }

}
