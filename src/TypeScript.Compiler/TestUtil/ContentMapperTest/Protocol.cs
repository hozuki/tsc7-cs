// ported from: internal/testutil/contentmappertest/protocol.go
//
// C# 映射与偏离（逐条）：
// 1) `noNotifications`（未导出类型）→ `NoNotifications`：与 Go 一样**只**提供 HandleNotification，因此它本身
//    不实现 ipc.Handler（Go 的 noNotifications 也不满足该接口）；每个具体 handler 用
//    `: NoNotifications, Ipc.Handler` 补齐自己的 HandleRequest（对应 Go 的内嵌 + 各自实现 HandleRequest）。
// 2) `initializeResult`/`identityMappedOutput`（未导出包级函数）→ 同名静态类 `ContentMapperTest` 上的 PascalCase
//    静态方法（Go 的包级函数统一收进与包同名的静态类，见 architecture.md §2）。
// 3) `identityMappedOutput` 的 `(MappedOutput, error)` → 正常返回 MappedOutput、失败抛异常
//    （`SpanMap.Marshal` 自己抛；见 SpanMap/SpanMap.cs 文件头第 5 条）。
// 4) `staticProjectHandler`（未导出类型）→ `StaticProjectHandler`：Go 内嵌的 `ipc.Handler` 字段 → C# 的公开
//    字段 `Handler` + 构造器（对应 Go 的复合字面量 `staticProjectHandler{Handler: h}`）；内嵌提升的
//    HandleNotification 显式转发给内部 handler。
// 5) `projectLifecycleHandler`（未导出接口）→ `IProjectLifecycleHandler`；`OpenProject` 在 Go 里返回 error
//    → C# 抛异常，`CloseProject` 在 Go 里没有返回值，保持一致。
// 6) Go 的 `json.Unmarshal(params, &p)` → `UnmarshalNew<T>(@params ?? [])`（本项目 Json 的静态分派；
//    nil/空输入与 Go 一样抛 JsonException）。
// 7) Go 的 `string(p.Options)`（json.Value → string）→ `Encoding.UTF8.GetString`；nil → ""。
using System.Text;
using TypeScript.Core;
using TypeScript.ContentMapper;
using TypeScript.Json;
using TypeScript.SpanMap;
using static TypeScript.ContentMapper.ContentMapper;
using static TypeScript.Json.Json;

namespace TypeScript.TestUtil.ContentMapperTest;

/// <summary>
/// Go: <c>type noNotifications struct{}</c>（未导出 → PascalCase）。不需要处理通知的 handler 内嵌它。
/// **注意**：它不实现 ipc.Handler（与 Go 同），需要它的类型自己声明 `, Ipc.Handler` 并提供 HandleRequest。
/// </summary>
public class NoNotifications
{
    /// <summary>Go: <c>func (noNotifications) HandleNotification(ctx, method, params) error</c>（永远返回 nil）。</summary>
    public void HandleNotification(Context ctx, string method, byte[]? @params)
    {
    }
}

/// <summary>
/// Go: <c>type projectLifecycleHandler interface</c>（未导出 → I 前缀 + PascalCase）。mapper 的 project
/// 协议回调：记录/校验 openProject / closeProject。Go 的 `OpenProject(p) error` → C# 抛异常。
/// </summary>
public interface IProjectLifecycleHandler
{
    /// <summary>Go: <c>OpenProject(params contentmapper.OpenProjectParams) error</c>。</summary>
    void OpenProject(OpenProjectParams p);

    /// <summary>Go: <c>CloseProject(params contentmapper.CloseProjectParams)</c>（Go 里无返回值）。</summary>
    void CloseProject(CloseProjectParams p);
}

public static partial class ContentMapperTest
{
    /// <summary>
    /// Go: <c>func initializeResult(source string) contentmapper.InitializeResult</c>。
    /// <para>
    /// **偏离**：Go 的 <c>PositionEncoding: contentmapper.PositionEncodingUTF8</c> 之所以成立，是因为 Go 的
    /// string 是 UTF-8 字节序列、`len(content)`/`strings.Index` 给出的就是**字节**偏移，与宿主内部坐标一致
    /// （Go 侧 <c>positionNormalizer.normalize</c> 对 utf-8 是恒等映射）。本移植的宿主内部坐标是 **UTF-16
    /// 代码单元**（docs/architecture.md §3.1；`PositionNormalizer` 的 utf-8 分支会把字节偏移**换算**到 UTF-16），
    /// 而本进程内的测试 mapper 全部用 C# 的 `string.Length`/`IndexOf` 取位置——那是 UTF-16 下标。若仍声明
    /// utf-8，宿主会把 UTF-16 下标当成字节偏移再换算一次，凡是含非 ASCII（尤其非 BMP，如 😀）的内容都会
    /// 整体错位（必现 TS18073 verbatim 映射不匹配）。所以这里按测试 mapper 的**真实坐标空间**声明 utf-16，
    /// 宿主侧即为恒等映射——与 Go 的 utf-8 情形在"归一化后编码无关"这个意义上等价（见
    /// ContentMapperHostTests.TestRunnerPositionEncodings 钉住的语义）。
    /// </para>
    /// </summary>
    public static InitializeResult InitializeResult(string source) => new()
    {
        PositionEncoding = PositionEncoding.UTF16,
        DiagnosticSource = source,
    };

    /// <summary>
    /// Go: <c>func identityMappedOutput(content string) (contentmapper.MappedOutput, error)</c>。整个输出是原文的
    /// 单段 verbatim 映射（`VirtualEnd`/`OriginalEnd` 都是内容长度、起点都是 0）。
    /// </summary>
    public static MappedOutput IdentityMappedOutput(string content) => new()
    {
        Text = content,
        Extension = ".ts",
        Mappings = SpanMap.SpanMap.New([new Segment
        {
            VirtualEnd = new TextPos(content.Length),
            OriginalEnd = new TextPos(content.Length),
            Kind = Kind.Verbatim,
            Features = Feature.All,
        }]).Marshal(),
    };
}

/// <summary>
/// Go: <c>type staticProjectHandler struct{ ipc.Handler }</c>（未导出 → PascalCase）。包住内层 handler，自己
/// 处理 openProject/closeProject（把项目生命周期事件转给实现了 <see cref="IProjectLifecycleHandler"/> 的内层
/// handler），其余请求转发。
/// </summary>
public sealed class StaticProjectHandler : Ipc.Handler
{
    /// <summary>Go 的内嵌字段 <c>ipc.Handler</c>（字段名取类型名，导出 → PascalCase）。</summary>
    public Ipc.Handler Handler;

    /// <summary>C# 侧新增：Go 的复合字面量 <c>staticProjectHandler{Handler: handler}</c>。</summary>
    public StaticProjectHandler(Ipc.Handler handler)
    {
        Handler = handler;
    }

    /// <summary>Go: <c>func (h staticProjectHandler) HandleRequest(ctx, method, params) (any, error)</c>。</summary>
    public object? HandleRequest(Context ctx, string method, byte[]? @params)
    {
        switch (method)
        {
            case MethodOpenProject:
            {
                var p = UnmarshalNew<OpenProjectParams>(@params ?? []);
                // Go: `if handler, ok := h.Handler.(projectLifecycleHandler); ok { … }`。
                if (Handler is IProjectLifecycleHandler lifecycle)
                {
                    lifecycle.OpenProject(p);
                }

                // Go: `if string(p.Options) == `{"plugins":[{"name":1}]}` { … }`。
                const string invalidPlugins = "{\"plugins\":[{\"name\":1}]}";
                OptionDiagnosticResult[]? diagnostics = null;
                if ((p.Options is null ? "" : Encoding.UTF8.GetString(p.Options)) == invalidPlugins)
                {
                    diagnostics =
                    [
                        new OptionDiagnosticResult
                        {
                            Path = ["\"plugins\""u8.ToArray(), "0"u8.ToArray(), "\"name\""u8.ToArray()],
                            MessageText = "Option 'name' requires a string.",
                            Code = 123,
                        },
                    ];
                }

                return new OpenProjectResult { OptionDiagnostics = diagnostics };
            }

            case MethodCloseProject:
            {
                var p = UnmarshalNew<CloseProjectParams>(@params ?? []);
                if (Handler is IProjectLifecycleHandler lifecycle)
                {
                    lifecycle.CloseProject(p);
                }

                return null;
            }

            default:
                return Handler.HandleRequest(ctx, method, @params);
        }
    }

    /// <summary>Go: 内嵌提升的 <c>ipc.Handler.HandleNotification</c>。</summary>
    public void HandleNotification(Context ctx, string method, byte[]? @params) =>
        Handler.HandleNotification(ctx, method, @params);
}
