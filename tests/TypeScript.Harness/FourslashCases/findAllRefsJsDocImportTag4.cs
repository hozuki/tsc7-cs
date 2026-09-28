// ported from: internal/fourslash/tests/findAllRefsJsDocImportTag4_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class FindAllRefsJsDocImportTag4Case
{
    public static void TestFindAllRefsJsDocImportTag4(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @checkJs: true\n// @Filename: /component.js\nexport class Component {\n  constructor() {\n    this.id_ = Math.random();\n  }\n  id() {\n    return this.id_;\n  }\n}\n// @Filename: /spatial-navigation.js\n/** @import * as C from './component.js' */\n\nexport class SpatialNavigation {\n  /**\n   * @param {C.Component} component\n   */\n  add(component) {}\n}\n// @Filename: /player.js\nimport * as C from './component.js';\n\n/**\n * @extends C/*1*/.Component\n */\nexport class Player extends Component {}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyBaselineFindAllReferences(t, "1");
            }
            finally
            {
                done();
            }
        });
    }

}
