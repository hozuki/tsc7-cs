// ported from: internal/testutil/contentmappertest/transforming.go
//
// C# 映射与偏离（逐条）：
// 1) `Handler` 是导出类型（保持 Go 名字）；Go 内嵌的 `noNotifications` → C# 继承 `NoNotifications`，并声明
//    `, Ipc.Handler, IProjectLifecycleHandler`。Go 的 `var _ ipc.Handler = (*Handler)(nil)` 断言在 C# 里由
//    类声明本身表达（编译期即检查），不需要运行时断言。
// 2) `preamble`/`diagnosticSource`/`unclosedInterpolationCode`（未导出包级常量）→ 同名静态类
//    `ContentMapperTest` 上的 `internal const`（Go 未导出 → C# internal）；未导出的包级函数
//    `mappedExtension`/`transform`/`diagnosticDirectives`/`renderOption` 同理 → `internal static`
//    （Go 的包级可见性 = 同包任意类型都能调，C# 的 private 只有同一类型可见，而这里 `Handler` 是另一个类型）。
//    文件头的 `using static ...ContentMapperTest;` 让 `Handler` 里的调用点与 Go 一样是裸名。
// 3) `DeclaredOptions`（导出变量）→ `public static readonly string[]`（C# 的 const 不能是数组）。
// 4) `sync.Mutex` + `map[string]*collections.OrderedMap[string, json.Value]` → `lock` + 可空 Dictionary
//    （C# 用引用类型可空表达 Go 的 nil map：nil map 的读与 delete 都是 no-op）。
// 5) Go 的 `json.Unmarshal(p.CompilerOptions, &options)`（目标是 `*OrderedMap[string, json.Value]`）**不能**
//    走 `UnmarshalNew<OrderedMap<string, byte[]>>`：本项目 Json 只在值位置把 `byte[]` 当 base64 解，而 Go 的
//    `json.Value` 是原始 JSON 字节 → 本文件里手写 `UnmarshalCompilerOptions`（`Decoder.ReadValue()` 逐个取
//    原始值、保持键顺序）。
//    另：C# 的 `OpenProjectParams.CompilerOptions` 是 `byte[]?`，"字段缺失"与"JSON null"都表现为 null
//    （Go 能区分：缺失会让 json.Unmarshal 报错、JSON null 会得到 nil map）。宿主总是写出该成员
//    （contentmapper/HostImpl.cs 写对象或 `null`），所以这里把 null 当 Go 的 JSON null（options = nil）。
// 6) Go 的闭包 `writeVerbatim`/`writeAtom`（修改外层的 strings.Builder/slice）→ C# 的局部函数。
//    `strings.Builder` → `StringBuilder`；Go 的变量名 `virtual` 是 C# 关键字 → 写作 `@virtual`。
// 7) `strings.Index`/`strings.IndexByte` → `string.IndexOf`（Go 的切片相对下标 → C# 的绝对下标；"没找到"
//    的分支照抄）。`strings.Cut`/`SplitN`/`TrimPrefix`/`TrimSpace` → `IndexOf('\n')` + 范围运算符 + `Trim()`。
// 8) Go 的多返回值 `transform(...) (string, json.Value, []Diagnostic, *DiagnosticDirectives, error)` →
//    C# 元组，失败抛异常（`SpanMap.Marshal` 抛）；Go 的 nil 切片在 C# 里显式写成 `null`
//    （没有诊断时 Diagnostics 是 nil → JSON 里省略，与 Go 的 omitempty 一致）。
// 9) `DiagnosticDirectives`（Go 的 `diagnosticDirectives` 函数）与 `contentmapper.DiagnosticDirectives`
//    （类型）在 C# 里同名：返回类型/`new` 是类型上下文，调用点是表达式上下文，编译器能区分，无需改名。
// 10) 本文件没有 panic/debug.Fail。
using System.Text;
using TypeScript.Collections;
using TypeScript.ContentMapper;
using TypeScript.Core;
using TypeScript.Json;
using TypeScript.SpanMap;
using static TypeScript.ContentMapper.ContentMapper;
using static TypeScript.Core.Core;
using static TypeScript.Json.Json;

// `System.Text.Decoder`（隐式 using）与 `TypeScript.Json.Decoder` 同名 → CS0104，必须别名。
using JsonDecoder = TypeScript.Json.Decoder;

using static TypeScript.TestUtil.ContentMapperTest.ContentMapperTest;

namespace TypeScript.TestUtil.ContentMapperTest;

public static partial class ContentMapperTest
{
    /// <summary>Go: <c>const preamble = "const __VERSION = \"1.0.0\";\n"</c>（未导出 → internal）。</summary>
    internal const string Preamble = "const __VERSION = \"1.0.0\";\n";

    /// <summary>Go: <c>var DeclaredOptions = []string{"target", "jsx"}</c>（导出 → public；manifest.go 用它填
    /// package.json 的 compilerOptions）。</summary>
    public static readonly string[] DeclaredOptions = ["target", "jsx"];

    /// <summary>Go: <c>const diagnosticSource = "box"</c>（未导出 → internal）。</summary>
    internal const string DiagnosticSource = "box";

    /// <summary>Go: <c>const unclosedInterpolationCode = 1000</c>（未导出 → internal）。</summary>
    internal const int UnclosedInterpolationCode = 1000;

    /// <summary>
    /// Go: <c>func mappedExtension(content string) string</c>（未导出 → PascalCase）。首行是
    /// `// @box-extension:` 时用它声明的虚拟扩展名，否则 ".ts"。
    /// </summary>
    internal static string MappedExtension(string content)
    {
        const string prefix = "// @box-extension:";
        var newline = content.IndexOf('\n');
        if (newline >= 0)
        {
            var firstLine = content[..newline];
            if (firstLine.StartsWith(prefix, StringComparison.Ordinal))
            {
                return firstLine[prefix.Length..].Trim();
            }
        }

        return ".ts";
    }

    /// <summary>
    /// Go: <c>func transform(content string, options *collections.OrderedMap[string, json.Value])
    /// (string, json.Value, []contentmapper.Diagnostic, *contentmapper.DiagnosticDirectives, error)</c>。
    /// 把 `#{name}` 换成声明过的编译选项值（原样写入选项的原始 JSON 文本），原文其余部分逐字节 verbatim
    /// 映射到虚拟文本；未闭合的插值报一条诊断并写入 `undefined`。
    /// </summary>
    internal static (string Text, byte[] Mappings, Diagnostic[]? Diagnostics, DiagnosticDirectives? Directives) Transform(
        string content,
        OrderedMap<string, byte[]>? options)
    {
        var @virtual = new StringBuilder();
        var segments = new List<Segment>();
        var diagnostics = new List<Diagnostic>();

        @virtual.Append(Preamble);

        void WriteVerbatim(int from, int to)
        {
            if (to <= from)
            {
                return;
            }

            var virtualStart = new TextPos(@virtual.Length);
            @virtual.Append(content, from, to - from);
            segments.Add(new Segment
            {
                VirtualStart = virtualStart,
                VirtualEnd = new TextPos(@virtual.Length),
                OriginalStart = new TextPos(from),
                OriginalEnd = new TextPos(to),
                Kind = Kind.Verbatim,
                Features = Feature.All,
            });
        }

        void WriteAtom(string value, int from, int to)
        {
            var virtualStart = new TextPos(@virtual.Length);
            @virtual.Append(value);
            segments.Add(new Segment
            {
                VirtualStart = virtualStart,
                VirtualEnd = new TextPos(@virtual.Length),
                OriginalStart = new TextPos(from),
                OriginalEnd = new TextPos(to),
                Kind = Kind.Atom,
                Features = Feature.All,
            });
        }

        var pos = 0;
        while (pos < content.Length)
        {
            // Go: `rel := strings.Index(content[pos:], "#{")`（相对 pos）→ C# 的绝对下标即 tokenStart。
            var tokenStart = content.IndexOf("#{", pos, StringComparison.Ordinal);
            if (tokenStart < 0)
            {
                WriteVerbatim(pos, content.Length);
                break;
            }

            var lineEnd = content.IndexOf('\n', tokenStart);
            if (lineEnd < 0)
            {
                lineEnd = content.Length;
            }

            var close = content.IndexOf('}', tokenStart, lineEnd - tokenStart);
            if (close < 0)
            {
                WriteVerbatim(pos, tokenStart);
                WriteAtom("undefined", tokenStart, lineEnd);
                diagnostics.Add(new Diagnostic
                {
                    MessageText = "Unclosed interpolation.",
                    Start = tokenStart,
                    Length = lineEnd - tokenStart,
                    Code = UnclosedInterpolationCode,
                });
                pos = lineEnd;
                continue;
            }

            var tokenEnd = close + 1;
            var name = content[(tokenStart + 2)..(tokenEnd - 1)];

            WriteVerbatim(pos, tokenStart);
            WriteAtom(RenderOption(options, name), tokenStart, tokenEnd);
            pos = tokenEnd;
        }

        var spanMap = SpanMap.SpanMap.New([.. segments]);
        var mappings = spanMap.Marshal();
        return (@virtual.ToString(), mappings, diagnostics.Count == 0 ? null : diagnostics.ToArray(), DiagnosticDirectives(content, spanMap));
    }

    /// <summary>
    /// Go: <c>func diagnosticDirectives(content string, mappings *spanmap.SpanMap)
    /// *contentmapper.DiagnosticDirectives</c>。`// @box-ignore` / `// @box-expect-error: …` 行会为"下一行"造出
    /// 映射后的诊断指令；`// @box-invalid-directive:` 开头的内容造出各种非法指令（用于错误路径测试）。
    /// 没有指令时返回 nil。
    /// </summary>
    internal static DiagnosticDirectives? DiagnosticDirectives(string content, SpanMap.SpanMap mappings)
    {
        DiagnosticDirectives Wrap(MappedDiagnosticDirective[] directives, params UnusedExpectDirectiveDiagnostic[] unused) => new()
        {
            UnusedExpectDirectiveDiagnostics = unused,
            Directives = directives,
        };

        const string invalidPrefix = "// @box-invalid-directive:";
        if (content.StartsWith(invalidPrefix, StringComparison.Ordinal))
        {
            // Go: `strings.TrimSpace(strings.TrimPrefix(strings.SplitN(content, "\n", 2)[0], invalidPrefix))`。
            var newline = content.IndexOf('\n');
            var firstLine = newline < 0 ? content : content[..newline];
            switch (firstLine[invalidPrefix.Length..].Trim())
            {
                case "invalid-range":
                    return Wrap([new MappedDiagnosticDirective { VirtualStart = -1, Policy = DiagnosticDirectivePolicy.Ignore }]);

                case "original-range-out-of-bounds":
                    return Wrap([new MappedDiagnosticDirective { OriginalStart = content.Length + 1, Policy = DiagnosticDirectivePolicy.Ignore }]);

                case "virtual-range-out-of-bounds":
                    return Wrap([new MappedDiagnosticDirective { VirtualStart = 1 << 20, VirtualEnd = 1 << 20, Policy = DiagnosticDirectivePolicy.Ignore }]);

                case "invalid-policy":
                    return Wrap([new MappedDiagnosticDirective { Policy = (DiagnosticDirectivePolicy)2 }]);

                case "ignore-with-unused-diagnostic":
                    return Wrap([new MappedDiagnosticDirective { Policy = DiagnosticDirectivePolicy.Ignore }], new UnusedExpectDirectiveDiagnostic());

                case "expect-without-unused-diagnostic":
                    return Wrap([new MappedDiagnosticDirective { Policy = DiagnosticDirectivePolicy.Expect }]);

                case "invalid-unused-diagnostic-index":
                {
                    var index = 1;
                    return Wrap([new MappedDiagnosticDirective { Policy = DiagnosticDirectivePolicy.Expect, UnusedExpectDirectiveIndex = index }], new UnusedExpectDirectiveDiagnostic());
                }

                case "overlap":
                    return Wrap(
                    [
                        new MappedDiagnosticDirective { VirtualEnd = 2, Policy = DiagnosticDirectivePolicy.Ignore },
                        new MappedDiagnosticDirective { VirtualStart = 1, VirtualEnd = 3, Policy = DiagnosticDirectivePolicy.Ignore },
                    ]);
            }
        }

        const string ignorePrefix = "// @box-ignore";
        const string expectPrefix = "// @box-expect-error";
        var result = new List<MappedDiagnosticDirective>();
        var unusedDiagnostics = new List<UnusedExpectDirectiveDiagnostic>();
        for (var lineStart = 0; lineStart < content.Length;)
        {
            var lineEnd = content.IndexOf('\n', lineStart);
            if (lineEnd < 0)
            {
                lineEnd = content.Length;
            }

            var line = content[lineStart..lineEnd];
            var trimmed = line.Trim();
            var policy = DiagnosticDirectivePolicy.Ignore;
            var hasPolicy = false;
            var unusedDiagnosticIndex = -1;
            if (trimmed == ignorePrefix)
            {
                policy = DiagnosticDirectivePolicy.Ignore;
                hasPolicy = true;
            }
            else if (trimmed.StartsWith(expectPrefix + ":", StringComparison.Ordinal))
            {
                policy = DiagnosticDirectivePolicy.Expect;
                hasPolicy = true;
                unusedDiagnosticIndex = unusedDiagnostics.Count;
                unusedDiagnostics.Add(new UnusedExpectDirectiveDiagnostic
                {
                    Code = 2578,
                    MessageText = trimmed[(expectPrefix.Length + 1)..].Trim(),
                });
            }

            if (hasPolicy && lineEnd < content.Length)
            {
                // 指令作用于它的下一行。
                var affectedStart = lineEnd + 1;
                var affectedEnd = content.IndexOf('\n', affectedStart);
                var affectedLength = affectedEnd < 0 ? content.Length - affectedStart : affectedEnd - affectedStart;
                var virtualSpans = mappings.OriginalToVirtualSpans(NewTextRange(affectedStart, affectedStart + affectedLength), Feature.All);
                if (virtualSpans.Length == 1)
                {
                    var directive = new MappedDiagnosticDirective
                    {
                        OriginalStart = lineStart,
                        OriginalLength = lineEnd - lineStart,
                        VirtualStart = virtualSpans[0].Span.Pos(),
                        VirtualEnd = virtualSpans[0].Span.End(),
                        Policy = policy,
                    };
                    if (unusedDiagnosticIndex >= 0)
                    {
                        directive.UnusedExpectDirectiveIndex = unusedDiagnosticIndex;
                    }

                    result.Add(directive);
                }
            }

            if (lineEnd == content.Length)
            {
                break;
            }

            lineStart = lineEnd + 1;
        }

        // Go: 只有一条 unused-expect 诊断时，指令不必写索引（省略即指它）。
        if (unusedDiagnostics.Count == 1)
        {
            foreach (var directive in result)
            {
                directive.UnusedExpectDirectiveIndex = null;
            }
        }

        if (result.Count == 0 && unusedDiagnostics.Count == 0)
        {
            return null;
        }

        return Wrap([.. result], [.. unusedDiagnostics]);
    }

    /// <summary>Go: <c>func renderOption(options *collections.OrderedMap[string, json.Value], name string) string</c>。
    /// 选项存在且非空时返回它的原始 JSON 文本，否则返回 "undefined"。</summary>
    internal static string RenderOption(OrderedMap<string, byte[]>? options, string name)
    {
        if (options is not null)
        {
            var (value, ok) = options.Get(name);
            if (ok && value is { Length: > 0 })
            {
                return Encoding.UTF8.GetString(value);
            }
        }

        return "undefined";
    }
}

/// <summary>Go: <c>type Handler struct</c>（transforming mapper：把 `#{option}` 插值成编译选项值）。</summary>
public class Handler : NoNotifications, Ipc.Handler, IProjectLifecycleHandler
{
    /// <summary>Go: <c>mu sync.Mutex</c>。</summary>
    private readonly object _mu = new();

    /// <summary>Go: <c>compilerOptions map[string]*collections.OrderedMap[string, json.Value]</c>（nil → null）。</summary>
    private Dictionary<string, OrderedMap<string, byte[]>?>? _compilerOptions;

    /// <summary>
    /// C# 侧新增：Go 的 `json.Unmarshal(p.CompilerOptions, &options)`（见文件头第 5 条）。逐 token 读出
    /// `{ "jsx": "react", … }` 的键与**原始 JSON 值**（`Decoder.ReadValue()`），保持键顺序。
    /// </summary>
    private static OrderedMap<string, byte[]>? UnmarshalCompilerOptions(byte[]? data)
    {
        // C# 的 `byte[]?` 同时表示 Go 的"字段缺失"与"JSON null"（文件头第 5 条）。
        if (data is null)
        {
            return null;
        }

        var decoder = new JsonDecoder(data);
        if (decoder.PeekKind() == JsonKind.Null)
        {
            decoder.ReadToken();
            return null;
        }

        if (decoder.PeekKind() != JsonKind.BeginObject)
        {
            throw new JsonException("contentmappertest: expected an object for compilerOptions");
        }

        decoder.ReadToken();
        var options = new OrderedMap<string, byte[]>();
        while (decoder.PeekKind() != JsonKind.EndObject)
        {
            var name = decoder.ReadName();
            options.Set(name, decoder.ReadValue());
        }

        decoder.ReadToken();
        return options;
    }

    /// <summary>Go: <c>func (h *Handler) OpenProject(p contentmapper.OpenProjectParams) error</c>。</summary>
    public void OpenProject(OpenProjectParams p)
    {
        var options = UnmarshalCompilerOptions(p.CompilerOptions);
        lock (_mu)
        {
            // Go: `if h.compilerOptions == nil { h.compilerOptions = make(…) }`。
            _compilerOptions ??= new Dictionary<string, OrderedMap<string, byte[]>?>(StringComparer.Ordinal);
            _compilerOptions[p.ProjectHandle] = options;
        }
    }

    /// <summary>Go: <c>func (h *Handler) CloseProject(p contentmapper.CloseProjectParams)</c>。</summary>
    public void CloseProject(CloseProjectParams p)
    {
        lock (_mu)
        {
            // Go: `delete(h.compilerOptions, p.ProjectHandle)`（nil map 上的 delete 是 no-op）。
            _compilerOptions?.Remove(p.ProjectHandle);
        }
    }

    /// <summary>Go: <c>func (h *Handler) HandleRequest(ctx, method, params) (any, error)</c>。</summary>
    public object? HandleRequest(Context ctx, string method, byte[]? @params)
    {
        switch (method)
        {
            case MethodInitialize:
                return InitializeResult(DiagnosticSource);

            case MethodTransform:
            {
                var p = UnmarshalNew<TransformParams>(@params ?? []);
                OrderedMap<string, byte[]>? options;
                lock (_mu)
                {
                    // Go: `options := h.compilerOptions[p.ProjectHandle]`（nil map 上取不到 → nil）。
                    options = _compilerOptions is not null && _compilerOptions.TryGetValue(p.ProjectHandle, out var stored)
                        ? stored
                        : null;
                }

                if (options is null)
                {
                    throw new Exception($"contentmappertest: project \"{p.ProjectHandle}\" is not open");
                }

                var (text, mappings, diagnostics, diagnosticDirectives) = Transform(p.Content, options);
                return new TransformResult
                {
                    Text = text,
                    Extension = MappedExtension(p.Content),
                    Mappings = mappings,
                    DiagnosticDirectives = diagnosticDirectives,
                    Diagnostics = diagnostics,
                };
            }

            default:
                throw new Exception($"contentmappertest: unexpected method \"{method}\"");
        }
    }
}
