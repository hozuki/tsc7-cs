// ported from: internal/fourslash/tests/organizeImports1_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using LSP = TypeScript.LsProto;
using LSUP = TypeScript.LsUtil;
using TRI = TypeScript.Core.Tristate;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class OrganizeImports1Case
{
    public static void TestOrganizeImports1(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("import {\n    d, d as D,\n    c,\n    c as C, b,\n    b as B, a\n} from './foo';\nimport {\n    h, h as H,\n    g,\n    g as G, f,\n    f as F, e\n} from './foo';\n\nconsole.log(a, B, b, c, C, d, D);\nconsole.log(e, f, F, g, G, H, h);"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyOrganizeImportsWithRequestKind(t, "import {\n    a,\n    b,\n    b as B,\n    c,\n    c as C,\n    d, d as D,\n    e,\n    f,\n    f as F,\n    g,\n    g as G,\n    h, h as H\n} from './foo';\n\nconsole.log(a, B, b, c, C, d, D);\nconsole.log(e, f, F, g, G, H, h);", LSP.CodeActionKind.SourceOrganizeImports, LSP.CodeActionKind.SourceOrganizeImportsTs, new LSUP.UserPreferences { OrganizeImportsIgnoreCase = TRI.TSTrue });
                f.VerifyOrganizeImports(t, "import {\n    b as B,\n    c as C,\n    d as D,\n    f as F,\n    g as G,\n    h as H,\n    a,\n    b,\n    c,\n    d,\n    e,\n    f,\n    g,\n    h\n} from './foo';\n\nconsole.log(a, B, b, c, C, d, D);\nconsole.log(e, f, F, g, G, H, h);", LSP.CodeActionKind.SourceOrganizeImportsTs, new LSUP.UserPreferences { OrganizeImportsIgnoreCase = TRI.TSFalse });
            }
            finally
            {
                done();
            }
        });
    }

}
