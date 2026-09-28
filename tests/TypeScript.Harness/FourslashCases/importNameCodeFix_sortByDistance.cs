// ported from: internal/fourslash/tests/importNameCodeFix_sortByDistance_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class ImportNameCodeFix_sortByDistanceCase
{
    public static void TestImportNameCodeFix_sortByDistance(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @module: commonjs\n// @Filename: /src/admin/utils/db/db.ts\nexport const db = {};\n// @Filename: /src/admin/utils/db/index.ts\nexport * from \"./db\";\n// @Filename: /src/client/helpers/db.ts\nexport const db = {};\n// @Filename: /src/client/db.ts\nexport const db = {};\n// @Filename: /src/client/foo.ts\ndb/**/"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.GoToMarker(t, "");
                f.VerifyImportFixAtPosition(t, ["import { db } from \"./db\";\n\ndb", "import { db } from \"./helpers/db\";\n\ndb", "import { db } from \"../admin/utils/db\";\n\ndb", "import { db } from \"../admin/utils/db/db\";\n\ndb"], null!);
            }
            finally
            {
                done();
            }
        });
    }

}
