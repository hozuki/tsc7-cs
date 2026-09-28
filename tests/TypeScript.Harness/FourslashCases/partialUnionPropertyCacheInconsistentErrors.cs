// ported from: internal/fourslash/tests/partialUnionPropertyCacheInconsistentErrors_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class PartialUnionPropertyCacheInconsistentErrorsCase
{
    public static void TestPartialUnionPropertyCacheInconsistentErrors(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @strict: true\n// @lib: esnext\ninterface ComponentOptions<Props> {\n  setup?: (props: Props) => void;\n  name?: string;\n}\n\ninterface FunctionalComponent<P> {\n  (props: P): void;\n}\n\ntype ConcreteComponent<Props> =\n  | ComponentOptions<Props>\n  | FunctionalComponent<Props>;\n\ntype Component<Props = {}> = ConcreteComponent<Props>;\n\ntype WithInstallPlugin = { _prefix?: string };\n\n\n/**/\nexport function withInstall<C extends Component, T extends WithInstallPlugin>(\n  component: C | C[],\n  target?: T,\n): string {\n  const componentWithInstall = (target ?? component) as T;\n  const components = Array.isArray(component) ? component : [component];\n\n  const { name } = components[0];\n  if (name) {\n    return name;\n  }\n\n  return \"\";\n}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyNoErrors(t);
                f.GoToMarker(t, "");
                f.Insert(t, "type C = Component['name']");
                f.VerifyNoErrors(t);
            }
            finally
            {
                done();
            }
        });
    }

}
