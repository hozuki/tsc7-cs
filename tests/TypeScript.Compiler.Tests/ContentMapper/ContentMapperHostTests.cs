// ported from: internal/contentmapper/host_test.go
//
// 与 Go 测试的差异（逐条）：
// 1) `errors.AsType[*X](err)`（沿 Unwrap 找错误链里的某个类型）→ 本文件的 `FindCause<T>(Exception)`：沿
//    `InnerException` 链找第一个 T（C# 的 error → 异常映射见 docs/architecture.md §3.2）。Go 的
//    `Unwrap` 在 C# 里就是 `InnerException`（见 ContentMapper/Host.cs 文件头第 1 条）。
// 2) `t.Context()` → `Context.Background`（C# 侧没有 testing.T 的 context；Go 那边它只是"测试结束时取消"）。
// 3) `chan struct{}` + `select`（阻塞/放行、超时）→ `ManualResetEventSlim` + `Wait(TimeSpan)`；Go 的
//    "立刻检查 SetLocale 是否完成"（select 的 default 分支）→ `Wait(300ms)` 后断言未完成（C# 没有非阻塞的
//    "锁是否已获取"查询；这是时序等价的写法，见 TestHostSetLocaleWaitsForTransform 注释）。
// 4) `assert.Equal(t, x, io.Discard)` → `Assert.Same(Stream.Null, stderr)`（HostImpl 无 logger 时传的就是 Stream.Null）。
// 5) `t.Run(name, …)` 的表驱动用例 → `[Theory]`/`[MemberData]`（docs/architecture.md §5）。
// 6) Go 的 `unicodeMapper`/`recordingMapper` 等假 mapper 见 ContentMapperTestSupport.cs。
// 7) `TestRunnerForwardsProjectOptions` 里 Go 用 `json.Marshal(compilerOptions)` 生成期望值；C# 的
//    `CompilerOptions` 还没有 IJsonMarshaler（docs/json.md §4：本轮不实现），所以这里改成"解析 host 发出去的
//    compilerOptions JSON，断言其中的字段值"（`target`/`strict`），语义等价且不是拿被测代码的输出当期望。
// 8) `TestRunnerPositionEncodings` 的期望值：Go 里归一化后的坐标是**字节**偏移（Go 的 string 是 UTF-8），
//    所以 "éx" 的段端点是 2/3；C# 的 `core.TextPos` 是 **UTF-16 代码单元**下标（docs/architecture.md §3.1，
//    见 HostImpl 的 `PositionNormalizer` 注释），同一个位置的期望值因此是 1/2。两种编码下 C# 的期望值相同
//    （正是这个用例要钉的"归一化到内部坐标后编码无关"）。
using System.Text;
using TypeScript.ContentMapper;
using TypeScript.Core;
using TypeScript.SpanMap;
using Xunit;

using static TypeScript.ContentMapper.ContentMapper;
using static TypeScript.Json.Json;
// Go 里 `contentmapper.Diagnostic`/`contentmapper.MappedDiagnosticDirective`（协议 DTO）与
// `ast.Diagnostic`/`ast.MappedDiagnosticDirective` 同名但不同包；C# 的 using 会把两个命名空间都拉进来 →
// 必须用别名还原 Go 的包限定（ContentMapper 侧 = 协议 DTO，Ast 侧 = 解析后的结果类型）。
using AstDirectivePolicy = TypeScript.Ast.MappedDiagnosticDirectivePolicy;
using MappedDiagnosticDirective = TypeScript.ContentMapper.MappedDiagnosticDirective;
using Loc = TypeScript.Locale.Locale;
using Path = TypeScript.TSPath.Path;

namespace TypeScript.Tests;

public class ContentMapperHostTests
{
    /// <summary>Go 的 <c>errors.AsType[T](err)</c>：沿 InnerException 链找第一个 T。</summary>
    private static T? FindCause<T>(Exception error)
        where T : Exception
    {
        for (Exception? current = error; current is not null; current = current.InnerException)
        {
            if (current is T match)
            {
                return match;
            }
        }

        return null;
    }

    private static Mapper Mapper_(string name, string? version = null, string[]? exec = null) => new()
    {
        Name = name,
        Version = version ?? "",
        Exec = exec ?? ["mapper"],
    };

    [Fact]
    public void TestRunnerTransform()
    {
        var host = NewHost(Context.Background, new FakeSpawner(), Loc.Default);
        try
        {
            var mapper = new Mapper { Extensions = [".vue"], Name = "vue", Version = "1.0.0", Exec = ["vue-mapper"] };
            var result = host.Transform(mapper, new Request { FileName = "/a.vue", Content = "export const x = 1;" });
            Assert.Equal("export const x = 1;", result.Text);
            Assert.Equal(".ts", result.VirtualExtension);
            Assert.NotNull(result.Mappings);
            Assert.Single(result.Diagnostics);
            Assert.Equal(9999, result.Diagnostics[0].Code());
            Assert.Equal("vue", result.Diagnostics[0].Source());
        }
        finally
        {
            host.Close();
        }
    }

    [Fact]
    public void TestHostLogging()
    {
        var mu = new object();
        var logs = new List<string>();
        Logger logger = message =>
        {
            lock (mu)
            {
                logs.Add(message);
            }
        };
        Spawner spawner = new SpawnerFunc((command, dir, stderr) =>
        {
            stderr.Write("mapper diagnostic\n"u8);
            return new FakeSpawner().Spawn(command, dir, stderr);
        });
        var host = NewHostWithOptions(Context.Background, spawner, Loc.Default, new HostOptions { Logger = logger });
        try
        {
            var mapper = new Mapper
            {
                Package = "configured",
                Extensions = [".vue"],
                Name = "resolved",
                Version = "1.0.0",
                Exec = ["mapper"],
            };
            host.Transform(mapper, new Request { FileName = "/a.vue", Content = "export const x = 1;" });
        }
        finally
        {
            host.Close();
        }

        string joined;
        lock (mu)
        {
            joined = string.Join("\n", logs);
        }

        Assert.Contains("[content mapper: resolved] send: {\"jsonrpc\":\"2.0\",\"id\":\"api1\",\"method\":\"initialize\"", joined);
        Assert.Contains("[content mapper: resolved] receive: {\"jsonrpc\":\"2.0\",\"id\":\"api1\",\"result\":", joined);
        Assert.Contains("[content mapper: resolved] stderr: mapper diagnostic", joined);
    }

    [Fact]
    public void TestHostDiscardsStderrWithoutLogging()
    {
        Spawner spawner = new SpawnerFunc((command, dir, stderr) =>
        {
            Assert.Same(Stream.Null, stderr);
            return new FakeSpawner().Spawn(command, dir, stderr);
        });
        var host = NewHost(Context.Background, spawner, Loc.Default);
        try
        {
            var mapper = new Mapper
            {
                Package = "configured",
                Extensions = [".vue"],
                Name = "resolved",
                Version = "1.0.0",
                Exec = ["mapper"],
            };
            host.Transform(mapper, new Request { FileName = "/a.vue", Content = "export const x = 1;" });
        }
        finally
        {
            host.Close();
        }
    }

    public static TheoryData<string, string, string, string> DiagnosticNameCases() => new()
    {
        { "configured", "resolved", "contributed", "resolved" },
        { "configured", "", "contributed", "configured" },
        { "", "", "contributed", "contributed" },
    };

    [Theory]
    [MemberData(nameof(DiagnosticNameCases))]
    public void TestMapperDiagnosticName(string package, string name, string contributionId, string want)
    {
        var mapper = new Mapper { Package = package, Name = name, ContributionID = contributionId };
        Assert.Equal(want, mapper.DiagnosticName());
    }

    [Fact]
    public void TestRunnerTransformResponseValidationMalformedResultFailsTheRequest()
    {
        var host = NewHost(
            Context.Background,
            new FakeSpawner
            {
                Handler = new ResponseMapper(_ => new Dictionary<string, object?> { ["text"] = 1 }),
            },
            Loc.Default);
        try
        {
            var mapper = new Mapper { Extensions = [".vue"], Name = "mapper", Exec = ["mapper"] };
            var error = Assert.ThrowsAny<Exception>(() => host.Transform(mapper, new Request { FileName = "/a.vue", Content = "a" }));
            Assert.IsType<TransformError>(error);
        }
        finally
        {
            host.Close();
        }
    }

    [Fact]
    public void TestHostClosesProcessWhenReadLoopFails()
    {
        var closed = new ManualResetEventSlim(false);
        Spawner spawner = new SpawnerFunc((command, dir, stderr) =>
        {
            var (client, server) = TestPipes.Create();
            _ = Task.Run(() =>
            {
                try
                {
                    var protocol = Ipc.JSONRPCProtocol.NewJSONRPCProtocol(server);
                    var message = protocol.ReadMessage();
                    protocol.WriteResponse(message!.ID, new InitializeResult
                    {
                        PositionEncoding = PositionEncoding.UTF8,
                        DiagnosticSource = "mapper",
                    });
                    server.Write("oops\n"u8);
                }
                catch (Exception)
                {
                    // Go 的 goroutine 里 assert 失败会 panic 掉测试；这里让 host 侧的失败来暴露问题。
                }
                finally
                {
                    server.Dispose();
                }
            });
            return new CloseSignalStream(client, closed);
        });
        var host = NewHost(Context.Background, spawner, Loc.Default);
        try
        {
            var mapper = Mapper_("mapper");
            Assert.ThrowsAny<Exception>(() => host.Transform(mapper, new Request { FileName = "/a.vue", Content = "" }));
        }
        finally
        {
            host.Close();
        }

        Assert.True(closed.Wait(TimeSpan.FromSeconds(1)), "mapper process was not closed after its read loop failed");
    }

    [Fact]
    public void TestHostReportsInitializationTimeoutBeforeClosingProcess()
    {
        Spawner spawner = new SpawnerFunc((command, dir, stderr) =>
        {
            var (client, server) = TestPipes.Create();
            _ = Task.Run(() =>
            {
                try
                {
                    Ipc.JSONRPCProtocol.NewJSONRPCProtocol(server).ReadMessage();
                    // Go: `<-t.Context().Done()`（不再响应，直到 host 拆掉连接）。
                    var buffer = new byte[1];
                    while (server.Read(buffer, 0, 1) > 0)
                    {
                    }
                }
                catch (Exception)
                {
                }
            });
            return new ExitOnCloseStream(client);
        });
        var host = NewHost(Context.Background, spawner, Loc.Default);
        try
        {
            var mapper = Mapper_("mapper");
            var error = Assert.ThrowsAny<Exception>(() => host.Transform(mapper, new Request { FileName = "/a.vue", Content = "" }));
            var initializeError = FindCause<InitializeError>(error);
            Assert.NotNull(initializeError);
            Assert.Equal(InitializeErrorKind.NoResponse, initializeError.Kind);
        }
        finally
        {
            host.Close();
        }
    }

    [Fact]
    public void TestHostReportsProcessExitBeforeInitialization()
    {
        Spawner spawner = new SpawnerFunc((command, dir, stderr) =>
        {
            var (client, server) = TestPipes.Create();
            server.Dispose();
            return new ExitedStream(client, 42);
        });
        var host = NewHost(Context.Background, spawner, Loc.Default);
        try
        {
            var mapper = Mapper_("mapper");
            var error = Assert.ThrowsAny<Exception>(() => host.Transform(mapper, new Request { FileName = "/a.vue", Content = "" }));
            var initializeError = FindCause<InitializeError>(error);
            Assert.NotNull(initializeError);
            Assert.Equal(InitializeErrorKind.ProcessExit, initializeError.Kind);
            Assert.Equal(42, initializeError.ExitCode);
        }
        finally
        {
            host.Close();
        }
    }

    private static Result TransformWithDirectives(string text, DiagnosticDirectives? directives)
    {
        var host = NewHost(
            Context.Background,
            new FakeSpawner
            {
                Handler = new ResponseMapper(_ => new TransformResult
                {
                    Text = text,
                    Extension = ".ts",
                    DiagnosticDirectives = directives,
                }),
            },
            Loc.Default);
        try
        {
            var mapper = new Mapper { Extensions = [".vue"], Name = "mapper", Exec = ["mapper"] };
            return host.Transform(mapper, new Request { FileName = "/a.vue", Content = "directive\nsource" });
        }
        finally
        {
            host.Close();
        }
    }

    [Fact]
    public void TestRunnerTransformDiagnosticDirectives()
    {
        var result = TransformWithDirectives("virtual source", ProtocolDiagnostics.Directives(
            [
                new MappedDiagnosticDirective
                {
                    OriginalStart = 0,
                    OriginalLength = 9,
                    VirtualStart = 8,
                    VirtualEnd = 14,
                    Policy = DiagnosticDirectivePolicy.Expect,
                },
            ],
            new UnusedExpectDirectiveDiagnostic { Code = 2578, MessageText = "Unused framework directive." }));
        Assert.Single(result.DiagnosticDirectives);
        var directive = result.DiagnosticDirectives[0];
        Assert.Equal(0, directive.OriginalRange.Pos());
        Assert.Equal(9, directive.OriginalRange.End());
        Assert.Equal(8, directive.VirtualRange.Pos());
        Assert.Equal(14, directive.VirtualRange.End());
        Assert.Equal(AstDirectivePolicy.Expect, directive.Policy);
        Assert.Equal(2578, directive.UnusedCode);
        Assert.Equal("Unused framework directive.", directive.UnusedMessageText);
        Assert.Equal("mapper", directive.Source);

        result = TransformWithDirectives("virtual source", ProtocolDiagnostics.Directives(
            [
                new MappedDiagnosticDirective
                {
                    OriginalLength = 9,
                    VirtualStart = 8,
                    VirtualEnd = 14,
                    Policy = DiagnosticDirectivePolicy.Expect,
                    UnusedExpectDirectiveIndex = 1,
                },
            ],
            new UnusedExpectDirectiveDiagnostic { Code = 1, MessageText = "first" },
            new UnusedExpectDirectiveDiagnostic { Code = 2, MessageText = "second" }));
        Assert.Equal(2, result.DiagnosticDirectives[0].UnusedCode);
        Assert.Equal("second", result.DiagnosticDirectives[0].UnusedMessageText);

        TransformWithDirectives("x", ProtocolDiagnostics.Directives(
            [
                new MappedDiagnosticDirective
                {
                    OriginalStart = -1,
                    Policy = DiagnosticDirectivePolicy.Ignore,
                },
            ],
            new UnusedExpectDirectiveDiagnostic()));
    }

    public static TheoryData<string, string, DiagnosticDirectivePolicy, int?, int, DiagnosticDirectiveErrorKind, bool> InvalidDirectiveCases() => new()
    {
        // name, text, policy, unusedIndex, originalStart, expected kind, add empty unused diagnostic
        { "invalid range", "x", DiagnosticDirectivePolicy.Ignore, null, 0, DiagnosticDirectiveErrorKind.InvalidRange, false },
        { "unknown policy", "x", (DiagnosticDirectivePolicy)2, null, 0, DiagnosticDirectiveErrorKind.InvalidPolicy, false },
        { "expect requires unused diagnostic", "x", DiagnosticDirectivePolicy.Expect, null, 0, DiagnosticDirectiveErrorKind.ExpectMissingUnusedDiagnostic, false },
        { "original range out of bounds", "x", DiagnosticDirectivePolicy.Expect, null, 99, DiagnosticDirectiveErrorKind.InvalidRange, true },
        { "virtual range out of bounds", "x", DiagnosticDirectivePolicy.Ignore, null, 0, DiagnosticDirectiveErrorKind.InvalidRange, false },
    };

    [Theory]
    [MemberData(nameof(InvalidDirectiveCases))]
    public void TestRunnerTransformDiagnosticDirectivesInvalid(
        string name,
        string text,
        DiagnosticDirectivePolicy policy,
        int? unusedIndex,
        int originalStart,
        DiagnosticDirectiveErrorKind kind,
        bool addUnusedDiagnostic)
    {
        var diagnostics = ProtocolDiagnostics.Directives(
            [
                new MappedDiagnosticDirective
                {
                    OriginalStart = originalStart,
                    VirtualStart = name == "invalid range" ? -1 : name == "virtual range out of bounds" ? 99 : 0,
                    Policy = policy,
                    UnusedExpectDirectiveIndex = unusedIndex,
                },
            ],
            addUnusedDiagnostic ? [new UnusedExpectDirectiveDiagnostic()] : []);
        var error = Assert.ThrowsAny<Exception>(() => TransformWithDirectives(text, diagnostics));
        var directiveError = FindCause<DiagnosticDirectiveError>(error);
        Assert.NotNull(directiveError);
        Assert.Equal(kind, directiveError.Kind);
    }

    [Fact]
    public void TestRunnerTransformDiagnosticDirectivesMultipleUnusedRequireIndex()
    {
        var diagnostics = ProtocolDiagnostics.Directives(
            [
                new MappedDiagnosticDirective { Policy = DiagnosticDirectivePolicy.Expect },
            ],
            new UnusedExpectDirectiveDiagnostic(),
            new UnusedExpectDirectiveDiagnostic());
        var error = Assert.ThrowsAny<Exception>(() => TransformWithDirectives("x", diagnostics));
        var directiveError = FindCause<DiagnosticDirectiveError>(error);
        Assert.NotNull(directiveError);
        Assert.Equal(DiagnosticDirectiveErrorKind.ExpectMissingUnusedDiagnostic, directiveError.Kind);
    }

    [Fact]
    public void TestRunnerTransformDiagnosticDirectivesOverlap()
    {
        var diagnostics = ProtocolDiagnostics.Directives(
        [
            new MappedDiagnosticDirective { VirtualEnd = 2, Policy = DiagnosticDirectivePolicy.Ignore },
            new MappedDiagnosticDirective { VirtualStart = 1, VirtualEnd = 3, Policy = DiagnosticDirectivePolicy.Ignore },
        ]);
        var error = Assert.ThrowsAny<Exception>(() => TransformWithDirectives("abc", diagnostics));
        var directiveError = FindCause<DiagnosticDirectiveError>(error);
        Assert.NotNull(directiveError);
        Assert.Equal(DiagnosticDirectiveErrorKind.Overlap, directiveError.Kind);
    }

    [Theory]
    [InlineData("[0,9,8,14,0]", DiagnosticDirectivePolicy.Ignore)]
    [InlineData("[0,9,8,14,1]", DiagnosticDirectivePolicy.Expect)]
    public void TestMappedDiagnosticDirectiveJSON(string want, DiagnosticDirectivePolicy policy)
    {
        var directive = new MappedDiagnosticDirective
        {
            VirtualStart = 8,
            VirtualEnd = 14,
            OriginalStart = 0,
            OriginalLength = 9,
            Policy = policy,
        };
        var data = Marshal(directive);
        Assert.Equal(want, Encoding.UTF8.GetString(data));
        var decoded = UnmarshalNew<MappedDiagnosticDirective>(data);
        Assert.Equal(directive.OriginalStart, decoded.OriginalStart);
        Assert.Equal(directive.OriginalLength, decoded.OriginalLength);
        Assert.Equal(directive.VirtualStart, decoded.VirtualStart);
        Assert.Equal(directive.VirtualEnd, decoded.VirtualEnd);
        Assert.Equal(directive.Policy, decoded.Policy);
        Assert.Equal(directive.UnusedExpectDirectiveIndex, decoded.UnusedExpectDirectiveIndex);
    }

    [Theory]
    [InlineData("[0,0,0,0]")]
    [InlineData("[0,0,0,0,0,1,2]")]
    public void TestMappedDiagnosticDirectiveJSONRejectsBadTuples(string data)
    {
        var error = Assert.ThrowsAny<Exception>(() => UnmarshalNew<MappedDiagnosticDirective>(Encoding.UTF8.GetBytes(data)));
        Assert.Contains("diagnostic directive tuple", error.Message);
    }

    [Fact]
    public void TestDiagnosticDirectivesJSON()
    {
        var directives = new DiagnosticDirectives
        {
            UnusedExpectDirectiveDiagnostics =
            [
                new UnusedExpectDirectiveDiagnostic { Code = 1, MessageText = "first" },
                new UnusedExpectDirectiveDiagnostic { Code = 2, MessageText = "second" },
            ],
            Directives =
            [
                new MappedDiagnosticDirective
                {
                    OriginalStart = 2,
                    OriginalLength = 3,
                    VirtualStart = 5,
                    VirtualEnd = 9,
                    Policy = DiagnosticDirectivePolicy.Expect,
                    UnusedExpectDirectiveIndex = 1,
                },
            ],
        };
        var data = Marshal(directives);
        Assert.Equal(
            "{\"unusedExpectDirectiveDiagnostics\":[{\"code\":1,\"messageText\":\"first\"},{\"code\":2,\"messageText\":\"second\"}],\"directives\":[[2,3,5,9,1,1]]}",
            Encoding.UTF8.GetString(data));
        var decoded = UnmarshalNew<DiagnosticDirectives>(data);
        Assert.Equal(2, decoded.UnusedExpectDirectiveDiagnostics.Length);
        Assert.Equal("second", decoded.UnusedExpectDirectiveDiagnostics[1].MessageText);
        Assert.Equal(1, decoded.Directives[0].UnusedExpectDirectiveIndex);
    }

    [Fact]
    public void TestRunnerTransformSupplementalOutputs()
    {
        var host = NewHost(
            Context.Background,
            new FakeSpawner
            {
                Handler = new ResponseMapper(_ => new TransformResult
                {
                    Text = "export default 1;",
                    Extension = ".ts",
                    Supplemental =
                    [
                        new SupplementalOutput
                        {
                            Text = "declare const first: string;",
                            Extension = ".ts",
                            DiagnosticDirectives = ProtocolDiagnostics.Directives(
                            [
                                new MappedDiagnosticDirective { VirtualEnd = 7, Policy = DiagnosticDirectivePolicy.Ignore },
                            ]),
                        },
                        new SupplementalOutput { Text = "declare const second: number;", Extension = ".mjs" },
                    ],
                }),
            },
            Loc.Default);
        try
        {
            var mapper = new Mapper { Extensions = [".vue"], Name = "mapper", Exec = ["mapper"] };
            var result = host.Transform(mapper, new Request { FileName = "/component.vue", Content = "component" });
            Assert.Equal(2, result.Supplemental.Length);
            Assert.Equal("declare const first: string;", result.Supplemental[0].Text);
            Assert.Equal(".ts", result.Supplemental[0].VirtualExtension);
            Assert.NotNull(result.Supplemental[0].Mappings);
            Assert.Single(result.Supplemental[0].DiagnosticDirectives);
            Assert.Equal(7, result.Supplemental[0].DiagnosticDirectives[0].VirtualRange.End());
            Assert.Equal(".mjs", result.Supplemental[1].VirtualExtension);
            Assert.NotNull(result.Supplemental[1].Mappings);
        }
        finally
        {
            host.Close();
        }
    }

    [Fact]
    public void TestRunnerTransformInvalidSupplementalDiagnosticDirective()
    {
        var host = NewHost(
            Context.Background,
            new FakeSpawner
            {
                Handler = new ResponseMapper(_ => new TransformResult
                {
                    Text = "export {};",
                    Extension = ".ts",
                    Supplemental =
                    [
                        new SupplementalOutput { Text = "export {};", Extension = ".ts" },
                        new SupplementalOutput
                        {
                            Text = "export {};",
                            Extension = ".ts",
                            DiagnosticDirectives = ProtocolDiagnostics.Directives(
                            [
                                new MappedDiagnosticDirective { Policy = DiagnosticDirectivePolicy.Expect },
                            ]),
                        },
                    ],
                }),
            },
            Loc.Default);
        try
        {
            var mapper = new Mapper { Extensions = [".vue"], Name = "mapper", Exec = ["mapper"] };
            var error = Assert.ThrowsAny<Exception>(() => host.Transform(mapper, new Request { FileName = "/component.vue", Content = "component" }));
            var directiveError = FindCause<DiagnosticDirectiveError>(error);
            Assert.NotNull(directiveError);
            Assert.Equal(DiagnosticDirectiveErrorKind.ExpectMissingUnusedDiagnostic, directiveError.Kind);
            Assert.Equal(0, directiveError.Index);
            Assert.Equal(1, directiveError.SupplementalIndex);
        }
        finally
        {
            host.Close();
        }
    }

    [Theory]
    [InlineData(false, "")]
    [InlineData(false, ".coffee")]
    [InlineData(true, "")]
    [InlineData(true, ".coffee")]
    public void TestRunnerRejectsInvalidVirtualExtension(bool supplemental, string extension)
    {
        var host = NewHost(
            Context.Background,
            new FakeSpawner
            {
                Handler = new ResponseMapper(_ =>
                {
                    var canonicalExtension = extension;
                    SupplementalOutput[] supplementalOutputs = [];
                    if (supplemental)
                    {
                        canonicalExtension = ".ts";
                        supplementalOutputs = [new SupplementalOutput { Text = "export {};", Extension = extension }];
                    }

                    return new TransformResult
                    {
                        Text = "export {};",
                        Extension = canonicalExtension,
                        Supplemental = supplementalOutputs,
                    };
                }),
            },
            Loc.Default);
        try
        {
            var mapper = new Mapper { Extensions = [".vue"], Name = "mapper", Exec = ["mapper"] };
            var error = Assert.ThrowsAny<Exception>(() => host.Transform(mapper, new Request { FileName = "/component.vue", Content = "component" }));
            Assert.Contains("invalid virtual extension", error.Message);
        }
        finally
        {
            host.Close();
        }
    }

    [Theory]
    [InlineData("utf-8")]
    [InlineData("utf-16")]
    public void TestRunnerPositionEncodings(string encodingName)
    {
        var encoding = new PositionEncoding(encodingName);
        var host = NewHost(Context.Background, new FakeSpawner { Handler = new UnicodeMapper(encoding) }, Loc.Default);
        try
        {
            var mapper = new Mapper { Name = encodingName, Exec = ["mapper"] };
            var result = host.Transform(mapper, new Request { FileName = "/a.vue", Content = "éx" });
            var segments = result.Mappings!.Segments();
            Assert.Equal(2, segments.Length);
            Assert.Equal(1, segments[0].VirtualEnd.Value);
            Assert.Equal(1, segments[0].OriginalEnd.Value);
            Assert.Equal(1, segments[1].VirtualStart.Value);
            Assert.Equal(1, segments[1].OriginalStart.Value);
            Assert.Equal("éx", result.Text);
            Assert.Null(result.Mappings.Validate(result.Text, "éx"));
            var (mapped, fidelity) = result.Mappings.VirtualToOriginalPosition(new TextPos(1));
            Assert.Equal(1, mapped.Value);
            Assert.Equal(Fidelity.Exact, fidelity);
            Assert.Equal(1, result.Diagnostics[0].Loc().Pos());
            Assert.Equal(2, result.Diagnostics[0].Loc().End());
            Assert.Equal(1, result.DiagnosticDirectives[0].OriginalRange.Pos());
            Assert.Equal(2, result.DiagnosticDirectives[0].OriginalRange.End());
            Assert.Equal(1, result.DiagnosticDirectives[0].VirtualRange.Pos());
            Assert.Equal(2, result.DiagnosticDirectives[0].VirtualRange.End());
        }
        finally
        {
            host.Close();
        }
    }

    [Fact]
    public void TestRunnerRejectsUnsupportedPositionEncoding()
    {
        var host = NewHost(Context.Background, new FakeSpawner { Handler = new UnicodeMapper(new PositionEncoding("utf-32")) }, Loc.Default);
        try
        {
            var mapper = Mapper_("invalid");
            var error = Assert.ThrowsAny<Exception>(() => host.Transform(mapper, new Request { FileName = "/a.vue", Content = "x" }));
            Assert.Contains("unsupported position encoding", error.Message);
        }
        finally
        {
            host.Close();
        }
    }

    [Theory]
    [InlineData("", "diagnostic source must not be empty")]
    [InlineData(" ", "diagnostic source must not be empty")]
    [InlineData("ts", "is reserved by TypeScript")]
    [InlineData("TS", "is reserved by TypeScript")]
    [InlineData("d.ts", "is reserved by TypeScript")]
    [InlineData("json", "is reserved by TypeScript")]
    [InlineData("typescript", "is reserved by TypeScript")]
    [InlineData("TypeScript", "is reserved by TypeScript")]
    [InlineData("tsc", "is reserved by TypeScript")]
    [InlineData("TSC", "is reserved by TypeScript")]
    public void TestRunnerRejectsInvalidDiagnosticSource(string source, string expectedMessage)
    {
        var host = NewHost(
            Context.Background,
            new FakeSpawner { Handler = new UnicodeMapper(PositionEncoding.UTF8, source) },
            Loc.Default);
        try
        {
            var mapper = Mapper_("invalid");
            var error = Assert.ThrowsAny<Exception>(() => host.Transform(mapper, new Request { FileName = "/a.vue", Content = "x" }));
            Assert.Contains(expectedMessage, error.Message);
        }
        finally
        {
            host.Close();
        }
    }

    [Theory]
    [InlineData("utf-8", "é")]
    [InlineData("utf-16", "😀")]
    public void TestRunnerRejectsPositionsInsideUnicodeCharacters(string encodingName, string content)
    {
        var host = NewHost(
            Context.Background,
            new FakeSpawner { Handler = new InvalidDiagnosticMapper(new PositionEncoding(encodingName)) },
            Loc.Default);
        try
        {
            var mapper = Mapper_(encodingName);
            var error = Assert.ThrowsAny<Exception>(() => host.Transform(mapper, new Request { FileName = "/a.vue", Content = content }));
            Assert.Contains("splits a Unicode code point", error.Message);
        }
        finally
        {
            host.Close();
        }
    }

    [Fact]
    public void TestRunnerConsolidatesByIdentity()
    {
        var spawner = new FakeSpawner();
        var host = NewHost(Context.Background, spawner, Loc.Default);
        try
        {
            var vueA = new Mapper { Package = "a", Name = "vue", Version = "1.0.0", Exec = ["vue-mapper"] };
            var vueB = new Mapper { Package = "b", Name = "vue", Version = "1.0.0", Exec = ["vue-mapper"] };
            var svelte = new Mapper { Name = "svelte", Version = "2.0.0", Exec = ["svelte-mapper"] };
            var project = host.Project(new ProjectSpec { Mappers = [vueA, vueB, svelte], CompilerOptions = new CompilerOptions() });
            try
            {
                foreach (var mapper in new[] { vueA, vueB, vueA, svelte })
                {
                    project.Transform(mapper, new Request { FileName = "/x", Content = "y" });
                }
            }
            finally
            {
                project.Close();
            }

            Assert.Equal(2, spawner.Spawns);
        }
        finally
        {
            host.Close();
        }
    }

    [Fact]
    public void TestRunnerLeaseLifecycle()
    {
        var spawner = new FakeSpawner();
        var host = NewHost(Context.Background, spawner, Loc.Default);
        try
        {
            var vueA = new Mapper { Package = "a", Name = "vue", Version = "1.0.0", Exec = ["vue-mapper"] };
            var vueB = new Mapper { Package = "b", Name = "vue", Version = "1.0.0", Exec = ["vue-mapper"] };
            var svelte = new Mapper { Name = "svelte", Version = "2.0.0", Exec = ["svelte-mapper"] };

            var releaseVueA = host.Acquire([vueA, vueA]);
            var releaseVueB = host.Acquire([vueB]);
            var releaseSvelte = host.Acquire([svelte]);
            foreach (var mapper in new[] { vueA, svelte })
            {
                host.Transform(mapper, new Request { FileName = "/x", Content = "y" });
            }

            Assert.Equal(2, spawner.Spawns);

            releaseVueA();
            Assert.Equal(0, spawner.Closes);
            releaseSvelte();
            Assert.Equal(1, spawner.Closes);
            releaseVueB();
            releaseVueB();
            Assert.Equal(2, spawner.Closes);

            var releaseNew = host.Acquire([vueA]);
            host.Transform(vueA, new Request { FileName = "/x", Content = "y" });
            Assert.Equal(3, spawner.Spawns);
            releaseNew();
            Assert.Equal(3, spawner.Closes);
        }
        finally
        {
            host.Close();
        }
    }

    [Fact]
    public void TestProjectLifecycle()
    {
        var mapperProcess = new RecordingMapper { DynamicConfig = true };
        var spawner = new FakeSpawner { Handler = mapperProcess };
        var host = NewHost(Context.Background, spawner, Loc.Default);
        try
        {
            var staticMapper = new Mapper
            {
                Options = "{\"mode\":\"static\"}"u8.ToArray(),
                Name = "static",
                Version = "1.0.0",
                Exec = ["mapper"],
            };
            var staticProject = host.Project(new ProjectSpec
            {
                ConfigFileName = "/repo/tsconfig.json",
                Mappers = [staticMapper],
                CompilerOptions = new CompilerOptions(),
            });
            Assert.Equal(0, spawner.Spawns);
            var staticIdentities = staticProject.Identities();
            Assert.Single(staticIdentities);
            staticProject.Close();

            var dynamicA = new Mapper
            {
                Options = "{\"mode\":\"a\"}"u8.ToArray(),
                Name = "dynamic",
                Version = "1.0.0",
                Exec = ["mapper"],
                CompilerOptions = ["jsx"],
                DynamicConfig = true,
            };
            var dynamicB = new Mapper
            {
                Options = "{\"mode\":\"b\"}"u8.ToArray(),
                Name = "dynamic",
                Version = "1.0.0",
                Exec = ["mapper"],
                DynamicConfig = true,
            };
            var dynamicAOptions = new CompilerOptions();
            var projectA = host.Project(new ProjectSpec
            {
                ConfigFileName = "/repo/a/tsconfig.json",
                Mappers = [dynamicA, dynamicB],
                CompilerOptions = dynamicAOptions,
            });
            var projectAReversed = host.Project(new ProjectSpec
            {
                ConfigFileName = "/repo/reversed/tsconfig.json",
                Mappers = [dynamicB, dynamicA],
                CompilerOptions = dynamicAOptions,
            });
            var projectDifferentOptions = host.Project(new ProjectSpec
            {
                ConfigFileName = "/repo/options/tsconfig.json",
                Mappers = [dynamicA],
                CompilerOptions = new CompilerOptions { Jsx = JsxEmit.React },
            });
            var projectB = host.Project(new ProjectSpec
            {
                ConfigFileName = "/repo/b/tsconfig.json",
                Mappers = [dynamicA],
                CompilerOptions = new CompilerOptions(),
            });
            var projectAAgain = host.Project(new ProjectSpec
            {
                ConfigFileName = "/repo/a/tsconfig.json",
                Mappers = [dynamicA, dynamicB],
                CompilerOptions = dynamicAOptions,
            });
            Assert.Equal(0, spawner.Spawns);
            var projectAIdentities = projectA.Identities();
            var projectAReversedIdentities = projectAReversed.Identities();
            var projectDifferentOptionIdentities = projectDifferentOptions.Identities();
            var projectBIdentities = projectB.Identities();
            Assert.Equal(2, projectAIdentities.Length);
            Assert.Equal(2, projectAReversedIdentities.Length);
            Assert.Equal(projectAIdentities[0], projectAReversedIdentities[1]);
            Assert.Equal(projectAIdentities[1], projectAReversedIdentities[0]);
            Assert.NotEqual(projectAIdentities[0], projectDifferentOptionIdentities[0]);
            Assert.Single(projectBIdentities);
            Assert.Equal(1, spawner.Spawns);
            var projectAWatchedFiles = projectA.WatchedFiles();
            var projectBWatchedFiles = projectB.WatchedFiles();
            Assert.Single(projectAWatchedFiles);
            Assert.Single(projectBWatchedFiles);

            projectA.Transform(dynamicB, new Request { FileName = "/repo/a/file.ext", Content = "x" });
            Assert.Contains(mapperProcess.TransformHandle, mapperProcess.ProjectHandlesSnapshot().Take(2));

            projectAAgain.Close();
            projectA.Close();
            projectAReversed.Close();
            projectDifferentOptions.Close();
            projectB.Close();
            var timings = host.Timings();
            var dynamicTimings = timings.Mappers[dynamicA.Identity()];
            Assert.Equal(1UL, dynamicTimings.Spawn.Count);
            Assert.Equal(1UL, dynamicTimings.Initialize.Count);
            Assert.Equal(6UL, dynamicTimings.OpenProject.Count);
            Assert.Equal(1UL, dynamicTimings.Transform.Count);
            Assert.Equal(6UL, dynamicTimings.CloseProject.Count);
            Assert.Equal(6, mapperProcess.ProjectHandlesSnapshot().Count);
            Assert.Equal(6, mapperProcess.ClosedHandlesSnapshot().Count);
        }
        finally
        {
            host.Close();
        }
    }

    [Fact]
    public void TestProjectMethodsAfterHostClose()
    {
        var mapperProcess = new RecordingMapper { DynamicConfig = true };
        var host = NewHost(Context.Background, new FakeSpawner { Handler = mapperProcess }, Loc.Default);
        var mapper = new Mapper { Name = "dynamic", Version = "1.0.0", Exec = ["mapper"], DynamicConfig = true };
        var project = host.Project(new ProjectSpec
        {
            ConfigFileName = "/repo/tsconfig.json",
            Mappers = [mapper],
            CompilerOptions = new CompilerOptions(),
        });
        try
        {
            project.Transform(mapper, new Request { FileName = "/repo/file.ext", Content = "x" });
            Assert.Equal("", project.Identity(new Mapper()));
            host.Close();

            project.Refresh();
            Assert.Empty(project.Identities());
            Assert.Equal("", project.Identity(mapper));
            Assert.Equal("", project.Identity(new Mapper()));
            Assert.Empty(project.WatchedFiles());
            Assert.Empty(project.Diagnostics());
            var error = Assert.ThrowsAny<Exception>(() => project.Transform(mapper, new Request { FileName = "/repo/file.ext", Content = "x" }));
            Assert.Contains("content mapper project is closed", error.Message);
        }
        finally
        {
            project.Close();
            host.Close();
        }
    }

    [Fact]
    public void TestProjectRejectsRelativeWatchedFiles()
    {
        var mapperProcess = new RecordingMapper { WatchedFiles = ["mapper.config.js"], DynamicConfig = true };
        var host = NewHost(Context.Background, new FakeSpawner { Handler = mapperProcess }, Loc.Default);
        try
        {
            var projectMapper = new Mapper
            {
                Package = "dynamic",
                Name = "dynamic",
                Version = "1.0.0",
                Exec = ["mapper"],
                DynamicConfig = true,
            };
            var project = host.Project(new ProjectSpec
            {
                ConfigFileName = "/repo/tsconfig.json",
                Mappers = [projectMapper],
                CompilerOptions = new CompilerOptions(),
            });
            var error = Assert.ThrowsAny<Exception>(() => project.Transform(projectMapper, new Request { FileName = "/repo/file.ext", Content = "x" }));
            var transformError = FindCause<TransformError>(error);
            Assert.NotNull(transformError);
            var projectError = FindCause<ProjectError>(error);
            Assert.NotNull(projectError);
            Assert.Equal(ProjectErrorKind.NonAbsoluteWatchedFile, projectError.Kind);
            project.Close();
        }
        finally
        {
            host.Close();
        }
    }

    [Fact]
    public void TestDynamicProjectRequiresConfigIdentity()
    {
        var mapperProcess = new RecordingMapper { ConfigIdentity = "", DynamicConfig = true };
        var host = NewHost(Context.Background, new FakeSpawner { Handler = mapperProcess }, Loc.Default);
        try
        {
            var projectMapper = new Mapper
            {
                Package = "dynamic",
                Name = "dynamic",
                Version = "1.0.0",
                Exec = ["mapper"],
                DynamicConfig = true,
            };
            var project = host.Project(new ProjectSpec
            {
                ConfigFileName = "/repo/tsconfig.json",
                Mappers = [projectMapper],
                CompilerOptions = new CompilerOptions(),
            });
            var error = Assert.ThrowsAny<Exception>(() => project.Transform(projectMapper, new Request { FileName = "/repo/file.ext", Content = "x" }));
            Assert.NotNull(FindCause<TransformError>(error));
            var projectError = FindCause<ProjectError>(error);
            Assert.NotNull(projectError);
            Assert.Equal(ProjectErrorKind.MissingConfigIdentity, projectError.Kind);
            project.Close();
        }
        finally
        {
            host.Close();
        }
    }

    public static TheoryData<string, string, ProjectErrorKind> StaticMapperRejectsDynamicFieldsCases() => new()
    {
        { "config identity", "dynamic", ProjectErrorKind.UnexpectedConfigIdentity },
        { "watched files", "", ProjectErrorKind.UnexpectedWatchedFiles },
    };

    [Theory]
    [MemberData(nameof(StaticMapperRejectsDynamicFieldsCases))]
    public void TestStaticMapperRejectsDynamicProjectResponseFields(string name, string configIdentity, ProjectErrorKind kind)
    {
        var mapperProcess = name == "config identity"
            ? new RecordingMapper { ConfigIdentity = configIdentity }
            : new RecordingMapper { WatchedFiles = ["/repo/mapper.config.js"] };
        var host = NewHost(Context.Background, new FakeSpawner { Handler = mapperProcess }, Loc.Default);
        try
        {
            var projectMapper = new Mapper { Name = "static", Version = "1.0.0", Exec = ["mapper"] };
            var project = host.Project(new ProjectSpec
            {
                ConfigFileName = "/repo/tsconfig.json",
                Mappers = [projectMapper],
                CompilerOptions = new CompilerOptions(),
            });
            try
            {
                var error = Assert.ThrowsAny<Exception>(() => project.Transform(projectMapper, new Request { FileName = "/repo/file.ext", Content = "x" }));
                Assert.NotNull(FindCause<TransformError>(error));
                var projectError = FindCause<ProjectError>(error);
                Assert.NotNull(projectError);
                Assert.Equal(kind, projectError.Kind);
            }
            finally
            {
                project.Close();
            }
        }
        finally
        {
            host.Close();
        }
    }

    [Fact]
    public void TestProjectRejectsInvalidOptionDiagnosticPath()
    {
        var mapperProcess = new RecordingMapper
        {
            OptionDiagnostics =
            [
                new OptionDiagnosticResult
                {
                    Path = ["null"u8.ToArray()],
                    MessageText = "Invalid option.",
                    Code = 123,
                },
            ],
        };
        var host = NewHost(Context.Background, new FakeSpawner { Handler = mapperProcess }, Loc.Default);
        try
        {
            var projectMapper = new Mapper { Name = "mapper", Version = "1.0.0", Exec = ["mapper"] };
            var project = host.Project(new ProjectSpec
            {
                Mappers = [projectMapper],
                CompilerOptions = new CompilerOptions(),
            });
            try
            {
                var error = Assert.ThrowsAny<Exception>(() => project.Transform(projectMapper, new Request { FileName = "/repo/file.ext", Content = "x" }));
                Assert.NotNull(FindCause<TransformError>(error));
                var projectError = FindCause<ProjectError>(error);
                Assert.NotNull(projectError);
                Assert.Equal(ProjectErrorKind.MalformedResponse, projectError.Kind);
            }
            finally
            {
                project.Close();
            }
        }
        finally
        {
            host.Close();
        }
    }

    [Fact]
    public void TestRunnerForwardsProjectOptions()
    {
        var mapperProcess = new RecordingMapper();
        Assert.True(Loc.TryParse("cs-CZ", out var diagnosticLocale));
        var host = NewHost(Context.Background, new FakeSpawner { Handler = mapperProcess }, diagnosticLocale);
        try
        {
            var mapperDefinition = new Mapper
            {
                Options = "{\"strictTemplates\":true}"u8.ToArray(),
                Name = "vue",
                Version = "1.0.0",
                Exec = ["vue-mapper"],
                CompilerOptions = ["target", "jsx"],
            };
            var compilerOptions = new CompilerOptions { Target = ScriptTarget.ES2020, Strict = Tristate.TSTrue };
            var project = host.Project(new ProjectSpec { Mappers = [mapperDefinition], CompilerOptions = compilerOptions });
            try
            {
                project.Transform(mapperDefinition, new Request { FileName = "/a.vue", Content = "x" });
            }
            finally
            {
                project.Close();
            }

            // Go 这里比对 `json.Marshal(compilerOptions)`；C# 的 CompilerOptions 还没有 IJsonMarshaler
            // （docs/json.md §4），所以改为解析 host 发出去的 JSON 后断言字段值（见文件头第 7 条）。
            // Go 的 ScriptTarget 没有 MarshalJSON → 写成底层整数（7 = es2020）；Tristate 有 → 写成布尔。
            var received = UnmarshalNew<Dictionary<string, object?>>(Encoding.UTF8.GetBytes(mapperProcess.Received));
            Assert.Equal((double)ScriptTarget.ES2020, Assert.IsType<double>(received["target"]));
            Assert.True(Assert.IsType<bool>(received["strict"]));
            Assert.Equal("{\"strictTemplates\":true}", mapperProcess.ReceivedOptions);
            Assert.Equal("cs-CZ", mapperProcess.ReceivedLocale);
            Assert.DoesNotContain("\"options\"", mapperProcess.TransformParams);
            Assert.DoesNotContain("\"compilerOptions\"", mapperProcess.TransformParams);
        }
        finally
        {
            host.Close();
        }
    }

    [Fact]
    public void TestHostSetLocaleRestartsMapper()
    {
        var mapperProcess = new RecordingMapper();
        var spawner = new FakeSpawner { Handler = mapperProcess };
        var host = NewHost(Context.Background, spawner, Loc.Default);
        try
        {
            var definition = new Mapper { Name = "vue", Version = "1.0.0", Exec = ["vue-mapper"] };
            var release = host.Acquire([definition]);
            try
            {
                host.Transform(definition, new Request { FileName = "/a.vue", Content = "x" });
                Assert.Equal(1, spawner.Spawns);

                Assert.True(Loc.TryParse("fr", out var french));
                host.SetLocale(french);
                Assert.Equal(1, spawner.Closes);

                host.Transform(definition, new Request { FileName = "/a.vue", Content = "x" });
                Assert.Equal(2, spawner.Spawns);
                Assert.Equal("fr", mapperProcess.ReceivedLocale);
            }
            finally
            {
                release();
            }
        }
        finally
        {
            host.Close();
        }
    }

    [Fact]
    public void TestHostSetLocaleWaitsForTransform()
    {
        using var started = new ManualResetEventSlim(false);
        using var proceed = new ManualResetEventSlim(false);
        var mapperProcess = new BlockingMapper(started, proceed);
        var spawner = new FakeSpawner { Handler = mapperProcess };
        var host = NewHost(Context.Background, spawner, Loc.Default);
        try
        {
            var definition = new Mapper { Name = "vue", Version = "1.0.0", Exec = ["vue-mapper"] };

            // xUnit1031：测试里不能用阻塞式 Task 操作（.Result/Wait/GetAwaiter().GetResult()），改用事件 +
            // 由后台任务写入的局部变量（ManualResetEventSlim.Wait 自带内存屏障）。
            using var transformDone = new ManualResetEventSlim(false);
            Exception? transformError = null;
            _ = Task.Run(() =>
            {
                try
                {
                    host.Transform(definition, new Request { FileName = "/a.vue", Content = "x" });
                }
                catch (Exception e)
                {
                    transformError = e;
                }
                finally
                {
                    transformDone.Set();
                }
            });
            Assert.True(started.Wait(TimeSpan.FromSeconds(5)));

            Assert.True(Loc.TryParse("fr", out var french));
            using var setDone = new ManualResetEventSlim(false);
            _ = Task.Run(() =>
            {
                host.SetLocale(french);
                setDone.Set();
            });

            // Go 用 `select { case <-setDone: …; default: … }` 立刻检查；C# 没有"非阻塞查询"，这里等一小段
            // 时间后断言仍未完成（SetLocale 在等 content mapper 的读锁）。
            Assert.False(setDone.Wait(TimeSpan.FromMilliseconds(300)), "SetLocale completed while a transform was in flight");

            proceed.Set();
            Assert.True(transformDone.Wait(TimeSpan.FromSeconds(5)));
            Assert.Null(transformError);
            Assert.True(setDone.Wait(TimeSpan.FromSeconds(5)));
            Assert.Equal(1, spawner.Closes);
        }
        finally
        {
            host.Close();
        }
    }
}
