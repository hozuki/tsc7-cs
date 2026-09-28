// ported from: internal/fourslash/tests/findAllReferencesUmdModuleAsGlobalConst_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class FindAllReferencesUmdModuleAsGlobalConstCase
{
    public static void TestFindAllReferencesUmdModuleAsGlobalConst(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @Filename: /node_modules/@types/three/three-core.d.ts\nexport class Vector3 {\n    constructor(x?: number, y?: number, z?: number);\n    x: number;\n    y: number;\n}\n// @Filename: /node_modules/@types/three/index.d.ts\nexport * from \"./three-core\";\nexport as namespace /*0*/THREE;\n// @Filename: /typings/global.d.ts\nimport * as _THREE from '/*1*/three';\ndeclare global {\n    const /*2*/THREE: typeof _THREE;\n}\n// @Filename: /src/index.ts\nexport const a = {};\nlet v = new /*3*/THREE.Vector2();\n// @Filename: /tsconfig.json\n{\n    \"compilerOptions\": {\n        \"esModuleInterop\": true,\n        \"outDir\": \"./build/js/\",\n        \"noImplicitAny\": true,\n        \"module\": \"es6\",\n        \"target\": \"es6\",\n        \"allowJs\": true,\n        \"skipLibCheck\": true,\n        \"lib\": [\"es2016\", \"dom\"],\n        \"typeRoots\": [\"node_modules/@types/\"],\n        \"types\": [\"three\"]\n \t},\n    \"files\": [\"/src/index.ts\", \"typings/global.d.ts\"]\n}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyBaselineFindAllReferences(t, "0", "1", "2", "3");
            }
            finally
            {
                done();
            }
        });
    }

}
