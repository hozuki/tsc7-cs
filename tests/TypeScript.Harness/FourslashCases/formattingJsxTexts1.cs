// ported from: internal/fourslash/tests/formattingJsxTexts1_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class FormattingJsxTexts1Case
{
    public static void TestFormattingJsxTexts1(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("//@Filename: file.tsx\n<option>\n    homu   ;      homu\n    homu;homu\n    homu   :    homu\n    homu:homu\n    homu    ?     homu\n    homu    .    homu\n\n    homu    [   homu   ]   homu\n\n    !     homu\n    --    Type\n    homu    --\n    homu    ++\n    ++     homu\n\n    homu  ,   homu\n\n    var    homu\n    throw    homu\n    new    homu\n    delete   homu\n    return       homu\n    typeof     homu\n    await     homu\n\n    abstract  homu\n    class     homu\n    declare   homu\n    default   homu\n    enum      homu\n    export    homu\n    homu    extends   homu\n    get       homu\n    homu    implements     homu\n    interface      homu\n    module    homu\n    namespace      homu\n    private   homu\n    public    homu\n    protected      homu\n    set       homu\n    static    homu\n    type      homu\n\n    homu    =>    homu\n    homu=>homu\n\n    ...       homu\n\n    homu     @     homu\n    homu@homu\n\n    (    homu   )    homu\n</option>;"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.FormatDocument(t, "");
                f.VerifyCurrentFileContent(t, "<option>\n    homu   ;      homu\n    homu;homu\n    homu   :    homu\n    homu:homu\n    homu    ?     homu\n    homu    .    homu\n\n    homu    [   homu   ]   homu\n\n    !     homu\n    --    Type\n    homu    --\n    homu    ++\n    ++     homu\n\n    homu  ,   homu\n\n    var    homu\n    throw    homu\n    new    homu\n    delete   homu\n    return       homu\n    typeof     homu\n    await     homu\n\n    abstract  homu\n    class     homu\n    declare   homu\n    default   homu\n    enum      homu\n    export    homu\n    homu    extends   homu\n    get       homu\n    homu    implements     homu\n    interface      homu\n    module    homu\n    namespace      homu\n    private   homu\n    public    homu\n    protected      homu\n    set       homu\n    static    homu\n    type      homu\n\n    homu    =>    homu\n    homu=>homu\n\n    ...       homu\n\n    homu     @     homu\n    homu@homu\n\n    (    homu   )    homu\n</option>;");
            }
            finally
            {
                done();
            }
        });
    }

}
