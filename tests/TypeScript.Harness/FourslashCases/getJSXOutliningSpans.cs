// ported from: internal/fourslash/tests/getJSXOutliningSpans_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class GetJSXOutliningSpansCase
{
    public static void TestGetJSXOutliningSpans(TSU.IHarnessTest t)
    {
        t.Skip("Known failing fourslash test");
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("import React, { Component } from 'react';\n\nexport class Home extends Component[| {\n  render()[| {\n    return [|(\n    [|<div>\n      [|<h1>Hello, world!</h1>|]\n      [|<ul>\n        [|<li>\n          [|<a [|href='https://get.asp.net/'|]>\n            ASP.NET Core\n          </a>|]\n        </li>|]\n        [|<li>[|<a [|href='https://facebook.github.io/react/'|]>React</a>|] for client-side code</li>|]\n        [|<li>[|<a [|href='http://getbootstrap.com/'|]>Bootstrap</a>|] for layout and styling</li>|]\n      </ul>|]\n      <div\n        [|accesskey=\"test\"\n        class=\"active\"\n        dir=\"auto\"|] />\n      <PageHeader [|title=\"Log in\"\n        {...[|{\n          item: true,\n          xs: 9,\n          md: 5\n        }|]}|]\n      />\n      [|<>\n          text \n      </>|]\n    </div>|]\n    )|];\n  }|]\n}|]"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyOutliningSpans(t);
            }
            finally
            {
                done();
            }
        });
    }

}
