// ported from: internal/fourslash/tests/linkedEditingJsxTag12_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using LSP = TypeScript.LsProto;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class LinkedEditingJsxTag12Case
{
    public static void TestLinkedEditingJsxTag12(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @Filename: /incomplete.tsx\nfunction Test() {\n    return <div>\n        </*0*/\n        <div {...{}}>\n        </div>\n    </div>\n}\n// @Filename: /incompleteMismatched.tsx\nfunction Test() {\n    return <div>\n        <T\n        <div {...{}}>\n        </div>\n    </div>\n}\n// @Filename: /incompleteMismatched2.tsx\nfunction Test() {\n    return <div>\n        <T\n        <div {...{}}>\n        T</div>\n    </div>\n}\n// @Filename: /incompleteMismatched3.tsx\nfunction Test() {\n    return <div>\n        <div {...{}}>\n        </div>\n        <T\n    </div>\n}\n// @Filename: /mismatched.tsx\nfunction Test() {\n    return <div>\n        <T>\n        <div {...{}}>\n        </div>\n    </div>\n}\n// @Filename: /matched.tsx\nfunction Test() {\n    return <div>\n\n        <div {...{}}>\n        </div>\n    </div>\n}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyLinkedEditing(t, new Dictionary<string, LSP.Range[]> { ["0"] = [] });
                f.VerifyBaselineLinkedEditing(t);
            }
            finally
            {
                done();
            }
        });
    }

}
