// ported from: internal/testutil/contentmappertest/synthesizing.go
//
// C# 映射与偏离（逐条）：
// 1) `synthesizingHandler`（未导出类型）→ `SynthesizingHandler`：Go 内嵌的 `noNotifications` → C# 继承
//    `NoNotifications` + 自己的 `, Ipc.Handler`（HandleRequest）。
// 2) `const synthesizedOutput`（未导出包级常量）→ 同名静态类 `ContentMapperTest` 上的 `internal const`
//    （Go 未导出 → C# internal；`SynthesizingHandler` 是另一个类型，private 就取不到了）。
// 3) `spanmap.New(nil)` → `SpanMap.SpanMap.New(null)`（nil 切片在 C# 里显式写 null，与 Go 一致：空 SpanMap =
//    "虚拟文本全是合成的"）。
using TypeScript.ContentMapper;
using TypeScript.Core;
using TypeScript.Json;
using TypeScript.SpanMap;
using static TypeScript.ContentMapper.ContentMapper;
using static TypeScript.Json.Json;

using static TypeScript.TestUtil.ContentMapperTest.ContentMapperTest;

namespace TypeScript.TestUtil.ContentMapperTest;

public static partial class ContentMapperTest
{
    /// <summary>Go: <c>const synthesizedOutput = "export const el = jsxRuntime(Widget);\n"</c>（未导出 → internal）。</summary>
    internal const string SynthesizedOutput = "export const el = jsxRuntime(Widget);\n";
}

/// <summary>
/// Go: <c>type synthesizingHandler struct{ noNotifications }</c>。输出完全是合成的（空 span map）：虚拟文本
/// 里每个位置都没有原文对应物。
/// </summary>
public class SynthesizingHandler : NoNotifications, Ipc.Handler
{
    /// <summary>Go: <c>func (synthesizingHandler) HandleRequest(ctx, method, params) (any, error)</c>。</summary>
    public object? HandleRequest(Context ctx, string method, byte[]? @params)
    {
        switch (method)
        {
            case MethodInitialize:
                return InitializeResult("mapper");

            case MethodTransform:
            {
                // Go: `var p contentmapper.TransformParams; json.Unmarshal(params, &p)`（解出来但不用）。
                _ = UnmarshalNew<TransformParams>(@params ?? []);
                var mappings = SpanMap.SpanMap.New(null).Marshal();
                return new TransformResult
                {
                    Text = SynthesizedOutput,
                    Extension = ".ts",
                    Mappings = mappings,
                };
            }

            default:
                throw new Exception($"contentmappertest: unexpected method \"{method}\"");
        }
    }
}
