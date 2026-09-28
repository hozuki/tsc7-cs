// ported from: internal/fourslash/tests/organizeImports_typeOrderSameModule_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using LSP = TypeScript.LsProto;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class OrganizeImports_typeOrderSameModuleCase
{
    public static void TestOrganizeImports_importKindOrder(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @module: commonjs\n// @Filename: /main.ts\nimport { foo } from './package';\nimport type { Foo } from './package';\nimport './package';\nimport Default from './package';\nimport * as ns from './package';\n\nconst x: Foo = foo;\nconsole.log(x, Default, ns);\n// @Filename: /package.d.ts\nexport type Foo = string;\nexport declare const foo: Foo;\nexport declare function fn(): void;\nexport default class Default {}\nexport as namespace Package;"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyOrganizeImports(t, "import './package';\nimport type { Foo } from './package';\nimport * as ns from './package';\nimport Default, { foo } from './package';\n\nconst x: Foo = foo;\nconsole.log(x, Default, ns);", LSP.CodeActionKind.SourceOrganizeImportsTs, null!);
            }
            finally
            {
                done();
            }
        });
    }

    public static void TestOrganizeImports_importKindOrderMultipleModules(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @module: commonjs\n// @Filename: /main.ts\nimport { b } from './b';\nimport { a } from './a';\nimport type { TypeB } from './b';\nimport type { TypeA } from './a';\nimport './b';\nimport './a';\n\nconst x: TypeA = a;\nconst y: TypeB = b;\nconsole.log(x, y);\n// @Filename: /a.d.ts\nexport type TypeA = string;\nexport declare const a: TypeA;\n// @Filename: /b.d.ts\nexport type TypeB = string;\nexport declare const b: TypeB;"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyOrganizeImports(t, "import './a';\nimport type { TypeA } from './a';\nimport { a } from './a';\nimport './b';\nimport type { TypeB } from './b';\nimport { b } from './b';\n\nconst x: TypeA = a;\nconst y: TypeB = b;\nconsole.log(x, y);", LSP.CodeActionKind.SourceOrganizeImportsTs, null!);
            }
            finally
            {
                done();
            }
        });
    }

}
