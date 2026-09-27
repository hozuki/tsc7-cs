// ported from: internal/testutil/contentmappertest/editing.go
//
// 与 Go 的差异（逐条）：
// 1) Go 的 `prefixedSupplementalHandler struct{ noNotifications }` / `unmappedFoldingHandler struct{ noNotifications }`
//    （内嵌只为提升 HandleNotification）→ C# 继承 `NoNotifications`（同包 Protocol.cs 提供）。
// 2) 未导出类型改名：`prefixedSupplementalHandler` → `PrefixedSupplementalHandler`、`unmappedFoldingHandler` →
//    `UnmappedFoldingHandler`。
// 3) `(T, error)` → 正常返回 T、失败抛异常；`json.Unmarshal` 失败 → JsonException；params 为空 → `?? throw`。
// 4) `strings.Contains`/`strings.Index` → 显式序数比较的 `string.Contains`/`IndexOf`（Go 的字符串比较就是序数比较）。
// 5) Go 的 `content[a:b]` 切片 → C# 的区间下标（`content[a..b]`）；Go 的 `segments = []spanmap.Segment{…}` 重新赋值 →
//    C# 对 `Segment[]` 局部变量用集合表达式重新赋值。
// 6) Go 的 `spanmap.New(segments)` → `SpanMap.SpanMap.New(segments)`（见 SpanMap/SpanMap.cs 文件头第 2 条）；
//    `Marshal()` 失败抛异常，没有错误分支。Go 的 `spanmap.New(nil)` → `SpanMap.SpanMap.New(null)`。
// 7) Go 的 `TransformResult{MappedOutput: canonical, …}`（内嵌结构体赋值）→ C# 逐字段复制（内嵌 → 继承的映射见
//    ContentMapper/HostImpl.cs 文件头）。
// 8) Go 的反引号原始字符串 → C# raw string literal（`"""`）。
// 9) Go 的 switch 各 case 是独立作用域，C# 的 switch 段共享作用域 → formatting-overlap 分支里的 `mappings`
//    改名 `overlapMappings`（该分支立即 return，Go 里与后面的 `mappings` 不冲突）。
using TypeScript.ContentMapper;
using TypeScript.Core;
using TypeScript.Ipc;
using TypeScript.Json;
using TypeScript.SpanMap;
using static TypeScript.ContentMapper.ContentMapper;
using static TypeScript.Ipc.Ipc;

namespace TypeScript.TestUtil.ContentMapperTest;

/// <summary>Go: <c>type prefixedSupplementalHandler struct{ noNotifications }</c>（补充输出带前缀/插入生成代码的映射器）。</summary>
public class PrefixedSupplementalHandler : NoNotifications, Ipc.Handler
{
    /// <summary>Go: <c>func (prefixedSupplementalHandler) HandleRequest(ctx, method, params) (any, error)</c>。</summary>
    public object? HandleRequest(Context ctx, string method, byte[]? @params)
    {
        switch (method)
        {
            case MethodInitialize:
                return ContentMapperTest.InitializeResult("mapper");
            case MethodTransform:
                var p = UnmarshalParams<TransformParams>(@params)
                    ?? throw new JsonException("contentmappertest: missing transform parameters");
                const string prefix = "/* generated */\n";
                var features = Feature.All;
                if (p.FileName.Contains("folding-disabled", StringComparison.Ordinal)
                    || p.FileName.Contains("codelens-disabled", StringComparison.Ordinal)
                    || p.FileName.Contains("formatting-disabled", StringComparison.Ordinal))
                {
                    features = Feature.None;
                }

                var supplementalText = prefix + p.Content;
                var segments = new Segment[]
                {
                    new()
                    {
                        VirtualStart = new TextPos(prefix.Length),
                        VirtualEnd = new TextPos(prefix.Length + p.Content.Length),
                        OriginalStart = 0,
                        OriginalEnd = new TextPos(p.Content.Length),
                        Kind = Kind.Verbatim,
                        Features = features,
                    },
                };
                if (p.FileName.Contains("formatting-split", StringComparison.Ordinal))
                {
                    var secondStart = p.Content.IndexOf("function second", StringComparison.Ordinal);
                    if (secondStart < 0)
                    {
                        throw new Exception("contentmappertest: formatting-split input is missing function second");
                    }

                    const string generated = "const generated={x:1};\n";
                    supplementalText = prefix + p.Content[..secondStart] + generated + p.Content[secondStart..];
                    segments =
                    [
                        new Segment
                        {
                            VirtualStart = new TextPos(prefix.Length),
                            VirtualEnd = new TextPos(prefix.Length + secondStart),
                            OriginalStart = 0,
                            OriginalEnd = new TextPos(secondStart),
                            Kind = Kind.Verbatim,
                            Features = Feature.All,
                        },
                        new Segment
                        {
                            VirtualStart = new TextPos(prefix.Length + secondStart + generated.Length),
                            VirtualEnd = new TextPos(supplementalText.Length),
                            OriginalStart = new TextPos(secondStart),
                            OriginalEnd = new TextPos(p.Content.Length),
                            Kind = Kind.Verbatim,
                            Features = Feature.All,
                        },
                    ];
                }

                if (p.FileName.Contains("formatting-overlap", StringComparison.Ordinal))
                {
                    var secondStart = p.Content.IndexOf("function second", StringComparison.Ordinal);
                    var thirdStart = p.Content.IndexOf("function third", StringComparison.Ordinal);
                    if (secondStart < 0 || thirdStart < 0)
                    {
                        throw new Exception("contentmappertest: formatting-overlap input is missing function second or third");
                    }

                    const string wrapperStart = "if (true) {\n";
                    const string wrapperEnd = "}\n";
                    supplementalText = wrapperStart + p.Content[secondStart..] + wrapperEnd;
                    segments =
                    [
                        new Segment
                        {
                            VirtualStart = new TextPos(wrapperStart.Length),
                            VirtualEnd = new TextPos(wrapperStart.Length + p.Content.Length - secondStart),
                            OriginalStart = new TextPos(secondStart),
                            OriginalEnd = new TextPos(p.Content.Length),
                            Kind = Kind.Verbatim,
                            Features = Feature.All,
                        },
                    ];
                    var canonicalMappings = SpanMap.SpanMap.New(
                    [
                        new Segment
                        {
                            VirtualStart = 0,
                            VirtualEnd = new TextPos(thirdStart),
                            OriginalStart = 0,
                            OriginalEnd = new TextPos(thirdStart),
                            Kind = Kind.Verbatim,
                            Features = Feature.All,
                        },
                    ]).Marshal();
                    var overlapMappings = SpanMap.SpanMap.New(segments).Marshal();
                    return new TransformResult
                    {
                        Text = p.Content[..thirdStart],
                        Extension = ".ts",
                        Mappings = canonicalMappings,
                        Supplemental =
                        [
                            new SupplementalOutput
                            {
                                Text = supplementalText,
                                Extension = ".ts",
                                Mappings = overlapMappings,
                            },
                        ],
                    };
                }

                var mappings = SpanMap.SpanMap.New(segments).Marshal();
                var canonical = new MappedOutput { Text = "export {};", Extension = ".ts" };
                if (p.FileName.Contains("folding-duplicate", StringComparison.Ordinal)
                    || p.FileName.Contains("codelens-disabled", StringComparison.Ordinal)
                    || p.FileName.Contains("codelens-duplicate", StringComparison.Ordinal))
                {
                    var canonicalMappings = SpanMap.SpanMap.New(
                    [
                        new Segment
                        {
                            VirtualStart = 0,
                            VirtualEnd = new TextPos(p.Content.Length),
                            OriginalStart = 0,
                            OriginalEnd = new TextPos(p.Content.Length),
                            Kind = Kind.Verbatim,
                            Features = Feature.All,
                        },
                    ]).Marshal();
                    canonical = new MappedOutput { Text = p.Content, Extension = ".ts", Mappings = canonicalMappings };
                }

                return new TransformResult
                {
                    Text = canonical.Text,
                    Extension = canonical.Extension,
                    Mappings = canonical.Mappings,
                    DiagnosticDirectives = canonical.DiagnosticDirectives,
                    Supplemental =
                    [
                        new SupplementalOutput
                        {
                            Text = supplementalText,
                            Extension = ".ts",
                            Mappings = mappings,
                        },
                    ],
                };
            default:
                throw new Exception($"contentmappertest: unexpected method \"{method}\"");
        }
    }
}

/// <summary>Go: <c>type unmappedFoldingHandler struct{ noNotifications }</c>（输出不带任何映射的映射器）。</summary>
public class UnmappedFoldingHandler : NoNotifications, Ipc.Handler
{
    /// <summary>Go: <c>func (unmappedFoldingHandler) HandleRequest(ctx, method, params) (any, error)</c>。</summary>
    public object? HandleRequest(Context ctx, string method, byte[]? @params)
    {
        switch (method)
        {
            case MethodInitialize:
                return ContentMapperTest.InitializeResult("mapper");
            case MethodTransform:
                var mappings = SpanMap.SpanMap.New(null).Marshal();
                return new TransformResult
                {
                    Text = """
                        import "a";
                        import "b";
                        /*
                         * generated
                         */
                        export {};
                        """,
                    Extension = ".ts",
                    Mappings = mappings,
                };
            default:
                throw new Exception($"contentmappertest: unexpected method \"{method}\"");
        }
    }
}
