// ported from: internal/fourslash/tests/callHierarchyIncomingCallsNoCrashArrayPush_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class CallHierarchyIncomingCallsNoCrashArrayPushCase
{
    public static void TestCallHierarchyIncomingCallsNoCrashArrayPush(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("function splitNames(name: string) {\n  return (name || \"\").split(\",\").filter(Boolean);\n}\n\nasync function trim(packageNames: string[]) {\n  const nameOrPkgs = packageNames.filter(Boolean);\n  const names = [];\n  for (const nameOrPkg of nameOrPkgs) {\n    try {\n      names./*push*/push(nameOrPkg);\n    } catch (error) {\n    }\n  }\n  return names;\n}\n\t"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.GoToMarker(t, "push");
                f.VerifyBaselineCallHierarchy(t);
            }
            finally
            {
                done();
            }
        });
    }

}
