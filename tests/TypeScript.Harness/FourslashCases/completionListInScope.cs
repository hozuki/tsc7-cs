// ported from: internal/fourslash/tests/completionListInScope_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;
using U = TypeScript.Harness.FourslashCases.FourslashTestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class CompletionListInScopeCase
{
    public static void TestCompletionListInScope(TSU.IHarnessTest t)
    {
        t.Skip("Known failing fourslash test");
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("namespace TestModule {\n    var localVariable = \"\";\n    export var exportedVariable = 0;\n\n    function localFunction() { }\n    export function exportedFunction() { }\n\n    class localClass { }\n    export class exportedClass { }\n\n    interface localInterface {}\n    export interface exportedInterface {}\n\n    namespace localModule {\n        export var x = 0;\n    }\n    export namespace exportedModule {\n        export var x = 0;\n    }\n\n    var v = /*valueReference*/ 0;\n    var t :/*typeReference*/;\n}\n\n// Add some new items to the module\nnamespace TestModule {\n    var localVariable2 = \"\";\n    export var exportedVariable2 = 0;\n\n    function localFunction2() { }\n    export function exportedFunction2() { }\n\n    class localClass2 { }\n    export class exportedClass2 { }\n\n    interface localInterface2 {}\n    export interface exportedInterface2 {}\n\n    namespace localModule2 {\n        export var x = 0;\n    }\n    export namespace exportedModule2 {\n        export var x = 0;\n    }\n}\nvar globalVar: string = \"\";\nfunction globalFunction() { }\n\nclass TestClass {\n    property: number;\n    method() { }\n    staticMethod() { }\n    testMethod(param: number) {\n        var localVar = 0;\n        function localFunction() {};\n        /*insideMethod*/\n    }\n}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyCompletions(t, "valueReference", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = [], EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Includes = ["localVariable", "exportedVariable", "localFunction", "exportedFunction", "localClass", "exportedClass", "localModule", "exportedModule", "exportedVariable2", "exportedFunction2", "exportedClass2", "exportedModule2"] } });
                f.VerifyCompletions(t, "typeReference", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Includes = ["localInterface", "exportedInterface", "localClass", "exportedClass", "exportedClass2"], Excludes = ["localModule", "exportedModule", "exportedModule2"] } });
                f.VerifyCompletions(t, "insideMethod", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Includes = ["globalVar", "globalFunction", "param", "localVar", "localFunction"], Excludes = ["property", "testMethod", "staticMethod"] } });
            }
            finally
            {
                done();
            }
        });
    }

}
