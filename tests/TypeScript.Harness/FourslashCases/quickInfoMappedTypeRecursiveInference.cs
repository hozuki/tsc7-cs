// ported from: internal/fourslash/tests/quickInfoMappedTypeRecursiveInference_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class QuickInfoMappedTypeRecursiveInferenceCase
{
    public static void TestQuickInfoMappedTypeRecursiveInference(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @Filename: test.ts\ninterface A { a: A }\ndeclare let a: A;\ntype Deep<T> = { [K in keyof T]: Deep<T[K]> }\ndeclare function foo<T>(deep: Deep<T>): T;\nconst out/*1*/ = foo/*2*/(a);\nout.a/*3*/\nout.a.a/*4*/\nout.a.a.a.a.a.a.a/*5*/\n\ninterface B { [s: string]: B }\ndeclare let b: B;\nconst oub/*6*/ = foo/*7*/(b);\noub.b/*8*/\noub.b.b/*9*/\noub.b.a.n.a.n.a/*10*/"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyQuickInfoAt(t, "1", "const out: {\n    a: {\n        a: ...;\n    };\n}", "");
                f.VerifyQuickInfoAt(t, "2", "function foo<{\n    a: {\n        a: ...;\n    };\n}>(deep: Deep<{\n    a: {\n        a: ...;\n    };\n}>): {\n    a: {\n        a: ...;\n    };\n}", "");
                f.VerifyQuickInfoAt(t, "3", "(property) a: {\n    a: {\n        a: ...;\n    };\n}", "");
                f.VerifyQuickInfoAt(t, "4", "(property) a: {\n    a: {\n        a: ...;\n    };\n}", "");
                f.VerifyQuickInfoAt(t, "5", "(property) a: {\n    a: {\n        a: ...;\n    };\n}", "");
                f.VerifyQuickInfoAt(t, "6", "const oub: {\n    [x: string]: ...;\n}", "");
                f.VerifyQuickInfoAt(t, "7", "function foo<{\n    [x: string]: ...;\n}>(deep: Deep<{\n    [x: string]: ...;\n}>): {\n    [x: string]: ...;\n}", "");
                f.VerifyQuickInfoAt(t, "8", "{\n    [x: string]: ...;\n}", "");
                f.VerifyQuickInfoAt(t, "9", "{\n    [x: string]: ...;\n}", "");
                f.VerifyQuickInfoAt(t, "10", "{\n    [x: string]: ...;\n}", "");
            }
            finally
            {
                done();
            }
        });
    }

}
