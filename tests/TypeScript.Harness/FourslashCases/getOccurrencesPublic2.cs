// ported from: internal/fourslash/tests/getOccurrencesPublic2_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;
using U = TypeScript.Harness.FourslashCases.FourslashTestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class GetOccurrencesPublic2Case
{
    public static void TestGetOccurrencesPublic2(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("namespace m {\n    export class C1 {\n        public pub1;\n        public pub2;\n        private priv1;\n        private priv2;\n        protected prot1;\n        protected prot2;\n\n        public public;\n        private private;\n        protected protected;\n\n        public constructor(public a, private b, protected c, public d, private e, protected f) {\n            this.public = 10;\n            this.private = 10;\n            this.protected = 10;\n        }\n\n        public get x() { return 10; }\n        public set x(value) { }\n\n        public static statPub;\n        private static statPriv;\n        protected static statProt;\n    }\n\n    export interface I1 {\n    }\n\n    export declare namespace ma.m1.m2.m3 {\n        interface I2 {\n        }\n    }\n\n    export namespace mb.m1.m2.m3 {\n        declare var foo;\n\n        export class C2 {\n            [|public|] pub1;\n            private priv1;\n            protected prot1;\n\n            protected constructor([|public|] public, protected protected, private private) {\n                public = private = protected;\n            }\n        }\n    }\n\n    declare var ambientThing: number;\n    export var exportedThing = 10;\n    declare function foo(): string;\n}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyBaselineDocumentHighlights(t, null!, U.ToAny(f.Ranges()));
            }
            finally
            {
                done();
            }
        });
    }

}
