// ported from: internal/fourslash/tests/goToSource15_bundler_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class GoToSource15_bundlerCase
{
    public static void TestGoToSource15_bundler(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @Filename: /home/src/workspaces/project/tsconfig.json\n{ \"compilerOptions\": { \"module\": \"esnext\", \"moduleResolution\": \"bundler\", \"lib\": [\"es5\"] } }\n// @Filename: /home/src/workspaces/project/node_modules/react/package.json\n{ \"name\": \"react\", \"version\": \"16.8.6\", \"main\": \"index.js\" }\n// @Filename: /home/src/workspaces/project/node_modules/react/index.js\n'use strict';\n\nif (process.env.NODE_ENV === 'production') {\n  module.exports = require('./cjs/react.production.min.js');\n} else {\n  module.exports = require('./cjs/react.development.js');\n}\n// @Filename: /home/src/workspaces/project/node_modules/react/cjs/react.production.min.js\n'use strict';exports./*production*/useState=function(a){};exports.version='16.8.6';\n// @Filename: /home/src/workspaces/project/node_modules/react/cjs/react.development.js\n'use strict';\nif (process.env.NODE_ENV !== 'production') {\n  (function() {\n    function useState(initialState) {}\n    exports./*development*/useState = useState;\n    exports.version = '16.8.6';\n  }());\n}\n// @Filename: /home/src/workspaces/project/index.ts\nimport { [|/*start*/useState|] } from 'react';"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.MarkTestAsStradaServer();
                f.VerifyBaselineGoToSourceDefinition(t, "start");
            }
            finally
            {
                done();
            }
        });
    }

}
