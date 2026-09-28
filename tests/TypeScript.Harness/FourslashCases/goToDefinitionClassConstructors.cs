// ported from: internal/fourslash/tests/goToDefinitionClassConstructors_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class GoToDefinitionClassConstructorsCase
{
    public static void TestGoToDefinitionClassConstructors(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @filename: definitions.ts\nexport class Base {\n    constructor(protected readonly cArg: string) {}\n}\n\nexport class Derived extends Base {\n    readonly email = this.cArg.getByLabel('Email')\n    readonly password =  this.cArg.getByLabel('Password')\n}\n// @filename: main.ts\nimport { Derived } from './definitions'\nconst derived = new [|/*Derived*/Derived|](cArg)\n// @filename: defInSameFile.ts\nimport { Base } from './definitions'\nclass SameFile extends Base {\n    readonly name: string = 'SameFile'\n}\nconst SameFile = new [|/*SameFile*/SameFile|](cArg)\nconst wrapper = new [|/*Base*/Base|](cArg)\n// @filename: hasConstructor.ts\nimport { Base } from './definitions'\nclass HasConstructor extends Base {\n    constructor() {}\n    readonly name: string = '';\n}\nconst hasConstructor = new [|/*HasConstructor*/HasConstructor|](cArg)"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyBaselineGoToDefinition(t, true, "Derived", "SameFile", "HasConstructor", "Base");
            }
            finally
            {
                done();
            }
        });
    }

}
