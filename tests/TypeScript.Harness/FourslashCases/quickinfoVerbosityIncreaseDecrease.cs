// ported from: internal/fourslash/tests/quickinfoVerbosityIncreaseDecrease_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class QuickinfoVerbosityIncreaseDecreaseCase
{
    public static void TestQuickinfoVerbosityIncreaseDecrease(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("export const JOB_STATES = [\"created\", \"active\", \"completed\", \"failed\", \"retry\", \"cancelled\", \"archive\"] as const\nexport type JobState = (typeof JOB_STATES)[number]\ntype Color = \"default\" | \"primary\" | \"secondary\" | \"success\" | \"warning\" | \"danger\"\nconst JobsStateToColor/*a*/: Record<\n  JobState,\n  {\n    color: Color\n    label: string\n    labelPlural: string\n  }\n> = {\n  created: {\n    color: \"success\",\n    label: \"\u041D\u0430\u043F\u0440\u0430\u0432\u0459\u0435\u043D\",\n    labelPlural: \"\u041D\u0430\u043F\u0440\u0430\u0432\u0459\u0435\u043D\u0438\",\n  },\n  active: {\n    color: \"success\",\n    label: \"\u0410\u043A\u0442\u0438\u0432\u0430\u043D\",\n    labelPlural: \"\u0410\u043A\u0442\u0438\u0432\u043D\u0438\",\n  },\n  completed: {\n    color: \"success\",\n    label: \"\u0423\u0441\u043F\u0435\u0448\u0430\u043D\",\n    labelPlural: \"\u0423\u0441\u043F\u0435\u0448\u043D\u0438\",\n  },\n  cancelled: {\n    color: \"default\",\n    label: \"\u041E\u0442\u0430\u043A\u0430\u0437\u0430\u043D\",\n    labelPlural: \"\u041E\u0442\u0430\u043A\u0430\u0437\u043D\u0438\",\n  },\n  failed: {\n    color: \"danger\",\n    label: \"\u041F\u0430\u043E\",\n    labelPlural: \"\u041F\u0430\u043B\u0438\",\n  },\n  archive: {\n    color: \"default\",\n    label: \"\u0410\u0440\u0445\u0438\u0432\u0438\u0440\u0430\u043D\",\n    labelPlural: \"\u0410\u0440\u0445\u0438\u0432\u0438\u0440\u0430\u043D\u0438\",\n  },\n  retry: {\n    color: \"warning\",\n    label: \"\u041F\u043E\u043D\u0430\u0432\u0459\u0430 \u0441\u0435\",\n    labelPlural: \"\u041F\u043E\u043D\u0430\u0432\u0459\u0430\u0458\u0443 \u0441\u0435\",\n  },\n}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyBaselineHoverWithVerbosity(t, new Dictionary<string, int[]> { ["a"] = [0, 1, 0] });
            }
            finally
            {
                done();
            }
        });
    }

}
