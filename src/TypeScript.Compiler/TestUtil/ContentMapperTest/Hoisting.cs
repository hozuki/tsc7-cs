// ported from: internal/testutil/contentmappertest/hoisting.go
//
// 与 Go 的差异（逐条）：
// 1) Go 的 `hoistingHandler struct{ noNotifications }`（内嵌只为提升 HandleNotification）→ C# 继承
//    `NoNotifications`（同包 Protocol.cs 提供）。
// 2) 未导出类型/函数改名：`hoistingHandler` → `HoistingHandler`、`scriptRange` → `ScriptRange`、
//    `leadingImportRange` → `LeadingImportRange`、`isSpaceByte` → `IsSpaceByte`。
// 3) `(string, json.Value, error)` / `(start, end int, err error)` → 具名元组 + 异常（见 Ipc/Conn.cs 文件头第 7 条）；
//    `Marshal()` 失败也抛异常，没有错误分支。
// 4) `strings.IndexByte(content[end:scriptEnd], '\n')` 是**相对**下标 → C# 保持相对下标：对切片用
//    `content.AsSpan(end, scriptEnd - end).IndexOf('\n')`（若写成 `content.IndexOf('\n', end)` 会越过 scriptEnd）。
//    同理 `strings.HasPrefix(content[end:scriptEnd], "import ")` → `AsSpan(...).StartsWith("import ")`。
// 5) `json.Value`（= 原始 JSON 字节）→ `byte[]?`；Go 的 `strings.Builder` → `System.Text.StringBuilder`（别名）。
// 6) 未导出包级函数按约定落成 `ContentMapperTest` 的 public static 成员。
using TypeScript.ContentMapper;
using TypeScript.Core;
using TypeScript.Ipc;
using TypeScript.Json;
using TypeScript.SpanMap;
using static TypeScript.ContentMapper.ContentMapper;
using static TypeScript.Ipc.Ipc;
using StringBuilder = System.Text.StringBuilder;

namespace TypeScript.TestUtil.ContentMapperTest;

/// <summary>Go: <c>type hoistingHandler struct{ noNotifications }</c>（把 import 提升到渲染函数之上的映射器）。</summary>
public class HoistingHandler : NoNotifications, Ipc.Handler
{
    /// <summary>Go: <c>func (hoistingHandler) HandleRequest(ctx, method, params) (any, error)</c>。</summary>
    public object? HandleRequest(Context ctx, string method, byte[]? @params)
    {
        switch (method)
        {
            case MethodInitialize:
                return ContentMapperTest.InitializeResult("mapper");
            case MethodTransform:
                var p = UnmarshalParams<TransformParams>(@params)
                    ?? throw new JsonException("contentmappertest: missing transform parameters");
                var (text, mappings) = ContentMapperTest.TransformHoisting(p.Content);
                return new TransformResult { Text = text, Extension = ".ts", Mappings = mappings };
            default:
                throw new Exception($"contentmappertest: unexpected method \"{method}\"");
        }
    }
}

/// <summary>Go 的 <c>contentmappertest</c> 包级函数/常量（hoisting.go 部分）。</summary>
public static partial class ContentMapperTest
{
    /// <summary>
    /// Go: <c>func transformHoisting(content string) (string, json.Value, error)</c>。把 script 体放进渲染函数，
    /// 但把开头连续的 import 声明提升到它之上，虚拟文本布局为：
    /// <code>
    /// ///&lt;reference types="svelte" /&gt;
    /// ;
    /// import { existing } from "./dep";      &lt;- 原文 [importsStart, importsEnd)
    /// function $$render() {
    /// &lt;第一个 import 之前的空白&gt;                 &lt;- 原文 [scriptStart, importsStart)
    /// &lt;最后一个 import 之后的 script 体&gt;         &lt;- 原文 [importsEnd, scriptEnd)
    /// }
    /// </code>
    /// 这与 svelte2tsx 为组件产出的布局一致。提升把 script 切成原文连续、虚拟文本不相邻的段，所以拼接点
    /// （importsStart）在原文位置恰好落在两个 verbatim 段的边界上，会投影到两个不同的精确虚拟位置。
    /// </summary>
    public static (string Text, byte[]? Mappings) TransformHoisting(string content)
    {
        var virtualText = new StringBuilder();
        var segments = new List<Segment>();

        // Go: writeMapped := func(originalStart, originalEnd int)（把原文区间逐字复制进虚拟文本）。
        void WriteMapped(int originalStart, int originalEnd)
        {
            var virtualStart = new TextPos(virtualText.Length);
            virtualText.Append(content[originalStart..originalEnd]);
            segments.Add(new Segment
            {
                VirtualStart = virtualStart,
                VirtualEnd = new TextPos(virtualText.Length),
                OriginalStart = new TextPos(originalStart),
                OriginalEnd = new TextPos(originalEnd),
                Kind = Kind.Verbatim,
                Features = Feature.All,
            });
        }

        var (scriptStart, scriptEnd) = ScriptRange(content);
        var (importsStart, importsEnd) = LeadingImportRange(content, scriptStart, scriptEnd);

        virtualText.Append("///<reference types=\"svelte\" />\n;\n");
        WriteMapped(importsStart, importsEnd);
        virtualText.Append("\nfunction $$render() {");
        WriteMapped(scriptStart, importsStart);
        WriteMapped(importsEnd, scriptEnd);
        virtualText.Append("\n;\nreturn { props: {} as Record<string, never> }}\n");

        // Go: `mappings, err := spanmap.New(segments).Marshal(); if err != nil { return "", nil, err }`——
        // C# 的 Marshal() 失败直接抛异常，没有错误分支。
        var mappings = SpanMap.SpanMap.New([.. segments]).Marshal();
        return (virtualText.ToString(), mappings);
    }

    /// <summary>Go: <c>func scriptRange(content string) (start, end int, err error)</c>。</summary>
    public static (int Start, int End) ScriptRange(string content)
    {
        var scriptOpen = content.IndexOf("<script", StringComparison.Ordinal);
        if (scriptOpen < 0)
        {
            throw new Exception("contentmappertest: missing <script> tag");
        }

        // Go: openEndRel := strings.IndexByte(content[scriptOpen:], '>')（相对下标）→ C# 用绝对下标，算式等价。
        var openEnd = content.IndexOf('>', scriptOpen);
        if (openEnd < 0)
        {
            throw new Exception("contentmappertest: unclosed <script> tag");
        }

        var start = openEnd + 1;
        // Go: closeRel := strings.Index(content[start:], "</script>")（相对下标）→ C# 的 IndexOf(start) 返回绝对下标。
        var end = content.IndexOf("</script>", start, StringComparison.Ordinal);
        if (end < 0)
        {
            throw new Exception("contentmappertest: missing </script> tag");
        }

        return (start, end);
    }

    /// <summary>
    /// Go: <c>func leadingImportRange(content string, scriptStart, scriptEnd int) (start, end int)</c>。返回 script 体
    /// 开头连续 import 声明所覆盖的区间，跳过它们之前的空白。
    /// </summary>
    public static (int Start, int End) LeadingImportRange(string content, int scriptStart, int scriptEnd)
    {
        var start = scriptStart;
        while (start < scriptEnd && IsSpaceByte(content[start]))
        {
            start++;
        }

        var end = start;
        while (end < scriptEnd && content.AsSpan(end, scriptEnd - end).StartsWith("import "))
        {
            var lineEnd = content.AsSpan(end, scriptEnd - end).IndexOf('\n');
            if (lineEnd < 0)
            {
                end = scriptEnd;
                break;
            }

            end += lineEnd;
            while (end < scriptEnd && IsSpaceByte(content[end]))
            {
                end++;
            }
        }

        while (end > start && IsSpaceByte(content[end - 1]))
        {
            end--;
        }

        return (start, end);
    }

    /// <summary>Go: <c>func isSpaceByte(ch byte) bool</c>（UTF-8 单字节判定 → C# 的 char 判定）。</summary>
    public static bool IsSpaceByte(char ch) => ch == ' ' || ch == '\t' || ch == '\r' || ch == '\n';
}
