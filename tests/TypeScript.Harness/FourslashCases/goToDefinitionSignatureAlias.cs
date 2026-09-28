// ported from: internal/fourslash/tests/goToDefinitionSignatureAlias_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class GoToDefinitionSignatureAliasCase
{
    public static void TestGoToDefinitionSignatureAlias(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @jsx: preserve\n// @Filename: /a.tsx\nfunction /*f*/f() {}\nconst /*g*/g = f;\nconst /*h*/h = g;\n[|/*useF*/f|]();\n[|/*useG*/g|]();\n[|/*useH*/h|]();\nconst /*i*/i = () => 0;\nconst /*iFn*/iFn = function () { return 0; };\nconst /*j*/j = i;\n[|/*useI*/i|]();\n[|/*useIFn*/iFn|]();\n[|/*useJ*/j|]();\nconst o = { /*m*/m: () => 0 };\no.[|/*useM*/m|]();\nconst oFn = { /*mFn*/mFn: function () { return 0; } };\noFn.[|/*useMFn*/mFn|]();\nclass Component { /*componentCtr*/constructor(props: {}) {} }\ntype ComponentClass = /*ComponentClass*/new () => Component;\ninterface ComponentClass2 { /*ComponentClass2*/new(): Component; }\n\nclass /*MyComponent*/MyComponent extends Component {}\n<[|/*jsxMyComponent*/MyComponent|] />;\nnew [|/*newMyComponent*/MyComponent|]({});\n\ndeclare const /*MyComponent2*/MyComponent2: ComponentClass;\n<[|/*jsxMyComponent2*/MyComponent2|] />;\nnew [|/*newMyComponent2*/MyComponent2|]();\n\ndeclare const /*MyComponent3*/MyComponent3: ComponentClass2;\n<[|/*jsxMyComponent3*/MyComponent3|] />;\nnew [|/*newMyComponent3*/MyComponent3|]();"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyNoErrors(t);
                f.VerifyBaselineGoToDefinition(t, true, "useF", "useG", "useH", "useI", "useIFn", "useJ", "useM", "useMFn", "jsxMyComponent", "newMyComponent", "jsxMyComponent2", "newMyComponent2", "jsxMyComponent3", "newMyComponent3");
            }
            finally
            {
                done();
            }
        });
    }

}
