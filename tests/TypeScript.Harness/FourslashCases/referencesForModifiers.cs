// ported from: internal/fourslash/tests/referencesForModifiers_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class ReferencesForModifiersCase
{
    public static void TestReferencesForModifiers(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @lib: es5\n[|/*declareModifier*/declare /*abstractModifier*/abstract class C1 {\n    [|/*staticModifier*/static a;|]\n    [|/*readonlyModifier*/readonly b;|]\n    [|/*publicModifier*/public c;|]\n    [|/*protectedModifier*/protected d;|]\n    [|/*privateModifier*/private e;|]\n}|]\n[|/*constModifier*/const enum E {\n}|]\n[|/*asyncModifier*/async function fn() {}|]\n[|/*exportModifier*/export /*defaultModifier*/default class C2 {}|]"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyBaselineFindAllReferences(t, "declareModifier", "abstractModifier", "staticModifier", "readonlyModifier", "publicModifier", "protectedModifier", "privateModifier", "constModifier", "asyncModifier", "exportModifier", "defaultModifier");
            }
            finally
            {
                done();
            }
        });
    }

}
