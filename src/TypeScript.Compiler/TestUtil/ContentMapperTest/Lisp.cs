// ported from: internal/testutil/contentmappertest/lisp.go
//
// 与 Go 的差异（逐条）：
// 1) Go 的 `lispHandler struct{ noNotifications }`（内嵌只为提升 HandleNotification）→ C# 继承
//    `NoNotifications`（同包 Protocol.cs 提供）。
// 2) 未导出类型改名：`lispHandler` → `LispHandler`。
// 3) `(T, error)` → 正常返回 T、失败抛异常；`json.Unmarshal` 失败 → JsonException；params 为空 → `?? throw`。
// 4) `strings.TrimSuffix(s, "\n")` → `s.EndsWith("\n", StringComparison.Ordinal) ? s[..^1] : s`（C# 没有等价成员，
//    等价写法见 ModuleSpecifiers/Util.cs 的 TrimSuffix / StringUtil.HasSuffix）。
// 5) `json.Value`（= 原始 JSON 字节）→ `byte[]?`；`%q` → `"\"…\""`（ContentMapper/HostImpl.cs 文件头第 9 条）。
using TypeScript.ContentMapper;
using TypeScript.Core;
using TypeScript.Ipc;
using TypeScript.Json;
using TypeScript.SpanMap;
using static TypeScript.ContentMapper.ContentMapper;
using static TypeScript.Ipc.Ipc;

namespace TypeScript.TestUtil.ContentMapperTest;

/// <summary>Go: <c>type lispHandler struct{ noNotifications }</c>（把一种 Lisp 表达式映射成 TS 调用的映射器）。</summary>
public class LispHandler : NoNotifications, Ipc.Handler
{
    /// <summary>Go: <c>func (lispHandler) HandleRequest(ctx, method, params) (any, error)</c>。</summary>
    public object? HandleRequest(Context ctx, string method, byte[]? @params)
    {
        switch (method)
        {
            case MethodInitialize:
                return ContentMapperTest.InitializeResult("lisp");
            case MethodTransform:
                var p = UnmarshalParams<TransformParams>(@params)
                    ?? throw new JsonException("contentmappertest: missing transform parameters");
                var trimmed = p.Content.EndsWith("\n", StringComparison.Ordinal) ? p.Content[..^1] : p.Content;
                if (!trimmed.Equals("(+ 1 2 \"oops\")", StringComparison.Ordinal))
                {
                    throw new Exception($"contentmappertest: unsupported Lisp expression \"{p.Content}\"");
                }

                var mappings = SpanMap.SpanMap.New(
                [
                    new Segment { VirtualStart = 0, VirtualEnd = 3, OriginalStart = 1, OriginalEnd = 2, Kind = Kind.Alias, Features = Feature.All },
                    new Segment { VirtualStart = 4, VirtualEnd = 5, OriginalStart = 3, OriginalEnd = 4, Kind = Kind.Verbatim, Features = Feature.All },
                    new Segment { VirtualStart = 7, VirtualEnd = 8, OriginalStart = 5, OriginalEnd = 6, Kind = Kind.Verbatim, Features = Feature.All },
                    new Segment { VirtualStart = 10, VirtualEnd = 16, OriginalStart = 7, OriginalEnd = 13, Kind = Kind.Verbatim, Features = Feature.All },
                ]).Marshal();
                return new TransformResult
                {
                    Text = "add(1, 2, \"oops\");",
                    Extension = ".ts",
                    Mappings = mappings,
                };
            default:
                throw new Exception($"contentmappertest: unexpected method \"{method}\"");
        }
    }
}
