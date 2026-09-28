// ported from: internal/fourslash/tests/referencesForDeclarationKeywords_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class ReferencesForDeclarationKeywordsCase
{
    public static void TestReferencesForDeclarationKeywords(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("class Base {}\ninterface Implemented1 {}\n/*classDecl1_classKeyword*/class C1 /*classDecl1_extendsKeyword*/extends Base /*classDecl1_implementsKeyword*/implements Implemented1 {\n    /*getDecl_getKeyword*/get e() { return 1; }\n    /*setDecl_setKeyword*/set e(v) {}\n}\n/*interfaceDecl1_interfaceKeyword*/interface I1 /*interfaceDecl1_extendsKeyword*/extends Base { }\n/*typeDecl_typeKeyword*/type T = { }\n/*enumDecl_enumKeyword*/enum E { }\n/*namespaceDecl_namespaceKeyword*/namespace N { }\n/*moduleDecl_moduleKeyword*/namespace M { }\n/*functionDecl_functionKeyword*/function fn() {}\n/*varDecl_varKeyword*/var x;\n/*letDecl_letKeyword*/let y;\n/*constDecl_constKeyword*/const z = 1;\ninterface Implemented2 {}\ninterface Implemented3 {}\nclass C2 /*classDecl2_implementsKeyword*/implements Implemented2, Implemented3 {}\ninterface I2 /*interfaceDecl2_extendsKeyword*/extends Implemented2, Implemented3 {}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyBaselineFindAllReferences(t, "classDecl1_classKeyword", "classDecl1_extendsKeyword", "classDecl1_implementsKeyword", "classDecl2_implementsKeyword", "getDecl_getKeyword", "setDecl_setKeyword", "interfaceDecl1_interfaceKeyword", "interfaceDecl1_extendsKeyword", "interfaceDecl2_extendsKeyword", "typeDecl_typeKeyword", "enumDecl_enumKeyword", "namespaceDecl_namespaceKeyword", "moduleDecl_moduleKeyword", "functionDecl_functionKeyword", "varDecl_varKeyword", "letDecl_letKeyword", "constDecl_constKeyword");
            }
            finally
            {
                done();
            }
        });
    }

}
