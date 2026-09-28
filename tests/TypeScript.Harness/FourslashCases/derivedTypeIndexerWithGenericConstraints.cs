// ported from: internal/fourslash/tests/derivedTypeIndexerWithGenericConstraints_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class DerivedTypeIndexerWithGenericConstraintsCase
{
    public static void TestDerivedTypeIndexerWithGenericConstraints(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @strict: false\nclass CollectionItem {\n    x: number;\n}\nclass Entity extends CollectionItem {\n    y: number;\n}\nclass BaseCollection<TItem extends CollectionItem>  {\n    _itemsByKey: { [key: string]: TItem; };\n}\nclass DbSet<TEntity extends Entity> extends BaseCollection<TEntity> { // error\n    _itemsByKey: { [key: string]: TEntity; } = {};\n}\nvar a: BaseCollection<CollectionItem>;\nvar /**/r = a._itemsByKey['x']; // should just say CollectionItem not TItem extends CollectionItem\nvar result = r.x;\na = new DbSet<Entity>();\nvar r2 = a._itemsByKey['x'];\nvar result2 = r2.x;"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyQuickInfoAt(t, "", "var r: CollectionItem", "");
                f.VerifyNoErrors(t);
            }
            finally
            {
                done();
            }
        });
    }

}
