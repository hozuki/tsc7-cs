// ported from: internal/fourslash/tests/codeFixClassImplementInterfaceComputedPropertyNameWellKnownSymbols_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class CodeFixClassImplementInterfaceComputedPropertyNameWellKnownSymbolsCase
{
    public static void TestCodeFixClassImplementInterfaceComputedPropertyNameWellKnownSymbols(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @strict: false\n// @lib: es2017\ninterface I<Species> {\n    [Symbol.hasInstance](o: any): boolean;\n    [Symbol.isConcatSpreadable]: boolean;\n    [Symbol.iterator](): any;\n    [Symbol.match]: boolean;\n    [Symbol.replace](...args);\n    [Symbol.search](str: string): number;\n    [Symbol.species](): Species;\n    [Symbol.split](str: string, limit?: number): string[];\n    [Symbol.toPrimitive](hint: \"number\"): number;\n    [Symbol.toPrimitive](hint: \"default\"): number;\n    [Symbol.toPrimitive](hint: \"string\"): string;\n    [Symbol.toStringTag]: string;\n    [Symbol.unscopables]: any;\n}\nclass C implements I<number> {}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyCodeFix(t, new F.VerifyCodeFixOptions { Description = "Implement interface 'I<number>'", NewFileContent = "interface I<Species> {\n    [Symbol.hasInstance](o: any): boolean;\n    [Symbol.isConcatSpreadable]: boolean;\n    [Symbol.iterator](): any;\n    [Symbol.match]: boolean;\n    [Symbol.replace](...args);\n    [Symbol.search](str: string): number;\n    [Symbol.species](): Species;\n    [Symbol.split](str: string, limit?: number): string[];\n    [Symbol.toPrimitive](hint: \"number\"): number;\n    [Symbol.toPrimitive](hint: \"default\"): number;\n    [Symbol.toPrimitive](hint: \"string\"): string;\n    [Symbol.toStringTag]: string;\n    [Symbol.unscopables]: any;\n}\nclass C implements I<number> {\n    [Symbol.hasInstance](o: any): boolean {\n        throw new Error(\"Method not implemented.\");\n    }\n    [Symbol.isConcatSpreadable]: boolean;\n    [Symbol.iterator]() {\n        throw new Error(\"Method not implemented.\");\n    }\n    [Symbol.match]: boolean;\n    [Symbol.replace](...args: any[]) {\n        throw new Error(\"Method not implemented.\");\n    }\n    [Symbol.search](str: string): number {\n        throw new Error(\"Method not implemented.\");\n    }\n    [Symbol.species](): number {\n        throw new Error(\"Method not implemented.\");\n    }\n    [Symbol.split](str: string, limit?: number): string[] {\n        throw new Error(\"Method not implemented.\");\n    }\n    [Symbol.toPrimitive](hint: \"number\"): number;\n    [Symbol.toPrimitive](hint: \"default\"): number;\n    [Symbol.toPrimitive](hint: \"string\"): string;\n    [Symbol.toPrimitive](hint: unknown): string | number {\n        throw new Error(\"Method not implemented.\");\n    }\n    [Symbol.toStringTag]: string;\n    [Symbol.unscopables]: any;\n}", Index = 0 });
            }
            finally
            {
                done();
            }
        });
    }

}
