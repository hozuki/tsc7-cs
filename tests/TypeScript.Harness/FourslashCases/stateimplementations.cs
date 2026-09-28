// ported from: internal/fourslash/tests/stateimplementations_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class StateimplementationsCase
{
    public static void TestImplementationsAcrossProjects(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("\n// @stateBaseline: true\n// @Filename: /projects/temp/temp.ts\n/*temp*/let x = 10\n// @Filename: /projects/temp/tsconfig.json\n{}\n// @Filename: /projects/container/lib/tsconfig.json\n{\n\t\"compilerOptions\": {\n\t\t\"composite\": true,\n\t},\n\treferences: [],\n\tfiles: [\n\t\t\"index.ts\",\n\t\t\"bar.ts\"\n\t],\n}\n// @Filename: /projects/container/lib/index.ts\nexport interface /*impl*/Foo {\n    func();\n}\nexport const val = 42;\n// @Filename: /projects/container/lib/bar.ts\nimport {Foo} from './index'\nclass A implements Foo {\n    func() {}\n}\nclass B implements Foo {\n    func() {}\n}\n// @Filename: /projects/container/exec/tsconfig.json\n{\n\t\"files\": [\"./index.ts\"],\n\t\"references\": [\n\t\t{ \"path\": \"../lib\" },\n\t],\n}\n// @Filename: /projects/container/exec/index.ts\nimport { Foo } from \"../lib\";\nclass A1 implements Foo {\n    func() {}\n}\nclass B1 implements Foo {\n    func() {}\n}\n// @Filename: /projects/container/compositeExec/tsconfig.json\n{\n\t\"compilerOptions\": {\n\t\t\"composite\": true,\n\t},\n\t\"files\": [\"./index.ts\"],\n\t\"references\": [\n\t\t{ \"path\": \"../lib\" },\n\t],\n}\n// @Filename: /projects/container/compositeExec/index.ts\nimport { Foo } from \"../lib\";\nclass A2 implements Foo {\n    func() {}\n}\nclass B2 implements Foo {\n    func() {}\n}\n// @Filename: /projects/container/tsconfig.json\n{\n\t\"files\": [],\n\t\"include\": [],\n\t\"references\": [\n\t\t{ \"path\": \"./exec\" },\n\t\t{ \"path\": \"./compositeExec\" },\n\t],\n}\n// @Filename: /projects/container/tsconfig.json\n{\n\t\"files\": [],\n\t\"include\": [],\n\t\"references\": [\n\t\t{ \"path\": \"./exec\" },\n\t\t{ \"path\": \"./compositeExec\" },\n\t],\n}\n// @Filename: /projects/container/tsconfig.json\n{\n\t\"files\": [],\n\t\"include\": [],\n\t\"references\": [\n\t\t{ \"path\": \"./exec\" },\n\t\t{ \"path\": \"./compositeExec\" },\n\t],\n}\n"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.GoToMarker(t, "impl");
                f.GoToMarker(t, "temp");
                f.VerifyBaselineGoToImplementation(t, "impl");
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

}
