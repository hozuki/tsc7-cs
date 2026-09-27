// ported from: internal/testutil/contentmappertest/component.go
//
// 与 Go 的差异（逐条）：
// 1) Go 的 `componentHandler struct{ noNotifications }`（内嵌只为提升 HandleNotification）→ C# 用继承：
//    `ComponentHandler : NoNotifications, Ipc.Handler`（NoNotifications 由同包的 Protocol.cs 提供）。
// 2) 未导出类型/函数按 C# 命名习惯改名为 PascalCase：`componentHandler` → `ComponentHandler`、
//    `componentNameRange` → `TryComponentNameRange`、`isIdentifierStart`/`isIdentifierPart` →
//    `IsIdentifierStart`/`IsIdentifierPart`。
// 3) `(T, error)` → 正常返回 T、失败抛异常（见 Ipc/Conn.cs 文件头第 7 条）。
// 4) `(start, end int, ok bool)` → `bool TryComponentNameRange(..., out int start, out int end)`。
// 5) `json.Value`（= 原始 JSON 字节）→ `byte[]?`（见 ContentMapper/HostImpl.cs 文件头第 1 条）。
// 6) Go 的 `content[i]` 是 UTF-8 字节下标；C# 的 `content[i]` 是 UTF-16 码元，与全仓位置语义一致
//    （见 Core/Text.cs 文件头）。
// 7) `if err := json.Unmarshal(params, &p); err != nil { return nil, err }` → 解码失败时 Json 抛
//    JsonException；params 为空/nil 时 Go 的 Unmarshal 同样报错，故用 `?? throw` 保留同一失败路径。
using TypeScript.ContentMapper;
using TypeScript.Core;
using TypeScript.Ipc;
using TypeScript.Json;
using TypeScript.SpanMap;
using static TypeScript.ContentMapper.ContentMapper;
using static TypeScript.Ipc.Ipc;
using StringBuilder = System.Text.StringBuilder;

namespace TypeScript.TestUtil.ContentMapperTest;

/// <summary>Go: <c>type componentHandler struct{ noNotifications }</c>（Svelte 风格的组件映射器）。</summary>
public class ComponentHandler : NoNotifications, Ipc.Handler
{
    /// <summary>Go: <c>func (componentHandler) HandleRequest(ctx context.Context, method string, params json.Value) (any, error)</c>。</summary>
    public object? HandleRequest(Context ctx, string method, byte[]? @params)
    {
        switch (method)
        {
            case MethodInitialize:
                return ContentMapperTest.InitializeResult("mapper");
            case MethodTransform:
                var p = UnmarshalParams<TransformParams>(@params)
                    ?? throw new JsonException("contentmappertest: missing transform parameters");
                var (text, mappings) = ContentMapperTest.TransformComponent(p.Content);
                return new TransformResult { Text = text, Extension = ".ts", Mappings = mappings };
            default:
                throw new Exception($"contentmappertest: unexpected method \"{method}\"");
        }
    }
}

/// <summary>Go 的 <c>contentmappertest</c> 包级函数/常量（component.go 部分）。</summary>
public static partial class ContentMapperTest
{
    /// <summary>Go: <c>func transformComponent(content string) (string, json.Value, error)</c>。</summary>
    public static (string Text, byte[]? Mappings) TransformComponent(string content)
    {
        var virtualText = new StringBuilder();
        var segments = new List<Segment>();

        // Go: writeSynthesized := func(text string) { virtual.WriteString(text) }（合成内容没有原文对应物）。
        void WriteSynthesized(string text)
        {
            virtualText.Append(text);
        }

        // Go: writeMapped := func(text string, originalStart, originalEnd int, kind spanmap.Kind)。
        void WriteMapped(string text, int originalStart, int originalEnd, Kind kind)
        {
            var virtualStart = new TextPos(virtualText.Length);
            virtualText.Append(text);
            segments.Add(new Segment
            {
                VirtualStart = virtualStart,
                VirtualEnd = new TextPos(virtualText.Length),
                OriginalStart = new TextPos(originalStart),
                OriginalEnd = new TextPos(originalEnd),
                Kind = kind,
                Features = Feature.All,
            });
        }

        // Go: writeAnchored := func(text string, originalPosition int, features spanmap.Feature)。
        void WriteAnchored(string text, int originalPosition, Feature features)
        {
            var virtualStart = new TextPos(virtualText.Length);
            virtualText.Append(text);
            segments.Add(new Segment
            {
                VirtualStart = virtualStart,
                VirtualEnd = new TextPos(virtualText.Length),
                OriginalStart = new TextPos(originalPosition),
                OriginalEnd = new TextPos(originalPosition),
                Kind = Kind.Atom,
                Features = features,
            });
        }

        // Go: scriptOpen := strings.Index(content, "<script")。
        var scriptOpen = content.IndexOf("<script", StringComparison.Ordinal);
        if (scriptOpen >= 0)
        {
            // Go: openEndRel := strings.IndexByte(content[scriptOpen:], '>')（相对下标）→ C# 用从 scriptOpen
            // 起的绝对下标，`scriptStart` 的算式等价。
            var openEnd = content.IndexOf('>', scriptOpen);
            if (openEnd < 0)
            {
                throw new Exception("contentmappertest: unclosed <script> tag");
            }

            var scriptStart = openEnd + 1;
            // Go: closeRel := strings.Index(content[scriptStart:], "</script>")（相对下标）→ C# 的
            // IndexOf(startIndex) 返回绝对下标，`scriptEnd` 就是它。
            var scriptEnd = content.IndexOf("</script>", scriptStart, StringComparison.Ordinal);
            if (scriptEnd < 0)
            {
                throw new Exception("contentmappertest: missing </script> tag");
            }

            WriteMapped(content[scriptStart..scriptEnd], scriptStart, scriptEnd, Kind.Verbatim);
        }

        WriteSynthesized("\nfunction __render() {\n");
        for (var searchStart = 0; searchStart < content.Length;)
        {
            var openRel = content.IndexOf("{{", searchStart, StringComparison.Ordinal);
            if (openRel < 0)
            {
                break;
            }

            var exprStart = openRel + "{{".Length;
            var exprEnd = content.IndexOf("}}", exprStart, StringComparison.Ordinal);
            if (exprEnd < 0)
            {
                throw new Exception("contentmappertest: unclosed template expression");
            }

            WriteSynthesized("  void (");
            for (var pos = exprStart; pos < exprEnd;)
            {
                if (!IsIdentifierStart(content[pos]))
                {
                    WriteSynthesized(content.Substring(pos, 1));
                    pos++;
                    continue;
                }

                var end = pos + 1;
                while (end < exprEnd && IsIdentifierPart(content[end]))
                {
                    end++;
                }

                WriteMapped(content[pos..end], pos, end, Kind.Atom);
                pos = end;
            }

            WriteSynthesized(");\n");
            searchStart = exprEnd + "}}".Length;
        }

        WriteSynthesized("}\n");
        if (TryComponentNameRange(content, out var nameStart, out var nameEnd))
        {
            WriteSynthesized("export class ");
            WriteMapped(content[nameStart..nameEnd], nameStart, nameEnd, Kind.Atom);
            WriteSynthesized(" {}\n");
        }

        WriteAnchored("export default {};\n", 0, Feature.Definition | Feature.References);

        // Go: `mappings, err := spanmap.New(segments).Marshal(); if err != nil { return "", nil, err }`——
        // C# 的 Marshal() 失败直接抛异常，没有错误分支。
        var mappings = SpanMap.SpanMap.New([.. segments]).Marshal();
        return (virtualText.ToString(), mappings);
    }

    /// <summary>Go: <c>func componentNameRange(content string) (start, end int, ok bool)</c>。</summary>
    public static bool TryComponentNameRange(string content, out int start, out int end)
    {
        start = 0;
        end = 0;

        // Go: componentStart := strings.Index(content, "<component")。
        var componentStart = content.IndexOf("<component", StringComparison.Ordinal);
        if (componentStart < 0)
        {
            return false;
        }

        // Go: tagEndRel := strings.IndexByte(content[componentStart:], '>')（相对下标）→ C# 用绝对下标切 tag。
        var tagEnd = content.IndexOf('>', componentStart);
        if (tagEnd < 0)
        {
            return false;
        }

        var tag = content[componentStart..tagEnd];
        var nameRel = tag.IndexOf("name=\"", StringComparison.Ordinal);
        if (nameRel < 0)
        {
            return false;
        }

        start = componentStart + nameRel + "name=\"".Length;
        // Go: endRel := strings.IndexByte(content[start:], '"')（相对下标）→ C# 的 IndexOf(start) 是绝对下标。
        end = content.IndexOf('"', start);
        if (end < 0)
        {
            return false;
        }

        return true;
    }

    /// <summary>Go: <c>func isIdentifierStart(ch byte) bool</c>（UTF-8 单字节判定 → C# 的 char 判定）。</summary>
    public static bool IsIdentifierStart(char ch) =>
        ch == '_' || ch == '$' || ch >= 'A' && ch <= 'Z' || ch >= 'a' && ch <= 'z';

    /// <summary>Go: <c>func isIdentifierPart(ch byte) bool</c>。</summary>
    public static bool IsIdentifierPart(char ch) => IsIdentifierStart(ch) || ch >= '0' && ch <= '9';
}
