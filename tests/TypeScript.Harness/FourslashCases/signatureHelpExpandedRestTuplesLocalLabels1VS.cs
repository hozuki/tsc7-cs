// ported from: internal/fourslash/tests/signatureHelpExpandedRestTuplesLocalLabels1VS_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using LSP = TypeScript.LsProto;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class SignatureHelpExpandedRestTuplesLocalLabels1VSCase
{
    public static void TestSignatureHelpExpandedRestTuplesLocalLabels1VS(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("interface AppleInfo {\n  color: \"green\" | \"red\";\n}\n\ninterface BananaInfo {\n  curvature: number;\n}\n\ntype FruitAndInfo1 = [\"apple\", AppleInfo] | [\"banana\", BananaInfo];\n\nfunction logFruitTuple1(...[fruit, info]: FruitAndInfo1) {}\nlogFruitTuple1(/*1*/);\n\nfunction logFruitTuple2(...[, info]: FruitAndInfo1) {}\nlogFruitTuple2(/*2*/);\nlogFruitTuple2(\"apple\", /*3*/);\n\nfunction logFruitTuple3(...[fruit, ...rest]: FruitAndInfo1) {}\nlogFruitTuple3(/*4*/);\nlogFruitTuple3(\"apple\", /*5*/);\nfunction logFruitTuple4(...[fruit, ...[info]]: FruitAndInfo1) {}\nlogFruitTuple4(/*6*/);\nlogFruitTuple4(\"apple\", /*7*/);\n\ntype FruitAndInfo2 = [\"apple\", ...AppleInfo[]] | [\"banana\", ...BananaInfo[]];\n\nfunction logFruitTuple5(...[fruit, firstInfo]: FruitAndInfo2) {}\nlogFruitTuple5(/*8*/);\nlogFruitTuple5(\"apple\", /*9*/);\n\nfunction logFruitTuple6(...[fruit, ...fruitInfo]: FruitAndInfo2) {}\nlogFruitTuple6(/*10*/);\nlogFruitTuple6(\"apple\", /*11*/);\n\ntype FruitAndInfo3 = [\"apple\", ...AppleInfo[], number] | [\"banana\", ...BananaInfo[], number];\n\nfunction logFruitTuple7(...[fruit, fruitInfoOrNumber, secondFruitInfoOrNumber]: FruitAndInfo3) {}\nlogFruitTuple7(/*12*/);\nlogFruitTuple7(\"apple\", /*13*/);\nlogFruitTuple7(\"apple\", { color: \"red\" }, /*14*/);\n\nfunction logFruitTuple8(...[fruit, , secondFruitInfoOrNumber]: FruitAndInfo3) {}\nlogFruitTuple8(/*15*/);\nlogFruitTuple8(\"apple\", /*16*/);\nlogFruitTuple8(\"apple\", { color: \"red\" }, /*17*/);\n\nfunction logFruitTuple9(...[...[fruit, fruitInfoOrNumber, secondFruitInfoOrNumber]]: FruitAndInfo3) {}\nlogFruitTuple9(/*18*/);\nlogFruitTuple9(\"apple\", /*19*/);\nlogFruitTuple9(\"apple\", { color: \"red\" }, /*20*/);\n\nfunction logFruitTuple10(...[fruit, {}, secondFruitInfoOrNumber]: FruitAndInfo3) {}\nlogFruitTuple10(/*21*/);\nlogFruitTuple10(\"apple\", /*22*/);\nlogFruitTuple10(\"apple\", { color: \"red\" }, /*23*/);\n\nfunction logFruitTuple11(...{}: FruitAndInfo3) {}\nlogFruitTuple11(/*24*/);\nlogFruitTuple11(\"apple\", /*25*/);\nlogFruitTuple11(\"apple\", { color: \"red\" }, /*26*/);\nfunction withPair(...[first, second]: [number, named: string]) {}\nwithPair(/*27*/);\nwithPair(101, /*28*/);"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, new LSP.ClientCapabilities { VSSupportsVisualStudioExtensions = true }, content);
            try
            {
                f.VerifyBaselineSignatureHelp(t);
            }
            finally
            {
                done();
            }
        });
    }

}
