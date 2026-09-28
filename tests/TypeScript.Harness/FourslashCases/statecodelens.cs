// ported from: internal/fourslash/tests/statecodelens_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using LSUP = TypeScript.LsUtil;
using TRI = TypeScript.Core.Tristate;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class StatecodelensCase
{
    public static void TestCodeLensAcrossProjects(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("\n// @stateBaseline: true\n// @Filename: /projects/temp/temp.ts\n/*temp*/let x = 10\n// @Filename: /projects/temp/tsconfig.json\n{}\n// @Filename: /projects/container/lib/tsconfig.json\n{\n\t\"compilerOptions\": {\n\t\t\"composite\": true,\n\t},\n\treferences: [],\n\tfiles: [\n\t\t\"index.ts\",\n\t\t\"bar.ts\"\n\t],\n}\n// @Filename: /projects/container/lib/index.ts\n/*impl*/\nexport interface Pointable {\n  getX(): number;\n  getY(): number;\n}\nexport const val = 42;\n// @Filename: /projects/container/lib/bar.ts\nimport { Pointable } from \"./index\";\nclass Point implements Pointable {\n  getX(): number {\n    return 0;\n  }\n  getY(): number {\n    return 0;\n  }\n}\n// @Filename: /projects/container/exec/tsconfig.json\n{\n\t\"files\": [\"./index.ts\"],\n\t\"references\": [\n\t\t{ \"path\": \"../lib\" },\n\t],\n}\n// @Filename: /projects/container/exec/index.ts\nimport { Pointable } from \"../lib\";\nclass Point1 implements Pointable {\n  getX(): number {\n    return 0;\n  }\n  getY(): number {\n    return 0;\n  }\n}\n// @Filename: /projects/container/compositeExec/tsconfig.json\n{\n\t\"compilerOptions\": {\n\t\t\"composite\": true,\n\t},\n\t\"files\": [\"./index.ts\"],\n\t\"references\": [\n\t\t{ \"path\": \"../lib\" },\n\t],\n}\n// @Filename: /projects/container/compositeExec/index.ts\nimport { Pointable } from \"../lib\";\nclass Point2 implements Pointable {\n  getX(): number {\n    return 0;\n  }\n  getY(): number {\n    return 0;\n  }\n}\n// @Filename: /projects/container/tsconfig.json\n{\n\t\"files\": [],\n\t\"include\": [],\n\t\"references\": [\n\t\t{ \"path\": \"./exec\" },\n\t\t{ \"path\": \"./compositeExec\" },\n\t],\n}\n// @Filename: /projects/container/tsconfig.json\n{\n\t\"files\": [],\n\t\"include\": [],\n\t\"references\": [\n\t\t{ \"path\": \"./exec\" },\n\t\t{ \"path\": \"./compositeExec\" },\n\t],\n}\n// @Filename: /projects/container/tsconfig.json\n{\n\t\"files\": [],\n\t\"include\": [],\n\t\"references\": [\n\t\t{ \"path\": \"./exec\" },\n\t\t{ \"path\": \"./compositeExec\" },\n\t],\n}\n"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.GoToMarker(t, "impl");
                f.GoToMarker(t, "temp");
                f.VerifyBaselineCodeLens(t, new LSUP.UserPreferences { CodeLens = new LSUP.CodeLensUserPreferences { ReferencesCodeLensEnabled = TRI.TSTrue, ReferencesCodeLensShowOnAllFunctions = TRI.TSTrue, ImplementationsCodeLensEnabled = TRI.TSTrue, ImplementationsCodeLensShowOnInterfaceMethods = TRI.TSTrue, ImplementationsCodeLensShowOnAllClassMethods = TRI.TSTrue } });
                f.CloseFileOfMarker(t, "temp");
                f.GoToMarker(t, "temp");
                f.CloseFileOfMarker(t, "impl");
                f.CloseFileOfMarker(t, "temp");
                f.GoToMarker(t, "temp");
            }
            finally
            {
                done();
            }
        });
    }

    public static void TestCodeLensOnFunctionAcrossProjects1(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("\n// @filename: ./a/tsconfig.json\n{\n  \"compilerOptions\": {\n\t\"composite\": true,\n\t\"declaration\": true,\n\t\"declarationMaps\": true,\n\t\"outDir\": \"./dist\",\n\t\"rootDir\": \"src\"\n  },\n  \"include\": [\"./src\"]\n}\n\n// @filename: ./a/src/foo.ts\nexport function aaa() {}\naaa();\n\n// @filename: ./b/tsconfig.json\n{\n  \"compilerOptions\": {\n\t\"composite\": true,\n\t\"declaration\": true,\n\t\"declarationMaps\": true,\n\t\"outDir\": \"./dist\",\n\t\"rootDir\": \"src\"\n  },\n  \"references\": [{ \"path\": \"../a\" }],\n  \"include\": [\"./src\"]\n}\n\n// @filename: ./b/src/bar.ts\nimport * as foo from '../../a/dist/foo.js';\nfoo.aaa();\n"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyBaselineCodeLens(t, new LSUP.UserPreferences { CodeLens = new LSUP.CodeLensUserPreferences { ReferencesCodeLensEnabled = TRI.TSTrue } });
            }
            finally
            {
                done();
            }
        });
    }

}
