// ported from: internal/fourslash/tests/formattingTypeInfer_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class FormattingTypeInferCase
{
    public static void TestFormattingTypeInfer(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("\n/*L1*/type C<T> = T extends Array<infer U> ? U : never;\n\n/*L2*/  type   C  <  T  >   =   T   extends   Array   <   infer     U  >  ?   U   :   never  ; \n\n/*L3*/type C<T> = T extends Array<infer U> ? U : T;\n\n/*L4*/  type   C  <  T  >   =   T   extends   Array   <   infer     U  >  ?   U   :   T  ;  \n\n/*L5*/type Foo<T> = T extends { a: infer U, b: infer U } ? U : never;\n\n/*L6*/  type   Foo  <  T  > = T   extends   {   a  :   infer   U  ,   b  :   infer   U   }   ?   U   :   never  ;  \n\n/*L7*/type Bar<T> = T extends { a: (x: infer U) => void, b: (x: infer U) => void } ? U : never;\n\n/*L8*/  type   Bar  <  T  >   =   T   extends   {   a  :   (x  :  infer  U  ) =>   void  ,   b  :   (x  :   infer   U  )   =>   void   }    ?   U   :   never  ;\n"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.FormatDocument(t, "");
                f.GoToMarker(t, "L1");
                f.VerifyCurrentLineContent(t, "type C<T> = T extends Array<infer U> ? U : never;");
                f.GoToMarker(t, "L2");
                f.VerifyCurrentLineContent(t, "type C<T> = T extends Array<infer U> ? U : never;");
                f.GoToMarker(t, "L3");
                f.VerifyCurrentLineContent(t, "type C<T> = T extends Array<infer U> ? U : T;");
                f.GoToMarker(t, "L4");
                f.VerifyCurrentLineContent(t, "type C<T> = T extends Array<infer U> ? U : T;");
                f.GoToMarker(t, "L5");
                f.VerifyCurrentLineContent(t, "type Foo<T> = T extends { a: infer U, b: infer U } ? U : never;");
                f.GoToMarker(t, "L6");
                f.VerifyCurrentLineContent(t, "type Foo<T> = T extends { a: infer U, b: infer U } ? U : never;");
                f.GoToMarker(t, "L7");
                f.VerifyCurrentLineContent(t, "type Bar<T> = T extends { a: (x: infer U) => void, b: (x: infer U) => void } ? U : never;");
                f.GoToMarker(t, "L8");
                f.VerifyCurrentLineContent(t, "type Bar<T> = T extends { a: (x: infer U) => void, b: (x: infer U) => void } ? U : never;");
            }
            finally
            {
                done();
            }
        });
    }

}
