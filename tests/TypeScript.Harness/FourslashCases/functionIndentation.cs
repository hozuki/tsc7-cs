// ported from: internal/fourslash/tests/functionIndentation_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class FunctionIndentationCase
{
    public static void TestFunctionIndentation(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("namespace M {\nexport =\nC;\nclass C {\nconstructor(b\n) {\n}\nfoo(a\n: string) {\nreturn a\n|| true;\n}\nget bar(\n) {\nreturn 1;\n}\n}\nfunction foo(a,\nb?) {\nnew M.C(\n\"hello\");\n}\n{\n{\n}\n}\nfoo(\nfunction() {\n\"hello\";\n});\nfoo(\n() => {\n\"hello\";\n});\nvar t,\nu = 1,\nv;\n}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.FormatDocument(t, "");
                f.VerifyCurrentFileContent(t, "namespace M {\n" + "    export =\n" + "        C;\n" + "    class C {\n" + "        constructor(b\n" + "        ) {\n" + "        }\n" + "        foo(a\n" + "            : string) {\n" + "            return a\n" + "                || true;\n" + "        }\n" + "        get bar(\n" + "        ) {\n" + "            return 1;\n" + "        }\n" + "    }\n" + "    function foo(a,\n" + "        b?) {\n" + "        new M.C(\n" + "            \"hello\");\n" + "    }\n" + "    {\n" + "        {\n" + "        }\n" + "    }\n" + "    foo(\n" + "        function() {\n" + "            \"hello\";\n" + "        });\n" + "    foo(\n" + "        () => {\n" + "            \"hello\";\n" + "        });\n" + "    var t,\n" + "        u = 1,\n" + "        v;\n" + "}");
            }
            finally
            {
                done();
            }
        });
    }

}
