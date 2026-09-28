// ported from: internal/fourslash/tests/codeFixMissingTypeAnnotationOnExports28-long-types_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class CodeFixMissingTypeAnnotationOnExports28_long_typesCase
{
    public static void TestCodeFixMissingTypeAnnotationOnExports28_long_types(TSU.IHarnessTest t)
    {
        t.Skip("Known failing fourslash test");
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @strict: false\n// @isolatedDeclarations: true\n// @declaration: true\nexport const sessionLoader = {\n    async loadSession() {\n        if (Math.random() > 0.5) {\n            return {\n                PROP_1: {\n                    name: false,\n                },\n                PROPERTY_2: {\n                    name: 1,\n                },\n                PROPERTY_3: {\n                    name: 1\n                },\n                PROPERTY_4: {\n                    name: 315,\n                },\n            };\n        }\n\n        return {\n            PROP_1: {\n                name: false,\n            },\n            PROPERTY_2: {\n                name: undefined,\n            },\n            PROPERTY_3: {\n            },\n            PROPERTY_4: {\n                name: 576,\n            },\n        };\n    },\n};"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyCodeFixAvailable(t, ["Add return type 'Promise<{\n    PROP_1: {\n        name: boolean;\n    };\n    PROPERTY_2: {\n        name: number;\n    };\n    PROPERTY_3: {\n        name: number;\n    };\n    PROPE...'"]);
                f.VerifyCodeFix(t, new F.VerifyCodeFixOptions { Description = "Add return type 'Promise<{\n    PROP_1: {\n        name: boolean;\n    };\n    PROPERTY_2: {\n        name: number;\n    };\n    PROPERTY_3: {\n        name: number;\n    };\n    PROPE...'", NewFileContent = "export const sessionLoader = {\n    async loadSession(): Promise<{\n        PROP_1: {\n            name: boolean;\n        };\n        PROPERTY_2: {\n            name: number;\n        };\n        PROPERTY_3: {\n            name: number;\n        };\n        PROPERTY_4: {\n            name: number;\n        };\n    } | {\n        PROP_1: {\n            name: boolean;\n        };\n        PROPERTY_2: {\n            name: any;\n        };\n        PROPERTY_3: {\n            name?: undefined;\n        };\n        PROPERTY_4: {\n            name: number;\n        };\n    }> {\n        if (Math.random() > 0.5) {\n            return {\n                PROP_1: {\n                    name: false,\n                },\n                PROPERTY_2: {\n                    name: 1,\n                },\n                PROPERTY_3: {\n                    name: 1\n                },\n                PROPERTY_4: {\n                    name: 315,\n                },\n            };\n        }\n\n        return {\n            PROP_1: {\n                name: false,\n            },\n            PROPERTY_2: {\n                name: undefined,\n            },\n            PROPERTY_3: {\n            },\n            PROPERTY_4: {\n                name: 576,\n            },\n        };\n    },\n};", Index = 0 });
            }
            finally
            {
                done();
            }
        });
    }

}
