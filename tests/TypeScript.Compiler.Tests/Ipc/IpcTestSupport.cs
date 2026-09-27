// 测试辅助：Go 的 `errors.Is`/`errors.As` 在 C# 侧没有对应物（错误值是异常实例，见 Ipc/Conn.cs 文件头）。
// Go 测试里的 `assert.Assert(t, errors.Is(err, target))` 在这里写成沿 InnerException 链找**同一个**实例。
using Xunit;

namespace TypeScript.Tests;

internal static class IpcTestSupport
{
    /// <summary>Go 的 <c>errors.Is(err, cause)</c>：错误包装成异常后，原始异常在 InnerException 链上。</summary>
    internal static void IsCausedBy(Exception error, Exception cause)
    {
        for (Exception? current = error; current is not null; current = current.InnerException)
        {
            if (ReferenceEquals(current, cause))
            {
                return;
            }
        }

        Assert.Fail($"expected the cause chain of \"{error.Message}\" to contain \"{cause.Message}\"");
    }
}
