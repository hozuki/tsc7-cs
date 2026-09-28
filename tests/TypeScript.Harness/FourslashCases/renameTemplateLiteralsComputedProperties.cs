// ported from: internal/fourslash/tests/renameTemplateLiteralsComputedProperties_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class RenameTemplateLiteralsComputedPropertiesCase
{
    public static void TestRenameTemplateLiteralsComputedProperties(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @Filename: a.ts\ninterface Obj {\n    [|[`[|{| \"contextRangeIndex\": 0 |}num|]`]: number;|]\n    [|['[|{| \"contextRangeIndex\": 2 |}bool|]']: boolean;|]\n}\n\nlet o: Obj = {\n    [|[`[|{| \"contextRangeIndex\": 4 |}num|]`]: 0|],\n    [|['[|{| \"contextRangeIndex\": 6 |}bool|]']: true|],\n};\n\no = {\n    [|['[|{| \"contextRangeIndex\": 8 |}num|]']: 1|],\n    [|[`[|{| \"contextRangeIndex\": 10 |}bool|]`]: false|],\n};\n\no.[|num|];\no['[|num|]'];\no[\"[|num|]\"];\no[`[|num|]`];\n\no.[|bool|];\no['[|bool|]'];\no[\"[|bool|]\"];\no[`[|bool|]`];\n\nexport { o };\n// @allowJs: true\n// @Filename: b.js\nimport { o as obj } from './a';\n\nobj.[|num|];\nobj[`[|num|]`];\n\nobj.[|bool|];\nobj[`[|bool|]`];"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyBaselineRenameAtRangesWithText(t, null!, "num", "bool");
            }
            finally
            {
                done();
            }
        });
    }

}
