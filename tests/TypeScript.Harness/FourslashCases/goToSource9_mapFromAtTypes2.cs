// ported from: internal/fourslash/tests/goToSource9_mapFromAtTypes2_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class GoToSource9_mapFromAtTypes2Case
{
    public static void TestGoToSource9_mapFromAtTypes2(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @lib: es5\n// @moduleResolution: bundler\n// @Filename: /home/src/workspaces/project/node_modules/lodash/package.json\n{ \"name\": \"lodash\", \"version\": \"4.17.15\", \"main\": \"./lodash.js\" }\n// @Filename: /home/src/workspaces/project/node_modules/lodash/lodash.js\n;(function() {\n    /**\n     * Adds two numbers.\n     *\n     * @static\n     * @memberOf _\n     * @since 3.4.0\n     * @category Math\n     * @param {number} augend The first number in an addition.\n     * @param {number} addend The second number in an addition.\n     * @returns {number} Returns the total.\n     * @example\n     *\n     * _.add(6, 4);\n     * // => 10\n     */\n    var [|/*variable*/add|] = createMathOperation(function(augend, addend) {\n     return augend + addend;\n    }, 0);\n\n    function lodash(value) {}\n    lodash.[|/*property*/add|] = add;\n\n    /** Detect free variable `global` from Node.js. */\n    var freeGlobal = typeof global == 'object' && global && global.Object === Object && global;\n    /** Detect free variable `self`. */\n    var freeSelf = typeof self == 'object' && self && self.Object === Object && self;\n    /** Used as a reference to the global object. */\n    var root = freeGlobal || freeSelf || Function('return this')();\n    /** Detect free variable `exports`. */\n    var freeExports = typeof exports == 'object' && exports && !exports.nodeType && exports;////     \n    /** Detect free variable `module`. */\n    var freeModule = freeExports && typeof module == 'object' && module && !module.nodeType && module;\n    if (freeModule) {\n      // Export for Node.js.\n      (freeModule.exports = _)._ = _;\n      // Export for CommonJS support.\n      freeExports._ = _;\n    }\n    else {\n      // Export to the global object.\n      root._ = _;\n    }\n}.call(this));\n// @Filename: /home/src/workspaces/project/node_modules/@types/lodash/package.json\n{ \"name\": \"@types/lodash\", \"version\": \"4.14.97\", \"types\": \"index.d.ts\" }\n// @Filename: /home/src/workspaces/project/node_modules/@types/lodash/index.d.ts\n/// <reference path=\"./common/math.d.ts\" />\nexport = _;\nexport as namespace _;\ndeclare const _: _.LoDashStatic;\ndeclare namespace _ {\n    interface LoDashStatic {}\n}\n// @Filename: /home/src/workspaces/project/node_modules/@types/lodash/common/math.d.ts\nimport _ = require(\"../index\");\ndeclare module \"../index\" {\n    interface LoDashStatic {\n        add(augend: number, addend: number): number;\n    }\n}\n// @Filename: /home/src/workspaces/project/index.ts\nimport [|/*defaultImport*/_|], { [|/*unresolvableNamedImport*/foo|] } from [|/*moduleSpecifier*/'lodash'|];\n_.[|/*propertyAccess*/add|]"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.MarkTestAsStradaServer();
                f.VerifyBaselineGoToSourceDefinition(t, "defaultImport", "unresolvableNamedImport", "moduleSpecifier");
            }
            finally
            {
                done();
            }
        });
    }

}
