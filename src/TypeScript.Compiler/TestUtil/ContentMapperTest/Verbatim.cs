// ported from: internal/testutil/contentmappertest/verbatim.go
//
// C# 映射与偏离（逐条）：
// 1) `verbatimHandler`/`moduleVerbatimHandler`（未导出类型）→ `VerbatimHandler`/`ModuleVerbatimHandler`；
//    Go 的内嵌 `noNotifications` → C# 继承 `NoNotifications`，并各自声明 `, Ipc.Handler` 提供 HandleRequest
//    （Go 里这两个类型都是值类型，C# 侧是引用类型——它们没有值语义，不受影响）。
// 2) `verbatimHandler.HandleRequest` 是 `virtual`：`dynamicVerbatimHandler`（sibling 的 dynamic_verbatim.go）
//    内嵌它并在 default 分支转发 `h.verbatimHandler.HandleRequest(...)` → C# 的 `override` + `base.…`。
// 3) Go 的 `TransformResult{MappedOutput: mappedOutput}`（内嵌赋值 = 逐字段复制）→ C# 显式复制 MappedOutput
//    的 4 个字段（C# 的继承不能整体赋值基类部分）。
using TypeScript.ContentMapper;
using TypeScript.Core;
using TypeScript.Json;
using static TypeScript.ContentMapper.ContentMapper;
using static TypeScript.Json.Json;

using static TypeScript.TestUtil.ContentMapperTest.ContentMapperTest;

namespace TypeScript.TestUtil.ContentMapperTest;

/// <summary>Go: <c>type verbatimHandler struct{ noNotifications }</c>（原样返回输入文本的 mapper）。</summary>
public class VerbatimHandler : NoNotifications, Ipc.Handler
{
    /// <summary>Go: <c>func (verbatimHandler) HandleRequest(ctx, method, params) (any, error)</c>。</summary>
    public virtual object? HandleRequest(Context ctx, string method, byte[]? @params)
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
                    Text = mappedOutput.Text,
                    Extension = mappedOutput.Extension,
                    Mappings = mappedOutput.Mappings,
                    DiagnosticDirectives = mappedOutput.DiagnosticDirectives,
                };
            }

            default:
                throw new Exception($"contentmappertest: unexpected method \"{method}\"");
        }
    }
}

/// <summary>Go: <c>type moduleVerbatimHandler struct{ noNotifications }</c>（同上，但虚拟扩展名是 .mts）。</summary>
public class ModuleVerbatimHandler : NoNotifications, Ipc.Handler
{
    /// <summary>Go: <c>func (moduleVerbatimHandler) HandleRequest(ctx, method, params) (any, error)</c>。</summary>
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
                mappedOutput.Extension = ".mts";
                return new TransformResult
                {
                    Text = mappedOutput.Text,
                    Extension = mappedOutput.Extension,
                    Mappings = mappedOutput.Mappings,
                    DiagnosticDirectives = mappedOutput.DiagnosticDirectives,
                };
            }

            default:
                throw new Exception($"contentmappertest: unexpected method \"{method}\"");
        }
    }
}
