// ported from: internal/testutil/contentmappertest/duplicate.go
//
// 与 Go 的差异（逐条）：
// 1) Go 的 `duplicateHandler struct{ noNotifications }`（内嵌只为提升 HandleNotification）→ C# 继承
//    `NoNotifications`（同包 Protocol.cs 提供）。
// 2) 未导出类型改名：`duplicateHandler` → `DuplicateHandler`。
// 3) `(T, error)` → 正常返回 T、失败抛异常；`json.Unmarshal` 失败 → JsonException；params 为空 → `?? throw`。
// 4) `strings.Contains(s, sub)` → `s.Contains(sub, StringComparison.Ordinal)`；`strings.Index`/`LastIndex` 同理
//    显式用序数比较（Go 的字符串比较就是按字节的序数比较）。
// 5) Go 的 `content[a:b]` 切片 → C# 的区间下标（`content[a..b]`），下标都是绝对位置，与 Go 的相对下标算式等价。
// 6) `json.Value`（= 原始 JSON 字节）→ `byte[]?`（见 ContentMapper/HostImpl.cs 文件头第 1 条）。
// 7) Go 的局部变量名 `virtual` 是 C# 的保留字，且各分支里的 `first`/`second`/`mappings` 在 C# 的 switch 段里共享
//    作用域 → 最后一个（兜底）分支改用 `plainVirtual`/`plainFirst`/`plainSecond`/`plainMappings`（若分支内的名字在
//    C# 里不冲突则照抄 Go）。
using TypeScript.ContentMapper;
using TypeScript.Core;
using TypeScript.Ipc;
using TypeScript.Json;
using TypeScript.SpanMap;
using static TypeScript.ContentMapper.ContentMapper;
using static TypeScript.Ipc.Ipc;

namespace TypeScript.TestUtil.ContentMapperTest;

/// <summary>Go: <c>type duplicateHandler struct{ noNotifications }</c>（同一原文位置出现多份投影的映射器）。</summary>
public class DuplicateHandler : NoNotifications, Ipc.Handler
{
    /// <summary>Go: <c>func (duplicateHandler) HandleRequest(ctx, method, params) (any, error)</c>。</summary>
    public object? HandleRequest(Context ctx, string method, byte[]? @params)
    {
        switch (method)
        {
            case MethodInitialize:
                return ContentMapperTest.InitializeResult("mapper");
            case MethodTransform:
                var p = UnmarshalParams<TransformParams>(@params)
                    ?? throw new JsonException("contentmappertest: missing transform parameters");
                if (p.FileName.Contains("hover-fallback", StringComparison.Ordinal))
                {
                    var virtualText = "// " + p.Content + "\nconst " + p.Content + " = 1;\n";
                    var first = "// ".Length;
                    var second = first + p.Content.Length + "\nconst ".Length;
                    var mappings = SpanMap.SpanMap.New(
                    [
                        new Segment { VirtualStart = new TextPos(first), VirtualEnd = new TextPos(first + p.Content.Length), OriginalStart = 0, OriginalEnd = new TextPos(p.Content.Length), Kind = Kind.Verbatim, Features = Feature.Hover },
                        new Segment { VirtualStart = new TextPos(second), VirtualEnd = new TextPos(second + p.Content.Length), OriginalStart = 0, OriginalEnd = new TextPos(p.Content.Length), Kind = Kind.Verbatim, Features = Feature.Hover },
                    ]).Marshal();
                    return new TransformResult { Text = virtualText, Extension = ".ts", Mappings = mappings };
                }

                if (p.FileName.Contains("hover-concat", StringComparison.Ordinal))
                {
                    var virtualText = "namespace A { export const " + p.Content + " = 1; }\nnamespace B { export const " + p.Content + " = \"text\"; }\n";
                    var first = virtualText.IndexOf(p.Content, StringComparison.Ordinal);
                    var second = virtualText.LastIndexOf(p.Content, StringComparison.Ordinal);
                    var mappings = SpanMap.SpanMap.New(
                    [
                        new Segment { VirtualStart = new TextPos(first), VirtualEnd = new TextPos(first + p.Content.Length), OriginalStart = 0, OriginalEnd = new TextPos(p.Content.Length), Kind = Kind.Verbatim, Features = Feature.Hover },
                        new Segment { VirtualStart = new TextPos(second), VirtualEnd = new TextPos(second + p.Content.Length), OriginalStart = 0, OriginalEnd = new TextPos(p.Content.Length), Kind = Kind.Verbatim, Features = Feature.Hover },
                    ]).Marshal();
                    return new TransformResult { Text = virtualText, Extension = ".ts", Mappings = mappings };
                }

                if (p.FileName.Contains("signature-fallback", StringComparison.Ordinal))
                {
                    var virtualText = "// " + p.Content + "\nfunction use(value: number): void {}\n" + p.Content + ";\n";
                    var first = "// ".Length;
                    var second = virtualText.LastIndexOf(p.Content, StringComparison.Ordinal);
                    var mappings = SpanMap.SpanMap.New(
                    [
                        new Segment { VirtualStart = new TextPos(first), VirtualEnd = new TextPos(first + p.Content.Length), OriginalStart = 0, OriginalEnd = new TextPos(p.Content.Length), Kind = Kind.Verbatim, Features = Feature.SignatureHelp },
                        new Segment { VirtualStart = new TextPos(second), VirtualEnd = new TextPos(second + p.Content.Length), OriginalStart = 0, OriginalEnd = new TextPos(p.Content.Length), Kind = Kind.Verbatim, Features = Feature.SignatureHelp },
                    ]).Marshal();
                    return new TransformResult { Text = virtualText, Extension = ".ts", Mappings = mappings };
                }

                if (p.FileName.Contains("rename-conflict", StringComparison.Ordinal))
                {
                    var virtualText = "export const " + p.Content + " = 1;\nconst object = { " + p.Content + " };\n" + p.Content + ";\n";
                    var first = virtualText.IndexOf(p.Content, StringComparison.Ordinal);
                    // Go: second := strings.Index(virtual[first+len(p.Content):], p.Content) + first + len(p.Content)
                    //（相对下标 + 偏移）→ C# 的 IndexOf(startIndex) 直接给出绝对下标。
                    var second = virtualText.IndexOf(p.Content, first + p.Content.Length, StringComparison.Ordinal);
                    var third = virtualText.IndexOf(p.Content, second + p.Content.Length, StringComparison.Ordinal);
                    var mappings = SpanMap.SpanMap.New(
                    [
                        new Segment { VirtualStart = new TextPos(first), VirtualEnd = new TextPos(first + p.Content.Length), OriginalStart = 0, OriginalEnd = new TextPos(p.Content.Length), Kind = Kind.Verbatim, Features = Feature.Rename },
                        new Segment { VirtualStart = new TextPos(second), VirtualEnd = new TextPos(second + p.Content.Length), OriginalStart = 0, OriginalEnd = new TextPos(p.Content.Length), Kind = Kind.Verbatim, Features = Feature.Rename },
                        new Segment { VirtualStart = new TextPos(third), VirtualEnd = new TextPos(third + p.Content.Length), OriginalStart = 0, OriginalEnd = new TextPos(p.Content.Length), Kind = Kind.Verbatim, Features = Feature.Rename },
                    ]).Marshal();
                    return new TransformResult { Text = virtualText, Extension = ".ts", Mappings = mappings };
                }

                var plainVirtual = "export const " + p.Content + " = 1;\n" + p.Content + ";\n";
                var plainFirst = "export const ".Length;
                var plainSecond = plainFirst + p.Content.Length + " = 1;\n".Length;
                var disabled = p.FileName.Contains("disabled", StringComparison.Ordinal);
                var semanticFeatures = Feature.Hover | Feature.Definition | Feature.References | Feature.Rename;
                var navigationFeatures = Feature.Definition | Feature.References | Feature.Rename;
                if (disabled)
                {
                    semanticFeatures = Feature.None;
                    navigationFeatures = Feature.None;
                }

                var plainMappings = SpanMap.SpanMap.New(
                [
                    new Segment { VirtualStart = new TextPos(plainFirst), VirtualEnd = new TextPos(plainFirst + p.Content.Length), OriginalStart = 0, OriginalEnd = new TextPos(p.Content.Length), Kind = Kind.Verbatim, Features = semanticFeatures },
                    new Segment { VirtualStart = new TextPos(plainSecond), VirtualEnd = new TextPos(plainSecond + p.Content.Length), OriginalStart = 0, OriginalEnd = new TextPos(p.Content.Length), Kind = Kind.Verbatim, Features = navigationFeatures },
                ]).Marshal();
                return new TransformResult { Text = plainVirtual, Extension = ".ts", Mappings = plainMappings };
            default:
                throw new Exception($"contentmappertest: unexpected method \"{method}\"");
        }
    }
}
