// ported from: internal/fourslash/tests/genericTypeAliasIntersectionCompletions_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;
using U = TypeScript.Harness.FourslashCases.FourslashTestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class GenericTypeAliasIntersectionCompletionsCase
{
    public static void TestGenericTypeAliasIntersectionCompletions(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("type MixinCtor<A, B> = new () => A & B & { constructor: MixinCtor<A, B> };\nfunction merge<A, B>(a: { prototype: A }, b: { prototype: B }): MixinCtor<A, B> {\n  let merged = function() { }\n  Object.assign(merged.prototype, a.prototype, b.prototype);\n  return <MixinCtor<A, B>><any>merged;\n}\n\nclass TreeNode {\n  value: any;\n}\n\nabstract class LeftSideNode extends TreeNode {\n  abstract right(): TreeNode;\n  left(): TreeNode {\n    return null;\n  }\n}\n\nabstract class RightSideNode extends TreeNode {\n  abstract left(): TreeNode;\n  right(): TreeNode {\n    return null;\n  };\n}\n\nvar obj = new (merge(LeftSideNode, RightSideNode))();\nobj./**/"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyCompletions(t, "", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Unsorted = ["right", "left", "value", "constructor"] } });
            }
            finally
            {
                done();
            }
        });
    }

}
