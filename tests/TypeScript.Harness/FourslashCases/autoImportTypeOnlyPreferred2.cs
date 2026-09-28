// ported from: internal/fourslash/tests/autoImportTypeOnlyPreferred2_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class AutoImportTypeOnlyPreferred2Case
{
    public static void TestAutoImportTypeOnlyPreferred2(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @Filename: /node_modules/react/index.d.ts\nexport interface ComponentType {}\nexport interface ComponentProps {}\nexport declare function useState<T>(initialState: T): [T, (newState: T) => void];\nexport declare function useEffect(callback: () => void, deps: any[]): void;\n// @Filename: /main.ts\nimport type { ComponentType } from \"react\";\nimport { useState } from \"react\";\n\nexport function Component({ prop } : { prop: ComponentType }) {\n    const codeIsUnimportant = useState(1);\n    useEffect/*1*/(() => {}, []);\n}\n// @Filename: /main2.ts\nimport { useState } from \"react\";\nimport type { ComponentType } from \"react\";\n\ntype _ = ComponentProps/*2*/;"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.GoToMarker(t, "1");
                f.VerifyImportFixAtPosition(t, ["import type { ComponentType } from \"react\";\nimport { useEffect, useState } from \"react\";\n\nexport function Component({ prop } : { prop: ComponentType }) {\n    const codeIsUnimportant = useState(1);\n    useEffect(() => {}, []);\n}"], null!);
                f.GoToMarker(t, "2");
                f.VerifyImportFixAtPosition(t, ["import { useState } from \"react\";\nimport type { ComponentProps, ComponentType } from \"react\";\n\ntype _ = ComponentProps;"], null!);
            }
            finally
            {
                done();
            }
        });
    }

}
