// ported from: internal/testutil/contentmappertest/supplemental_diagnostics.go
//
// C# 映射与偏离（逐条）：
// 1) `supplementalDiagnosticsHandler`（未导出类型）→ `SupplementalDiagnosticsHandler`：Go 内嵌的
//    `noNotifications` → C# 继承 `NoNotifications` + 自己的 `, Ipc.Handler`（HandleRequest）。
// 2) `const prefix = "missingSupplementalGlobal;\n"` 在 Go 里是函数内的常量 → C# 的局部 `const`，同名同值。
using TypeScript.ContentMapper;
using TypeScript.Core;
using TypeScript.Json;
using TypeScript.SpanMap;
using static TypeScript.ContentMapper.ContentMapper;
using static TypeScript.Json.Json;

using static TypeScript.TestUtil.ContentMapperTest.ContentMapperTest;

namespace TypeScript.TestUtil.ContentMapperTest;

/// <summary>
/// Go: <c>type supplementalDiagnosticsHandler struct{ noNotifications }</c>。补充输入比原文多一个前缀
/// （映射段的 VirtualStart 因此偏移），用来测补充文件里报错的诊断映射。
/// </summary>
public class SupplementalDiagnosticsHandler : NoNotifications, Ipc.Handler
{
    /// <summary>Go: <c>func (supplementalDiagnosticsHandler) HandleRequest(ctx, method, params) (any, error)</c>。</summary>
    public object? HandleRequest(Context ctx, string method, byte[]? @params)
    {
        switch (method)
        {
            case MethodInitialize:
                return InitializeResult("mapper");

            case MethodTransform:
            {
                var p = UnmarshalNew<TransformParams>(@params ?? []);
                const string prefix = "missingSupplementalGlobal;\n";
                var mappings = SpanMap.SpanMap.New([new Segment
                {
                    VirtualStart = new TextPos(prefix.Length),
                    VirtualEnd = new TextPos(prefix.Length + p.Content.Length),
                    OriginalEnd = new TextPos(p.Content.Length),
                    Kind = Kind.Verbatim,
                    Features = Feature.All,
                }]).Marshal();
                return new TransformResult
                {
                    Text = "export {};",
                    Extension = ".ts",
                    Supplemental =
                    [
                        new SupplementalOutput { Text = prefix + p.Content, Extension = ".ts", Mappings = mappings },
                    ],
                };
            }

            default:
                throw new Exception($"contentmappertest: unexpected method \"{method}\"");
        }
    }
}
