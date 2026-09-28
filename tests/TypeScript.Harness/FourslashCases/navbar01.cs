// ported from: internal/fourslash/tests/navbar01_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class Navbar01Case
{
    public static void TestNavbar01(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @lib: es5\n// Interface\ninterface IPoint {\n    getDist(): number;\n    new(): IPoint;\n    (): any;\n    [x:string]: number;\n    prop: string;\n}\n\n/// Module\nnamespace Shapes {\n    // Class\n    export class Point implements IPoint {\n        constructor (public x: number, public y: number) { }\n\n        // Instance member\n        getDist() { return Math.sqrt(this.x * this.x + this.y * this.y); }\n\n        // Getter\n        get value(): number { return 0; }\n\n        // Setter\n        set value(newValue: number) { return; }\n\n        // Static member\n        static origin = new Point(0, 0);\n\n        // Static method\n        private static getOrigin() { return Point.origin;}\n    }\n\n    enum Values { value1, value2, value3 }\n}\n\n// Local variables\nvar p: IPoint = new Shapes.Point(3, 4);\nvar dist = p.getDist();"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.MarkTestAsStradaServer();
                f.VerifyBaselineDocumentSymbol(t);
            }
            finally
            {
                done();
            }
        });
    }

}
