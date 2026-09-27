// ported from: internal/testutil/contentmappertest/supplemental_module.go
//
// C# 映射与偏离（逐条）：
// 1) `supplementalModuleHandler`（未导出类型）→ `SupplementalModuleHandler`：Go 内嵌的 `noNotifications`
//    → C# 继承 `NoNotifications` + 自己的 `, Ipc.Handler`（HandleRequest）——这个 handler 完全不用参数
//    （连 p.Content 都不读），仅造出固定的规范输出 + 一个会报错的补充输入。
using TypeScript.ContentMapper;
using TypeScript.Core;
using TypeScript.Json;
using static TypeScript.ContentMapper.ContentMapper;
using static TypeScript.Json.Json;

using static TypeScript.TestUtil.ContentMapperTest.ContentMapperTest;

namespace TypeScript.TestUtil.ContentMapperTest;

/// <summary>Go: <c>type supplementalModuleHandler struct{ noNotifications }</c>。</summary>
public class SupplementalModuleHandler : NoNotifications, Ipc.Handler
{
    /// <summary>Go: <c>func (supplementalModuleHandler) HandleRequest(ctx, method, params) (any, error)</c>。</summary>
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
                return new TransformResult
                {
                    Text = "export default 1;",
                    Extension = ".ts",
                    Supplemental =
                    [
                        new SupplementalOutput { Text = "export const privateValue: number = \"wrong\";", Extension = ".ts" },
                    ],
                };
            }

            default:
                throw new Exception($"contentmappertest: unexpected method \"{method}\"");
        }
    }
}
