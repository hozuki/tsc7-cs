// ported from: internal/fourslash/tests/quickInfoGetterSetter_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class QuickInfoGetterSetterCase
{
    public static void TestQuickInfoGetterSetter(TSU.IHarnessTest t)
    {
        t.Skip("Known failing fourslash test");
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @target: es2015\nclass C {\n    #x = Promise.resolve(\"\")\n    set /*setterDef*/myValue(x: Promise<string> | string) {\n        this.#x = Promise.resolve(x);\n    }\n    get /*getterDef*/myValue(): Promise<string> {\n        return this.#x;\n    }\n}\nlet instance = new C();\ninstance./*setterUse*/myValue = instance./*getterUse*/myValue;"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyQuickInfoAt(t, "getterUse", "(property) C.myValue: Promise<string>", "");
                f.VerifyQuickInfoAt(t, "getterDef", "(getter) C.myValue: Promise<string>", "");
                f.VerifyQuickInfoAt(t, "setterUse", "(property) C.myValue: string | Promise<string>", "");
                f.VerifyQuickInfoAt(t, "setterDef", "(setter) C.myValue: string | Promise<string>", "");
            }
            finally
            {
                done();
            }
        });
    }

}
