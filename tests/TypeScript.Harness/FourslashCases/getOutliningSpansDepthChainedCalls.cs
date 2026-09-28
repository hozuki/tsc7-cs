// ported from: internal/fourslash/tests/getOutliningSpansDepthChainedCalls_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class GetOutliningSpansDepthChainedCallsCase
{
    public static void TestGetOutliningSpansDepthChainedCalls(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("declare var router: any;\nrouter\n    .get[|(\"/\", async(ctx) =>[|{\n        ctx.body = \"base\";\n    }|])|]\n    .post[|(\"/a\", async(ctx) =>[|{\n        //a\n    }|])|]\n    .get[|(\"/\", async(ctx) =>[|{\n        ctx.body = \"base\";\n    }|])|]\n    .post[|(\"/a\", async(ctx) =>[|{\n        //a\n    }|])|]\n    .get[|(\"/\", async(ctx) =>[|{\n        ctx.body = \"base\";\n    }|])|]\n    .post[|(\"/a\", async(ctx) =>[|{\n        //a\n    }|])|]\n    .get[|(\"/\", async(ctx) =>[|{\n        ctx.body = \"base\";\n    }|])|]\n    .post[|(\"/a\", async(ctx) =>[|{\n        //a\n    }|])|]\n    .get[|(\"/\", async(ctx) =>[|{\n        ctx.body = \"base\";\n    }|])|]\n    .post[|(\"/a\", async(ctx) =>[|{\n        //a\n    }|])|]\n    .get[|(\"/\", async(ctx) =>[|{\n        ctx.body = \"base\";\n    }|])|]\n    .post[|(\"/a\", async(ctx) =>[|{\n        //a\n    }|])|]\n    .get[|(\"/\", async(ctx) =>[|{\n        ctx.body = \"base\";\n    }|])|]\n    .post[|(\"/a\", async(ctx) =>[|{\n        //a\n    }|])|]\n    .get[|(\"/\", async(ctx) =>[|{\n        ctx.body = \"base\";\n    }|])|]\n    .post[|(\"/a\", async(ctx) =>[|{\n        //a\n    }|])|]\n    .get[|(\"/\", async(ctx) =>[|{\n        ctx.body = \"base\";\n    }|])|]\n    .post[|(\"/a\", async(ctx) =>[|{\n        //a\n    }|])|]\n    .get[|(\"/\", async(ctx) =>[|{\n        ctx.body = \"base\";\n    }|])|]\n    .post[|(\"/a\", async(ctx) =>[|{\n        //a\n    }|])|]\n    .get[|(\"/\", async(ctx) =>[|{\n        ctx.body = \"base\";\n    }|])|]\n    .post[|(\"/a\", async(ctx) =>[|{\n        //a\n    }|])|]\n    .get[|(\"/\", async(ctx) =>[|{\n        ctx.body = \"base\";\n    }|])|]\n    .post[|(\"/a\", async(ctx) =>[|{\n        //a\n    }|])|]\n    .get[|(\"/\", async(ctx) =>[|{\n        ctx.body = \"base\";\n    }|])|]\n    .post[|(\"/a\", async(ctx) =>[|{\n        //a\n    }|])|]\n    .get[|(\"/\", async(ctx) =>[|{\n        ctx.body = \"base\";\n    }|])|]\n    .post[|(\"/a\", async(ctx) =>[|{\n        //a\n    }|])|]\n    .get[|(\"/\", async(ctx) =>[|{\n        ctx.body = \"base\";\n    }|])|]\n    .post[|(\"/a\", async(ctx) =>[|{\n        //a\n    }|])|]\n    .get[|(\"/\", async(ctx) =>[|{\n        ctx.body = \"base\";\n    }|])|]\n    .post[|(\"/a\", async(ctx) =>[|{\n        //a\n    }|])|]\n    .get[|(\"/\", async(ctx) =>[|{\n        ctx.body = \"base\";\n    }|])|]\n    .post[|(\"/a\", async(ctx) =>[|{\n        //a\n    }|])|]\n    .get[|(\"/\", async(ctx) =>[|{\n        ctx.body = \"base\";\n    }|])|]\n    .post[|(\"/a\", async(ctx) =>[|{\n        //a\n    }|])|]"u8);
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
