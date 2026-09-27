// ported from: internal/testutil/contentmappertest/diagnostic_code_collision.go
//
// 与 Go 的差异（逐条）：
// 1) Go 的 `diagnosticCodeCollisionHandler struct{ noNotifications }`（内嵌只为提升 HandleNotification）→
//    C# 继承 `NoNotifications`（同包 Protocol.cs 提供）。
// 2) 未导出类型改名：`diagnosticCodeCollisionHandler` → `DiagnosticCodeCollisionHandler`。
// 3) `(T, error)` → 正常返回 T、失败抛异常；`json.Unmarshal` 失败 → JsonException；params 为空 → `?? throw`。
// 4) Go 的 `TransformResult{MappedOutput: mappedOutput, …}`（内嵌结构体赋值）→ C# 逐字段复制（内嵌 → 继承的
//    映射见 ContentMapper/HostImpl.cs 文件头）。
// 5) Go 的消息常量 `diagnostics.X` → 全限定 `TypeScript.Diagnostics.Diagnostics.X`（裸写 `Diagnostics` 会先
//    解析到 `TransformResult.Diagnostics` 所在的名字空间，见 Transformers/Declarations/Transform.Exports.cs
//    文件头第 8 条）。
using TypeScript.ContentMapper;
using TypeScript.Core;
using TypeScript.Ipc;
using TypeScript.Json;
using static TypeScript.ContentMapper.ContentMapper;
using static TypeScript.Ipc.Ipc;

namespace TypeScript.TestUtil.ContentMapperTest;

/// <summary>Go: <c>type diagnosticCodeCollisionHandler struct{ noNotifications }</c>（诊断码与编译器诊断冲突的映射器）。</summary>
public class DiagnosticCodeCollisionHandler : NoNotifications, Ipc.Handler
{
    /// <summary>Go: <c>func (diagnosticCodeCollisionHandler) HandleRequest(ctx, method, params) (any, error)</c>。</summary>
    public object? HandleRequest(Context ctx, string method, byte[]? @params)
    {
        switch (method)
        {
            case MethodInitialize:
                return ContentMapperTest.InitializeResult("mapper");
            case MethodTransform:
                var p = UnmarshalParams<TransformParams>(@params)
                    ?? throw new JsonException("contentmappertest: missing transform parameters");
                var mappedOutput = ContentMapperTest.IdentityMappedOutput(p.Content);
                // Go: start := strings.Index(p.Content, "foo")。
                var start = p.Content.IndexOf("foo", StringComparison.Ordinal);
                return new TransformResult
                {
                    Text = mappedOutput.Text,
                    Extension = mappedOutput.Extension,
                    Mappings = mappedOutput.Mappings,
                    DiagnosticDirectives = mappedOutput.DiagnosticDirectives,
                    Diagnostics =
                    [
                        new Diagnostic
                        {
                            MessageText = "Mapper diagnostic with a colliding code.",
                            Start = start,
                            Length = "foo".Length,
                            Code = TypeScript.Diagnostics.Diagnostics.Function_must_have_an_explicit_return_type_annotation_with_isolatedDeclarations.Code(),
                        },
                    ],
                };
            default:
                throw new Exception($"contentmappertest: unexpected method \"{method}\"");
        }
    }
}
