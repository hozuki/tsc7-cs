// ported from: internal/fourslash/tests/jsdocTypedefTag_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;
using U = TypeScript.Harness.FourslashCases.FourslashTestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class JsdocTypedefTagCase
{
    public static void TestJsdocTypedefTag(TSU.IHarnessTest t)
    {
        t.Skip("Known failing fourslash test");
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @lib: es5\n// @allowNonTsExtensions: true\n// @Filename: jsdocCompletion_typedef.js\n/** @typedef {(string | number)} NumberLike */\n\n/**\n * @typedef Animal - think Giraffes\n * @type {Object}\n * @property {string} animalName\n * @property {number} animalAge\n */\n\n/**\n * @typedef {Object} Person\n * @property {string} personName\n * @property {number} personAge\n */\n\n/**\n * @typedef {Object}\n * @property {string} catName\n * @property {number} catAge\n */\nvar Cat;\n\n/** @typedef {{ dogName: string, dogAge: number }} */\nvar Dog;\n\n/** @type {NumberLike} */\nvar numberLike; numberLike./*numberLike*/\n\n/** @type {Person} */\nvar p;p./*person*/;\np.personName./*personName*/;\np.personAge./*personAge*/;\n\n/** @type {/*AnimalType*/Animal} */\nvar a;a./*animal*/;\na.animalName./*animalName*/;\na.animalAge./*animalAge*/;\n\n/** @type {Cat} */\nvar c;c./*cat*/;\nc.catName./*catName*/;\nc.catAge./*catAge*/;\n\n/** @type {Dog} */\nvar d;d./*dog*/;\nd.dogName./*dogName*/;\nd.dogAge./*dogAge*/;"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.MarkTestAsStradaServer();
                f.VerifyCompletions(t, "numberLike", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Includes = ["charAt", "toExponential"] } });
                f.VerifyCompletions(t, "person", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Includes = ["personName", "personAge"] } });
                f.VerifyCompletions(t, "personName", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Includes = ["charAt"] } });
                f.VerifyCompletions(t, "personAge", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Includes = ["toExponential"] } });
                f.VerifyCompletions(t, "animal", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Includes = ["animalName", "animalAge"] } });
                f.VerifyCompletions(t, "animalName", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Includes = ["charAt"] } });
                f.VerifyCompletions(t, "animalAge", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Includes = ["toExponential"] } });
                f.VerifyCompletions(t, "dog", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Includes = ["dogName", "dogAge"] } });
                f.VerifyCompletions(t, "dogName", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Includes = ["charAt"] } });
                f.VerifyCompletions(t, "dogAge", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Includes = ["toExponential"] } });
                f.VerifyCompletions(t, "cat", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Includes = ["catName", "catAge"] } });
                f.VerifyCompletions(t, "catName", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Includes = ["charAt"] } });
                f.VerifyCompletions(t, "catAge", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Includes = ["toExponential"] } });
                f.VerifyQuickInfoAt(t, "AnimalType", "type Animal = {\n    animalName: string;\n    animalAge: number;\n}", "- think Giraffes");
            }
            finally
            {
                done();
            }
        });
    }

}
