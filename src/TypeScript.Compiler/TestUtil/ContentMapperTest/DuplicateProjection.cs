// ported from: internal/testutil/contentmappertest/duplicate_projection.go
//
// 与 Go 的差异（逐条）：
// 1) Go 的 `duplicateProjectionHandler struct{ noNotifications }`（内嵌只为提升 HandleNotification）→ C# 继承
//    `NoNotifications`（同包 Protocol.cs 提供）。
// 2) 未导出类型改名：`duplicateProjectionHandler` → `DuplicateProjectionHandler`。
// 3) `(T, error)` → 正常返回 T、失败抛异常；`json.Unmarshal` 失败 → JsonException；params 为空 → `?? throw`。
// 4) Go 的 `TransformResult{MappedOutput: canonical, …}` / `SupplementalOutput{MappedOutput: supplemental}`
//    （内嵌结构体赋值）→ C# 逐字段复制（内嵌 → 继承的映射见 ContentMapper/HostImpl.cs 文件头）。
using TypeScript.ContentMapper;
using TypeScript.Core;
using TypeScript.Ipc;
using TypeScript.Json;
using static TypeScript.ContentMapper.ContentMapper;
using static TypeScript.Ipc.Ipc;

namespace TypeScript.TestUtil.ContentMapperTest;

/// <summary>
/// Go: <c>type duplicateProjectionHandler struct{ noNotifications }</c>。把原文同时作为规范输出与补充输出发出，
/// 于是同一原文区间在两个共享 OriginalFileName 的不同虚拟源文件里各有一份完全相同的对应物——对该区间的编辑
/// 因此会按每个投影各记录一次。
/// </summary>
public class DuplicateProjectionHandler : NoNotifications, Ipc.Handler
{
    /// <summary>Go: <c>func (duplicateProjectionHandler) HandleRequest(ctx, method, params) (any, error)</c>。</summary>
    public object? HandleRequest(Context ctx, string method, byte[]? @params)
    {
        switch (method)
        {
            case MethodInitialize:
                return ContentMapperTest.InitializeResult("mapper");
            case MethodTransform:
                var p = UnmarshalParams<TransformParams>(@params)
                    ?? throw new JsonException("contentmappertest: missing transform parameters");
                var canonical = ContentMapperTest.IdentityMappedOutput(p.Content);
                var supplemental = ContentMapperTest.IdentityMappedOutput(p.Content);
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
                            Text = supplemental.Text,
                            Extension = supplemental.Extension,
                            Mappings = supplemental.Mappings,
                            DiagnosticDirectives = supplemental.DiagnosticDirectives,
                        },
                    ],
                };
            default:
                throw new Exception($"contentmappertest: unexpected method \"{method}\"");
        }
    }
}
