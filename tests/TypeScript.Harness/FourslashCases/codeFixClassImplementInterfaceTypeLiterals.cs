// ported from: internal/fourslash/tests/codeFixClassImplementInterfaceTypeLiterals_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class CodeFixClassImplementInterfaceTypeLiteralsCase
{
    public static void TestCodeFixClassImplementInterfaceTypeLiterals(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("type Builtin = Date | Function | Uint8Array | string | number | boolean | undefined;\n\nexport type DeepPartial<T> = T extends Builtin ? T :\n    T extends Array<infer U> ? Array<DeepPartial<U>> :\n        T extends ReadonlyArray<infer U> ? ReadonlyArray<DeepPartial<U>> :\n            T extends {} ? { [K in keyof T]?: DeepPartial<T[K]> } : Partial<T>;\n\nexport interface Nested {\n    field: string;\n}\n\ninterface Foo {\n    request(): DeepPartial<{ nested1: Nested; test2: Nested }>;\n}\n[|export class C implements Foo {}|]"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyCodeFix(t, new F.VerifyCodeFixOptions { Description = "Implement interface 'Foo'", NewRangeContent = "export class C implements Foo {\n    request(): DeepPartial<{ nested1: Nested; test2: Nested; }> {\n        throw new Error(\"Method not implemented.\");\n    }\n}", Index = 0 });
            }
            finally
            {
                done();
            }
        });
    }

}
