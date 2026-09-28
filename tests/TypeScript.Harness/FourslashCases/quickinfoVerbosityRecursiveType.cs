// ported from: internal/fourslash/tests/quickinfoVerbosityRecursiveType_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class QuickinfoVerbosityRecursiveTypeCase
{
    public static void TestQuickinfoVerbosityRecursiveType(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @lib: es5\ntype Node/*N*/<T> = {\n    value: T;\n    left: Node<T> | undefined;\n    right: Node<T> | undefined;\n}\nconst n/*n*/: Node<number> = {\n    value: 1,\n    left: undefined,\n    right: undefined,\n}\ninterface Orange {\n    name: string;\n}\ntype TreeNode/*t*/<T> = {\n    value: T;\n    left: TreeNode<T> | undefined;\n    right: TreeNode<T> | undefined;\n    orange?: Orange;\n}\nconst m/*m*/: TreeNode<number> = {\n    value: 1,\n    left: undefined,\n    right: undefined,\n    orange: { name: \"orange\" },\n}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyBaselineHoverWithVerbosity(t, new Dictionary<string, int[]> { ["N"] = [0], ["n"] = [0, 1], ["t"] = [0, 1], ["m"] = [0, 1, 2] });
            }
            finally
            {
                done();
            }
        });
    }

}
