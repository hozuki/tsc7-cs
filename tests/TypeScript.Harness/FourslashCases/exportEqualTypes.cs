// ported from: internal/fourslash/tests/exportEqualTypes_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;
using U = TypeScript.Harness.FourslashCases.FourslashTestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class ExportEqualTypesCase
{
    public static void TestExportEqualTypes(TSU.IHarnessTest t)
    {
        t.Skip("Known failing fourslash test");
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @module: commonjs\n// @lib: es5\n// @strict: false\n// @Filename: exportEqualTypes_file0.ts\ninterface x {\n    (): Date;\n    foo: string;\n}\nexport = x;\n// @Filename: exportEqualTypes_file1.ts\n///<reference path='exportEqualTypes_file0.ts'/>\nimport test = require('./exportEqualTypes_file0');\nvar t: /*1*/test;  // var 't' should be of type 'test'\nvar /*2*/r1 = t(); // Should return a Date\nvar /*3*/r2 = t./*4*/foo; // t should have 'foo' in dropdown list and be of type 'string'"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyQuickInfoAt(t, "1", "(alias) interface test\nimport test = require('./exportEqualTypes_file0')", "");
                f.VerifyQuickInfoAt(t, "2", "var r1: Date", "");
                f.VerifyQuickInfoAt(t, "3", "var r2: string", "");
                f.VerifyCompletions(t, "4", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Exact = U.CompletionFunctionMembersWithPrototypePlus(["foo"]) } });
                f.VerifyNoErrors(t);
            }
            finally
            {
                done();
            }
        });
    }

}
