// ported from: internal/fourslash/tests/deprecatedInheritedJSDocOverload_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class DeprecatedInheritedJSDocOverloadCase
{
    public static void TestDeprecatedInheritedJSDocOverload(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = "// @strict: false\ninterface PartialObserver<T> {}\ninterface Subscription {}\ninterface Unsubscribable {}\n\nexport interface Subscribable<T> {\n  subscribe(observer?: PartialObserver<T>): Unsubscribable;\n  /** @deprecated Base deprecation 1 */\n  subscribe(next: null | undefined, error: null | undefined, complete: () => void): Unsubscribable;\n  /** @deprecated Base deprecation 2 */\n  subscribe(next: null | undefined, error: (error: any) => void, complete?: () => void): Unsubscribable;\n  /** @deprecated Base deprecation 3 */\n  subscribe(next: (value: T) => void, error: null | undefined, complete: () => void): Unsubscribable;\n  subscribe(next?: (value: T) => void, error?: (error: any) => void, complete?: () => void): Unsubscribable;\n}\ninterface ThingWithDeprecations<T> extends Subscribable<T> {\n   subscribe(observer?: PartialObserver<T>): Subscription;\n   /** @deprecated 'real' deprecation */\n   subscribe(next: null | undefined, error: null | undefined, complete: () => void): Subscription;\n   /** @deprecated 'real' deprecation */\n   subscribe(next: null | undefined, error: (error: any) => void, complete?: () => void): Subscription;\n}\ndeclare const a: ThingWithDeprecations<void>\na.subscribe/**/(() => {\n  console.log('something happened');\n});";
            var (f, done) = F.Fourslash.NewFourslash(t, null, content);
            try
            {
                f.VerifyBaselineHover(t);
            }
            finally
            {
                done();
            }
        });
    }

}
