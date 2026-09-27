// ported from: internal/lsp/lsproto/lsp_json_test.go
//
// 移植映射（与 docs/architecture.md §5 一致）：
//   * Go 的 `t.Run(name, …)` 子测试 → 本文件的 `RunCase(name, …)`：保留 Go 的用例表（含用例名），
//     xunit 的每条 [Fact] 各自并行（Go 的 t.Parallel()）。
//   * Go 的 `json.Unmarshal(data, new(T))` → `UnmarshalNew<T>(…UTF-8 字节…)`；`json.Marshal(&v)` → `Marshal(v)`；
//     `string(data)` → `Encoding.UTF8.GetString(data)`。
//   * Go 的 `assert.ErrorContains` → `Assert.Contains`；`assert.DeepEqual` → `Assert.Equivalent`（结构比较：
//     xunit 的等价比较同样遍历 public 字段/属性，见 xunit.assert 的 GetRuntimeFields/GetRuntimeProperties）
//     **加上**具体字段断言（Go 的 DeepEqual 只看数据，C# 的等价比较是反射实现，两者都保留以免断言变空转）。
//   * Go 的 `assert.ErrorIs(err, ErrorCodeInvalidParams)` → `Assert.Throws<ErrorCodeException>` + `Assert.Equal(ErrorCode.InvalidParams, e.Code)`
//     （Go 的 ErrorCode 自身实现了 error 接口；C# 侧见 Lsp.cs 的 ErrorCodeException）。
//   * Go 的 `reflect.New(reflect.TypeOf(v).Elem())`（TestRoundTrip）→ 泛型 `AssertReMarshalEquals<T>`。
//   * Go 的 `scanDiscriminatedStruct` / `unmarshalDiscriminatedArm[T]`（未导出，靠同 package 测试直接调）→
//     C# 的 `ScanDiscriminatedStruct` / `UnmarshalDiscriminatedArm<T>`（internal，靠 AssemblyInfo 的 InternalsVisibleTo）。
//   * Go 的 `optionalDiscriminatorArm`（测试内自定义 struct，字段表由 Go 反射现场生成）→ 本文件的
//     `OptionalDiscriminatorArm` + 手写静态 `Spec`（C# 侧没有反射，字段表只能由代码给出，见 StructCodec.cs 文件头）。
//
// 已知偏离：`internal/json` 的 C# 移植（TypeScript.Json）不检查重复的 JSON 成员名（docs/json.md；Go 的
// jsontext 默认拒绝）——本文件没有依赖该行为的用例。
// Go 的 BenchmarkUnmarshalDiscriminatedUnion 不移植（C# 侧没有 Go 风格的 benchmark）；它跑的
// discriminator-first / discriminator-last 两种输入形态已由 TestUnmarshalDiscriminatorUnion 覆盖。
using System.Text;
using Xunit;
using Xunit.Abstractions;

using TypeScript.Json;
using TypeScript.LsProto;
using static TypeScript.Json.Json;
using static TypeScript.LsProto.LsProto;
using JsonDecoder = TypeScript.Json.Decoder;
// `TypeScript.LsProto.Range` 与 implicit usings 拉进来的 `System.Range` 撞名（Go 侧没有 System.Range 这种东西），
// 与 SemVer 测试里 `using Version = …` 同一种处理。
using Range = TypeScript.LsProto.Range;

namespace TypeScript.Tests;

public class LspJsonTests(ITestOutputHelper output)
{
    /// <summary>Go 的 <c>[]byte(s)</c>。</summary>
    private static byte[] Utf8(string text) => Encoding.UTF8.GetBytes(text);

    /// <summary>Go 的 <c>string(data)</c>。</summary>
    private static string Text(byte[] data) => Encoding.UTF8.GetString(data);

    /// <summary>Go 的 <c>json.Unmarshal([]byte(input), new(T))</c>，必须成功并返回解出的值。</summary>
    private static T UnmarshalValue<T>(string input)
        where T : class, IJsonUnmarshaler, new()
        => UnmarshalNew<T>(Utf8(input));

    /// <summary>Go 的 <c>json.Unmarshal([]byte(input), new(T))</c>，必须报错。</summary>
    private static JsonException ThrowsUnmarshal<T>(string input)
        where T : class, IJsonUnmarshaler, new()
        => Assert.Throws<JsonException>(() =>
        {
            UnmarshalNew<T>(Utf8(input));
        });

    /// <summary>Go 的 <c>t.Run(name, …)</c>：跑一条用例，失败时把用例名加进消息。</summary>
    private void RunCase(string name, Action run)
    {
        output.WriteLine(name);
        try
        {
            run();
        }
        catch (Exception error)
        {
            throw new InvalidOperationException($"[{name}] {error.Message}", error);
        }
    }

    /// <summary>Go 的 <c>json.Marshal(&amp;v)</c> → 字符串。</summary>
    private static string MarshalText(object? value) => Text(Marshal(value));

    /// <summary>Go 的 marshal → unmarshal 后 <c>assert.DeepEqual(t, *v, result)</c>。</summary>
    private static T AssertRoundTrips<T>(T value)
        where T : class, IJsonMarshaler, IJsonUnmarshaler, new()
    {
        var data = Marshal(value);
        var result = UnmarshalNew<T>(data);
        Assert.Equivalent(value, result);
        return result;
    }

    /// <summary>Go 的 TestRoundTrip：marshal → unmarshal → 再 marshal，字节必须一致。</summary>
    private static void AssertReMarshalEquals<T>(T value)
        where T : class, IJsonMarshaler, IJsonUnmarshaler, new()
    {
        var data = Marshal(value);
        var got = UnmarshalNew<T>(data);
        var again = Marshal(got);
        Assert.Equal(Text(data), Text(again));
    }

    [Fact]
    public void TestUnmarshalRejectsNullForOptionalNonNullableFields()
    {
        // Go 里 target 是 `any`（持有 *T）；C# 无反射，改成每条用例自带"往哪个类型里解"。
        var tests = new (string Name, string Input, Func<string, JsonException> Run, string Field)[]
        {
            ("InlayHint kind null",
                """{"position": {"line": 0, "character": 0}, "label": "foo", "kind": null}""",
                static input => ThrowsUnmarshal<InlayHint>(input), "kind"),
            ("InlayHint textEdits null",
                """{"position": {"line": 0, "character": 0}, "label": "foo", "textEdits": null}""",
                static input => ThrowsUnmarshal<InlayHint>(input), "textEdits"),
            ("InlayHint paddingLeft null",
                """{"position": {"line": 0, "character": 0}, "label": "foo", "paddingLeft": null}""",
                static input => ThrowsUnmarshal<InlayHint>(input), "paddingLeft"),
            ("FoldingRange kind null",
                """{"startLine": 0, "endLine": 10, "kind": null}""",
                static input => ThrowsUnmarshal<FoldingRange>(input), "kind"),
            ("FoldingRange startCharacter null",
                """{"startLine": 0, "endLine": 10, "startCharacter": null}""",
                static input => ThrowsUnmarshal<FoldingRange>(input), "startCharacter"),
            ("CompletionItem insertTextFormat null",
                """{"label": "test", "insertTextFormat": null}""",
                static input => ThrowsUnmarshal<CompletionItem>(input), "insertTextFormat"),
            ("Hover range null",
                """{"contents": {"kind": "plaintext", "value": "hi"}, "range": null}""",
                static input => ThrowsUnmarshal<Hover>(input), "range"),
            ("WorkDoneProgressOptions workDoneProgress null",
                """{"workDoneProgress": null}""",
                static input => ThrowsUnmarshal<WorkDoneProgressOptions>(input), "workDoneProgress"),
            ("CallHierarchyIncomingCallsParams item null",
                """{"item": null}""",
                static input => ThrowsUnmarshal<CallHierarchyIncomingCallsParams>(input), "item"),
            ("CallHierarchyIncomingCall from null",
                """{"from": null, "fromRanges": []}""",
                static input => ThrowsUnmarshal<CallHierarchyIncomingCall>(input), "from"),
            ("InitializeParams capabilities null",
                """{"processId": null, "rootUri": null, "capabilities": null}""",
                static input => ThrowsUnmarshal<InitializeParams>(input), "capabilities"),
            ("InitializeResult capabilities null",
                """{"capabilities": null}""",
                static input => ThrowsUnmarshal<InitializeResult>(input), "capabilities"),
            ("SemanticTokens data null (required slice)",
                """{"data": null}""",
                static input => ThrowsUnmarshal<SemanticTokens>(input), "data"),
            ("TextDocumentEdit edits null (required slice)",
                """{"textDocument": {"uri": "file:///a.ts", "version": 1}, "edits": null}""",
                static input => ThrowsUnmarshal<TextDocumentEdit>(input), "edits"),
        };

        foreach (var (name, input, run, field) in tests)
        {
            RunCase(name, () =>
            {
                var error = run(input);
                Assert.Contains($"null value is not allowed for field \"{field}\"", error.Message);
            });
        }
    }

    [Fact]
    public void TestUnmarshalAcceptsNullForNullableFields()
    {
        var tests = new (string Name, string Input, Action<string> Run)[]
        {
            ("InitializeParams rootUri null",
                """{"processId": null, "rootUri": null, "capabilities": {}}""",
                static input => UnmarshalValue<InitializeParams>(input)),
            ("InitializeParams workspaceFolders null",
                """{"processId": null, "rootUri": null, "capabilities": {}, "workspaceFolders": null}""",
                static input => UnmarshalValue<InitializeParams>(input)),
            ("InitializeParams processId null",
                """{"processId": null, "rootUri": null, "capabilities": {}}""",
                static input => UnmarshalValue<InitializeParams>(input)),
            ("InitializationOptions userPreferences null",
                """{"userPreferences": null}""",
                static input => UnmarshalValue<InitializationOptions>(input)),
            ("InitializeParams initializationOptions null",
                """{"processId": null, "rootUri": null, "capabilities": {}, "initializationOptions": null}""",
                static input => UnmarshalValue<InitializeParams>(input)),
        };

        foreach (var (name, input, run) in tests)
        {
            RunCase(name, () => run(input));
        }
    }

    [Fact]
    public void TestUnmarshalAcceptsOmittedOptionalFields()
    {
        RunCase("InlayHint with only required fields", () =>
        {
            var hint = UnmarshalValue<InlayHint>("""{"position": {"line": 1, "character": 5}, "label": "test"}""");
            Assert.Null(hint.Kind);
            Assert.Null(hint.TextEdits);
            Assert.Null(hint.Tooltip);
            Assert.Null(hint.PaddingLeft);
            Assert.Null(hint.PaddingRight);
            Assert.Null(hint.Data);
            Assert.Equal(1u, hint.Position.Line);
            Assert.Equal(5u, hint.Position.Character);
        });

        RunCase("FoldingRange with only required fields", () =>
        {
            var range = UnmarshalValue<FoldingRange>("""{"startLine": 5, "endLine": 10}""");
            Assert.Null(range.Kind);
            Assert.Null(range.StartCharacter);
            Assert.Null(range.EndCharacter);
            Assert.Null(range.CollapsedText);
            Assert.Equal(5u, range.StartLine);
            Assert.Equal(10u, range.EndLine);
        });
    }

    [Fact]
    public void TestUnmarshalRejectsIncompleteObjects()
    {
        var tests = new (string Name, string Input, Func<string, JsonException> Run, string Expected)[]
        {
            ("InlayHint missing position", """{"label": "test"}""",
                static input => ThrowsUnmarshal<InlayHint>(input), "missing required properties: position"),
            ("InlayHint missing label", """{"position": {"line": 0, "character": 0}}""",
                static input => ThrowsUnmarshal<InlayHint>(input), "missing required properties: label"),
            ("Location missing uri",
                """{"range": {"start": {"line": 0, "character": 0}, "end": {"line": 0, "character": 0}}}""",
                static input => ThrowsUnmarshal<Location>(input), "missing required properties: uri"),
            ("Location empty object", "{}",
                static input => ThrowsUnmarshal<Location>(input), "missing required properties: uri, range"),
        };

        foreach (var (name, input, run, expected) in tests)
        {
            RunCase(name, () =>
            {
                var error = run(input);
                Assert.Contains(expected, error.Message);
            });
        }
    }

    [Fact]
    public void TestMarshalUnmarshalRoundTrip()
    {
        RunCase("InlayHint with kind", () =>
        {
            var result = AssertRoundTrips(new InlayHint
            {
                Position = new Position { Line = 1, Character = 5 },
                Label = new StringOrInlayHintLabelParts { String = "param" },
                Kind = InlayHintKind.Parameter,
            });
            Assert.Equal(1u, result.Position.Line);
            Assert.Equal(5u, result.Position.Character);
            Assert.Equal("param", result.Label.String);
            Assert.Equal((InlayHintKind?)InlayHintKind.Parameter, result.Kind);
        });

        RunCase("InlayHint minimal", () =>
        {
            var result = AssertRoundTrips(new InlayHint
            {
                Position = new Position { Line = 0, Character = 0 },
                Label = new StringOrInlayHintLabelParts { String = "x" },
            });
            Assert.Equal("x", result.Label.String);
            Assert.Null(result.Kind);
        });

        RunCase("FoldingRange with all fields", () =>
        {
            var result = AssertRoundTrips(new FoldingRange
            {
                StartLine = 1,
                StartCharacter = 0,
                EndLine = 10,
                EndCharacter = 5,
                Kind = FoldingRangeKind.Region,
                CollapsedText = "...",
            });
            Assert.Equal(1u, result.StartLine);
            Assert.Equal((uint?)0, result.StartCharacter);
            Assert.Equal(10u, result.EndLine);
            Assert.Equal((uint?)5, result.EndCharacter);
            Assert.Equal((FoldingRangeKind?)FoldingRangeKind.Region, result.Kind);
            Assert.Equal("...", result.CollapsedText);
        });

        RunCase("Location", () =>
        {
            var result = AssertRoundTrips(new Location
            {
                Uri = new DocumentUri("file:///test.ts"),
                Range = new Range
                {
                    Start = new Position { Line = 1, Character = 2 },
                    End = new Position { Line = 3, Character = 4 },
                },
            });
            Assert.Equal(new DocumentUri("file:///test.ts"), result.Uri);
            Assert.Equal(1u, result.Range.Start.Line);
            Assert.Equal(2u, result.Range.Start.Character);
            Assert.Equal(3u, result.Range.End.Line);
            Assert.Equal(4u, result.Range.End.Character);
        });

        RunCase("InitializeParams with null processId", () =>
        {
            var result = AssertRoundTrips(new InitializeParams
            {
                ProcessId = new IntegerOrNull(),
                RootUri = new DocumentUriOrNull { DocumentUri = new DocumentUri("file:///workspace") },
                Capabilities = new ClientCapabilities(),
            });
            Assert.Null(result.ProcessId.Integer);
            Assert.Equal(new DocumentUri("file:///workspace"), result.RootUri.DocumentUri);
            Assert.NotNull(result.Capabilities);
        });
    }

    [Fact]
    public void TestUnmarshalUnionTypes()
    {
        RunCase("IntegerOrString with integer", () =>
        {
            var v = UnmarshalValue<IntegerOrString>("42");
            Assert.Equal((int?)42, v.Integer);
            Assert.Null(v.String);
        });

        RunCase("IntegerOrString with string", () =>
        {
            var v = UnmarshalValue<IntegerOrString>(@"""hello""");
            Assert.Equal("hello", v.String);
            Assert.Null(v.Integer);
        });

        RunCase("IntegerOrNull with integer", () =>
        {
            var v = UnmarshalValue<IntegerOrNull>("42");
            Assert.Equal((int?)42, v.Integer);
        });

        RunCase("IntegerOrNull with null", () =>
        {
            var v = UnmarshalValue<IntegerOrNull>("null");
            Assert.Null(v.Integer);
        });

        RunCase("DocumentUriOrNull with string", () =>
        {
            var v = UnmarshalValue<DocumentUriOrNull>(@"""file:///test.ts""");
            Assert.Equal(new DocumentUri("file:///test.ts"), v.DocumentUri);
        });

        RunCase("DocumentUriOrNull with null", () =>
        {
            var v = UnmarshalValue<DocumentUriOrNull>("null");
            Assert.Null(v.DocumentUri);
        });
    }

    [Fact]
    public void TestMarshalUnionTypes()
    {
        RunCase("IntegerOrNull with value", () =>
            Assert.Equal("42", MarshalText(new IntegerOrNull { Integer = 42 })));

        RunCase("IntegerOrNull with null", () =>
            Assert.Equal("null", MarshalText(new IntegerOrNull())));

        RunCase("IntegerOrString with integer", () =>
            Assert.Equal("7", MarshalText(new IntegerOrString { Integer = 7 })));

        RunCase("IntegerOrString with string", () =>
            Assert.Equal(@"""tok""", MarshalText(new IntegerOrString { String = "tok" })));
    }

    [Fact]
    public void TestUnmarshalIgnoresUnknownFields()
    {
        RunCase("Location with extra fields", () =>
        {
            var location = UnmarshalValue<Location>("""
                {
                    "uri": "file:///test.ts",
                    "range": {"start": {"line": 0, "character": 0}, "end": {"line": 0, "character": 5}},
                    "someUnknownField": 42,
                    "anotherUnknown": {"nested": true}
                }
                """);
            Assert.Equal(new DocumentUri("file:///test.ts"), location.Uri);
        });

        RunCase("InlayHint with extra fields", () =>
        {
            UnmarshalValue<InlayHint>("""
                {
                    "position": {"line": 0, "character": 0},
                    "label": "x",
                    "futureField": [1, 2, 3]
                }
                """);
        });
    }

    [Fact]
    public void TestUnmarshalRejectsWrongTypes()
    {
        var tests = new (string Name, string Input, Func<string, JsonException> Run)[]
        {
            ("Location receives array", "[]", static input => ThrowsUnmarshal<Location>(input)),
            ("Location receives string", @"""not an object""", static input => ThrowsUnmarshal<Location>(input)),
            ("Location receives number", "42", static input => ThrowsUnmarshal<Location>(input)),
            ("Location receives null", "null", static input => ThrowsUnmarshal<Location>(input)),
            ("FoldingRange receives boolean", "true", static input => ThrowsUnmarshal<FoldingRange>(input)),
        };

        foreach (var (name, input, run) in tests)
        {
            RunCase(name, () =>
            {
                Assert.NotNull(run(input));
            });
        }
    }

    [Fact]
    public void TestUnmarshalUnionTypeWrongKind()
    {
        RunCase("IntegerOrString rejects boolean", () => ThrowsUnmarshal<IntegerOrString>("true"));
        RunCase("IntegerOrString rejects null", () => ThrowsUnmarshal<IntegerOrString>("null"));
        RunCase("IntegerOrString rejects object", () => ThrowsUnmarshal<IntegerOrString>("{}"));
        RunCase("IntegerOrString rejects array", () => ThrowsUnmarshal<IntegerOrString>("[]"));
        RunCase("StringOrInlayHintLabelParts rejects number", () => ThrowsUnmarshal<StringOrInlayHintLabelParts>("42"));
        RunCase("StringOrInlayHintLabelParts rejects boolean", () => ThrowsUnmarshal<StringOrInlayHintLabelParts>("true"));
    }

    [Fact]
    public void TestUnmarshalBooleanUnionTypes()
    {
        RunCase("BooleanOrHoverOptions with true", () =>
        {
            var v = UnmarshalValue<BooleanOrHoverOptions>("true");
            Assert.Equal((bool?)true, v.Boolean);
            Assert.Null(v.HoverOptions);
        });

        RunCase("BooleanOrHoverOptions with false", () =>
        {
            var v = UnmarshalValue<BooleanOrHoverOptions>("false");
            Assert.Equal((bool?)false, v.Boolean);
            Assert.Null(v.HoverOptions);
        });

        RunCase("BooleanOrHoverOptions with object", () =>
        {
            var v = UnmarshalValue<BooleanOrHoverOptions>("{}");
            Assert.Null(v.Boolean);
            Assert.NotNull(v.HoverOptions);
        });

        RunCase("BooleanOrHoverOptions rejects string", () =>
            ThrowsUnmarshal<BooleanOrHoverOptions>(@"""nope"""));
    }

    /// <summary>Go 的 <c>optionalDiscriminatorArm</c>（测试内自定义 struct；Go 侧字段表由反射现场生成，
    /// 这里按 StructCodec.cs 的显式字段表写。Go 的测试类型只有解码路径，所以没有 MarshalJSONTo，
    /// encoders 传空数组）。</summary>
    private sealed class OptionalDiscriminatorArm : IJsonUnmarshaler
    {
        public StringLiteralBegin? Kind;

        public string Title = "";

        internal static readonly StructSpec Spec = new(
        [
            new StructFieldSpec("kind", false, true,
                static (target, dec) =>
                {
                    var t = (OptionalDiscriminatorArm)target;
                    t.Kind = DecodeNullable<StringLiteralBegin>(dec, static d => d.DecodeNew<StringLiteralBegin>());
                }),
            new StructFieldSpec("title", true, false,
                static (target, dec) =>
                {
                    var t = (OptionalDiscriminatorArm)target;
                    t.Title = dec.Decode<string>() ?? "";
                }),
        ],
        []);

        public void UnmarshalJSONFrom(JsonDecoder dec) => UnmarshalStruct(this, Spec, dec);
    }

    [Fact]
    public void TestUnmarshalDiscriminatorUnion()
    {
        RunCase("WorkDoneProgressBegin", () =>
        {
            var v = UnmarshalValue<WorkDoneProgressBeginOrReportOrEnd>("""{"kind": "begin", "title": "Indexing"}""");
            Assert.NotNull(v.Begin);
            Assert.Null(v.Report);
            Assert.Null(v.End);
            Assert.Equal("Indexing", v.Begin!.Title);
        });

        RunCase("WorkDoneProgressReport", () =>
        {
            var v = UnmarshalValue<WorkDoneProgressBeginOrReportOrEnd>("""{"kind": "report", "message": "50%"}""");
            Assert.Null(v.Begin);
            Assert.NotNull(v.Report);
            Assert.Null(v.End);
            Assert.Equal("50%", v.Report!.Message);
        });

        RunCase("WorkDoneProgressEnd", () =>
        {
            var v = UnmarshalValue<WorkDoneProgressBeginOrReportOrEnd>("""{"kind": "end"}""");
            Assert.Null(v.Begin);
            Assert.Null(v.Report);
            Assert.NotNull(v.End);
        });

        RunCase("discriminator after variant fields", () =>
        {
            var v = UnmarshalValue<WorkDoneProgressBeginOrReportOrEnd>(
                """{"title": "Indexing", "percentage": 25, "kind": "begin"}""");
            Assert.NotNull(v.Begin);
            Assert.Equal("Indexing", v.Begin!.Title);
            Assert.Equal((uint?)25, v.Begin.Percentage);
        });

        RunCase("optional discriminator is preserved", () =>
        {
            var dec = new JsonDecoder(Utf8("""{"kind": "begin", "title": "Indexing"}"""));
            var state = ScanDiscriminatedStruct(dec, "optionalDiscriminatorArm", "kind");
            var v = UnmarshalDiscriminatedArm<OptionalDiscriminatorArm>(state, OptionalDiscriminatorArm.Spec);
            Assert.NotNull(v);
            Assert.NotNull(v.Kind);
            Assert.Equal("Indexing", v.Title);
        });

        RunCase("invalid discriminator", () =>
            ThrowsUnmarshal<WorkDoneProgressBeginOrReportOrEnd>("""{"kind": "invalid"}"""));

        RunCase("non-string discriminator", () =>
            ThrowsUnmarshal<WorkDoneProgressBeginOrReportOrEnd>("""{"kind": null}"""));

        RunCase("missing discriminator", () =>
        {
            var error = ThrowsUnmarshal<WorkDoneProgressBeginOrReportOrEnd>("""{"message": "missing kind"}""");
            Assert.Contains("missing discriminator \"kind\"", error.Message);
        });
    }

    [Fact]
    public void TestUnmarshalPresenceDiscriminatorUnion()
    {
        RunCase("TextEdit via range field", () =>
        {
            var v = UnmarshalValue<TextEditOrInsertReplaceEdit>("""
                {
                    "range": {"start": {"line": 0, "character": 0}, "end": {"line": 0, "character": 1}},
                    "newText": "x"
                }
                """);
            Assert.NotNull(v.TextEdit);
            Assert.Null(v.InsertReplaceEdit);
            Assert.Equal("x", v.TextEdit!.NewText);
        });

        RunCase("InsertReplaceEdit via insert field", () =>
        {
            var v = UnmarshalValue<TextEditOrInsertReplaceEdit>("""
                {
                    "insert": {"start": {"line": 0, "character": 0}, "end": {"line": 0, "character": 1}},
                    "replace": {"start": {"line": 0, "character": 0}, "end": {"line": 0, "character": 2}},
                    "newText": "y"
                }
                """);
            Assert.Null(v.TextEdit);
            Assert.NotNull(v.InsertReplaceEdit);
            Assert.Equal("y", v.InsertReplaceEdit!.NewText);
        });
    }

    [Fact]
    public void TestUnmarshalStringOrArrayUnion()
    {
        RunCase("StringOrInlayHintLabelParts with string", () =>
        {
            var v = UnmarshalValue<StringOrInlayHintLabelParts>(@"""hello""");
            Assert.Equal("hello", v.String);
            Assert.Null(v.InlayHintLabelParts);
        });

        RunCase("StringOrInlayHintLabelParts with array", () =>
        {
            var v = UnmarshalValue<StringOrInlayHintLabelParts>(
                """[{"value": "param"}, {"value": ": "}, {"value": "string"}]""");
            Assert.Null(v.String);
            Assert.NotNull(v.InlayHintLabelParts);
            var parts = v.InlayHintLabelParts!;
            Assert.Equal(3, parts.Length);
            Assert.Equal("param", parts[0]!.Value);
        });
    }

    [Fact]
    public void TestUnmarshalDocumentEditUnion()
    {
        RunCase("TextDocumentEdit without kind", () =>
        {
            var v = UnmarshalValue<TextDocumentEditOrCreateFileOrRenameFileOrDeleteFile>("""
                {
                    "textDocument": {"uri": "file:///a.ts", "version": 1},
                    "edits": [{"range": {"start": {"line": 0, "character": 0}, "end": {"line": 0, "character": 0}}, "newText": "x"}]
                }
                """);
            Assert.NotNull(v.TextDocumentEdit);
            Assert.Null(v.CreateFile);
            Assert.Null(v.RenameFile);
            Assert.Null(v.DeleteFile);
        });

        RunCase("TextDocumentEdit with non-string kind", () =>
        {
            var v = UnmarshalValue<TextDocumentEditOrCreateFileOrRenameFileOrDeleteFile>("""
                {
                    "kind": null,
                    "textDocument": {"uri": "file:///a.ts", "version": 1},
                    "edits": []
                }
                """);
            Assert.NotNull(v.TextDocumentEdit);
            Assert.Null(v.CreateFile);
            Assert.Null(v.RenameFile);
            Assert.Null(v.DeleteFile);
        });

        RunCase("CreateFile with kind create", () =>
        {
            var v = UnmarshalValue<TextDocumentEditOrCreateFileOrRenameFileOrDeleteFile>(
                """{"kind": "create", "uri": "file:///new.ts"}""");
            Assert.Null(v.TextDocumentEdit);
            Assert.NotNull(v.CreateFile);
            Assert.Equal(new DocumentUri("file:///new.ts"), v.CreateFile!.Uri);
        });

        RunCase("CreateFile with kind after fields", () =>
        {
            var v = UnmarshalValue<TextDocumentEditOrCreateFileOrRenameFileOrDeleteFile>(
                """{"uri": "file:///new.ts", "kind": "create"}""");
            Assert.NotNull(v.CreateFile);
            Assert.Equal(new DocumentUri("file:///new.ts"), v.CreateFile!.Uri);
        });

        RunCase("RenameFile with kind rename", () =>
        {
            var v = UnmarshalValue<TextDocumentEditOrCreateFileOrRenameFileOrDeleteFile>(
                """{"kind": "rename", "oldUri": "file:///old.ts", "newUri": "file:///new.ts"}""");
            Assert.NotNull(v.RenameFile);
            Assert.Equal(new DocumentUri("file:///old.ts"), v.RenameFile!.OldUri);
        });

        RunCase("DeleteFile with kind delete", () =>
        {
            var v = UnmarshalValue<TextDocumentEditOrCreateFileOrRenameFileOrDeleteFile>(
                """{"kind": "delete", "uri": "file:///gone.ts"}""");
            Assert.NotNull(v.DeleteFile);
            Assert.Equal(new DocumentUri("file:///gone.ts"), v.DeleteFile!.Uri);
        });
    }

    [Fact]
    public void TestUnmarshalFieldOrdering()
    {
        RunCase("Location with reversed field order", () =>
        {
            var location = UnmarshalValue<Location>("""
                {
                    "range": {"start": {"line": 1, "character": 2}, "end": {"line": 3, "character": 4}},
                    "uri": "file:///test.ts"
                }
                """);
            Assert.Equal(new DocumentUri("file:///test.ts"), location.Uri);
            Assert.Equal(1u, location.Range.Start.Line);
        });

        RunCase("InlayHint with kind before label", () =>
        {
            var hint = UnmarshalValue<InlayHint>("""
                {
                    "kind": 1,
                    "label": "x",
                    "position": {"line": 0, "character": 0}
                }
                """);
            Assert.Equal((InlayHintKind?)InlayHintKind.Type, hint.Kind);
        });
    }

    [Fact]
    public void TestUnmarshalEmptyObject()
    {
        RunCase("WorkDoneProgressOptions empty", () =>
        {
            var v = UnmarshalValue<WorkDoneProgressOptions>("{}");
            Assert.Null(v.WorkDoneProgress);
        });

        RunCase("InitializationOptions empty", () => UnmarshalValue<InitializationOptions>("{}"));
        RunCase("ClientCapabilities empty", () => UnmarshalValue<ClientCapabilities>("{}"));
        RunCase("ServerCapabilities empty", () => UnmarshalValue<ServerCapabilities>("{}"));
    }

    [Fact]
    public void TestMarshalOmitsZeroOptionalFields()
    {
        RunCase("InlayHint omits nil fields", () =>
        {
            var hint = new InlayHint
            {
                Position = new Position { Line = 0, Character = 0 },
                Label = new StringOrInlayHintLabelParts { String = "x" },
            };
            var s = MarshalText(hint);
            Assert.DoesNotContain("kind", s);
            Assert.DoesNotContain("textEdits", s);
            Assert.DoesNotContain("paddingLeft", s);
            Assert.Contains("position", s);
            Assert.Contains("label", s);
        });

        RunCase("FoldingRange omits nil optional fields", () =>
        {
            var range = new FoldingRange { StartLine = 1, EndLine = 10 };
            var s = MarshalText(range);
            Assert.DoesNotContain("kind", s);
            Assert.DoesNotContain("startCharacter", s);
            Assert.Contains("startLine", s);
            Assert.Contains("endLine", s);
        });
    }

    [Fact]
    public void TestLiteralTypes()
    {
        RunCase("StringLiteralCreate marshal", () =>
            Assert.Equal(@"""create""", MarshalText(new StringLiteralCreate())));

        RunCase("StringLiteralCreate unmarshal", () => UnmarshalValue<StringLiteralCreate>(@"""create"""));

        RunCase("StringLiteralCreate rejects wrong value", () =>
            ThrowsUnmarshal<StringLiteralCreate>(@"""delete"""));

        RunCase("StringLiteralCreate rejects wrong type", () =>
            ThrowsUnmarshal<StringLiteralCreate>("42"));
    }

    [Fact]
    public void TestEnumStringValues()
    {
        RunCase("InlayHintKind values", () =>
        {
            Assert.Equal("Type", InlayHintKind.Type.String());
            Assert.Equal("Parameter", InlayHintKind.Parameter.String());
        });

        RunCase("SymbolKind values", () =>
        {
            Assert.Equal("File", SymbolKind.File.String());
            Assert.Equal("Function", SymbolKind.Function.String());
            Assert.Equal("Variable", SymbolKind.Variable.String());
        });

        RunCase("unknown enum value", () =>
        {
            var s = ((InlayHintKind)999).String();
            Assert.Contains("999", s);
        });
    }

    /// <summary>TestRoundTrip locks the generated codecs: every value must survive
    /// marshal -&gt; unmarshal -&gt; marshal unchanged. This guards fidelity so codec changes
    /// (e.g. pruning or table-driving them) cannot silently corrupt the wire format.
    /// Cover a representative spread of shapes: required fields, nullable/non-nullable
    /// optionals, enums, slices, nested objects, and unions。</summary>
    [Fact]
    public void TestRoundTrip()
    {
        RunCase("Range", () => AssertReMarshalEquals(new Range
        {
            Start = new Position { Line = 1, Character = 2 },
            End = new Position { Line = 3, Character = 4 },
        }));

        RunCase("TextEdit", () => AssertReMarshalEquals(new TextEdit
        {
            Range = new Range
            {
                Start = new Position { Line = 1, Character = 2 },
                End = new Position { Line = 3, Character = 4 },
            },
            NewText = "hello",
        }));

        RunCase("MarkupContent", () => AssertReMarshalEquals(new MarkupContent
        {
            Kind = MarkupKind.Markdown,
            Value = "**x**",
        }));

        RunCase("DidChangeConfigurationParams object", () => AssertReMarshalEquals(new DidChangeConfigurationParams
        {
            Settings = new Dictionary<string, object?>
            {
                ["js/ts"] = new Dictionary<string, object?> { ["x"] = 1.0 },
            },
        }));

        RunCase("DidChangeConfigurationParams null", () =>
            AssertReMarshalEquals(new DidChangeConfigurationParams { Settings = null }));

        RunCase("CompletionItem", () => AssertReMarshalEquals(new CompletionItem
        {
            Label = "pageXOffset",
            Kind = CompletionItemKind.Field,
            SortText = "15",
            InsertTextFormat = InsertTextFormat.PlainText,
        }));

        // StringOrTuple union (string arm and tuple arm).
        RunCase("ParameterInformation string label", () => AssertReMarshalEquals(new ParameterInformation
        {
            Label = new StringOrTuple { String = "p: number" },
        }));

        RunCase("ParameterInformation tuple label", () => AssertReMarshalEquals(new ParameterInformation
        {
            Label = new StringOrTuple { Tuple = [0u, 4u] },
        }));
    }

    /// <summary>TestStrictnessMissingRequired confirms required fields are still enforced;
    /// default reflective decoding would silently accept these.</summary>
    [Fact]
    public void TestStrictnessMissingRequired()
    {
        var tests = new (string Name, string Input, Func<string, JsonException> Run)[]
        {
            ("TextEdit missing newText",
                """{"range":{"start":{"line":0,"character":0},"end":{"line":0,"character":1}}}""",
                static input => ThrowsUnmarshal<TextEdit>(input)),
            ("Range missing end", """{"start":{"line":0,"character":0}}""",
                static input => ThrowsUnmarshal<Range>(input)),
            ("Position missing character", """{"line":0}""",
                static input => ThrowsUnmarshal<Position>(input)),
        };

        foreach (var (name, input, run) in tests)
        {
            RunCase(name, () =>
            {
                var error = run(input);
                Assert.Contains("missing required properties", error.Message);
            });
        }
    }

    /// <summary>TestStrictnessNotObject confirms a non-object where an object is required
    /// is rejected rather than coerced.</summary>
    [Fact]
    public void TestStrictnessNotObject()
    {
        var error = ThrowsUnmarshal<TextEdit>(@"""oops""");
        Assert.True(
            error.Message.Contains("object", StringComparison.Ordinal)
                || error.Message.Contains("cannot unmarshal", StringComparison.Ordinal),
            error.Message);
    }

    /// <summary>TestUnmarshalParamsRequiresParams verifies that a NoParams method must be
    /// given no params while every other method must be given params, and that a
    /// mismatch (including a null value either way) is an InvalidParams error.</summary>
    [Fact]
    public void TestUnmarshalParamsRequiresParams()
    {
        // NoParams: only truly-absent/empty params are accepted; null and any
        // present value are rejected.
        // Go 的 `json.Value(``)`（非 nil 的空切片）→ C# 的非 null 空 byte[]。
        var noParamsTests = new (string Name, object? RawParams, bool WantError)[]
        {
            ("absent", null, false),
            ("empty", Array.Empty<byte>(), false),
            ("null", Utf8("null"), true),
            ("object", Utf8("{}"), true),
        };

        foreach (var (name, rawParams, wantError) in noParamsTests)
        {
            RunCase($"NoParams/{name}", () =>
            {
                var message = new RequestMessage { Params = rawParams };
                if (wantError)
                {
                    var error = Assert.Throws<ErrorCodeException>(() =>
                    {
                        message.UnmarshalParams<NoParams>();
                    });
                    Assert.Equal(ErrorCode.InvalidParams, error.Code);
                }
                else
                {
                    message.UnmarshalParams<NoParams>();
                }
            });
        }

        // Required-params method: only an object or array is accepted; absent,
        // empty, null, and other scalars are rejected.
        var typedTests = new (string Name, object? RawParams, bool WantError)[]
        {
            ("absent", null, true),
            ("empty", Array.Empty<byte>(), true),
            ("null", Utf8("null"), true),
            ("number", Utf8("5"), true),
            ("string", Utf8(@"""x"""), true),
            ("object", Utf8("""{"settings":{"x":1}}"""), false),
        };

        foreach (var (name, rawParams, wantError) in typedTests)
        {
            RunCase($"typed/{name}", () =>
            {
                var message = new RequestMessage { Params = rawParams };
                if (wantError)
                {
                    var error = Assert.Throws<ErrorCodeException>(() =>
                    {
                        message.UnmarshalParams<DidChangeConfigurationParams>();
                    });
                    Assert.Equal(ErrorCode.InvalidParams, error.Code);
                }
                else
                {
                    var got = message.UnmarshalParams<DidChangeConfigurationParams>();
                    Assert.True(got is not null && got.Settings is not null);
                }
            });
        }
    }
}
