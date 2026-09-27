// ported from: internal/testutil/contentmappertest/supplemental.go
//
// C# 映射与偏离（逐条）：
// 1) `supplementalHandler`（未导出类型）→ `SupplementalHandler`：Go 内嵌的 `noNotifications` → C# 继承
//    `NoNotifications` + 自己的 `, Ipc.Handler`（HandleRequest）。
// 2) Go 的 `Supplemental: []SupplementalOutput{{MappedOutput: mappedOutput}}`（内嵌赋值 = 逐字段复制）→
//    C# 显式复制 MappedOutput 的 4 个字段（C# 的继承不能整体赋值基类部分）。
using TypeScript.ContentMapper;
using TypeScript.Core;
using TypeScript.Json;
using static TypeScript.ContentMapper.ContentMapper;
using static TypeScript.Json.Json;

using static TypeScript.TestUtil.ContentMapperTest.ContentMapperTest;

namespace TypeScript.TestUtil.ContentMapperTest;

/// <summary>
/// Go: <c>type supplementalHandler struct{ noNotifications }</c>。规范输出是空模块，另造一个与原文逐字节
/// 相同的补充输入（用来测"补充文件与规范文件同文本"的情形）。
/// </summary>
public class SupplementalHandler : NoNotifications, Ipc.Handler
{
    /// <summary>Go: <c>func (supplementalHandler) HandleRequest(ctx, method, params) (any, error)</c>。</summary>
    public object? HandleRequest(Context ctx, string method, byte[]? @params)
    {
        switch (method)
        {
            case MethodInitialize:
                return InitializeResult("mapper");

            case MethodTransform:
            {
                var p = UnmarshalNew<TransformParams>(@params ?? []);
                var mappedOutput = IdentityMappedOutput(p.Content);
                return new TransformResult
                {
                    Text = "export {};",
                    Extension = ".ts",
                    Supplemental =
                    [
                        new SupplementalOutput
                        {
                            Text = mappedOutput.Text,
                            Extension = mappedOutput.Extension,
                            Mappings = mappedOutput.Mappings,
                            DiagnosticDirectives = mappedOutput.DiagnosticDirectives,
                        },
                    ],
                };
            }

            default:
                throw new Exception($"contentmappertest: unexpected method \"{method}\"");
        }
    }
}
