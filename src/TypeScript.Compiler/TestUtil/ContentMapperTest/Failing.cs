// ported from: internal/testutil/contentmappertest/failing.go
//
// 与 Go 的差异（逐条）：
// 1) Go 的 `failingHandler struct{ noNotifications }`（内嵌只为提升 HandleNotification）→ C# 继承
//    `NoNotifications`（同包 Protocol.cs 提供）。
// 2) 未导出类型改名：`failingHandler` → `FailingHandler`。
// 3) `(any, error)` → 返回 `object?`；返回 error → 抛异常（见 Ipc/Conn.cs 文件头第 7 条）。
using TypeScript.ContentMapper;
using TypeScript.Core;
using TypeScript.Ipc;
using static TypeScript.ContentMapper.ContentMapper;

namespace TypeScript.TestUtil.ContentMapperTest;

/// <summary>Go: <c>type failingHandler struct{ noNotifications }</c>（transform 必定失败的映射器）。</summary>
public class FailingHandler : NoNotifications, Ipc.Handler
{
    /// <summary>Go: <c>func (failingHandler) HandleRequest(ctx, method, params) (any, error)</c>。</summary>
    public object? HandleRequest(Context ctx, string method, byte[]? @params)
    {
        switch (method)
        {
            case MethodInitialize:
                return ContentMapperTest.InitializeResult("mapper");
            case MethodTransform:
                throw new Exception("content mapper failed to transform the file");
            default:
                throw new Exception($"contentmappertest: unexpected method \"{method}\"");
        }
    }
}
