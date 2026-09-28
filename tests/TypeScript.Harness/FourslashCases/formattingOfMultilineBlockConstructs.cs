// ported from: internal/fourslash/tests/formattingOfMultilineBlockConstructs_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class FormattingOfMultilineBlockConstructsCase
{
    public static void TestFormattingOfMultilineBlockConstructs(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("namespace InternalModule/*1*/\n{\n}\ninterface MyInterface/*2*/\n{\n}\nenum E/*3*/\n{\n}\nclass MyClass/*4*/\n{\nconstructor()/*cons*/\n{ }\n        public MyFunction()/*5*/\n        {\n                return 0;\n        }\npublic get Getter()/*6*/\n{\n}\npublic set Setter(x)/*7*/\n{\n}\n}\nfunction foo()/*8*/\n{\n{}/*9*/\n}\n(function()/*10*/\n{\n});\n(() =>/*11*/\n{\n});\nvar x :/*12*/\n{};/*13*/"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.FormatDocument(t, "");
                f.GoToMarker(t, "1");
                f.VerifyCurrentLineContent(t, "namespace InternalModule {");
                f.GoToMarker(t, "2");
                f.VerifyCurrentLineContent(t, "interface MyInterface {");
                f.GoToMarker(t, "3");
                f.VerifyCurrentLineContent(t, "enum E {");
                f.GoToMarker(t, "4");
                f.VerifyCurrentLineContent(t, "class MyClass {");
                f.GoToMarker(t, "cons");
                f.VerifyCurrentLineContent(t, "    constructor() { }");
                f.GoToMarker(t, "5");
                f.VerifyCurrentLineContent(t, "    public MyFunction() {");
                f.GoToMarker(t, "6");
                f.VerifyCurrentLineContent(t, "    public get Getter() {");
                f.GoToMarker(t, "7");
                f.VerifyCurrentLineContent(t, "    public set Setter(x) {");
                f.GoToMarker(t, "8");
                f.VerifyCurrentLineContent(t, "function foo() {");
                f.GoToMarker(t, "9");
                f.VerifyCurrentLineContent(t, "    { }");
                f.GoToMarker(t, "10");
                f.VerifyCurrentLineContent(t, "(function() {");
                f.GoToMarker(t, "11");
                f.VerifyCurrentLineContent(t, "(() => {");
                f.GoToMarker(t, "12");
                f.VerifyCurrentLineContent(t, "var x:");
                f.GoToMarker(t, "13");
                f.VerifyCurrentLineContent(t, "    {};");
            }
            finally
            {
                done();
            }
        });
    }

}
