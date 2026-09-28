// ported from: internal/fourslash/tests/thisPredicateFunctionQuickInfo01_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class ThisPredicateFunctionQuickInfo01Case
{
    public static void TestThisPredicateFunctionQuickInfo01(TSU.IHarnessTest t)
    {
        t.Skip("Known failing fourslash test");
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("class FileSystemObject {\n    /*1*/isFile(): this is Item {\n        return this instanceof Item;\n    }\n    /*2*/isDirectory(): this is Directory {\n        return this instanceof Directory;\n    }\n    /*3*/isNetworked(): this is (Networked & this) {\n       return !!(this as Networked).host;\n    }\n    constructor(public path: string) {}\n}\n\nclass Item extends FileSystemObject {\n    constructor(path: string, public content: string) { super(path); }\n}\nclass Directory extends FileSystemObject {\n    children: FileSystemObject[];\n}\ninterface Networked {\n    host: string;\n}\n\nconst obj: FileSystemObject = new Item(\"/foo\", \"\");\nif (obj.isFile/*4*/()) {\n    obj.;\n    if (obj.isNetworked/*5*/()) {\n        obj.;\n    }\n}\nif (obj.isDirectory/*6*/()) {\n    obj.;\n    if (obj.isNetworked/*7*/()) {\n        obj.;\n    }\n}\nif (obj.isNetworked/*8*/()) {\n    obj.;\n}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyQuickInfoAt(t, "1", "(method) FileSystemObject.isFile(): this is Item", "");
                f.VerifyQuickInfoAt(t, "2", "(method) FileSystemObject.isDirectory(): this is Directory", "");
                f.VerifyQuickInfoAt(t, "3", "(method) FileSystemObject.isNetworked(): this is (Networked & this)", "");
                f.VerifyQuickInfoAt(t, "4", "(method) FileSystemObject.isFile(): this is Item", "");
                f.VerifyQuickInfoAt(t, "5", "(method) FileSystemObject.isNetworked(): this is (Networked & Item)", "");
                f.VerifyQuickInfoAt(t, "6", "(method) FileSystemObject.isDirectory(): this is Directory", "");
                f.VerifyQuickInfoAt(t, "7", "(method) FileSystemObject.isNetworked(): this is (Networked & Directory)", "");
                f.VerifyQuickInfoAt(t, "8", "(method) FileSystemObject.isNetworked(): this is (Networked & FileSystemObject)", "");
            }
            finally
            {
                done();
            }
        });
    }

}
