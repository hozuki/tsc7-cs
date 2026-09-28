// ported from: internal/fourslash/tests/organizeImports13_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using LSP = TypeScript.LsProto;
using LSUP = TypeScript.LsUtil;
using TRI = TypeScript.Core.Tristate;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class OrganizeImports13Case
{
    public static void TestOrganizeImports13(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("import {\n    Type1,\n    Type2,\n    func4,\n    Type3,\n    Type4,\n    Type5,\n    Type7,\n    Type8,\n    Type9,\n    func1,\n    func2,\n    Type6,\n    func3,\n    func5,\n    func6,\n    func7,\n    func8,\n    func9,\n} from \"foo\";\ninterface Use extends Type1, Type2, Type3, Type4, Type5, Type6, Type7, Type8, Type9 {}\nconsole.log(func1, func2, func3, func4, func5, func6, func7, func8, func9);"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyOrganizeImports(t, "import {\n    Type1,\n    Type2,\n    Type3,\n    Type4,\n    Type5,\n    Type6,\n    Type7,\n    Type8,\n    Type9,\n    func1,\n    func2,\n    func3,\n    func4,\n    func5,\n    func6,\n    func7,\n    func8,\n    func9,\n} from \"foo\";\ninterface Use extends Type1, Type2, Type3, Type4, Type5, Type6, Type7, Type8, Type9 {}\nconsole.log(func1, func2, func3, func4, func5, func6, func7, func8, func9);", LSP.CodeActionKind.SourceOrganizeImportsTs, null!);
                f.VerifyOrganizeImports(t, "import {\n    func1,\n    func2,\n    func3,\n    func4,\n    func5,\n    func6,\n    func7,\n    func8,\n    func9,\n    Type1,\n    Type2,\n    Type3,\n    Type4,\n    Type5,\n    Type6,\n    Type7,\n    Type8,\n    Type9,\n} from \"foo\";\ninterface Use extends Type1, Type2, Type3, Type4, Type5, Type6, Type7, Type8, Type9 {}\nconsole.log(func1, func2, func3, func4, func5, func6, func7, func8, func9);", LSP.CodeActionKind.SourceOrganizeImportsTs, new LSUP.UserPreferences { OrganizeImportsIgnoreCase = TRI.TSTrue });
            }
            finally
            {
                done();
            }
        });
    }

}
