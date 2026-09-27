// ported from: internal/testutil/contentmappertest/supplemental_globals.go
//
// C# 映射与偏离（逐条）：
// 1) `supplementalGlobalsHandler`（未导出类型）→ `SupplementalGlobalsHandler`：Go 内嵌的 `noNotifications`
//    → C# 继承 `NoNotifications` + 自己的 `, Ipc.Handler`（HandleRequest）。
// 2) Go 的
//    `switch { case strings.HasSuffix(p.FileName, "/a.vue"): … case strings.HasSuffix(p.FileName, "/b.vue"): … }`
//    （带 default 分支且 default 返回错误）→ 等价的 if/else 链（两个后缀互斥，语义相同）；
//    `strings.HasSuffix` → `string.EndsWith(…, StringComparison.Ordinal)`。
using TypeScript.ContentMapper;
using TypeScript.Core;
using TypeScript.Json;
using static TypeScript.ContentMapper.ContentMapper;
using static TypeScript.Json.Json;

using static TypeScript.TestUtil.ContentMapperTest.ContentMapperTest;

namespace TypeScript.TestUtil.ContentMapperTest;

/// <summary>
/// Go: <c>type supplementalGlobalsHandler struct{ noNotifications }</c>。按输入文件名造出互相引用的补充输入：
/// a.vue 的补充输入声明全局类型（还带一条 reference 指令），b.vue 的补充输入消费它。
/// </summary>
public class SupplementalGlobalsHandler : NoNotifications, Ipc.Handler
{
    /// <summary>Go: <c>func (supplementalGlobalsHandler) HandleRequest(ctx, method, params) (any, error)</c>。</summary>
    public object? HandleRequest(Context ctx, string method, byte[]? @params)
    {
        switch (method)
        {
            case MethodInitialize:
                return InitializeResult("mapper");

            case MethodTransform:
            {
                var p = UnmarshalNew<TransformParams>(@params ?? []);
                string supplemental;
                if (p.FileName.EndsWith("/a.vue", StringComparison.Ordinal))
                {
                    supplemental = "/// <reference path=\"./extra.d.ts\" />\ninterface Shared extends Extra { value: string }";
                }
                else if (p.FileName.EndsWith("/b.vue", StringComparison.Ordinal))
                {
                    supplemental = "declare const shared: Shared;";
                }
                else
                {
                    throw new Exception($"contentmappertest: unexpected supplemental global input \"{p.FileName}\"");
                }

                return new TransformResult
                {
                    Text = "export default shared.value;",
                    Extension = ".ts",
                    Supplemental = [new SupplementalOutput { Text = supplemental, Extension = ".ts" }],
                };
            }

            default:
                throw new Exception($"contentmappertest: unexpected method \"{method}\"");
        }
    }
}
