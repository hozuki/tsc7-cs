// ported from: internal/fourslash/tests/codeFixAddMissingImportForReactJsx2_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class CodeFixAddMissingImportForReactJsx2Case
{
    public static void TestCodeFixAddMissingImportForReactJsx2(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @jsx: react-jsxdev\n// @Filename: node_modules/react/index.d.ts\nexport declare var React: any;\n// @Filename: node_modules/react/package.json\n{\n  \"name\": \"react\",\n  \"types\": \"./index.d.ts\"\n}\n// @Filename: foo.tsx\n export default function Foo(){\n     return <></>;\n }\n// @Filename: bar.tsx\n export default function Bar(){\n     return <Foo></Foo>;\n }\n// @Filename: package.json\n{\n  \"dependencies\": {\n    \"react\": \"*\"\n  }\n}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.GoToFile(t, "bar.tsx");
                f.VerifyCodeFixAll(t, new F.VerifyCodeFixAllOptions { FixID = "fixMissingImport", NewFileContent = "import Foo from \"./foo\";\n\nexport default function Bar(){\n    return <Foo></Foo>;\n}" });
            }
            finally
            {
                done();
            }
        });
    }

}
